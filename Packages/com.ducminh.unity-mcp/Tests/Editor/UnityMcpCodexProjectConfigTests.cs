using System;
using System.IO;
using DucMinh.UnityMcp.Editor;
using NUnit.Framework;

namespace DucMinh.UnityMcp.Tests
{
    public sealed class UnityMcpCodexProjectConfigTests
    {
        [Test]
        public void Merge_AppendsManagedStdioServer_AndPreservesOtherSettings()
        {
            const string existing = "model = \"gpt-test\"\n\n[mcp_servers.docs]\nurl = \"https://example.test/mcp\"\n";
            var projectRoot = Path.Combine(Path.GetTempPath(), "unity-mcp-codex-merge-" + Guid.NewGuid().ToString("N"));
            var normalizedRoot = UnityMcpProjectConfigFile.NormalizeProjectRoot(projectRoot);

            var result = UnityMcpCodexProjectConfig.Merge(
                existing,
                projectRoot,
                "unity",
                "unity");

            StringAssert.Contains("model = \"gpt-test\"", result);
            StringAssert.Contains("[mcp_servers.docs]", result);
            StringAssert.Contains("# UnityMCP managed project server: unity", result);
            StringAssert.Contains("[mcp_servers.unity]", result);
            StringAssert.Contains("command = \"unity\"", result);
            StringAssert.Contains(
                "args = [\"mcp\", \"--tool-groups\", \"--project-path\", \"" + EscapeToml(normalizedRoot) + "\"]",
                result);
            StringAssert.DoesNotContain("Bearer", result);
            StringAssert.DoesNotContain("http://", result);
        }

        [Test]
        public void Merge_ReplacesLegacyHttpFieldsWithStdio_WithoutDuplicatingServer()
        {
            const string existing = "# UnityMCP managed project server: unity_sample\n"
                + "[mcp_servers.unity_sample]\n"
                + "url = \"http://127.0.0.1:8765/mcp\"\n"
                + "enabled = true\n"
                + "http_headers = { Authorization = \"Bearer old-token\" }\n"
                + "args = [\n"
                + "  \"old-arg\"\n"
                + "]\n\n"
                + "[history]\npersistence = \"save-all\"\n";
            var projectRoot = Path.Combine(Path.GetTempPath(), "unity-mcp-codex-replace-" + Guid.NewGuid().ToString("N"));
            var normalizedRoot = UnityMcpProjectConfigFile.NormalizeProjectRoot(projectRoot);

            var result = UnityMcpCodexProjectConfig.Merge(
                existing,
                projectRoot,
                "unity_sample",
                @"C:\Program Files\Unity\unity.exe");

            Assert.That(Count(result, "[mcp_servers.unity_sample]"), Is.EqualTo(1));
            Assert.That(Count(result, "command = "), Is.EqualTo(1));
            Assert.That(Count(result, "args = "), Is.EqualTo(1));
            StringAssert.Contains("enabled = true", result);
            StringAssert.Contains("[history]", result);
            StringAssert.Contains("command = \"C:\\\\Program Files\\\\Unity\\\\unity.exe\"", result);
            StringAssert.Contains(EscapeToml(normalizedRoot), result);
            StringAssert.DoesNotContain("url =", result);
            StringAssert.DoesNotContain("http_headers", result);
            StringAssert.DoesNotContain("old-token", result);
            StringAssert.DoesNotContain("old-arg", result);
        }

        [Test]
        public void TryWrite_CreatesProjectConfig_WithoutSecretsOrGitExcludeWrites()
        {
            var projectRoot = Path.Combine(Path.GetTempPath(), "unity-mcp-codex-config-" + Guid.NewGuid().ToString("N"));
            try
            {
                var excludePath = Path.Combine(projectRoot, ".git", "info", "exclude");
                Directory.CreateDirectory(Path.GetDirectoryName(excludePath));
                File.WriteAllText(excludePath, "# existing git exclude\n");

                var success = UnityMcpCodexProjectConfig.TryWrite(
                    projectRoot,
                    "unity",
                    "unity",
                    out var configPath,
                    out var error);

                Assert.That(success, Is.True, error);
                Assert.That(configPath, Is.EqualTo(Path.Combine(Path.GetFullPath(projectRoot), ".codex", "config.toml")));
                var content = File.ReadAllText(configPath);
                StringAssert.Contains("[mcp_servers.unity]", content);
                StringAssert.Contains("command = \"unity\"", content);
                StringAssert.Contains("\"mcp\", \"--tool-groups\", \"--project-path\"", content);
                StringAssert.DoesNotContain("Bearer", content);
                StringAssert.DoesNotContain("http://", content);
                Assert.That(File.ReadAllText(excludePath), Is.EqualTo("# existing git exclude\n"));
                Assert.That(UnityMcpCodexProjectConfig.IsManaged(projectRoot, "unity"), Is.True);
            }
            finally
            {
                if (Directory.Exists(projectRoot))
                    Directory.Delete(projectRoot, true);
            }
        }

        [Test]
        public void TryWrite_UpdatesExistingConfig_Idempotently()
        {
            var projectRoot = Path.Combine(Path.GetTempPath(), "unity-mcp-codex-update-" + Guid.NewGuid().ToString("N"));
            try
            {
                Assert.That(UnityMcpCodexProjectConfig.TryWrite(
                    projectRoot,
                    "unity",
                    "old-unity",
                    out _,
                    out var firstError), Is.True, firstError);

                Assert.That(UnityMcpCodexProjectConfig.TryWrite(
                    projectRoot,
                    "unity",
                    "new-unity",
                    out var configPath,
                    out var secondError), Is.True, secondError);

                var result = File.ReadAllText(configPath);
                Assert.That(Count(result, "[mcp_servers.unity]"), Is.EqualTo(1));
                StringAssert.Contains("command = \"new-unity\"", result);
                StringAssert.DoesNotContain("old-unity", result);
            }
            finally
            {
                if (Directory.Exists(projectRoot))
                    Directory.Delete(projectRoot, true);
            }
        }

        [Test]
        public void Merge_RejectsDuplicateTargetTables_AndInvalidServerNames()
        {
            const string existing = "[mcp_servers.unity]\ncommand = \"one\"\n\n[mcp_servers.unity]\ncommand = \"two\"\n";
            var projectRoot = Path.Combine(Path.GetTempPath(), "unity-mcp-codex-dup-" + Guid.NewGuid().ToString("N"));

            Assert.Throws<InvalidDataException>(() => UnityMcpCodexProjectConfig.Merge(
                existing,
                projectRoot,
                "unity",
                "unity"));

            Assert.Throws<ArgumentException>(() => UnityMcpCodexProjectConfig.Merge(
                string.Empty,
                projectRoot,
                "invalid server!",
                "unity"));
        }

        [Test]
        public void EnsureSafeProjectPath_RejectsOutsideRoot()
        {
            var projectRoot = Path.Combine(Path.GetTempPath(), "unity-mcp-codex-safe-" + Guid.NewGuid().ToString("N"));
            var outsidePath = Path.GetFullPath(Path.Combine(projectRoot, "..", "outside.toml"));

            Assert.Throws<InvalidOperationException>(() =>
                UnityMcpProjectConfigFile.EnsureSafeProjectPath(projectRoot, outsidePath));
        }

        private static string EscapeToml(string value) =>
            value.Replace("\\", "\\\\").Replace("\"", "\\\"");

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
