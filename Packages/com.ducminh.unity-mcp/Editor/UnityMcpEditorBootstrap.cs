using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.UIElements;
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

        internal static void SetDebugLoggingEnabled(bool enabled)
        {
            if (server != null) server.DebugLoggingEnabled = enabled;
        }

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
            var debugLoggingEnabled = UnityMcpGatewayHost.GetSettings().DebugLoggingEnabled;
            server = new UnityMcpHttpServer(Registry, UnityMcpScope.Editor, debugLoggingEnabled);
            UnityMcpInstanceDescriptor preferred = null;
            var stored = SessionState.GetString(SessionDescriptorKey, string.Empty);
            if (!string.IsNullOrEmpty(stored))
            {
                try { preferred = JsonConvert.DeserializeObject<UnityMcpInstanceDescriptor>(stored); } catch { }
            }
            try { server.Start(preferred); }
            catch when (preferred != null) { server.Dispose(); server = new UnityMcpHttpServer(Registry, UnityMcpScope.Editor, debugLoggingEnabled); server.Start(); }
            SessionState.SetString(SessionDescriptorKey, JsonConvert.SerializeObject(server.Descriptor));
        }

        private static void Stop()
        {
            server?.Dispose();
            server = null;
        }
    }

    /// <summary>UI Toolkit control centre for the local UnityMCP connection and tool permissions.</summary>
    public sealed class UnityMcpToolsWindow : EditorWindow
    {
        private const string RuntimeProfilePath = "Assets/UnityMCP/Resources/UnityMcpRuntimeProfile.asset";
        private const string StyleSheetPath = "Packages/com.ducminh.unity-mcp/Editor/UI/UnityMcpToolsWindow.uss";
        private const string GatewayConfigClipboardWarning = "Client configuration copied. It contains a local bearer token; do not commit or share it.";
        private const string AllSafetyFilter = "All safety";

        private static readonly List<string> SafetyFilterChoices = new List<string>
        {
            AllSafetyFilter, "safe-read", "write", "destructive", "unsafe"
        };

        private enum Page
        {
            Connection,
            Tools,
            Runtime
        }

        private readonly Dictionary<Page, VisualElement> pages = new Dictionary<Page, VisualElement>();
        private readonly Dictionary<Page, Button> pageButtons = new Dictionary<Page, Button>();
        private readonly Color stoppedColor = new Color(0.48f, 0.48f, 0.48f);
        private readonly Color startingColor = new Color(0.88f, 0.62f, 0.12f);
        private readonly Color runningColor = new Color(0.22f, 0.67f, 0.35f);
        private readonly Color errorColor = new Color(0.84f, 0.27f, 0.24f);

        private Page activePage = Page.Connection;
        private bool gatewaySettingsDirty;
        private UnityMcpRegistry observedRegistry;
        private IVisualElementScheduledItem refreshSchedule;

        private Label headerGatewayStatusLabel;
        private VisualElement headerGatewayStatusDot;
        private Label headerToolSummaryLabel;
        private Button gatewayActionButton;
        private Button gatewayConfigureCodexButton;
        private Button gatewayConfigureAntigravityButton;
        private Button gatewayConfigureClaudeButton;
        private Button gatewayCopyConfigButton;
        private Button gatewayStopButton;

        private Label gatewayStatusTitleLabel;
        private VisualElement gatewayStatusDot;
        private Label gatewayDetailLabel;
        private Label gatewayEndpointLabel;
        private VisualElement gatewayEndpointRow;
        private HelpBox gatewayFeedbackBox;
        private VisualElement gatewayInstallGuidePanel;
        private Label gatewayInstallTargetLabel;
        private Label gatewayInstallCommandsLabel;
        private Button gatewayUseRecommendedPathButton;
        private UnityMcpGatewayInstallGuide gatewayInstallGuide;
        private TextField executablePathField;
        private IntegerField portField;
        private TextField mcpPathField;
        private Toggle debugLoggingToggle;
        private Button saveGatewaySettingsButton;
        private Button regenerateGatewayTokenButton;

        private ToolbarSearchField toolSearchField;
        private DropdownField safetyFilterField;
        private Toggle enabledOnlyToggle;
        private Toggle customOnlyToggle;
        private Button enableVisibleToolsButton;
        private Button disableVisibleToolsButton;
        private Button enableSafeReadToolsButton;
        private Label toolFilterSummaryLabel;
        private ScrollView toolsScrollView;
        private VisualElement toolsContainer;
        private Button toolsPageButton;

        private Label runtimeProfileStatusLabel;
        private Button runtimePrimaryButton;

        [MenuItem("Window/UnityMCP/Tools")]
        public static void Open() => GetWindow<UnityMcpToolsWindow>("UnityMCP Tools");

        private void OnEnable()
        {
            titleContent = new GUIContent("UnityMCP Tools");
            minSize = new Vector2(560, 420);
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
            rootVisualElement.AddToClassList("unity-mcp-window");
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);
            if (styleSheet != null) rootVisualElement.styleSheets.Add(styleSheet);

            var content = new VisualElement { name = "unity-mcp-content" };
            content.AddToClassList("unity-mcp-content");
            content.style.flexGrow = 1;
            rootVisualElement.Add(content);

            BuildHeader(content);
            BuildNavigation(content);

            var pageHost = new VisualElement { name = "unity-mcp-page-host" };
            pageHost.style.flexGrow = 1;
            content.Add(pageHost);

            var connectionPage = CreateScrollPage("unity-mcp-connection-page");
            var toolsPage = new VisualElement { name = "unity-mcp-tools-page" };
            toolsPage.AddToClassList("unity-mcp-page");
            toolsPage.style.flexGrow = 1;
            var runtimePage = CreateScrollPage("unity-mcp-runtime-page");
            pages[Page.Connection] = connectionPage;
            pages[Page.Tools] = toolsPage;
            pages[Page.Runtime] = runtimePage;
            pageHost.Add(connectionPage);
            pageHost.Add(toolsPage);
            pageHost.Add(runtimePage);

            BuildConnectionPage(connectionPage);
            BuildToolsPage(toolsPage);
            BuildRuntimePage(runtimePage);

            LoadGatewaySettings();
            ShowPage(activePage);
            RefreshAll();
            refreshSchedule?.Pause();
            refreshSchedule = rootVisualElement.schedule.Execute(RefreshAll).Every(1000);
        }

        private static ScrollView CreateScrollPage(string name)
        {
            var page = new ScrollView(ScrollViewMode.Vertical) { name = name };
            page.AddToClassList("unity-mcp-page");
            page.AddToClassList("unity-mcp-scroll");
            return page;
        }

        private void BuildHeader(VisualElement parent)
        {
            var header = new VisualElement { name = "unity-mcp-header" };
            header.AddToClassList("unity-mcp-header");
            parent.Add(header);

            var identity = new VisualElement { name = "unity-mcp-header-identity" };
            identity.AddToClassList("unity-mcp-header__identity");
            identity.Add(new Label("M") { name = "unity-mcp-brand-mark" }.WithClass("unity-mcp-brand-mark"));

            var copy = new VisualElement();
            copy.AddToClassList("unity-mcp-header__copy");
            copy.Add(new Label("UnityMCP") { name = "unity-mcp-title" }.WithClass("unity-mcp-header__title"));
            copy.Add(new Label("Secure local development bridge for Unity") { name = "unity-mcp-subtitle" }.WithClass("unity-mcp-header__subtitle"));
            identity.Add(copy);
            header.Add(identity);

            var metadata = new VisualElement { name = "unity-mcp-header-metadata" };
            metadata.AddToClassList("unity-mcp-header__metadata");

            var status = new VisualElement { name = "unity-mcp-header-status" };
            status.AddToClassList("unity-mcp-status-pill");
            headerGatewayStatusDot = new VisualElement { name = "unity-mcp-header-status-dot" };
            headerGatewayStatusDot.AddToClassList("unity-mcp-status__icon");
            headerGatewayStatusLabel = new Label("Stopped");
            headerGatewayStatusLabel.AddToClassList("unity-mcp-status__title");
            status.Add(headerGatewayStatusDot);
            status.Add(headerGatewayStatusLabel);
            metadata.Add(status);

            headerToolSummaryLabel = new Label("Tools loading");
            headerToolSummaryLabel.AddToClassList("unity-mcp-header__tool-summary");
            metadata.Add(headerToolSummaryLabel);

            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(StyleSheetPath);
            if (!string.IsNullOrWhiteSpace(packageInfo?.version))
                metadata.Add(new Label("v" + packageInfo.version).WithClass("unity-mcp-version-pill"));
            header.Add(metadata);
        }

        private void BuildNavigation(VisualElement parent)
        {
            var navigation = new VisualElement { name = "unity-mcp-navigation" };
            navigation.AddToClassList("unity-mcp-nav");
            parent.Add(navigation);
            AddPageButton(navigation, Page.Connection, "Connection", "Start or connect the local MCP gateway.");
            toolsPageButton = AddPageButton(navigation, Page.Tools, "Tools", "Review and enable the tools advertised to MCP clients.");
            AddPageButton(navigation, Page.Runtime, "Runtime", "Configure the optional Development Player runtime bridge.");
        }

        private Button AddPageButton(VisualElement parent, Page page, string text, string tooltip)
        {
            var button = new Button(() => ShowPage(page)) { name = "unity-mcp-page-" + page.ToString().ToLowerInvariant(), text = text, tooltip = tooltip };
            button.AddToClassList("unity-mcp-nav__item");
            pageButtons[page] = button;
            parent.Add(button);
            return button;
        }

        private void ShowPage(Page page)
        {
            activePage = page;
            foreach (var entry in pages)
                entry.Value.style.display = entry.Key == page ? DisplayStyle.Flex : DisplayStyle.None;
            foreach (var entry in pageButtons)
                entry.Value.EnableInClassList("unity-mcp-nav__item--active", entry.Key == page);
            if (page == Page.Tools) RebuildToolList();
        }

        private void BuildConnectionPage(ScrollView page)
        {
            page.contentContainer.AddToClassList("unity-mcp-stack");
            var gatewayCard = CreateCard("Local MCP gateway", "Connect this Editor to Codex, Antigravity, or Claude Code over a project-scoped Streamable HTTP endpoint.");
            page.Add(gatewayCard);

            var gatewayStatus = new VisualElement { name = "unity-mcp-gateway-status" };
            gatewayStatus.AddToClassList("unity-mcp-status");
            gatewayStatusDot = new VisualElement { name = "unity-mcp-gateway-status-dot" };
            gatewayStatusDot.AddToClassList("unity-mcp-status__icon");
            gatewayStatusTitleLabel = new Label("Stopped");
            gatewayStatusTitleLabel.AddToClassList("unity-mcp-status__title");
            gatewayDetailLabel = new Label("Gateway is stopped.");
            gatewayDetailLabel.AddToClassList("unity-mcp-status__detail");
            gatewayStatus.Add(gatewayStatusDot);
            gatewayStatus.Add(gatewayStatusTitleLabel);
            gatewayStatus.Add(gatewayDetailLabel);
            gatewayCard.Add(gatewayStatus);

            gatewayEndpointRow = new VisualElement { name = "unity-mcp-gateway-endpoint" };
            gatewayEndpointRow.AddToClassList("unity-mcp-key-value");
            gatewayEndpointRow.Add(new Label("Endpoint").WithClass("unity-mcp-key-value__key"));
            gatewayEndpointLabel = new Label();
            gatewayEndpointLabel.AddToClassList("unity-mcp-key-value__value");
            gatewayEndpointLabel.AddToClassList("unity-mcp-code");
            gatewayEndpointRow.Add(gatewayEndpointLabel);
            gatewayCard.Add(gatewayEndpointRow);

            var primaryActions = new VisualElement { name = "unity-mcp-gateway-primary-actions" };
            primaryActions.AddToClassList("unity-mcp-primary-action-stack");
            gatewayActionButton = new Button(HandleGatewayPrimaryAction) { name = "unity-mcp-gateway-primary", text = "Start gateway", tooltip = "Start this Editor's Streamable HTTP gateway." };
            gatewayActionButton.AddToClassList("unity-mcp-primary-button");
            gatewayActionButton.AddToClassList("unity-mcp-wide-button");
            gatewayConfigureCodexButton = new Button(ConfigureCodexForProject) { name = "unity-mcp-gateway-configure-codex", text = "Configure Codex for this project", tooltip = "Update this project's .codex/config.toml and install its project-local UnityMCP skill." };
            gatewayConfigureCodexButton.AddToClassList("unity-mcp-primary-button");
            gatewayConfigureCodexButton.AddToClassList("unity-mcp-wide-button");
            gatewayConfigureAntigravityButton = new Button(ConfigureAntigravityForProject) { name = "unity-mcp-gateway-configure-antigravity", text = "Configure Antigravity for this project", tooltip = "Update this project's .agents/mcp_config.json and install its project-local UnityMCP skill." };
            gatewayConfigureAntigravityButton.AddToClassList("unity-mcp-primary-button");
            gatewayConfigureAntigravityButton.AddToClassList("unity-mcp-wide-button");
            gatewayConfigureClaudeButton = new Button(ConfigureClaudeForProject) { name = "unity-mcp-gateway-configure-claude", text = "Configure Claude for this project", tooltip = "Update this project's .mcp.json and install its project-local UnityMCP skill for Claude Code." };
            gatewayConfigureClaudeButton.AddToClassList("unity-mcp-primary-button");
            gatewayConfigureClaudeButton.AddToClassList("unity-mcp-wide-button");
            primaryActions.Add(gatewayActionButton);
            primaryActions.Add(gatewayConfigureCodexButton);
            primaryActions.Add(gatewayConfigureAntigravityButton);
            primaryActions.Add(gatewayConfigureClaudeButton);
            gatewayCard.Add(primaryActions);

            var connectionActions = new VisualElement { name = "unity-mcp-gateway-connection-actions" };
            connectionActions.AddToClassList("unity-mcp-action-row");
            connectionActions.AddToClassList("unity-mcp-action-row--end");
            gatewayCopyConfigButton = new Button(CopyGatewayClientConfiguration) { name = "unity-mcp-gateway-copy-config", text = "Copy MCP config", tooltip = "Copy connection details for another MCP client." };
            gatewayCopyConfigButton.AddToClassList("unity-mcp-secondary-button");
            gatewayStopButton = new Button(StopGateway) { name = "unity-mcp-gateway-stop", text = "Stop gateway", tooltip = "Stop only the gateway owned by this Editor." };
            gatewayStopButton.AddToClassList("unity-mcp-danger-button");
            connectionActions.Add(gatewayCopyConfigButton);
            connectionActions.Add(gatewayStopButton);
            gatewayCard.Add(connectionActions);

            gatewayFeedbackBox = new HelpBox(string.Empty, HelpBoxMessageType.Info) { name = "unity-mcp-gateway-feedback" };
            gatewayFeedbackBox.AddToClassList("unity-mcp-help");
            gatewayFeedbackBox.style.display = DisplayStyle.None;
            gatewayCard.Add(gatewayFeedbackBox);

            gatewayInstallGuidePanel = new VisualElement { name = "unity-mcp-gateway-install-guide" };
            gatewayInstallGuidePanel.AddToClassList("unity-mcp-install-guide");
            gatewayInstallGuidePanel.style.display = DisplayStyle.None;
            gatewayInstallGuidePanel.Add(new Label("Install the local server").WithClass("unity-mcp-install-guide__title"));
            gatewayInstallGuidePanel.Add(new Label("Git and Python 3.11 or newer are required. Run these commands in a terminal; Unity only prepares and copies them.")
                .WithClass("unity-mcp-install-guide__description"));

            var installTargetRow = new VisualElement();
            installTargetRow.AddToClassList("unity-mcp-key-value");
            installTargetRow.Add(new Label("Install target").WithClass("unity-mcp-key-value__key"));
            gatewayInstallTargetLabel = new Label();
            gatewayInstallTargetLabel.AddToClassList("unity-mcp-key-value__value");
            gatewayInstallTargetLabel.AddToClassList("unity-mcp-code");
            installTargetRow.Add(gatewayInstallTargetLabel);
            gatewayInstallGuidePanel.Add(installTargetRow);

            gatewayInstallCommandsLabel = new Label { name = "unity-mcp-gateway-install-commands" };
            gatewayInstallCommandsLabel.AddToClassList("unity-mcp-install-guide__commands");
            gatewayInstallCommandsLabel.AddToClassList("unity-mcp-code");
            gatewayInstallGuidePanel.Add(gatewayInstallCommandsLabel);

            var installActions = new VisualElement();
            installActions.AddToClassList("unity-mcp-action-row");
            var copyInstallCommandsButton = new Button(CopyGatewayInstallCommands) { name = "unity-mcp-gateway-copy-install", text = "Copy install commands", tooltip = "Copy the platform-specific Git and Python commands. Unity will not run them." };
            copyInstallCommandsButton.AddToClassList("unity-mcp-primary-button");
            gatewayUseRecommendedPathButton = new Button(UseRecommendedGatewayPath) { name = "unity-mcp-gateway-use-recommended", text = "Use recommended path", tooltip = "Use the executable path created by these install commands." };
            gatewayUseRecommendedPathButton.AddToClassList("unity-mcp-secondary-button");
            installActions.Add(copyInstallCommandsButton);
            installActions.Add(gatewayUseRecommendedPathButton);
            gatewayInstallGuidePanel.Add(installActions);
            gatewayCard.Add(gatewayInstallGuidePanel);

            var advanced = new Foldout { name = "unity-mcp-gateway-advanced", text = "Advanced gateway settings", value = false };
            gatewayCard.Add(advanced);
            AddMutedLabel(advanced, "These settings are saved locally for this user and project. They are not stored in Assets or source control.", 7);

            executablePathField = new TextField { name = "unity-mcp-gateway-executable", isDelayed = true, tooltip = "Path to unity-mcp.exe (or unity-mcp on macOS/Linux)." };
            advanced.Add(CreateFormRow("Gateway executable", executablePathField));
            var executableActions = new VisualElement();
            executableActions.AddToClassList("unity-mcp-action-row");
            executableActions.AddToClassList("unity-mcp-form-actions");
            var browseButton = new Button(BrowseForGatewayExecutable) { name = "unity-mcp-gateway-browse", text = "Browse", tooltip = "Choose the unity-mcp executable in your Python virtual environment." };
            browseButton.AddToClassList("unity-mcp-secondary-button");
            var useDefaultButton = new Button(() =>
            {
                executablePathField.value = UnityMcpGatewayHost.GetDefaultExecutablePath();
                MarkGatewaySettingsDirty();
            }) { name = "unity-mcp-gateway-default-path", text = "Use default" };
            useDefaultButton.AddToClassList("unity-mcp-secondary-button");
            executableActions.Add(browseButton);
            executableActions.Add(useDefaultButton);
            advanced.Add(executableActions);

            portField = new IntegerField { name = "unity-mcp-gateway-port", isDelayed = true, tooltip = "Uses another loopback port automatically if this one is busy." };
            mcpPathField = new TextField { name = "unity-mcp-gateway-path", isDelayed = true, tooltip = "The local Streamable HTTP path; /mcp is recommended." };
            advanced.Add(CreateFormRow("Preferred port", portField));
            advanced.Add(CreateFormRow("MCP path", mcpPathField));
            debugLoggingToggle = new Toggle("Debug logging")
            {
                name = "unity-mcp-debug-logging",
                tooltip = "Write UnityMCP startup and tool-call audit messages to the Unity Console. Warnings and errors are always logged."
            };
            advanced.Add(CreateFormRow("Logging", debugLoggingToggle));

            var settingsActions = new VisualElement();
            settingsActions.AddToClassList("unity-mcp-action-row");
            settingsActions.AddToClassList("unity-mcp-form-actions");
            saveGatewaySettingsButton = new Button(() => SaveGatewaySettings()) { name = "unity-mcp-gateway-save", text = "Save changes" };
            saveGatewaySettingsButton.AddToClassList("unity-mcp-secondary-button");
            settingsActions.Add(saveGatewaySettingsButton);
            advanced.Add(settingsActions);

            executablePathField.RegisterValueChangedCallback(_ => MarkGatewaySettingsDirty());
            portField.RegisterValueChangedCallback(_ => MarkGatewaySettingsDirty());
            mcpPathField.RegisterValueChangedCallback(_ => MarkGatewaySettingsDirty());
            debugLoggingToggle.RegisterValueChangedCallback(_ => MarkGatewaySettingsDirty());

            var security = new Foldout { name = "unity-mcp-gateway-security", text = "Security", value = false };
            gatewayCard.Add(security);
            AddMutedLabel(security, "Client setup writes the bearer token only to this project's locally ignored .codex/config.toml, .agents/mcp_config.json, or .mcp.json. Project skills contain no token. No global client config or skill is changed. Copy MCP config places the token on the clipboard.", 7);
            regenerateGatewayTokenButton = new Button(RegenerateGatewayToken) { name = "unity-mcp-gateway-regenerate-token", text = "Regenerate token", tooltip = "Creates a new local token after the gateway is stopped." };
            regenerateGatewayTokenButton.AddToClassList("unity-mcp-danger-button");
            var securityActions = new VisualElement();
            securityActions.AddToClassList("unity-mcp-action-row");
            securityActions.AddToClassList("unity-mcp-form-actions");
            securityActions.Add(regenerateGatewayTokenButton);
            security.Add(securityActions);
        }

        private void BuildToolsPage(VisualElement page)
        {
            var pageHeader = new VisualElement();
            pageHeader.AddToClassList("unity-mcp-stack");
            page.Add(pageHeader);

            var filterCard = CreateCard("Tool permissions", "Only enabled tools are advertised to MCP clients. Custom, write, destructive, and unsafe tools require explicit local opt-in.");
            filterCard.AddToClassList("unity-mcp-tool-filter-card");
            pageHeader.Add(filterCard);

            toolSearchField = new ToolbarSearchField { name = "unity-mcp-tool-search", tooltip = "Search tool name, title, description, category, or source." };
            toolSearchField.AddToClassList("unity-mcp-tool-toolbar__search");
            toolSearchField.RegisterValueChangedCallback(_ => ApplyToolFilter());
            filterCard.Add(CreateFormRow("Search", toolSearchField));

            safetyFilterField = new DropdownField(new List<string>(SafetyFilterChoices), 0) { name = "unity-mcp-tool-safety-filter", tooltip = "Filter by safety tier." };
            safetyFilterField.AddToClassList("unity-mcp-tool-toolbar__filter");
            safetyFilterField.RegisterValueChangedCallback(_ => ApplyToolFilter());
            filterCard.Add(CreateFormRow("Safety", safetyFilterField));

            var toolbarActions = new VisualElement();
            toolbarActions.AddToClassList("unity-mcp-tool-toolbar__actions");
            enabledOnlyToggle = new Toggle("Enabled only") { name = "unity-mcp-tool-enabled-filter", tooltip = "Show only currently enabled tools." };
            customOnlyToggle = new Toggle("Custom only") { name = "unity-mcp-tool-custom-filter", tooltip = "Show only project-defined custom tools." };
            enabledOnlyToggle.AddToClassList("unity-mcp-tool-option");
            customOnlyToggle.AddToClassList("unity-mcp-tool-option");
            enabledOnlyToggle.RegisterValueChangedCallback(_ => ApplyToolFilter());
            customOnlyToggle.RegisterValueChangedCallback(_ => ApplyToolFilter());
            var reloadButton = new Button(ReloadRegistry) { name = "unity-mcp-tool-reload", text = "Reload", tooltip = "Rescan built-in and custom UnityMCP tools." };
            reloadButton.AddToClassList("unity-mcp-secondary-button");
            toolbarActions.Add(enabledOnlyToggle);
            toolbarActions.Add(customOnlyToggle);
            toolbarActions.Add(reloadButton);
            filterCard.Add(CreateFormRow("Show", toolbarActions));

            var bulkActions = new VisualElement { name = "unity-mcp-tool-bulk-actions" };
            bulkActions.AddToClassList("unity-mcp-tool-bulk-actions");
            enableVisibleToolsButton = new Button(EnableVisibleTools) { name = "unity-mcp-tool-enable-visible", text = "Enable shown", tooltip = "Enable every currently shown tool. Search and filters determine the selection." };
            enableVisibleToolsButton.AddToClassList("unity-mcp-primary-button");
            disableVisibleToolsButton = new Button(DisableVisibleTools) { name = "unity-mcp-tool-disable-visible", text = "Disable shown", tooltip = "Disable every currently shown tool. Search and filters determine the selection." };
            disableVisibleToolsButton.AddToClassList("unity-mcp-secondary-button");
            enableSafeReadToolsButton = new Button(EnableSafeReadTools) { name = "unity-mcp-tool-enable-safe-read", text = "Enable safe-read", tooltip = "Enable every available safe-read tool, regardless of the current filter." };
            enableSafeReadToolsButton.AddToClassList("unity-mcp-secondary-button");
            bulkActions.Add(enableVisibleToolsButton);
            bulkActions.Add(disableVisibleToolsButton);
            bulkActions.Add(enableSafeReadToolsButton);
            filterCard.Add(CreateFormRow("Bulk actions", bulkActions));

            toolFilterSummaryLabel = new Label("Tools are loading.") { name = "unity-mcp-tool-summary" };
            toolFilterSummaryLabel.AddToClassList("unity-mcp-tool-summary");
            filterCard.Add(toolFilterSummaryLabel);

            toolsScrollView = new ScrollView(ScrollViewMode.Vertical) { name = "unity-mcp-tool-scroll" };
            toolsScrollView.AddToClassList("unity-mcp-scroll");
            toolsScrollView.style.flexGrow = 1;
            page.Add(toolsScrollView);
            toolsContainer = new VisualElement { name = "unity-mcp-tool-list" };
            toolsContainer.AddToClassList("unity-mcp-tool-list");
            toolsScrollView.Add(toolsContainer);
        }

        private void BuildRuntimePage(ScrollView page)
        {
            page.contentContainer.AddToClassList("unity-mcp-stack");
            var runtimeCard = CreateCard("Development Player runtime", "Runtime tools are intentionally separate. Create an explicit profile before building a desktop Development Player; release builds never expose the bridge.");
            page.Add(runtimeCard);
            runtimeProfileStatusLabel = new Label { name = "unity-mcp-runtime-status" };
            runtimeProfileStatusLabel.AddToClassList("unity-mcp-runtime-status");
            runtimeCard.Add(CreateFormRow("Profile", runtimeProfileStatusLabel));
            var runtimeActions = new VisualElement();
            runtimeActions.AddToClassList("unity-mcp-primary-action-stack");
            runtimePrimaryButton = new Button(HandleRuntimeProfileAction) { name = "unity-mcp-runtime-primary", text = "Create runtime profile" };
            runtimePrimaryButton.AddToClassList("unity-mcp-primary-button");
            runtimePrimaryButton.AddToClassList("unity-mcp-wide-button");
            runtimeActions.Add(runtimePrimaryButton);
            runtimeCard.Add(runtimeActions);
        }

        private static VisualElement CreateCard(string title, string description)
        {
            var card = new VisualElement();
            card.AddToClassList("unity-mcp-card");
            var header = new VisualElement();
            header.AddToClassList("unity-mcp-card__header");
            var copy = new VisualElement();
            copy.Add(new Label(title).WithClass("unity-mcp-card__title"));
            copy.Add(new Label(description).WithClass("unity-mcp-card__description"));
            header.Add(copy);
            card.Add(header);
            return card;
        }

        private static VisualElement CreateFormRow(string label, VisualElement field)
        {
            var row = new VisualElement();
            row.AddToClassList("unity-mcp-form-row");
            row.Add(new Label(label).WithClass("unity-mcp-form-row__label"));
            var fieldContainer = new VisualElement();
            fieldContainer.AddToClassList("unity-mcp-form-row__field");
            field.AddToClassList("unity-mcp-form-row__control");
            fieldContainer.Add(field);
            row.Add(fieldContainer);
            return row;
        }

        private static void AddMutedLabel(VisualElement parent, string text, int bottomMargin = 0)
        {
            var label = new Label(text);
            label.AddToClassList("unity-mcp-card__description");
            label.style.marginBottom = bottomMargin;
            parent.Add(label);
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
                SetToolSummary("UnityMCP is still starting.");
                return;
            }
            registry.Reload();
            RebuildToolList();
        }

        private void RebuildToolList()
        {
            if (toolsContainer == null) return;
            var preservedOffset = toolsScrollView == null ? Vector2.zero : toolsScrollView.scrollOffset;
            toolsContainer.Clear();
            var registry = UnityMcpEditorBootstrap.Registry;
            if (registry == null)
            {
                SetToolSummary("Tools are loading.");
                RefreshBulkActionButtons(Array.Empty<UnityMcpToolDescriptor>(), Array.Empty<UnityMcpToolDescriptor>());
                toolsContainer.Add(CreateEmptyState("Waiting for the UnityMCP registry", "Tool permissions appear after Unity has discovered the current project's tools."));
                return;
            }

            var allTools = registry.Tools.OrderBy(tool => tool.category ?? "project", StringComparer.OrdinalIgnoreCase).ThenBy(tool => tool.name, StringComparer.Ordinal).ToList();
            var visibleTools = allTools.Where(MatchesActiveToolFilter).ToList();
            var enabledCount = allTools.Count(tool => tool.enabled);
            RefreshBulkActionButtons(allTools, visibleTools);
            headerToolSummaryLabel.text = enabledCount + " / " + allTools.Count + " tools enabled";
            if (toolsPageButton != null) toolsPageButton.text = "Tools (" + allTools.Count + ")";
            SetToolSummary(visibleTools.Count + " of " + allTools.Count + " tools shown - " + enabledCount + " enabled");

            if (visibleTools.Count == 0)
            {
                toolsContainer.Add(CreateEmptyState("No tools match this filter", "Adjust the search or filters to see available tool contracts."));
                return;
            }

            var hasSearch = !string.IsNullOrWhiteSpace(toolSearchField?.value);
            foreach (var category in visibleTools.GroupBy(tool => string.IsNullOrWhiteSpace(tool.category) ? "project" : tool.category, StringComparer.OrdinalIgnoreCase).OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
            {
                var categoryTools = category.OrderBy(tool => GetToolTitle(tool), StringComparer.OrdinalIgnoreCase).ToList();
                toolsContainer.Add(CreateToolGroup(category.Key, categoryTools, hasSearch));
            }
            if (toolsScrollView != null) toolsScrollView.scrollOffset = preservedOffset;
        }

        private void ApplyToolFilter()
        {
            if (toolsScrollView != null) toolsScrollView.scrollOffset = Vector2.zero;
            RebuildToolList();
        }

        private void RefreshBulkActionButtons(IReadOnlyCollection<UnityMcpToolDescriptor> allTools, IReadOnlyCollection<UnityMcpToolDescriptor> visibleTools)
        {
            var visibleDisabled = visibleTools.Count(tool => !tool.enabled);
            var visibleEnabled = visibleTools.Count(tool => tool.enabled);
            var safeReadDisabled = allTools.Count(tool => !tool.enabled && string.Equals(tool.safety, "safe-read", StringComparison.OrdinalIgnoreCase));
            if (enableVisibleToolsButton != null)
            {
                enableVisibleToolsButton.text = "Enable shown (" + visibleDisabled + ")";
                enableVisibleToolsButton.SetEnabled(visibleDisabled > 0);
            }
            if (disableVisibleToolsButton != null)
            {
                disableVisibleToolsButton.text = "Disable shown (" + visibleEnabled + ")";
                disableVisibleToolsButton.SetEnabled(visibleEnabled > 0);
            }
            if (enableSafeReadToolsButton != null)
            {
                enableSafeReadToolsButton.text = "Enable safe-read (" + safeReadDisabled + ")";
                enableSafeReadToolsButton.SetEnabled(safeReadDisabled > 0);
            }
        }

        private void EnableVisibleTools() => ApplyBulkToolEnablement(GetVisibleTools(), true, "shown");

        private void DisableVisibleTools() => ApplyBulkToolEnablement(GetVisibleTools(), false, "shown");

        private void EnableSafeReadTools()
        {
            var registry = UnityMcpEditorBootstrap.Registry;
            ApplyBulkToolEnablement(registry?.Tools.Where(tool => string.Equals(tool.safety, "safe-read", StringComparison.OrdinalIgnoreCase)), true, "safe-read");
        }

        private IEnumerable<UnityMcpToolDescriptor> GetVisibleTools()
        {
            return UnityMcpEditorBootstrap.Registry?.Tools.Where(MatchesActiveToolFilter) ?? Enumerable.Empty<UnityMcpToolDescriptor>();
        }

        private bool ApplyBulkToolEnablement(IEnumerable<UnityMcpToolDescriptor> tools, bool enabled, string selectionLabel)
        {
            var registry = UnityMcpEditorBootstrap.Registry;
            if (registry == null) return false;
            var targets = (tools ?? Enumerable.Empty<UnityMcpToolDescriptor>()).Where(tool => tool.enabled != enabled).ToList();
            if (targets.Count == 0) return true;
            if (enabled && !ConfirmBulkToolEnablement(targets, selectionLabel)) return false;
            registry.SetEnabled(targets.Select(tool => tool.name), enabled);
            ShowNotification(new GUIContent((enabled ? "Enabled " : "Disabled ") + targets.Count + " tools."));
            return true;
        }

        private static bool ConfirmBulkToolEnablement(IReadOnlyCollection<UnityMcpToolDescriptor> tools, string selectionLabel)
        {
            var destructiveCount = tools.Count(tool => string.Equals(tool.safety, "destructive", StringComparison.OrdinalIgnoreCase));
            var unsafeCount = tools.Count(tool => string.Equals(tool.safety, "unsafe", StringComparison.OrdinalIgnoreCase));
            if (destructiveCount == 0 && unsafeCount == 0) return true;
            var riskySummary = new List<string>();
            if (destructiveCount > 0) riskySummary.Add(destructiveCount + " destructive");
            if (unsafeCount > 0) riskySummary.Add(unsafeCount + " unsafe");
            return EditorUtility.DisplayDialog(
                "Enable multiple UnityMCP tools",
                "Enable " + tools.Count + " " + selectionLabel + " tools? This selection includes " + string.Join(" and ", riskySummary) + " tools. Connected MCP clients can call them until you disable them again. Use the Safety filter to narrow the selection if needed.",
                "Enable " + tools.Count + " tools",
                "Cancel");
        }

        private VisualElement CreateToolGroup(string category, List<UnityMcpToolDescriptor> tools, bool hasSearch)
        {
            var group = new Foldout { name = "unity-mcp-tool-category-" + ToElementIdentifier(category) };
            group.AddToClassList("unity-mcp-tool-group");
            var categoryStateKey = GetCategoryStateKey(category);
            var defaultExpanded = tools.Any(tool => tool.enabled);
            group.SetValueWithoutNotify(hasSearch || SessionState.GetBool(categoryStateKey, defaultExpanded));
            group.text = FormatCategory(category) + "  " + tools.Count(tool => tool.enabled) + "/" + tools.Count + " enabled";
            var groupHeader = group.Q<Toggle>();
            if (groupHeader != null)
            {
                groupHeader.AddToClassList("unity-mcp-tool-group__header");
                var enabledCount = tools.Count(tool => tool.enabled);
                var groupEnablement = new Toggle("All enabled")
                {
                    value = enabledCount == tools.Count,
                    tooltip = "Enable or disable every currently shown tool in this category."
                };
                groupEnablement.AddToClassList("unity-mcp-tool-group__enablement");
                groupEnablement.RegisterCallback<ClickEvent>(click => click.StopPropagation());
                groupEnablement.RegisterValueChangedCallback(change =>
                {
                    if (!ApplyBulkToolEnablement(tools, change.newValue, FormatCategory(category)))
                        groupEnablement.SetValueWithoutNotify(enabledCount == tools.Count);
                });
                groupHeader.Add(groupEnablement);
            }
            group.RegisterValueChangedCallback(change => SessionState.SetBool(categoryStateKey, change.newValue));
            foreach (var tool in tools) group.Add(CreateToolRow(UnityMcpEditorBootstrap.Registry, tool));
            return group;
        }

        private VisualElement CreateToolRow(UnityMcpRegistry registry, UnityMcpToolDescriptor tool)
        {
            var row = new VisualElement { name = "unity-mcp-tool-" + ToElementIdentifier(tool.name) };
            row.AddToClassList("unity-mcp-tool-row");

            var enabledToggle = new Toggle { value = tool.enabled, tooltip = tool.enabled ? "Disable this tool for MCP clients." : "Enable this tool for MCP clients." };
            enabledToggle.AddToClassList("unity-mcp-tool-row__toggle");
            enabledToggle.RegisterValueChangedCallback(change =>
            {
                if (change.newValue == tool.enabled) return;
                if (change.newValue && RequiresEnablementConfirmation(tool) && !ConfirmToolEnablement(tool))
                {
                    enabledToggle.SetValueWithoutNotify(false);
                    return;
                }
                registry.SetEnabled(tool.name, change.newValue);
            });
            row.Add(enabledToggle);

            var copy = new VisualElement();
            copy.AddToClassList("unity-mcp-tool-row__content");
            var heading = new VisualElement();
            heading.AddToClassList("unity-mcp-tool-row__heading");
            var title = new Label(GetToolTitle(tool)) { tooltip = tool.name };
            title.AddToClassList("unity-mcp-tool-row__name");
            heading.Add(title);
            AddToolBadge(heading, tool.safety, tool.safety);
            AddToolBadge(heading, tool.enabled ? "Enabled" : "Disabled", tool.enabled ? "enabled" : "disabled");
            if (!string.IsNullOrWhiteSpace(tool.source)) AddToolBadge(heading, FormatSource(tool.source), tool.source);
            copy.Add(heading);
            copy.Add(new Label(tool.name).WithClass("unity-mcp-tool-row__meta", "unity-mcp-code"));
            if (!string.IsNullOrWhiteSpace(tool.description)) copy.Add(new Label(tool.description).WithClass("unity-mcp-tool-row__description"));

            var metadata = new List<string>();
            if (tool.scopes != null && tool.scopes.Length > 0) metadata.Add(string.Join(" + ", tool.scopes));
            if (tool.supportsDryRun) metadata.Add("dry-run");
            if (tool.returnsJob) metadata.Add("job");
            if (tool.mainThread) metadata.Add("main thread");
            if (metadata.Count > 0) copy.Add(new Label(string.Join(" - ", metadata)).WithClass("unity-mcp-tool-row__meta"));
            row.Add(copy);
            return row;
        }

        private static void AddToolBadge(VisualElement parent, string text, string variant)
        {
            var badge = new Label(text);
            badge.AddToClassList("unity-mcp-badge");
            badge.AddToClassList("unity-mcp-badge--" + ToElementIdentifier(variant));
            parent.Add(badge);
        }

        private bool MatchesActiveToolFilter(UnityMcpToolDescriptor tool)
        {
            if (enabledOnlyToggle != null && enabledOnlyToggle.value && !tool.enabled) return false;
            if (customOnlyToggle != null && customOnlyToggle.value && string.Equals(tool.source, "builtin", StringComparison.OrdinalIgnoreCase)) return false;
            var safetyFilter = safetyFilterField?.value ?? AllSafetyFilter;
            if (!string.Equals(safetyFilter, AllSafetyFilter, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(tool.safety, safetyFilter, StringComparison.OrdinalIgnoreCase)) return false;
            var query = toolSearchField?.value;
            if (string.IsNullOrWhiteSpace(query)) return true;
            return ContainsIgnoreCase(tool.name, query)
                || ContainsIgnoreCase(tool.title, query)
                || ContainsIgnoreCase(tool.description, query)
                || ContainsIgnoreCase(tool.category, query)
                || ContainsIgnoreCase(tool.source, query);
        }

        private void SetToolSummary(string value)
        {
            if (toolFilterSummaryLabel != null) toolFilterSummaryLabel.text = value ?? string.Empty;
            if (headerToolSummaryLabel != null && UnityMcpEditorBootstrap.Registry == null) headerToolSummaryLabel.text = "Tools loading";
        }

        private static VisualElement CreateEmptyState(string title, string description)
        {
            var empty = new VisualElement();
            empty.AddToClassList("unity-mcp-empty-state");
            empty.Add(new Label(title).WithClass("unity-mcp-empty-state__title"));
            empty.Add(new Label(description).WithClass("unity-mcp-empty-state__description"));
            return empty;
        }

        private void LoadGatewaySettings()
        {
            var settings = UnityMcpGatewayHost.GetSettings();
            executablePathField?.SetValueWithoutNotify(settings.ExecutablePath);
            portField?.SetValueWithoutNotify(settings.PreferredPort);
            mcpPathField?.SetValueWithoutNotify(settings.McpPath);
            debugLoggingToggle?.SetValueWithoutNotify(settings.DebugLoggingEnabled);
            gatewaySettingsDirty = false;
            RefreshGatewaySettingsButton();
        }

        private void MarkGatewaySettingsDirty()
        {
            gatewaySettingsDirty = true;
            RefreshGatewaySettingsButton();
        }

        private void RefreshGatewaySettingsButton()
        {
            saveGatewaySettingsButton?.SetEnabled(gatewaySettingsDirty);
        }

        private bool SaveGatewaySettings()
        {
            var settings = new UnityMcpGatewaySettings
            {
                ExecutablePath = executablePathField?.value,
                PreferredPort = portField?.value ?? 0,
                McpPath = mcpPathField?.value,
                DebugLoggingEnabled = debugLoggingToggle?.value ?? true
            };
            if (!UnityMcpGatewayHost.TrySaveSettings(settings, out var error))
            {
                SetGatewayFeedback(error, true);
                return false;
            }
            UnityMcpEditorBootstrap.SetDebugLoggingEnabled(settings.DebugLoggingEnabled);
            gatewaySettingsDirty = false;
            RefreshGatewaySettingsButton();
            SetGatewayFeedback("Gateway settings saved locally.", false);
            return true;
        }

        private void BrowseForGatewayExecutable()
        {
            var current = executablePathField?.value;
            var directory = string.IsNullOrWhiteSpace(current)
                ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                : System.IO.Path.GetDirectoryName(current);
            var selected = EditorUtility.OpenFilePanel("Select unity-mcp executable", directory ?? string.Empty, string.Empty);
            if (string.IsNullOrWhiteSpace(selected)) return;
            executablePathField.value = selected;
            MarkGatewaySettingsDirty();
        }

        private void HandleGatewayPrimaryAction()
        {
            var status = UnityMcpGatewayHost.GetStatus();
            if (status.State == UnityMcpGatewayState.Running)
            {
                CopyGatewayClientConfiguration();
                return;
            }
            StartGateway();
        }

        private void StartGateway()
        {
            if (!SaveGatewaySettings()) return;
            if (!UnityMcpGatewayHost.Start(out var error)) SetGatewayFeedback(error, true);
            else SetGatewayFeedback("Starting the local UnityMCP HTTP gateway.", false);
            RefreshGatewayStatus(UnityMcpGatewayHost.GetStatus());
        }

        private void CopyGatewayInstallCommands()
        {
            gatewayInstallGuide = UnityMcpGatewayHost.GetInstallationGuide();
            RefreshGatewayInstallGuide(UnityMcpGatewayHost.GetStatus());
            EditorGUIUtility.systemCopyBuffer = gatewayInstallGuide.Commands;
            SetGatewayFeedback("Install commands copied. Run them in a terminal, then select Retry after installation.", false);
            ShowNotification(new GUIContent("Server install commands copied."));
        }

        private void UseRecommendedGatewayPath()
        {
            gatewayInstallGuide = UnityMcpGatewayHost.GetInstallationGuide();
            if (executablePathField != null) executablePathField.value = gatewayInstallGuide.ExecutablePath;
            MarkGatewaySettingsDirty();
            if (gatewayUseRecommendedPathButton != null) gatewayUseRecommendedPathButton.style.display = DisplayStyle.None;
            SetGatewayFeedback("The recommended executable path is selected. It will be saved when you retry the gateway.", false);
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
                SetGatewayFeedback("Start the gateway before copying its client configuration.", true);
                return;
            }
            EditorGUIUtility.systemCopyBuffer = configuration;
            SetGatewayFeedback(GatewayConfigClipboardWarning, false);
            ShowNotification(new GUIContent("MCP configuration copied."));
        }

        private void ConfigureCodexForProject()
        {
            if (!UnityMcpGatewayHost.TryConfigureCodexForProject(out _, out var error))
            {
                SetGatewayFeedback(error, true);
                return;
            }
            SetGatewayFeedback("Codex config and project skill updated at .codex/config.toml and .agents/skills/unity-mcp. UnityMCP will keep them synchronized; restart Codex if the skill is not detected automatically.", false);
            ShowNotification(new GUIContent("Codex configured for this project."));
        }

        private void ConfigureAntigravityForProject()
        {
            if (!UnityMcpGatewayHost.TryConfigureAntigravityForProject(out _, out var error))
            {
                SetGatewayFeedback(error, true);
                return;
            }
            SetGatewayFeedback("Antigravity config and project skill updated at .agents/mcp_config.json and .agents/skills/unity-mcp. UnityMCP will keep them synchronized; reload MCP servers or restart Antigravity to apply them.", false);
            ShowNotification(new GUIContent("Antigravity configured for this project."));
        }

        private void ConfigureClaudeForProject()
        {
            if (!UnityMcpGatewayHost.TryConfigureClaudeForProject(out _, out var error))
            {
                SetGatewayFeedback(error, true);
                return;
            }
            SetGatewayFeedback("Claude Code config and project skill updated at .mcp.json and .claude/skills/unity-mcp. UnityMCP will keep them synchronized; restart Claude Code if needed and approve the project MCP server when prompted.", false);
            ShowNotification(new GUIContent("Claude configured for this project."));
        }

        private void RegenerateGatewayToken()
        {
            if (!EditorUtility.DisplayDialog("Regenerate UnityMCP token", "Existing copied client configurations will stop working. Regenerate the local token?", "Regenerate", "Cancel")) return;
            if (!UnityMcpGatewayHost.TryRegenerateBearerToken(out var error))
            {
                SetGatewayFeedback(error, true);
                return;
            }
            SetGatewayFeedback("A new local token was created. Start the gateway and copy a new configuration.", false);
        }

        private void OnGatewayStatusChanged(UnityMcpGatewayStatus status) => RefreshGatewayStatus(status);

        private void RefreshGatewayStatus(UnityMcpGatewayStatus status)
        {
            if (status == null) return;
            var stateText = status.RequiresInstallation ? "Server not installed" : GatewayStateLabel(status.State);
            var stateColor = GatewayStateColor(status.State);
            if (headerGatewayStatusLabel != null) headerGatewayStatusLabel.text = stateText;
            if (headerGatewayStatusDot != null) headerGatewayStatusDot.style.backgroundColor = stateColor;
            if (gatewayStatusTitleLabel != null) gatewayStatusTitleLabel.text = stateText;
            if (gatewayStatusDot != null) gatewayStatusDot.style.backgroundColor = stateColor;
            if (gatewayDetailLabel != null) gatewayDetailLabel.text = string.IsNullOrWhiteSpace(status.Message) ? "Gateway status unavailable." : status.Message;

            var isRunning = status.State == UnityMcpGatewayState.Running;
            if (gatewayEndpointRow != null) gatewayEndpointRow.style.display = isRunning ? DisplayStyle.Flex : DisplayStyle.None;
            if (gatewayEndpointLabel != null) gatewayEndpointLabel.text = status.Endpoint ?? string.Empty;
            if (gatewayActionButton != null)
            {
                gatewayActionButton.style.display = isRunning ? DisplayStyle.None : DisplayStyle.Flex;
                gatewayActionButton.text = status.State == UnityMcpGatewayState.Starting
                    ? "Starting gateway"
                    : status.RequiresInstallation
                        ? "Retry after installation"
                        : status.State == UnityMcpGatewayState.Error ? "Retry gateway" : "Start gateway";
                gatewayActionButton.SetEnabled(status.State != UnityMcpGatewayState.Starting);
            }
            if (gatewayConfigureCodexButton != null)
            {
                gatewayConfigureCodexButton.style.display = isRunning ? DisplayStyle.Flex : DisplayStyle.None;
                gatewayConfigureCodexButton.SetEnabled(isRunning);
            }
            if (gatewayConfigureAntigravityButton != null)
            {
                gatewayConfigureAntigravityButton.style.display = isRunning ? DisplayStyle.Flex : DisplayStyle.None;
                gatewayConfigureAntigravityButton.SetEnabled(isRunning);
            }
            if (gatewayConfigureClaudeButton != null)
            {
                gatewayConfigureClaudeButton.style.display = isRunning ? DisplayStyle.Flex : DisplayStyle.None;
                gatewayConfigureClaudeButton.SetEnabled(isRunning);
            }
            if (gatewayCopyConfigButton != null)
            {
                gatewayCopyConfigButton.style.display = isRunning ? DisplayStyle.Flex : DisplayStyle.None;
                gatewayCopyConfigButton.SetEnabled(isRunning);
            }
            if (gatewayStopButton != null)
            {
                gatewayStopButton.style.display = status.IsRunning ? DisplayStyle.Flex : DisplayStyle.None;
                gatewayStopButton.SetEnabled(status.IsRunning);
            }
            if (regenerateGatewayTokenButton != null) regenerateGatewayTokenButton.SetEnabled(!status.IsRunning);
            RefreshGatewayInstallGuide(status);
            if (status.State == UnityMcpGatewayState.Error && !status.RequiresInstallation && !string.IsNullOrWhiteSpace(status.LastError)) SetGatewayFeedback(status.LastError, true);
        }

        private void RefreshGatewayInstallGuide(UnityMcpGatewayStatus status)
        {
            if (gatewayInstallGuidePanel == null) return;
            var visible = status != null && status.RequiresInstallation;
            gatewayInstallGuidePanel.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (!visible)
            {
                gatewayInstallGuide = null;
                return;
            }

            if (gatewayInstallGuide == null) gatewayInstallGuide = UnityMcpGatewayHost.GetInstallationGuide();
            if (gatewayInstallTargetLabel != null) gatewayInstallTargetLabel.text = gatewayInstallGuide.ExecutablePath;
            if (gatewayInstallCommandsLabel != null) gatewayInstallCommandsLabel.text = gatewayInstallGuide.Commands;
            if (gatewayUseRecommendedPathButton != null)
            {
                var configuredPath = executablePathField?.value;
                gatewayUseRecommendedPathButton.style.display = PathsEqual(configuredPath, gatewayInstallGuide.ExecutablePath)
                    ? DisplayStyle.None
                    : DisplayStyle.Flex;
            }
        }

        private static bool PathsEqual(string left, string right)
        {
            var normalizedLeft = (left ?? string.Empty).Trim().Replace('\\', '/').TrimEnd('/');
            var normalizedRight = (right ?? string.Empty).Trim().Replace('\\', '/').TrimEnd('/');
            return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
        }

        private void SetGatewayFeedback(string value, bool isError)
        {
            if (gatewayFeedbackBox == null) return;
            gatewayFeedbackBox.text = value ?? string.Empty;
            gatewayFeedbackBox.messageType = isError ? HelpBoxMessageType.Error : HelpBoxMessageType.Info;
            gatewayFeedbackBox.style.display = string.IsNullOrWhiteSpace(value) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void RefreshRuntimeProfile()
        {
            if (runtimeProfileStatusLabel == null || runtimePrimaryButton == null) return;
            var profile = AssetDatabase.LoadAssetAtPath<UnityMcpRuntimeProfile>(RuntimeProfilePath);
            var exists = profile != null;
            runtimeProfileStatusLabel.text = exists
                ? "Configured: " + RuntimeProfilePath
                : "Not configured. Runtime tools stay unavailable in Development Players until you create this profile.";
            runtimePrimaryButton.text = exists ? "Select runtime profile" : "Create runtime profile";
        }

        private void HandleRuntimeProfileAction()
        {
            var profile = AssetDatabase.LoadAssetAtPath<UnityMcpRuntimeProfile>(RuntimeProfilePath);
            if (profile == null) CreateRuntimeProfile();
            else
            {
                Selection.activeObject = profile;
                EditorGUIUtility.PingObject(profile);
            }
            RefreshRuntimeProfile();
        }

        private static bool RequiresEnablementConfirmation(UnityMcpToolDescriptor tool)
        {
            return string.Equals(tool.safety, "destructive", StringComparison.OrdinalIgnoreCase)
                || string.Equals(tool.safety, "unsafe", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ConfirmToolEnablement(UnityMcpToolDescriptor tool)
        {
            var tier = string.Equals(tool.safety, "unsafe", StringComparison.OrdinalIgnoreCase) ? "unsafe" : "destructive";
            return EditorUtility.DisplayDialog(
                "Enable " + tier + " UnityMCP tool",
                "Enable '" + GetToolTitle(tool) + "'? Connected MCP clients will be allowed to call this " + tier + " tool until you disable it again.",
                "Enable tool",
                "Cancel");
        }

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
                case UnityMcpGatewayState.Starting: return "Starting";
                case UnityMcpGatewayState.Running: return "Running";
                case UnityMcpGatewayState.Error: return "Needs attention";
                default: return "Stopped";
            }
        }

        private static string GetToolTitle(UnityMcpToolDescriptor tool)
        {
            if (!string.IsNullOrWhiteSpace(tool.title)) return tool.title;
            if (string.IsNullOrWhiteSpace(tool.name)) return "Unnamed tool";
            return string.Join(" ", tool.name.Split(new[] { '-', '_', '.' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Length == 0 ? part : char.ToUpperInvariant(part[0]) + part.Substring(1)));
        }

        private static string FormatCategory(string category)
        {
            if (string.IsNullOrWhiteSpace(category)) return "Project";
            return char.ToUpperInvariant(category[0]) + category.Substring(1);
        }

        private static string FormatSource(string source)
        {
            if (string.Equals(source, "builtin", StringComparison.OrdinalIgnoreCase)) return "Built-in";
            if (string.Equals(source, "project", StringComparison.OrdinalIgnoreCase)) return "Custom";
            return source;
        }

        private static string GetCategoryStateKey(string category)
        {
            using (var sha = SHA256.Create())
            {
                var project = Encoding.UTF8.GetBytes(Application.dataPath + "|" + category);
                return "DucMinh.UnityMcp.ToolsWindow.Category." + BitConverter.ToString(sha.ComputeHash(project)).Replace("-", "").Substring(0, 16);
            }
        }

        private static string ToElementIdentifier(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "item";
            var builder = new StringBuilder(value.Length);
            foreach (var character in value.ToLowerInvariant())
                builder.Append(char.IsLetterOrDigit(character) ? character : '-');
            return builder.ToString().Trim('-');
        }

        private static bool ContainsIgnoreCase(string value, string query)
        {
            return !string.IsNullOrWhiteSpace(value) && value.IndexOf(query ?? string.Empty, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        [MenuItem("Assets/Create/UnityMCP/Development Player Runtime Profile")]
        private static void CreateRuntimeProfile()
        {
            var existing = AssetDatabase.LoadAssetAtPath<UnityMcpRuntimeProfile>(RuntimeProfilePath);
            if (existing != null)
            {
                Selection.activeObject = existing;
                EditorGUIUtility.PingObject(existing);
                return;
            }
            System.IO.Directory.CreateDirectory(System.IO.Path.GetFullPath("Assets/UnityMCP/Resources"));
            var profile = CreateInstance<UnityMcpRuntimeProfile>();
            AssetDatabase.CreateAsset(profile, RuntimeProfilePath);
            AssetDatabase.SaveAssets();
            Selection.activeObject = profile;
            EditorGUIUtility.PingObject(profile);
        }
    }

    internal static class UnityMcpToolsWindowVisualElementExtensions
    {
        internal static T WithClass<T>(this T element, params string[] classNames) where T : VisualElement
        {
            foreach (var className in classNames)
                if (!string.IsNullOrWhiteSpace(className)) element.AddToClassList(className);
            return element;
        }
    }
}
