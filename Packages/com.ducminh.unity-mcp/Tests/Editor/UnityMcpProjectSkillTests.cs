using System;
using System.IO;
using NUnit.Framework;

namespace DucMinh.UnityMcp.Tests
{
    public sealed class UnityMcpProjectSkillTests
    {
        [Test]
        public void TryWrite_InstallsAgentSkillOnlyInsideProject()
        {
            var projectRoot = Path.Combine(Path.GetTempPath(), "unity-mcp-agent-skill-" + Guid.NewGuid().ToString("N"));
            try
            {
                Assert.That(DucMinh.UnityMcp.Editor.UnityMcpProjectSkill.TryWrite(
                    projectRoot,
                    DucMinh.UnityMcp.Editor.UnityMcpSkillClient.AgentSkills,
                    out var skillPath,
                    out var error), Is.True, error);

                Assert.That(skillPath, Is.EqualTo(Path.Combine(projectRoot, ".agents", "skills", "unity-mcp", "SKILL.md")));
                var content = File.ReadAllText(skillPath);
                StringAssert.Contains("name: unity-mcp", content);
                StringAssert.Contains("Trigger even when the user does not mention MCP", content);
                StringAssert.Contains("call `unity-status` first", content);
                var metadataPath = Path.Combine(projectRoot, ".agents", "skills", "unity-mcp", "agents", "openai.yaml");
                Assert.That(File.Exists(metadataPath), Is.True);
                StringAssert.Contains("allow_implicit_invocation: true", File.ReadAllText(metadataPath));
                Assert.That(DucMinh.UnityMcp.Editor.UnityMcpProjectSkill.IsManaged(
                    projectRoot,
                    DucMinh.UnityMcp.Editor.UnityMcpSkillClient.AgentSkills), Is.True);
            }
            finally
            {
                if (Directory.Exists(projectRoot)) Directory.Delete(projectRoot, true);
            }
        }

        [Test]
        public void TryWrite_InstallsClaudeSkillOnlyInsideProject()
        {
            var projectRoot = Path.Combine(Path.GetTempPath(), "unity-mcp-claude-skill-" + Guid.NewGuid().ToString("N"));
            try
            {
                Assert.That(DucMinh.UnityMcp.Editor.UnityMcpProjectSkill.TryWrite(
                    projectRoot,
                    DucMinh.UnityMcp.Editor.UnityMcpSkillClient.Claude,
                    out var skillPath,
                    out var error), Is.True, error);

                Assert.That(skillPath, Is.EqualTo(Path.Combine(projectRoot, ".claude", "skills", "unity-mcp", "SKILL.md")));
                Assert.That(DucMinh.UnityMcp.Editor.UnityMcpProjectSkill.IsManaged(
                    projectRoot,
                    DucMinh.UnityMcp.Editor.UnityMcpSkillClient.Claude), Is.True);
            }
            finally
            {
                if (Directory.Exists(projectRoot)) Directory.Delete(projectRoot, true);
            }
        }

        [Test]
        public void TryWrite_RefreshesManagedSkillIdempotently()
        {
            var projectRoot = Path.Combine(Path.GetTempPath(), "unity-mcp-refresh-skill-" + Guid.NewGuid().ToString("N"));
            try
            {
                Assert.That(DucMinh.UnityMcp.Editor.UnityMcpProjectSkill.TryWrite(
                    projectRoot,
                    DucMinh.UnityMcp.Editor.UnityMcpSkillClient.AgentSkills,
                    out var skillPath,
                    out var firstError), Is.True, firstError);
                File.AppendAllText(skillPath, "\nlocal edit\n");

                Assert.That(DucMinh.UnityMcp.Editor.UnityMcpProjectSkill.TryWrite(
                    projectRoot,
                    DucMinh.UnityMcp.Editor.UnityMcpSkillClient.AgentSkills,
                    out _,
                    out var secondError), Is.True, secondError);

                StringAssert.DoesNotContain("local edit", File.ReadAllText(skillPath));
            }
            finally
            {
                if (Directory.Exists(projectRoot)) Directory.Delete(projectRoot, true);
            }
        }

        [Test]
        public void TryWrite_RefusesToOverwriteUnmanagedSkill()
        {
            var projectRoot = Path.Combine(Path.GetTempPath(), "unity-mcp-unmanaged-skill-" + Guid.NewGuid().ToString("N"));
            try
            {
                var skillPath = Path.Combine(projectRoot, ".agents", "skills", "unity-mcp", "SKILL.md");
                Directory.CreateDirectory(Path.GetDirectoryName(skillPath));
                File.WriteAllText(skillPath, "---\nname: unity-mcp\ndescription: Custom user skill.\n---\n");

                Assert.That(DucMinh.UnityMcp.Editor.UnityMcpProjectSkill.TryWrite(
                    projectRoot,
                    DucMinh.UnityMcp.Editor.UnityMcpSkillClient.AgentSkills,
                    out _,
                    out var error), Is.False);
                StringAssert.Contains("is not managed by UnityMCP", error);
                StringAssert.Contains("Custom user skill", File.ReadAllText(skillPath));
            }
            finally
            {
                if (Directory.Exists(projectRoot)) Directory.Delete(projectRoot, true);
            }
        }
    }
}
