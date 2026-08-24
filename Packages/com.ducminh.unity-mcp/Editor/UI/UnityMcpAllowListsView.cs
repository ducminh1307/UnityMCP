using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DucMinh.UnityMcp.Editor
{
    internal sealed class UnityMcpAllowListDefinition
    {
        internal UnityMcpAllowListDefinition(string id, string title, string description, Type assetType, string propertyName, string defaultFileName, params string[] toolNames)
        {
            Id = id;
            Title = title;
            Description = description;
            AssetType = assetType;
            PropertyName = propertyName;
            DefaultFileName = defaultFileName;
            ToolNames = toolNames ?? Array.Empty<string>();
        }

        internal string Id { get; }
        internal string Title { get; }
        internal string Description { get; }
        internal Type AssetType { get; }
        internal string PropertyName { get; }
        internal string DefaultFileName { get; }
        internal IReadOnlyList<string> ToolNames { get; }
    }

    /// <summary>
    /// Catalog of active project-owned policy assets. The legacy ScriptableObject allowlist is
    /// intentionally absent because generic ScriptableObject tools no longer consume it.
    /// </summary>
    internal static class UnityMcpAllowListCatalog
    {
        internal static readonly IReadOnlyList<UnityMcpAllowListDefinition> Definitions = new[]
        {
            new UnityMcpAllowListDefinition(
                "menu",
                "Menu items",
                "Exact Unity Editor menu paths that an enabled MCP client may invoke.",
                typeof(UnityMcpMenuAllowlist),
                "allowedMenuItems",
                "UnityMcpMenuAllowlist",
                "editor-menu-execute"),
            new UnityMcpAllowListDefinition(
                "reflection",
                "Reflection",
                "Exact object types, members, and instance methods exposed to the reflection tools.",
                typeof(UnityMcpReflectionAllowlist),
                "types",
                "UnityMcpReflectionAllowlist",
                "object-get", "object-set", "method-find", "method-call"),
            new UnityMcpAllowListDefinition(
                "csharp-commands",
                "C# commands",
                "Reviewed public static project commands that the execute-csharp tool may invoke.",
                typeof(UnityMcpCSharpCommandAllowlist),
                "commands",
                "UnityMcpCSharpCommandAllowlist",
                "execute-csharp"),
            new UnityMcpAllowListDefinition(
                "batch",
                "Batch execution",
                "Enabled UnityMCP tools that may be composed by the bounded batch executor.",
                typeof(UnityMcpBatchAllowlist),
                "allowedToolNames",
                "UnityMcpBatchAllowlist",
                "batch-execute")
        };

        internal static List<ScriptableObject> FindAssets(UnityMcpAllowListDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            return AssetDatabase.FindAssets("t:" + definition.AssetType.Name, new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(path => AssetDatabase.LoadAssetAtPath(path, definition.AssetType) as ScriptableObject)
                .Where(asset => asset != null && definition.AssetType.IsInstanceOfType(asset))
                .OrderBy(asset => AssetDatabase.GetAssetPath(asset), StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        internal static ScriptableObject CreateAsset(UnityMcpAllowListDefinition definition, string assetPath)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            var normalized = (assetPath ?? string.Empty).Replace('\\', '/');
            if (!normalized.StartsWith("Assets/", StringComparison.Ordinal) || !normalized.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) || normalized.Contains(".."))
                throw new ArgumentException("Allowlist assets must use a project-relative .asset path under Assets/.", nameof(assetPath));
            if (AssetDatabase.LoadMainAssetAtPath(normalized) != null)
                throw new ArgumentException("An asset already exists at the selected path.", nameof(assetPath));

            var asset = ScriptableObject.CreateInstance(definition.AssetType);
            AssetDatabase.CreateAsset(asset, normalized);
            AssetDatabase.SaveAssetIfDirty(asset);
            return asset;
        }
    }

    /// <summary>
    /// Local Editor UI for project-owned allowlist assets. This view does not expose any MCP
    /// mutation path; it edits the same ScriptableObjects consumed by the allowlisted tools.
    /// </summary>
    internal sealed class UnityMcpAllowListsView : VisualElement
    {
        private readonly List<UnityMcpAllowListSection> sections = new List<UnityMcpAllowListSection>();

        internal UnityMcpAllowListsView(Func<UnityMcpRegistry> registryProvider)
        {
            name = "unity-mcp-allow-list-content";
            AddToClassList("unity-mcp-allow-list-content");

            var introduction = CreateCard(
                "Project allow lists",
                "Edit the project-owned ScriptableObject policies used by powerful UnityMCP tools. Tool enablement remains separate on the Tools page.");
            introduction.Add(new HelpBox(
                "Only a local Unity Editor user can create or edit these assets. MCP clients cannot grant themselves additional allowlist entries.",
                HelpBoxMessageType.Info));
            Add(introduction);

            foreach (var definition in UnityMcpAllowListCatalog.Definitions)
            {
                var section = new UnityMcpAllowListSection(definition, registryProvider);
                sections.Add(section);
                Add(section);
            }

            Refresh();
        }

        internal void Refresh()
        {
            foreach (var section in sections) section.Refresh();
        }

        internal void RefreshToolStatuses()
        {
            foreach (var section in sections) section.RefreshToolStatus();
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
    }

    internal sealed class UnityMcpAllowListSection : VisualElement
    {
        private readonly UnityMcpAllowListDefinition definition;
        private readonly Func<UnityMcpRegistry> registryProvider;
        private readonly ObjectField assetField;
        private readonly Label assetSummaryLabel;
        private readonly Label toolStatusLabel;
        private readonly VisualElement editorHost;
        private readonly Button locateButton;
        private readonly Button saveButton;
        private ScriptableObject selectedAsset;
        private SerializedObject serializedAsset;

        internal UnityMcpAllowListSection(UnityMcpAllowListDefinition definition, Func<UnityMcpRegistry> registryProvider)
        {
            this.definition = definition ?? throw new ArgumentNullException(nameof(definition));
            this.registryProvider = registryProvider;
            name = "unity-mcp-allow-list-" + definition.Id;
            AddToClassList("unity-mcp-card");
            AddToClassList("unity-mcp-allow-list-section");

            var header = new VisualElement();
            header.AddToClassList("unity-mcp-card__header");
            var copy = new VisualElement();
            copy.Add(new Label(definition.Title).WithClass("unity-mcp-card__title"));
            copy.Add(new Label(definition.Description).WithClass("unity-mcp-card__description"));
            header.Add(copy);
            Add(header);

            toolStatusLabel = new Label { name = name + "-tools" };
            toolStatusLabel.AddToClassList("unity-mcp-allow-list-tools");
            Add(toolStatusLabel);

            assetField = new ObjectField
            {
                name = name + "-asset",
                objectType = definition.AssetType,
                allowSceneObjects = false,
                tooltip = "Select an existing " + definition.AssetType.Name + " asset."
            };
            assetField.RegisterValueChangedCallback(change => SelectAsset(change.newValue as ScriptableObject));
            Add(CreateFormRow("Policy asset", assetField));

            assetSummaryLabel = new Label { name = name + "-summary" };
            assetSummaryLabel.AddToClassList("unity-mcp-allow-list-summary");
            Add(assetSummaryLabel);

            var actions = new VisualElement { name = name + "-actions" };
            actions.AddToClassList("unity-mcp-action-row");
            actions.AddToClassList("unity-mcp-form-actions");
            var createButton = new Button(CreateAsset) { name = name + "-create", text = "Create...", tooltip = "Create another project-owned policy asset under Assets/." };
            createButton.AddToClassList("unity-mcp-primary-button");
            locateButton = new Button(LocateAsset) { name = name + "-locate", text = "Locate", tooltip = "Select and ping this asset in the Project window." };
            locateButton.AddToClassList("unity-mcp-secondary-button");
            var refreshButton = new Button(Refresh) { name = name + "-refresh", text = "Refresh", tooltip = "Rescan project allowlist assets." };
            refreshButton.AddToClassList("unity-mcp-secondary-button");
            saveButton = new Button(SaveAsset) { name = name + "-save", text = "Save asset", tooltip = "Apply serialized edits and save only this policy asset." };
            saveButton.AddToClassList("unity-mcp-secondary-button");
            actions.Add(createButton);
            actions.Add(locateButton);
            actions.Add(refreshButton);
            actions.Add(saveButton);
            Add(actions);

            editorHost = new VisualElement { name = name + "-editor" };
            editorHost.AddToClassList("unity-mcp-allow-list-editor");
            Add(editorHost);
        }

        internal void Refresh()
        {
            var assets = UnityMcpAllowListCatalog.FindAssets(definition);
            assetSummaryLabel.text = assets.Count == 0
                ? "No " + definition.AssetType.Name + " assets found under Assets/."
                : assets.Count + " policy asset" + (assets.Count == 1 ? string.Empty : "s") + " found under Assets/.";

            var currentPath = selectedAsset == null ? string.Empty : AssetDatabase.GetAssetPath(selectedAsset);
            var currentIsValid = !string.IsNullOrEmpty(currentPath) && assets.Contains(selectedAsset);
            if (!currentIsValid) SetAssetWithoutNotify(assets.FirstOrDefault());
            RefreshToolStatus();
        }

        internal void RefreshToolStatus()
        {
            var registry = registryProvider?.Invoke();
            var states = new List<string>();
            foreach (var toolName in definition.ToolNames)
            {
                var tool = registry?.Tools.FirstOrDefault(candidate => string.Equals(candidate.name, toolName, StringComparison.Ordinal));
                states.Add(tool == null ? toolName + " (unavailable)" : toolName + (tool.enabled ? " (enabled)" : " (disabled)"));
            }
            toolStatusLabel.text = "Used by: " + string.Join(", ", states);
        }

        private void SetAssetWithoutNotify(ScriptableObject asset)
        {
            assetField.SetValueWithoutNotify(asset);
            SelectAsset(asset);
        }

        private void SelectAsset(ScriptableObject asset)
        {
            if (asset != null && !definition.AssetType.IsInstanceOfType(asset)) asset = null;
            if (asset != null)
            {
                var path = AssetDatabase.GetAssetPath(asset).Replace('\\', '/');
                if (!path.StartsWith("Assets/", StringComparison.Ordinal)) asset = null;
            }
            if (!ReferenceEquals(assetField.value, asset)) assetField.SetValueWithoutNotify(asset);

            selectedAsset = asset;
            serializedAsset = asset == null ? null : new SerializedObject(asset);
            locateButton.SetEnabled(asset != null);
            saveButton.SetEnabled(asset != null);
            RebuildEditor();
        }

        private void RebuildEditor()
        {
            editorHost.Unbind();
            editorHost.Clear();
            if (serializedAsset == null)
            {
                editorHost.Add(new HelpBox("Select an existing policy asset or create a new one to edit its allowlist entries.", HelpBoxMessageType.Info));
                return;
            }

            var property = serializedAsset.FindProperty(definition.PropertyName);
            if (property == null)
            {
                editorHost.Add(new HelpBox("The selected asset does not contain the expected serialized policy field '" + definition.PropertyName + "'.", HelpBoxMessageType.Error));
                return;
            }

            var path = AssetDatabase.GetAssetPath(selectedAsset);
            editorHost.Add(new Label(path).WithClass("unity-mcp-allow-list-path", "unity-mcp-code"));
            var propertyField = new PropertyField(property) { name = name + "-property" };
            propertyField.AddToClassList("unity-mcp-allow-list-property");
            editorHost.Add(propertyField);
            editorHost.Bind(serializedAsset);
        }

        private void CreateAsset()
        {
            var path = EditorUtility.SaveFilePanelInProject(
                "Create " + definition.Title + " allowlist",
                definition.DefaultFileName,
                "asset",
                "Choose a project path for the ScriptableObject policy asset.",
                "Assets");
            if (string.IsNullOrWhiteSpace(path)) return;

            try
            {
                var asset = UnityMcpAllowListCatalog.CreateAsset(definition, path);
                SetAssetWithoutNotify(asset);
                Refresh();
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
            }
            catch (Exception exception)
            {
                EditorUtility.DisplayDialog("Could not create allowlist", exception.Message, "OK");
            }
        }

        private void LocateAsset()
        {
            if (selectedAsset == null) return;
            Selection.activeObject = selectedAsset;
            EditorGUIUtility.PingObject(selectedAsset);
        }

        private void SaveAsset()
        {
            if (selectedAsset == null || serializedAsset == null) return;
            serializedAsset.ApplyModifiedProperties();
            AssetDatabase.SaveAssetIfDirty(selectedAsset);
            assetSummaryLabel.text = "Saved " + AssetDatabase.GetAssetPath(selectedAsset) + ".";
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
    }
}
