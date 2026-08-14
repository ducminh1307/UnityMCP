using System;
using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace DucMinh.UnityMcp.Tests
{
    public sealed class UnityMcpJsonProjectConfigTests
    {
        [Test]
        public void Merge_AntigravityUsesServerUrl_AndPreservesOtherServers()
        {
            const string existing = "{\n"
                + "  \"theme\": \"dark\",\n"
                + "  \"mcpServers\": {\n"
                + "    \"docs\": { \"serverUrl\": \"https://example.test/mcp\" }\n"
                + "  }\n"
                + "}\n";

            var result = DucMinh.UnityMcp.Editor.UnityMcpJsonProjectConfig.Merge(
                existing,
                "unity_sample_123456",
                "http://127.0.0.1:8988/mcp",
                "secret-token",
                DucMinh.UnityMcp.Editor.UnityMcpJsonClient.Antigravity);

            var root = JObject.Parse(result);
            Assert.That((string)root["theme"], Is.EqualTo("dark"));
            Assert.That((string)root["mcpServers"]?["docs"]?["serverUrl"], Is.EqualTo("https://example.test/mcp"));
            var server = root["mcpServers"]?["unity_sample_123456"];
            Assert.That((string)server?["serverUrl"], Is.EqualTo("http://127.0.0.1:8988/mcp"));
            Assert.That(server?["url"], Is.Null);
            Assert.That((string)server?["headers"]?["Authorization"], Is.EqualTo("Bearer secret-token"));
        }

        [Test]
        public void Merge_ClaudeUsesHttpUrl_AndUpdatesOnlyOwnedFields()
        {
            const string existing = "{\n"
                + "  \"mcpServers\": {\n"
                + "    \"unity_sample_123456\": {\n"
                + "      \"type\": \"sse\",\n"
                + "      \"url\": \"http://127.0.0.1:8765/mcp\",\n"
                + "      \"timeout\": 15,\n"
                + "      \"headers\": { \"Authorization\": \"Bearer old-token\", \"X-Test\": \"kept\" }\n"
                + "    }\n"
                + "  }\n"
                + "}\n";

            var result = DucMinh.UnityMcp.Editor.UnityMcpJsonProjectConfig.Merge(
                existing,
                "unity_sample_123456",
                "http://127.0.0.1:8988/mcp",
                "new-token",
                DucMinh.UnityMcp.Editor.UnityMcpJsonClient.Claude);

            var server = JObject.Parse(result)["mcpServers"]?["unity_sample_123456"];
            Assert.That((string)server?["type"], Is.EqualTo("http"));
            Assert.That((string)server?["url"], Is.EqualTo("http://127.0.0.1:8988/mcp"));
            Assert.That((int?)server?["timeout"], Is.EqualTo(15));
            Assert.That((string)server?["headers"]?["Authorization"], Is.EqualTo("Bearer new-token"));
            Assert.That((string)server?["headers"]?["X-Test"], Is.EqualTo("kept"));
            StringAssert.DoesNotContain("old-token", result);
        }

        [Test]
        public void TryWrite_CreatesOnlyProjectScopedFiles_AndLocalGitExcludes()
        {
            var projectRoot = Path.Combine(Path.GetTempPath(), "unity-mcp-json-config-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(projectRoot, ".git", "info"));

                Assert.That(DucMinh.UnityMcp.Editor.UnityMcpJsonProjectConfig.TryWrite(
                    projectRoot,
                    "unity_sample_123456",
                    "http://127.0.0.1:8988/mcp",
                    "secret-token",
                    DucMinh.UnityMcp.Editor.UnityMcpJsonClient.Antigravity,
                    out var antigravityPath,
                    out var antigravityError), Is.True, antigravityError);
                Assert.That(DucMinh.UnityMcp.Editor.UnityMcpJsonProjectConfig.TryWrite(
                    projectRoot,
                    "unity_sample_123456",
                    "http://127.0.0.1:8988/mcp",
                    "secret-token",
                    DucMinh.UnityMcp.Editor.UnityMcpJsonClient.Claude,
                    out var claudePath,
                    out var claudeError), Is.True, claudeError);

                Assert.That(antigravityPath, Is.EqualTo(Path.Combine(projectRoot, ".agents", "mcp_config.json")));
                Assert.That(claudePath, Is.EqualTo(Path.Combine(projectRoot, ".mcp.json")));
                Assert.That(DucMinh.UnityMcp.Editor.UnityMcpJsonProjectConfig.IsManaged(
                    projectRoot,
                    "unity_sample_123456",
                    DucMinh.UnityMcp.Editor.UnityMcpJsonClient.Antigravity), Is.True);
                Assert.That(DucMinh.UnityMcp.Editor.UnityMcpJsonProjectConfig.IsManaged(
                    projectRoot,
                    "unity_sample_123456",
                    DucMinh.UnityMcp.Editor.UnityMcpJsonClient.Claude), Is.True);

                var exclude = File.ReadAllText(Path.Combine(projectRoot, ".git", "info", "exclude"));
                StringAssert.Contains("/.agents/mcp_config.json", exclude);
                StringAssert.Contains("/.mcp.json", exclude);
            }
            finally
            {
                if (Directory.Exists(projectRoot)) Directory.Delete(projectRoot, true);
            }
        }

        [Test]
        public void Merge_RejectsInvalidMcpServersShape()
        {
            Assert.Throws<InvalidDataException>(() => DucMinh.UnityMcp.Editor.UnityMcpJsonProjectConfig.Merge(
                "{ \"mcpServers\": [] }",
                "unity_sample",
                "http://127.0.0.1:8988/mcp",
                "secret-token",
                DucMinh.UnityMcp.Editor.UnityMcpJsonClient.Claude));
        }
    }
}
