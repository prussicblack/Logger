using System;
using System.Collections.Concurrent;
using System.Threading;

namespace SOEM_FrontEnd.Util.Logging
{
    /// <summary>
    /// UI 프레임워크와 무관한 버퍼형 로그 싱크입니다.
    /// 로그 워커에서는 큐에 넣기만 하고, 실제 append 작업은 전달받은 UI dispatcher에서 실행합니다.
    /// </summary>
    public sealed class BufferedUiLogSink : IOPLogUiSink, IDisposable
    {
        private readonly ConcurrentQueue<string> _queue = new ConcurrentQueue<string>();
        private readonly Action<Action> _dispatch;
        private readonly Action<string> _append;
        private readonly Timer _timer;

        private int _pendingCount;
        private int _flushScheduled;
        private int _disposed;

        public BufferedUiLogSink(Action<Action> dispatch, Action<string> append, TimeSpan? flushInterval = null, int maxPending = 5000, int maxBatchSize = 2000)
        {
            if (dispatch == null)
                throw new ArgumentNullException(nameof(dispatch));
            if (append == null)
                throw new ArgumentNullException(nameof(append));

            _dispatch = dispatch;
            _append = append;

            if (maxPending <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxPending));
            if (maxBatchSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxBatchSize));

            MaxPending = maxPending;
            MaxBatchSize = maxBatchSize;

            TimeSpan interval;
            if (flushInterval.HasValue)
                interval = flushInterval.Value;
            else
                interval = TimeSpan.FromMilliseconds(100);
            if (interval <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(flushInterval));

            _timer = new Timer(_ => TryScheduleFlush(), null, interval, interval);
        }

        public int MaxPending { get; }

        public int MaxBatchSize { get; }

        public void Enqueue(string line)
        {
            if (line == null || Volatile.Read(ref _disposed) != 0)
                return;

            _queue.Enqueue(line);
            int count = Interlocked.Increment(ref _pendingCount);

            // UI가 밀리면 가장 오래된 화면용 로그부터 버립니다.
            while (count > MaxPending && _queue.TryDequeue(out _))
            {
                count = Interlocked.Decrement(ref _pendingCount);
            }
        }

        private void TryScheduleFlush()
        {
            if (Volatile.Read(ref _disposed) != 0 || _queue.IsEmpty)
                return;
            if (Interlocked.CompareExchange(ref _flushScheduled, 1, 0) != 0)
                return;

            try
            {
                _dispatch(FlushOnUiThread);
            }
            catch
            {
                // UI 핸들이 아직 없거나 종료 중이면 다음 timer tick에서 다시 시도합니다.
                Volatile.Write(ref _flushScheduled, 0);
            }
        }

        private void FlushOnUiThread()
        {
            try
            {
                int remaining = MaxBatchSize;
                while (remaining-- > 0 && _queue.TryDequeue(out string? line))
                {
                    Interlocked.Decrement(ref _pendingCount);
                    if (line != null)
                    {
                        try { _append(line); } catch { }
                    }
                }
            }
            finally
            {
                Volatile.Write(ref _flushScheduled, 0);
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            _timer.Dispose();

            while (_queue.TryDequeue(out _)) { }
            Interlocked.Exchange(ref _pendingCount, 0);
        }
    }
}
