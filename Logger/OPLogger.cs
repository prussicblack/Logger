using System;
using Microsoft.Extensions.Logging;
using System.Threading;



namespace LogUtil
{
    /// <summary>
    /// 주요 구현점.
    /// OPLogger: Microsoft.Extensions.Logging.ILogger 기반 + 내부 Serilog 파일 기록 + UI sink.
    /// - 별도 worker thread (bounded queue)
    /// - threadSafe, bounded DropOldest/DropNewest queue
    /// - 폴더 총 용량 제한(초과 시 오래된 파일 삭제)
    /// - 파일 용량 제한
    /// - 파일 잠김/공유위반/IO 예외 시 failover(새 파일로 전환)
    /// - 예외가 나도 프로그램이 죽지 않음
    /// </summary>


    public static class OPLogger
    {
        private static readonly object _lock = new object();

        private static OPLogWorker? _worker;
        private static OPLoggerProvider? _provider;
        private static ILoggerFactory? _factory;

        public static bool IsConfigured
        {
            get { lock (_lock) { return _factory != null; } }
        }

        public static void Configure(OPLoggerOptions options, IOPLogUiSink? uiSink = null)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            lock (_lock)
            {
                Shutdown_NoLock();

                _worker = new OPLogWorker(options, uiSink);
                _provider = new OPLoggerProvider(_worker, options);

                _factory = LoggerFactory.Create(builder =>
                {
                    builder.ClearProviders();
                    builder.AddProvider(_provider);
                    builder.SetMinimumLevel(options.MinimumLevel);
                });
            }
        }

        public static void SetUiSink(IOPLogUiSink? uiSink)
        {
            lock (_lock)
            {
                if (_worker == null)
                {
                    return;
                }

                _worker.SetUiSink(uiSink);
            }
        }

        public static ILogger CreateLogger(string categoryName)
        {
            lock (_lock)
            {
                if (_factory == null)
                {
                    throw new InvalidOperationException("OPLogger is not configured. Call OPLogger.Configure(...) first.");
                }

                return _factory.CreateLogger(categoryName);
            }
        }

        public static ILogger<T> CreateLogger<T>()
        {
            lock (_lock)
            {
                if (_factory == null)
                {
                    throw new InvalidOperationException("OPLogger is not configured. Call OPLogger.Configure(...) first.");
                }

                return _factory.CreateLogger<T>();
            }
        }

        /// <summary>
        /// DateTime.Now와 값만 센서 CSV 행으로 기록합니다.
        /// 일반 로그와 동일한 bounded queue 및 worker를 사용합니다.
        /// </summary>
        public static bool WriteSensor(params object?[] values)
        {
            OPLogWorker? worker = Volatile.Read(ref _worker);
            if (worker == null)
            {
                return false;
            }

            object?[] sensorValues;
            if (values == null)
            {
                sensorValues = Array.Empty<object?>();
            }
            else
            {
                sensorValues = values;
            }

            return worker.EnqueueSensor(new SensorLogEnvelope(DateTime.Now, sensorValues));
        }

        public static OPLogStats GetStats()
        {
            lock (_lock)
            {
                if (_worker == null)
                {
                    return OPLogStats.Empty;
                }

                return _worker.GetStats();
            }
        }

        public static void Shutdown()
        {
            lock (_lock)
            {
                Shutdown_NoLock();
            }
        }

        private static void Shutdown_NoLock()
        {
            try { if (_factory != null) { _factory.Dispose(); } } catch { }
            _factory = null;

            try { if (_provider != null) { _provider.Dispose(); } } catch { }
            _provider = null;

            try { if (_worker != null) { _worker.Dispose(); } } catch { }
            _worker = null;
        }
    }
}
