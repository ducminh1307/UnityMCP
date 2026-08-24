using System;
using DucMinh.UnityMcp.Editor;
using NUnit.Framework;

namespace DucMinh.UnityMcp.Tests
{
    public sealed class EditorWorkflowRecoveryTests
    {
        private string compileJobId;

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrEmpty(compileJobId)) CompileRequestRecovery.Clear(compileJobId);
        }

        [Test]
        public void CompileRequestRecovery_RoundTripsReloadStateWithoutSubmittingAgain()
        {
            var original = UnityMcpJobStore.Shared.Create("compile");
            compileJobId = original.jobId;
            UnityMcpJobStore.Shared.Start(original, "Script compilation requested; waiting for Unity to become idle.");
            UnityMcpJobStore.Shared.Report(original, 0.25f);
            var requestedUtc = DateTime.UtcNow.AddSeconds(-1);
            var deadlineUtc = requestedUtc.AddMinutes(10);

            CompileRequestRecovery.Track(original, requestedUtc, deadlineUtc);

            Assert.That(CompileRequestRecovery.TryRead(out var persisted), Is.True);
            Assert.That(persisted.jobId, Is.EqualTo(original.jobId));
            Assert.That(CompileRequestRecovery.TryCreateRestoredState(persisted, out var restored, out var operation), Is.True);
            Assert.That(restored.jobId, Is.EqualTo(original.jobId));
            Assert.That(restored.jobType, Is.EqualTo("compile"));
            Assert.That(restored.status, Is.EqualTo("running"));
            Assert.That(restored.progress, Is.EqualTo(0.25f));
            Assert.That(operation.RequestAlreadySubmitted, Is.True);
            Assert.That(operation.DeadlineUtc, Is.EqualTo(deadlineUtc).Within(TimeSpan.FromMilliseconds(1)));
            Assert.Throws<InvalidOperationException>(() => CompileRequestRecovery.EnsureNoActiveRequest());
        }

        [Test]
        public void LightingBakeCancellation_RejectsJobOwnedByAnotherTool()
        {
            var otherJob = UnityMcpJobStore.Shared.Create("test");

            var exception = Assert.Throws<ArgumentException>(() =>
                EditorLightingBakeTools.RequireActiveLightingBakeJob(otherJob.jobId));

            Assert.That(exception.Message, Does.Contain("lighting bake job"));
            Assert.That(otherJob.status, Is.EqualTo("queued"));
            Assert.That(otherJob.IsCancellationRequested, Is.False);
        }

        [Test]
        public void LightingBakeCancellation_RejectsTerminalOwnedJob()
        {
            var lightingJob = UnityMcpJobStore.Shared.Create("lighting-bake", true);
            UnityMcpJobStore.Shared.Complete(lightingJob, UnityMcpResult.Success());

            var exception = Assert.Throws<InvalidOperationException>(() =>
                EditorLightingBakeTools.RequireActiveLightingBakeJob(lightingJob.jobId));

            Assert.That(exception.Message, Does.Contain("no longer active"));
        }

        [Test]
        public void JobCancellation_RejectsJobThatDoesNotDeclareSupport()
        {
            var buildJob = UnityMcpJobStore.Shared.Create("build-player");

            Assert.That(UnityMcpJobStore.Shared.Cancel(buildJob.jobId, out var returned), Is.False);
            Assert.That(returned, Is.SameAs(buildJob));
            Assert.That(buildJob.status, Is.EqualTo("queued"));
            Assert.That(buildJob.IsCancellationRequested, Is.False);
            Assert.That(buildJob.CanCancel, Is.False);
        }

        [Test]
        public void JobCancellation_CancelsOnlyDeclaredActiveJob()
        {
            var testJob = UnityMcpJobStore.Shared.Create("test", true);

            Assert.That(testJob.CanCancel, Is.True);
            Assert.That(UnityMcpJobStore.Shared.Cancel(testJob.jobId, out var returned), Is.True);
            Assert.That(returned, Is.SameAs(testJob));
            Assert.That(testJob.status, Is.EqualTo("cancelled"));
            Assert.That(testJob.IsCancellationRequested, Is.True);
            Assert.That(testJob.CanCancel, Is.False);
            Assert.That(UnityMcpJobStore.Shared.Cancel(testJob.jobId, out _), Is.False);
        }
    }
}
