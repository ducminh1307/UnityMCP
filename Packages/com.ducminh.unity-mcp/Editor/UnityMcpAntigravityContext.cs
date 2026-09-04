using System;
using System.IO;

namespace DucMinh.UnityMcp.Editor
{
    /// <summary>Adds UnityMCP guidance to Antigravity's persistent workspace context.</summary>
    internal static class UnityMcpAntigravityContext
    {
        internal const string RelativePath = "AGENTS.md";
        internal const string StartMarker = "<!-- UnityMCP Antigravity context: start -->";
        internal const string EndMarker = "<!-- UnityMCP Antigravity context: end -->";

        private const string ManagedBlock = StartMarker + "\n"
            + "## UnityMCP live-Unity workflow\n\n"
            + "For every task that queries, diagnoses, verifies, or changes Unity, invoke the appropriate project-scoped UnityMCP tool (`unity_*`) before using terminal commands, searching serialized project files, or inferring live Editor state.\n\n"
            + "Use terminal or project-file inspection only when the UnityMCP gateway is unreachable, the required tool is unavailable, or that tool returns an error. State the reason before falling back. After a Unity-related change, use UnityMCP to check compilation, console output, or the relevant test/live state.\n"
            + EndMarker + "\n";

        internal static bool TryWrite(string projectRoot, out string contextPath, out string error)
        {
            contextPath = null;
            error = null;
            try
            {
                if (string.IsNullOrWhiteSpace(projectRoot))
                    throw new ArgumentException("The Unity project root is required.", nameof(projectRoot));

                contextPath = Path.Combine(Path.GetFullPath(projectRoot), RelativePath);
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
                var path = Path.Combine(Path.GetFullPath(projectRoot), RelativePath);
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
