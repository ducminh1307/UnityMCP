using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DucMinh.UnityMcp.Editor
{
    /// <summary>UI Toolkit control centre for Unity CLI, Pipeline MCP configuration, and extension commands.</summary>
    public sealed class UnityMcpToolsWindow : EditorWindow
    {
        private const string StyleSheetPath = "Packages/com.ducminh.unity-mcp/Editor/UI/UnityMcpToolsWindow.uss";
        private const string PipelineEditorSettingsPath = "Project/Pipeline/Editor";
        private const string PipelineRuntimeSettingsPath = "Project/Pipeline/Runtime";
        private const string AllEffectsFilter = "All effects";

        private static readonly List<string> EffectFilterChoices = new List<string>
        {
            AllEffectsFilter,
            nameof(CommandEffect.ReadOnly),
            nameof(CommandEffect.Idempotent),
            nameof(CommandEffect.Additive),
            nameof(CommandEffect.Destructive),
            nameof(CommandEffect.Unspecified)
        };

        private enum Page
        {
            CliAndMcp,
            ExtensionCommands
        }

        private readonly Dictionary<Page, VisualElement> pages = new Dictionary<Page, VisualElement>();
        private readonly Dictionary<Page, Button> pageButtons = new Dictionary<Page, Button>();
        private readonly Color stoppedColor = new Color(0.48f, 0.48f, 0.48f);
        private readonly Color checkingColor = new Color(0.88f, 0.62f, 0.12f);
        private readonly Color runningColor = new Color(0.22f, 0.67f, 0.35f);

        private Page activePage = Page.CliAndMcp;
        private IVisualElementScheduledItem refreshSchedule;
        private IReadOnlyList<DiscoveredCommandInfo> discoveredCommands = Array.Empty<DiscoveredCommandInfo>();

        private Label headerStatusLabel;
        private VisualElement headerStatusDot;
        private Label headerCommandSummaryLabel;
        private Button commandsPageButton;

        private VisualElement cliStatusDot;
        private Label cliStatusTitleLabel;
        private Label cliStatusDetailLabel;
        private Label cliPathLabel;
        private Label cliVersionLabel;
        private VisualElement cliInstallGuidePanel;
        private Label cliInstallCommandsLabel;
        private TextField cliCommandOverrideField;

        private VisualElement pipelineStatusDot;
        private Label pipelineStatusTitleLabel;
        private Label pipelineStatusDetailLabel;
        private Label pipelinePortLabel;
        private Label pipelinePidLabel;
        private Label pipelineModeLabel;
        private Label pipelineHeartbeatLabel;

        private Label clientStatusLabel;
        private HelpBox clientFeedbackBox;

        private ToolbarSearchField commandSearchField;
        private DropdownField effectFilterField;
        private Label commandFilterSummaryLabel;
        private ScrollView commandsScrollView;
        private VisualElement commandsContainer;

        [MenuItem("Window/UnityMCP/Tools", priority = 10)]
        public static void Open() => GetWindow<UnityMcpToolsWindow>("UnityMCP Tools");

        private void OnEnable()
        {
            titleContent = new GUIContent("UnityMCP Tools");
            minSize = new Vector2(560, 420);
        }

        private void OnDisable()
        {
            refreshSchedule?.Pause();
            refreshSchedule = null;
        }

        public void CreateGUI()
        {
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

            var cliMcpPage = CreateScrollPage("unity-mcp-cli-page");
            var commandsPage = new VisualElement { name = "unity-mcp-commands-page" };
            commandsPage.AddToClassList("unity-mcp-page");
            commandsPage.style.flexGrow = 1;

            pages[Page.CliAndMcp] = cliMcpPage;
            pages[Page.ExtensionCommands] = commandsPage;
            pageHost.Add(cliMcpPage);
            pageHost.Add(commandsPage);

            BuildCliAndMcpPage(cliMcpPage);
            BuildExtensionCommandsPage(commandsPage);

            discoveredCommands = UnityCliDiscovery.DiscoverExtensionCommands();
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
            copy.Add(new Label("Unity CLI & Pipeline extension suite for Unity 6") { name = "unity-mcp-subtitle" }.WithClass("unity-mcp-header__subtitle"));
            identity.Add(copy);
            header.Add(identity);

            var metadata = new VisualElement { name = "unity-mcp-header-metadata" };
            metadata.AddToClassList("unity-mcp-header__metadata");

            var status = new VisualElement { name = "unity-mcp-header-status" };
            status.AddToClassList("unity-mcp-status-pill");
            headerStatusDot = new VisualElement { name = "unity-mcp-header-status-dot" };
            headerStatusDot.AddToClassList("unity-mcp-status__icon");
            headerStatusLabel = new Label("Pipeline Stopped");
            headerStatusLabel.AddToClassList("unity-mcp-status__title");
            status.Add(headerStatusDot);
            status.Add(headerStatusLabel);
            metadata.Add(status);

            headerCommandSummaryLabel = new Label("0 commands");
            headerCommandSummaryLabel.AddToClassList("unity-mcp-header__tool-summary");
            metadata.Add(headerCommandSummaryLabel);

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

            AddPageButton(navigation, Page.CliAndMcp, "Unity CLI & MCP", "Inspect Unity CLI, Pipeline server status, and configure AI clients.");
            commandsPageButton = AddPageButton(navigation, Page.ExtensionCommands, "Extension Commands", "Browse discovered DucMinh.UnityMcp, companion package, and project CLI commands.");
        }

        private Button AddPageButton(VisualElement parent, Page page, string text, string tooltip)
        {
            var button = new Button(() => ShowPage(page))
            {
                name = "unity-mcp-page-" + page.ToString().ToLowerInvariant(),
                text = text,
                tooltip = tooltip
            };
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

            if (page == Page.ExtensionCommands) RebuildCommandList();
        }

        private void BuildCliAndMcpPage(ScrollView page)
        {
            page.contentContainer.AddToClassList("unity-mcp-stack");

            // Card 1: Unity CLI status & install guidance
            var cliCard = CreateCard(
                "Unity CLI",
                "Official Unity CLI (`unity`) powers both shell commands and `unity mcp --tool-groups` stdio MCP servers.");
            page.Add(cliCard);

            var cliStatus = new VisualElement { name = "unity-mcp-cli-status" };
            cliStatus.AddToClassList("unity-mcp-status");
            cliStatusDot = new VisualElement { name = "unity-mcp-cli-status-dot" };
            cliStatusDot.AddToClassList("unity-mcp-status__icon");
            cliStatusTitleLabel = new Label("Checking...");
            cliStatusTitleLabel.AddToClassList("unity-mcp-status__title");
            cliStatusDetailLabel = new Label("Probing unity CLI availability.");
            cliStatusDetailLabel.AddToClassList("unity-mcp-status__detail");
            cliStatus.Add(cliStatusDot);
            cliStatus.Add(cliStatusTitleLabel);
            cliStatus.Add(cliStatusDetailLabel);
            cliCard.Add(cliStatus);

            cliPathLabel = AddKeyValueRow(cliCard, "Executable");
            cliVersionLabel = AddKeyValueRow(cliCard, "Version");

            var cliActions = new VisualElement();
            cliActions.AddToClassList("unity-mcp-action-row");
            var refreshCliButton = new Button(() =>
            {
                UnityCliDiscovery.RequestCliRefresh();
                RefreshCliSection();
            }) { name = "unity-mcp-cli-refresh", text = "Refresh CLI status" };
            refreshCliButton.AddToClassList("unity-mcp-secondary-button");
            cliActions.Add(refreshCliButton);
            cliCard.Add(cliActions);

            cliInstallGuidePanel = new VisualElement { name = "unity-mcp-cli-install-guide" };
            cliInstallGuidePanel.AddToClassList("unity-mcp-install-guide");
            cliInstallGuidePanel.style.display = DisplayStyle.None;
            cliInstallGuidePanel.Add(new Label("Install official Unity CLI").WithClass("unity-mcp-install-guide__title"));
            cliInstallGuidePanel.Add(new Label("Run the installer for your platform in a terminal, then click Refresh CLI status.")
                .WithClass("unity-mcp-install-guide__description"));
            cliInstallCommandsLabel = new Label { name = "unity-mcp-cli-install-commands" };
            cliInstallCommandsLabel.AddToClassList("unity-mcp-install-guide__commands");
            cliInstallCommandsLabel.AddToClassList("unity-mcp-code");
            cliInstallGuidePanel.Add(cliInstallCommandsLabel);

            var installActions = new VisualElement();
            installActions.AddToClassList("unity-mcp-action-row");
            var copyInstallButton = new Button(CopyCliInstallCommand)
            {
                name = "unity-mcp-cli-copy-install",
                text = "Copy install command"
            };
            copyInstallButton.AddToClassList("unity-mcp-primary-button");
            installActions.Add(copyInstallButton);
            cliInstallGuidePanel.Add(installActions);
            cliCard.Add(cliInstallGuidePanel);

            // Card 2: Pipeline server status & Project Settings shortcuts
            var pipelineCard = CreateCard(
                "Pipeline Server",
                "Managed by com.unity.pipeline in Project Settings. Status is read from Library/Pipeline/.unity-pipeline-port.");
            page.Add(pipelineCard);

            var pipelineStatus = new VisualElement { name = "unity-mcp-pipeline-status" };
            pipelineStatus.AddToClassList("unity-mcp-status");
            pipelineStatusDot = new VisualElement { name = "unity-mcp-pipeline-status-dot" };
            pipelineStatusDot.AddToClassList("unity-mcp-status__icon");
            pipelineStatusTitleLabel = new Label("Stopped");
            pipelineStatusTitleLabel.AddToClassList("unity-mcp-status__title");
            pipelineStatusDetailLabel = new Label("Pipeline server is not running.");
            pipelineStatusDetailLabel.AddToClassList("unity-mcp-status__detail");
            pipelineStatus.Add(pipelineStatusDot);
            pipelineStatus.Add(pipelineStatusTitleLabel);
            pipelineStatus.Add(pipelineStatusDetailLabel);
            pipelineCard.Add(pipelineStatus);

            pipelinePortLabel = AddKeyValueRow(pipelineCard, "Port");
            pipelinePidLabel = AddKeyValueRow(pipelineCard, "PID");
            pipelineModeLabel = AddKeyValueRow(pipelineCard, "Mode");
            pipelineHeartbeatLabel = AddKeyValueRow(pipelineCard, "Last heartbeat");

            var settingsActions = new VisualElement();
            settingsActions.AddToClassList("unity-mcp-action-row");
            var openEditorSettingsBtn = new Button(() => SettingsService.OpenProjectSettings(PipelineEditorSettingsPath))
            {
                name = "unity-mcp-open-pipeline-editor-settings",
                text = "Open Pipeline Editor Settings"
            };
            openEditorSettingsBtn.AddToClassList("unity-mcp-secondary-button");
            var openRuntimeSettingsBtn = new Button(() => SettingsService.OpenProjectSettings(PipelineRuntimeSettingsPath))
            {
                name = "unity-mcp-open-pipeline-runtime-settings",
                text = "Open Pipeline Runtime Settings"
            };
            openRuntimeSettingsBtn.AddToClassList("unity-mcp-secondary-button");
            settingsActions.Add(openEditorSettingsBtn);
            settingsActions.Add(openRuntimeSettingsBtn);
            pipelineCard.Add(settingsActions);

            // Card 3: One-click client config writers
            var clientCard = CreateCard(
                "AI Client Configuration",
                "Write project-scoped stdio MCP configuration (`unity mcp --tool-groups --project-path <project>`), skill, and rule files. Configs contain no secrets.");
            page.Add(clientCard);

            clientStatusLabel = AddKeyValueRow(clientCard, "Configured");

            cliCommandOverrideField = new TextField("CLI command override")
            {
                name = "unity-mcp-cli-command-override",
                tooltip = "Optional executable or command name written to client configs. Leave blank to auto-detect unity CLI."
            };
            clientCard.Add(CreateFormRow("CLI command", cliCommandOverrideField));

            var primaryActions = new VisualElement { name = "unity-mcp-client-primary-actions" };
            primaryActions.AddToClassList("unity-mcp-primary-action-stack");

            var antigravityBtn = new Button(ConfigureAntigravityForProject)
            {
                name = "unity-mcp-configure-antigravity",
                text = "Configure Antigravity for this project",
                tooltip = "Write .agents/mcp_config.json, .agents/skills/unity-mcp, .agents/rules/unity-mcp.md, and AGENTS.md."
            };
            antigravityBtn.AddToClassList("unity-mcp-primary-button");
            antigravityBtn.AddToClassList("unity-mcp-wide-button");

            var codexBtn = new Button(ConfigureCodexForProject)
            {
                name = "unity-mcp-configure-codex",
                text = "Configure Codex for this project",
                tooltip = "Write .codex/config.toml, .agents/skills/unity-mcp, and .agents/rules/unity-mcp.md."
            };
            codexBtn.AddToClassList("unity-mcp-primary-button");
            codexBtn.AddToClassList("unity-mcp-wide-button");

            var claudeBtn = new Button(ConfigureClaudeForProject)
            {
                name = "unity-mcp-configure-claude",
                text = "Configure Claude Code for this project",
                tooltip = "Write .mcp.json, .claude/skills/unity-mcp, and .claude/rules/unity-mcp.md."
            };
            claudeBtn.AddToClassList("unity-mcp-primary-button");
            claudeBtn.AddToClassList("unity-mcp-wide-button");

            primaryActions.Add(antigravityBtn);
            primaryActions.Add(codexBtn);
            primaryActions.Add(claudeBtn);
            clientCard.Add(primaryActions);

            var secondaryActions = new VisualElement();
            secondaryActions.AddToClassList("unity-mcp-action-row");
            secondaryActions.AddToClassList("unity-mcp-action-row--end");
            var copyConfigBtn = new Button(CopyMcpConfigSnippet)
            {
                name = "unity-mcp-copy-mcp-config",
                text = "Copy MCP JSON",
                tooltip = "Copy the stdio MCP server JSON entry for manual setup in another client."
            };
            copyConfigBtn.AddToClassList("unity-mcp-secondary-button");
            secondaryActions.Add(copyConfigBtn);
            clientCard.Add(secondaryActions);

            clientFeedbackBox = new HelpBox(string.Empty, HelpBoxMessageType.Info) { name = "unity-mcp-client-feedback" };
            clientFeedbackBox.AddToClassList("unity-mcp-help");
            clientFeedbackBox.style.display = DisplayStyle.None;
            clientCard.Add(clientFeedbackBox);
        }

        private void BuildExtensionCommandsPage(VisualElement page)
        {
            var pageHeader = new VisualElement();
            pageHeader.AddToClassList("unity-mcp-stack");
            page.Add(pageHeader);

            var filterCard = CreateCard(
                "Extension Commands",
                "Discovered [CliGroupCommand] and [CliCommand] commands outside built-in Unity.Pipeline.* assemblies.");
            filterCard.AddToClassList("unity-mcp-tool-filter-card");
            pageHeader.Add(filterCard);

            commandSearchField = new ToolbarSearchField
            {
                name = "unity-mcp-command-search",
                tooltip = "Search group, command name, description, tags, or assembly."
            };
            commandSearchField.AddToClassList("unity-mcp-tool-toolbar__search");
            commandSearchField.RegisterValueChangedCallback(_ => ApplyCommandFilter());
            filterCard.Add(CreateFormRow("Search", commandSearchField));

            effectFilterField = new DropdownField(new List<string>(EffectFilterChoices), 0)
            {
                name = "unity-mcp-command-effect-filter",
                tooltip = "Filter by declared CommandEffect."
            };
            effectFilterField.AddToClassList("unity-mcp-tool-toolbar__filter");
            effectFilterField.RegisterValueChangedCallback(_ => ApplyCommandFilter());
            filterCard.Add(CreateFormRow("Effect", effectFilterField));

            var toolbarActions = new VisualElement();
            toolbarActions.AddToClassList("unity-mcp-tool-toolbar__actions");
            var refreshBtn = new Button(() =>
            {
                discoveredCommands = UnityCliDiscovery.DiscoverExtensionCommands(forceRefresh: true);
                RebuildCommandList();
            })
            {
                name = "unity-mcp-command-refresh",
                text = "Refresh",
                tooltip = "Rescan TypeCache for non-Pipeline CLI commands."
            };
            refreshBtn.AddToClassList("unity-mcp-secondary-button");
            toolbarActions.Add(refreshBtn);
            filterCard.Add(CreateFormRow("Actions", toolbarActions));

            commandFilterSummaryLabel = new Label("Discovering commands...") { name = "unity-mcp-command-summary" };
            commandFilterSummaryLabel.AddToClassList("unity-mcp-tool-summary");
            filterCard.Add(commandFilterSummaryLabel);

            commandsScrollView = new ScrollView(ScrollViewMode.Vertical) { name = "unity-mcp-command-scroll" };
            commandsScrollView.AddToClassList("unity-mcp-scroll");
            commandsScrollView.style.flexGrow = 1;
            page.Add(commandsScrollView);

            commandsContainer = new VisualElement { name = "unity-mcp-command-list" };
            commandsContainer.AddToClassList("unity-mcp-tool-list");
            commandsScrollView.Add(commandsContainer);
        }

        private void RefreshAll()
        {
            RefreshCliSection();
            RefreshPipelineSection();
            RefreshClientStatusLabel();
            RefreshHeaderCommandSummary();
        }

        private void RefreshCliSection()
        {
            var status = UnityCliDiscovery.GetCachedCliStatus(triggerRefreshIfStale: true);
            if (cliStatusTitleLabel == null) return;

            if (status.IsChecking && !status.IsInstalled && string.IsNullOrEmpty(status.ExecutablePath))
            {
                cliStatusTitleLabel.text = "Checking...";
                cliStatusDetailLabel.text = "Checking PATH and standard Unity CLI locations.";
                cliStatusDot.style.backgroundColor = checkingColor;
            }
            else if (status.IsInstalled)
            {
                cliStatusTitleLabel.text = "Available";
                cliStatusDetailLabel.text = string.IsNullOrWhiteSpace(status.Error)
                    ? "unity CLI is installed and ready."
                    : "Detected unity CLI, but version check reported: " + status.Error;
                cliStatusDot.style.backgroundColor = runningColor;
            }
            else
            {
                cliStatusTitleLabel.text = "Not installed";
                cliStatusDetailLabel.text = "Official Unity CLI was not found on PATH or standard install directories.";
                cliStatusDot.style.backgroundColor = stoppedColor;
            }

            if (cliPathLabel != null)
                cliPathLabel.text = string.IsNullOrWhiteSpace(status.ExecutablePath) ? "-" : status.ExecutablePath;
            if (cliVersionLabel != null)
            {
                cliVersionLabel.text = !string.IsNullOrWhiteSpace(status.Version)
                    ? status.Version
                    : (status.IsChecking ? "Checking..." : "-");
            }

            if (cliInstallGuidePanel != null)
            {
                var showGuide = !status.IsInstalled && !status.IsChecking;
                cliInstallGuidePanel.style.display = showGuide ? DisplayStyle.Flex : DisplayStyle.None;
                if (showGuide && cliInstallCommandsLabel != null)
                {
                    cliInstallCommandsLabel.text =
                        "Active platform: " + UnityCliDiscovery.GetInstallCommand()
                        + "\nWindows: " + UnityCliDiscovery.WindowsInstallCommand
                        + "\nmacOS/Linux: " + UnityCliDiscovery.UnixInstallCommand;
                }
            }
        }

        private void RefreshPipelineSection()
        {
            var projectRoot = GetProjectRoot();
            var status = UnityCliDiscovery.ReadPipelineStatus(projectRoot);
            var stateText = status.IsRunning ? "Running" : "Stopped";
            var stateColor = status.IsRunning ? runningColor : stoppedColor;

            if (headerStatusLabel != null) headerStatusLabel.text = "Pipeline " + stateText;
            if (headerStatusDot != null) headerStatusDot.style.backgroundColor = stateColor;
            if (pipelineStatusTitleLabel != null) pipelineStatusTitleLabel.text = stateText;
            if (pipelineStatusDot != null) pipelineStatusDot.style.backgroundColor = stateColor;

            if (pipelineStatusDetailLabel != null)
            {
                if (!string.IsNullOrWhiteSpace(status.Error))
                    pipelineStatusDetailLabel.text = "Error reading descriptor: " + status.Error;
                else if (status.IsRunning)
                    pipelineStatusDetailLabel.text = "Pipeline server is active for this Unity Editor.";
                else if (status.DescriptorExists)
                    pipelineStatusDetailLabel.text = "Port descriptor exists, but the recorded process is not running.";
                else
                    pipelineStatusDetailLabel.text = "No active Library/Pipeline/.unity-pipeline-port descriptor found.";
            }

            if (pipelinePortLabel != null)
                pipelinePortLabel.text = status.IsRunning && status.Port > 0 ? status.Port.ToString(CultureInfo.InvariantCulture) : "-";
            if (pipelinePidLabel != null)
                pipelinePidLabel.text = status.Pid > 0 ? status.Pid.ToString(CultureInfo.InvariantCulture) : "-";
            if (pipelineModeLabel != null)
                pipelineModeLabel.text = string.IsNullOrWhiteSpace(status.Mode)
                    ? "-"
                    : (string.IsNullOrWhiteSpace(status.UnityVersion) ? status.Mode : status.Mode + " (" + status.UnityVersion + ")");
            if (pipelineHeartbeatLabel != null)
                pipelineHeartbeatLabel.text = status.LastHeartbeatUtc.HasValue
                    ? status.LastHeartbeatUtc.Value.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture)
                    : "-";
        }

        private void RefreshClientStatusLabel()
        {
            if (clientStatusLabel == null) return;
            var projectRoot = GetProjectRoot();
            var configured = new List<string>();
            if (UnityMcpJsonProjectConfig.IsManaged(projectRoot, UnityMcpJsonClient.Antigravity))
                configured.Add("Antigravity");
            if (UnityMcpCodexProjectConfig.IsManaged(projectRoot))
                configured.Add("Codex");
            if (UnityMcpJsonProjectConfig.IsManaged(projectRoot, UnityMcpJsonClient.Claude))
                configured.Add("Claude Code");

            clientStatusLabel.text = configured.Count == 0
                ? "None yet"
                : string.Join(", ", configured);
        }

        private void RefreshHeaderCommandSummary()
        {
            var count = discoveredCommands?.Count ?? 0;
            if (headerCommandSummaryLabel != null)
                headerCommandSummaryLabel.text = count + (count == 1 ? " extension command" : " extension commands");
            if (commandsPageButton != null)
                commandsPageButton.text = "Extension Commands (" + count + ")";
        }

        private void CopyCliInstallCommand()
        {
            var cmd = UnityCliDiscovery.GetInstallCommand();
            EditorGUIUtility.systemCopyBuffer = cmd;
            SetClientFeedback("Copied Unity CLI install command: " + cmd, false);
            ShowNotification(new GUIContent("Install command copied."));
        }

        private void CopyMcpConfigSnippet()
        {
            var projectRoot = GetProjectRoot();
            var unityCmd = GetConfiguredUnityCommand();
            var snippet = UnityMcpJsonProjectConfig.BuildSnippetJson(projectRoot, UnityMcpJsonProjectConfig.DefaultServerName, unityCmd);
            EditorGUIUtility.systemCopyBuffer = snippet;
            SetClientFeedback("Copied stdio MCP JSON entry for `unity mcp --tool-groups`.", false);
            ShowNotification(new GUIContent("MCP JSON copied."));
        }

        private void ConfigureAntigravityForProject()
        {
            var projectRoot = GetProjectRoot();
            var unityCmd = GetConfiguredUnityCommand();

            if (!UnityMcpJsonProjectConfig.TryWrite(projectRoot, UnityMcpJsonProjectConfig.DefaultServerName, unityCmd, UnityMcpJsonClient.Antigravity, out var configPath, out var error)
                || !UnityMcpProjectSkill.TryWrite(projectRoot, UnityMcpSkillClient.AgentSkills, out var skillPath, out error)
                || !UnityMcpProjectRule.TryWrite(projectRoot, UnityMcpRuleClient.AgentRules, out var rulePath, out error)
                || !UnityMcpAntigravityContext.TryWrite(projectRoot, out var contextPath, out error))
            {
                SetClientFeedback(error, true);
                return;
            }

            RefreshClientStatusLabel();
            SetClientFeedback(
                $"Antigravity configured:\n- {configPath}\n- {skillPath}\n- {rulePath}\n- {contextPath}",
                false);
            ShowNotification(new GUIContent("Antigravity configured."));
        }

        private void ConfigureCodexForProject()
        {
            var projectRoot = GetProjectRoot();
            var unityCmd = GetConfiguredUnityCommand();

            if (!UnityMcpCodexProjectConfig.TryWrite(projectRoot, UnityMcpCodexProjectConfig.DefaultServerName, unityCmd, out var configPath, out var error)
                || !UnityMcpProjectSkill.TryWrite(projectRoot, UnityMcpSkillClient.AgentSkills, out var skillPath, out error)
                || !UnityMcpProjectRule.TryWrite(projectRoot, UnityMcpRuleClient.AgentRules, out var rulePath, out error))
            {
                SetClientFeedback(error, true);
                return;
            }

            RefreshClientStatusLabel();
            SetClientFeedback(
                $"Codex configured:\n- {configPath}\n- {skillPath}\n- {rulePath}",
                false);
            ShowNotification(new GUIContent("Codex configured."));
        }

        private void ConfigureClaudeForProject()
        {
            var projectRoot = GetProjectRoot();
            var unityCmd = GetConfiguredUnityCommand();

            if (!UnityMcpJsonProjectConfig.TryWrite(projectRoot, UnityMcpJsonProjectConfig.DefaultServerName, unityCmd, UnityMcpJsonClient.Claude, out var configPath, out var error)
                || !UnityMcpProjectSkill.TryWrite(projectRoot, UnityMcpSkillClient.Claude, out var skillPath, out error)
                || !UnityMcpProjectRule.TryWrite(projectRoot, UnityMcpRuleClient.Claude, out var rulePath, out error))
            {
                SetClientFeedback(error, true);
                return;
            }

            RefreshClientStatusLabel();
            SetClientFeedback(
                $"Claude Code configured:\n- {configPath}\n- {skillPath}\n- {rulePath}",
                false);
            ShowNotification(new GUIContent("Claude Code configured."));
        }

        private void SetClientFeedback(string value, bool isError)
        {
            if (clientFeedbackBox == null) return;
            clientFeedbackBox.text = value ?? string.Empty;
            clientFeedbackBox.messageType = isError ? HelpBoxMessageType.Error : HelpBoxMessageType.Info;
            clientFeedbackBox.style.display = string.IsNullOrWhiteSpace(value) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private string GetConfiguredUnityCommand()
        {
            var custom = cliCommandOverrideField?.value;
            return UnityCliDiscovery.ResolveUnityCommand(custom);
        }

        private static string GetProjectRoot()
        {
            return UnityMcpProjectConfigFile.NormalizeProjectRoot(Path.Combine(Application.dataPath, ".."));
        }

        private void ApplyCommandFilter()
        {
            if (commandsScrollView != null) commandsScrollView.scrollOffset = Vector2.zero;
            RebuildCommandList();
        }

        private void RebuildCommandList()
        {
            if (commandsContainer == null) return;
            var preservedOffset = commandsScrollView == null ? Vector2.zero : commandsScrollView.scrollOffset;
            commandsContainer.Clear();

            var all = discoveredCommands ?? Array.Empty<DiscoveredCommandInfo>();
            var visible = all.Where(MatchesCommandFilter).ToList();
            if (commandFilterSummaryLabel != null)
                commandFilterSummaryLabel.text = visible.Count + " of " + all.Count + " extension commands shown";
            RefreshHeaderCommandSummary();

            if (visible.Count == 0)
            {
                commandsContainer.Add(CreateEmptyState(
                    all.Count == 0 ? "No extension commands discovered" : "No extension commands match this filter",
                    all.Count == 0
                        ? "Commands marked with [CliGroupCommand] or [CliCommand] outside Unity.Pipeline.* appear here."
                        : "Adjust the search query or Effect filter to view commands."));
                return;
            }

            var hasSearch = !string.IsNullOrWhiteSpace(commandSearchField?.value);
            foreach (var group in visible.GroupBy(c => c.Group, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                var groupCommands = group.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
                commandsContainer.Add(CreateCommandGroup(group.Key, groupCommands, hasSearch));
            }

            if (commandsScrollView != null) commandsScrollView.scrollOffset = preservedOffset;
        }

        private bool MatchesCommandFilter(DiscoveredCommandInfo command)
        {
            var effectFilter = effectFilterField?.value ?? AllEffectsFilter;
            if (!string.Equals(effectFilter, AllEffectsFilter, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(command.Effect.ToString(), effectFilter, StringComparison.OrdinalIgnoreCase))
                return false;

            var query = commandSearchField?.value;
            if (string.IsNullOrWhiteSpace(query)) return true;

            if (ContainsIgnoreCase(command.Group, query)
                || ContainsIgnoreCase(command.Name, query)
                || ContainsIgnoreCase(command.FullName, query)
                || ContainsIgnoreCase(command.Description, query)
                || ContainsIgnoreCase(command.AssemblyName, query))
                return true;

            if (command.Tags != null)
            {
                foreach (var tag in command.Tags)
                {
                    if (ContainsIgnoreCase(tag, query)) return true;
                }
            }

            return false;
        }

        private VisualElement CreateCommandGroup(string groupName, List<DiscoveredCommandInfo> commands, bool hasSearch)
        {
            var group = new Foldout { name = "unity-mcp-command-group-" + ToElementIdentifier(groupName) };
            group.AddToClassList("unity-mcp-tool-group");
            var stateKey = GetGroupStateKey(groupName);
            group.SetValueWithoutNotify(hasSearch || SessionState.GetBool(stateKey, true));
            group.text = groupName + "  (" + commands.Count + ")";
            group.RegisterValueChangedCallback(change => SessionState.SetBool(stateKey, change.newValue));

            foreach (var command in commands)
                group.Add(CreateCommandRow(command));

            return group;
        }

        private VisualElement CreateCommandRow(DiscoveredCommandInfo command)
        {
            var row = new VisualElement { name = "unity-mcp-command-" + ToElementIdentifier(command.FullName) };
            row.AddToClassList("unity-mcp-tool-row");

            var copy = new VisualElement();
            copy.AddToClassList("unity-mcp-tool-row__content");

            var heading = new VisualElement();
            heading.AddToClassList("unity-mcp-tool-row__heading");
            var title = new Label(command.Name) { tooltip = command.FullName };
            title.AddToClassList("unity-mcp-tool-row__name");
            heading.Add(title);

            var effectText = command.Effect.ToString();
            AddBadge(heading, effectText, effectText.ToLowerInvariant());
            if (!string.IsNullOrWhiteSpace(command.AssemblyName))
                AddBadge(heading, command.AssemblyName, "assembly");
            if (command.Tags != null)
            {
                foreach (var tag in command.Tags)
                {
                    if (!string.IsNullOrWhiteSpace(tag))
                        AddBadge(heading, tag, "tag");
                }
            }
            copy.Add(heading);

            if (!string.IsNullOrWhiteSpace(command.Description))
                copy.Add(new Label(command.Description).WithClass("unity-mcp-tool-row__description"));

            var cliRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginTop = 4 } };
            var snippetLabel = new Label(command.CliInvocation) { style = { flexGrow = 1 } };
            snippetLabel.AddToClassList("unity-mcp-tool-row__meta");
            snippetLabel.AddToClassList("unity-mcp-code");
            var copySnippetButton = new Button(() =>
            {
                EditorGUIUtility.systemCopyBuffer = command.CliInvocation;
                ShowNotification(new GUIContent("Copied: " + command.CliInvocation));
            }) { text = "Copy CLI", tooltip = "Copy CLI invocation to clipboard." };
            copySnippetButton.AddToClassList("unity-mcp-secondary-button");
            copySnippetButton.style.minWidth = 64;
            copySnippetButton.style.fontSize = 10;
            cliRow.Add(snippetLabel);
            cliRow.Add(copySnippetButton);
            copy.Add(cliRow);

            row.Add(copy);
            return row;
        }

        private static void AddBadge(VisualElement parent, string text, string variant)
        {
            var badge = new Label(text);
            badge.AddToClassList("unity-mcp-badge");
            badge.AddToClassList("unity-mcp-badge--" + ToElementIdentifier(variant));
            parent.Add(badge);
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

        private static Label AddKeyValueRow(VisualElement parent, string key)
        {
            var row = new VisualElement();
            row.AddToClassList("unity-mcp-key-value");
            row.Add(new Label(key).WithClass("unity-mcp-key-value__key"));
            var valueLabel = new Label("-");
            valueLabel.AddToClassList("unity-mcp-key-value__value");
            valueLabel.AddToClassList("unity-mcp-code");
            row.Add(valueLabel);
            parent.Add(row);
            return valueLabel;
        }

        private static VisualElement CreateEmptyState(string title, string description)
        {
            var empty = new VisualElement();
            empty.AddToClassList("unity-mcp-empty-state");
            empty.Add(new Label(title).WithClass("unity-mcp-empty-state__title"));
            empty.Add(new Label(description).WithClass("unity-mcp-empty-state__description"));
            return empty;
        }

        private static string GetGroupStateKey(string groupName)
        {
            using (var sha = SHA256.Create())
            {
                var project = Encoding.UTF8.GetBytes(Application.dataPath + "|" + groupName);
                return "DucMinh.UnityMcp.ToolsWindow.Group." + BitConverter.ToString(sha.ComputeHash(project)).Replace("-", "").Substring(0, 16);
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
