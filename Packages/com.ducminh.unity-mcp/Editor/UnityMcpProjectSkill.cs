using System;
using System.IO;
using UnityEditor.PackageManager;

namespace DucMinh.UnityMcp.Editor
{
    internal enum UnityMcpSkillClient
    {
        AgentSkills,
        Claude
    }

    /// <summary>Installs UnityMCP's managed skill into only the current project.</summary>
    internal static class UnityMcpProjectSkill
    {
        internal const string AgentSkillsRelativePath = ".agents/skills/unity-mcp";
        internal const string ClaudeRelativePath = ".claude/skills/unity-mcp";
        internal const string ManagedMarker = "<!-- UnityMCP managed project skill. -->";

        private static readonly string[] TemplateFiles =
        {
            "SKILL.md",
            "agents/openai.yaml"
        };

        internal static bool TryWrite(
            string projectRoot,
            UnityMcpSkillClient client,
            out string skillPath,
            out string error)
        {
            skillPath = null;
            error = null;
            try
            {
                if (string.IsNullOrWhiteSpace(projectRoot))
                    throw new ArgumentException("The Unity project root is required.", nameof(projectRoot));

                var fullProjectRoot = Path.GetFullPath(projectRoot);
                var targetDirectory = Path.Combine(
                    fullProjectRoot,
                    RelativeSkillPath(client).Replace('/', Path.DirectorySeparatorChar));
                skillPath = Path.Combine(targetDirectory, "SKILL.md");
                if (File.Exists(skillPath)
                    && !File.ReadAllText(skillPath).Contains(ManagedMarker))
                    throw new InvalidOperationException(
                        RelativeSkillPath(client) + "/SKILL.md already exists and is not managed by UnityMCP. "
                        + "Rename or remove that skill before configuring UnityMCP.");

                var templateDirectory = GetTemplateDirectory();
                foreach (var relativeFile in TemplateFiles)
                {
                    var sourcePath = Path.Combine(templateDirectory, relativeFile.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(sourcePath))
                        throw new FileNotFoundException("The packaged UnityMCP skill template is incomplete.", sourcePath);
                    var targetPath = Path.Combine(targetDirectory, relativeFile.Replace('/', Path.DirectorySeparatorChar));
                    var content = File.ReadAllText(sourcePath);
                    if (!File.Exists(targetPath)
                        || !string.Equals(File.ReadAllText(targetPath), content, StringComparison.Ordinal))
                        UnityMcpProjectConfigFile.WriteAtomically(targetPath, content);
                }
                return true;
            }
            catch (Exception exception)
            {
                error = "Could not write the project-local UnityMCP skill: " + exception.Message;
                return false;
            }
        }

        internal static bool IsManaged(string projectRoot, UnityMcpSkillClient client)
        {
            try
            {
                var skillPath = Path.Combine(
                    Path.GetFullPath(projectRoot),
                    RelativeSkillPath(client).Replace('/', Path.DirectorySeparatorChar),
                    "SKILL.md");
                return File.Exists(skillPath) && File.ReadAllText(skillPath).Contains(ManagedMarker);
            }
            catch
            {
                return false;
            }
        }

        private static string RelativeSkillPath(UnityMcpSkillClient client)
        {
            return client == UnityMcpSkillClient.Claude ? ClaudeRelativePath : AgentSkillsRelativePath;
        }

        private static string GetTemplateDirectory()
        {
            var package = PackageInfo.FindForAssembly(typeof(UnityMcpProjectSkill).Assembly)
                ?? throw new InvalidOperationException("Unity could not resolve the UnityMCP package directory.");
            return Path.Combine(package.resolvedPath, "Editor", "SkillTemplates~", "unity-mcp");
        }
    }
}
