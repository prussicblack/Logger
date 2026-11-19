using System;
using Avalonia.Threading;

namespace LogUtil.Avalonia
{
    /// <summary>Avalonia Dispatcher의 UI 스레드로 로그를 전달하는 싱크입니다.</summary>
    public sealed class AvaloniaUiLogSink : IOPLogUiSink, IDisposable
    {
        private readonly BufferedUiLogSink _inner;

        public AvaloniaUiLogSink(Action<string> append, TimeSpan? flushInterval = null, int maxPending = 5000, int maxBatchSize = 2000)
        {
            _inner = new BufferedUiLogSink(Dispatch, append, flushInterval, maxPending, maxBatchSize);
        }

        public void Enqueue(string line) => _inner.Enqueue(line);

        private static void Dispatch(Action action) =>
            Dispatcher.UIThread.Post(action, DispatcherPriority.Background);

        public void Dispose() => _inner.Dispose();
    }
}
