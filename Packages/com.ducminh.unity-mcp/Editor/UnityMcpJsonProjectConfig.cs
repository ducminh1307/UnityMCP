using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DucMinh.UnityMcp.Editor
{
    internal enum UnityMcpJsonClient
    {
        Antigravity,
        Claude
    }

    /// <summary>
    /// Merges Unity CLI stdio MCP configuration into the workspace-scoped JSON configuration
    /// understood by Antigravity and Claude Code.
    /// </summary>
    internal static class UnityMcpJsonProjectConfig
    {
        internal const string DefaultServerName = "unity";
        internal const string AntigravityRelativeConfigPath = ".agents/mcp_config.json";
        internal const string ClaudeRelativeConfigPath = ".mcp.json";

        internal static string[] BuildMcpArgs(string projectRoot)
        {
            var fullProjectRoot = UnityMcpProjectConfigFile.NormalizeProjectRoot(projectRoot);
            return new[] { "mcp", "--tool-groups", "--project-path", fullProjectRoot };
        }

        internal static string BuildSnippetJson(
            string projectRoot,
            string serverName = DefaultServerName,
            string unityCommand = null)
        {
            var resolvedCommand = string.IsNullOrWhiteSpace(unityCommand)
                ? UnityCliDiscovery.ResolveUnityCommand()
                : unityCommand.Trim();
            return Merge(string.Empty, projectRoot, serverName, resolvedCommand, UnityMcpJsonClient.Antigravity);
        }

        internal static bool TryWrite(
            string projectRoot,
            UnityMcpJsonClient client,
            out string configPath,
            out string error)
        {
            return TryWrite(projectRoot, DefaultServerName, null, client, out configPath, out error);
        }

        internal static bool TryWrite(
            string projectRoot,
            string serverName,
            string unityCommand,
            UnityMcpJsonClient client,
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

                configPath = Path.Combine(
                    fullProjectRoot,
                    RelativeConfigPath(client).Replace('/', Path.DirectorySeparatorChar));
                UnityMcpProjectConfigFile.EnsureSafeProjectPath(fullProjectRoot, configPath);

                Directory.CreateDirectory(Path.GetDirectoryName(configPath) ?? fullProjectRoot);

                var existing = File.Exists(configPath) ? File.ReadAllText(configPath) : string.Empty;
                var merged = Merge(existing, fullProjectRoot, serverName, resolvedCommand, client);
                if (!string.Equals(existing, merged, StringComparison.Ordinal))
                    UnityMcpProjectConfigFile.WriteAtomically(configPath, merged);
                return true;
            }
            catch (Exception exception)
            {
                error = "Could not write the " + ClientName(client) + " project configuration: " + exception.Message;
                return false;
            }
        }

        internal static bool IsManaged(string projectRoot, UnityMcpJsonClient client)
        {
            return IsManaged(projectRoot, DefaultServerName, client);
        }

        internal static bool IsManaged(string projectRoot, string serverName, UnityMcpJsonClient client)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(projectRoot) || string.IsNullOrWhiteSpace(serverName))
                    return false;

                var fullProjectRoot = UnityMcpProjectConfigFile.NormalizeProjectRoot(projectRoot);
                var path = Path.Combine(
                    fullProjectRoot,
                    RelativeConfigPath(client).Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path)) return false;

                var root = JObject.Parse(File.ReadAllText(path));
                var server = root["mcpServers"]?[serverName] as JObject;
                if (server == null) return false;
                if (server["serverUrl"] != null || server["url"] != null || server["httpUrl"] != null)
                    return false;

                var command = (string)server["command"];
                if (string.IsNullOrWhiteSpace(command)) return false;

                var args = server["args"] as JArray;
                if (args == null) return false;

                var expectedArgs = BuildMcpArgs(fullProjectRoot);
                if (args.Count != expectedArgs.Length) return false;
                for (var index = 0; index < expectedArgs.Length; index++)
                {
                    if (!string.Equals((string)args[index], expectedArgs[index], StringComparison.Ordinal))
                        return false;
                }

                return true;
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
            string unityCommand,
            UnityMcpJsonClient client)
        {
            var fullProjectRoot = UnityMcpProjectConfigFile.NormalizeProjectRoot(projectRoot);
            ValidateServerName(serverName);
            if (string.IsNullOrWhiteSpace(unityCommand))
                throw new ArgumentException("The Unity CLI command is required.", nameof(unityCommand));

            existing = existing ?? string.Empty;
            var root = string.IsNullOrWhiteSpace(existing) ? new JObject() : JObject.Parse(existing);
            var mcpServersToken = root["mcpServers"];
            JObject mcpServers;
            if (mcpServersToken == null)
            {
                mcpServers = new JObject();
                root["mcpServers"] = mcpServers;
            }
            else
            {
                mcpServers = mcpServersToken as JObject
                    ?? throw new InvalidDataException("The mcpServers property must be a JSON object.");
            }

            var serverToken = mcpServers[serverName];
            JObject server;
            if (serverToken == null)
            {
                server = new JObject();
                mcpServers[serverName] = server;
            }
            else
            {
                server = serverToken as JObject
                    ?? throw new InvalidDataException("The MCP server entry " + serverName + " must be a JSON object.");
            }

            server.Remove("serverUrl");
            server.Remove("url");
            server.Remove("httpUrl");
            server.Remove("type");
            server.Remove("headers");

            server["command"] = unityCommand.Trim();
            server["args"] = new JArray(BuildMcpArgs(fullProjectRoot));

            var newline = existing.Contains("\r\n") ? "\r\n" : "\n";
            return root.ToString(Formatting.Indented).Replace("\r\n", "\n").Replace("\n", newline) + newline;
        }

        private static string RelativeConfigPath(UnityMcpJsonClient client)
        {
            return client == UnityMcpJsonClient.Antigravity
                ? AntigravityRelativeConfigPath
                : ClaudeRelativeConfigPath;
        }

        private static string ClientName(UnityMcpJsonClient client)
        {
            return client == UnityMcpJsonClient.Antigravity ? "Antigravity" : "Claude";
        }

        private static void ValidateServerName(string serverName)
        {
            if (string.IsNullOrWhiteSpace(serverName))
                throw new ArgumentException("The MCP server name is required.", nameof(serverName));
            foreach (var character in serverName)
            {
                if (!IsAsciiLetterOrDigit(character) && character != '_' && character != '-')
                    throw new ArgumentException("The MCP server name contains unsupported characters.", nameof(serverName));
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
