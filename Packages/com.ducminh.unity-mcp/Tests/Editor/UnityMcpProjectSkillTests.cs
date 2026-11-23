using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using DucMinh.UnityMcp.Editor;
using Newtonsoft.Json;
using NUnit.Framework;

namespace DucMinh.UnityMcp.Tests
{
    public sealed class UnityMcpProjectSkillTests
    {
        [Test]
        public void TryWrite_InstallsAgentSkillWithUnityCliAndMcpContent()
        {
            var projectRoot = Path.Combine(Path.GetTempPath(), "unity-mcp-agent-skill-" + Guid.NewGuid().ToString("N"));
            try
            {
                Assert.That(UnityMcpProjectSkill.TryWrite(
                    projectRoot,
                    UnityMcpSkillClient.AgentSkills,
                    out var skillPath,
                    out var error), Is.True, error);

                var fullRoot = Path.GetFullPath(projectRoot);
                Assert.That(skillPath, Is.EqualTo(Path.Combine(fullRoot, ".agents", "skills", "unity-mcp", "SKILL.md")));

                var content = File.ReadAllText(skillPath);
                StringAssert.Contains("name: unity-mcp", content);
                StringAssert.Contains("Trigger even when the user does not mention MCP", content);
                StringAssert.Contains(UnityMcpProjectSkill.ManagedMarker, content);
                StringAssert.Contains("unity status --json", content);
                StringAssert.Contains("unity command <group> <name>", content);
                StringAssert.Contains("unity mcp --tool-groups --project-path", content);
                StringAssert.Contains("ui_ai", content);
                StringAssert.DoesNotContain("unity-status", content);

                var metadataPath = Path.Combine(fullRoot, ".agents", "skills", "unity-mcp", "agents", "openai.yaml");
                Assert.That(File.Exists(metadataPath), Is.True);
                StringAssert.Contains("allow_implicit_invocation: true", File.ReadAllText(metadataPath));
                Assert.That(UnityMcpProjectSkill.IsManaged(projectRoot, UnityMcpSkillClient.AgentSkills), Is.True);
            }
            finally
            {
                if (Directory.Exists(projectRoot))
                    Directory.Delete(projectRoot, true);
            }
        }

        [Test]
        public void TryWrite_InstallsClaudeSkill_RefreshesIdempotently_AndRefusesUnmanaged()
        {
            var projectRoot = Path.Combine(Path.GetTempPath(), "unity-mcp-claude-skill-" + Guid.NewGuid().ToString("N"));
            try
            {
                Assert.That(UnityMcpProjectSkill.TryWrite(
                    projectRoot,
                    UnityMcpSkillClient.Claude,
                    out var skillPath,
                    out var error), Is.True, error);

                Assert.That(skillPath, Is.EqualTo(Path.Combine(Path.GetFullPath(projectRoot), ".claude", "skills", "unity-mcp", "SKILL.md")));
                Assert.That(UnityMcpProjectSkill.IsManaged(projectRoot, UnityMcpSkillClient.Claude), Is.True);

                File.AppendAllText(skillPath, "\nlocal edit\n");
                Assert.That(UnityMcpProjectSkill.TryWrite(
                    projectRoot,
                    UnityMcpSkillClient.Claude,
                    out _,
                    out var refreshError), Is.True, refreshError);
                StringAssert.DoesNotContain("local edit", File.ReadAllText(skillPath));

                var agentSkillPath = Path.Combine(projectRoot, ".agents", "skills", "unity-mcp", "SKILL.md");
                Directory.CreateDirectory(Path.GetDirectoryName(agentSkillPath));
                File.WriteAllText(agentSkillPath, "---\nname: unity-mcp\ndescription: Custom user skill.\n---\n");

                Assert.That(UnityMcpProjectSkill.TryWrite(
                    projectRoot,
                    UnityMcpSkillClient.AgentSkills,
                    out _,
                    out var unmanagedError), Is.False);
                StringAssert.Contains("is not managed by UnityMCP", unmanagedError);
                StringAssert.Contains("Custom user skill", File.ReadAllText(agentSkillPath));
            }
            finally
            {
                if (Directory.Exists(projectRoot))
                    Directory.Delete(projectRoot, true);
            }
        }

        [Test]
        public void UnityCliDiscovery_ReadPipelineStatus_ParsesStatusAndNeverExposesEvalToken()
        {
            var projectRoot = Path.Combine(Path.GetTempPath(), "unity-mcp-pipeline-status-" + Guid.NewGuid().ToString("N"));
            try
            {
                // 1. Missing descriptor
                var missingStatus = UnityCliDiscovery.ReadPipelineStatus(projectRoot);
                Assert.That(missingStatus.DescriptorExists, Is.False);
                Assert.That(missingStatus.IsRunning, Is.False);

                // 2. Valid descriptor containing evalToken must never store or serialize evalToken
                var pipelineDir = Path.Combine(projectRoot, "Library", "Pipeline");
                Directory.CreateDirectory(pipelineDir);
                var descriptorPath = Path.Combine(pipelineDir, ".unity-pipeline-port");
                var currentPid = Process.GetCurrentProcess().Id;
                const string secretToken = "secret-eval-token-must-never-leak";
                File.WriteAllText(descriptorPath, "{\n"
                    + $"  \"pid\": {currentPid},\n"
                    + "  \"port\": 52345,\n"
                    + "  \"unityVersion\": \"6000.6.5f1\",\n"
                    + "  \"mode\": \"editor\",\n"
                    + "  \"lastHeartbeat\": \"2026-11-22T15:00:00Z\",\n"
                    + $"  \"evalToken\": \"{secretToken}\",\n"
                    + "  \"nestedSecret\": { \"token\": \"nested-secret\" }\n"
                    + "}\n");

                var status = UnityCliDiscovery.ReadPipelineStatus(projectRoot);
                Assert.That(status.DescriptorExists, Is.True);
                Assert.That(status.IsRunning, Is.True);
                Assert.That(status.Pid, Is.EqualTo(currentPid));
                Assert.That(status.Port, Is.EqualTo(52345));
                Assert.That(status.UnityVersion, Is.EqualTo("6000.6.5f1"));
                Assert.That(status.Mode, Is.EqualTo("editor"));
                Assert.That(status.LastHeartbeatUtc, Is.Not.Null);
                Assert.That(status.Error, Is.Null);

                var statusMembers = typeof(PipelineServerStatus)
                    .GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .Select(m => m.Name)
                    .ToArray();
                Assert.That(statusMembers.Any(n => n.IndexOf("token", StringComparison.OrdinalIgnoreCase) >= 0), Is.False,
                    "PipelineServerStatus must never define any token field or property.");

                var serialized = JsonConvert.SerializeObject(status);
                StringAssert.DoesNotContain(secretToken, serialized);
                StringAssert.DoesNotContain("evalToken", serialized);
                StringAssert.DoesNotContain("nested-secret", serialized);

                // 3. Malformed JSON descriptor reports Error without throwing
                File.WriteAllText(descriptorPath, "{ malformed json");
                var malformedStatus = UnityCliDiscovery.ReadPipelineStatus(projectRoot);
                Assert.That(malformedStatus.DescriptorExists, Is.True);
                Assert.That(malformedStatus.IsRunning, Is.False);
                Assert.That(malformedStatus.Error, Is.Not.Null.And.Not.Empty);
            }
            finally
            {
                if (Directory.Exists(projectRoot))
                    Directory.Delete(projectRoot, true);
            }
        }

        [Test]
        public void UnityCliDiscovery_DiscoverExtensionCommands_ExcludesBuiltInPipelineAndFindsExtensions()
        {
            var discovered = UnityCliDiscovery.DiscoverExtensionCommands(forceRefresh: true);
            Assert.That(discovered, Is.Not.Null.And.Not.Empty);

            foreach (var command in discovered)
            {
                Assert.That(UnityCliDiscovery.IsBuiltInPipelineAssembly(command.AssemblyName), Is.False,
                    $"Built-in pipeline assembly '{command.AssemblyName}' leaked into extension discovery.");
                Assert.That(command.FullName, Is.Not.Null.And.Not.Empty);
                Assert.That(command.CliInvocation, Does.StartWith("unity command "));
            }

            var fullNames = discovered.Select(c => c.FullName).ToArray();
            Assert.That(fullNames, Does.Contain("particle create"));
            Assert.That(fullNames, Does.Contain("physics_ext raycast"));
            Assert.That(fullNames, Does.Contain("navmesh_ext calculate_path"));
            Assert.That(fullNames, Does.Contain("uitoolkit scan"));
            Assert.That(fullNames, Does.Contain("ugui raycast"));
            Assert.That(fullNames, Does.Contain("scene_ext validate"));
            Assert.That(fullNames, Does.Contain("asset_ext get_dependencies"));
            Assert.That(fullNames, Does.Contain("render_ext get_settings"));
            Assert.That(fullNames, Does.Contain("visual_ext capture_gameobject"));
            Assert.That(fullNames, Does.Contain("checkpoint create"));
        }

        [Test]
        public void UnityCliDiscovery_HelperMethods_ParseAndResolveCleanly()
        {
            Assert.That(UnityCliDiscovery.TryParseVersionOutput("\n  1.0.1  \n", out var version), Is.True);
            Assert.That(version, Is.EqualTo("1.0.1"));
            Assert.That(UnityCliDiscovery.TryParseVersionOutput("   ", out _), Is.False);
            Assert.That(UnityCliDiscovery.ResolveUnityCommand("  /custom/unity  "), Is.EqualTo("/custom/unity"));
            Assert.That(UnityCliDiscovery.GetInstallCommand(), Is.Not.Null.And.Not.Empty);
            Assert.That(UnityCliDiscovery.DeriveCommandName("GetUIElement"), Is.EqualTo("get_ui_element"));
        }
    }
}
