using System;
using DucMinh.UnityMcp.Editor;
using NUnit.Framework;

namespace DucMinh.UnityMcp.Tests
{
    public sealed class UnityMcpGatewayInstallGuideTests
    {
        [Test]
        public void Create_PowerShellSourceMissing_UsesRecommendedPathsAndClone()
        {
            var guide = UnityMcpGatewayInstallGuide.Create(
                @"C:\Users\Test User\AppData\Local",
                UnityMcpInstallShell.PowerShell,
                false);

            Assert.That(guide.SourcePath, Is.EqualTo(@"C:\Users\Test User\AppData\Local\UnityMCP\source"));
            Assert.That(guide.VirtualEnvironmentPath, Is.EqualTo(@"C:\Users\Test User\AppData\Local\UnityMCP\venv"));
            Assert.That(guide.ExecutablePath, Is.EqualTo(@"C:\Users\Test User\AppData\Local\UnityMCP\venv\Scripts\unity-mcp.exe"));
            Assert.That(guide.IncludesClone, Is.True);
            Assert.That(guide.Commands, Does.Contain("git clone --depth 1 --branch main --single-branch"));
            Assert.That(guide.Commands, Does.Contain("'C:\\Users\\Test User\\AppData\\Local\\UnityMCP\\source'"));
            Assert.That(guide.Commands, Does.Contain("py -3 -m venv"));
            Assert.That(guide.Commands, Does.Contain("& 'C:\\Users\\Test User\\AppData\\Local\\UnityMCP\\venv\\Scripts\\python.exe' -m pip install -e"));
            Assert.That(guide.Commands, Does.Not.Contain("UNITY_MCP_HTTP_TOKEN"));
            Assert.That(guide.Commands, Does.Not.Contain("Authorization"));
        }

        [Test]
        public void Create_PosixSourceMissing_UsesUnixPathsAndQuotesSpaces()
        {
            var guide = UnityMcpGatewayInstallGuide.Create(
                "/Users/Test User/Library/Application Support",
                UnityMcpInstallShell.Posix,
                false);

            Assert.That(guide.SourcePath, Is.EqualTo("/Users/Test User/Library/Application Support/UnityMCP/source"));
            Assert.That(guide.VirtualEnvironmentPath, Is.EqualTo("/Users/Test User/Library/Application Support/UnityMCP/venv"));
            Assert.That(guide.ExecutablePath, Is.EqualTo("/Users/Test User/Library/Application Support/UnityMCP/venv/bin/unity-mcp"));
            Assert.That(guide.IncludesClone, Is.True);
            Assert.That(guide.Commands, Does.Contain("git clone --depth 1 --branch main --single-branch"));
            Assert.That(guide.Commands, Does.Contain("'/Users/Test User/Library/Application Support/UnityMCP/source'"));
            Assert.That(guide.Commands, Does.Contain("python3 -m venv '/Users/Test User/Library/Application Support/UnityMCP/venv'"));
            Assert.That(guide.Commands, Does.Contain("'/Users/Test User/Library/Application Support/UnityMCP/venv/bin/python' -m pip install -e"));
        }

        [Test]
        public void Create_SourceExists_DoesNotCloneAgain()
        {
            var guide = UnityMcpGatewayInstallGuide.Create(
                @"C:\Users\Test User\AppData\Local",
                UnityMcpInstallShell.PowerShell,
                true);

            Assert.That(guide.IncludesClone, Is.False);
            Assert.That(guide.Commands, Does.Not.Contain("git clone"));
            Assert.That(guide.Commands, Does.Contain("py -3 -m venv"));
            Assert.That(guide.Commands, Does.Contain("-m pip install -e"));
        }

        [Test]
        public void MissingExecutableStatus_IsDistinctFromOrdinaryErrors()
        {
            const string executablePath = @"C:\Users\Test User\AppData\Local\UnityMCP\venv\Scripts\unity-mcp.exe";
            var installation = UnityMcpGatewayHost.CreateMissingExecutableStatus(executablePath);
            var ordinaryError = UnityMcpGatewayHost.CreateErrorStatus("Port unavailable.");

            Assert.That(installation.State, Is.EqualTo(UnityMcpGatewayState.Error));
            Assert.That(installation.RequiresInstallation, Is.True);
            Assert.That(installation.ExpectedExecutablePath, Is.EqualTo(executablePath));
            Assert.That(ordinaryError.State, Is.EqualTo(UnityMcpGatewayState.Error));
            Assert.That(ordinaryError.RequiresInstallation, Is.False);
            Assert.That(ordinaryError.ExpectedExecutablePath, Is.Null);
        }

        [Test]
        public void RestartPolicy_RetriesSameEndpointWithExponentialBackoff()
        {
            var policy = new UnityMcpGatewayRestartPolicy();
            var now = new DateTime(2026, 8, 23, 0, 0, 0, DateTimeKind.Utc);

            Assert.That(policy.Schedule(now, 8765, "/mcp", "gateway exited"), Is.True);
            Assert.That(policy.TryBeginAttempt(now, out _, out _), Is.False);
            Assert.That(policy.TryBeginAttempt(now.AddSeconds(1), out var firstPort, out var firstPath), Is.True);
            Assert.That(firstPort, Is.EqualTo(8765));
            Assert.That(firstPath, Is.EqualTo("/mcp"));
            Assert.That(policy.AttemptCount, Is.EqualTo(1));

            Assert.That(policy.Schedule(now.AddSeconds(1), 8765, "/mcp", "port still busy"), Is.True);
            Assert.That(policy.NextAttemptUtc, Is.EqualTo(now.AddSeconds(3)));
            Assert.That(policy.TryBeginAttempt(now.AddSeconds(2), out _, out _), Is.False);
            Assert.That(policy.TryBeginAttempt(now.AddSeconds(3), out var secondPort, out var secondPath), Is.True);
            Assert.That(secondPort, Is.EqualTo(8765));
            Assert.That(secondPath, Is.EqualTo("/mcp"));
            Assert.That(policy.AttemptCount, Is.EqualTo(2));
        }

        [Test]
        public void RestartPolicy_StopsAfterBoundedAttempts()
        {
            var policy = new UnityMcpGatewayRestartPolicy();
            var now = new DateTime(2026, 8, 23, 0, 0, 0, DateTimeKind.Utc);

            for (var attempt = 0; attempt < UnityMcpGatewayRestartPolicy.MaxAttempts; attempt++)
            {
                Assert.That(policy.Schedule(now, 8765, "/mcp", "failure " + attempt), Is.True);
                now = policy.NextAttemptUtc;
                Assert.That(policy.TryBeginAttempt(now, out _, out _), Is.True);
            }

            Assert.That(policy.Schedule(now, 8765, "/mcp", "still failing"), Is.False);
            Assert.That(policy.Pending, Is.False);
        }

        [Test]
        public void RestartPolicy_StableRunResetsCrashBudget()
        {
            var policy = new UnityMcpGatewayRestartPolicy();
            var now = new DateTime(2026, 8, 23, 0, 0, 0, DateTimeKind.Utc);
            policy.Schedule(now, 8765, "/mcp", "failure");
            policy.TryBeginAttempt(policy.NextAttemptUtc, out _, out _);
            policy.MarkRunning(now);

            policy.ObserveRunning(now + UnityMcpGatewayRestartPolicy.StableRunWindow - TimeSpan.FromMilliseconds(1));
            Assert.That(policy.AttemptCount, Is.EqualTo(1));
            policy.ObserveRunning(now + UnityMcpGatewayRestartPolicy.StableRunWindow);
            Assert.That(policy.AttemptCount, Is.Zero);
            Assert.That(policy.LastError, Is.Null);
        }

        [Test]
        public void HealthPolicy_RequiresThreeConsecutiveFailuresAndHealthyProbeResetsCount()
        {
            var policy = new UnityMcpGatewayHealthPolicy();

            Assert.That(policy.Observe(false), Is.False);
            Assert.That(policy.Observe(false), Is.False);
            Assert.That(policy.ConsecutiveFailures, Is.EqualTo(2));
            Assert.That(policy.Observe(true), Is.False);
            Assert.That(policy.ConsecutiveFailures, Is.Zero);
            Assert.That(policy.Observe(false), Is.False);
            Assert.That(policy.Observe(false), Is.False);
            Assert.That(policy.Observe(false), Is.True);
            Assert.That(policy.ConsecutiveFailures, Is.EqualTo(UnityMcpGatewayHealthPolicy.FailureThreshold));
        }

        [Test]
        public void HealthPolicy_ExplicitResetClearsFailureCount()
        {
            var policy = new UnityMcpGatewayHealthPolicy();
            policy.Observe(false);
            policy.Observe(false);

            policy.Reset();

            Assert.That(policy.ConsecutiveFailures, Is.Zero);
            Assert.That(policy.Observe(false), Is.False);
        }

        [Test]
        public void ReadyEvent_JsonPayloadMustMatchOwnedPort()
        {
            Assert.That(
                UnityMcpGatewayHost.IsReadyEventForPort("UNITY_MCP_READY {\"port\":8765}", 8765),
                Is.True);
            Assert.That(
                UnityMcpGatewayHost.IsReadyEventForPort("UNITY_MCP_READY {\"port\":8766}", 8765),
                Is.False);
            Assert.That(
                UnityMcpGatewayHost.IsReadyEventForPort("UNITY_MCP_READY {not-json}", 8765),
                Is.False);
        }

        [Test]
        public void ReadyEvent_LegacyPayloadRemainsCompatible()
        {
            Assert.That(UnityMcpGatewayHost.IsReadyEventForPort("UNITY_MCP_READY legacy", 8765), Is.True);
        }

        [TestCase("HTTP/1.1 401 Unauthorized", true)]
        [TestCase("HTTP/1.0 404 Not Found", true)]
        [TestCase("HTTP/2 200", false)]
        [TestCase("NOT_HTTP", false)]
        public void HttpHealthResponse_RequiresHttp1StatusPrefix(string response, bool expected)
        {
            Assert.That(UnityMcpGatewayHost.IsHttpResponsePrefix(response), Is.EqualTo(expected));
        }
    }
}
