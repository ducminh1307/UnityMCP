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
        public string jobType;
        public string status;
        public float progress;
        public string progressMessage;
        public string createdUtc;
        public string startedUtc;
        public string completedUtc;
        public long durationMilliseconds;
        public UnityMcpResult result;
        public string error;
        internal CancellationTokenSource cancellation;

        /// <summary>
        /// Allows an Editor-side job operation to observe cancellation without exposing the
        /// mutable cancellation source across the Runtime/Editor assembly boundary.
        /// </summary>
        public bool IsCancellationRequested => cancellation != null && cancellation.IsCancellationRequested;
    }

    public sealed class UnityMcpJobStore
    {
        public static UnityMcpJobStore Shared { get; } = new UnityMcpJobStore();
        private readonly ConcurrentDictionary<string, UnityMcpJob> jobs = new ConcurrentDictionary<string, UnityMcpJob>();

        public UnityMcpJob Create(string jobType = "operation")
        {
            var job = new UnityMcpJob
            {
                jobId = Guid.NewGuid().ToString("N"), jobType = string.IsNullOrWhiteSpace(jobType) ? "operation" : jobType,
                status = "queued", progress = 0f, createdUtc = DateTime.UtcNow.ToString("O"), cancellation = new CancellationTokenSource()
            };
            jobs[job.jobId] = job;
            return job;
        }

        /// <summary>Restores an Editor-persisted job after a Unity domain reload.</summary>
        public UnityMcpJob Restore(string jobId, string jobType, string status, float progress, string progressMessage, string createdUtc, string startedUtc)
        {
            if (string.IsNullOrWhiteSpace(jobId)) throw new ArgumentException("jobId is required.");
            var job = new UnityMcpJob
            {
                jobId = jobId,
                jobType = string.IsNullOrWhiteSpace(jobType) ? "operation" : jobType,
                status = string.IsNullOrWhiteSpace(status) ? "queued" : status,
                progress = Math.Max(0f, Math.Min(1f, progress)),
                progressMessage = progressMessage,
                createdUtc = string.IsNullOrWhiteSpace(createdUtc) ? DateTime.UtcNow.ToString("O") : createdUtc,
                startedUtc = startedUtc,
                cancellation = new CancellationTokenSource()
            };
            jobs[jobId] = job;
            return job;
        }

        public void Start(UnityMcpJob job, string message = null)
        {
            if (job == null || job.status == "cancelled" || IsTerminal(job.status)) return;
            job.status = "running";
            if (string.IsNullOrEmpty(job.startedUtc)) job.startedUtc = DateTime.UtcNow.ToString("O");
            if (message != null) job.progressMessage = message;
        }

        public void Report(UnityMcpJob job, float progress, string message = null)
        {
            Start(job);
            if (job == null || IsTerminal(job.status)) return;
            job.progress = Math.Max(0f, Math.Min(1f, progress));
            if (message != null) job.progressMessage = message;
        }

        public void Complete(UnityMcpJob job, UnityMcpResult result, string message = null)
        {
            Finish(job, "completed", result, null, message);
        }

        public void Fail(UnityMcpJob job, string error, UnityMcpResult result = null)
        {
            Finish(job, "failed", result ?? UnityMcpResult.Error(error ?? "UnityMCP job failed.", "job_failed"), error, null);
        }

        public bool TryGet(string id, out UnityMcpJob job) => jobs.TryGetValue(id, out job);

        public bool Cancel(string id, out UnityMcpJob job)
        {
            if (!jobs.TryGetValue(id, out job)) return false;
            job.cancellation.Cancel();
            if (job.status == "queued" || job.status == "running")
                Finish(job, "cancelled", job.result, null, "Cancellation requested.");
            return true;
        }

        private static void Finish(UnityMcpJob job, string status, UnityMcpResult result, string error, string message)
        {
            if (job == null || IsTerminal(job.status)) return;
            var now = DateTime.UtcNow;
            job.status = status;
            job.progress = status == "completed" ? 1f : job.progress;
            job.progressMessage = message ?? job.progressMessage;
            job.result = result;
            job.error = error;
            job.completedUtc = now.ToString("O");
            if (DateTime.TryParse(job.startedUtc ?? job.createdUtc, out var started)) job.durationMilliseconds = Math.Max(0L, (long)(now - started.ToUniversalTime()).TotalMilliseconds);
        }

        private static bool IsTerminal(string status) => status == "completed" || status == "failed" || status == "cancelled";
    }
}
#endif
