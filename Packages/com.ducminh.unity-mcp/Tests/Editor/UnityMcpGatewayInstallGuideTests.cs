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
    }
}
