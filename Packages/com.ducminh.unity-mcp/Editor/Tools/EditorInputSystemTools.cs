#if DM_INPUTSYSTEM_PRESENT
#pragma warning disable UAC0005 // Editor tool reflection scans currently loaded Input System types.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Editor.Authoring;
using UnityEditor;
using UnityEngine;

namespace DucMinh.UnityMcp.Editor
{
    [Serializable]
    public sealed class InputBindingInfo
    {
        public string name;
        public string id;
        public string path;
        public string action;
        public string groups;
        public string interactions;
        public string processors;
        public bool isComposite;
        public bool isPartOfComposite;
    }

    [Serializable]
    public sealed class InputActionInfo
    {
        public string name;
        public string id;
        public string type;
        public string expectedControlType;
        public string processors;
        public string interactions;
        public List<InputBindingInfo> bindings = new List<InputBindingInfo>();
        public bool bindingsTruncated;
    }

    [Serializable]
    public sealed class InputActionMapInfo
    {
        public string name;
        public string id;
        public int bindingCount;
        public List<InputActionInfo> actions = new List<InputActionInfo>();
    }

    [Serializable]
    public sealed class InputDeviceRequirementInfo
    {
        public string controlPath;
        public bool isOptional;
        public bool isOr;
    }

    [Serializable]
    public sealed class InputControlSchemeInfo
    {
        public string name;
        public string bindingGroup;
        public List<InputDeviceRequirementInfo> deviceRequirements = new List<InputDeviceRequirementInfo>();
    }

    [Serializable]
    public sealed class InputActionsGetOutput
    {
        public string path;
        public string name;
        public string id;
        public List<InputActionMapInfo> maps = new List<InputActionMapInfo>();
        public List<InputControlSchemeInfo> controlSchemes = new List<InputControlSchemeInfo>();
        public bool truncated;
    }

    [Serializable]
    public sealed class InputActionBindingCreateInput
    {
        /// <summary>Input System control path, such as &lt;Keyboard&gt;/space.</summary>
        public string path;
        public string interactions;
        public string processors;
        public string groups;
    }

    [Serializable]
    public sealed class InputActionCreateOutput
    {
        public bool dryRun;
        public bool changed;
        public string path;
        public string map;
        public string action;
        public string actionId;
        public int bindingCount;
        public bool rollbackSupported;
        public List<ChangeJournalEntry> journal = new List<ChangeJournalEntry>();
    }

    /// <summary>
    /// Input Actions asset inspection and bounded action creation using reflection inside
    /// DM_INPUTSYSTEM_PRESENT so com.unity.inputsystem requires no extra assembly reference.
    /// </summary>
    public static class EditorInputSystemTools
    {
        private const string Group = "input_actions";
        private const int MaxActions = 2048;
        private const int MaxBindingsPerAction = 128;
        private const int MaxBindingPathLength = 512;

        [CliGroupCommand(
            Group,
            "Read action maps, actions, bindings, and control schemes from an Input Actions asset.",
            Name = "get",
            Effect = CommandEffect.ReadOnly,
            Tags = new[] { "input" })]
        internal static ExitCode Get(
            CommandContext cmd,
            [CliArg("path", "Project-relative path to an existing .inputactions asset under Assets/.", Required = true)] string path,
            [CliArg("max-actions", "Maximum number of actions to return (1-2048).")] int maxActions = 512)
        {
            return Execute(cmd, () => GetCore(path, maxActions));
        }

        [CliGroupCommand(
            Group,
            "Add a typed action with simple bindings to an existing Input Actions map.",
            Name = "create_action",
            Effect = CommandEffect.Additive,
            Tags = new[] { "input" })]
        internal static ExitCode CreateAction(
            CommandContext cmd,
            [CliArg("path", "Project-relative path to an existing .inputactions asset under Assets/.", Required = true)] string path,
            [CliArg("map", "Name of an existing InputActionMap in the asset.", Required = true)] string map,
            [CliArg("action", "Name of the new InputAction to create.", Required = true)] string action,
            [CliArg("action-type", "Action type: button, value, or pass-through.")] string actionType = "button",
            [CliArg("expected-control-type", "Optional expected control type (e.g. Button, Vector2).")] string expectedControlType = null,
            [CliArg("interactions", "Optional action interactions string.")] string interactions = null,
            [CliArg("processors", "Optional action processors string.")] string processors = null,
            [CliArg("bindings-json", "Optional JSON array of {path, interactions, processors, groups} binding objects.")] string bindingsJson = null,
            [CliArg("dry-run", "If true, validate inputs and preview without modifying the .inputactions asset.")] bool dryRun = false)
        {
            return Execute(cmd, () => CreateActionCore(
                path,
                map,
                action,
                actionType,
                expectedControlType,
                interactions,
                processors,
                ParseBindingsJson(bindingsJson),
                dryRun));
        }

        internal static InputActionsGetOutput GetCore(string path, int maxActions = 512)
        {
            var limit = Math.Max(1, Math.Min(maxActions, MaxActions));
            var asset = RequireInputActionsAsset(path, out var normalizedPath);
            var output = new InputActionsGetOutput
            {
                path = normalizedPath,
                name = ((UnityEngine.Object)asset).name,
                id = ReadText(asset, "id")
            };

            var emittedActions = 0;
            foreach (var map in Enumerate(ReadRequiredMember(asset, "actionMaps")))
            {
                var mapInfo = new InputActionMapInfo
                {
                    name = ReadText(map, "name"),
                    id = ReadText(map, "id"),
                    bindingCount = Enumerate(ReadOptionalMember(map, "bindings")).Count
                };
                output.maps.Add(mapInfo);
                foreach (var action in Enumerate(ReadRequiredMember(map, "actions")))
                {
                    if (emittedActions >= limit) { output.truncated = true; break; }
                    mapInfo.actions.Add(ToActionInfo(action, output));
                    emittedActions++;
                }

                if (output.truncated) break;
            }

            foreach (var scheme in Enumerate(ReadOptionalMember(asset, "controlSchemes")))
                output.controlSchemes.Add(ToControlSchemeInfo(scheme));
            return output;
        }

        internal static InputActionCreateOutput CreateActionCore(
            string path,
            string map,
            string action,
            string actionType = "button",
            string expectedControlType = null,
            string interactions = null,
            string processors = null,
            List<InputActionBindingCreateInput> bindings = null,
            bool dryRun = false)
        {
            ValidateName(map, "map");
            ValidateName(action, "action");
            ValidateOptionalText(expectedControlType, "expected-control-type");
            ValidateOptionalText(interactions, "interactions");
            ValidateOptionalText(processors, "processors");
            var validatedBindings = ValidateBindings(bindings);
            var asset = RequireInputActionsAsset(path, out var normalizedPath);
            var targetMap = RequireMap(asset, map);
            EnsureActionsAreDisabled(asset);
            if (Enumerate(ReadRequiredMember(targetMap, "actions")).Any(existing => string.Equals(ReadText(existing, "name"), action, StringComparison.Ordinal)))
                throw new InvalidOperationException("The Input Actions map already contains an action named '" + action + "'.");

            var inputActionType = RequireType("UnityEngine.InputSystem.InputActionType");
            var parsedActionType = ParseActionType(inputActionType, actionType);
            var addAction = RequireAddAction(targetMap, inputActionType);
            var actionTypeObject = addAction.ReturnType;
            var addBinding = RequireAddBinding(actionTypeObject);
            var removeAction = FindRemoveAction(targetMap, actionTypeObject);

            var result = new InputActionCreateOutput
            {
                dryRun = dryRun,
                changed = !dryRun,
                path = normalizedPath,
                map = map,
                action = action,
                bindingCount = validatedBindings.Count,
                rollbackSupported = !dryRun,
                journal = new List<ChangeJournalEntry>
                {
                    new ChangeJournalEntry { operation = "add-input-action", after = normalizedPath + "#" + map + "/" + action }
                }
            };
            if (dryRun) return result;

            object createdAction = null;
            using (new AuthoringUndoScope("UnityMCP Add Input Action"))
            {
                try
                {
                    Undo.RecordObject((UnityEngine.Object)asset, "UnityMCP Add Input Action");
                    createdAction = addAction.Invoke(targetMap, new object[]
                    {
                        action,
                        parsedActionType,
                        null,
                        NormalizeOptional(interactions),
                        NormalizeOptional(processors),
                        NormalizeOptional(expectedControlType)
                    });
                    foreach (var binding in validatedBindings)
                        addBinding.Invoke(createdAction, new object[]
                        {
                            binding.path,
                            NormalizeOptional(binding.interactions),
                            NormalizeOptional(binding.processors),
                            NormalizeOptional(binding.groups)
                        });
                    EditorUtility.SetDirty((UnityEngine.Object)asset);
                    AssetDatabase.SaveAssetIfDirty((UnityEngine.Object)asset);
                    result.actionId = ReadText(createdAction, "id");
                    return result;
                }
                catch (TargetInvocationException exception)
                {
                    TryRemoveAction(targetMap, removeAction, createdAction);
                    throw new InvalidOperationException("Unity Input System rejected the requested action or binding: " + (exception.InnerException?.Message ?? exception.Message), exception.InnerException ?? exception);
                }
                catch
                {
                    TryRemoveAction(targetMap, removeAction, createdAction);
                    throw;
                }
            }
        }

        internal static List<InputActionBindingCreateInput> ParseBindingsJson(string bindingsJson)
        {
            if (string.IsNullOrWhiteSpace(bindingsJson))
                return new List<InputActionBindingCreateInput>();

            JArray array;
            try
            {
                if (!(JToken.Parse(bindingsJson) is JArray parsed))
                    throw new ArgumentException("bindings-json must be a JSON array of binding objects.");
                array = parsed;
            }
            catch (ArgumentException) { throw; }
            catch (Exception ex)
            {
                throw new ArgumentException("bindings-json is not valid JSON: " + ex.Message, ex);
            }

            var result = new List<InputActionBindingCreateInput>(array.Count);
            foreach (var item in array)
            {
                if (!(item is JObject obj))
                    throw new ArgumentException("bindings-json entries must be JSON objects.");
                result.Add(new InputActionBindingCreateInput
                {
                    path = ReadJsonString(obj, "path"),
                    interactions = ReadJsonString(obj, "interactions"),
                    processors = ReadJsonString(obj, "processors"),
                    groups = ReadJsonString(obj, "groups")
                });
            }

            return result;
        }

        private static ExitCode Execute(CommandContext cmd, Func<object> action)
        {
            try
            {
                cmd.Result.Write(action());
                return ExitCode.Success;
            }
            catch (TargetInvocationException ex)
            {
                var inner = ex.InnerException ?? ex;
                cmd.Log.Error(inner.Message);
                return inner is ArgumentException ? ExitCode.InvalidUsage : ExitCode.Failed;
            }
            catch (ArgumentException ex)
            {
                cmd.Log.Error(ex.Message);
                return ExitCode.InvalidUsage;
            }
            catch (Exception ex)
            {
                cmd.Log.Error(ex.Message);
                return ExitCode.Failed;
            }
        }

        private static string ReadJsonString(JObject obj, string key)
        {
            var token = obj[key];
            return token == null || token.Type == JTokenType.Null ? null : token.Value<string>();
        }

        private static InputActionInfo ToActionInfo(object action, InputActionsGetOutput output)
        {
            var result = new InputActionInfo
            {
                name = ReadText(action, "name"),
                id = ReadText(action, "id"),
                type = ReadText(action, "type"),
                expectedControlType = ReadText(action, "expectedControlType"),
                processors = ReadText(action, "processors"),
                interactions = ReadText(action, "interactions")
            };
            foreach (var binding in Enumerate(ReadOptionalMember(action, "bindings")))
            {
                if (result.bindings.Count >= MaxBindingsPerAction)
                {
                    result.bindingsTruncated = true;
                    output.truncated = true;
                    break;
                }

                result.bindings.Add(new InputBindingInfo
                {
                    name = ReadText(binding, "name"),
                    id = ReadText(binding, "id"),
                    path = ReadText(binding, "path"),
                    action = ReadText(binding, "action"),
                    groups = ReadText(binding, "groups"),
                    interactions = ReadText(binding, "interactions"),
                    processors = ReadText(binding, "processors"),
                    isComposite = ReadBool(binding, "isComposite"),
                    isPartOfComposite = ReadBool(binding, "isPartOfComposite")
                });
            }

            return result;
        }

        private static InputControlSchemeInfo ToControlSchemeInfo(object scheme)
        {
            var result = new InputControlSchemeInfo
            {
                name = ReadText(scheme, "name"),
                bindingGroup = ReadText(scheme, "bindingGroup")
            };
            foreach (var requirement in Enumerate(ReadOptionalMember(scheme, "deviceRequirements")))
                result.deviceRequirements.Add(new InputDeviceRequirementInfo
                {
                    controlPath = ReadText(requirement, "controlPath"),
                    isOptional = ReadBool(requirement, "isOptional"),
                    isOr = ReadBool(requirement, "isOR") || ReadBool(requirement, "isOr")
                });
            return result;
        }

        private static object RequireInputActionsAsset(string path, out string normalizedPath)
        {
            normalizedPath = NormalizeInputActionsPath(path);
            var type = RequireType("UnityEngine.InputSystem.InputActionAsset");
            var asset = AssetDatabase.LoadAssetAtPath(normalizedPath, type);
            if (asset == null) throw new ArgumentException("path must identify an Input Actions asset: " + normalizedPath);
            return asset;
        }

        private static object RequireMap(object asset, string requestedName)
        {
            foreach (var map in Enumerate(ReadRequiredMember(asset, "actionMaps")))
                if (string.Equals(ReadText(map, "name"), requestedName, StringComparison.Ordinal)) return map;
            throw new ArgumentException("The Input Actions asset does not contain a map named '" + requestedName + "'. Create the map in Unity's Input Actions editor first.");
        }

        private static void EnsureActionsAreDisabled(object asset)
        {
            foreach (var map in Enumerate(ReadRequiredMember(asset, "actionMaps")))
                foreach (var action in Enumerate(ReadRequiredMember(map, "actions")))
                    if (ReadBool(action, "enabled"))
                        throw new InvalidOperationException("Disable the Input Actions asset before editing it. Enabled action '" + ReadText(action, "name") + "' would make this mutation unsafe.");
        }

        private static MethodInfo RequireAddAction(object map, Type inputActionType)
        {
            var method = map.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance).FirstOrDefault(candidate =>
            {
                var parameters = candidate.GetParameters();
                return candidate.Name == "AddAction" && parameters.Length == 6 && parameters[0].ParameterType == typeof(string)
                    && parameters[1].ParameterType == inputActionType && parameters.Skip(2).All(parameter => parameter.ParameterType == typeof(string));
            });
            if (method == null) throw new InvalidOperationException("The installed Input System does not expose the supported InputActionMap.AddAction overload.");
            return method;
        }

        private static MethodInfo RequireAddBinding(Type actionType)
        {
            var method = actionType.GetMethods(BindingFlags.Public | BindingFlags.Instance).FirstOrDefault(candidate =>
            {
                var parameters = candidate.GetParameters();
                return candidate.Name == "AddBinding" && parameters.Length == 4 && parameters.All(parameter => parameter.ParameterType == typeof(string));
            });
            if (method == null) throw new InvalidOperationException("The installed Input System does not expose the supported InputAction.AddBinding overload.");
            return method;
        }

        private static MethodInfo FindRemoveAction(object map, Type actionType)
        {
            return map.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance).FirstOrDefault(candidate =>
            {
                var parameters = candidate.GetParameters();
                return candidate.Name == "RemoveAction" && parameters.Length == 1 && parameters[0].ParameterType.IsAssignableFrom(actionType);
            });
        }

        private static void TryRemoveAction(object map, MethodInfo removeAction, object action)
        {
            if (map == null || removeAction == null || action == null) return;
            try { removeAction.Invoke(map, new[] { action }); }
            catch { /* Best-effort recovery after an Input System exception. */ }
        }

        private static List<InputActionBindingCreateInput> ValidateBindings(List<InputActionBindingCreateInput> values)
        {
            var result = values == null ? new List<InputActionBindingCreateInput>() : values.ToList();
            if (result.Count > MaxBindingsPerAction) throw new ArgumentException("bindings may contain at most " + MaxBindingsPerAction + " entries.");
            foreach (var binding in result)
            {
                if (binding == null) throw new ArgumentException("bindings may not contain null entries.");
                if (string.IsNullOrWhiteSpace(binding.path) || binding.path.Length > MaxBindingPathLength || binding.path.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0)
                    throw new ArgumentException("Every binding.path must be a non-empty Input System control path shorter than " + MaxBindingPathLength + " characters.");
                ValidateOptionalText(binding.interactions, "binding interactions");
                ValidateOptionalText(binding.processors, "binding processors");
                ValidateOptionalText(binding.groups, "binding groups");
            }

            return result;
        }

        private static object ParseActionType(Type enumType, string value)
        {
            var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
            string enumName;
            switch (normalized)
            {
                case "button": enumName = "Button"; break;
                case "value": enumName = "Value"; break;
                case "pass-through":
                case "passthrough":
                case "pass_through": enumName = "PassThrough"; break;
                default: throw new ArgumentException("action-type must be button, value, or pass-through.");
            }

            return Enum.Parse(enumType, enumName, true);
        }

        private static string NormalizeInputActionsPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path is required.");
            var normalized = path.Trim().Replace('\\', '/');
            if (!normalized.StartsWith("Assets/", StringComparison.Ordinal) || normalized.Contains(":") || normalized.Split('/').Any(part => string.IsNullOrEmpty(part) || part == "." || part == ".."))
                throw new ArgumentException("path must be a normalized project-relative path under Assets/.");
            if (!normalized.EndsWith(".inputactions", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("path must end with .inputactions.");
            var assetsRoot = Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var projectRoot = Directory.GetParent(assetsRoot).FullName;
            var fullPath = Path.GetFullPath(Path.Combine(projectRoot, normalized.Replace('/', Path.DirectorySeparatorChar)));
            if (!fullPath.StartsWith(assetsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
                throw new ArgumentException("path must identify an existing Input Actions asset under Assets/.");
            EnsureNoReparsePoint(assetsRoot, fullPath);
            return normalized;
        }

        private static void EnsureNoReparsePoint(string root, string fullPath)
        {
            var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var normalizedPath = Path.GetFullPath(fullPath);
            if (!normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("path must remain under Assets/.");
            if (Directory.Exists(normalizedRoot) && (File.GetAttributes(normalizedRoot) & FileAttributes.ReparsePoint) != 0)
                throw new ArgumentException("The Assets root may not be a symbolic link or junction.");
            var relative = normalizedPath.Substring(normalizedRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var current = normalizedRoot;
            foreach (var part in relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, part);
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new ArgumentException("Symbolic links and junctions are not permitted in Input Actions paths.");
            }
        }

        private static Type RequireType(string fullName)
        {
            return EditorCommandHelpers.TryResolveOptionalType(fullName)
                ?? throw new InvalidOperationException("The required Input System type is unavailable: " + fullName);
        }

        private static object ReadRequiredMember(object target, string name)
        {
            var value = ReadOptionalMember(target, name);
            if (value == null) throw new InvalidOperationException(target.GetType().FullName + " does not expose readable member '" + name + "'.");
            return value;
        }

        private static object ReadOptionalMember(object target, string name)
        {
            if (target == null) return null;
            var type = target.GetType();
            var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (property != null && property.CanRead && property.GetIndexParameters().Length == 0) return property.GetValue(target, null);
            var field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            return field == null ? null : field.GetValue(target);
        }

        private static string ReadText(object target, string name)
        {
            var value = ReadOptionalMember(target, name);
            return value == null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static bool ReadBool(object target, string name)
        {
            var value = ReadOptionalMember(target, name);
            return value != null && Convert.ToBoolean(value, CultureInfo.InvariantCulture);
        }

        private static List<object> Enumerate(object value)
        {
            var result = new List<object>();
            if (!(value is IEnumerable enumerable)) return result;
            foreach (var item in enumerable) result.Add(item);
            return result;
        }

        private static void ValidateName(string value, string field)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 128 || value.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0)
                throw new ArgumentException(field + " must be non-empty, at most 128 characters, and contain no control characters.");
        }

        private static void ValidateOptionalText(string value, string field)
        {
            if (value != null && (value.Length > 1024 || value.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0))
                throw new ArgumentException(field + " must be at most 1024 characters and contain no control characters.");
        }

        private static string NormalizeOptional(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
#endif
