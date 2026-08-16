using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace DucMinh.UnityMcp.Editor
{
    /// <summary>
    /// Local, per-user configuration for the optional Editor-managed Streamable HTTP gateway.
    /// The bearer token is deliberately not part of this serializable settings object.
    /// </summary>
    public sealed class UnityMcpGatewaySettings
    {
        public string ExecutablePath { get; set; }
        public int PreferredPort { get; set; }
        public string McpPath { get; set; }
        public bool DebugLoggingEnabled { get; set; }

        internal UnityMcpGatewaySettings Clone()
        {
            return new UnityMcpGatewaySettings
            {
                ExecutablePath = ExecutablePath,
                PreferredPort = PreferredPort,
                McpPath = McpPath,
                DebugLoggingEnabled = DebugLoggingEnabled
            };
        }
    }

    public enum UnityMcpGatewayState
    {
        Stopped,
        Starting,
        Running,
        Error
    }

    /// <summary>Non-secret status which is safe to render in an Editor window.</summary>
    public sealed class UnityMcpGatewayStatus
    {
        public UnityMcpGatewayState State { get; internal set; }
        public string Message { get; internal set; }
        public string LastError { get; internal set; }
        public bool RequiresInstallation { get; internal set; }
        public string ExpectedExecutablePath { get; internal set; }
        public int Port { get; internal set; }
        public int ProcessId { get; internal set; }
        public string Endpoint { get; internal set; }
        public string InstanceId { get; internal set; }
        public bool IsRunning => State == UnityMcpGatewayState.Starting || State == UnityMcpGatewayState.Running;
    }

    /// <summary>
    /// Sensitive connection material. Do not write this value to the Unity Console, source
    /// control, or a project asset. It exists so an explicit UI action can copy a client config.
    /// </summary>
    public sealed class UnityMcpGatewayConnectionInfo
    {
        public string Endpoint { get; internal set; }
        public string BearerToken { get; internal set; }
        public string InstanceId { get; internal set; }
    }

    /// <summary>
    /// Starts an owned Python <c>unity-mcp</c> process for this exact Unity Editor instance.
    ///
    /// This is intentionally Editor-only and only starts the Streamable HTTP transport. The
    /// default stdio integration remains client-owned, because an Editor cannot safely own the
    /// stdin/stdout connection used by an MCP client.
    /// </summary>
    [InitializeOnLoad]
    public static class UnityMcpGatewayHost
    {
        private const string ProductKeyPrefix = "DucMinh.UnityMcp.Gateway.";
        private const string TokenEnvironmentVariable = "UNITY_MCP_HTTP_TOKEN";
        private const string DefaultMcpPath = "/mcp";
        private const int DefaultPort = 8765;
        private const int PortProbeCount = 128;
        private const int MaxLogLines = 20;
        private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(15);

        private static readonly object Gate = new object();
        private static readonly string ProjectKey = BuildProjectKey();
        private static readonly string SessionKeyPrefix = BuildSessionKeyPrefix();

        private static Process gatewayProcess;
        private static UnityMcpGatewayStatus status = NewStoppedStatus("Gateway is stopped.");
        private static DateTime startedAtUtc;
        private static bool expectedStop;
        private static bool processExited;
        private static bool gatewayReady;
        private static bool restartAfterAssemblyReload;
        private static bool restartAfterGatewayExit;
        private static int restartPort;
        private static DateTime restartDeadlineUtc;
        private static string bearerToken;
        private static readonly Queue<string> recentLogs = new Queue<string>();

        public static event Action<UnityMcpGatewayStatus> StatusChanged;

        static UnityMcpGatewayHost()
        {
            // Keep the gateway alive through a domain reload. The Editor bridge deliberately
            // reuses its descriptor identity, token, and port, so the long-lived gateway can
            // ride out the brief bridge outage and refresh its registry afterwards. Killing the
            // gateway here tears down every MCP HTTP session and is the source of intermittent
            // client disconnects during script compilation.
            RecoverPersistedGatewayIfOwned();
            restartAfterAssemblyReload = SessionState.GetBool(SessionKey("restartAfterReload"), false);
            SessionState.EraseBool(SessionKey("restartAfterReload"));
            if (restartAfterAssemblyReload) restartDeadlineUtc = DateTime.UtcNow + StartupTimeout;
            AssemblyReloadEvents.beforeAssemblyReload += StopForAssemblyReload;
            EditorApplication.quitting += Stop;
            EditorApplication.update += Tick;
        }

        /// <summary>Returns a copy of the current user's settings for this project only.</summary>
        public static UnityMcpGatewaySettings GetSettings()
        {
            lock (Gate)
            {
                var configuredPath = EditorPrefs.GetString(PreferenceKey("executablePath"), string.Empty);
                return new UnityMcpGatewaySettings
                {
                    ExecutablePath = string.IsNullOrWhiteSpace(configuredPath) ? GetDefaultExecutablePath() : configuredPath,
                    PreferredPort = EditorPrefs.HasKey(PreferenceKey("preferredPort"))
                        ? ClampPort(EditorPrefs.GetInt(PreferenceKey("preferredPort"), DefaultPort))
                        : GetDefaultPreferredPort(),
                    McpPath = NormalizeMcpPath(EditorPrefs.GetString(PreferenceKey("mcpPath"), DefaultMcpPath)),
                    DebugLoggingEnabled = !EditorPrefs.HasKey(PreferenceKey("debugLoggingEnabled"))
                        || EditorPrefs.GetBool(PreferenceKey("debugLoggingEnabled"), true)
                };
            }
        }

        /// <summary>
        /// Saves non-secret local settings. This never writes under Assets and is not shared with
        /// other users or projects.
        /// </summary>
        public static bool TrySaveSettings(UnityMcpGatewaySettings settings, out string error)
        {
            error = null;
            if (settings == null)
            {
                error = "Gateway settings are required.";
                return false;
            }

            var executablePath = string.IsNullOrWhiteSpace(settings.ExecutablePath)
                ? GetDefaultExecutablePath()
                : settings.ExecutablePath.Trim();
            var mcpPath = NormalizeMcpPath(settings.McpPath);
            if (!IsValidMcpPath(mcpPath))
            {
                error = "MCP path must start with '/' and contain only letters, digits, '-', '_' or '/'.";
                return false;
            }
            if (settings.PreferredPort < 1 || settings.PreferredPort > 65535)
            {
                error = "Preferred port must be between 1 and 65535.";
                return false;
            }

            lock (Gate)
            {
                EditorPrefs.SetString(PreferenceKey("executablePath"), executablePath);
                EditorPrefs.SetInt(PreferenceKey("preferredPort"), settings.PreferredPort);
                EditorPrefs.SetString(PreferenceKey("mcpPath"), mcpPath);
                EditorPrefs.SetBool(PreferenceKey("debugLoggingEnabled"), settings.DebugLoggingEnabled);
            }
            return true;
        }

        /// <summary>Convenience overload for callers that render validation through GetStatus().</summary>
        public static void SaveSettings(UnityMcpGatewaySettings settings)
        {
            if (!TrySaveSettings(settings, out var error)) throw new ArgumentException(error, nameof(settings));
        }

        /// <summary>
        /// Starts a Python gateway for the exact bridge descriptor owned by this Editor. It does
        /// not ever use implicit instance selection, so concurrent Unity projects stay isolated.
        /// </summary>
        public static bool Start(out string error)
        {
            return Start(out error, null, false);
        }

        /// <summary>Starts the gateway, optionally reserving one exact loopback port for a restart.</summary>
        private static bool Start(out string error, int? requiredPort, bool requireExactPort)
        {
            error = null;
            UnityMcpGatewayStatus changedStatus = null;

            lock (Gate)
            {
                restartAfterAssemblyReload = false;
                SessionState.EraseBool(SessionKey("restartAfterReload"));
                RefreshProcessStateLocked();
                if (status.IsRunning)
                {
                    error = "UnityMCP gateway is already running.";
                    return false;
                }

                var settings = GetSettings();
                if (!IsValidMcpPath(settings.McpPath))
                {
                    error = "MCP path is invalid. Save a path beginning with '/'.";
                    SetStatusLocked(CreateErrorStatus(error));
                    changedStatus = SnapshotStatusLocked();
                }
                else
                {
                    var executablePath = ResolveExecutablePath(settings.ExecutablePath);
                    if (executablePath == null)
                    {
                        var installationStatus = CreateMissingExecutableStatus(settings.ExecutablePath);
                        error = installationStatus.LastError;
                        SetStatusLocked(installationStatus);
                        changedStatus = SnapshotStatusLocked();
                    }
                    else
                    {
                        var descriptor = FindCurrentEditorDescriptor();
                        if (descriptor == null)
                        {
                            error = "UnityMCP Editor bridge is not ready. Wait for UnityMCP to finish starting, then try again.";
                            SetStatusLocked(CreateErrorStatus(error));
                            changedStatus = SnapshotStatusLocked();
                        }
                        else
                        {
                            var port = requireExactPort && requiredPort.HasValue
                                ? (IsLoopbackPortAvailable(requiredPort.Value) ? requiredPort.Value : 0)
                                : FindAvailablePort(settings.PreferredPort);
                            if (port == 0)
                            {
                                error = requireExactPort && requiredPort.HasValue
                                    ? "The existing UnityMCP gateway port is still unavailable."
                                    : "Could not reserve a free loopback port for the UnityMCP gateway.";
                                SetStatusLocked(CreateErrorStatus(error));
                                changedStatus = SnapshotStatusLocked();
                            }
                            else
                            {
                                var token = GetOrCreateBearerToken();
                                try
                                {
                                    var startInfo = BuildStartInfo(executablePath, descriptor.instanceId, port, settings.McpPath, token);
                                    var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
                                    process.Exited += OnGatewayProcessExited;
                                    process.OutputDataReceived += OnGatewayOutput;
                                    process.ErrorDataReceived += OnGatewayError;
                                    if (!process.Start())
                                    {
                                        process.Dispose();
                                        throw new InvalidOperationException("The Python gateway process did not start.");
                                    }

                                    gatewayProcess = process;
                                    expectedStop = false;
                                    processExited = false;
                                    gatewayReady = false;
                                    startedAtUtc = DateTime.UtcNow;
                                    recentLogs.Clear();
                                    PersistOwnedGatewayLocked(process, port, descriptor.instanceId);
                                    process.BeginOutputReadLine();
                                    process.BeginErrorReadLine();
                                    SetStatusLocked(new UnityMcpGatewayStatus
                                    {
                                        State = UnityMcpGatewayState.Starting,
                                        Message = $"Starting UnityMCP HTTP gateway on 127.0.0.1:{port}.",
                                        Port = port,
                                        ProcessId = process.Id,
                                        Endpoint = BuildEndpoint(port, settings.McpPath),
                                        InstanceId = descriptor.instanceId
                                    });
                                    changedStatus = SnapshotStatusLocked();
                                }
                                catch (Exception exception)
                                {
                                    error = "Could not start unity-mcp: " + Sanitize(exception.Message);
                                    DisposeGatewayProcessLocked();
                                    ClearPersistedGatewayLocked();
                                    SetStatusLocked(CreateErrorStatus(error));
                                    changedStatus = SnapshotStatusLocked();
                                }
                            }
                        }
                    }
                }
            }

            if (changedStatus != null) RaiseStatusChanged(changedStatus);
            return error == null;
        }

        /// <summary>Convenience overload for button callbacks. Inspect GetStatus() for errors.</summary>
        public static bool Start() => Start(out _);

        /// <summary>Stops only the Python gateway process started by this Editor instance.</summary>
        public static void Stop()
        {
            UnityMcpGatewayStatus changedStatus;
            lock (Gate)
            {
                restartAfterAssemblyReload = false;
                restartAfterGatewayExit = false;
                restartPort = 0;
                SessionState.EraseBool(SessionKey("restartAfterReload"));
                expectedStop = true;
                StopGatewayProcessLocked();
                ClearPersistedGatewayLocked();
                SetStatusLocked(NewStoppedStatus("Gateway is stopped."));
                changedStatus = SnapshotStatusLocked();
            }
            RaiseStatusChanged(changedStatus);
        }

        private static void StopForAssemblyReload()
        {
            UnityMcpGatewayStatus changedStatus;
            lock (Gate)
            {
                // Do not kill the child here. UnityMcpEditorBootstrap rebinds the bridge using
                // the same session descriptor after the reload, while the gateway's registry
                // poller reports a retryable reloading state and then reconnects automatically.
                // Keep the persisted process record so the next domain can reattach ownership.
                SessionState.EraseBool(SessionKey("restartAfterReload"));
                restartAfterAssemblyReload = false;
                SetStatusLocked(status.IsRunning
                    ? new UnityMcpGatewayStatus
                    {
                        State = UnityMcpGatewayState.Running,
                        Message = "Gateway remains connected while the Unity domain reloads.",
                        Port = status.Port,
                        ProcessId = status.ProcessId,
                        Endpoint = status.Endpoint,
                        InstanceId = status.InstanceId
                    }
                    : NewStoppedStatus("Gateway is stopped."));
                changedStatus = SnapshotStatusLocked();
            }
            RaiseStatusChanged(changedStatus);
        }

        public static UnityMcpGatewayStatus GetStatus()
        {
            UnityMcpGatewayStatus changedStatus = null;
            UnityMcpGatewayStatus result;
            lock (Gate)
            {
                if (RefreshProcessStateLocked()) changedStatus = SnapshotStatusLocked();
                result = SnapshotStatusLocked();
            }
            if (changedStatus != null) RaiseStatusChanged(changedStatus);
            return result;
        }

        /// <summary>
        /// Returns the endpoint and bearer token only while this Editor-owned gateway is running.
        /// Callers should expose this through an explicit copy action rather than rendering it.
        /// </summary>
        public static UnityMcpGatewayConnectionInfo GetConnectionInfo()
        {
            lock (Gate)
            {
                RefreshProcessStateLocked();
                if (status.State != UnityMcpGatewayState.Running || string.IsNullOrEmpty(status.Endpoint)) return null;
                return new UnityMcpGatewayConnectionInfo
                {
                    Endpoint = status.Endpoint,
                    BearerToken = GetOrCreateBearerToken(),
                    InstanceId = status.InstanceId
                };
            }
        }

        /// <summary>
        /// Returns connection details plus ready-to-paste project configuration fragments for
        /// Codex, Antigravity, and Claude Code. The bearer token is included, so callers must
        /// make copying this an explicit user action and must not log it.
        /// </summary>
        public static string GetClientConfigurationText()
        {
            var connection = GetConnectionInfo();
            if (connection == null || GetStatus().State != UnityMcpGatewayState.Running) return null;
            var serverName = GetProjectServerName();
            return "# Generic Streamable HTTP connection\n"
                + "# URL: " + connection.Endpoint + "\n"
                + "# Authorization: Bearer " + connection.BearerToken + "\n\n"
                + "# Codex: <project>/.codex/config.toml\n"
                + "[mcp_servers." + serverName + "]\n"
                + "url = \"" + connection.Endpoint + "\"\n"
                + "http_headers = { Authorization = \"Bearer " + connection.BearerToken + "\" }\n\n"
                + "# Antigravity: <project>/.agents/mcp_config.json\n"
                + "{\n"
                + "  \"mcpServers\": {\n"
                + "    \"" + serverName + "\": {\n"
                + "      \"serverUrl\": \"" + connection.Endpoint + "\",\n"
                + "      \"headers\": { \"Authorization\": \"Bearer " + connection.BearerToken + "\" }\n"
                + "    }\n"
                + "  }\n"
                + "}\n\n"
                + "# Claude Code: <project>/.mcp.json\n"
                + "{\n"
                + "  \"mcpServers\": {\n"
                + "    \"" + serverName + "\": {\n"
                + "      \"type\": \"http\",\n"
                + "      \"url\": \"" + connection.Endpoint + "\",\n"
                + "      \"headers\": { \"Authorization\": \"Bearer " + connection.BearerToken + "\" }\n"
                + "    }\n"
                + "  }\n"
                + "}\n";
        }

        /// <summary>
        /// Writes this running gateway and UnityMCP's proactive skill to the trusted project.
        /// The generated Codex section is marked as managed so later gateway restarts can refresh
        /// its port or token automatically. Existing unrelated Codex settings are preserved.
        /// </summary>
        public static bool TryConfigureCodexForProject(out string configPath, out string error)
        {
            configPath = null;
            error = null;
            var connection = GetConnectionInfo();
            if (connection == null)
            {
                error = "Start the UnityMCP gateway before configuring Codex for this project.";
                return false;
            }

            if (!UnityMcpCodexProjectConfig.TryWrite(
                GetProjectRootPath(),
                GetProjectServerName(),
                connection.Endpoint,
                connection.BearerToken,
                out configPath,
                out error)) return false;

            if (UnityMcpProjectSkill.TryWrite(
                GetProjectRootPath(),
                UnityMcpSkillClient.AgentSkills,
                out _,
                out var skillError)) return true;
            error = "The Codex MCP config was updated, but its project skill was not: " + skillError;
            return false;
        }

        /// <summary>Writes this gateway and skill to this project's Antigravity workspace.</summary>
        public static bool TryConfigureAntigravityForProject(out string configPath, out string error)
        {
            return TryConfigureJsonClientForProject(UnityMcpJsonClient.Antigravity, out configPath, out error);
        }

        /// <summary>Writes this gateway and skill to this project's Claude Code configuration.</summary>
        public static bool TryConfigureClaudeForProject(out string configPath, out string error)
        {
            return TryConfigureJsonClientForProject(UnityMcpJsonClient.Claude, out configPath, out error);
        }

        private static bool TryConfigureJsonClientForProject(
            UnityMcpJsonClient client,
            out string configPath,
            out string error)
        {
            configPath = null;
            error = null;
            var connection = GetConnectionInfo();
            if (connection == null)
            {
                var clientName = client == UnityMcpJsonClient.Antigravity ? "Antigravity" : "Claude";
                error = "Start the UnityMCP gateway before configuring " + clientName + " for this project.";
                return false;
            }

            if (!UnityMcpJsonProjectConfig.TryWrite(
                GetProjectRootPath(),
                GetProjectServerName(),
                connection.Endpoint,
                connection.BearerToken,
                client,
                out configPath,
                out error)) return false;

            var skillClient = client == UnityMcpJsonClient.Claude
                ? UnityMcpSkillClient.Claude
                : UnityMcpSkillClient.AgentSkills;
            if (UnityMcpProjectSkill.TryWrite(GetProjectRootPath(), skillClient, out _, out var skillError)) return true;
            error = "The MCP config was updated, but its project skill was not: " + skillError;
            return false;
        }

        /// <summary>Creates a new local bearer token. Stop the running gateway first.</summary>
        public static bool TryRegenerateBearerToken(out string error)
        {
            error = null;
            lock (Gate)
            {
                RefreshProcessStateLocked();
                if (status.IsRunning)
                {
                    error = "Stop the UnityMCP gateway before regenerating its bearer token.";
                    return false;
                }
                bearerToken = CreateBearerToken();
                EditorPrefs.SetString(PreferenceKey("httpToken"), bearerToken);
            }
            return true;
        }

        /// <summary>The recommended venv executable path for the current operating system.</summary>
        public static string GetDefaultExecutablePath()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
#if UNITY_EDITOR_WIN
            return Path.Combine(localAppData, "UnityMCP", "venv", "Scripts", "unity-mcp.exe");
#else
            return Path.Combine(localAppData, "UnityMCP", "venv", "bin", "unity-mcp");
#endif
        }

        internal static UnityMcpGatewayInstallGuide GetInstallationGuide() => UnityMcpGatewayInstallGuide.CreateDefault();

        private static void Tick()
        {
            UnityMcpGatewayStatus changedStatus = null;
            var startAfterReload = false;
            var restartForBridgeReplacement = false;
            lock (Gate)
            {
                if (RefreshProcessStateLocked()) changedStatus = SnapshotStatusLocked();

                // Normally the Editor bridge reuses its descriptor across a domain reload and
                // the gateway can remain connected. If the bridge had to fall back to a new
                // descriptor (for example its former loopback port was not available), the
                // existing gateway is permanently pinned to the old bridge. Restart it as soon
                // as the replacement bridge is ready instead of leaving every MCP call to time
                // out against the retired endpoint.
                var currentDescriptor = FindCurrentEditorDescriptor();
                if (gatewayProcess != null
                    && status.IsRunning
                    && currentDescriptor != null
                    && !string.Equals(status.InstanceId, currentDescriptor.instanceId, StringComparison.Ordinal))
                {
                    // Keep the Process handle until the old listener is really gone. Starting
                    // immediately after Kill can make FindAvailablePort choose a different
                    // port, leaving long-lived Streamable HTTP clients on the retired URL.
                    restartAfterGatewayExit = true;
                    restartPort = status.Port;
                    expectedStop = true;
                    KillGatewayProcessLocked();
                    SetStatusLocked(new UnityMcpGatewayStatus
                    {
                        State = UnityMcpGatewayState.Starting,
                        Message = "Unity bridge identity changed; waiting to restart the gateway on its existing port.",
                        Port = status.Port,
                        ProcessId = status.ProcessId,
                        Endpoint = status.Endpoint,
                        InstanceId = currentDescriptor.instanceId
                    });
                    changedStatus = SnapshotStatusLocked();
                }

                if (restartAfterGatewayExit && gatewayProcess == null && FindCurrentEditorDescriptor() != null)
                {
                    restartAfterGatewayExit = false;
                    restartForBridgeReplacement = true;
                }

                if (restartAfterAssemblyReload)
                {
                    if (FindCurrentEditorDescriptor() != null)
                    {
                        restartAfterAssemblyReload = false;
                        startAfterReload = true;
                    }
                    else if (DateTime.UtcNow >= restartDeadlineUtc)
                    {
                        restartAfterAssemblyReload = false;
                        SetStatusLocked(CreateErrorStatus("UnityMCP bridge did not become ready after the domain reload."));
                        changedStatus = SnapshotStatusLocked();
                    }
                }
            }
            if (changedStatus != null && changedStatus.State == UnityMcpGatewayState.Running)
                RefreshManagedProjectConfigurations();
            if (changedStatus != null) RaiseStatusChanged(changedStatus);
            if (startAfterReload) Start(out _);
            if (restartForBridgeReplacement) Start(out _, restartPort, true);
        }

        private static void RefreshManagedProjectConfigurations()
        {
            var projectRoot = GetProjectRootPath();
            var serverName = GetProjectServerName();
            if (UnityMcpCodexProjectConfig.IsManaged(projectRoot, serverName)
                && !TryConfigureCodexForProject(out _, out var codexError)
                && !string.IsNullOrWhiteSpace(codexError))
                UnityEngine.Debug.LogWarning("UnityMCP could not refresh the local Codex project config: " + Sanitize(codexError));
            if (UnityMcpJsonProjectConfig.IsManaged(projectRoot, serverName, UnityMcpJsonClient.Antigravity)
                && !TryConfigureAntigravityForProject(out _, out var antigravityError)
                && !string.IsNullOrWhiteSpace(antigravityError))
                UnityEngine.Debug.LogWarning("UnityMCP could not refresh the local Antigravity project config: " + Sanitize(antigravityError));
            if (UnityMcpJsonProjectConfig.IsManaged(projectRoot, serverName, UnityMcpJsonClient.Claude)
                && !TryConfigureClaudeForProject(out _, out var claudeError)
                && !string.IsNullOrWhiteSpace(claudeError))
                UnityEngine.Debug.LogWarning("UnityMCP could not refresh the local Claude project config: " + Sanitize(claudeError));
        }

        private static bool RefreshProcessStateLocked()
        {
            if (gatewayProcess == null) return false;
            var exited = processExited;
            try { exited |= gatewayProcess.HasExited; }
            catch { exited = true; }
            if (exited)
            {
                var exitCode = TryGetExitCode(gatewayProcess);
                var message = expectedStop
                    ? "Gateway is stopped."
                    : BuildExitedMessage(exitCode);
                DisposeGatewayProcessLocked();
                ClearPersistedGatewayLocked();
                SetStatusLocked(expectedStop ? NewStoppedStatus(message) : CreateErrorStatus(message));
                expectedStop = false;
                processExited = false;
                return true;
            }

            if (status.State == UnityMcpGatewayState.Starting && gatewayReady)
            {
                status.State = UnityMcpGatewayState.Running;
                status.Message = $"UnityMCP HTTP gateway is running on {status.Endpoint}.";
                return true;
            }
            if (status.State == UnityMcpGatewayState.Starting && DateTime.UtcNow - startedAtUtc > StartupTimeout)
            {
                expectedStop = true;
                StopGatewayProcessLocked();
                ClearPersistedGatewayLocked();
                SetStatusLocked(CreateErrorStatus("unity-mcp did not report readiness within 15 seconds."));
                return true;
            }
            return false;
        }

        private static ProcessStartInfo BuildStartInfo(string executablePath, string instanceId, int port, string mcpPath, string token)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = GetProjectRootPath(),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = false
            };
            // Unity 6 exposes ArgumentList, so the executable path and every argument remain
            // separate OS tokens. Keep this list fixed; do not append user-provided arbitrary
            // arguments. instanceId is a bridge GUID, parent PID/port are integers, and mcpPath
            // is validated before reaching this method.
            startInfo.ArgumentList.Add("--transport");
            startInfo.ArgumentList.Add("streamable-http");
            startInfo.ArgumentList.Add("--instance");
            startInfo.ArgumentList.Add(instanceId);
            startInfo.ArgumentList.Add("--parent-pid");
            startInfo.ArgumentList.Add(GetCurrentProcessId().ToString(System.Globalization.CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add("--port");
            startInfo.ArgumentList.Add(port.ToString(System.Globalization.CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add("--mcp-path");
            startInfo.ArgumentList.Add(mcpPath);
            startInfo.ArgumentList.Add("--log-level");
            startInfo.ArgumentList.Add("WARNING");
            // Never pass the token through argv, where it is exposed in the process list.
            startInfo.EnvironmentVariables[TokenEnvironmentVariable] = token;
            return startInfo;
        }

        private static UnityMcpInstanceDescriptor FindCurrentEditorDescriptor()
        {
            var currentPid = GetCurrentProcessId();
            var descriptor = UnityMcpEditorBootstrap.Descriptor;
            if (descriptor == null || descriptor.pid != currentPid || descriptor.port < 1 || descriptor.port > 65535) return null;
            if (!string.Equals(descriptor.kind, "editor", StringComparison.Ordinal) || !IsSafeArgumentToken(descriptor.instanceId)) return null;
            return descriptor;
        }

        private static int FindAvailablePort(int preferredPort)
        {
            var first = ClampPort(preferredPort);
            for (var offset = 0; offset < PortProbeCount && first + offset <= 65535; offset++)
            {
                var candidate = first + offset;
                if (IsLoopbackPortAvailable(candidate)) return candidate;
            }

            // The OS chooses an ephemeral loopback port if the preferred range is busy.
            try
            {
                var listener = new TcpListener(IPAddress.Loopback, 0);
                try
                {
                    listener.Start();
                    return ((IPEndPoint)listener.LocalEndpoint).Port;
                }
                finally { listener.Stop(); }
            }
            catch { return 0; }
        }

        private static bool IsLoopbackPortAvailable(int port)
        {
            try
            {
                var listener = new TcpListener(IPAddress.Loopback, port);
                try
                {
                    listener.Start();
                    return true;
                }
                finally { listener.Stop(); }
            }
            catch (SocketException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        private static string ResolveExecutablePath(string configuredPath)
        {
            var fullPath = GetExpectedExecutablePath(configuredPath);
            return !string.IsNullOrEmpty(fullPath) && File.Exists(fullPath) ? fullPath : null;
        }

        private static string GetExpectedExecutablePath(string configuredPath)
        {
            if (string.IsNullOrWhiteSpace(configuredPath)) configuredPath = GetDefaultExecutablePath();
            try
            {
                return Path.GetFullPath(configuredPath.Trim());
            }
            catch { return configuredPath.Trim(); }
        }

        private static void OnGatewayProcessExited(object sender, EventArgs args)
        {
            lock (Gate)
            {
                if (ReferenceEquals(sender, gatewayProcess)) processExited = true;
            }
        }

        private static void OnGatewayOutput(object sender, DataReceivedEventArgs args) => AddLogLine(args.Data);
        private static void OnGatewayError(object sender, DataReceivedEventArgs args) => AddLogLine(args.Data);

        private static void AddLogLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            lock (Gate)
            {
                if (line.StartsWith("UNITY_MCP_READY ", StringComparison.Ordinal)) gatewayReady = true;
                recentLogs.Enqueue(Sanitize(line));
                while (recentLogs.Count > MaxLogLines) recentLogs.Dequeue();
            }
        }

        private static void StopGatewayProcessLocked()
        {
            if (gatewayProcess == null) return;
            KillGatewayProcessLocked();
            DisposeGatewayProcessLocked();
            processExited = false;
            gatewayReady = false;
            expectedStop = false;
        }

        private static void KillGatewayProcessLocked()
        {
            if (gatewayProcess == null) return;
            try
            {
                if (!gatewayProcess.HasExited)
                {
                    gatewayProcess.Kill();
                    // This method runs from Editor UI callbacks and Unity lifecycle events.
                    // Waiting for Python to terminate here can block the Editor while a domain
                    // reload waits for this assembly to return (and has previously left the
                    // Stop gateway button stuck). Kill is asynchronous; release our handle and
                    // let the OS finish process teardown in the background.
                }
            }
            catch { /* The process may have exited concurrently. */ }
        }

        private static void DisposeGatewayProcessLocked()
        {
            if (gatewayProcess == null) return;
            try
            {
                gatewayProcess.Exited -= OnGatewayProcessExited;
                gatewayProcess.OutputDataReceived -= OnGatewayOutput;
                gatewayProcess.ErrorDataReceived -= OnGatewayError;
                gatewayProcess.Dispose();
            }
            catch { }
            gatewayProcess = null;
        }

        private static void RecoverPersistedGatewayIfOwned()
        {
            var rawPid = SessionState.GetString(SessionKey("pid"), string.Empty);
            var rawStartedTicks = SessionState.GetString(SessionKey("startedTicks"), string.Empty);
            var rawPort = SessionState.GetString(SessionKey("port"), string.Empty);
            var instanceId = SessionState.GetString(SessionKey("instanceId"), string.Empty);
            if (!int.TryParse(rawPid, out var pid)
                || !long.TryParse(rawStartedTicks, out var startedTicks)
                || !int.TryParse(rawPort, out var port)
                || port < 1 || port > 65535
                || !IsSafeArgumentToken(instanceId))
            {
                ClearPersistedGateway();
                return;
            }
            try
            {
                var process = Process.GetProcessById(pid);
                if (process.HasExited || process.StartTime.ToUniversalTime().Ticks != startedTicks)
                {
                    process.Dispose();
                    ClearPersistedGateway();
                    return;
                }

                process.EnableRaisingEvents = true;
                process.Exited += OnGatewayProcessExited;
                gatewayProcess = process;
                expectedStop = false;
                processExited = false;
                gatewayReady = true;
                var endpoint = BuildEndpoint(port, GetSettings().McpPath);
                SetStatusLocked(new UnityMcpGatewayStatus
                {
                    State = UnityMcpGatewayState.Running,
                    Message = "UnityMCP HTTP gateway recovered after the Unity domain reload.",
                    Port = port,
                    ProcessId = pid,
                    Endpoint = endpoint,
                    InstanceId = instanceId
                });
            }
            catch
            {
                // The PID may be gone, reused, or inaccessible. Never touch an unverified
                // process; simply discard the stale ownership record.
                ClearPersistedGateway();
            }
        }

        private static void PersistOwnedGatewayLocked(Process process, int port, string instanceId)
        {
            SessionState.SetString(SessionKey("pid"), process.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
            try { SessionState.SetString(SessionKey("startedTicks"), process.StartTime.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture)); }
            catch { SessionState.EraseString(SessionKey("startedTicks")); }
            SessionState.SetString(SessionKey("port"), port.ToString(System.Globalization.CultureInfo.InvariantCulture));
            SessionState.SetString(SessionKey("instanceId"), instanceId ?? string.Empty);
        }

        private static void ClearPersistedGatewayLocked() => ClearPersistedGateway();

        private static void ClearPersistedGateway()
        {
            SessionState.EraseString(SessionKey("pid"));
            SessionState.EraseString(SessionKey("startedTicks"));
            SessionState.EraseString(SessionKey("port"));
            SessionState.EraseString(SessionKey("instanceId"));
        }

        private static string GetOrCreateBearerToken()
        {
            if (IsValidBearerToken(bearerToken)) return bearerToken;
            var key = PreferenceKey("httpToken");
            var existing = EditorPrefs.GetString(key, string.Empty);
            if (IsValidBearerToken(existing)) return bearerToken = existing;
            var token = CreateBearerToken();
            EditorPrefs.SetString(key, token);
            return bearerToken = token;
        }

        private static string CreateBearerToken()
        {
            var bytes = new byte[48];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static bool IsValidBearerToken(string value)
        {
            return !string.IsNullOrEmpty(value) && value.Length >= 32 && value.Length <= 512 && value.All(character => character >= 33 && character <= 126);
        }

        private static void SetStatusLocked(UnityMcpGatewayStatus next) => status = next;

        private static UnityMcpGatewayStatus SnapshotStatusLocked()
        {
            return new UnityMcpGatewayStatus
            {
                State = status.State,
                Message = status.Message,
                LastError = status.LastError,
                RequiresInstallation = status.RequiresInstallation,
                ExpectedExecutablePath = status.ExpectedExecutablePath,
                Port = status.Port,
                ProcessId = status.ProcessId,
                Endpoint = status.Endpoint,
                InstanceId = status.InstanceId
            };
        }

        private static UnityMcpGatewayStatus NewStoppedStatus(string message) => new UnityMcpGatewayStatus
        {
            State = UnityMcpGatewayState.Stopped,
            Message = message
        };

        internal static UnityMcpGatewayStatus CreateErrorStatus(string message) => new UnityMcpGatewayStatus
        {
            State = UnityMcpGatewayState.Error,
            Message = message,
            LastError = message
        };

        internal static UnityMcpGatewayStatus CreateMissingExecutableStatus(string configuredPath)
        {
            var expectedExecutablePath = GetExpectedExecutablePath(configuredPath);
            var message = "UnityMCP server is not installed. Expected the gateway executable at '"
                + expectedExecutablePath + "'.";
            return new UnityMcpGatewayStatus
            {
                State = UnityMcpGatewayState.Error,
                Message = message,
                LastError = message,
                RequiresInstallation = true,
                ExpectedExecutablePath = expectedExecutablePath
            };
        }

        private static void RaiseStatusChanged(UnityMcpGatewayStatus changedStatus)
        {
            var listeners = StatusChanged;
            if (listeners == null) return;
            foreach (Action<UnityMcpGatewayStatus> listener in listeners.GetInvocationList())
            {
                try { listener(changedStatus); }
                catch (Exception exception) { UnityEngine.Debug.LogException(exception); }
            }
        }

        private static string BuildExitedMessage(int? exitCode)
        {
            var suffix = exitCode.HasValue ? " (exit code " + exitCode.Value + ")." : ".";
            var logs = recentLogs.Count == 0 ? string.Empty : " " + recentLogs.Last();
            return "unity-mcp stopped unexpectedly" + suffix + logs;
        }

        private static int? TryGetExitCode(Process process)
        {
            try { return process.ExitCode; }
            catch { return null; }
        }

        private static string BuildEndpoint(int port, string mcpPath) => "http://127.0.0.1:" + port + mcpPath;

        private static string NormalizeMcpPath(string value)
        {
            var result = string.IsNullOrWhiteSpace(value) ? DefaultMcpPath : value.Trim();
            if (!result.StartsWith("/", StringComparison.Ordinal)) result = "/" + result;
            return result;
        }

        private static bool IsValidMcpPath(string value)
        {
            if (string.IsNullOrEmpty(value) || !value.StartsWith("/", StringComparison.Ordinal) || value.Contains("..")) return false;
            return value.All(character => char.IsLetterOrDigit(character) || character == '/' || character == '-' || character == '_');
        }

        private static bool IsSafeArgumentToken(string value)
        {
            return !string.IsNullOrEmpty(value) && value.All(character => char.IsLetterOrDigit(character) || character == '-' || character == '_');
        }

        private static int ClampPort(int value) => value >= 1 && value <= 65535 ? value : DefaultPort;

        private static string ToConfigIdentifier(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "project";
            var builder = new StringBuilder(value.Length);
            foreach (var character in value.ToLowerInvariant())
                builder.Append(character >= 'a' && character <= 'z' || character >= '0' && character <= '9' ? character : '_');
            var result = builder.ToString().Trim('_');
            return string.IsNullOrEmpty(result) ? "project" : result;
        }

        private static string GetProjectServerName()
        {
            return "unity_" + ToConfigIdentifier(Path.GetFileName(GetProjectRootPath())) + "_" + ProjectKey.Substring(0, 6);
        }

        // Fresh projects choose different stable defaults so two Editors are unlikely to contend
        // for 8765. An explicit per-project preference always wins.
        private static int GetDefaultPreferredPort()
        {
            var hashPrefix = ProjectKey.Substring(0, Math.Min(6, ProjectKey.Length));
            return DefaultPort + (int.Parse(hashPrefix, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture) % 1000);
        }

        private static string PreferenceKey(string name) => ProductKeyPrefix + ProjectKey + "." + name;
        private static string SessionKey(string name) => SessionKeyPrefix + "." + name;

        private static string BuildProjectKey() => Hash(GetProjectRoot()).Substring(0, 16);

        private static string BuildSessionKeyPrefix()
        {
            return ProductKeyPrefix + ProjectKey + "." + GetCurrentProcessId().ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string GetProjectRoot()
        {
            return GetProjectRootPath().ToLowerInvariant();
        }

        private static string GetProjectRootPath()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, ".."))
                .Replace('\\', '/')
                .TrimEnd('/');
        }

        private static string Hash(string value)
        {
            using (var sha = SHA256.Create())
            {
                return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(value)).Select(item => item.ToString("x2")));
            }
        }

        private static int GetCurrentProcessId()
        {
            using (var process = Process.GetCurrentProcess()) return process.Id;
        }

        private static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var sanitized = IsValidBearerToken(bearerToken) ? value.Replace(bearerToken, "[redacted]") : value;
            return sanitized.Length <= 500 ? sanitized : sanitized.Substring(0, 500) + "…";
        }
    }
}
