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
        public List<string> testIds = new List<string>();
        public List<string> testNames = new List<string>();
        public List<string> assemblyNames = new List<string>();
        public string namePattern;
        public List<string> categories = new List<string>();
        public List<string> excludeCategories = new List<string>();
        public bool includeExplicit;
        public bool runAll;
        public int timeoutMs;
        public string expectedSelectionHash;
        public bool apply;
    }

    [Serializable]
    public sealed class TestRunStartOutput
    {
        public bool dryRun;
        public bool accepted;
        public bool failed;
        public string jobId;
        public string runnerId;
        public string mode;
        public int resolvedCount;
        public int explicitCount;
        public List<TestRunResolvedTest> resolvedTests = new List<TestRunResolvedTest>();
        public List<string> unknownTests = new List<string>();
        public string selectionHash;
        public string summary;
        public TestRunErrorOutput error;
    }

    [Serializable] public sealed class TestRunResolvedTest { public string id; public string fullName; public List<string> categories = new List<string>(); public bool @explicit; }
    [Serializable] public sealed class TestRunErrorOutput { public string code; public string message; public List<string> unknownTests = new List<string>(); public List<string> explicitTests = new List<string>(); }

    [Serializable]
    public sealed class TestCaseResultOutput
    {
        public string fullName;
        public string state;
        public double durationSeconds;
        public string message;
        public string stackTrace;
    }

    [Serializable]
    public sealed class TestRunResultOutput
    {
        public string jobId;
        public string jobType;
        public string runnerId;
        public string status;
        public float progress;
        public string progressMessage;
        public long durationMilliseconds;
        public string resultState;
        public int passed;
        public int failed;
        public int skipped;
        public int inconclusive;
        public int asserts;
        public double durationSeconds;
        public string message;
        public string finishedUtc;
        public List<TestCaseResultOutput> results = new List<TestCaseResultOutput>();
    }

    [Serializable] public sealed class TestJobGetInput { public string jobId; }
    [Serializable] public sealed class TestCancelInput { public string jobId; public bool apply; }
    [Serializable] public sealed class TestCancelOutput { public bool dryRun; public bool cancelled; public string jobId; public string runnerId; public string status; public string summary; }
    [Serializable] internal sealed class PersistedTestRun { public string jobId; public string runnerId; public string mode; public string createdUtc; public string startedUtc; public string deadlineUtc; public float progress; public string progressMessage; }

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
            TestSelection selection;
            try { selection = EditorTestCatalog.Resolve(input); }
            catch (UnityMcpValidationException failure) { return StartFailure(null, null, null, failure.ErrorCode, failure.Message, failure.StructuredContent as TestValidationError); }
            var filter = new Filter { testMode = ParseMode(selection.mode), testNames = selection.tests.Select(test => test.fullName).ToArray() };
            if (context.DryRun)
            {
                return StartOutput(selection, true, "Dry run: resolved the selected Unity Test Framework tests.");
            }

            lock (Gate)
            {
                if (active != null && !active.IsFinished)
                    return StartFailure(active.job.jobId, active.runnerId, selection, "TEST_RUN_ALREADY_ACTIVE", "A UnityMCP test run is already active. Read or cancel that job before starting another run.");
                var job = UnityMcpJobStore.Shared.Create("test");
                var api = ScriptableObject.CreateInstance<TestRunnerApi>();
                var run = new ActiveTestRun(job, api);
                try
                {
                    api.RegisterCallbacks(run);
                    active = run;
                    run.runnerId = api.Execute(new ExecutionSettings(filter));
                    if (string.IsNullOrWhiteSpace(run.runnerId)) throw new InvalidOperationException("The Unity Test Framework did not return a run identifier.");
                    UnityMcpJobStore.Shared.Start(job, "Unity Test Framework run started.");
                    TestRunRecovery.Track(job, run.runnerId, ModeName(filter.testMode));
                    return new TestRunStartOutput
                    {
                        accepted = true,
                        jobId = job.jobId,
                        runnerId = run.runnerId,
                        mode = ModeName(filter.testMode),
                        resolvedCount = selection.tests.Count,
                        explicitCount = selection.tests.Count(test => test.explicitTest),
                        selectionHash = selection.hash,
                        summary = "Unity Test Framework run queued."
                    };
                }
                catch (Exception exception)
                {
                    if (ReferenceEquals(active, run)) active = null;
                    var validation = exception as UnityMcpValidationException;
                    var code = validation == null ? "TEST_RUN_START_FAILED" : validation.ErrorCode;
                    var message = validation == null
                        ? "The Unity Test Framework run could not be started. See the local Unity Console for details."
                        : validation.Message;
                    UnityMcpJobStore.Shared.Fail(job, message);
                    TestRunRecovery.Clear(job.jobId);
                    run.Dispose();
                    return StartFailure(job.jobId, run.runnerId, selection, code, message, validation?.StructuredContent as TestValidationError);
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
                UnityMcpJobStore.Shared.Cancel(run.job.jobId, out _);
                TestRunRecovery.Clear(run.job.jobId);
                return new TestCancelOutput { cancelled = true, jobId = run.job.jobId, runnerId = run.runnerId, status = run.job.status, summary = "Cancellation requested from Unity Test Framework." };
            }
        }

        // TestRunnerApi dispatches callbacks process-wide, so a fresh API instance can
        // subscribe to the run that was already started before a PlayMode domain reload.
        // Keeping this reattachment here also makes cancellation available after reconnect.
        internal static void RecoverActiveTestRun(UnityMcpJob job, string runnerId)
        {
            lock (Gate)
            {
                if (active != null && !active.IsFinished) return;
                var api = ScriptableObject.CreateInstance<TestRunnerApi>();
                var run = new ActiveTestRun(job, api) { runnerId = runnerId };
                try
                {
                    api.RegisterCallbacks(run);
                    active = run;
                }
                catch
                {
                    if (ReferenceEquals(active, run)) active = null;
                    run.Dispose();
                    UnityMcpJobStore.Shared.Fail(job, "Unity reloaded during the PlayMode test and Unity Test Framework callbacks could not be reconnected.");
                    TestRunRecovery.Clear(job.jobId);
                }
            }
        }

        private static TestRunStartOutput StartOutput(TestSelection selection, bool dryRun, string summary)
        {
            var output = new TestRunStartOutput { dryRun = dryRun, mode = selection.mode, resolvedCount = selection.tests.Count, explicitCount = selection.tests.Count(test => test.explicitTest), selectionHash = selection.hash, summary = summary };
            output.resolvedTests = selection.tests.Select(test => new TestRunResolvedTest { id = test.id, fullName = test.fullName, categories = test.categories, @explicit = test.explicitTest }).ToList();
            return output;
        }

        private static TestRunStartOutput StartFailure(string jobId, string runnerId, TestSelection selection, string code, string message, TestValidationError details = null)
        {
            return new TestRunStartOutput
            {
                accepted = false,
                failed = true,
                jobId = jobId,
                runnerId = runnerId,
                mode = selection?.mode,
                resolvedCount = selection?.tests?.Count ?? 0,
                explicitCount = selection?.tests?.Count(test => test.explicitTest) ?? 0,
                selectionHash = selection?.hash,
                summary = message,
                error = new TestRunErrorOutput
                {
                    code = string.IsNullOrWhiteSpace(code) ? "TEST_RUN_FAILED" : code,
                    message = string.IsNullOrWhiteSpace(message) ? "UnityMCP could not start the requested test run." : message,
                    unknownTests = details?.unknownTests ?? new List<string>(),
                    explicitTests = details?.explicitTests ?? new List<string>()
                }
            };
        }

        private static TestMode ParseMode(string mode)
        {
            switch (EditorTestCatalog.NormalizeMode(mode, false))
            {
                case "editmode":
                case "edit": return TestMode.EditMode;
                case "playmode":
                case "play": return TestMode.PlayMode;
                default: throw new UnityMcpValidationException("INVALID_TEST_MODE", "mode must be editmode or playmode.");
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
            private readonly List<TestCaseResultOutput> results = new List<TestCaseResultOutput>();

            public ActiveTestRun(UnityMcpJob job, TestRunnerApi api)
            {
                this.job = job;
                this.api = api;
            }

            public void RunStarted(ITestAdaptor testsToRun)
            {
                lock (Gate)
                {
                    if (!IsFinished) UnityMcpJobStore.Shared.Start(job, "Unity Test Framework is executing tests.");
                }
            }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result)
            {
                if (result == null || result.Test == null || result.Test.IsSuite) return;
                lock (Gate)
                {
                    if (IsFinished) return;
                    results.Add(new TestCaseResultOutput
                    {
                        fullName = result.Test.FullName,
                        state = result.ResultState,
                        durationSeconds = result.Duration,
                        message = Clip(result.Message, 4096),
                        stackTrace = Clip(result.StackTrace, 16384)
                    });
                }
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                lock (Gate)
                {
                    if (IsFinished) return;
                    IsFinished = true;
                    state = result?.ResultState ?? (cancelRequested || job.IsCancellationRequested ? "Cancelled" : "Unknown");
                    passed = result?.PassCount ?? 0;
                    failed = result?.FailCount ?? 0;
                    skipped = result?.SkipCount ?? 0;
                    inconclusive = result?.InconclusiveCount ?? 0;
                    asserts = result?.AssertCount ?? 0;
                    duration = result?.Duration ?? 0d;
                    message = Clip(result?.Message, 4096);
                    finishedUtc = DateTime.UtcNow.ToString("O");
                    var output = ToOutput();
                    if (cancelRequested || job.IsCancellationRequested || state.IndexOf("cancel", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        job.result = UnityMcpResult.Success(output);
                    }
                    else if (failed > 0 || string.Equals(result?.TestStatus.ToString(), "Failed", StringComparison.Ordinal))
                    {
                        UnityMcpJobStore.Shared.Fail(job, "Unity Test Framework reported one or more failed tests.", UnityMcpResult.Success(output));
                    }
                    else UnityMcpJobStore.Shared.Complete(job, UnityMcpResult.Success(output));
                    output.status = job.status;
                    output.progress = job.progress;
                    output.progressMessage = job.progressMessage;
                    output.durationMilliseconds = job.durationMilliseconds;
                    TestRunRecovery.Clear(job.jobId);
                    Dispose();
                    if (ReferenceEquals(active, this)) active = null;
                }
            }

            public TestRunResultOutput ToOutput() => new TestRunResultOutput
            {
                jobId = job.jobId,
                jobType = job.jobType,
                runnerId = runnerId,
                status = job.status,
                progress = job.progress,
                progressMessage = job.progressMessage,
                durationMilliseconds = job.durationMilliseconds,
                resultState = state,
                passed = passed,
                failed = failed,
                skipped = skipped,
                inconclusive = inconclusive,
                asserts = asserts,
                durationSeconds = duration,
                message = message,
                finishedUtc = finishedUtc,
                results = results.OrderBy(value => value.fullName, StringComparer.Ordinal).ToList()
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

    [InitializeOnLoad]
    internal static class TestRunRecovery
    {
        private const string SessionKey = "DucMinh.UnityMcp.TestRun";

        static TestRunRecovery()
        {
            EditorApplication.delayCall += Restore;
        }

        public static void Track(UnityMcpJob job, string runnerId, string mode)
        {
            if (job == null || string.IsNullOrWhiteSpace(runnerId)) return;
            SessionState.SetString(SessionKey, JsonConvert.SerializeObject(new PersistedTestRun
            {
                jobId = job.jobId,
                runnerId = runnerId,
                mode = mode,
                createdUtc = job.createdUtc,
                startedUtc = job.startedUtc,
                deadlineUtc = DateTime.UtcNow.AddMinutes(10).ToString("O"),
                progress = job.progress,
                progressMessage = job.progressMessage
            }));
        }

        public static void Clear(string jobId)
        {
            var raw = SessionState.GetString(SessionKey, string.Empty);
            if (string.IsNullOrEmpty(raw)) return;
            try
            {
                var persisted = JsonConvert.DeserializeObject<PersistedTestRun>(raw);
                if (persisted == null || string.Equals(persisted.jobId, jobId, StringComparison.Ordinal)) SessionState.EraseString(SessionKey);
            }
            catch { SessionState.EraseString(SessionKey); }
        }

        private static void Restore()
        {
            var raw = SessionState.GetString(SessionKey, string.Empty);
            if (string.IsNullOrEmpty(raw)) return;
            PersistedTestRun persisted;
            try { persisted = JsonConvert.DeserializeObject<PersistedTestRun>(raw); }
            catch { SessionState.EraseString(SessionKey); return; }
            if (persisted == null || string.IsNullOrWhiteSpace(persisted.jobId) || string.IsNullOrWhiteSpace(persisted.runnerId) || !DateTime.TryParse(persisted.deadlineUtc, out var deadlineUtc)) { SessionState.EraseString(SessionKey); return; }
            var job = UnityMcpJobStore.Shared.Restore(persisted.jobId, "test", "running", persisted.progress, "Unity reloaded during PlayMode test; reconnecting Test Framework callbacks.", persisted.createdUtc, persisted.startedUtc);
            if (DateTime.UtcNow > deadlineUtc)
            {
                UnityMcpJobStore.Shared.Fail(job, "Timed out waiting for the PlayMode test run to reconnect after the Unity domain reload.");
                SessionState.EraseString(SessionKey);
                return;
            }
            EditorTestRunnerTools.RecoverActiveTestRun(job, persisted.runnerId);
        }
    }
}
