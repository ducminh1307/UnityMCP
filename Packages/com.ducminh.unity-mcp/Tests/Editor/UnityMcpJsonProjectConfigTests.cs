using System;
using System.Diagnostics;
using System.IO;
using DucMinh.UnityMcp.Editor;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace DucMinh.UnityMcp.Tests
{
    public sealed class UnityMcpJsonProjectConfigTests
    {
        [Test]
        public void Merge_AntigravityAndClaudeWriteStdioEntry_AndPreserveOtherServers()
        {
            const string existing = "{\n"
                + "  \"theme\": \"dark\",\n"
                + "  \"mcpServers\": {\n"
                + "    \"docs\": { \"serverUrl\": \"https://example.test/mcp\" }\n"
                + "  }\n"
                + "}\n";
            var projectRoot = Path.Combine(Path.GetTempPath(), "unity-mcp-json-merge-" + Guid.NewGuid().ToString("N"));
            var normalizedRoot = UnityMcpProjectConfigFile.NormalizeProjectRoot(projectRoot);

            var result = UnityMcpJsonProjectConfig.Merge(
                existing,
                projectRoot,
                UnityMcpJsonProjectConfig.DefaultServerName,
                "unity",
                UnityMcpJsonClient.Antigravity);

            var root = JObject.Parse(result);
            Assert.That((string)root["theme"], Is.EqualTo("dark"));
            Assert.That((string)root["mcpServers"]?["docs"]?["serverUrl"], Is.EqualTo("https://example.test/mcp"));

            var server = root["mcpServers"]?["unity"] as JObject;
            Assert.That(server, Is.Not.Null);
            Assert.That((string)server["command"], Is.EqualTo("unity"));
            Assert.That(server["args"]?.ToObject<string[]>(), Is.EqualTo(new[]
            {
                "mcp", "--tool-groups", "--project-path", normalizedRoot
            }));
            Assert.That(server["serverUrl"], Is.Null);
            Assert.That(server["url"], Is.Null);
            Assert.That(server["httpUrl"], Is.Null);
            Assert.That(server["headers"], Is.Null);
            StringAssert.DoesNotContain("Bearer", result);
        }

        [Test]
        public void Merge_StripsLegacyHttpFields_AndPreservesExtraCustomFields()
        {
            const string existing = "{\n"
                + "  \"mcpServers\": {\n"
                + "    \"unity\": {\n"
                + "      \"type\": \"http\",\n"
                + "      \"url\": \"http://127.0.0.1:8765/mcp\",\n"
                + "      \"serverUrl\": \"http://127.0.0.1:8765/mcp\",\n"
                + "      \"httpUrl\": \"http://127.0.0.1:8765/mcp\",\n"
                + "      \"timeout\": 15,\n"
                + "      \"headers\": { \"Authorization\": \"Bearer old-token\" }\n"
                + "    }\n"
                + "  }\n"
                + "}\n";
            var projectRoot = Path.Combine(Path.GetTempPath(), "unity-mcp-json-strip-" + Guid.NewGuid().ToString("N"));
            var normalizedRoot = UnityMcpProjectConfigFile.NormalizeProjectRoot(projectRoot);

            var result = UnityMcpJsonProjectConfig.Merge(
                existing,
                projectRoot,
                "unity",
                @"C:\Unity\bin\unity.exe",
                UnityMcpJsonClient.Claude);

            var server = JObject.Parse(result)["mcpServers"]?["unity"] as JObject;
            Assert.That(server, Is.Not.Null);
            Assert.That((string)server["command"], Is.EqualTo(@"C:\Unity\bin\unity.exe"));
            Assert.That(server["args"]?.ToObject<string[]>(), Is.EqualTo(new[]
            {
                "mcp", "--tool-groups", "--project-path", normalizedRoot
            }));
            Assert.That((int?)server["timeout"], Is.EqualTo(15));
            Assert.That(server["type"], Is.Null);
            Assert.That(server["url"], Is.Null);
            Assert.That(server["serverUrl"], Is.Null);
            Assert.That(server["httpUrl"], Is.Null);
            Assert.That(server["headers"], Is.Null);
            StringAssert.DoesNotContain("old-token", result);
            StringAssert.DoesNotContain("127.0.0.1", result);
        }

        [Test]
        public void TryWrite_CreatesOnlyProjectScopedFiles_WithoutModifyingGitExclude()
        {
            var projectRoot = Path.Combine(Path.GetTempPath(), "unity-mcp-json-config-" + Guid.NewGuid().ToString("N"));
            try
            {
                var excludePath = Path.Combine(projectRoot, ".git", "info", "exclude");
                Directory.CreateDirectory(Path.GetDirectoryName(excludePath));
                File.WriteAllText(excludePath, "# keep untouched\n");

                Assert.That(UnityMcpJsonProjectConfig.TryWrite(
                    projectRoot,
                    "unity",
                    "unity",
                    UnityMcpJsonClient.Antigravity,
                    out var antigravityPath,
                    out var antigravityError), Is.True, antigravityError);
                Assert.That(UnityMcpJsonProjectConfig.TryWrite(
                    projectRoot,
                    "unity",
                    "unity",
                    UnityMcpJsonClient.Claude,
                    out var claudePath,
                    out var claudeError), Is.True, claudeError);

                var fullRoot = UnityMcpProjectConfigFile.NormalizeProjectRoot(projectRoot);
                Assert.That(antigravityPath, Is.EqualTo(Path.Combine(fullRoot, ".agents", "mcp_config.json")));
                Assert.That(claudePath, Is.EqualTo(Path.Combine(fullRoot, ".mcp.json")));
                Assert.That(UnityMcpJsonProjectConfig.IsManaged(projectRoot, UnityMcpJsonClient.Antigravity), Is.True);
                Assert.That(UnityMcpJsonProjectConfig.IsManaged(projectRoot, UnityMcpJsonClient.Claude), Is.True);
                Assert.That(File.ReadAllText(excludePath), Is.EqualTo("# keep untouched\n"));
            }
            finally
            {
                if (Directory.Exists(projectRoot))
                    Directory.Delete(projectRoot, true);
            }
        }

        [Test]
        public void BuildSnippetJson_And_MergeShapeValidation_BehaveAsExpected()
        {
            var projectRoot = Path.Combine(Path.GetTempPath(), "unity-mcp-json-snippet-" + Guid.NewGuid().ToString("N"));
            var snippet = UnityMcpJsonProjectConfig.BuildSnippetJson(projectRoot, "unity", "unity");
            var parsed = JObject.Parse(snippet);
            var server = parsed["mcpServers"]?["unity"];
            Assert.That((string)server?["command"], Is.EqualTo("unity"));
            Assert.That(server?["args"]?.ToObject<string[]>(), Is.EqualTo(UnityMcpJsonProjectConfig.BuildMcpArgs(projectRoot)));

            Assert.Throws<InvalidDataException>(() => UnityMcpJsonProjectConfig.Merge(
                "{ \"mcpServers\": [] }",
                projectRoot,
                "unity",
                "unity",
                UnityMcpJsonClient.Claude));
        }

        [Test]
        public void EnsureSafeProjectPath_RejectsSymlinkOrJunctionInsideProject()
        {
            var baseTemp = Path.Combine(Path.GetTempPath(), "unity-mcp-symlink-test-" + Guid.NewGuid().ToString("N"));
            var projectRoot = Path.Combine(baseTemp, "Project");
            var outsideDir = Path.Combine(baseTemp, "Outside");
            var linkedAgentsDir = Path.Combine(projectRoot, ".agents");

            try
            {
                Directory.CreateDirectory(projectRoot);
                Directory.CreateDirectory(outsideDir);

                if (!TryCreateDirectoryLink(linkedAgentsDir, outsideDir))
                    Assert.Ignore("OS environment does not permit creating directory links/junctions without elevation.");

                var targetFile = Path.Combine(linkedAgentsDir, "mcp_config.json");
                Assert.Throws<InvalidOperationException>(() =>
                    UnityMcpProjectConfigFile.EnsureSafeProjectPath(projectRoot, targetFile));

                Assert.That(UnityMcpJsonProjectConfig.TryWrite(
                    projectRoot,
                    "unity",
                    "unity",
                    UnityMcpJsonClient.Antigravity,
                    out _,
                    out var error), Is.False);
                StringAssert.Contains("symbolic link", error);
            }
            finally
            {
                if (Directory.Exists(linkedAgentsDir))
                {
                    try { Directory.Delete(linkedAgentsDir, false); } catch { }
                }
                if (Directory.Exists(baseTemp))
                {
                    try { Directory.Delete(baseTemp, true); } catch { }
                }
            }
        }

        private static bool TryCreateDirectoryLink(string linkPath, string targetDir)
        {
            try
            {
                if (Path.DirectorySeparatorChar == '\\')
                {
                    var psi = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{linkPath}\" \"{targetDir}\"")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    using (var proc = Process.Start(psi))
                    {
                        proc?.WaitForExit(5000);
                        if (proc != null && proc.ExitCode == 0 && Directory.Exists(linkPath))
                            return true;
                    }
                    return false;
                }

                var unixPsi = new ProcessStartInfo("ln", $"-s \"{targetDir}\" \"{linkPath}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (var unixProc = Process.Start(unixPsi))
                {
                    unixProc?.WaitForExit(5000);
                    return unixProc != null && unixProc.ExitCode == 0 && Directory.Exists(linkPath);
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
