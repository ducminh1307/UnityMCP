using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DucMinh.UnityMcp.Editor
{
    /// <summary>
    /// Owns the project-scoped Codex MCP stdio entry written to <c>.codex/config.toml</c>.
    /// </summary>
    internal static class UnityMcpCodexProjectConfig
    {
        internal const string DefaultServerName = "unity";
        internal const string RelativeConfigPath = ".codex/config.toml";

        internal static bool TryWrite(
            string projectRoot,
            out string configPath,
            out string error)
        {
            return TryWrite(projectRoot, DefaultServerName, null, out configPath, out error);
        }

        internal static bool TryWrite(
            string projectRoot,
            string serverName,
            string unityCommand,
            out string configPath,
            out string error)
        {
            configPath = null;
            error = null;
            try
            {
                var fullProjectRoot = UnityMcpProjectConfigFile.NormalizeProjectRoot(projectRoot);
                ValidateServerName(serverName);
                var resolvedCommand = string.IsNullOrWhiteSpace(unityCommand)
                    ? UnityCliDiscovery.ResolveUnityCommand()
                    : unityCommand.Trim();

                configPath = Path.Combine(fullProjectRoot, ".codex", "config.toml");
                UnityMcpProjectConfigFile.EnsureSafeProjectPath(fullProjectRoot, configPath);

                Directory.CreateDirectory(Path.GetDirectoryName(configPath) ?? fullProjectRoot);

                var existing = File.Exists(configPath) ? File.ReadAllText(configPath) : string.Empty;
                var merged = Merge(existing, fullProjectRoot, serverName, resolvedCommand);
                if (!string.Equals(existing, merged, StringComparison.Ordinal))
                    UnityMcpProjectConfigFile.WriteAtomically(configPath, merged);
                return true;
            }
            catch (Exception exception)
            {
                error = "Could not write the Codex project configuration: " + exception.Message;
                return false;
            }
        }

        internal static bool IsManaged(string projectRoot, string serverName = DefaultServerName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(projectRoot) || string.IsNullOrWhiteSpace(serverName))
                    return false;

                var fullProjectRoot = UnityMcpProjectConfigFile.NormalizeProjectRoot(projectRoot);
                var path = Path.Combine(fullProjectRoot, ".codex", "config.toml");
                if (!File.Exists(path)) return false;

                var marker = ManagedMarker(serverName);
                var header = "[mcp_servers." + serverName + "]";
                var lines = File.ReadAllLines(path);
                var hasMarker = false;
                var inSection = false;
                var hasCommand = false;
                var hasArgs = false;

                foreach (var rawLine in lines)
                {
                    var trimmed = rawLine.Trim();
                    if (string.Equals(trimmed, marker, StringComparison.Ordinal))
                    {
                        hasMarker = true;
                        continue;
                    }

                    if (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal))
                    {
                        inSection = string.Equals(trimmed, header, StringComparison.Ordinal);
                        continue;
                    }

                    if (!inSection) continue;
                    if (IsAssignment(trimmed, "command")) hasCommand = true;
                    if (IsAssignment(trimmed, "args")) hasArgs = true;
                }

                return hasMarker && hasCommand && hasArgs;
            }
            catch
            {
                return false;
            }
        }

        internal static string Merge(
            string existing,
            string projectRoot,
            string serverName,
            string unityCommand)
        {
            var fullProjectRoot = UnityMcpProjectConfigFile.NormalizeProjectRoot(projectRoot);
            ValidateServerName(serverName);
            if (string.IsNullOrWhiteSpace(unityCommand))
                throw new ArgumentException("The Unity CLI command is required.", nameof(unityCommand));

            existing = existing ?? string.Empty;
            var newline = existing.Contains("\r\n") ? "\r\n" : "\n";
            var normalized = existing.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n');
            var lines = normalized.Length == 0
                ? new List<string>()
                : new List<string>(normalized.Split('\n'));

            var header = "[mcp_servers." + serverName + "]";
            var sectionStart = FindUniqueSection(lines, header);
            var commandLine = "command = \"" + EscapeTomlString(unityCommand.Trim()) + "\"";
            var argsLine = "args = [\"mcp\", \"--tool-groups\", \"--project-path\", \""
                + EscapeTomlString(fullProjectRoot)
                + "\"]";
            var marker = ManagedMarker(serverName);

            if (sectionStart < 0)
            {
                if (lines.Count > 0 && lines[lines.Count - 1].Length != 0) lines.Add(string.Empty);
                lines.Add(marker);
                lines.Add(header);
                lines.Add(commandLine);
                lines.Add(argsLine);
            }
            else
            {
                var sectionEnd = FindSectionEnd(lines, sectionStart + 1);
                RemoveManagedAssignments(lines, sectionStart + 1, sectionEnd);

                if (sectionStart == 0 || !string.Equals(lines[sectionStart - 1].Trim(), marker, StringComparison.Ordinal))
                {
                    lines.Insert(sectionStart, marker);
                    sectionStart++;
                }
                lines.Insert(sectionStart + 1, commandLine);
                lines.Insert(sectionStart + 2, argsLine);
            }

            return string.Join(newline, lines) + newline;
        }

        private static string ManagedMarker(string serverName) => "# UnityMCP managed project server: " + serverName;

        private static void RemoveManagedAssignments(List<string> lines, int start, int endExclusive)
        {
            var index = start;
            var currentEnd = endExclusive;
            while (index < currentEnd)
            {
                var line = lines[index];
                if (IsAssignment(line, "url")
                    || IsAssignment(line, "http_headers")
                    || IsAssignment(line, "command"))
                {
                    lines.RemoveAt(index);
                    currentEnd--;
                    continue;
                }

                if (IsAssignment(line, "args"))
                {
                    var removeCount = CountArrayAssignmentLines(lines, index, currentEnd);
                    lines.RemoveRange(index, removeCount);
                    currentEnd -= removeCount;
                    continue;
                }

                index++;
            }
        }

        private static int CountArrayAssignmentLines(IReadOnlyList<string> lines, int startIndex, int endExclusive)
        {
            var depth = 0;
            var inString = false;
            var escaped = false;
            var sawOpeningBracket = false;

            for (var index = startIndex; index < endExclusive; index++)
            {
                var line = lines[index] ?? string.Empty;
                for (var charIndex = 0; charIndex < line.Length; charIndex++)
                {
                    var ch = line[charIndex];
                    if (inString)
                    {
                        if (escaped) escaped = false;
                        else if (ch == '\\') escaped = true;
                        else if (ch == '"') inString = false;
                        continue;
                    }

                    if (ch == '#') break;
                    if (ch == '"')
                    {
                        inString = true;
                        continue;
                    }
                    if (ch == '[')
                    {
                        sawOpeningBracket = true;
                        depth++;
                    }
                    else if (ch == ']')
                    {
                        depth--;
                    }
                }

                if (!sawOpeningBracket || depth <= 0)
                    return index - startIndex + 1;
            }

            return endExclusive - startIndex;
        }

        private static int FindUniqueSection(IReadOnlyList<string> lines, string header)
        {
            var found = -1;
            for (var index = 0; index < lines.Count; index++)
            {
                if (!string.Equals(lines[index].Trim(), header, StringComparison.Ordinal)) continue;
                if (found >= 0) throw new InvalidDataException("The Codex config contains duplicate " + header + " tables.");
                found = index;
            }
            return found;
        }

        private static int FindSectionEnd(IReadOnlyList<string> lines, int start)
        {
            for (var index = start; index < lines.Count; index++)
            {
                var value = lines[index].Trim();
                if (value.StartsWith("[", StringComparison.Ordinal) && value.EndsWith("]", StringComparison.Ordinal))
                    return index;
            }
            return lines.Count;
        }

        private static bool IsAssignment(string line, string key)
        {
            var value = (line ?? string.Empty).TrimStart();
            if (!value.StartsWith(key, StringComparison.Ordinal)) return false;
            var index = key.Length;
            while (index < value.Length && char.IsWhiteSpace(value[index])) index++;
            return index < value.Length && value[index] == '=';
        }

        private static string EscapeTomlString(string value)
        {
            var builder = new StringBuilder(value.Length + 16);
            foreach (var character in value)
            {
                switch (character)
                {
                    case '\\': builder.Append("\\\\"); break;
                    case '"': builder.Append("\\\""); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\t': builder.Append("\\t"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\f': builder.Append("\\f"); break;
                    case '\r': builder.Append("\\r"); break;
                    default:
                        if (character < 0x20) builder.Append("\\u").Append(((int)character).ToString("x4"));
                        else builder.Append(character);
                        break;
                }
            }
            return builder.ToString();
        }

        private static void ValidateServerName(string serverName)
        {
            if (string.IsNullOrWhiteSpace(serverName))
                throw new ArgumentException("The Codex server name is required.", nameof(serverName));
            foreach (var character in serverName)
            {
                if (!IsAsciiLetterOrDigit(character) && character != '_' && character != '-')
                    throw new ArgumentException("The Codex server name contains unsupported characters.", nameof(serverName));
            }
        }

        private static bool IsAsciiLetterOrDigit(char character)
        {
            return character >= 'a' && character <= 'z'
                || character >= 'A' && character <= 'Z'
                || character >= '0' && character <= '9';
        }
    }
}
