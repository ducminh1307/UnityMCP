using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Unity.Pipeline.Commands;
using UnityEditor;

namespace DucMinh.UnityMcp.Editor
{
    internal sealed class UnityCliStatus
    {
        public bool IsInstalled { get; set; }
        public string ExecutablePath { get; set; }
        public string Version { get; set; }
        public bool IsChecking { get; set; }
        public string Error { get; set; }
        public DateTime CheckedAtUtc { get; set; }
    }

    internal sealed class PipelineServerStatus
    {
        public bool DescriptorExists { get; set; }
        public bool IsRunning { get; set; }
        public int Pid { get; set; }
        public int Port { get; set; }
        public string UnityVersion { get; set; }
        public string Mode { get; set; }
        public DateTime? LastHeartbeatUtc { get; set; }
        public string DescriptorPath { get; set; }
        public string Error { get; set; }
    }

    internal sealed class DiscoveredCommandInfo
    {
        public string Group { get; set; }
        public string Name { get; set; }
        public string FullName { get; set; }
        public string Description { get; set; }
        public CommandEffect Effect { get; set; }
        public IReadOnlyList<string> Tags { get; set; }
        public string AssemblyName { get; set; }
        public string Package => AssemblyName;
        public string CliInvocation { get; set; }
    }

    /// <summary>
    /// Discovers the official Unity CLI executable, Pipeline server status, and non-built-in
    /// <see cref="CliGroupCommandAttribute"/> / <see cref="CliCommandAttribute"/> extension commands.
    /// </summary>
    internal static class UnityCliDiscovery
    {
        internal const string WindowsInstallCommand = "irm https://unity.com/install.ps1 | iex";
        internal const string UnixInstallCommand = "curl -fsSL https://unity.com/install.sh | bash";

        private static readonly TimeSpan CliProbeCacheDuration = TimeSpan.FromSeconds(30);
        private static readonly object CliLock = new object();
        private static UnityCliStatus cachedCliStatus;
        private static bool cliProbeInFlight;
        private static IReadOnlyList<DiscoveredCommandInfo> cachedCommands;

        private static bool IsWindowsPlatform => Path.DirectorySeparatorChar == '\\';

        internal static string GetInstallCommand()
        {
            return IsWindowsPlatform ? WindowsInstallCommand : UnixInstallCommand;
        }

        internal static string FindUnityExecutable()
        {
            var executableName = IsWindowsPlatform ? "unity.exe" : "unity";
            var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

            foreach (var rawEntry in pathValue.Split(Path.PathSeparator))
            {
                var entry = (rawEntry ?? string.Empty).Trim().Trim('"');
                if (entry.Length == 0) continue;

                try
                {
                    var candidate = Path.Combine(entry, executableName);
                    if (File.Exists(candidate))
                        return Path.GetFullPath(candidate);
                }
                catch
                {
                    // Ignore malformed PATH entries.
                }
            }

            foreach (var candidate in GetStandardFallbackPaths())
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
                        return Path.GetFullPath(candidate);
                }
                catch
                {
                    // Ignore inaccessible candidate paths.
                }
            }

            return null;
        }

        internal static string ResolveUnityCommand(string customOverride = null)
        {
            if (!string.IsNullOrWhiteSpace(customOverride))
                return customOverride.Trim();

            return FindUnityExecutable() ?? "unity";
        }

        internal static UnityCliStatus GetCachedCliStatus(bool triggerRefreshIfStale = true)
        {
            lock (CliLock)
            {
                var now = DateTime.UtcNow;
                var isStale = cachedCliStatus == null
                    || now - cachedCliStatus.CheckedAtUtc > CliProbeCacheDuration;

                if (triggerRefreshIfStale && isStale && !cliProbeInFlight)
                    StartCliProbeLocked();

                if (cachedCliStatus != null)
                {
                    return new UnityCliStatus
                    {
                        IsInstalled = cachedCliStatus.IsInstalled,
                        ExecutablePath = cachedCliStatus.ExecutablePath,
                        Version = cachedCliStatus.Version,
                        IsChecking = cliProbeInFlight,
                        Error = cachedCliStatus.Error,
                        CheckedAtUtc = cachedCliStatus.CheckedAtUtc
                    };
                }

                return new UnityCliStatus
                {
                    IsInstalled = false,
                    IsChecking = cliProbeInFlight
                };
            }
        }

        internal static void RequestCliRefresh()
        {
            lock (CliLock)
            {
                if (!cliProbeInFlight)
                    StartCliProbeLocked();
            }
        }

        private static void StartCliProbeLocked()
        {
            cliProbeInFlight = true;
            Task.Run(() =>
            {
                UnityCliStatus result;
                try
                {
                    result = ProbeCliStatus();
                }
                catch (Exception exception)
                {
                    result = new UnityCliStatus
                    {
                        IsInstalled = false,
                        Error = exception.Message,
                        CheckedAtUtc = DateTime.UtcNow
                    };
                }

                lock (CliLock)
                {
                    cachedCliStatus = result;
                    cliProbeInFlight = false;
                }
            });
        }

        private static UnityCliStatus ProbeCliStatus()
        {
            var executablePath = FindUnityExecutable();
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                return new UnityCliStatus
                {
                    IsInstalled = false,
                    ExecutablePath = null,
                    Version = null,
                    IsChecking = false,
                    CheckedAtUtc = DateTime.UtcNow
                };
            }

            string version = null;
            string error = null;
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = executablePath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                startInfo.ArgumentList.Add("--version");

                using (var process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        error = "Could not start the unity CLI process.";
                    }
                    else
                    {
                        var stdoutTask = process.StandardOutput.ReadToEndAsync();
                        var stderrTask = process.StandardError.ReadToEndAsync();
                        if (!process.WaitForExit(3000))
                        {
                            try { process.Kill(); } catch { }
                            error = "Timed out while checking 'unity --version'.";
                        }
                        else
                        {
                            Task.WaitAll(new Task[] { stdoutTask, stderrTask }, 500);
                            var stdout = stdoutTask.Status == TaskStatus.RanToCompletion ? stdoutTask.Result : string.Empty;
                            var stderr = stderrTask.Status == TaskStatus.RanToCompletion ? stderrTask.Result : string.Empty;

                            if (process.ExitCode == 0 && TryParseVersionOutput(stdout, out var parsedVersion))
                            {
                                version = parsedVersion;
                            }
                            else
                            {
                                var detail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                                error = string.IsNullOrWhiteSpace(detail)
                                    ? $"unity --version exited with code {process.ExitCode}."
                                    : detail.Trim();
                            }
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                error = exception.Message;
            }

            return new UnityCliStatus
            {
                IsInstalled = true,
                ExecutablePath = executablePath,
                Version = version,
                IsChecking = false,
                Error = error,
                CheckedAtUtc = DateTime.UtcNow
            };
        }

        internal static bool TryParseVersionOutput(string output, out string version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(output)) return false;

            foreach (var rawLine in output.Replace("\r\n", "\n").Split('\n'))
            {
                var trimmed = rawLine.Trim();
                if (trimmed.Length == 0) continue;
                version = trimmed;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Reads Pipeline server status from <c>&lt;projectRoot&gt;/Library/Pipeline/.unity-pipeline-port</c>
        /// while deliberately skipping <c>evalToken</c> and unrelated fields.
        /// </summary>
        internal static PipelineServerStatus ReadPipelineStatus(string projectRoot)
        {
            if (string.IsNullOrWhiteSpace(projectRoot))
                return new PipelineServerStatus { DescriptorExists = false, IsRunning = false };

            var fullProjectRoot = Path.GetFullPath(projectRoot);
            var descriptorPath = Path.Combine(fullProjectRoot, "Library", "Pipeline", ".unity-pipeline-port");
            if (!File.Exists(descriptorPath))
            {
                return new PipelineServerStatus
                {
                    DescriptorExists = false,
                    IsRunning = false,
                    DescriptorPath = descriptorPath
                };
            }

            try
            {
                int pid = 0;
                int port = 0;
                string unityVersion = null;
                string mode = null;
                DateTime? lastHeartbeatUtc = null;

                using (var stream = new FileStream(descriptorPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var streamReader = new StreamReader(stream, Encoding.UTF8))
                using (var reader = new JsonTextReader(streamReader))
                {
                    if (!reader.Read() || reader.TokenType != JsonToken.StartObject)
                        throw new InvalidDataException("The Pipeline port descriptor is not a JSON object.");

                    while (reader.Read() && reader.TokenType != JsonToken.EndObject)
                    {
                        if (reader.TokenType != JsonToken.PropertyName) continue;
                        var propertyName = reader.Value as string;
                        if (!reader.Read()) break;

                        switch (propertyName)
                        {
                            case "pid":
                                pid = reader.TokenType == JsonToken.Integer ? Convert.ToInt32(reader.Value, CultureInfo.InvariantCulture) : 0;
                                break;
                            case "port":
                                port = reader.TokenType == JsonToken.Integer ? Convert.ToInt32(reader.Value, CultureInfo.InvariantCulture) : 0;
                                break;
                            case "unityVersion":
                                unityVersion = reader.TokenType == JsonToken.String ? (string)reader.Value : null;
                                break;
                            case "mode":
                                mode = reader.TokenType == JsonToken.String ? (string)reader.Value : null;
                                break;
                            case "lastHeartbeat":
                                lastHeartbeatUtc = ReadUtcDateTime(reader);
                                break;
                            default:
                                // Never read, store, or materialize auth tokens or unrelated fields.
                                reader.Skip();
                                break;
                        }
                    }
                }

                var isRunning = pid > 0 && port > 0 && IsProcessAlive(pid);
                return new PipelineServerStatus
                {
                    DescriptorExists = true,
                    IsRunning = isRunning,
                    Pid = pid,
                    Port = port,
                    UnityVersion = unityVersion,
                    Mode = mode,
                    LastHeartbeatUtc = lastHeartbeatUtc,
                    DescriptorPath = descriptorPath
                };
            }
            catch (Exception exception)
            {
                return new PipelineServerStatus
                {
                    DescriptorExists = true,
                    IsRunning = false,
                    DescriptorPath = descriptorPath,
                    Error = exception.Message
                };
            }
        }

        internal static IReadOnlyList<DiscoveredCommandInfo> DiscoverExtensionCommands(bool forceRefresh = false)
        {
            if (!forceRefresh && cachedCommands != null)
                return cachedCommands;

            var discovered = new List<DiscoveredCommandInfo>();
            var seenFullNames = new HashSet<string>(StringComparer.Ordinal);
            var methods = TypeCache.GetMethodsWithAttribute<CliGroupCommandAttribute>()
                .Concat(TypeCache.GetMethodsWithAttribute<CliCommandAttribute>())
                .Distinct();

            foreach (var method in methods)
            {
                if (method == null || !method.IsStatic) continue;

                var assemblyName = method.DeclaringType?.Assembly.GetName().Name ?? string.Empty;
                if (IsBuiltInPipelineAssembly(assemblyName)) continue;

                if (!TryBuildCommandInfo(method, assemblyName, out var info)) continue;
                if (!seenFullNames.Add(info.FullName)) continue;
                discovered.Add(info);
            }

            discovered.Sort((left, right) =>
            {
                var groupCompare = string.Compare(left.Group, right.Group, StringComparison.OrdinalIgnoreCase);
                return groupCompare != 0
                    ? groupCompare
                    : string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
            });

            cachedCommands = discovered;
            return cachedCommands;
        }

        internal static bool IsBuiltInPipelineAssembly(string assemblyName)
        {
            if (string.IsNullOrWhiteSpace(assemblyName)) return false;
            return string.Equals(assemblyName, "Unity.Pipeline", StringComparison.OrdinalIgnoreCase)
                || assemblyName.StartsWith("Unity.Pipeline.", StringComparison.OrdinalIgnoreCase);
        }

        internal static string DeriveCommandName(string methodName)
        {
            if (string.IsNullOrEmpty(methodName)) return string.Empty;

            var builder = new StringBuilder(methodName.Length + 8);
            for (var index = 0; index < methodName.Length; index++)
            {
                var character = methodName[index];
                if (char.IsUpper(character) && index > 0)
                {
                    var previousIsUpper = char.IsUpper(methodName[index - 1]);
                    var nextIsLower = index + 1 < methodName.Length && char.IsLower(methodName[index + 1]);
                    if (!previousIsUpper || nextIsLower)
                        builder.Append('_');
                }
                builder.Append(char.ToLowerInvariant(character));
            }
            return builder.ToString();
        }

        private static bool TryBuildCommandInfo(MethodInfo method, string assemblyName, out DiscoveredCommandInfo info)
        {
            info = null;
            var groupAttr = method.GetCustomAttribute<CliGroupCommandAttribute>();
            var cliAttr = method.GetCustomAttribute<CliCommandAttribute>();
            if (groupAttr == null && cliAttr == null) return false;

            string group;
            string name;
            string description;
            CommandEffect effect;
            string[] tags;

            if (groupAttr != null)
            {
                group = groupAttr.Group;
                name = string.IsNullOrEmpty(groupAttr.Name) ? DeriveCommandName(method.Name) : groupAttr.Name;
                description = groupAttr.Description;
                effect = groupAttr.Effect;
                tags = groupAttr.Tags;
            }
            else
            {
                group = cliAttr.Group ?? method.DeclaringType?.FullName?.Replace('+', '.').ToLowerInvariant();
                name = string.IsNullOrEmpty(cliAttr.Name) ? DeriveCommandName(method.Name) : cliAttr.Name;
                description = cliAttr.Description;
                effect = cliAttr.Effect;
                tags = cliAttr.Tags;
            }

            if (string.IsNullOrWhiteSpace(name)) return false;

            var fullName = string.IsNullOrWhiteSpace(group) ? name : group + " " + name;
            var cliInvocation = string.IsNullOrWhiteSpace(group) || string.Equals(group, "global", StringComparison.Ordinal)
                ? "unity command " + name
                : "unity command " + group + " " + name;

            info = new DiscoveredCommandInfo
            {
                Group = string.IsNullOrWhiteSpace(group) ? "global" : group,
                Name = name,
                FullName = fullName,
                Description = description ?? string.Empty,
                Effect = effect,
                Tags = tags ?? Array.Empty<string>(),
                AssemblyName = assemblyName,
                CliInvocation = cliInvocation
            };
            return true;
        }

        private static IEnumerable<string> GetStandardFallbackPaths()
        {
            if (IsWindowsPlatform)
            {
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (!string.IsNullOrWhiteSpace(localAppData))
                    yield return Path.Combine(localAppData, "Unity", "bin", "unity.exe");
                yield break;
            }

            yield return "/usr/local/bin/unity";
            yield return "/opt/homebrew/bin/unity";
            yield return "/usr/bin/unity";

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(home))
            {
                yield return Path.Combine(home, ".local", "bin", "unity");
                yield return Path.Combine(home, ".unity", "bin", "unity");
            }
        }

        private static DateTime? ReadUtcDateTime(JsonTextReader reader)
        {
            if (reader.TokenType == JsonToken.Date && reader.Value is DateTime dateTime)
                return dateTime.ToUniversalTime();

            if (reader.TokenType == JsonToken.String && reader.Value is string text
                && DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
            {
                return parsed;
            }

            return null;
        }

        private static bool IsProcessAlive(int pid)
        {
            if (pid <= 0) return false;
            try
            {
                if (pid == Process.GetCurrentProcess().Id) return true;
                using (var process = Process.GetProcessById(pid))
                    return !process.HasExited;
            }
            catch
            {
                return false;
            }
        }
    }
}
