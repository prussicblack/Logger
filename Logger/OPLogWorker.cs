using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace LogUtil
{
    internal readonly struct LogEnvelope
    {
        public LogEnvelope(DateTime utc, DateTime local, LogLevel level, string category, EventId eventId, string message, Exception? exception, IReadOnlyDictionary<string, object?>? props)
        {
            Utc = utc;
            Local = local;
            Level = level;
            if (category == null)
            {
                Category = "";
            }
            else
            {
                Category = category;
            }

            EventId = eventId;
            if (message == null)
            {
                Message = "";
            }
            else
            {
                Message = message;
            }

            Exception = exception;
            Props = props;
        }

        public DateTime Utc { get; }
        public DateTime Local { get; }
        public LogLevel Level { get; }
        public string Category { get; }
        public EventId EventId { get; }
        public string Message { get; }
        public Exception? Exception { get; }
        public IReadOnlyDictionary<string, object?>? Props { get; }
    }

    internal enum OPLogWorkItemKind
    {
        Log,
        Sensor
    }

    internal readonly struct OPLogWorkItem
    {
        private OPLogWorkItem(OPLogWorkItemKind kind, LogEnvelope log, SensorLogEnvelope sensor)
        {
            Kind = kind;
            Log = log;
            Sensor = sensor;
        }

        public OPLogWorkItemKind Kind { get; }
        public LogEnvelope Log { get; }
        public SensorLogEnvelope Sensor { get; }

        public static OPLogWorkItem FromLog(in LogEnvelope envelope) =>
            new OPLogWorkItem(OPLogWorkItemKind.Log, envelope, default);

        public static OPLogWorkItem FromSensor(in SensorLogEnvelope envelope) =>
            new OPLogWorkItem(OPLogWorkItemKind.Sensor, default, envelope);
    }

    internal sealed class OPLogWorker : IDisposable
    {
        //실제 로그관련 동작 스레드.
        private readonly OPLoggerOptions _opt;

        private readonly object _queueLock = new object();
        private readonly Queue<OPLogWorkItem> _queue;
        private readonly int _queueCapacity;
        private readonly ManualResetEventSlim _stopSignal = new ManualResetEventSlim(false);
        private readonly Thread _thread;
        private volatile bool _accepting = true;
        private volatile bool _stopping;
        private int _disposed;

        private readonly object _uiLock = new object();
        private IOPLogUiSink _uiSink;

        private Logger? _fileLogger;
        private string _currentFilePath = "";
        private volatile bool _fileEnabled = true;

        private Logger? _sensorLogger;
        private string _sensorCurrentFilePath = "";

        private int _consecutiveFileFailures;
        private int _failoverSeq;
        private int _fileUnavailableNotified;

        // stats (ticks + Interlocked)
        private long _enqueued;
        private long _written;
        private long _dropped;
        private int _maxDepth;
        private long _lastEnqueueUtcTicks;
        private long _lastWriteUtcTicks;
        private long _workerFaults;
        private long _lastWorkerFaultUtcTicks;

        private long _nextTrimUtcTicks;
        private long _nextSizeCheckUtcTicks;

        public OPLogWorker(OPLoggerOptions opt, IOPLogUiSink? uiSink)
        {
            if (opt == null)
            {
                throw new ArgumentNullException(nameof(opt));
            }

            _opt = opt;

            if (string.IsNullOrWhiteSpace(_opt.LogFolder))
            {
                _opt.LogFolder = Path.Combine(AppContext.BaseDirectory, "Logs");
            }
            else
            {
                _opt.LogFolder = Path.Combine(AppContext.BaseDirectory, _opt.LogFolder);
            }

            if (uiSink == null)
            {
                _uiSink = NullUiSink.Instance;
            }
            else
            {
                _uiSink = uiSink;
            }


            if (_opt.QueueCapacity > 0)
            {
                _queueCapacity = _opt.QueueCapacity;
            }
            else
            {
                _queueCapacity = 1024;
            }

            _queue = new Queue<OPLogWorkItem>(_queueCapacity);

            _currentFilePath = BuildNewLogFilePath(_opt.FileNamePrefix);
            _fileEnabled = TryCreateFileLogger(_currentFilePath);

            if (_opt.SensorCsv.Enabled)
            {
                _sensorCurrentFilePath = BuildNewSensorFilePath();
            }

            _thread = new Thread(ThreadMain);
            _thread.IsBackground = true;
            _thread.Name = "OPLogWorker";
            _thread.Priority = ThreadPriority.Normal;
            _thread.Start();
        }

        public void SetUiSink(IOPLogUiSink? uiSink)
        {
            lock (_uiLock)
            {
                if (uiSink == null)
                {
                    _uiSink = NullUiSink.Instance;
                }
                else
                {
                    _uiSink = uiSink;
                }
            }
        }

        public void Enqueue(in LogEnvelope e)
        {
            TryEnqueue(OPLogWorkItem.FromLog(e));
        }

        public bool EnqueueSensor(in SensorLogEnvelope e)
        {
            SensorCsvOptions options = _opt.SensorCsv;
            string[] columns;
            if (options.Columns == null)
            {
                columns = Array.Empty<string>();
            }
            else
            {
                columns = options.Columns;
            }

            if (!options.Enabled || columns.Length != e.Values.Length)
            {
                Interlocked.Increment(ref _dropped);
                return false;
            }

            return TryEnqueue(OPLogWorkItem.FromSensor(e));
        }

        private bool TryEnqueue(in OPLogWorkItem item)
        {
            lock (_queueLock)
            {
                if (!_accepting || _stopping)
                {
                    Interlocked.Increment(ref _dropped);
                    return false;
                }

                if (_queue.Count >= _queueCapacity)
                {
                    if (_opt.QueueMode == OPLogQueueMode.DropNewest)
                    {
                        Interlocked.Increment(ref _dropped);
                        return false;
                    }

                    _queue.Dequeue(); // 기본값: DropOldest
                    Interlocked.Increment(ref _dropped);
                }

                _queue.Enqueue(item);
                Interlocked.Increment(ref _enqueued);
                Interlocked.Exchange(ref _lastEnqueueUtcTicks, DateTime.UtcNow.Ticks);

                int d = _queue.Count;
                if (d > _maxDepth)
                {
                    _maxDepth = d;
                }

                Monitor.Pulse(_queueLock);
                return true;
            }
        }

        private void ThreadMain()
        {
            try
            {
                while (!_stopping)
                {
                    try
                    {
                        if (!TryTake(out OPLogWorkItem item))
                        {
                            break;
                        }

                        try
                        {
                            ProcessOne(item);
                        }
                        catch (Exception ex)
                        {
                            RecordWorkerFault(ex);
                            Interlocked.Increment(ref _dropped);
                        }
                    }
                    catch (Exception ex)
                    {
                        RecordWorkerFault(ex);
                        if (_stopping)
                        {
                            break;
                        }

                        _stopSignal.Wait(TimeSpan.FromMilliseconds(100));
                    }
                }
            }
            finally
            {
                try { if (_fileLogger != null) { _fileLogger.Dispose(); } } catch { }
                _fileLogger = null;
                _fileEnabled = false;

                try
                {
                    if (_sensorLogger != null)
                    {
                        _sensorLogger.Dispose();
                    }
                }
                catch { }
                _sensorLogger = null;
            }
        }

        private bool TryTake(out OPLogWorkItem item)
        {
            lock (_queueLock)
            {
                while (_queue.Count == 0 && _accepting && !_stopping)
                {
                    Monitor.Wait(_queueLock);
                }

                if (_stopping || (_queue.Count == 0 && !_accepting))
                {
                    item = default;
                    return false;
                }

                item = _queue.Dequeue();
                return true;
            }
        }

        private void RecordWorkerFault(Exception ex)
        {
            Interlocked.Increment(ref _workerFaults);
            Interlocked.Exchange(ref _lastWorkerFaultUtcTicks, DateTime.UtcNow.Ticks);

            try { Trace.WriteLine("[OPLogger] Worker fault: " + ex); } catch { }
            SafeUi("[LOG] Worker fault; continuing: " + ex.Message);
        }

        private void ProcessOne(in OPLogWorkItem item)
        {
            if (item.Kind == OPLogWorkItemKind.Sensor)
            {
                ProcessSensor(item.Sensor);
            }
            else
            {
                ProcessLog(item.Log);
            }
        }

        private void ProcessLog(in LogEnvelope e)
        {
            // UI
            try
            {
                IOPLogUiSink ui;
                lock (_uiLock) { ui = _uiSink; }
                ui.Enqueue(FormatForUi(e));
            }
            catch { }

            // 파일 기록이 복구될 때까지 현재 항목을 보류한다.
            // 그동안 생산자는 계속 enqueue하며, 큐가 차면 설정된 Drop 정책을 적용한다.
            bool fileWritten = false;
            while (!_stopping && !fileWritten)
            {
                if (!_fileEnabled && !TryRecoverFileLogger())
                {
                    WaitForFileRecovery();
                    continue;
                }

                fileWritten = TryWriteToFile(e);
                if (!fileWritten)
                {
                    WaitForFileRecovery();
                }
            }

            if (!fileWritten)
            {
                return;
            }

            Interlocked.Exchange(ref _lastWriteUtcTicks, DateTime.UtcNow.Ticks);
            Interlocked.Increment(ref _written);
            MaybeTrimFolder();
            MaybeRotateOnSize();
        }

        private void ProcessSensor(in SensorLogEnvelope e)
        {
            if (!EnsureSensorLogger())
            {
                Interlocked.Increment(ref _dropped);
                return;
            }

            string csvLine = SensorCsvFormatter.SerializeRow(e, _opt.SensorCsv);
            var property = new LogEventProperty(SensorCsvFormatter.CsvLinePropertyName, new ScalarValue(csvLine)); var logEvent = 
                new LogEvent(new DateTimeOffset(e.Timestamp), LogEventLevel.Information, exception: null, MessageTemplate.Empty, new[] { property });

            try
            {
                _sensorLogger!.Write(logEvent);
                Interlocked.Exchange(ref _lastWriteUtcTicks, DateTime.UtcNow.Ticks);
                Interlocked.Increment(ref _written);
            }
            catch
            {
                try
                {
                    if (_sensorLogger != null)
                    {
                        _sensorLogger.Dispose();
                    }
                }
                catch { }
                _sensorLogger = null;
                throw;
            }
        }

        private bool EnsureSensorLogger()
        {
            if (_sensorLogger != null)
            {
                return true;
            }

            SensorCsvOptions options = _opt.SensorCsv;
            if (!options.Enabled)
            {
                return false;
            }

            try
            {
                if (string.IsNullOrWhiteSpace(_sensorCurrentFilePath))
                {
                    _sensorCurrentFilePath = BuildNewSensorFilePath();
                }

                TimeSpan flushInterval;
                if (options.FlushToDiskInterval > TimeSpan.Zero)
                {
                    flushInterval = options.FlushToDiskInterval;
                }
                else
                {
                    flushInterval = TimeSpan.FromMilliseconds(100);
                }

                long? fileSizeLimit;
                if (options.FileSizeLimitBytes > 0)
                {
                    fileSizeLimit = options.FileSizeLimitBytes;
                }
                else
                {
                    fileSizeLimit = null;
                }

                int? retainedFileCount;
                if (options.RetainedFileCountLimit > 0)
                {
                    retainedFileCount = options.RetainedFileCountLimit;
                }
                else
                {
                    retainedFileCount = null;
                }

                var logger = new LoggerConfiguration()
                    .MinimumLevel.Verbose()
                    .WriteTo.File(
                        formatter: new SensorCsvFormatter(),
                        path: _sensorCurrentFilePath,
                        restrictedToMinimumLevel: LogEventLevel.Verbose,
                        fileSizeLimitBytes: fileSizeLimit,
                        buffered: true,
                        shared: false,
                        flushToDiskInterval: flushInterval,
                        rollingInterval: RollingInterval.Infinite,
                        rollOnFileSizeLimit: fileSizeLimit.HasValue,
                        retainedFileCountLimit: retainedFileCount,
                        encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                        hooks: new SensorCsvHeaderHook(SensorCsvFormatter.SerializeHeader(options)))
                    .CreateLogger();

                _sensorLogger = (Logger)logger;
                return true;
            }
            catch (Exception ex)
            {
                SafeUi("[LOG] Sensor CSV unavailable: " + ex.Message);
                return false;
            }
        }

        private void WaitForFileRecovery()
        {
            TimeSpan delay = _opt.FileRecoveryRetryInterval;
            if (delay <= TimeSpan.Zero)
            {
                delay = TimeSpan.FromSeconds(1);
            }

            _stopSignal.Wait(delay);
        }

        private string FormatForUi(in LogEnvelope e)
        {
            string msg;
            if (e.Message == null)
            {
                msg = "";
            }
            else
            {
                msg = e.Message;
            }

            if (_opt.MaxMessageChars > 0 && msg.Length > _opt.MaxMessageChars)
            {
                msg = msg.Substring(0, _opt.MaxMessageChars) + " …(truncated)";
            }

            return string.Format(CultureInfo.InvariantCulture, "{0:HH:mm:ss.fff} [{1}] {2}", e.Local, e.Level, msg);
        }

        private bool TryWriteToFile(in LogEnvelope e)
        {
            string msg;
            if (e.Message == null)
            {
                msg = "";
            }
            else
            {
                msg = e.Message;
            }

            if (_opt.MaxMessageChars > 0 && msg.Length > _opt.MaxMessageChars)
            {
                msg = msg.Substring(0, _opt.MaxMessageChars) + " …(truncated)";
            }

            Exception? ex = e.Exception;
            if (ex != null && _opt.MaxExceptionChars > 0)
            {
                string exText;
                try { exText = ex.ToString(); } catch { exText = "<exception>"; }
                if (exText.Length > _opt.MaxExceptionChars)
                {
                    exText = exText.Substring(0, _opt.MaxExceptionChars) + " …(truncated)";
                    msg = msg + Environment.NewLine + exText;
                    ex = null;
                }
            }

            var logger = _fileLogger;
            if (logger == null)
            {
                _fileEnabled = false;
                return false;
            }

            try
            {
                var level = MapLevel(e.Level);

                Serilog.ILogger l = logger.ForContext("Category", e.Category);
                if (e.EventId.Id != 0 || !string.IsNullOrWhiteSpace(e.EventId.Name))
                {
                    l = l.ForContext("EventId", e.EventId.Id);
                }

                if (e.Props != null)
                {
                    foreach (var kv in e.Props)
                    {
                        if (kv.Key == null)
                        {
                            continue;
                        }

                        l = l.ForContext(kv.Key, kv.Value, destructureObjects: true);
                    }
                }

                // 중괄호/템플릿 해석 이슈 방지
                l.Write(level, ex, "{Text}", msg);

                _consecutiveFileFailures = 0;
                Interlocked.Exchange(ref _fileUnavailableNotified, 0);
                return true;
            }
            catch (Exception ioEx)
            {
                // 메시지/속성 직렬화 같은 항목 자체의 오류는 파일 장애로 취급하지 않는다.
                // 상위 항목 단위 catch에서 현재 로그만 버리고 다음 로그를 계속 처리한다.
                if (!IsRecoverableFileException(ioEx))
                {
                    throw;
                }

                _consecutiveFileFailures++;

                if (_opt.EnableFailover)
                {
                    if (TryFailover(ioEx))
                    {
                        try
                        {
                            var level = MapLevel(e.Level);
                            Logger? retryLogger = _fileLogger;
                            if (retryLogger == null)
                            {
                                throw new IOException("Failover logger was not created.");
                            }

                            retryLogger.Write(level, ex, "{Text}", msg);
                            _consecutiveFileFailures = 0;
                            Interlocked.Exchange(ref _fileUnavailableNotified, 0);
                            return true;
                        }
                        catch (Exception retryEx) when (IsRecoverableFileException(retryEx))
                        {
                            _consecutiveFileFailures++;
                        }
                    }
                }

                _fileEnabled = false;
                if (Interlocked.Exchange(ref _fileUnavailableNotified, 1) == 0)
                {
                    SafeUi("[LOG] File logging unavailable; buffering with " + _opt.QueueMode + ": " + ioEx.Message);
                }

                return false;
            }
        }

        private static bool IsRecoverableFileException(Exception ex)
        {
            return ex is IOException ||
                   ex is UnauthorizedAccessException ||
                   ex is ObjectDisposedException;
        }

        private bool TryRecoverFileLogger()
        {
            Logger? old = _fileLogger;

            _failoverSeq++;
            string prefix = _opt.FileNamePrefix + "recovery_";
            string path = BuildNewLogFilePath(prefix, _failoverSeq);

            if (!TryCreateFileLogger(path))
            {
                if (Interlocked.Exchange(ref _fileUnavailableNotified, 1) == 0)
                {
                    SafeUi("[LOG] File logging unavailable; buffering with " + _opt.QueueMode + ".");
                }

                return false;
            }

            try
            {
                if (old != null && !ReferenceEquals(old, _fileLogger))
                {
                    old.Dispose();
                }
            }
            catch { }

            _consecutiveFileFailures = 0;
            if (Interlocked.Exchange(ref _fileUnavailableNotified, 0) != 0)
            {
                SafeUi("[LOG] File logging recovered -> " + Path.GetFileName(path));
            }

            return true;
        }

        private void SafeUi(string line)
        {
            try
            {
                IOPLogUiSink ui;
                lock (_uiLock) { ui = _uiSink; }
                ui.Enqueue(line);
            }
            catch { }
        }

        private bool TryFailover(Exception reason)
        {
            try
            {
                _failoverSeq++;
                string prefix = _opt.FileNamePrefix + "fail_";
                string newPath = BuildNewLogFilePath(prefix, _failoverSeq);

                Logger? old = _fileLogger;

                if (TryCreateFileLogger(newPath))
                {
                    try { if (old != null) { old.Dispose(); } } catch { }
                    SafeUi("[LOG] Failover -> " + Path.GetFileName(newPath));
                    return true;
                }
            }
            catch { }

            return false;
        }

        private bool TryCreateFileLogger(string path)
        {
            try
            {
                Directory.CreateDirectory(_opt.LogFolder);

                var min = MapLevel(_opt.MinimumLevel);

                var cfg = new LoggerConfiguration()
                    .MinimumLevel.Is(min)
                    .WriteTo.File(
                        path: path,
                        shared: _opt.SharedFileSink,
                        flushToDiskInterval: _opt.FlushToDiskInterval,
                        outputTemplate: _opt.OutputTemplate);

                var created = cfg.CreateLogger();

                _fileLogger = (Logger)created;
                _currentFilePath = path;
                _fileEnabled = true;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private string BuildNewLogFilePath(string prefix)
        {
            return BuildNewLogFilePath(prefix, 0);
        }

        private string BuildNewSensorFilePath()
        {
            SensorCsvOptions options = _opt.SensorCsv;
            string folder;
            if (string.IsNullOrWhiteSpace(options.LogFolder))
            {
                folder = "SensorLogs";
            }
            else
            {
                folder = options.LogFolder;
            }

            if (!Path.IsPathRooted(folder))
            {
                folder = Path.Combine(AppContext.BaseDirectory, folder);
            }

            string extension;
            if (string.IsNullOrWhiteSpace(options.FileExtension))
            {
                extension = ".csv";
            }
            else
            {
                extension = options.FileExtension;
            }

            if (!extension.StartsWith("."))
            {
                extension = "." + extension;
            }

            string prefix;
            if (string.IsNullOrWhiteSpace(options.FileNamePrefix))
            {
                prefix = "sensor_";
            }
            else
            {
                prefix = options.FileNamePrefix;
            }

            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);

            return Path.Combine(folder, prefix + stamp + extension);
        }

        private string BuildNewLogFilePath(string prefix, int seq)
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);

            string ext = _opt.FileExtension;
            if (string.IsNullOrWhiteSpace(ext))
            {
                ext = ".log";
            }

            if (!ext.StartsWith("."))
            {
                ext = "." + ext;
            }

            string name;
            if (seq > 0)
            {
                name = prefix + stamp + "_" + seq.ToString("D4", CultureInfo.InvariantCulture) + ext;
            }
            else
            {
                name = prefix + stamp + ext;
            }

            return Path.Combine(_opt.LogFolder, name);
        }

        private void MaybeTrimFolder()
        {
            if (_opt.MaxFolderBytes <= 0)
            {
                return;
            }

            if (_opt.FolderTrimInterval <= TimeSpan.Zero)
            {
                return;
            }

            long now = DateTime.UtcNow.Ticks;
            long next = Interlocked.Read(ref _nextTrimUtcTicks);
            if (next != 0 && now < next)
            {
                return;
            }

            Interlocked.Exchange(ref _nextTrimUtcTicks, now + _opt.FolderTrimInterval.Ticks);

            try
            {
                TrimFolderToLimit(_opt.LogFolder, _opt.MaxFolderBytes, _currentFilePath, _opt.FileExtension);
            }
            catch { }
        }

        private void MaybeRotateOnSize()
        {
            if (_opt.MaxActiveFileBytes <= 0)
            {
                return;
            }

            if (_opt.ActiveFileSizeCheckInterval <= TimeSpan.Zero)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(_currentFilePath))
            {
                return;
            }

            long now = DateTime.UtcNow.Ticks;
            long next = Interlocked.Read(ref _nextSizeCheckUtcTicks);
            if (next != 0 && now < next)
            {
                return;
            }

            Interlocked.Exchange(ref _nextSizeCheckUtcTicks, now + _opt.ActiveFileSizeCheckInterval.Ticks);

            try
            {
                var fi = new FileInfo(_currentFilePath);
                if (!fi.Exists)
                {
                    return;
                }

                if (fi.Length <= _opt.MaxActiveFileBytes)
                {
                    return;
                }

                Logger? old = _fileLogger;
                string newPath = BuildNewLogFilePath(_opt.FileNamePrefix);

                if (TryCreateFileLogger(newPath))
                {
                    try { if (old != null) { old.Dispose(); } } catch { }
                    SafeUi("[LOG] Rotate(size) -> " + Path.GetFileName(newPath));
                }
            }
            catch { }
        }

        private static void TrimFolderToLimit(string folder, long maxBytes, string currentFile, string fileExtension)
        {
            if (!Directory.Exists(folder))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(fileExtension))
            {
                fileExtension = ".log";
            }

            if (!fileExtension.StartsWith("."))
            {
                fileExtension = "." + fileExtension;
            }

            var files = new DirectoryInfo(folder)
                .GetFiles("*" + fileExtension, SearchOption.TopDirectoryOnly)
                .OrderBy(f => f.LastWriteTimeUtc)
                .ToList();

            long total = 0;
            for (int i = 0; i < files.Count; i++)
            {
                total += files[i].Length;
            }

            if (total <= maxBytes)
            {
                return;
            }

            for (int i = 0; i < files.Count && total > maxBytes; i++)
            {
                var f = files[i];
                if (string.Equals(f.FullName, currentFile, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    long len = f.Length;
                    f.Delete();
                    total -= len;
                }
                catch
                {
                    // 삭제 실패는 무시
                }
            }
        }

        private static LogEventLevel MapLevel(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Trace: return LogEventLevel.Verbose;
                case LogLevel.Debug: return LogEventLevel.Debug;
                case LogLevel.Information: return LogEventLevel.Information;
                case LogLevel.Warning: return LogEventLevel.Warning;
                case LogLevel.Error: return LogEventLevel.Error;
                case LogLevel.Critical: return LogEventLevel.Fatal;
                case LogLevel.None: return LogEventLevel.Fatal;
                default: return LogEventLevel.Information;
            }
        }

        public OPLogStats GetStats()
        {
            int depth;
            lock (_queueLock) { depth = _queue.Count; }

            bool fileOk = _fileEnabled && _fileLogger != null;

            return new OPLogStats(
                configured: true,
                fileEnabled: fileOk,
                currentFile: _currentFilePath,
                enqueued: Interlocked.Read(ref _enqueued),
                written: Interlocked.Read(ref _written),
                dropped: Interlocked.Read(ref _dropped),
                queueDepth: depth,
                maxQueueDepth: _maxDepth,
                lastEnqueueUtcTicks: Interlocked.Read(ref _lastEnqueueUtcTicks),
                lastWriteUtcTicks: Interlocked.Read(ref _lastWriteUtcTicks),
                consecutiveFileFailures: _consecutiveFileFailures,
                workerAlive: _thread.IsAlive,
                workerFaults: Interlocked.Read(ref _workerFaults),
                lastWorkerFaultUtcTicks: Interlocked.Read(ref _lastWorkerFaultUtcTicks));
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _accepting = false;

            lock (_queueLock)
            {
                Monitor.PulseAll(_queueLock);
            }

            bool stopped = false;
            try
            {
                if (_thread.IsAlive)
                {
                    stopped = _thread.Join(2000);
                }
                else
                {
                    stopped = true;
                }
            }
            catch { }

            if (!stopped)
            {
                _stopping = true;
                _stopSignal.Set();

                lock (_queueLock)
                {
                    Monitor.PulseAll(_queueLock);
                }

                try { stopped = _thread.Join(500); } catch { }
            }

            if (stopped || !_thread.IsAlive)
            {
                try { _stopSignal.Dispose(); } catch { }
            }
        }
    }


}
