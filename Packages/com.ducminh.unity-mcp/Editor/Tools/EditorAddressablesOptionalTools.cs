using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace DucMinh.UnityMcp.Editor
{
    [Serializable] public sealed class AddressablesGroupsListInput
    {
        /// <summary>Optional AddressableAssetSettings asset under Assets/. Omit to use the project's configured default settings without creating one.</summary>
        public string settingsPath;
    }

    [Serializable] public sealed class AddressablesGroupInfo
    {
        public string name;
        public string guid;
        public bool isDefault;
        public bool readOnly;
        public int entryCount;
        public List<string> schemaTypes = new List<string>();
    }

    [Serializable] public sealed class AddressablesGroupsListOutput
    {
        public string settingsPath;
        public int groupCount;
        public List<AddressablesGroupInfo> groups = new List<AddressablesGroupInfo>();
    }

    [Serializable] public sealed class AddressablesGroupCreateInput
    {
        /// <summary>Optional AddressableAssetSettings asset under Assets/. Omit to use the configured default settings.</summary>
        public string settingsPath;
        public string groupName;
        /// <summary>Optional existing group whose schema types are copied. Omit to use the existing default group.</summary>
        public string templateGroupName;
        public bool setAsDefaultGroup;
        public bool apply;
    }

    [Serializable] public sealed class AddressablesGroupCreateOutput
    {
        public bool dryRun;
        public bool created;
        public string settingsPath;
        public string groupName;
        public string groupGuid;
        public string templateGroupName;
        public bool setAsDefaultGroup;
        public List<string> schemaTypes = new List<string>();
        public bool rollbackSupported;
        public List<ChangeJournalEntry> journal = new List<ChangeJournalEntry>();
    }

    [Serializable] public sealed class AddressablesEntryAddInput
    {
        /// <summary>Optional AddressableAssetSettings asset under Assets/. Omit to use the configured default settings.</summary>
        public string settingsPath;
        public string groupName;
        /// <summary>Existing non-folder asset path under Assets/. Assets inside any Resources folder are deliberately rejected.</summary>
        public string assetPath;
        /// <summary>Optional address. If omitted, an existing entry keeps its address and a new entry uses Addressables' default address.</summary>
        public string address;
        public bool apply;
    }

    [Serializable] public sealed class AddressablesEntryAddOutput
    {
        public bool dryRun;
        public bool changed;
        public bool created;
        public bool moved;
        public string settingsPath;
        public string groupName;
        public string previousGroupName;
        public string assetPath;
        public string assetGuid;
        public string address;
        public bool rollbackSupported;
        public List<ChangeJournalEntry> journal = new List<ChangeJournalEntry>();
    }

    [Serializable] public sealed class AddressablesEntryRemoveInput
    {
        /// <summary>Optional AddressableAssetSettings asset under Assets/. Omit to use the configured default settings.</summary>
        public string settingsPath;
        public string assetPath;
        public bool apply;
    }

    [Serializable] public sealed class AddressablesEntryRemoveOutput
    {
        public bool dryRun;
        public bool removed;
        public string settingsPath;
        public string groupName;
        public string assetPath;
        public string assetGuid;
        public string address;
        public bool rollbackSupported;
        public List<ChangeJournalEntry> journal = new List<ChangeJournalEntry>();
    }

    [Serializable] public sealed class AddressablesBuildInput
    {
        /// <summary>Optional path to the configured default AddressableAssetSettings asset. A non-default settings asset is rejected because Addressables' public build entry point is static.</summary>
        public string settingsPath;
        public bool apply;
    }

    [Serializable] public sealed class AddressablesBuildOutput
    {
        public bool dryRun;
        public bool accepted;
        public string jobId;
        public string status;
        public string settingsPath;
        public string summary;
    }

    /// <summary>
    /// Reflection-only Addressables tools. No Addressables assembly is referenced at compile time;
    /// RequiredType keeps these tools out of the live registry unless the package is installed.
    /// The mutating tools intentionally use the documented public settings APIs only and do not
    /// initialize a missing Addressables project configuration.
    /// </summary>
    public static class EditorAddressablesOptionalTools
    {
        private const string SettingsTypeName = "UnityEditor.AddressableAssets.Settings.AddressableAssetSettings";
        private const string DefaultSettingsTypeName = "UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject";
        private const string GroupTypeName = "UnityEditor.AddressableAssets.Settings.AddressableAssetGroup";
        private const string SchemaTypeName = "UnityEditor.AddressableAssets.Settings.AddressableAssetGroupSchema";
        private const string BuildResultTypeName = "UnityEditor.AddressableAssets.Build.AddressablesPlayerBuildResult";
        private const int MaximumGroupNameLength = 128;
        private const int MaximumAddressLength = 512;
        private const int MaximumBuildErrorLength = 1024;

        [UnityMcpTool("addressables-groups-list", Description = "List Addressables groups and schema types from an existing settings asset without creating configuration.", Category = "project-extensions", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.SafeRead, RequiredType = SettingsTypeName)]
        public static AddressablesGroupsListOutput AddressablesGroupsList(AddressablesGroupsListInput input, UnityMcpContext context)
        {
            var settings = RequireSettings(input == null ? null : input.settingsPath, out var settingsPath);
            var groups = EnumerateGroups(settings).Select(ToGroupInfo).OrderBy(group => group.name, StringComparer.Ordinal).ToList();
            return new AddressablesGroupsListOutput
            {
                settingsPath = settingsPath,
                groupCount = groups.Count,
                groups = groups
            };
        }

        [UnityMcpTool("addressables-group-create", Description = "Create one Addressables group by copying validated schema types from an existing template/default group; dry-run unless apply is true.", Category = "project-extensions", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Write, SupportsDryRun = true, RequiredType = SettingsTypeName)]
        public static AddressablesGroupCreateOutput AddressablesGroupCreate(AddressablesGroupCreateInput input, UnityMcpContext context)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            var settings = RequireSettings(input.settingsPath, out var settingsPath);
            var groupName = RequireGroupName(input.groupName, "groupName");
            if (FindGroup(settings, groupName) != null) throw new ArgumentException("An Addressables group with groupName already exists.");
            var template = RequireTemplateGroup(settings, input.templateGroupName, out var templateName);
            if (ReadBool(template, "ReadOnly")) throw new ArgumentException("templateGroupName must not identify a read-only Addressables group.");
            if (input.setAsDefaultGroup && !ReadBool(template, "Default"))
                throw new ArgumentException("setAsDefaultGroup requires a template group that is already the Addressables default group.");
            var schemaTypes = ReadSchemaTypes(template);
            if (schemaTypes.Count == 0)
                throw new ArgumentException("The selected template group has no schemas. Use a configured Addressables group so the new group can participate in the active build.");

            var output = new AddressablesGroupCreateOutput
            {
                dryRun = context.DryRun,
                created = !context.DryRun,
                settingsPath = settingsPath,
                groupName = groupName,
                templateGroupName = templateName,
                setAsDefaultGroup = input.setAsDefaultGroup,
                schemaTypes = schemaTypes.Select(type => type.FullName ?? type.Name).OrderBy(name => name, StringComparer.Ordinal).ToList(),
                // Addressables creates persistent group/schema assets. Unity Undo is registered where
                // possible, but this tool cannot promise a complete atomic rollback of every subasset.
                rollbackSupported = false,
                journal = new List<ChangeJournalEntry> { new ChangeJournalEntry { operation = "create-addressables-group", after = groupName } }
            };
            if (context.DryRun) return output;

            var group = CreateGroup(settings, groupName, input.setAsDefaultGroup, schemaTypes);
            if (group == null) throw new InvalidOperationException("Addressables did not return the newly-created group.");
            if (group is UnityEngine.Object unityGroup) Undo.RegisterCreatedObjectUndo(unityGroup, "UnityMCP Create Addressables Group");
            if (settings is UnityEngine.Object unitySettings) EditorUtility.SetDirty(unitySettings);
            if (group is UnityEngine.Object dirtyGroup) EditorUtility.SetDirty(dirtyGroup);
            AssetDatabase.SaveAssets();
            output.groupGuid = ReadString(group, "Guid");
            return output;
        }

        [UnityMcpTool("addressables-entry-add", Description = "Add or move one existing non-Resources asset into a writable Addressables group, optionally setting its address; dry-run unless apply is true.", Category = "project-extensions", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Write, SupportsDryRun = true, RequiredType = SettingsTypeName)]
        public static AddressablesEntryAddOutput AddressablesEntryAdd(AddressablesEntryAddInput input, UnityMcpContext context)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            var settings = RequireSettings(input.settingsPath, out var settingsPath);
            var groupName = RequireGroupName(input.groupName, "groupName");
            var targetGroup = FindGroup(settings, groupName) ?? throw new ArgumentException("groupName does not identify an existing Addressables group.");
            if (ReadBool(targetGroup, "ReadOnly")) throw new ArgumentException("groupName identifies a read-only Addressables group.");
            var assetPath = NormalizeExistingAssetPath(input.assetPath, "assetPath");
            if (AssetDatabase.IsValidFolder(assetPath)) throw new ArgumentException("assetPath must identify an asset, not a folder.");
            if (IsResourcesPath(assetPath)) throw new ArgumentException("assetPath may not be under a Resources folder because Addressables manages those assets in a read-only built-in group.");
            if (AssetDatabase.LoadMainAssetAtPath(assetPath) == null) throw new ArgumentException("assetPath must identify a loadable main asset.");
            var guid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrWhiteSpace(guid)) throw new ArgumentException("Unity could not resolve an asset GUID for assetPath.");
            var entry = FindAssetEntry(settings, guid);
            var previousGroup = entry == null ? null : GetMember(entry, "parentGroup");
            var previousGroupName = previousGroup == null ? null : ReadGroupName(previousGroup);
            var requestedAddress = string.IsNullOrWhiteSpace(input.address) ? null : RequireAddress(input.address);
            var existingAddress = entry == null ? null : ReadString(entry, "address");
            var created = entry == null;
            var moved = entry == null || !SameUnityObject(previousGroup, targetGroup);
            var effectiveAddress = requestedAddress ?? existingAddress;

            var output = new AddressablesEntryAddOutput
            {
                dryRun = context.DryRun,
                changed = !context.DryRun && (created || moved || requestedAddress != null),
                created = created,
                moved = moved,
                settingsPath = settingsPath,
                groupName = groupName,
                previousGroupName = previousGroupName,
                assetPath = assetPath,
                assetGuid = guid,
                address = effectiveAddress,
                rollbackSupported = true,
                journal = new List<ChangeJournalEntry> { new ChangeJournalEntry { operation = "add-addressables-entry", before = previousGroupName, after = groupName + ":" + assetPath } }
            };
            if (context.DryRun) return output;

            // Preserve the settings asset's revision when the requested entry is already in the
            // desired group and the caller did not ask to change its address.
            if (!created && !moved && requestedAddress == null) return output;

            if (settings is UnityEngine.Object unitySettings) Undo.RecordObject(unitySettings, "UnityMCP Add Addressables Entry");
            if (targetGroup is UnityEngine.Object unityGroup) Undo.RecordObject(unityGroup, "UnityMCP Add Addressables Entry");
            if (previousGroup is UnityEngine.Object previousUnityGroup && !ReferenceEquals(previousUnityGroup, targetGroup)) Undo.RecordObject(previousUnityGroup, "UnityMCP Move Addressables Entry");
            var changedEntry = CreateOrMoveEntry(settings, guid, targetGroup);
            if (changedEntry == null) throw new InvalidOperationException("Addressables did not return an asset entry.");
            if (requestedAddress != null) SetEntryAddress(changedEntry, requestedAddress);
            if (settings is UnityEngine.Object dirtySettings) EditorUtility.SetDirty(dirtySettings);
            if (targetGroup is UnityEngine.Object dirtyTargetGroup) EditorUtility.SetDirty(dirtyTargetGroup);
            AssetDatabase.SaveAssets();
            output.address = ReadString(changedEntry, "address");
            return output;
        }

        [UnityMcpTool("addressables-entry-remove", Description = "Remove one existing writable Addressables entry without deleting its asset; dry-run unless apply is true.", Category = "project-extensions", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Destructive, SupportsDryRun = true, RequiredType = SettingsTypeName)]
        public static AddressablesEntryRemoveOutput AddressablesEntryRemove(AddressablesEntryRemoveInput input, UnityMcpContext context)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            var settings = RequireSettings(input.settingsPath, out var settingsPath);
            var assetPath = NormalizeExistingAssetPath(input.assetPath, "assetPath");
            var guid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrWhiteSpace(guid)) throw new ArgumentException("Unity could not resolve an asset GUID for assetPath.");
            var entry = FindAssetEntry(settings, guid) ?? throw new ArgumentException("assetPath is not currently an Addressables entry.");
            var group = GetMember(entry, "parentGroup") ?? throw new InvalidOperationException("The Addressables entry has no parent group.");
            if (ReadBool(group, "ReadOnly")) throw new ArgumentException("The Addressables entry belongs to a read-only group and cannot be removed by this tool.");
            var groupName = ReadGroupName(group);
            var address = ReadString(entry, "address");
            var output = new AddressablesEntryRemoveOutput
            {
                dryRun = context.DryRun,
                removed = !context.DryRun,
                settingsPath = settingsPath,
                groupName = groupName,
                assetPath = assetPath,
                assetGuid = guid,
                address = address,
                rollbackSupported = true,
                journal = new List<ChangeJournalEntry> { new ChangeJournalEntry { operation = "remove-addressables-entry", before = groupName + ":" + assetPath } }
            };
            if (context.DryRun) return output;

            if (settings is UnityEngine.Object unitySettings) Undo.RecordObject(unitySettings, "UnityMCP Remove Addressables Entry");
            if (group is UnityEngine.Object unityGroup) Undo.RecordObject(unityGroup, "UnityMCP Remove Addressables Entry");
            RemoveAssetEntry(settings, guid);
            if (settings is UnityEngine.Object dirtySettings) EditorUtility.SetDirty(dirtySettings);
            if (group is UnityEngine.Object dirtyGroup) EditorUtility.SetDirty(dirtyGroup);
            AssetDatabase.SaveAssets();
            return output;
        }

        [UnityMcpTool("addressables-build", Description = "Build Addressables player content with the project's configured default settings as a local job; dry-run unless apply is true. Cancellation is honored before the synchronous Unity build begins.", Category = "project-extensions", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Unsafe, SupportsDryRun = true, SupportsCancellation = true, ReturnsJob = true, RequiredType = SettingsTypeName, TimeoutMs = 600000)]
        public static AddressablesBuildOutput AddressablesBuild(AddressablesBuildInput input, UnityMcpContext context)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Addressables content cannot be built while the Unity Editor is compiling or updating.");
            var defaultSettings = RequireSettings(null, out var defaultSettingsPath);
            if (!string.IsNullOrWhiteSpace(input.settingsPath))
            {
                var requested = RequireSettings(input.settingsPath, out var requestedPath);
                if (!SameUnityObject(defaultSettings, requested))
                    throw new ArgumentException("settingsPath must identify the configured default Addressables settings because the public BuildPlayerContent API is static.");
                defaultSettingsPath = requestedPath;
            }
            var buildMethod = ResolveBuildMethod();
            if (context.DryRun)
            {
                return new AddressablesBuildOutput
                {
                    dryRun = true,
                    settingsPath = defaultSettingsPath,
                    status = "dry-run",
                    summary = "Dry run: build Addressables player content using the configured default settings."
                };
            }

            var handle = EditorWorkflowJobRunner.Start(new AddressablesBuildOperation(buildMethod, defaultSettingsPath));
            return new AddressablesBuildOutput
            {
                accepted = true,
                jobId = handle.jobId,
                status = handle.status,
                settingsPath = defaultSettingsPath,
                summary = "Addressables player-content build queued. Unity may be busy while its synchronous build runs."
            };
        }

        private static object RequireSettings(string requestedPath, out string settingsPath)
        {
            var settingsType = RequireType(SettingsTypeName);
            object settings;
            if (string.IsNullOrWhiteSpace(requestedPath))
            {
                var defaultType = RequireType(DefaultSettingsTypeName);
                var getSettings = defaultType.GetMethod("GetSettings", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(bool) }, null);
                if (getSettings == null) throw new InvalidOperationException("The installed Addressables package does not expose AddressableAssetSettingsDefaultObject.GetSettings(bool).");
                settings = getSettings.Invoke(null, new object[] { false });
                if (settings == null)
                    throw new InvalidOperationException("No configured Addressables settings asset exists. Create and configure Addressables in Unity first; UnityMCP will not create it implicitly.");
            }
            else
            {
                var normalizedPath = NormalizeExistingAssetPath(requestedPath, "settingsPath");
                if (!normalizedPath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("settingsPath must identify an .asset file under Assets/.");
                settings = AssetDatabase.LoadAssetAtPath(normalizedPath, settingsType);
                if (settings == null) throw new ArgumentException("settingsPath must identify an AddressableAssetSettings asset.");
            }
            if (!settingsType.IsInstanceOfType(settings)) throw new InvalidOperationException("The resolved Addressables settings object has an unexpected type.");
            var unitySettings = settings as UnityEngine.Object;
            settingsPath = unitySettings == null ? null : AssetDatabase.GetAssetPath(unitySettings);
            if (string.IsNullOrWhiteSpace(settingsPath)) throw new InvalidOperationException("The resolved Addressables settings object is not a persistent asset under this project.");
            settingsPath = NormalizeExistingAssetPath(settingsPath, "settingsPath");
            return settings;
        }

        private static List<object> EnumerateGroups(object settings)
        {
            var groups = GetMember(settings, "groups") as IEnumerable;
            if (groups == null) throw new InvalidOperationException("The installed Addressables package does not expose AddressableAssetSettings.groups.");
            var output = new List<object>();
            foreach (var group in groups) if (group != null) output.Add(group);
            return output;
        }

        private static AddressablesGroupInfo ToGroupInfo(object group)
        {
            return new AddressablesGroupInfo
            {
                name = ReadGroupName(group),
                guid = ReadString(group, "Guid"),
                isDefault = ReadBool(group, "Default"),
                readOnly = ReadBool(group, "ReadOnly"),
                entryCount = CountEnumerable(GetMember(group, "entries") as IEnumerable),
                schemaTypes = ReadSchemaTypes(group).Select(type => type.FullName ?? type.Name).OrderBy(name => name, StringComparer.Ordinal).ToList()
            };
        }

        private static object RequireTemplateGroup(object settings, string requestedName, out string resolvedName)
        {
            object group;
            if (string.IsNullOrWhiteSpace(requestedName))
            {
                group = EnumerateGroups(settings).FirstOrDefault(candidate => ReadBool(candidate, "Default"));
                if (group == null) throw new ArgumentException("templateGroupName is required because the configured Addressables settings have no default group.");
            }
            else
            {
                var groupName = RequireGroupName(requestedName, "templateGroupName");
                group = FindGroup(settings, groupName) ?? throw new ArgumentException("templateGroupName does not identify an existing Addressables group.");
            }
            resolvedName = ReadGroupName(group);
            return group;
        }

        private static object FindGroup(object settings, string groupName)
        {
            var method = settings.GetType().GetMethod("FindGroup", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(string) }, null);
            if (method == null) throw new InvalidOperationException("The installed Addressables package does not expose AddressableAssetSettings.FindGroup(string).");
            return method.Invoke(settings, new object[] { groupName });
        }

        private static object FindAssetEntry(object settings, string guid)
        {
            var method = settings.GetType().GetMethod("FindAssetEntry", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(string) }, null);
            if (method == null) throw new InvalidOperationException("The installed Addressables package does not expose AddressableAssetSettings.FindAssetEntry(string).");
            return method.Invoke(settings, new object[] { guid });
        }

        private static object CreateGroup(object settings, string groupName, bool setAsDefaultGroup, List<Type> schemaTypes)
        {
            var schemaType = RequireType(SchemaTypeName);
            var schemaListType = typeof(List<>).MakeGenericType(schemaType);
            var method = settings.GetType().GetMethod("CreateGroup", BindingFlags.Public | BindingFlags.Instance, null,
                new[] { typeof(string), typeof(bool), typeof(bool), typeof(bool), schemaListType, typeof(Type[]) }, null);
            if (method == null) throw new InvalidOperationException("The installed Addressables package does not expose the supported CreateGroup API.");
            var emptySchemas = Activator.CreateInstance(schemaListType);
            return method.Invoke(settings, new[] { (object)groupName, setAsDefaultGroup, false, true, emptySchemas, schemaTypes.ToArray() });
        }

        private static List<Type> ReadSchemaTypes(object group)
        {
            var types = GetMember(group, "SchemaTypes") as IEnumerable;
            if (types == null) throw new InvalidOperationException("The installed Addressables package does not expose AddressableAssetGroup.SchemaTypes.");
            var output = new List<Type>();
            foreach (var item in types)
            {
                var type = item as Type;
                if (type == null || !output.Contains(type)) continue;
                output.Add(type);
            }
            return output;
        }

        private static object CreateOrMoveEntry(object settings, string guid, object targetGroup)
        {
            var groupType = RequireType(GroupTypeName);
            var method = settings.GetType().GetMethod("CreateOrMoveEntry", BindingFlags.Public | BindingFlags.Instance, null,
                new[] { typeof(string), groupType, typeof(bool), typeof(bool) }, null);
            if (method == null) throw new InvalidOperationException("The installed Addressables package does not expose the supported CreateOrMoveEntry API.");
            return method.Invoke(settings, new[] { (object)guid, targetGroup, false, true });
        }

        private static void SetEntryAddress(object entry, string address)
        {
            var method = entry.GetType().GetMethod("SetAddress", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(string), typeof(bool) }, null);
            if (method == null) throw new InvalidOperationException("The installed Addressables package does not expose AddressableAssetEntry.SetAddress(string, bool).");
            method.Invoke(entry, new object[] { address, true });
        }

        private static void RemoveAssetEntry(object settings, string guid)
        {
            var method = settings.GetType().GetMethod("RemoveAssetEntry", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(string), typeof(bool) }, null);
            if (method == null) throw new InvalidOperationException("The installed Addressables package does not expose AddressableAssetSettings.RemoveAssetEntry(string, bool).");
            method.Invoke(settings, new object[] { guid, true });
        }

        private static MethodInfo ResolveBuildMethod()
        {
            var settingsType = RequireType(SettingsTypeName);
            var resultType = FindType(BuildResultTypeName);
            if (resultType != null)
            {
                var withResult = settingsType.GetMethod("BuildPlayerContent", BindingFlags.Public | BindingFlags.Static, null, new[] { resultType.MakeByRefType() }, null);
                if (withResult != null) return withResult;
            }
            var withoutResult = settingsType.GetMethod("BuildPlayerContent", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
            if (withoutResult == null) throw new InvalidOperationException("The installed Addressables package does not expose AddressableAssetSettings.BuildPlayerContent().");
            return withoutResult;
        }

        private sealed class AddressablesBuildOperation : IEditorWorkflowOperation
        {
            private readonly MethodInfo buildMethod;
            private readonly string settingsPath;
            private bool started;

            public AddressablesBuildOperation(MethodInfo buildMethod, string settingsPath)
            {
                this.buildMethod = buildMethod ?? throw new ArgumentNullException(nameof(buildMethod));
                this.settingsPath = settingsPath;
            }

            // BuildPlayerContent is synchronous. A queued job permits status inspection and
            // cancellation before Unity starts the build, but cannot interrupt the public API.
            public bool DrainWhenCancelled => false;

            public bool Tick(UnityMcpJob job)
            {
                if (job.IsCancellationRequested) return true;
                if (started) return true;
                started = true;
                job.status = "running";
                try
                {
                    object[] arguments = buildMethod.GetParameters().Length == 0 ? null : new object[] { null };
                    buildMethod.Invoke(null, arguments);
                    var result = arguments == null ? null : arguments[0];
                    var error = result == null ? null : ReadString(result, "Error");
                    if (!string.IsNullOrWhiteSpace(error))
                    {
                        EditorWorkflowJobRunner.Fail(job, "Addressables player-content build failed: " + SanitizeBuildError(error));
                        return true;
                    }
                    EditorWorkflowJobRunner.Succeed(job, new
                    {
                        status = "succeeded",
                        settingsPath,
                        outputPath = result == null ? null : ReadString(result, "OutputPath"),
                        contentStateFilePath = result == null ? null : ReadString(result, "ContentStateFilePath"),
                        resultReported = result != null
                    });
                }
                catch (TargetInvocationException exception)
                {
                    Debug.LogWarning("UnityMCP Addressables build failed (" + (exception.InnerException == null ? exception.GetType().Name : exception.InnerException.GetType().Name) + "). Details are available in the local Unity Console.");
                    EditorWorkflowJobRunner.Fail(job, "Addressables player-content build failed. See the local Unity Console for details.");
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("UnityMCP Addressables build operation failed (" + exception.GetType().Name + "). Details are available in the local Unity Console.");
                    EditorWorkflowJobRunner.Fail(job, "Addressables player-content build failed. See the local Unity Console for details.");
                }
                return true;
            }
        }

        private static string RequireGroupName(string value, string parameterName)
        {
            var normalized = (value ?? string.Empty).Trim();
            if (normalized.Length == 0 || normalized.Length > MaximumGroupNameLength || normalized.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || ContainsControlCharacter(normalized))
                throw new ArgumentException(parameterName + " must be a non-empty Addressables group name of at most " + MaximumGroupNameLength + " characters without invalid file-name or control characters.");
            return normalized;
        }

        private static string RequireAddress(string value)
        {
            if (value == null || value.Length == 0 || value.Length > MaximumAddressLength || value != value.Trim() || ContainsControlCharacter(value))
                throw new ArgumentException("address must contain 1 to " + MaximumAddressLength + " non-control characters and may not have leading/trailing whitespace.");
            return value;
        }

        private static string NormalizeExistingAssetPath(string value, string parameterName)
        {
            var normalized = (value ?? string.Empty).Replace('\\', '/').Trim();
            if (normalized.Length == 0 || Path.IsPathRooted(normalized) || normalized.IndexOf(':') >= 0 || !normalized.StartsWith("Assets/", StringComparison.Ordinal) || normalized.Contains("//"))
                throw new ArgumentException(parameterName + " must be a normalized existing path under Assets/.");
            var segments = normalized.Split('/');
            if (segments.Any(segment => string.IsNullOrEmpty(segment) || segment == "." || segment == ".."))
                throw new ArgumentException(parameterName + " must not contain empty, '.' or '..' path segments.");
            var fullPath = Path.GetFullPath(Path.Combine(ProjectRoot, normalized));
            if (!IsUnderRoot(fullPath, AssetsRoot)) throw new ArgumentException(parameterName + " must remain contained under this project's Assets directory.");
            var parent = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent)) throw new ArgumentException(parameterName + " must have an existing parent directory under Assets/.");
            EnsureNoReparsePoint(AssetsRoot, parent);
            return normalized;
        }

        private static bool IsResourcesPath(string assetPath)
        {
            return assetPath.StartsWith("Assets/Resources/", StringComparison.OrdinalIgnoreCase)
                || assetPath.IndexOf("/Resources/", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static int CountEnumerable(IEnumerable source)
        {
            if (source == null) return 0;
            var count = 0;
            foreach (var ignored in source)
            {
                count++;
                if (count == int.MaxValue) break;
            }
            return count;
        }

        private static string ReadGroupName(object group)
        {
            var name = ReadString(group, "Name");
            if (!string.IsNullOrWhiteSpace(name)) return name;
            return group is UnityEngine.Object unityObject ? unityObject.name : null;
        }

        private static string ReadString(object target, string memberName)
        {
            var value = GetMember(target, memberName);
            return value == null ? null : value.ToString();
        }

        private static bool ReadBool(object target, string memberName)
        {
            var value = GetMember(target, memberName);
            return value is bool boolean && boolean;
        }

        private static object GetMember(object target, string memberName)
        {
            if (target == null) return null;
            var type = target.GetType();
            var property = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.Instance);
            if (property != null && property.GetIndexParameters().Length == 0) return property.GetValue(target, null);
            var field = type.GetField(memberName, BindingFlags.Public | BindingFlags.Instance);
            return field == null ? null : field.GetValue(target);
        }

        private static bool SameUnityObject(object left, object right)
        {
            if (ReferenceEquals(left, right)) return true;
            var leftObject = left as UnityEngine.Object;
            var rightObject = right as UnityEngine.Object;
            return leftObject != null && rightObject != null && leftObject == rightObject;
        }

        private static Type RequireType(string fullName)
        {
            return FindType(fullName) ?? throw new InvalidOperationException("Required optional Unity package type is unavailable: " + fullName + ".");
        }

        private static Type FindType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var type = assembly.GetType(fullName, false);
                    if (type != null) return type;
                }
                catch { }
            }
            return null;
        }

        private static bool ContainsControlCharacter(string value)
        {
            return value.Any(character => char.IsControl(character));
        }

        private static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        private static string AssetsRoot => Path.GetFullPath(Application.dataPath);

        private static bool IsUnderRoot(string candidate, string root)
        {
            var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return Path.GetFullPath(candidate).StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
        }

        private static void EnsureNoReparsePoint(string root, string target)
        {
            var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var normalizedTarget = Path.GetFullPath(target);
            if (!IsUnderRoot(normalizedTarget, normalizedRoot) && !string.Equals(normalizedTarget, normalizedRoot, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("The requested path escapes the Assets directory.");
            var relative = normalizedTarget.Substring(normalizedRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var current = normalizedRoot;
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("The Assets directory may not be a reparse point.");
            foreach (var segment in relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, segment);
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new ArgumentException("The requested path traverses a reparse point.");
            }
        }

        private static string SanitizeBuildError(string value)
        {
            var compact = new string((value ?? string.Empty).Where(character => !char.IsControl(character) || character == '\n' || character == '\r' || character == '\t').ToArray()).Trim();
            return compact.Length <= MaximumBuildErrorLength ? compact : compact.Substring(0, MaximumBuildErrorLength) + "…";
        }
    }
}
