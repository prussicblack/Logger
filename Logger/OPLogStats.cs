using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LogUtil
{
    public readonly struct OPLogStats
    {
        public static readonly OPLogStats Empty = new OPLogStats(
            configured: false,
            fileEnabled: false,
            currentFile: "",
            enqueued: 0,
            written: 0,
            dropped: 0,
            queueDepth: 0,
            maxQueueDepth: 0,
            lastEnqueueUtcTicks: 0,
            lastWriteUtcTicks: 0,
            consecutiveFileFailures: 0,
            workerAlive: false,
            workerFaults: 0,
            lastWorkerFaultUtcTicks: 0);

        public OPLogStats(
            bool configured,
            bool fileEnabled,
            string currentFile,
            long enqueued,
            long written,
            long dropped,
            int queueDepth,
            int maxQueueDepth,
            long lastEnqueueUtcTicks,
            long lastWriteUtcTicks,
            int consecutiveFileFailures,
            bool workerAlive,
            long workerFaults,
            long lastWorkerFaultUtcTicks)
        {
            Configured = configured;
            FileEnabled = fileEnabled;
            if (currentFile == null)
            {
                CurrentFile = "";
            }
            else
            {
                CurrentFile = currentFile;
            }

            Enqueued = enqueued;
            Written = written;
            Dropped = dropped;
            QueueDepth = queueDepth;
            MaxQueueDepth = maxQueueDepth;
            LastEnqueueUtcTicks = lastEnqueueUtcTicks;
            LastWriteUtcTicks = lastWriteUtcTicks;
            ConsecutiveFileFailures = consecutiveFileFailures;
            WorkerAlive = workerAlive;
            WorkerFaults = workerFaults;
            LastWorkerFaultUtcTicks = lastWorkerFaultUtcTicks;
        }

        public bool Configured { get; }
        public bool FileEnabled { get; }
        public string CurrentFile { get; }

        public long Enqueued { get; }
        /// <summary>파일에 실제로 기록된 로그 수</summary>
        public long Written { get; }
        /// <summary>큐 정책 또는 워커 처리 예외로 폐기된 로그 수</summary>
        public long Dropped { get; }

        public int QueueDepth { get; }
        public int MaxQueueDepth { get; }

        public long LastEnqueueUtcTicks { get; }
        public long LastWriteUtcTicks { get; }

        public int ConsecutiveFileFailures { get; }

        public bool WorkerAlive { get; }
        public long WorkerFaults { get; }
        public long LastWorkerFaultUtcTicks { get; }
    }

}
