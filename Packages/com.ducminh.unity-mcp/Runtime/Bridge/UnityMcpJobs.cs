#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace DucMinh.UnityMcp
{
    [Serializable]
    public sealed class UnityMcpJob
    {
        public string jobId;
        public string status;
        public UnityMcpResult result;
        public string error;
        internal CancellationTokenSource cancellation;
    }

    public sealed class UnityMcpJobStore
    {
        public static UnityMcpJobStore Shared { get; } = new UnityMcpJobStore();
        private readonly ConcurrentDictionary<string, UnityMcpJob> jobs = new ConcurrentDictionary<string, UnityMcpJob>();

        public UnityMcpJob Create()
        {
            var job = new UnityMcpJob { jobId = Guid.NewGuid().ToString("N"), status = "queued", cancellation = new CancellationTokenSource() };
            jobs[job.jobId] = job;
            return job;
        }

        public bool TryGet(string id, out UnityMcpJob job) => jobs.TryGetValue(id, out job);

        public bool Cancel(string id, out UnityMcpJob job)
        {
            if (!jobs.TryGetValue(id, out job)) return false;
            job.cancellation.Cancel();
            if (job.status == "queued" || job.status == "running") job.status = "cancelled";
            return true;
        }
    }
}
#endif
