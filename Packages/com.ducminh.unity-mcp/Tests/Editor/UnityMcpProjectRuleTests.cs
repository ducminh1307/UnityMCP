using System;
using System.IO;
using NUnit.Framework;

namespace DucMinh.UnityMcp.Tests
{
    public sealed class UnityMcpProjectRuleTests
    {
        [Test]
        public void TryWrite_InstallsAgentRuleOnlyInsideProject()
        {
            var projectRoot = Path.Combine(Path.GetTempPath(), "unity-mcp-agent-rule-" + Guid.NewGuid().ToString("N"));
            try
            {
                Assert.That(DucMinh.UnityMcp.Editor.UnityMcpProjectRule.TryWrite(
                    projectRoot,
                    DucMinh.UnityMcp.Editor.UnityMcpRuleClient.AgentRules,
                    out var rulePath,
                    out var error), Is.True, error);

                Assert.That(rulePath, Is.EqualTo(Path.Combine(projectRoot, ".agents", "rules", "unity-mcp.md")));
                var content = File.ReadAllText(rulePath);
                StringAssert.Contains("always_on: true", content);
                StringAssert.Contains("Prioritize UnityMCP First", content);
                StringAssert.Contains("Fallback Protocol", content);
                StringAssert.Contains("<!-- UnityMCP managed project rule. -->", content);
                Assert.That(DucMinh.UnityMcp.Editor.UnityMcpProjectRule.IsManaged(
                    projectRoot,
                    DucMinh.UnityMcp.Editor.UnityMcpRuleClient.AgentRules), Is.True);
            }
            finally
            {
                if (Directory.Exists(projectRoot)) Directory.Delete(projectRoot, true);
            }
        }

        [Test]
        public void TryWrite_InstallsClaudeRuleOnlyInsideProject()
        {
            var projectRoot = Path.Combine(Path.GetTempPath(), "unity-mcp-claude-rule-" + Guid.NewGuid().ToString("N"));
            try
            {
                Assert.That(DucMinh.UnityMcp.Editor.UnityMcpProjectRule.TryWrite(
                    projectRoot,
                    DucMinh.UnityMcp.Editor.UnityMcpRuleClient.Claude,
                    out var rulePath,
                    out var error), Is.True, error);

                Assert.That(rulePath, Is.EqualTo(Path.Combine(projectRoot, ".claude", "rules", "unity-mcp.md")));
                Assert.That(DucMinh.UnityMcp.Editor.UnityMcpProjectRule.IsManaged(
                    projectRoot,
                    DucMinh.UnityMcp.Editor.UnityMcpRuleClient.Claude), Is.True);
            }
            finally
            {
                if (Directory.Exists(projectRoot)) Directory.Delete(projectRoot, true);
            }
        }

        [Test]
        public void TryWrite_RefreshesManagedRuleIdempotently()
        {
            var projectRoot = Path.Combine(Path.GetTempPath(), "unity-mcp-refresh-rule-" + Guid.NewGuid().ToString("N"));
            try
            {
                Assert.That(DucMinh.UnityMcp.Editor.UnityMcpProjectRule.TryWrite(
                    projectRoot,
                    DucMinh.UnityMcp.Editor.UnityMcpRuleClient.AgentRules,
                    out var rulePath,
                    out var firstError), Is.True, firstError);
                File.AppendAllText(rulePath, "\nlocal edit\n");

                Assert.That(DucMinh.UnityMcp.Editor.UnityMcpProjectRule.TryWrite(
                    projectRoot,
                    DucMinh.UnityMcp.Editor.UnityMcpRuleClient.AgentRules,
                    out _,
                    out var secondError), Is.True, secondError);

                StringAssert.DoesNotContain("local edit", File.ReadAllText(rulePath));
            }
            finally
            {
                if (Directory.Exists(projectRoot)) Directory.Delete(projectRoot, true);
            }
        }

        [Test]
        public void TryWrite_RefusesToOverwriteUnmanagedRule()
        {
            var projectRoot = Path.Combine(Path.GetTempPath(), "unity-mcp-unmanaged-rule-" + Guid.NewGuid().ToString("N"));
            try
            {
                var rulePath = Path.Combine(projectRoot, ".agents", "rules", "unity-mcp.md");
                Directory.CreateDirectory(Path.GetDirectoryName(rulePath));
                File.WriteAllText(rulePath, "---\ndescription: Custom user rule.\n---\n");

                Assert.That(DucMinh.UnityMcp.Editor.UnityMcpProjectRule.TryWrite(
                    projectRoot,
                    DucMinh.UnityMcp.Editor.UnityMcpRuleClient.AgentRules,
                    out _,
                    out var error), Is.False);
                StringAssert.Contains("is not managed by UnityMCP", error);
                StringAssert.Contains("Custom user rule", File.ReadAllText(rulePath));
            }
            finally
            {
                if (Directory.Exists(projectRoot)) Directory.Delete(projectRoot, true);
            }
        }

        [Test]
        public void TryWriteAntigravityContext_PreservesUserInstructions_AndRefreshesManagedBlock()
        {
            var projectRoot = Path.Combine(Path.GetTempPath(), "unity-mcp-antigravity-context-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(projectRoot);
                var contextPath = Path.Combine(projectRoot, "AGENTS.md");
                File.WriteAllText(contextPath, "# Project instructions\n\nKeep existing guidance.\n");

                Assert.That(DucMinh.UnityMcp.Editor.UnityMcpAntigravityContext.TryWrite(
                    projectRoot, out var writtenPath, out var error), Is.True, error);
                Assert.That(writtenPath, Is.EqualTo(contextPath));
                var content = File.ReadAllText(contextPath);
                StringAssert.Contains("Keep existing guidance.", content);
                StringAssert.Contains("UnityMCP live-Unity workflow", content);
                StringAssert.Contains("UnityMCP Antigravity context: start", content);
                Assert.That(DucMinh.UnityMcp.Editor.UnityMcpAntigravityContext.IsManaged(projectRoot), Is.True);

                File.WriteAllText(contextPath, content.Replace("UnityMCP live-Unity workflow", "stale local edit"));
                Assert.That(DucMinh.UnityMcp.Editor.UnityMcpAntigravityContext.TryWrite(
                    projectRoot, out _, out error), Is.True, error);
                StringAssert.Contains("UnityMCP live-Unity workflow", File.ReadAllText(contextPath));
                StringAssert.DoesNotContain("stale local edit", File.ReadAllText(contextPath));
            }
            finally
            {
                if (Directory.Exists(projectRoot)) Directory.Delete(projectRoot, true);
            }
        }
    }
}
