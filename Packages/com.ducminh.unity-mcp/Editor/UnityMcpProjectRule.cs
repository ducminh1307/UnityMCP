using System;
using System.IO;
using UnityEditor.PackageManager;

namespace DucMinh.UnityMcp.Editor
{
    internal enum UnityMcpRuleClient
    {
        AgentRules,
        Claude
    }

    /// <summary>Installs UnityMCP's managed rule into only the current project.</summary>
    internal static class UnityMcpProjectRule
    {
        internal const string AgentRulesRelativePath = ".agents/rules/unity-mcp.md";
        internal const string ClaudeRulesRelativePath = ".claude/rules/unity-mcp.md";
        internal const string ManagedMarker = "<!-- UnityMCP managed project rule. -->";

        internal static bool TryWrite(
            string projectRoot,
            UnityMcpRuleClient client,
            out string rulePath,
            out string error)
        {
            rulePath = null;
            error = null;
            try
            {
                if (string.IsNullOrWhiteSpace(projectRoot))
                    throw new ArgumentException("The Unity project root is required.", nameof(projectRoot));

                var fullProjectRoot = Path.GetFullPath(projectRoot);
                rulePath = Path.Combine(
                    fullProjectRoot,
                    RelativeRulePath(client).Replace('/', Path.DirectorySeparatorChar));

                if (File.Exists(rulePath)
                    && !File.ReadAllText(rulePath).Contains(ManagedMarker))
                    throw new InvalidOperationException(
                        RelativeRulePath(client) + " already exists and is not managed by UnityMCP. "
                        + "Rename or remove that rule before configuring UnityMCP.");

                var templatePath = GetTemplatePath();
                if (!File.Exists(templatePath))
                    throw new FileNotFoundException("The packaged UnityMCP rule template is missing.", templatePath);

                var content = File.ReadAllText(templatePath);
                if (!File.Exists(rulePath)
                    || !string.Equals(File.ReadAllText(rulePath), content, StringComparison.Ordinal))
                    UnityMcpProjectConfigFile.WriteAtomically(rulePath, content);

                return true;
            }
            catch (Exception exception)
            {
                error = "Could not write the project-local UnityMCP rule: " + exception.Message;
                return false;
            }
        }

        internal static bool IsManaged(string projectRoot, UnityMcpRuleClient client)
        {
            try
            {
                var rulePath = Path.Combine(
                    Path.GetFullPath(projectRoot),
                    RelativeRulePath(client).Replace('/', Path.DirectorySeparatorChar));
                return File.Exists(rulePath) && File.ReadAllText(rulePath).Contains(ManagedMarker);
            }
            catch
            {
                return false;
            }
        }

        private static string RelativeRulePath(UnityMcpRuleClient client)
        {
            return client == UnityMcpRuleClient.Claude ? ClaudeRulesRelativePath : AgentRulesRelativePath;
        }

        private static string GetTemplatePath()
        {
            var package = PackageInfo.FindForAssembly(typeof(UnityMcpProjectRule).Assembly)
                ?? throw new InvalidOperationException("Unity could not resolve the UnityMCP package directory.");
            return Path.Combine(package.resolvedPath, "Editor", "RuleTemplates~", "unity-mcp.md");
        }
    }
}
