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
    /// Merges UnityMCP into the workspace-scoped JSON configuration understood by Antigravity
    /// and Claude Code. Only the named server's connection fields are owned by UnityMCP.
    /// </summary>
    internal static class UnityMcpJsonProjectConfig
    {
        internal const string AntigravityRelativeConfigPath = ".agents/mcp_config.json";
        internal const string ClaudeRelativeConfigPath = ".mcp.json";

        internal static bool TryWrite(
            string projectRoot,
            string serverName,
            string endpoint,
            string bearerToken,
            UnityMcpJsonClient client,
            out string configPath,
            out string error)
        {
            configPath = null;
            error = null;
            try
            {
                ValidateInputs(projectRoot, serverName, endpoint, bearerToken);
                var fullProjectRoot = Path.GetFullPath(projectRoot);
                configPath = Path.Combine(fullProjectRoot, RelativeConfigPath(client).Replace('/', Path.DirectorySeparatorChar));

                UnityMcpProjectConfigFile.PrepareLocalSecretFile(fullProjectRoot, configPath, ClientName(client));
                Directory.CreateDirectory(Path.GetDirectoryName(configPath) ?? fullProjectRoot);

                var existing = File.Exists(configPath) ? File.ReadAllText(configPath) : string.Empty;
                var merged = Merge(existing, serverName, endpoint, bearerToken, client);
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

        internal static bool IsManaged(string projectRoot, string serverName, UnityMcpJsonClient client)
        {
            try
            {
                var path = Path.Combine(
                    Path.GetFullPath(projectRoot),
                    RelativeConfigPath(client).Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path)) return false;

                var root = JObject.Parse(File.ReadAllText(path));
                var server = root["mcpServers"]?[serverName] as JObject;
                if (server == null) return false;
                var endpoint = (string)server[client == UnityMcpJsonClient.Antigravity ? "serverUrl" : "url"];
                var authorization = (string)server["headers"]?["Authorization"];
                return !string.IsNullOrWhiteSpace(endpoint)
                    && endpoint.StartsWith("http://127.0.0.1:", StringComparison.Ordinal)
                    && !string.IsNullOrWhiteSpace(authorization)
                    && authorization.StartsWith("Bearer ", StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        internal static string Merge(
            string existing,
            string serverName,
            string endpoint,
            string bearerToken,
            UnityMcpJsonClient client)
        {
            ValidateServerName(serverName);
            if (string.IsNullOrWhiteSpace(endpoint)) throw new ArgumentException("The MCP endpoint is required.", nameof(endpoint));
            if (string.IsNullOrWhiteSpace(bearerToken)) throw new ArgumentException("The MCP bearer token is required.", nameof(bearerToken));

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

            if (client == UnityMcpJsonClient.Antigravity)
            {
                server.Remove("url");
                server.Remove("httpUrl");
                server["serverUrl"] = endpoint;
            }
            else
            {
                server.Remove("serverUrl");
                server.Remove("httpUrl");
                server["type"] = "http";
                server["url"] = endpoint;
            }

            var headersToken = server["headers"];
            JObject headers;
            if (headersToken == null)
            {
                headers = new JObject();
                server["headers"] = headers;
            }
            else
            {
                headers = headersToken as JObject
                    ?? throw new InvalidDataException("The MCP server headers property must be a JSON object.");
            }
            headers["Authorization"] = "Bearer " + bearerToken;

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
            if (string.IsNullOrWhiteSpace(serverName)) throw new ArgumentException("The MCP server name is required.", nameof(serverName));
            foreach (var character in serverName)
                if (!IsAsciiLetterOrDigit(character) && character != '_' && character != '-')
                    throw new ArgumentException("The MCP server name contains unsupported characters.", nameof(serverName));
        }

        private static bool IsAsciiLetterOrDigit(char character)
        {
            return character >= 'a' && character <= 'z'
                || character >= 'A' && character <= 'Z'
                || character >= '0' && character <= '9';
        }
    }
}
