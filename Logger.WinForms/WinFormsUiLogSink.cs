using System;
using System.Windows.Forms;

namespace SOEM_FrontEnd.Util.Logging.WinForms
{
    /// <summary>WinForms Control의 UI 스레드로 로그를 전달하는 싱크입니다.</summary>
    public sealed class WinFormsUiLogSink : IOPLogUiSink, IDisposable
    {
        private readonly Control _owner;
        private readonly BufferedUiLogSink _inner;

        public WinFormsUiLogSink(
            Control owner,
            Action<string> append,
            TimeSpan? flushInterval = null,
            int maxPending = 5000,
            int maxBatchSize = 2000)
        {
            if (owner == null)
                throw new ArgumentNullException(nameof(owner));
            _owner = owner;

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
            if (_owner.IsDisposed || _owner.Disposing)
                throw new ObjectDisposedException(_owner.Name);

            if (!_owner.IsHandleCreated)
                throw new InvalidOperationException("WinForms UI handle has not been created yet.");

            _owner.BeginInvoke(action);
        }

        public void Dispose() => _inner.Dispose();
    }
}
