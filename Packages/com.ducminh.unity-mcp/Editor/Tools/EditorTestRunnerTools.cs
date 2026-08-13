using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace DucMinh.UnityMcp.Editor
{
    [Serializable]
    public sealed class TestRunInput
    {
        public string mode = "editmode";
        public List<string> testNames = new List<string>();
        public List<string> assemblyNames = new List<string>();
        public bool apply;
    }

    [Serializable]
    public sealed class TestRunStartOutput
    {
        public bool dryRun;
        public bool accepted;
        public string jobId;
        public string runnerId;
        public string mode;
        public string summary;
    }

    [Serializable]
    public sealed class TestRunResultOutput
    {
        public string jobId;
        public string runnerId;
        public string status;
        public string resultState;
        public int passed;
        public int failed;
        public int skipped;
        public int inconclusive;
        public int asserts;
        public double durationSeconds;
        public string message;
        public string finishedUtc;
    }

    [Serializable] public sealed class TestJobGetInput { public string jobId; }
    [Serializable] public sealed class TestCancelInput { public string jobId; public bool apply; }
    [Serializable] public sealed class TestCancelOutput { public bool dryRun; public bool cancelled; public string jobId; public string runnerId; public string status; public string summary; }

    /// <summary>
    /// Test Framework integration. The UPM package declares com.unity.test-framework,
    /// so the implementation uses the supported TestRunnerApi instead of private Editor
    /// runner types. Runs are serialized because TestRunner callbacks do not identify a
    /// specific run id.
    /// </summary>
    public static class EditorTestRunnerTools
    {
        private static readonly object Gate = new object();
        private static ActiveTestRun active;

        [UnityMcpTool("test-run", Description = "Start one filtered Unity Test Framework run; dry-run unless apply is true.", Category = "test", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Write, SupportsDryRun = true, SupportsCancellation = true, ReturnsJob = true, TimeoutMs = 600000)]
        public static TestRunStartOutput TestRun(TestRunInput input, UnityMcpContext context)
        {
            var filter = BuildFilter(input);
            if (context.DryRun)
            {
                return new TestRunStartOutput
                {
                    dryRun = true,
                    mode = ModeName(filter.testMode),
                    summary = "Dry run: start the selected Unity Test Framework run."
                };
            }

            lock (Gate)
            {
                if (active != null && !active.IsFinished)
                    throw new InvalidOperationException("A UnityMCP test run is already active. Read or cancel that job before starting another run.");
                var job = UnityMcpJobStore.Shared.Create();
                job.status = "running";
                var api = ScriptableObject.CreateInstance<TestRunnerApi>();
                var run = new ActiveTestRun(job, api);
                try
                {
                    api.RegisterCallbacks(run);
                    active = run;
                    run.runnerId = api.Execute(new ExecutionSettings(filter));
                    if (string.IsNullOrWhiteSpace(run.runnerId)) throw new InvalidOperationException("The Unity Test Framework did not return a run identifier.");
                    return new TestRunStartOutput
                    {
                        accepted = true,
                        jobId = job.jobId,
                        runnerId = run.runnerId,
                        mode = ModeName(filter.testMode),
                        summary = "Unity Test Framework run queued."
                    };
                }
                catch
                {
                    if (ReferenceEquals(active, run)) active = null;
                    run.Dispose();
                    throw;
                }
            }
        }

        [UnityMcpTool("test-job-get", Description = "Read status and summary results for a UnityMCP Test Framework job.", Category = "test", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.SafeRead)]
        public static TestRunResultOutput TestJobGet(TestJobGetInput input)
        {
            if (string.IsNullOrWhiteSpace(input.jobId)) throw new ArgumentException("jobId is required.");
            if (!UnityMcpJobStore.Shared.TryGet(input.jobId, out var job)) throw new ArgumentException("Unknown UnityMCP test job.");
            lock (Gate)
            {
                if (active != null && string.Equals(active.job.jobId, input.jobId, StringComparison.Ordinal)) return active.ToOutput();
            }
            if (job.result?.structuredContent is TestRunResultOutput result) return result;
            return new TestRunResultOutput { jobId = job.jobId, status = job.status, message = job.error };
        }

        [UnityMcpTool("test-cancel", Description = "Request cancellation of a running UnityMCP Test Framework run; dry-run unless apply is true.", Category = "test", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Write, SupportsDryRun = true, SupportsCancellation = true)]
        public static TestCancelOutput TestCancel(TestCancelInput input, UnityMcpContext context)
        {
            if (string.IsNullOrWhiteSpace(input.jobId)) throw new ArgumentException("jobId is required.");
            ActiveTestRun run;
            lock (Gate)
            {
                run = active;
                if (run == null || !string.Equals(run.job.jobId, input.jobId, StringComparison.Ordinal) || run.IsFinished)
                    throw new ArgumentException("No active UnityMCP test run matches jobId.");
                if (context.DryRun)
                {
                    return new TestCancelOutput { dryRun = true, jobId = run.job.jobId, runnerId = run.runnerId, status = run.job.status, summary = "Dry run: cancel the active Unity Test Framework run." };
                }
                if (!TestRunnerApi.CancelTestRun(run.runnerId))
                    throw new InvalidOperationException("Unity Test Framework did not accept cancellation for this run.");
                run.cancelRequested = true;
                run.job.status = "cancelling";
                return new TestCancelOutput { cancelled = true, jobId = run.job.jobId, runnerId = run.runnerId, status = run.job.status, summary = "Cancellation requested from Unity Test Framework." };
            }
        }

        private static Filter BuildFilter(TestRunInput input)
        {
            var mode = ParseMode(input.mode);
            var names = NormalizeList(input.testNames, "testNames", 128, 512);
            var assemblies = NormalizeList(input.assemblyNames, "assemblyNames", 64, 256);
            if (names.Count == 0 && assemblies.Count == 0)
                throw new ArgumentException("At least one testNames or assemblyNames filter is required; UnityMCP does not start an unbounded all-project test run.");
            return new Filter { testMode = mode, testNames = names.ToArray(), assemblyNames = assemblies.ToArray() };
        }

        private static TestMode ParseMode(string mode)
        {
            switch ((mode ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "editmode":
                case "edit": return TestMode.EditMode;
                case "playmode":
                case "play": return TestMode.PlayMode;
                default: throw new ArgumentException("mode must be editmode or playmode.");
            }
        }

        private static string ModeName(TestMode mode) => mode == TestMode.EditMode ? "editmode" : "playmode";

        private static List<string> NormalizeList(List<string> values, string field, int maxCount, int maxLength)
        {
            values = values ?? new List<string>();
            if (values.Count > maxCount) throw new ArgumentException(field + " contains too many values.");
            var output = new List<string>();
            foreach (var value in values)
            {
                var normalized = (value ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > maxLength || normalized.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0)
                    throw new ArgumentException(field + " contains an invalid value.");
                if (!output.Contains(normalized)) output.Add(normalized);
            }
            return output;
        }

        private sealed class ActiveTestRun : ICallbacks
        {
            public readonly UnityMcpJob job;
            private readonly TestRunnerApi api;
            public string runnerId;
            public bool cancelRequested;
            public bool IsFinished { get; private set; }
            private int passed;
            private int failed;
            private int skipped;
            private int inconclusive;
            private int asserts;
            private double duration;
            private string state;
            private string message;
            private string finishedUtc;

            public ActiveTestRun(UnityMcpJob job, TestRunnerApi api)
            {
                this.job = job;
                this.api = api;
            }

            public void RunStarted(ITestAdaptor testsToRun) { }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }

            public void RunFinished(ITestResultAdaptor result)
            {
                lock (Gate)
                {
                    if (IsFinished) return;
                    IsFinished = true;
                    state = result?.ResultState ?? (cancelRequested ? "Cancelled" : "Unknown");
                    passed = result?.PassCount ?? 0;
                    failed = result?.FailCount ?? 0;
                    skipped = result?.SkipCount ?? 0;
                    inconclusive = result?.InconclusiveCount ?? 0;
                    asserts = result?.AssertCount ?? 0;
                    duration = result?.Duration ?? 0d;
                    message = Clip(result?.Message, 4096);
                    finishedUtc = DateTime.UtcNow.ToString("O");
                    var output = ToOutput();
                    job.result = UnityMcpResult.Success(output);
                    job.error = null;
                    job.status = cancelRequested || state.IndexOf("cancel", StringComparison.OrdinalIgnoreCase) >= 0
                        ? "cancelled" : failed > 0 || string.Equals(result?.TestStatus.ToString(), "Failed", StringComparison.Ordinal) ? "failed" : "succeeded";
                    Dispose();
                    if (ReferenceEquals(active, this)) active = null;
                }
            }

            public TestRunResultOutput ToOutput() => new TestRunResultOutput
            {
                jobId = job.jobId,
                runnerId = runnerId,
                status = job.status,
                resultState = state,
                passed = passed,
                failed = failed,
                skipped = skipped,
                inconclusive = inconclusive,
                asserts = asserts,
                durationSeconds = duration,
                message = message,
                finishedUtc = finishedUtc
            };

            public void Dispose()
            {
                try { api.UnregisterCallbacks(this); } catch { }
                if (api != null) UnityEngine.Object.DestroyImmediate(api);
            }

            private static string Clip(string value, int limit)
            {
                if (string.IsNullOrEmpty(value) || value.Length <= limit) return value;
                return value.Substring(0, limit - 1) + "…";
            }
        }
    }
}
