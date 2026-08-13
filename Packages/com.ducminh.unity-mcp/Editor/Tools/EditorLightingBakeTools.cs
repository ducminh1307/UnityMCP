using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DucMinh.UnityMcp.Editor
{
    [Serializable]
    public sealed class LightingBakeInput
    {
        /// <summary>"start" queues a bake; "cancel" stops the named UnityMCP bake job.</summary>
        public string action = "start";
        public string jobId;
        public bool apply;
    }

    [Serializable]
    public sealed class LightingBakeOutput
    {
        public bool dryRun;
        public bool accepted;
        public bool cancelled;
        public string jobId;
        public string status;
        public string summary;
    }

    /// <summary>
    /// Wraps Unity's public asynchronous lightmapping API in a cancellable UnityMCP job.
    /// It never changes LightingSettings; callers configure those separately and must opt into
    /// this unsafe, potentially long-running operation.
    /// </summary>
    public static class EditorLightingBakeTools
    {
        [UnityMcpTool("lighting-bake", Description = "Start or cancel an asynchronous Unity lighting bake; dry-run unless apply is true.", Category = "rendering", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Unsafe, SupportsDryRun = true, SupportsCancellation = true, ReturnsJob = true, TimeoutMs = 600000)]
        public static LightingBakeOutput LightingBake(LightingBakeInput input, UnityMcpContext context)
        {
            var action = (input.action ?? "start").Trim().ToLowerInvariant();
            if (action != "start" && action != "cancel") throw new ArgumentException("action must be start or cancel.");

            if (action == "cancel") return Cancel(input, context);
            if (Lightmapping.isRunning) throw new InvalidOperationException("Unity is already running a lighting bake. Wait for it or cancel its UnityMCP job.");
            if (context.DryRun)
            {
                return new LightingBakeOutput
                {
                    dryRun = true,
                    status = "dry-run",
                    summary = "Dry run: start an asynchronous Unity lighting bake."
                };
            }

            var handle = EditorWorkflowJobRunner.Start(new LightingBakeOperation());
            return new LightingBakeOutput
            {
                accepted = true,
                jobId = handle.jobId,
                status = handle.status,
                summary = "Lighting bake queued. Poll job-get or cancel with job-cancel."
            };
        }

        private static LightingBakeOutput Cancel(LightingBakeInput input, UnityMcpContext context)
        {
            if (string.IsNullOrWhiteSpace(input.jobId)) throw new ArgumentException("jobId is required when action is cancel.");
            if (!UnityMcpJobStore.Shared.TryGet(input.jobId, out var job)) throw new ArgumentException("Unknown UnityMCP job.");
            if (context.DryRun)
            {
                return new LightingBakeOutput
                {
                    dryRun = true,
                    jobId = job.jobId,
                    status = job.status,
                    summary = "Dry run: cancel the UnityMCP lighting bake job."
                };
            }

            if (!UnityMcpJobStore.Shared.Cancel(input.jobId, out job)) throw new ArgumentException("Unknown UnityMCP job.");
            if (Lightmapping.isRunning) Lightmapping.Cancel();
            return new LightingBakeOutput
            {
                cancelled = true,
                jobId = job.jobId,
                status = job.status,
                summary = "Lighting bake cancellation requested."
            };
        }

        private sealed class LightingBakeOperation : IEditorWorkflowOperation
        {
            private bool started;
            private bool cancellationRequested;

            public bool DrainWhenCancelled => true;

            public bool Tick(UnityMcpJob job)
            {
                if (!started)
                {
                    started = true;
                    job.status = "running";
                    if (!Lightmapping.BakeAsync())
                    {
                        EditorWorkflowJobRunner.Fail(job, "Unity could not start the asynchronous lighting bake.");
                        return true;
                    }
                    return false;
                }

                if (job.IsCancellationRequested && !cancellationRequested)
                {
                    cancellationRequested = true;
                    if (Lightmapping.isRunning) Lightmapping.Cancel();
                }
                if (Lightmapping.isRunning) return false;

                if (cancellationRequested)
                {
                    job.status = "cancelled";
                    job.error = null;
                    job.result = UnityMcpResult.Success(new { status = "cancelled" }, "Lighting bake cancelled.");
                }
                else
                {
                    EditorWorkflowJobRunner.Succeed(job, new { status = "succeeded", completed = true });
                }
                return true;
            }
        }
    }
}
