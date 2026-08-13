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
    [Serializable] public sealed class InputActionsGetInput { public string path; public int maxActions = 512; }
    [Serializable] public sealed class InputBindingInfo
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

    [Serializable] public sealed class InputActionInfo
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

    [Serializable] public sealed class InputActionMapInfo
    {
        public string name;
        public string id;
        public int bindingCount;
        public List<InputActionInfo> actions = new List<InputActionInfo>();
    }

    [Serializable] public sealed class InputDeviceRequirementInfo
    {
        public string controlPath;
        public bool isOptional;
        public bool isOr;
    }

    [Serializable] public sealed class InputControlSchemeInfo
    {
        public string name;
        public string bindingGroup;
        public List<InputDeviceRequirementInfo> deviceRequirements = new List<InputDeviceRequirementInfo>();
    }

    [Serializable] public sealed class InputActionsGetOutput
    {
        public string path;
        public string name;
        public string id;
        public List<InputActionMapInfo> maps = new List<InputActionMapInfo>();
        public List<InputControlSchemeInfo> controlSchemes = new List<InputControlSchemeInfo>();
        public bool truncated;
    }

    [Serializable] public sealed class InputActionBindingCreateInput
    {
        /// <summary>Input System control path, such as &lt;Keyboard&gt;/space.</summary>
        public string path;
        public string interactions;
        public string processors;
        public string groups;
    }

    [Serializable] public sealed class InputActionCreateInput
    {
        public string path;
        public string map;
        public string action;
        /// <summary>button, value, or pass-through.</summary>
        public string actionType = "button";
        public string expectedControlType;
        public string interactions;
        public string processors;
        public List<InputActionBindingCreateInput> bindings = new List<InputActionBindingCreateInput>();
        public bool apply;
    }

    [Serializable] public sealed class InputActionCreateOutput
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
    /// Input Actions asset inspection and bounded action creation. This assembly never references
    /// com.unity.inputsystem at compile time: the registry hides both tools unless the optional
    /// InputActionAsset type is actually installed in the target Editor.
    /// </summary>
    public static class EditorInputSystemTools
    {
        private const int MaxActions = 2048;
        private const int MaxBindingsPerAction = 128;
        private const int MaxBindingPathLength = 512;

        [UnityMcpTool("input-actions-get", Description = "Read action maps, actions, bindings, and control schemes from an Input Actions asset.", Category = "audio-input", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.SafeRead, RequiredType = "UnityEngine.InputSystem.InputActionAsset")]
        public static InputActionsGetOutput InputActionsGet(InputActionsGetInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            var limit = Math.Max(1, Math.Min(input.maxActions, MaxActions));
            var asset = RequireInputActionsAsset(input.path, out var normalizedPath);
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

        [UnityMcpTool("input-action-create", Description = "Add a typed action with simple bindings to an existing Input Actions map; dry-run unless apply is true.", Category = "audio-input", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Write, SupportsDryRun = true, RequiredType = "UnityEngine.InputSystem.InputActionAsset")]
        public static InputActionCreateOutput InputActionCreate(InputActionCreateInput input, UnityMcpContext context)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (context == null) throw new ArgumentNullException(nameof(context));
            ValidateName(input.map, "map");
            ValidateName(input.action, "action");
            var bindings = ValidateBindings(input.bindings);
            var asset = RequireInputActionsAsset(input.path, out var normalizedPath);
            var map = RequireMap(asset, input.map);
            EnsureActionsAreDisabled(asset);
            if (Enumerate(ReadRequiredMember(map, "actions")).Any(action => string.Equals(ReadText(action, "name"), input.action, StringComparison.Ordinal)))
                throw new InvalidOperationException("The Input Actions map already contains an action named '" + input.action + "'.");

            var inputActionType = RequireType("UnityEngine.InputSystem.InputActionType");
            var actionType = ParseActionType(inputActionType, input.actionType);
            var addAction = RequireAddAction(map, inputActionType);
            var actionTypeObject = addAction.ReturnType;
            var addBinding = RequireAddBinding(actionTypeObject);
            var removeAction = FindRemoveAction(map, actionTypeObject);
            var result = new InputActionCreateOutput
            {
                dryRun = context.DryRun,
                changed = !context.DryRun,
                path = normalizedPath,
                map = input.map,
                action = input.action,
                bindingCount = bindings.Count,
                rollbackSupported = !context.DryRun,
                journal = new List<ChangeJournalEntry>
                {
                    new ChangeJournalEntry { operation = "add-input-action", after = normalizedPath + "#" + input.map + "/" + input.action }
                }
            };
            if (context.DryRun) return result;

            object action = null;
            try
            {
                Undo.RecordObject((UnityEngine.Object)asset, "UnityMCP Add Input Action");
                action = addAction.Invoke(map, new object[]
                {
                    input.action, actionType, null, NormalizeOptional(input.interactions), NormalizeOptional(input.processors), NormalizeOptional(input.expectedControlType)
                });
                foreach (var binding in bindings)
                    addBinding.Invoke(action, new object[] { binding.path, NormalizeOptional(binding.interactions), NormalizeOptional(binding.processors), NormalizeOptional(binding.groups) });
                EditorUtility.SetDirty((UnityEngine.Object)asset);
                AssetDatabase.SaveAssetIfDirty((UnityEngine.Object)asset);
                result.actionId = ReadText(action, "id");
                return result;
            }
            catch (TargetInvocationException exception)
            {
                TryRemoveAction(map, removeAction, action);
                throw new InvalidOperationException("Unity Input System rejected the requested action or binding: " + (exception.InnerException?.Message ?? exception.Message), exception.InnerException ?? exception);
            }
            catch
            {
                TryRemoveAction(map, removeAction, action);
                throw;
            }
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
                default: throw new ArgumentException("actionType must be button, value, or pass-through.");
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
            var type = Type.GetType(fullName, false);
            if (type != null) return type;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    type = assembly.GetType(fullName, false);
                    if (type != null) return type;
                }
                catch { }
            }
            throw new InvalidOperationException("The required Input System type is unavailable: " + fullName);
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
            return value == null ? null : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
        }

        private static bool ReadBool(object target, string name)
        {
            var value = ReadOptionalMember(target, name);
            return value != null && Convert.ToBoolean(value, System.Globalization.CultureInfo.InvariantCulture);
        }

        private static List<object> Enumerate(object value)
        {
            var result = new List<object>();
            var enumerable = value as IEnumerable;
            if (enumerable == null) return result;
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
