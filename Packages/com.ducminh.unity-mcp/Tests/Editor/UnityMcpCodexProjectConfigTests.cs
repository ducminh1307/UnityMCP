using System;
using System.IO;
using NUnit.Framework;

namespace DucMinh.UnityMcp.Tests
{
    public sealed class UnityMcpCodexProjectConfigTests
    {
        [Test]
        public void Merge_AppendsManagedServer_AndPreservesOtherSettings()
        {
            const string existing = "model = \"gpt-test\"\n\n[mcp_servers.docs]\nurl = \"https://example.test/mcp\"\n";

            var result = DucMinh.UnityMcp.Editor.UnityMcpCodexProjectConfig.Merge(
                existing,
                "unity_sample_123456",
                "http://127.0.0.1:8988/mcp",
                "secret-token");

            StringAssert.Contains("model = \"gpt-test\"", result);
            StringAssert.Contains("[mcp_servers.docs]", result);
            StringAssert.Contains("# UnityMCP managed project server: unity_sample_123456", result);
            StringAssert.Contains("[mcp_servers.unity_sample_123456]", result);
            StringAssert.Contains("url = \"http://127.0.0.1:8988/mcp\"", result);
            StringAssert.Contains("Authorization = \"Bearer secret-token\"", result);
        }

        [Test]
        public void Merge_UpdatesOnlyManagedValues_WithoutDuplicatingServer()
        {
            const string existing = "# UnityMCP managed project server: unity_sample_123456\n"
                + "[mcp_servers.unity_sample_123456]\n"
                + "url = \"http://127.0.0.1:8765/mcp\"\n"
                + "enabled = true\n"
                + "http_headers = { Authorization = \"Bearer old-token\" }\n\n"
                + "[history]\npersistence = \"save-all\"\n";

            var result = DucMinh.UnityMcp.Editor.UnityMcpCodexProjectConfig.Merge(
                existing,
                "unity_sample_123456",
                "http://127.0.0.1:8988/mcp",
                "new-token");

            Assert.That(Count(result, "[mcp_servers.unity_sample_123456]"), Is.EqualTo(1));
            Assert.That(Count(result, "url = "), Is.EqualTo(1));
            Assert.That(Count(result, "http_headers = "), Is.EqualTo(1));
            StringAssert.Contains("enabled = true", result);
            StringAssert.Contains("[history]", result);
            StringAssert.DoesNotContain("old-token", result);
        }

        [Test]
        public void TryWrite_CreatesProjectConfig_AndLocalGitExclude()
        {
            var projectRoot = Path.Combine(Path.GetTempPath(), "unity-mcp-codex-config-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(projectRoot, ".git", "info"));

                var success = DucMinh.UnityMcp.Editor.UnityMcpCodexProjectConfig.TryWrite(
                    projectRoot,
                    "unity_sample_123456",
                    "http://127.0.0.1:8988/mcp",
                    "secret-token",
                    out var configPath,
                    out var error);

                Assert.That(success, Is.True, error);
                Assert.That(configPath, Is.EqualTo(Path.Combine(projectRoot, ".codex", "config.toml")));
                StringAssert.Contains("[mcp_servers.unity_sample_123456]", File.ReadAllText(configPath));
                StringAssert.Contains("/.codex/config.toml", File.ReadAllText(Path.Combine(projectRoot, ".git", "info", "exclude")));
                Assert.That(DucMinh.UnityMcp.Editor.UnityMcpCodexProjectConfig.IsManaged(projectRoot, "unity_sample_123456"), Is.True);
            }
            finally
            {
                if (Directory.Exists(projectRoot)) Directory.Delete(projectRoot, true);
            }
        }

        [Test]
        public void TryWrite_UpdatesExistingConfig_Idempotently()
        {
            var projectRoot = Path.Combine(Path.GetTempPath(), "unity-mcp-codex-update-" + Guid.NewGuid().ToString("N"));
            try
            {
                Assert.That(DucMinh.UnityMcp.Editor.UnityMcpCodexProjectConfig.TryWrite(
                    projectRoot,
                    "unity_sample_123456",
                    "http://127.0.0.1:8765/mcp",
                    "old-token",
                    out _,
                    out var firstError), Is.True, firstError);

                Assert.That(DucMinh.UnityMcp.Editor.UnityMcpCodexProjectConfig.TryWrite(
                    projectRoot,
                    "unity_sample_123456",
                    "http://127.0.0.1:8988/mcp",
                    "new-token",
                    out var configPath,
                    out var secondError), Is.True, secondError);

                var result = File.ReadAllText(configPath);
                Assert.That(Count(result, "[mcp_servers.unity_sample_123456]"), Is.EqualTo(1));
                StringAssert.Contains("http://127.0.0.1:8988/mcp", result);
                StringAssert.Contains("new-token", result);
                StringAssert.DoesNotContain("old-token", result);
            }
            finally
            {
                if (Directory.Exists(projectRoot)) Directory.Delete(projectRoot, true);
            }
        }

        [Test]
        public void Merge_RejectsDuplicateTargetTables()
        {
            const string existing = "[mcp_servers.unity_sample]\nurl = \"one\"\n\n[mcp_servers.unity_sample]\nurl = \"two\"\n";

            Assert.Throws<InvalidDataException>(() => DucMinh.UnityMcp.Editor.UnityMcpCodexProjectConfig.Merge(
                existing,
                "unity_sample",
                "http://127.0.0.1:8988/mcp",
                "secret-token"));
        }

        private static int Count(string value, string needle)
        {
            var count = 0;
            var offset = 0;
            while ((offset = value.IndexOf(needle, offset, StringComparison.Ordinal)) >= 0)
            {
                count++;
                offset += needle.Length;
            }
            return count;
        }
    }
}
