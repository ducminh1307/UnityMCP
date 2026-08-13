using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace DucMinh.UnityMcp.Editor
{
    /// <summary>
    /// Owns the project-scoped Codex MCP fragment written outside Assets. The generated file
    /// contains a bearer token, so Git repositories receive a matching local info/exclude entry.
    /// </summary>
    internal static class UnityMcpCodexProjectConfig
    {
        internal const string RelativeConfigPath = ".codex/config.toml";

        internal static bool TryWrite(
            string projectRoot,
            string serverName,
            string endpoint,
            string bearerToken,
            out string configPath,
            out string error)
        {
            configPath = null;
            error = null;
            try
            {
                ValidateInputs(projectRoot, serverName, endpoint, bearerToken);
                var fullProjectRoot = Path.GetFullPath(projectRoot);
                configPath = Path.Combine(fullProjectRoot, ".codex", "config.toml");

                EnsureLocalGitExclude(fullProjectRoot, configPath);
                Directory.CreateDirectory(Path.GetDirectoryName(configPath) ?? fullProjectRoot);

                var existing = File.Exists(configPath) ? File.ReadAllText(configPath) : string.Empty;
                var merged = Merge(existing, serverName, endpoint, bearerToken);
                if (!string.Equals(existing, merged, StringComparison.Ordinal)) WriteAtomically(configPath, merged);
                return true;
            }
            catch (Exception exception)
            {
                error = "Could not write the Codex project configuration: " + exception.Message;
                return false;
            }
        }

        internal static bool IsManaged(string projectRoot, string serverName)
        {
            try
            {
                var path = Path.Combine(Path.GetFullPath(projectRoot), ".codex", "config.toml");
                if (!File.Exists(path)) return false;
                var marker = ManagedMarker(serverName);
                foreach (var line in File.ReadAllLines(path))
                    if (string.Equals(line.Trim(), marker, StringComparison.Ordinal)) return true;
                return false;
            }
            catch
            {
                return false;
            }
        }

        internal static string Merge(string existing, string serverName, string endpoint, string bearerToken)
        {
            ValidateServerName(serverName);
            if (string.IsNullOrWhiteSpace(endpoint)) throw new ArgumentException("The MCP endpoint is required.", nameof(endpoint));
            if (string.IsNullOrWhiteSpace(bearerToken)) throw new ArgumentException("The MCP bearer token is required.", nameof(bearerToken));

            existing = existing ?? string.Empty;
            var newline = existing.Contains("\r\n") ? "\r\n" : "\n";
            var normalized = existing.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n');
            var lines = normalized.Length == 0
                ? new List<string>()
                : new List<string>(normalized.Split('\n'));

            var header = "[mcp_servers." + serverName + "]";
            var sectionStart = FindUniqueSection(lines, header);
            var urlLine = "url = \"" + EscapeTomlString(endpoint) + "\"";
            var headerLine = "http_headers = { Authorization = \"Bearer " + EscapeTomlString(bearerToken) + "\" }";
            var marker = ManagedMarker(serverName);

            if (sectionStart < 0)
            {
                if (lines.Count > 0 && lines[lines.Count - 1].Length != 0) lines.Add(string.Empty);
                lines.Add(marker);
                lines.Add(header);
                lines.Add(urlLine);
                lines.Add(headerLine);
            }
            else
            {
                var sectionEnd = FindSectionEnd(lines, sectionStart + 1);
                for (var index = sectionEnd - 1; index > sectionStart; index--)
                {
                    if (IsAssignment(lines[index], "url") || IsAssignment(lines[index], "http_headers"))
                        lines.RemoveAt(index);
                }

                if (sectionStart == 0 || !string.Equals(lines[sectionStart - 1].Trim(), marker, StringComparison.Ordinal))
                {
                    lines.Insert(sectionStart, marker);
                    sectionStart++;
                }
                lines.Insert(sectionStart + 1, urlLine);
                lines.Insert(sectionStart + 2, headerLine);
            }

            return string.Join(newline, lines) + newline;
        }

        private static string ManagedMarker(string serverName) => "# UnityMCP managed project server: " + serverName;

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
                if (value.StartsWith("[", StringComparison.Ordinal) && value.EndsWith("]", StringComparison.Ordinal)) return index;
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

        private static void EnsureLocalGitExclude(string projectRoot, string configPath)
        {
            if (!TryFindGitDirectory(projectRoot, out var repositoryRoot, out var gitDirectory)) return;

            if (File.Exists(configPath) && IsTrackedByGit(repositoryRoot, configPath))
                throw new InvalidOperationException(".codex/config.toml is already tracked by Git. Untrack it before writing a bearer token.");

            var commonGitDirectory = ResolveGitCommonDirectory(gitDirectory);
            var infoDirectory = Path.Combine(commonGitDirectory, "info");
            var excludePath = Path.Combine(infoDirectory, "exclude");
            Directory.CreateDirectory(infoDirectory);

            var relativePath = configPath.Substring(repositoryRoot.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Replace('\\', '/');
            var entry = "/" + relativePath;
            var directoryEntry = "/" + relativePath.Substring(0, relativePath.LastIndexOf('/') + 1);
            var existing = File.Exists(excludePath) ? File.ReadAllText(excludePath) : string.Empty;

            foreach (var line in existing.Replace("\r\n", "\n").Split('\n'))
            {
                var value = line.Trim().Replace('\\', '/');
                if (value.StartsWith("#", StringComparison.Ordinal)) continue;
                if (string.Equals(value, entry, StringComparison.Ordinal)
                    || string.Equals(value, entry.TrimStart('/'), StringComparison.Ordinal)
                    || string.Equals(value, directoryEntry, StringComparison.Ordinal)
                    || string.Equals(value, directoryEntry.TrimStart('/'), StringComparison.Ordinal)) return;
            }

            var newline = existing.Contains("\r\n") ? "\r\n" : Environment.NewLine;
            var builder = new StringBuilder(existing);
            if (builder.Length > 0 && !existing.EndsWith("\n", StringComparison.Ordinal)) builder.Append(newline);
            builder.Append("# Local bearer-authenticated Codex config generated by UnityMCP").Append(newline);
            builder.Append(entry).Append(newline);
            WriteAtomically(excludePath, builder.ToString());
        }

        private static string ResolveGitCommonDirectory(string gitDirectory)
        {
            var commonDirectoryFile = Path.Combine(gitDirectory, "commondir");
            if (!File.Exists(commonDirectoryFile)) return gitDirectory;
            var value = File.ReadAllText(commonDirectoryFile).Split('\n')[0].Trim();
            if (value.Length == 0) throw new InvalidDataException("The Git commondir file is empty.");
            var result = Path.GetFullPath(Path.IsPathRooted(value) ? value : Path.Combine(gitDirectory, value));
            if (!Directory.Exists(result)) throw new DirectoryNotFoundException("The shared worktree Git directory was not found.");
            return result;
        }

        private static bool IsTrackedByGit(string repositoryRoot, string configPath)
        {
            var relativePath = configPath.Substring(repositoryRoot.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Replace('\\', '/');
            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = repositoryRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("ls-files");
            startInfo.ArgumentList.Add("--error-unmatch");
            startInfo.ArgumentList.Add("--");
            startInfo.ArgumentList.Add(relativePath);

            using (var process = Process.Start(startInfo))
            {
                if (process == null) throw new InvalidOperationException("Git could not be started to verify that the Codex config is local-only.");
                if (!process.WaitForExit(5000))
                {
                    try { process.Kill(); } catch { }
                    throw new TimeoutException("Git did not finish checking the Codex config within five seconds.");
                }
                if (process.ExitCode == 0) return true;
                if (process.ExitCode == 1) return false;
                var detail = process.StandardError.ReadToEnd().Trim();
                throw new InvalidOperationException("Git could not verify the Codex config" + (detail.Length == 0 ? "." : ": " + detail));
            }
        }

        private static bool TryFindGitDirectory(string projectRoot, out string repositoryRoot, out string gitDirectory)
        {
            var cursor = new DirectoryInfo(Path.GetFullPath(projectRoot));
            while (cursor != null)
            {
                var candidate = Path.Combine(cursor.FullName, ".git");
                if (Directory.Exists(candidate))
                {
                    repositoryRoot = cursor.FullName;
                    gitDirectory = Path.GetFullPath(candidate);
                    return true;
                }
                if (File.Exists(candidate))
                {
                    var firstLine = File.ReadAllText(candidate).Split('\n')[0].Trim();
                    const string prefix = "gitdir:";
                    if (!firstLine.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("The .git file does not contain a gitdir reference.");
                    var value = firstLine.Substring(prefix.Length).Trim();
                    repositoryRoot = cursor.FullName;
                    gitDirectory = Path.GetFullPath(Path.IsPathRooted(value) ? value : Path.Combine(cursor.FullName, value));
                    if (!Directory.Exists(gitDirectory)) throw new DirectoryNotFoundException("The worktree Git directory was not found.");
                    return true;
                }
                cursor = cursor.Parent;
            }

            repositoryRoot = null;
            gitDirectory = null;
            return false;
        }

        private static void WriteAtomically(string path, string content)
        {
            var directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("The target file has no parent directory.");
            Directory.CreateDirectory(directory);
            var temporaryPath = Path.Combine(directory, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllText(temporaryPath, content, new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporaryPath, path, null);
                else File.Move(temporaryPath, path);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }

        private static void ValidateInputs(string projectRoot, string serverName, string endpoint, string bearerToken)
        {
            if (string.IsNullOrWhiteSpace(projectRoot)) throw new ArgumentException("The Unity project root is required.", nameof(projectRoot));
            ValidateServerName(serverName);
            if (string.IsNullOrWhiteSpace(endpoint) || !endpoint.StartsWith("http://127.0.0.1:", StringComparison.Ordinal))
                throw new ArgumentException("The MCP endpoint must use IPv4 loopback HTTP.", nameof(endpoint));
            if (string.IsNullOrWhiteSpace(bearerToken)) throw new ArgumentException("The MCP bearer token is required.", nameof(bearerToken));
        }

        private static void ValidateServerName(string serverName)
        {
            if (string.IsNullOrWhiteSpace(serverName)) throw new ArgumentException("The Codex server name is required.", nameof(serverName));
            foreach (var character in serverName)
                if (!IsAsciiLetterOrDigit(character) && character != '_' && character != '-')
                    throw new ArgumentException("The Codex server name contains unsupported characters.", nameof(serverName));
        }

        private static bool IsAsciiLetterOrDigit(char character)
        {
            return character >= 'a' && character <= 'z'
                || character >= 'A' && character <= 'Z'
                || character >= '0' && character <= '9';
        }
    }
}
