using System;
using System.IO;

namespace DucMinh.UnityMcp.Editor
{
    /// <summary>Adds Unity CLI and UnityMCP guidance to Antigravity's persistent workspace context.</summary>
    internal static class UnityMcpAntigravityContext
    {
        internal const string RelativePath = "AGENTS.md";
        internal const string StartMarker = "<!-- UnityMCP Antigravity context: start -->";
        internal const string EndMarker = "<!-- UnityMCP Antigravity context: end -->";

        private const string ManagedBlock = StartMarker + "\n"
            + "## UnityMCP live-Unity workflow\n\n"
            + "For every task that queries, diagnoses, verifies, or changes Unity:\n\n"
            + "- **When a shell is available**, prefer the official **Unity CLI** (`unity status`, `unity command <group> <name>`, `unity recompile`, `unity test`, `unity build`) backed by `com.unity.pipeline` and `com.ducminh.unity-mcp` extension commands before inspecting raw serialized files or inferring live Editor state.\n"
            + "- **When using MCP** (or when shell access is unavailable), invoke the project-scoped `unity` MCP server (`unity mcp --tool-groups`).\n"
            + "- Use project-file fallback only when no Editor/Pipeline server is running, the CLI/MCP command is unavailable, or the invocation returns an error. State the reason before falling back. After Unity changes, verify compilation (`unity recompile`), console output, or tests (`unity test`) via Unity CLI or MCP.\n"
            + EndMarker + "\n";

        internal static bool TryWrite(string projectRoot, out string contextPath, out string error)
        {
            contextPath = null;
            error = null;
            try
            {
                var fullProjectRoot = UnityMcpProjectConfigFile.NormalizeProjectRoot(projectRoot);
                contextPath = Path.Combine(fullProjectRoot, RelativePath);
                UnityMcpProjectConfigFile.EnsureSafeProjectPath(fullProjectRoot, contextPath);

                var existing = File.Exists(contextPath) ? File.ReadAllText(contextPath) : string.Empty;
                var updated = UpsertManagedBlock(existing);
                if (!string.Equals(existing, updated, StringComparison.Ordinal))
                    UnityMcpProjectConfigFile.WriteAtomically(contextPath, updated);
                return true;
            }
            catch (Exception exception)
            {
                error = "Could not write Antigravity's persistent UnityMCP workspace context: " + exception.Message;
                return false;
            }
        }

        internal static bool IsManaged(string projectRoot)
        {
            try
            {
                var path = Path.Combine(UnityMcpProjectConfigFile.NormalizeProjectRoot(projectRoot), RelativePath);
                return File.Exists(path) && File.ReadAllText(path).Contains(StartMarker);
            }
            catch { return false; }
        }

        private static string UpsertManagedBlock(string existing)
        {
            var start = existing.IndexOf(StartMarker, StringComparison.Ordinal);
            var end = existing.IndexOf(EndMarker, StringComparison.Ordinal);
            if (start >= 0 || end >= 0)
            {
                if (start < 0 || end < start)
                    throw new InvalidDataException("AGENTS.md contains an incomplete UnityMCP managed context block. Restore or remove its markers before configuring UnityMCP.");

                end += EndMarker.Length;
                if (end < existing.Length && existing[end] == '\r') end++;
                if (end < existing.Length && existing[end] == '\n') end++;
                return existing.Substring(0, start) + ManagedBlock + existing.Substring(end);
            }

            if (existing.Length == 0) return ManagedBlock;
            return existing + (existing.EndsWith("\n", StringComparison.Ordinal) ? "\n" : "\n\n") + ManagedBlock;
        }
    }
}
