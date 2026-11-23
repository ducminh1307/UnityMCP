using System;
using System.IO;
using DucMinh.UnityMcp.Editor;
using NUnit.Framework;

namespace DucMinh.UnityMcp.Tests
{
    public sealed class UnityMcpProjectRuleTests
    {
        [Test]
        public void TryWrite_InstallsAgentRuleWithUnityCliAndMcpGuidance()
        {
            var projectRoot = Path.Combine(Path.GetTempPath(), "unity-mcp-agent-rule-" + Guid.NewGuid().ToString("N"));
            try
            {
                Assert.That(UnityMcpProjectRule.TryWrite(
                    projectRoot,
                    UnityMcpRuleClient.AgentRules,
                    out var rulePath,
                    out var error), Is.True, error);

                Assert.That(rulePath, Is.EqualTo(Path.Combine(Path.GetFullPath(projectRoot), ".agents", "rules", "unity-mcp.md")));
                var content = File.ReadAllText(rulePath);
                StringAssert.Contains("always_on: true", content);
                StringAssert.Contains("Prioritize UnityMCP First", content);
                StringAssert.Contains("Fallback Protocol", content);
                StringAssert.Contains(UnityMcpProjectRule.ManagedMarker, content);
                StringAssert.Contains("unity status --json", content);
                StringAssert.Contains("unity command <group> <name>", content);
                StringAssert.Contains("unity recompile", content);
                StringAssert.Contains("unity test", content);
                StringAssert.Contains("unity mcp --tool-groups", content);
                StringAssert.DoesNotContain("127.0.0.1", content);
                StringAssert.DoesNotContain("Bearer", content);
                StringAssert.DoesNotContain("unity-status", content);
                Assert.That(UnityMcpProjectRule.IsManaged(projectRoot, UnityMcpRuleClient.AgentRules), Is.True);
            }
            finally
            {
                if (Directory.Exists(projectRoot))
                    Directory.Delete(projectRoot, true);
            }
        }

        [Test]
        public void TryWrite_InstallsClaudeRuleOnlyInsideProject()
        {
            var projectRoot = Path.Combine(Path.GetTempPath(), "unity-mcp-claude-rule-" + Guid.NewGuid().ToString("N"));
            try
            {
                Assert.That(UnityMcpProjectRule.TryWrite(
                    projectRoot,
                    UnityMcpRuleClient.Claude,
                    out var rulePath,
                    out var error), Is.True, error);

                Assert.That(rulePath, Is.EqualTo(Path.Combine(Path.GetFullPath(projectRoot), ".claude", "rules", "unity-mcp.md")));
                Assert.That(UnityMcpProjectRule.IsManaged(projectRoot, UnityMcpRuleClient.Claude), Is.True);
            }
            finally
            {
                if (Directory.Exists(projectRoot))
                    Directory.Delete(projectRoot, true);
            }
        }

        [Test]
        public void TryWrite_RefreshesManagedRuleIdempotently_AndRefusesUnmanagedRule()
        {
            var projectRoot = Path.Combine(Path.GetTempPath(), "unity-mcp-refresh-rule-" + Guid.NewGuid().ToString("N"));
            try
            {
                Assert.That(UnityMcpProjectRule.TryWrite(
                    projectRoot,
                    UnityMcpRuleClient.AgentRules,
                    out var rulePath,
                    out var firstError), Is.True, firstError);
                File.AppendAllText(rulePath, "\nlocal edit\n");

                Assert.That(UnityMcpProjectRule.TryWrite(
                    projectRoot,
                    UnityMcpRuleClient.AgentRules,
                    out _,
                    out var secondError), Is.True, secondError);
                StringAssert.DoesNotContain("local edit", File.ReadAllText(rulePath));

                var claudeRulePath = Path.Combine(projectRoot, ".claude", "rules", "unity-mcp.md");
                Directory.CreateDirectory(Path.GetDirectoryName(claudeRulePath));
                File.WriteAllText(claudeRulePath, "---\ndescription: Custom user rule.\n---\n");

                Assert.That(UnityMcpProjectRule.TryWrite(
                    projectRoot,
                    UnityMcpRuleClient.Claude,
                    out _,
                    out var unmanagedError), Is.False);
                StringAssert.Contains("is not managed by UnityMCP", unmanagedError);
                StringAssert.Contains("Custom user rule", File.ReadAllText(claudeRulePath));
            }
            finally
            {
                if (Directory.Exists(projectRoot))
                    Directory.Delete(projectRoot, true);
            }
        }

        [Test]
        public void TryWriteAntigravityContext_PreservesUserInstructions_AndWritesCliGuidance()
        {
            var projectRoot = Path.Combine(Path.GetTempPath(), "unity-mcp-antigravity-context-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(projectRoot);
                var contextPath = Path.Combine(Path.GetFullPath(projectRoot), "AGENTS.md");
                File.WriteAllText(contextPath, "# Project instructions\n\nKeep existing guidance.\n");

                Assert.That(UnityMcpAntigravityContext.TryWrite(
                    projectRoot, out var writtenPath, out var error), Is.True, error);
                Assert.That(writtenPath, Is.EqualTo(contextPath));

                var content = File.ReadAllText(contextPath);
                StringAssert.Contains("Keep existing guidance.", content);
                StringAssert.Contains("UnityMCP live-Unity workflow", content);
                StringAssert.Contains(UnityMcpAntigravityContext.StartMarker, content);
                StringAssert.Contains(UnityMcpAntigravityContext.EndMarker, content);
                StringAssert.Contains("unity status", content);
                StringAssert.Contains("unity command <group> <name>", content);
                StringAssert.Contains("unity mcp --tool-groups", content);
                StringAssert.DoesNotContain("127.0.0.1", content);
                Assert.That(UnityMcpAntigravityContext.IsManaged(projectRoot), Is.True);

                File.WriteAllText(contextPath, content.Replace("UnityMCP live-Unity workflow", "stale local edit"));
                Assert.That(UnityMcpAntigravityContext.TryWrite(
                    projectRoot, out _, out error), Is.True, error);
                StringAssert.Contains("UnityMCP live-Unity workflow", File.ReadAllText(contextPath));
                StringAssert.DoesNotContain("stale local edit", File.ReadAllText(contextPath));

                File.WriteAllText(contextPath, UnityMcpAntigravityContext.StartMarker + "\nbroken");
                Assert.That(UnityMcpAntigravityContext.TryWrite(projectRoot, out _, out var brokenError), Is.False);
                StringAssert.Contains("incomplete UnityMCP managed context block", brokenError);
            }
            finally
            {
                if (Directory.Exists(projectRoot))
                    Directory.Delete(projectRoot, true);
            }
        }
    }
}
