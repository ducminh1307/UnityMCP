using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DucMinh.UnityMcp.Editor
{
    [Serializable]
    public sealed class UnityMcpReflectionMethodRule
    {
        [Tooltip("Exact public instance method name.")]
        public string methodName;
        [Tooltip("Assembly-qualified or fully-qualified parameter type names, in order. Only simple UnityMCP value types are supported.")]
        public List<string> parameterTypeNames = new List<string>();
    }

    [Serializable]
    public sealed class UnityMcpReflectionTypeRule
    {
        [Tooltip("Exact fully-qualified or assembly-qualified UnityEngine.Object-derived type name.")]
        public string typeName;
        [Tooltip("Exact public instance field/property names that may be read.")]
        public List<string> readableMembers = new List<string>();
        [Tooltip("Exact public instance field/property names that may be written.")]
        public List<string> writableMembers = new List<string>();
        [Tooltip("Exact public instance methods that may be invoked.")]
        public List<UnityMcpReflectionMethodRule> callableMethods = new List<UnityMcpReflectionMethodRule>();
    }

    [Serializable] public sealed class EditorMenuExecuteInput { public string allowlistPath; public string menuItem; public bool apply; }
    [Serializable] public sealed class EditorMenuExecuteOutput { public bool dryRun; public bool executed; public string menuItem; public string allowlistPath; public string summary; }

    [Serializable] public sealed class ObjectGetInput { public string allowlistPath; public string type; public int? instanceId; public string assetPath; public string member; }
    [Serializable] public sealed class ObjectSetInput { public string allowlistPath; public string type; public int? instanceId; public string assetPath; public string member; public string valueJson; public bool apply; }
    [Serializable] public sealed class ObjectMemberOutput { public int instanceId; public string assetPath; public string type; public string member; public string memberType; public string valueJson; }
    [Serializable] public sealed class ObjectSetOutput { public bool dryRun; public bool changed; public int instanceId; public string type; public string member; public string summary; public bool rollbackSupported; }

    [Serializable] public sealed class MethodFindInput { public string allowlistPath; public string type; }
    [Serializable] public sealed class MethodInfoOutput { public string name; public string returnType; public List<string> parameterTypes = new List<string>(); }
    [Serializable] public sealed class MethodFindOutput { public string type; public List<MethodInfoOutput> methods = new List<MethodInfoOutput>(); }
    [Serializable] public sealed class MethodCallInput { public string allowlistPath; public string type; public int? instanceId; public string assetPath; public string method; public List<string> argumentsJson = new List<string>(); public bool apply; }
    [Serializable] public sealed class MethodCallOutput { public bool dryRun; public bool invoked; public int instanceId; public string type; public string method; public string returnType; public string returnJson; public string summary; }

    [Serializable] public sealed class CustomToolListItem { public string name; public string title; public string description; public string category; public string safety; public List<string> scopes = new List<string>(); public bool enabled; public bool supportsDryRun; public bool supportsCancel; public bool returnsJob; public int timeoutMs; public string source; public string schemaHash; }
    [Serializable] public sealed class CustomToolListOutput { public string registryRevision; public List<CustomToolListItem> tools = new List<CustomToolListItem>(); }
    [Serializable] public sealed class CustomToolReloadInput { public bool apply; }
    [Serializable] public sealed class CustomToolReloadOutput { public bool dryRun; public string revisionBefore; public string revisionAfter; public int toolCount; public int customToolCount; public string summary; }

    [Serializable] public sealed class CheckpointCreateInput { public string scenePath; public string checkpointId; public bool apply; }
    [Serializable] public sealed class CheckpointListInput { public int limit = 100; }
    [Serializable] public sealed class CheckpointDiffInput { public string checkpointId; public string scenePath; public int maxLines = 50; }
    [Serializable] public sealed class CheckpointRestoreInput { public string checkpointId; public bool apply; }
    [Serializable] public sealed class CheckpointInfo { public string checkpointId; public string scenePath; public string createdUtc; public string sourceSha256; public long sourceBytes; public bool snapshotExists; }
    [Serializable] public sealed class CheckpointCreateOutput { public bool dryRun; public bool created; public CheckpointInfo checkpoint; public string summary; public bool rollbackSupported; public List<ChangeJournalEntry> journal = new List<ChangeJournalEntry>(); }
    [Serializable] public sealed class CheckpointListOutput { public List<CheckpointInfo> checkpoints = new List<CheckpointInfo>(); public bool truncated; }
    [Serializable] public sealed class CheckpointLineDiff { public int line; public string checkpoint; public string current; }
    [Serializable] public sealed class CheckpointDiffOutput { public string checkpointId; public string scenePath; public bool sameContent; public string checkpointSha256; public string currentSha256; public long checkpointBytes; public long currentBytes; public string comparison = "bounded-line-by-line"; public List<CheckpointLineDiff> lines = new List<CheckpointLineDiff>(); public bool truncated; }
    [Serializable] public sealed class CheckpointRestoreOutput { public bool dryRun; public bool restored; public string checkpointId; public string scenePath; public string backupPath; public string summary; public bool rollbackSupported; public List<ChangeJournalEntry> journal = new List<ChangeJournalEntry>(); }

    /// <summary>
    /// Explicitly-allowlisted Editor automation and reflection. These tools are
    /// intentionally disabled by default and never accept arbitrary code,
    /// arbitrary member access, or arbitrary menu paths.
    /// </summary>
    public static class EditorAutomationReflectionTools
    {
        private const int MaxSerializedValueCharacters = 32768;
        private const long MaxCheckpointCompareBytes = 32L * 1024L * 1024L;
        private static readonly Regex CheckpointIdPattern = new Regex("^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$", RegexOptions.Compiled);

        [UnityMcpTool("editor-menu-execute", Description = "Execute an exact Unity menu path listed in a local UnityMCP menu allowlist; dry-run unless apply is true.", Category = "editor", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Unsafe, SupportsDryRun = true)]
        public static EditorMenuExecuteOutput EditorMenuExecute(EditorMenuExecuteInput input, UnityMcpContext context)
        {
            var allowlist = LoadProjectAsset<UnityMcpMenuAllowlist>(input.allowlistPath, "menu allowlist");
            if (string.IsNullOrWhiteSpace(input.menuItem)) throw new ArgumentException("menuItem is required.");
            if (!(allowlist.allowedMenuItems ?? new List<string>()).Any(value => string.Equals(value, input.menuItem, StringComparison.Ordinal)))
                throw new ArgumentException("The menu item is not listed in the supplied local UnityMCP menu allowlist.");

            var output = new EditorMenuExecuteOutput
            {
                dryRun = context.DryRun,
                menuItem = input.menuItem,
                allowlistPath = NormalizeAssetPath(input.allowlistPath, ".asset"),
                summary = "Execute allowlisted menu item '" + input.menuItem + "'."
            };
            if (!context.DryRun)
            {
                output.executed = EditorApplication.ExecuteMenuItem(input.menuItem);
                if (!output.executed) throw new InvalidOperationException("Unity did not recognize the allowlisted menu item.");
            }
            return output;
        }

        [UnityMcpTool("object-get", Description = "Read one explicitly allowlisted public field or property from an allowlisted Unity object.", Category = "reflection", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.SafeRead)]
        public static ObjectMemberOutput ObjectGet(ObjectGetInput input)
        {
            var rule = RequireReflectionRule(input.allowlistPath, input.type);
            var target = RequireReflectionTarget(input.instanceId, input.assetPath, rule);
            var member = RequireAllowedMember(rule.rule, rule.type, input.member, false);
            var value = ReadMember(member, target);
            return new ObjectMemberOutput
            {
                instanceId = target.GetInstanceID(),
                assetPath = AssetDatabase.GetAssetPath(target),
                type = rule.type.FullName,
                member = member.Name,
                memberType = MemberType(member).FullName,
                valueJson = SerializeValue(value, MemberType(member))
            };
        }

        [UnityMcpTool("object-set", Description = "Set one explicitly allowlisted public field or property on an allowlisted Unity object; dry-run unless apply is true.", Category = "reflection", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Unsafe, SupportsDryRun = true)]
        public static ObjectSetOutput ObjectSet(ObjectSetInput input, UnityMcpContext context)
        {
            var rule = RequireReflectionRule(input.allowlistPath, input.type);
            var target = RequireReflectionTarget(input.instanceId, input.assetPath, rule);
            var member = RequireAllowedMember(rule.rule, rule.type, input.member, true);
            var memberType = MemberType(member);
            var value = ParseValue(input.valueJson, memberType);
            if (!context.DryRun)
            {
                Undo.RecordObject(target, "UnityMCP Set Allowlisted Object Member");
                WriteMember(member, target, value);
                MarkTargetDirty(target);
            }
            return new ObjectSetOutput
            {
                dryRun = context.DryRun,
                changed = !context.DryRun,
                instanceId = target.GetInstanceID(),
                type = rule.type.FullName,
                member = member.Name,
                summary = "Set allowlisted member '" + member.Name + "' on '" + target.name + "'.",
                // Unity Undo is a local Editor convenience, not a cross-call transaction.
                rollbackSupported = false
            };
        }

        [UnityMcpTool("method-find", Description = "List public instance methods explicitly permitted by a local UnityMCP reflection allowlist.", Category = "reflection", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.SafeRead)]
        public static MethodFindOutput MethodFind(MethodFindInput input)
        {
            var rule = RequireReflectionRule(input.allowlistPath, input.type);
            var output = new MethodFindOutput { type = rule.type.FullName };
            var signatures = new HashSet<string>(StringComparer.Ordinal);
            foreach (var methodRule in rule.rule.callableMethods ?? new List<UnityMcpReflectionMethodRule>())
            {
                var method = RequireAllowedMethod(rule.type, methodRule);
                var signature = MethodSignature(method);
                if (!signatures.Add(signature)) throw new ArgumentException("The reflection allowlist contains a duplicate allowed method signature: " + signature);
                output.methods.Add(new MethodInfoOutput
                {
                    name = method.Name,
                    returnType = FriendlyTypeName(method.ReturnType),
                    parameterTypes = method.GetParameters().Select(parameter => FriendlyTypeName(parameter.ParameterType)).ToList()
                });
            }
            output.methods = output.methods.OrderBy(value => value.name, StringComparer.Ordinal).ThenBy(value => string.Join(",", value.parameterTypes), StringComparer.Ordinal).ToList();
            return output;
        }

        [UnityMcpTool("method-call", Description = "Invoke one explicitly allowlisted public instance method with simple typed JSON arguments; dry-run unless apply is true.", Category = "reflection", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Unsafe, SupportsDryRun = true)]
        public static MethodCallOutput MethodCall(MethodCallInput input, UnityMcpContext context)
        {
            var rule = RequireReflectionRule(input.allowlistPath, input.type);
            var target = RequireReflectionTarget(input.instanceId, input.assetPath, rule);
            var methodRule = RequireUniqueAllowedMethodRule(rule.rule, input.method);
            var method = RequireAllowedMethod(rule.type, methodRule);
            var parameters = method.GetParameters();
            var jsonArguments = input.argumentsJson ?? new List<string>();
            if (parameters.Length != jsonArguments.Count)
                throw new ArgumentException("The number of arguments does not match the local allowlisted method signature.");
            var arguments = new object[parameters.Length];
            for (var index = 0; index < parameters.Length; index++) arguments[index] = ParseValue(jsonArguments[index], parameters[index].ParameterType);

            var output = new MethodCallOutput
            {
                dryRun = context.DryRun,
                instanceId = target.GetInstanceID(),
                type = rule.type.FullName,
                method = MethodSignature(method),
                returnType = FriendlyTypeName(method.ReturnType),
                summary = "Invoke allowlisted method '" + MethodSignature(method) + "' on '" + target.name + "'."
            };
            if (!context.DryRun)
            {
                Undo.RecordObject(target, "UnityMCP Invoke Allowlisted Method");
                var result = method.Invoke(target, arguments);
                output.invoked = true;
                output.returnJson = method.ReturnType == typeof(void) ? null : SerializeValue(result, method.ReturnType);
                MarkTargetDirty(target);
            }
            return output;
        }

        [UnityMcpTool("custom-tool-list", Description = "List project-defined custom UnityMCP tools from the live Editor registry.", Category = "custom", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.SafeRead)]
        public static CustomToolListOutput CustomToolList(EmptyInput input)
        {
            var registry = RequireRegistry();
            var output = new CustomToolListOutput { registryRevision = registry.RegistryRevision };
            foreach (var tool in registry.Tools.Where(value => !string.Equals(value.source, "builtin", StringComparison.Ordinal)).OrderBy(value => value.name, StringComparer.Ordinal))
            {
                output.tools.Add(new CustomToolListItem
                {
                    name = tool.name,
                    title = tool.title,
                    description = tool.description,
                    category = tool.category,
                    safety = tool.safety,
                    scopes = tool.scopes == null ? new List<string>() : tool.scopes.ToList(),
                    enabled = tool.enabled,
                    supportsDryRun = tool.supportsDryRun,
                    supportsCancel = tool.supportsCancel,
                    returnsJob = tool.returnsJob,
                    timeoutMs = tool.timeoutMs,
                    source = tool.source,
                    schemaHash = tool.schemaHash
                });
            }
            return output;
        }

        [UnityMcpTool("custom-tool-reload", Description = "Rescan built-in and project custom UnityMCP tools; dry-run unless apply is true.", Category = "custom", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Write, SupportsDryRun = true)]
        public static CustomToolReloadOutput CustomToolReload(CustomToolReloadInput input, UnityMcpContext context)
        {
            var registry = RequireRegistry();
            var before = registry.RegistryRevision;
            if (!context.DryRun) registry.Reload();
            var tools = registry.Tools;
            return new CustomToolReloadOutput
            {
                dryRun = context.DryRun,
                revisionBefore = before,
                revisionAfter = registry.RegistryRevision,
                toolCount = tools.Count,
                customToolCount = tools.Count(value => !string.Equals(value.source, "builtin", StringComparison.Ordinal)),
                summary = context.DryRun ? "Dry run: rescan the live UnityMCP registry." : "Rescanned the live UnityMCP registry."
            };
        }

        [UnityMcpTool("checkpoint-create", Description = "Create a bounded on-disk snapshot of an Assets/*.unity scene under Library/UnityMCP/checkpoints; dry-run unless apply is true.", Category = "automation", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Write, SupportsDryRun = true)]
        public static CheckpointCreateOutput CheckpointCreate(CheckpointCreateInput input, UnityMcpContext context)
        {
            var scenePath = RequireExistingSceneAsset(input.scenePath);
            RefuseDirtyLoadedScene(scenePath, "create a checkpoint");
            var id = string.IsNullOrWhiteSpace(input.checkpointId) ? NewCheckpointId(scenePath) : RequireCheckpointId(input.checkpointId);
            var manifestPath = CheckpointManifestPath(id);
            var snapshotPath = CheckpointSnapshotPath(id);
            if (File.Exists(manifestPath) || File.Exists(snapshotPath)) throw new InvalidOperationException("A checkpoint with this id already exists.");
            var sourcePath = AssetFullPath(scenePath);
            var manifest = new CheckpointManifest
            {
                id = id,
                scenePath = scenePath,
                createdUtc = DateTime.UtcNow.ToString("O"),
                sourceSha256 = HashFile(sourcePath),
                sourceBytes = new FileInfo(sourcePath).Length,
                snapshotFile = Path.GetFileName(snapshotPath)
            };
            if (!context.DryRun)
            {
                Directory.CreateDirectory(CheckpointDirectory);
                File.Copy(sourcePath, snapshotPath, false);
                File.WriteAllText(manifestPath, JsonConvert.SerializeObject(manifest, Formatting.Indented), new UTF8Encoding(false));
            }
            return new CheckpointCreateOutput
            {
                dryRun = context.DryRun,
                created = !context.DryRun,
                checkpoint = ToCheckpointInfo(manifest),
                summary = "Create checkpoint '" + id + "' for '" + scenePath + "'.",
                rollbackSupported = false,
                journal = new List<ChangeJournalEntry>
                {
                    new ChangeJournalEntry { operation = "checkpoint-scene", before = scenePath, after = "Library/UnityMCP/checkpoints/" + manifest.snapshotFile }
                }
            };
        }

        [UnityMcpTool("checkpoint-list", Description = "List valid UnityMCP scene checkpoints stored under Library/UnityMCP/checkpoints.", Category = "automation", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.SafeRead)]
        public static CheckpointListOutput CheckpointList(CheckpointListInput input)
        {
            var output = new CheckpointListOutput();
            if (!Directory.Exists(CheckpointDirectory)) return output;
            var limit = Math.Max(1, Math.Min(input.limit, 1000));
            foreach (var manifestPath in Directory.GetFiles(CheckpointDirectory, "*.json", SearchOption.TopDirectoryOnly).OrderByDescending(value => value, StringComparer.Ordinal))
            {
                if (output.checkpoints.Count >= limit) { output.truncated = true; break; }
                CheckpointManifest manifest;
                try { manifest = ReadCheckpointManifest(manifestPath); }
                catch { continue; }
                output.checkpoints.Add(ToCheckpointInfo(manifest));
            }
            return output;
        }

        [UnityMcpTool("checkpoint-diff", Description = "Compare a checkpoint with its bounded Assets/*.unity source scene using hashes and a bounded line-by-line sample.", Category = "automation", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.SafeRead)]
        public static CheckpointDiffOutput CheckpointDiff(CheckpointDiffInput input)
        {
            var manifest = LoadCheckpoint(input.checkpointId);
            var scenePath = string.IsNullOrWhiteSpace(input.scenePath) ? manifest.scenePath : RequireExistingSceneAsset(input.scenePath);
            if (!string.Equals(scenePath, manifest.scenePath, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("scenePath must match the scene stored in the checkpoint.");
            var currentPath = AssetFullPath(scenePath);
            var snapshotPath = CheckpointSnapshotPath(manifest);
            RequireComparableCheckpointFile(snapshotPath);
            RequireComparableCheckpointFile(currentPath);
            var output = new CheckpointDiffOutput
            {
                checkpointId = manifest.id,
                scenePath = scenePath,
                checkpointSha256 = HashFile(snapshotPath),
                currentSha256 = HashFile(currentPath),
                checkpointBytes = new FileInfo(snapshotPath).Length,
                currentBytes = new FileInfo(currentPath).Length
            };
            output.sameContent = string.Equals(output.checkpointSha256, output.currentSha256, StringComparison.Ordinal);
            if (output.sameContent) return output;
            var maxLines = Math.Max(1, Math.Min(input.maxLines, 200));
            AppendLineDifferences(snapshotPath, currentPath, maxLines, output);
            return output;
        }

        [UnityMcpTool("checkpoint-restore", Description = "Restore an unloaded, clean Assets/*.unity scene from a local UnityMCP checkpoint; dry-run unless apply is true.", Category = "automation", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Destructive, SupportsDryRun = true)]
        public static CheckpointRestoreOutput CheckpointRestore(CheckpointRestoreInput input, UnityMcpContext context)
        {
            var manifest = LoadCheckpoint(input.checkpointId);
            var scenePath = RequireExistingSceneAsset(manifest.scenePath);
            RefuseLoadedOrDirtyScene(scenePath, "restore a checkpoint");
            var snapshotPath = CheckpointSnapshotPath(manifest);
            if (!File.Exists(snapshotPath)) throw new FileNotFoundException("The checkpoint snapshot is missing.");
            var sourcePath = AssetFullPath(scenePath);
            var backupName = manifest.id + "-before-restore-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".unity";
            var backupPath = ContainedCheckpointPath(backupName);
            if (!context.DryRun)
            {
                Directory.CreateDirectory(CheckpointDirectory);
                File.Copy(sourcePath, backupPath, false);
                File.Copy(snapshotPath, sourcePath, true);
                AssetDatabase.ImportAsset(scenePath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            }
            return new CheckpointRestoreOutput
            {
                dryRun = context.DryRun,
                restored = !context.DryRun,
                checkpointId = manifest.id,
                scenePath = scenePath,
                backupPath = "Library/UnityMCP/checkpoints/" + Path.GetFileName(backupPath),
                summary = "Restore checkpoint '" + manifest.id + "' to unloaded scene '" + scenePath + "'.",
                rollbackSupported = false,
                journal = new List<ChangeJournalEntry>
                {
                    new ChangeJournalEntry { operation = "restore-scene-checkpoint", before = scenePath, after = "Library/UnityMCP/checkpoints/" + Path.GetFileName(snapshotPath) },
                    new ChangeJournalEntry { operation = "backup-before-restore", before = scenePath, after = "Library/UnityMCP/checkpoints/" + Path.GetFileName(backupPath) }
                }
            };
        }

        private sealed class ReflectionRuleResolution
        {
            public UnityMcpReflectionTypeRule rule;
            public Type type;
        }

        [Serializable]
        private sealed class CheckpointManifest
        {
            public string id;
            public string scenePath;
            public string createdUtc;
            public string sourceSha256;
            public long sourceBytes;
            public string snapshotFile;
        }

        [Serializable]
        private sealed class UnityObjectReference
        {
            public int instanceId;
            public string name;
            public string type;
            public string assetPath;
        }

        private static UnityMcpRegistry RequireRegistry()
        {
            return UnityMcpEditorBootstrap.Registry ?? throw new InvalidOperationException("The UnityMCP Editor registry is still starting. Retry after the Editor finishes loading.");
        }

        private static T LoadProjectAsset<T>(string assetPath, string purpose) where T : UnityEngine.Object
        {
            var normalized = NormalizeAssetPath(assetPath, ".asset");
            var asset = AssetDatabase.LoadAssetAtPath<T>(normalized);
            if (asset == null) throw new ArgumentException("The supplied " + purpose + " asset was not found or has the wrong type.");
            return asset;
        }

        private static ReflectionRuleResolution RequireReflectionRule(string allowlistPath, string requestedType)
        {
            if (string.IsNullOrWhiteSpace(requestedType)) throw new ArgumentException("type is required and must be explicitly allowlisted.");
            var allowlist = LoadProjectAsset<UnityMcpReflectionAllowlist>(allowlistPath, "reflection allowlist");
            var requested = ResolveType(requestedType);
            if (requested == null) throw new ArgumentException("type could not be resolved.");
            var matches = new List<ReflectionRuleResolution>();
            foreach (var rule in allowlist.types ?? new List<UnityMcpReflectionTypeRule>())
            {
                var type = ResolveType(rule?.typeName);
                if (type == null || type != requested) continue;
                ValidateReflectionTargetType(type);
                matches.Add(new ReflectionRuleResolution { rule = rule, type = type });
            }
            if (matches.Count == 0) throw new ArgumentException("The requested type is not in the supplied local UnityMCP reflection allowlist.");
            if (matches.Count > 1) throw new ArgumentException("The supplied local UnityMCP reflection allowlist contains duplicate type rules.");
            return matches[0];
        }

        private static void ValidateReflectionTargetType(Type type)
        {
            if (!typeof(UnityEngine.Object).IsAssignableFrom(type) || type.IsAbstract)
                throw new ArgumentException("Reflection allowlist types must be concrete UnityEngine.Object-derived types.");
            if (type == typeof(UnityEngine.Object) || type == typeof(GameObject) || type == typeof(Component) || type == typeof(Behaviour) || type == typeof(MonoBehaviour) || type == typeof(ScriptableObject))
                throw new ArgumentException("Broad Unity base types cannot be used in a UnityMCP reflection allowlist.");
            if (!string.IsNullOrEmpty(type.Namespace) && type.Namespace.StartsWith("UnityEditor", StringComparison.Ordinal))
                throw new ArgumentException("UnityEditor types cannot be used in a UnityMCP reflection allowlist.");
        }

        private static UnityEngine.Object RequireReflectionTarget(int? instanceId, string assetPath, ReflectionRuleResolution rule)
        {
            if (instanceId.HasValue == !string.IsNullOrWhiteSpace(assetPath))
                throw new ArgumentException("Provide exactly one of instanceId or assetPath.");
            UnityEngine.Object target;
            if (instanceId.HasValue)
            {
                target = EditorUtility.InstanceIDToObject(instanceId.Value);
                if (target == null) throw new ArgumentException("instanceId does not identify a live Unity object.");
            }
            else
            {
                var normalized = NormalizeAssetPath(assetPath);
                target = AssetDatabase.LoadMainAssetAtPath(normalized);
                if (target == null) throw new ArgumentException("assetPath does not identify a project asset.");
            }
            if (!rule.type.IsInstanceOfType(target))
                throw new ArgumentException("The target object does not match the explicitly allowlisted type.");
            return target;
        }

        private static MemberInfo RequireAllowedMember(UnityMcpReflectionTypeRule rule, Type targetType, string memberName, bool forWrite)
        {
            if (string.IsNullOrWhiteSpace(memberName)) throw new ArgumentException("member is required.");
            var names = forWrite ? rule.writableMembers : rule.readableMembers;
            if (!(names ?? new List<string>()).Any(value => string.Equals(value, memberName, StringComparison.Ordinal)))
                throw new ArgumentException("The member is not explicitly permitted by the supplied local reflection allowlist.");
            var field = targetType.GetField(memberName, BindingFlags.Instance | BindingFlags.Public);
            if (field != null)
            {
                if (forWrite && field.IsInitOnly) throw new ArgumentException("The allowlisted field is read-only.");
                EnsureSupportedValueType(field.FieldType, "member");
                return field;
            }
            var property = targetType.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public);
            if (property == null || property.GetIndexParameters().Length != 0 || !property.CanRead || (forWrite && !property.CanWrite))
                throw new ArgumentException("The allowlisted member must be a public readable " + (forWrite ? "writable " : string.Empty) + "instance field or property.");
            EnsureSupportedValueType(property.PropertyType, "member");
            return property;
        }

        private static object ReadMember(MemberInfo member, UnityEngine.Object target)
        {
            if (member is FieldInfo field) return field.GetValue(target);
            if (member is PropertyInfo property) return property.GetValue(target, null);
            throw new InvalidOperationException("Unsupported member type.");
        }

        private static void WriteMember(MemberInfo member, UnityEngine.Object target, object value)
        {
            if (member is FieldInfo field) { field.SetValue(target, value); return; }
            if (member is PropertyInfo property) { property.SetValue(target, value, null); return; }
            throw new InvalidOperationException("Unsupported member type.");
        }

        private static Type MemberType(MemberInfo member)
        {
            if (member is FieldInfo field) return field.FieldType;
            if (member is PropertyInfo property) return property.PropertyType;
            throw new InvalidOperationException("Unsupported member type.");
        }

        private static UnityMcpReflectionMethodRule RequireUniqueAllowedMethodRule(UnityMcpReflectionTypeRule rule, string methodName)
        {
            if (string.IsNullOrWhiteSpace(methodName)) throw new ArgumentException("method is required.");
            var matches = (rule.callableMethods ?? new List<UnityMcpReflectionMethodRule>()).Where(value => value != null && string.Equals(value.methodName, methodName, StringComparison.Ordinal)).ToList();
            if (matches.Count == 0) throw new ArgumentException("The method is not explicitly permitted by the supplied local reflection allowlist.");
            if (matches.Count > 1) throw new ArgumentException("The local reflection allowlist may only contain one callable overload per method name.");
            return matches[0];
        }

        private static MethodInfo RequireAllowedMethod(Type targetType, UnityMcpReflectionMethodRule rule)
        {
            if (rule == null || string.IsNullOrWhiteSpace(rule.methodName)) throw new ArgumentException("The reflection allowlist contains an invalid method rule.");
            var parameterTypes = (rule.parameterTypeNames ?? new List<string>()).Select(ResolveSafeParameterType).ToArray();
            var matches = targetType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Where(method => method.Name == rule.methodName && !method.IsSpecialName && !method.ContainsGenericParameters && !typeof(System.Threading.Tasks.Task).IsAssignableFrom(method.ReturnType))
                .Where(method => ParametersMatch(method.GetParameters(), parameterTypes))
                .ToArray();
            if (matches.Length != 1) throw new ArgumentException("The allowlisted method rule must resolve to exactly one public synchronous instance method.");
            var method = matches[0];
            foreach (var parameter in method.GetParameters())
            {
                if (parameter.IsOut || parameter.ParameterType.IsByRef) throw new ArgumentException("Allowlisted methods cannot use ref or out parameters.");
                EnsureSupportedValueType(parameter.ParameterType, "method parameter");
            }
            if (method.ReturnType != typeof(void)) EnsureSupportedValueType(method.ReturnType, "method return value");
            return method;
        }

        private static bool ParametersMatch(ParameterInfo[] parameters, Type[] expected)
        {
            if (parameters.Length != expected.Length) return false;
            for (var index = 0; index < parameters.Length; index++) if (parameters[index].ParameterType != expected[index]) return false;
            return true;
        }

        private static Type ResolveSafeParameterType(string typeName)
        {
            var type = ResolveType(typeName);
            if (type == null) throw new ArgumentException("A reflection allowlist method parameter type could not be resolved.");
            EnsureSupportedValueType(type, "method parameter");
            return type;
        }

        private static void EnsureSupportedValueType(Type type, string purpose)
        {
            if (!IsSupportedValueType(type))
                throw new ArgumentException("The " + purpose + " type '" + FriendlyTypeName(type) + "' is not supported by UnityMCP's allowlisted reflection contract.");
        }

        private static bool IsSupportedValueType(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            if (type.IsEnum || type == typeof(string) || type == typeof(bool) || type == typeof(byte) || type == typeof(sbyte) ||
                type == typeof(short) || type == typeof(ushort) || type == typeof(int) || type == typeof(uint) || type == typeof(long) ||
                type == typeof(ulong) || type == typeof(float) || type == typeof(double) || type == typeof(decimal) || type == typeof(Guid) ||
                type == typeof(Vector2) || type == typeof(Vector3) || type == typeof(Vector4) || type == typeof(Quaternion) || type == typeof(Color) || type == typeof(Rect)) return true;
            return typeof(UnityEngine.Object).IsAssignableFrom(type);
        }

        private static object ParseValue(string valueJson, Type declaredType)
        {
            JToken token;
            try { token = JToken.Parse(valueJson ?? "null"); }
            catch (JsonReaderException exception) { throw new ArgumentException("The JSON value is invalid.", exception); }
            var type = Nullable.GetUnderlyingType(declaredType) ?? declaredType;
            if (typeof(UnityEngine.Object).IsAssignableFrom(type))
            {
                if (token.Type == JTokenType.Null) return null;
                if (token.Type != JTokenType.String) throw new ArgumentException("Unity object values must use a project asset path string or null.");
                var path = NormalizeAssetPath(token.Value<string>());
                var asset = AssetDatabase.LoadAssetAtPath(path, type);
                if (asset == null) throw new ArgumentException("The referenced project asset does not match the allowlisted member or parameter type.");
                return asset;
            }
            if (token.Type == JTokenType.Null && declaredType.IsValueType && Nullable.GetUnderlyingType(declaredType) == null)
                throw new ArgumentException("A non-nullable value type cannot be assigned null.");
            try { return token.ToObject(declaredType, JsonSerializer.CreateDefault()); }
            catch (Exception exception) when (exception is JsonException || exception is InvalidCastException || exception is ArgumentException)
            {
                throw new ArgumentException("The JSON value does not match the explicitly allowlisted type '" + FriendlyTypeName(declaredType) + "'.", exception);
            }
        }

        private static string SerializeValue(object value, Type declaredType)
        {
            if (value == null) return "null";
            var type = Nullable.GetUnderlyingType(declaredType) ?? declaredType;
            string json;
            if (typeof(UnityEngine.Object).IsAssignableFrom(type))
            {
                var unityObject = value as UnityEngine.Object;
                json = JsonConvert.SerializeObject(unityObject == null ? null : new UnityObjectReference
                {
                    instanceId = unityObject.GetInstanceID(),
                    name = unityObject.name,
                    type = unityObject.GetType().FullName,
                    assetPath = AssetDatabase.GetAssetPath(unityObject)
                });
            }
            else json = JsonConvert.SerializeObject(value);
            if (json.Length > MaxSerializedValueCharacters) throw new InvalidOperationException("The allowlisted reflection value exceeds the 32 KiB result limit.");
            return json;
        }

        private static void MarkTargetDirty(UnityEngine.Object target)
        {
            EditorUtility.SetDirty(target);
            if (EditorUtility.IsPersistent(target))
            {
                AssetDatabase.SaveAssetIfDirty(target);
                return;
            }
            if (target is Component component && component.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(component.gameObject.scene);
            else if (target is GameObject gameObject && gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(gameObject.scene);
        }

        private static string MethodSignature(MethodInfo method)
        {
            return method.Name + "(" + string.Join(", ", method.GetParameters().Select(value => FriendlyTypeName(value.ParameterType)).ToArray()) + ")";
        }

        private static string FriendlyTypeName(Type type)
        {
            var nullable = Nullable.GetUnderlyingType(type);
            if (nullable != null) return FriendlyTypeName(nullable) + "?";
            return type.FullName ?? type.Name;
        }

        private static Type ResolveType(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            var direct = Type.GetType(name, false);
            if (direct != null) return direct;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type;
                try { type = assembly.GetType(name, false); }
                catch { continue; }
                if (type != null) return type;
            }
            return null;
        }

        private static string RequireExistingSceneAsset(string path)
        {
            var normalized = NormalizeAssetPath(path, ".unity");
            if (!File.Exists(AssetFullPath(normalized))) throw new ArgumentException("The scene asset was not found: " + normalized);
            return normalized;
        }

        private static void RefuseDirtyLoadedScene(string scenePath, string operation)
        {
            var scene = FindLoadedScene(scenePath);
            if (scene.IsValid() && scene.isDirty)
                throw new InvalidOperationException("Cannot " + operation + " while the target scene has unsaved changes. Save or close it first.");
        }

        private static void RefuseLoadedOrDirtyScene(string scenePath, string operation)
        {
            var scene = FindLoadedScene(scenePath);
            if (!scene.IsValid()) return;
            if (scene.isDirty) throw new InvalidOperationException("Cannot " + operation + " while the target scene is loaded and dirty. Save or close it first.");
            throw new InvalidOperationException("Cannot " + operation + " while the target scene is loaded. Close it first to avoid invalidating the Editor scene state.");
        }

        private static Scene FindLoadedScene(string scenePath)
        {
            for (var index = 0; index < SceneManager.sceneCount; index++)
            {
                var scene = SceneManager.GetSceneAt(index);
                if (scene.isLoaded && string.Equals(scene.path, scenePath, StringComparison.OrdinalIgnoreCase)) return scene;
            }
            return default(Scene);
        }

        private static string RequireCheckpointId(string checkpointId)
        {
            if (!CheckpointIdPattern.IsMatch(checkpointId ?? string.Empty))
                throw new ArgumentException("checkpointId must contain 1-64 letters, numbers, underscores, or hyphens and may not start with punctuation.");
            return checkpointId;
        }

        private static string NewCheckpointId(string scenePath)
        {
            var baseName = Path.GetFileNameWithoutExtension(scenePath);
            var safe = new string(baseName.Select(character => (character >= 'A' && character <= 'Z') || (character >= 'a' && character <= 'z') || (character >= '0' && character <= '9') ? character : '-').ToArray()).Trim('-');
            if (string.IsNullOrEmpty(safe)) safe = "scene";
            var id = safe + "-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            return id.Substring(0, Math.Min(64, id.Length));
        }

        private static string ProjectRoot
        {
            get
            {
                var root = Directory.GetParent(Application.dataPath)?.FullName;
                if (string.IsNullOrEmpty(root)) throw new InvalidOperationException("Could not determine the Unity project root.");
                return Path.GetFullPath(root);
            }
        }

        private static string CheckpointDirectory => Path.Combine(ProjectRoot, "Library", "UnityMCP", "checkpoints");

        private static string CheckpointManifestPath(string id) => ContainedCheckpointPath(RequireCheckpointId(id) + ".json");
        private static string CheckpointSnapshotPath(string id) => ContainedCheckpointPath(RequireCheckpointId(id) + ".unity");
        private static string CheckpointSnapshotPath(CheckpointManifest manifest)
        {
            ValidateCheckpointManifest(manifest);
            return ContainedCheckpointPath(manifest.snapshotFile);
        }

        private static string ContainedCheckpointPath(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName) || !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal))
                throw new ArgumentException("Checkpoint storage must use a simple file name.");
            var directory = Path.GetFullPath(CheckpointDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var path = Path.GetFullPath(Path.Combine(directory, fileName));
            if (!path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Checkpoint path escapes local UnityMCP storage.");
            return path;
        }

        private static CheckpointManifest LoadCheckpoint(string checkpointId)
        {
            var path = CheckpointManifestPath(RequireCheckpointId(checkpointId));
            if (!File.Exists(path)) throw new ArgumentException("The requested checkpoint was not found.");
            return ReadCheckpointManifest(path);
        }

        private static CheckpointManifest ReadCheckpointManifest(string path)
        {
            var manifest = JsonConvert.DeserializeObject<CheckpointManifest>(File.ReadAllText(path));
            ValidateCheckpointManifest(manifest);
            return manifest;
        }

        private static void ValidateCheckpointManifest(CheckpointManifest manifest)
        {
            if (manifest == null) throw new ArgumentException("Checkpoint metadata is invalid.");
            RequireCheckpointId(manifest.id);
            NormalizeAssetPath(manifest.scenePath, ".unity");
            if (string.IsNullOrWhiteSpace(manifest.snapshotFile) || !string.Equals(manifest.snapshotFile, manifest.id + ".unity", StringComparison.Ordinal))
                throw new ArgumentException("Checkpoint metadata references an invalid snapshot file.");
            if (manifest.sourceBytes < 0 || string.IsNullOrWhiteSpace(manifest.sourceSha256)) throw new ArgumentException("Checkpoint metadata is incomplete.");
        }

        private static CheckpointInfo ToCheckpointInfo(CheckpointManifest manifest)
        {
            return new CheckpointInfo
            {
                checkpointId = manifest.id,
                scenePath = manifest.scenePath,
                createdUtc = manifest.createdUtc,
                sourceSha256 = manifest.sourceSha256,
                sourceBytes = manifest.sourceBytes,
                snapshotExists = File.Exists(CheckpointSnapshotPath(manifest))
            };
        }

        private static void RequireComparableCheckpointFile(string path)
        {
            var info = new FileInfo(path);
            if (!info.Exists) throw new FileNotFoundException("A checkpoint comparison file is missing.");
            if (info.Length > MaxCheckpointCompareBytes) throw new InvalidOperationException("Checkpoint comparisons are limited to 32 MiB scene files.");
        }

        private static void AppendLineDifferences(string checkpointPath, string currentPath, int maxLines, CheckpointDiffOutput output)
        {
            var checkpointLines = File.ReadAllLines(checkpointPath, Encoding.UTF8);
            var currentLines = File.ReadAllLines(currentPath, Encoding.UTF8);
            var count = Math.Max(checkpointLines.Length, currentLines.Length);
            for (var index = 0; index < count; index++)
            {
                var checkpoint = index < checkpointLines.Length ? checkpointLines[index] : null;
                var current = index < currentLines.Length ? currentLines[index] : null;
                if (string.Equals(checkpoint, current, StringComparison.Ordinal)) continue;
                if (output.lines.Count >= maxLines) { output.truncated = true; break; }
                output.lines.Add(new CheckpointLineDiff { line = index + 1, checkpoint = ClipLine(checkpoint), current = ClipLine(current) });
            }
        }

        private static string ClipLine(string value)
        {
            if (value == null) return null;
            return value.Length <= 512 ? value : value.Substring(0, 512) + "...";
        }

        private static string HashFile(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
                return string.Concat(sha.ComputeHash(stream).Select(value => value.ToString("x2")).ToArray());
        }

        private static string NormalizeAssetPath(string path, string requiredExtension = null)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A project-relative Assets path is required.");
            var normalized = path.Replace('\\', '/');
            if (!(normalized == "Assets" || normalized.StartsWith("Assets/", StringComparison.Ordinal)) || normalized.Contains("..") || Path.IsPathRooted(normalized))
                throw new ArgumentException("Path must be project-relative under Assets and may not contain '..'.");
            if (normalized.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Unity .meta files are not valid tool targets.");
            if (requiredExtension != null && !normalized.EndsWith(requiredExtension, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Path must end with " + requiredExtension + ".");
            var assetsRoot = Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullPath = AssetFullPathUnchecked(normalized);
            if (!fullPath.StartsWith(assetsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Path escapes the project's Assets directory.");
            return normalized;
        }

        private static string AssetFullPath(string assetPath) => AssetFullPathUnchecked(NormalizeAssetPath(assetPath));

        private static string AssetFullPathUnchecked(string assetPath)
        {
            return Path.GetFullPath(Path.Combine(ProjectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar)));
        }
    }
}
