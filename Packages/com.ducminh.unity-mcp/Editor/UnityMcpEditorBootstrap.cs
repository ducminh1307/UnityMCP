using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DucMinh.UnityMcp.Editor
{
    internal sealed class EditorEnablementStore : IUnityMcpEnablementStore
    {
        private readonly string prefix;
        public EditorEnablementStore()
        {
            using (var sha = SHA256.Create())
                prefix = "DucMinh.UnityMcp." + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Application.dataPath))).Replace("-", "").Substring(0, 16) + ".tool.";
        }

        public bool? GetOverride(string toolName)
        {
            var key = prefix + toolName;
            if (!EditorPrefs.HasKey(key)) return null;
            return EditorPrefs.GetBool(key);
        }

        public void SetOverride(string toolName, bool enabled) => EditorPrefs.SetBool(prefix + toolName, enabled);
    }

    [InitializeOnLoad]
    internal static class UnityMcpEditorBootstrap
    {
        private static UnityMcpHttpServer server;
        private const string SessionDescriptorKey = "DucMinh.UnityMcp.EditorDescriptor";
        internal static UnityMcpRegistry Registry { get; private set; }
        internal static UnityMcpInstanceDescriptor Descriptor => server == null ? null : server.Descriptor;

        static UnityMcpEditorBootstrap()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.quitting += Stop;
            EditorApplication.update += UnityMcpMainThread.Pump;
            EditorApplication.delayCall += Start;
        }

        private static void Start()
        {
            if (server != null) return;
            UnityMcpMainThread.Initialize(false);
            UnityMcpRegistry.DiscoveryOverride = () => TypeCache.GetMethodsWithAttribute<UnityMcpToolAttribute>().Cast<MethodInfo>();
            Registry = new UnityMcpRegistry(UnityMcpScope.Editor, new EditorEnablementStore());
            Registry.Reload();
            server = new UnityMcpHttpServer(Registry, UnityMcpScope.Editor);
            UnityMcpInstanceDescriptor preferred = null;
            var stored = SessionState.GetString(SessionDescriptorKey, string.Empty);
            if (!string.IsNullOrEmpty(stored))
            {
                try { preferred = JsonConvert.DeserializeObject<UnityMcpInstanceDescriptor>(stored); } catch { }
            }
            try { server.Start(preferred); }
            catch when (preferred != null) { server.Dispose(); server = new UnityMcpHttpServer(Registry, UnityMcpScope.Editor); server.Start(); }
            SessionState.SetString(SessionDescriptorKey, JsonConvert.SerializeObject(server.Descriptor));
        }

        private static void Stop()
        {
            server?.Dispose();
            server = null;
        }
    }

    public sealed class UnityMcpToolsWindow : EditorWindow
    {
        private const string RuntimeProfilePath = "Assets/UnityMCP/Resources/UnityMcpRuntimeProfile.asset";
        private const string GatewayConfigClipboardWarning = "The copied configuration contains a local bearer token. Do not commit it.";

        private readonly Color stoppedColor = new Color(0.48f, 0.48f, 0.48f);
        private readonly Color startingColor = new Color(0.88f, 0.62f, 0.12f);
        private readonly Color runningColor = new Color(0.22f, 0.67f, 0.35f);
        private readonly Color errorColor = new Color(0.84f, 0.27f, 0.24f);

        private ScrollView content;
        private VisualElement toolsContainer;
        private Label registryStatusLabel;
        private Label runtimeProfileStatusLabel;
        private Label gatewayStatusLabel;
        private Label gatewayDetailLabel;
        private Label gatewayFeedbackLabel;
        private TextField executablePathField;
        private IntegerField portField;
        private TextField mcpPathField;
        private Button startGatewayButton;
        private Button stopGatewayButton;
        private Button copyGatewayConfigButton;
        private Button rotateGatewayTokenButton;
        private UnityMcpRegistry observedRegistry;
        private IVisualElementScheduledItem refreshSchedule;

        [MenuItem("Window/UnityMCP/Tools")]
        public static void Open() => GetWindow<UnityMcpToolsWindow>("UnityMCP Tools");

        private void OnEnable()
        {
            titleContent = new GUIContent("UnityMCP Tools");
        }

        private void OnDisable()
        {
            DetachRegistry();
            UnityMcpGatewayHost.StatusChanged -= OnGatewayStatusChanged;
            refreshSchedule?.Pause();
            refreshSchedule = null;
        }

        public void CreateGUI()
        {
            DetachRegistry();
            UnityMcpGatewayHost.StatusChanged -= OnGatewayStatusChanged;
            UnityMcpGatewayHost.StatusChanged += OnGatewayStatusChanged;

            rootVisualElement.Clear();
            rootVisualElement.style.flexGrow = 1;
            rootVisualElement.style.paddingLeft = 12;
            rootVisualElement.style.paddingRight = 12;
            rootVisualElement.style.paddingTop = 10;
            rootVisualElement.style.paddingBottom = 10;

            content = new ScrollView(ScrollViewMode.Vertical);
            content.style.flexGrow = 1;
            rootVisualElement.Add(content);

            var title = new Label("UnityMCP")
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    fontSize = 18,
                    marginBottom = 2
                }
            };
            content.Add(title);
            AddMutedLabel(content, "Manage this project's optional HTTP gateway, local tool permissions, and Development Player profile.");
            content.Add(CreateInfoBox("Tool enablement is local to this user and project. Custom and mutating tools start disabled and must be explicitly enabled here."));

            BuildGatewaySection();
            BuildRuntimeProfileSection();
            BuildToolsSection();

            LoadGatewaySettings();
            RefreshAll();
            refreshSchedule?.Pause();
            refreshSchedule = rootVisualElement.schedule.Execute(RefreshAll).Every(1000);
        }

        private void BuildGatewaySection()
        {
            var section = CreateSection("Editor-managed HTTP gateway", "Optional: start a Streamable HTTP gateway for this exact Unity Editor instance. STDIO remains client-managed.");

            var statusRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 4 } };
            gatewayStatusLabel = new Label { style = { unityFontStyleAndWeight = FontStyle.Bold, marginRight = 8 } };
            gatewayDetailLabel = new Label { style = { flexGrow = 1, whiteSpace = WhiteSpace.Normal } };
            statusRow.Add(gatewayStatusLabel);
            statusRow.Add(gatewayDetailLabel);
            section.Add(statusRow);

            executablePathField = new TextField("Gateway executable") { isDelayed = true, tooltip = "Path to unity-mcp.exe (or unity-mcp on macOS/Linux)." };
            executablePathField.style.marginBottom = 3;
            section.Add(executablePathField);
            var executableActions = new VisualElement { style = { flexDirection = FlexDirection.Row, marginLeft = 150, marginBottom = 6 } };
            var browseButton = new Button(BrowseForGatewayExecutable) { text = "Browse…", tooltip = "Choose unity-mcp.exe installed in the Python virtual environment." };
            var useDefaultButton = new Button(() => executablePathField.value = UnityMcpGatewayHost.GetDefaultExecutablePath()) { text = "Use default" };
            browseButton.style.marginRight = 4;
            executableActions.Add(browseButton);
            executableActions.Add(useDefaultButton);
            section.Add(executableActions);

            var endpointFields = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 6 } };
            portField = new IntegerField("Preferred port") { isDelayed = true, tooltip = "The gateway will use this loopback port, or select another free local port if it is occupied." };
            portField.style.flexGrow = 1;
            portField.style.marginRight = 8;
            mcpPathField = new TextField("MCP path") { isDelayed = true, tooltip = "Local Streamable HTTP path, normally /mcp." };
            mcpPathField.style.flexGrow = 1;
            endpointFields.Add(portField);
            endpointFields.Add(mcpPathField);
            section.Add(endpointFields);

            var gatewayButtons = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, marginBottom = 5 } };
            var saveGatewayButton = new Button(() => { SaveGatewaySettings(); }) { text = "Save settings" };
            startGatewayButton = new Button(StartGateway) { text = "Start gateway" };
            stopGatewayButton = new Button(StopGateway) { text = "Stop gateway" };
            copyGatewayConfigButton = new Button(CopyGatewayClientConfiguration) { text = "Copy client config" };
            rotateGatewayTokenButton = new Button(RegenerateGatewayToken) { text = "Regenerate token" };
            AddButtonRow(gatewayButtons, saveGatewayButton, startGatewayButton, stopGatewayButton, copyGatewayConfigButton, rotateGatewayTokenButton);
            section.Add(gatewayButtons);

            gatewayFeedbackLabel = new Label { style = { whiteSpace = WhiteSpace.Normal, display = DisplayStyle.None } };
            section.Add(gatewayFeedbackLabel);
            content.Add(section);
        }

        private void BuildRuntimeProfileSection()
        {
            var section = CreateSection("Development Player runtime profile", "Runtime tools only run in desktop Development Players and need a profile baked into the build.");
            runtimeProfileStatusLabel = new Label { style = { whiteSpace = WhiteSpace.Normal, marginBottom = 5 } };
            section.Add(runtimeProfileStatusLabel);
            var buttons = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            var createButton = new Button(() =>
            {
                CreateRuntimeProfile();
                RefreshRuntimeProfile();
            }) { text = "Create runtime profile" };
            var selectButton = new Button(SelectRuntimeProfile) { text = "Select runtime profile" };
            AddButtonRow(buttons, createButton, selectButton);
            section.Add(buttons);
            content.Add(section);
        }

        private void BuildToolsSection()
        {
            var section = CreateSection("Tool permissions", "Only enabled tools are advertised to connected MCP clients.");
            var actions = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 6 } };
            var reloadButton = new Button(ReloadRegistry) { text = "Reload tool registry" };
            registryStatusLabel = new Label { style = { flexGrow = 1, marginLeft = 8, whiteSpace = WhiteSpace.Normal } };
            actions.Add(reloadButton);
            actions.Add(registryStatusLabel);
            section.Add(actions);

            toolsContainer = new VisualElement { style = { flexDirection = FlexDirection.Column } };
            section.Add(toolsContainer);
            content.Add(section);
        }

        private void RefreshAll()
        {
            ObserveRegistry();
            RefreshGatewayStatus(UnityMcpGatewayHost.GetStatus());
            RefreshRuntimeProfile();
        }

        private void ObserveRegistry()
        {
            var registry = UnityMcpEditorBootstrap.Registry;
            if (ReferenceEquals(registry, observedRegistry)) return;
            DetachRegistry();
            observedRegistry = registry;
            if (observedRegistry != null) observedRegistry.Changed += OnRegistryChanged;
            RebuildToolList();
        }

        private void DetachRegistry()
        {
            if (observedRegistry != null) observedRegistry.Changed -= OnRegistryChanged;
            observedRegistry = null;
        }

        private void OnRegistryChanged() => RebuildToolList();

        private void ReloadRegistry()
        {
            var registry = UnityMcpEditorBootstrap.Registry;
            if (registry == null)
            {
                SetRegistryStatus("UnityMCP is still starting.");
                return;
            }
            registry.Reload();
            RebuildToolList();
        }

        private void RebuildToolList()
        {
            if (toolsContainer == null) return;
            toolsContainer.Clear();
            var registry = UnityMcpEditorBootstrap.Registry;
            if (registry == null)
            {
                SetRegistryStatus("UnityMCP is starting. Tool permissions will appear shortly.");
                toolsContainer.Add(CreateInfoBox("Waiting for the UnityMCP tool registry."));
                return;
            }

            var tools = registry.Tools;
            SetRegistryStatus($"{tools.Count(tool => tool.enabled)} of {tools.Count} tools enabled · revision {ShortRevision(registry.RegistryRevision)}");
            foreach (var group in tools.GroupBy(tool => string.IsNullOrEmpty(tool.category) ? "project" : tool.category, StringComparer.OrdinalIgnoreCase).OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
            {
                var category = new Foldout { text = $"{group.Key} ({group.Count()})", value = true };
                category.style.marginBottom = 4;
                foreach (var tool in group.OrderBy(tool => tool.name, StringComparer.Ordinal))
                    category.Add(CreateToolRow(registry, tool));
                toolsContainer.Add(category);
            }
        }

        private VisualElement CreateToolRow(UnityMcpRegistry registry, UnityMcpToolDescriptor tool)
        {
            var row = new VisualElement
            {
                style =
                {
                    marginLeft = 10,
                    marginTop = 2,
                    marginBottom = 3,
                    paddingLeft = 6,
                    paddingTop = 3,
                    paddingBottom = 3,
                    borderLeftWidth = 2,
                    borderLeftColor = ToolSafetyColor(tool.safety)
                }
            };
            var toggle = new Toggle(tool.name) { value = tool.enabled, tooltip = tool.description };
            toggle.RegisterValueChangedCallback(change =>
            {
                if (change.newValue == tool.enabled) return;
                registry.SetEnabled(tool.name, change.newValue);
            });
            row.Add(toggle);
            var detail = new Label(BuildToolDetail(tool)) { style = { marginLeft = 22, fontSize = 10, color = new Color(0.63f, 0.63f, 0.63f), whiteSpace = WhiteSpace.Normal } };
            row.Add(detail);
            return row;
        }

        private void LoadGatewaySettings()
        {
            var settings = UnityMcpGatewayHost.GetSettings();
            executablePathField?.SetValueWithoutNotify(settings.ExecutablePath);
            portField?.SetValueWithoutNotify(settings.PreferredPort);
            mcpPathField?.SetValueWithoutNotify(settings.McpPath);
        }

        private bool SaveGatewaySettings()
        {
            var settings = new UnityMcpGatewaySettings
            {
                ExecutablePath = executablePathField?.value,
                PreferredPort = portField?.value ?? 0,
                McpPath = mcpPathField?.value
            };
            if (UnityMcpGatewayHost.TrySaveSettings(settings, out var error))
            {
                SetGatewayFeedback("Gateway settings saved.", false);
                LoadGatewaySettings();
                return true;
            }
            SetGatewayFeedback(error, true);
            return false;
        }

        private void BrowseForGatewayExecutable()
        {
            var current = executablePathField?.value;
            var directory = string.IsNullOrWhiteSpace(current) ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) : System.IO.Path.GetDirectoryName(current);
            var selected = EditorUtility.OpenFilePanel("Select unity-mcp executable", directory ?? string.Empty, string.Empty);
            if (!string.IsNullOrWhiteSpace(selected)) executablePathField.value = selected;
        }

        private void StartGateway()
        {
            if (!SaveGatewaySettings()) return;
            if (!UnityMcpGatewayHost.Start(out var error)) SetGatewayFeedback(error, true);
            else SetGatewayFeedback("Starting the local UnityMCP HTTP gateway…", false);
            RefreshGatewayStatus(UnityMcpGatewayHost.GetStatus());
        }

        private void StopGateway()
        {
            UnityMcpGatewayHost.Stop();
            SetGatewayFeedback("Gateway stopped.", false);
            RefreshGatewayStatus(UnityMcpGatewayHost.GetStatus());
        }

        private void CopyGatewayClientConfiguration()
        {
            var configuration = UnityMcpGatewayHost.GetClientConfigurationText();
            if (string.IsNullOrWhiteSpace(configuration))
            {
                SetGatewayFeedback("Start the gateway before copying client configuration.", true);
                return;
            }

            EditorGUIUtility.systemCopyBuffer = configuration;
            SetGatewayFeedback(GatewayConfigClipboardWarning, false);
            ShowNotification(new GUIContent("Client configuration copied."));
        }

        private void RegenerateGatewayToken()
        {
            if (!UnityMcpGatewayHost.TryRegenerateBearerToken(out var error))
            {
                SetGatewayFeedback(error, true);
                return;
            }
            SetGatewayFeedback("A new local bearer token was created. Start the gateway and copy a new client configuration.", false);
        }

        private void OnGatewayStatusChanged(UnityMcpGatewayStatus status) => RefreshGatewayStatus(status);

        private void RefreshGatewayStatus(UnityMcpGatewayStatus status)
        {
            if (gatewayStatusLabel == null || gatewayDetailLabel == null || status == null) return;
            gatewayStatusLabel.text = GatewayStateLabel(status.State);
            gatewayStatusLabel.style.color = GatewayStateColor(status.State);
            gatewayDetailLabel.text = string.IsNullOrWhiteSpace(status.Message) ? "Gateway status unavailable." : status.Message;

            var isRunning = status.IsRunning;
            startGatewayButton?.SetEnabled(!isRunning);
            stopGatewayButton?.SetEnabled(isRunning);
            copyGatewayConfigButton?.SetEnabled(status.State == UnityMcpGatewayState.Running);
            rotateGatewayTokenButton?.SetEnabled(!isRunning);
        }

        private void RefreshRuntimeProfile()
        {
            if (runtimeProfileStatusLabel == null) return;
            var profile = AssetDatabase.LoadAssetAtPath<UnityMcpRuntimeProfile>(RuntimeProfilePath);
            runtimeProfileStatusLabel.text = profile == null
                ? "No runtime profile has been created for this project."
                : "Runtime profile: " + RuntimeProfilePath;
        }

        private static void SelectRuntimeProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<UnityMcpRuntimeProfile>(RuntimeProfilePath);
            if (profile == null)
            {
                EditorUtility.DisplayDialog("UnityMCP runtime profile", "Create a Development Player runtime profile first.", "OK");
                return;
            }
            Selection.activeObject = profile;
            EditorGUIUtility.PingObject(profile);
        }

        private void SetRegistryStatus(string value)
        {
            if (registryStatusLabel != null) registryStatusLabel.text = value ?? string.Empty;
        }

        private void SetGatewayFeedback(string value, bool isError)
        {
            if (gatewayFeedbackLabel == null) return;
            gatewayFeedbackLabel.text = value ?? string.Empty;
            gatewayFeedbackLabel.style.color = isError ? errorColor : runningColor;
            gatewayFeedbackLabel.style.display = string.IsNullOrWhiteSpace(value) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private VisualElement CreateSection(string title, string description)
        {
            var section = new VisualElement
            {
                style =
                {
                    marginTop = 12,
                    paddingLeft = 10,
                    paddingRight = 10,
                    paddingTop = 9,
                    paddingBottom = 9,
                    borderLeftWidth = 1,
                    borderRightWidth = 1,
                    borderTopWidth = 1,
                    borderBottomWidth = 1,
                    borderLeftColor = new Color(0.35f, 0.35f, 0.35f),
                    borderRightColor = new Color(0.35f, 0.35f, 0.35f),
                    borderTopColor = new Color(0.35f, 0.35f, 0.35f),
                    borderBottomColor = new Color(0.35f, 0.35f, 0.35f)
                }
            };
            var heading = new Label(title) { style = { unityFontStyleAndWeight = FontStyle.Bold, fontSize = 13, marginBottom = 2 } };
            section.Add(heading);
            AddMutedLabel(section, description, 5);
            return section;
        }

        private static Label CreateInfoBox(string text)
        {
            return new Label(text)
            {
                style =
                {
                    whiteSpace = WhiteSpace.Normal,
                    marginTop = 8,
                    marginBottom = 2,
                    paddingLeft = 8,
                    paddingRight = 8,
                    paddingTop = 6,
                    paddingBottom = 6,
                    borderLeftWidth = 3,
                    borderLeftColor = new Color(0.25f, 0.53f, 0.83f),
                    backgroundColor = new Color(0.12f, 0.18f, 0.25f, 0.35f)
                }
            };
        }

        private static void AddMutedLabel(VisualElement parent, string text, int bottomMargin = 0)
        {
            parent.Add(new Label(text)
            {
                style =
                {
                    color = new Color(0.63f, 0.63f, 0.63f),
                    fontSize = 11,
                    whiteSpace = WhiteSpace.Normal,
                    marginBottom = bottomMargin
                }
            });
        }

        private static void AddButtonRow(VisualElement parent, params Button[] buttons)
        {
            foreach (var button in buttons)
            {
                button.style.marginRight = 4;
                button.style.marginBottom = 3;
                parent.Add(button);
            }
        }

        private static string BuildToolDetail(UnityMcpToolDescriptor tool)
        {
            var flags = new List<string> { tool.safety, tool.source };
            if (tool.supportsDryRun) flags.Add("dry-run");
            if (tool.returnsJob) flags.Add("job");
            if (tool.mainThread) flags.Add("main thread");
            return string.Join(" · ", flags.Where(value => !string.IsNullOrWhiteSpace(value))) + (string.IsNullOrWhiteSpace(tool.description) ? string.Empty : " — " + tool.description);
        }

        private static string ShortRevision(string revision) => string.IsNullOrEmpty(revision) ? "unknown" : revision.Substring(0, Math.Min(8, revision.Length));

        private Color GatewayStateColor(UnityMcpGatewayState state)
        {
            switch (state)
            {
                case UnityMcpGatewayState.Starting: return startingColor;
                case UnityMcpGatewayState.Running: return runningColor;
                case UnityMcpGatewayState.Error: return errorColor;
                default: return stoppedColor;
            }
        }

        private static string GatewayStateLabel(UnityMcpGatewayState state)
        {
            switch (state)
            {
                case UnityMcpGatewayState.Starting: return "● Starting";
                case UnityMcpGatewayState.Running: return "● Running";
                case UnityMcpGatewayState.Error: return "● Error";
                default: return "● Stopped";
            }
        }

        private static Color ToolSafetyColor(string safety)
        {
            switch (safety)
            {
                case "safe-read": return new Color(0.24f, 0.68f, 0.39f);
                case "write": return new Color(0.91f, 0.61f, 0.12f);
                case "destructive": return new Color(0.86f, 0.29f, 0.25f);
                default: return new Color(0.76f, 0.27f, 0.66f);
            }
        }

        [MenuItem("Assets/Create/UnityMCP/Development Player Runtime Profile")]
        private static void CreateRuntimeProfile()
        {
            var existing = AssetDatabase.LoadAssetAtPath<UnityMcpRuntimeProfile>(RuntimeProfilePath);
            if (existing != null) { Selection.activeObject = existing; EditorGUIUtility.PingObject(existing); return; }
            System.IO.Directory.CreateDirectory(System.IO.Path.GetFullPath("Assets/UnityMCP/Resources"));
            var profile = CreateInstance<UnityMcpRuntimeProfile>();
            AssetDatabase.CreateAsset(profile, RuntimeProfilePath);
            AssetDatabase.SaveAssets();
            Selection.activeObject = profile;
            EditorGUIUtility.PingObject(profile);
        }
    }
}
