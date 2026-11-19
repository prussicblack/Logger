using System;
using System.Windows.Threading;

namespace LogUtil.Wpf
{
    /// <summary>WPF Dispatcher의 UI 스레드로 로그를 전달하는 싱크입니다.</summary>
    public sealed class WpfUiLogSink : IOPLogUiSink, IDisposable
    {
        private readonly Dispatcher _dispatcher;
        private readonly BufferedUiLogSink _inner;

        public WpfUiLogSink(
            Dispatcher dispatcher,
            Action<string> append,
            TimeSpan? flushInterval = null,
            int maxPending = 5000,
            int maxBatchSize = 2000)
        {
            if (dispatcher == null)
                throw new ArgumentNullException(nameof(dispatcher));
            _dispatcher = dispatcher;

            _inner = new BufferedUiLogSink(
                Dispatch,
                append,
                flushInterval,
                maxPending,
                maxBatchSize);
        }

        public void Enqueue(string line) => _inner.Enqueue(line);

        private void Dispatch(Action action)
        {
            if (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished)
                throw new InvalidOperationException("WPF Dispatcher is shutting down.");

            _dispatcher.BeginInvoke(action, DispatcherPriority.Background);
        }

        public void Dispose() => _inner.Dispose();
    }
}
