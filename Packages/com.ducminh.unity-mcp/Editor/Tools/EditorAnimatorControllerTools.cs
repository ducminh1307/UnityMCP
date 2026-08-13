using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace DucMinh.UnityMcp.Editor
{
    [Serializable] public sealed class AnimatorControllerCreateInput
    {
        public string path;
        public string name;
        public string baseLayerName = "Base Layer";
        public bool apply;
    }

    [Serializable] public sealed class AnimatorControllerCreateOutput
    {
        public bool dryRun;
        public bool created;
        public string path;
        public string name;
        public string baseLayerName;
        public bool rollbackSupported;
        public List<ChangeJournalEntry> journal = new List<ChangeJournalEntry>();
    }

    [Serializable] public sealed class AnimatorStateAddInput
    {
        public string controllerPath;
        public string layerName = "Base Layer";
        public string stateName;
        public Vector3? position;
        /// <summary>Optional Assets-relative AnimationClip or other Motion asset path.</summary>
        public string motionPath;
        public bool apply;
    }

    [Serializable] public sealed class AnimatorStateAddOutput
    {
        public bool dryRun;
        public bool added;
        public string controllerPath;
        public string layerName;
        public string stateName;
        public string motionPath;
        public bool rollbackSupported;
        public List<ChangeJournalEntry> journal = new List<ChangeJournalEntry>();
    }

    [Serializable] public sealed class AnimatorTransitionConditionInput
    {
        public string parameter;
        /// <summary>if, if-not, greater, less, equals, or not-equal.</summary>
        public string mode;
        public float? threshold;
    }

    [Serializable] public sealed class AnimatorTransitionAddInput
    {
        public string controllerPath;
        public string layerName = "Base Layer";
        public string fromState;
        public string toState;
        public bool? hasExitTime;
        public float? exitTime;
        public float? duration;
        public bool? fixedDuration;
        public bool? canTransitionToSelf;
        public List<AnimatorTransitionConditionInput> conditions = new List<AnimatorTransitionConditionInput>();
        public bool apply;
    }

    [Serializable] public sealed class AnimatorTransitionAddOutput
    {
        public bool dryRun;
        public bool added;
        public string controllerPath;
        public string layerName;
        public string fromState;
        public string toState;
        public int conditionCount;
        public bool rollbackSupported;
        public List<ChangeJournalEntry> journal = new List<ChangeJournalEntry>();
    }

    /// <summary>
    /// Bounded AnimatorController asset authoring. The tools never alter controller parameters,
    /// Any State transitions, state machines, or blend trees: callers name an existing layer and
    /// concrete states, and all asset paths remain below Assets/.
    /// </summary>
    public static class EditorAnimatorControllerTools
    {
        private static readonly Regex ValidIdentifier = new Regex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

        [UnityMcpTool("animator-controller-create", Description = "Create an AnimatorController asset with one named base layer; dry-run unless apply is true.", Category = "animation-timeline", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Write, SupportsDryRun = true)]
        public static AnimatorControllerCreateOutput AnimatorControllerCreate(AnimatorControllerCreateInput input, UnityMcpContext context)
        {
            var path = NormalizeCreatableAssetPath(input.path, ".controller");
            if (AssetDatabase.LoadMainAssetAtPath(path) != null || File.Exists(ToFullPath(path)))
                throw new InvalidOperationException("An asset already exists at the requested controller path.");
            EnsureNoReparsePoint(AssetsRoot, ToFullPath(path));
            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(parent) || !AssetDatabase.IsValidFolder(parent)) throw new ArgumentException("The target AnimatorController folder must already exist under Assets.");
            var layerName = RequireName(input.baseLayerName, "baseLayerName");
            var controllerName = string.IsNullOrWhiteSpace(input.name) ? Path.GetFileNameWithoutExtension(path) : RequireName(input.name, "name");

            if (!context.DryRun)
            {
                var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
                if (controller == null) throw new InvalidOperationException("Unity could not create the AnimatorController asset.");
                var layers = controller.layers;
                if (layers == null || layers.Length != 1 || layers[0].stateMachine == null)
                    throw new InvalidOperationException("Unity created an AnimatorController without the expected base layer.");
                layers[0].name = layerName;
                controller.layers = layers;
                controller.name = controllerName;
                Undo.RegisterCreatedObjectUndo(controller, "UnityMCP Create AnimatorController");
                EditorUtility.SetDirty(controller);
                EditorUtility.SetDirty(layers[0].stateMachine);
                AssetDatabase.SaveAssets();
            }

            return new AnimatorControllerCreateOutput
            {
                dryRun = context.DryRun,
                created = !context.DryRun,
                path = path,
                name = controllerName,
                baseLayerName = layerName,
                rollbackSupported = true,
                journal = new List<ChangeJournalEntry> { new ChangeJournalEntry { operation = "create-animator-controller", after = path } }
            };
        }

        [UnityMcpTool("animator-state-add", Description = "Add a named state with an optional Motion to an existing AnimatorController layer; dry-run unless apply is true.", Category = "animation-timeline", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Write, SupportsDryRun = true)]
        public static AnimatorStateAddOutput AnimatorStateAdd(AnimatorStateAddInput input, UnityMcpContext context)
        {
            var path = NormalizeExistingAssetPath(input.controllerPath, ".controller");
            var controller = LoadController(path);
            var layer = RequireLayer(controller, input.layerName);
            var stateName = RequireName(input.stateName, "stateName");
            if (FindState(layer.stateMachine, stateName) != null) throw new ArgumentException("The target AnimatorController layer already contains a state named '" + stateName + "'.");
            Motion motion = null;
            string motionPath = null;
            if (!string.IsNullOrWhiteSpace(input.motionPath))
            {
                motionPath = NormalizeExistingAssetPath(input.motionPath, null);
                motion = AssetDatabase.LoadAssetAtPath<Motion>(motionPath);
                if (motion == null) throw new ArgumentException("motionPath must identify an AnimationClip or other Motion asset.");
            }
            var position = input.position ?? DefaultStatePosition(layer.stateMachine);

            if (!context.DryRun)
            {
                Undo.RegisterCompleteObjectUndo(new UnityEngine.Object[] { controller, layer.stateMachine }, "UnityMCP Add Animator State");
                var state = layer.stateMachine.AddState(stateName, position);
                if (state == null) throw new InvalidOperationException("Unity could not add the Animator state.");
                if (motion != null) state.motion = motion;
                EditorUtility.SetDirty(state);
                EditorUtility.SetDirty(layer.stateMachine);
                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssets();
            }

            return new AnimatorStateAddOutput
            {
                dryRun = context.DryRun,
                added = !context.DryRun,
                controllerPath = path,
                layerName = layer.name,
                stateName = stateName,
                motionPath = motionPath,
                rollbackSupported = true,
                journal = new List<ChangeJournalEntry> { new ChangeJournalEntry { operation = "add-animator-state", after = path + "#" + layer.name + "/" + stateName } }
            };
        }

        [UnityMcpTool("animator-transition-add", Description = "Add a validated state transition with optional exit settings and existing-controller parameter conditions; dry-run unless apply is true.", Category = "animation-timeline", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Write, SupportsDryRun = true)]
        public static AnimatorTransitionAddOutput AnimatorTransitionAdd(AnimatorTransitionAddInput input, UnityMcpContext context)
        {
            var path = NormalizeExistingAssetPath(input.controllerPath, ".controller");
            var controller = LoadController(path);
            var layer = RequireLayer(controller, input.layerName);
            var fromName = RequireName(input.fromState, "fromState");
            var toName = RequireName(input.toState, "toState");
            var from = FindState(layer.stateMachine, fromName) ?? throw new ArgumentException("fromState was not found in the selected AnimatorController layer.");
            var to = FindState(layer.stateMachine, toName) ?? throw new ArgumentException("toState was not found in the selected AnimatorController layer.");
            if (ReferenceEquals(from, to) && input.canTransitionToSelf != true)
                throw new ArgumentException("A transition to the same state requires canTransitionToSelf=true.");
            ValidateTransitionTiming(input);
            var conditions = ResolveConditions(controller, input.conditions);

            if (!context.DryRun)
            {
                Undo.RegisterCompleteObjectUndo(new UnityEngine.Object[] { controller, layer.stateMachine, from, to }, "UnityMCP Add Animator Transition");
                var transition = from.AddTransition(to);
                if (transition == null) throw new InvalidOperationException("Unity could not add the Animator transition.");
                if (input.hasExitTime.HasValue) transition.hasExitTime = input.hasExitTime.Value;
                if (input.exitTime.HasValue) transition.exitTime = input.exitTime.Value;
                if (input.duration.HasValue) transition.duration = input.duration.Value;
                if (input.fixedDuration.HasValue) transition.hasFixedDuration = input.fixedDuration.Value;
                if (input.canTransitionToSelf.HasValue) transition.canTransitionToSelf = input.canTransitionToSelf.Value;
                foreach (var condition in conditions) transition.AddCondition(condition.mode, condition.threshold, condition.parameter);
                EditorUtility.SetDirty(transition);
                EditorUtility.SetDirty(from);
                EditorUtility.SetDirty(layer.stateMachine);
                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssets();
            }

            return new AnimatorTransitionAddOutput
            {
                dryRun = context.DryRun,
                added = !context.DryRun,
                controllerPath = path,
                layerName = layer.name,
                fromState = fromName,
                toState = toName,
                conditionCount = conditions.Count,
                rollbackSupported = true,
                journal = new List<ChangeJournalEntry> { new ChangeJournalEntry { operation = "add-animator-transition", after = path + "#" + layer.name + "/" + fromName + "->" + toName } }
            };
        }

        private sealed class ResolvedCondition
        {
            public string parameter;
            public AnimatorConditionMode mode;
            public float threshold;
        }

        private static List<ResolvedCondition> ResolveConditions(AnimatorController controller, List<AnimatorTransitionConditionInput> values)
        {
            var result = new List<ResolvedCondition>();
            foreach (var value in values ?? new List<AnimatorTransitionConditionInput>())
            {
                if (value == null) throw new ArgumentException("conditions may not contain null entries.");
                var parameter = RequireName(value.parameter, "conditions.parameter");
                var parameterInfo = controller.parameters.FirstOrDefault(item => string.Equals(item.name, parameter, StringComparison.Ordinal));
                if (string.IsNullOrEmpty(parameterInfo.name)) throw new ArgumentException("AnimatorController has no parameter named '" + parameter + "'.");
                var mode = ParseConditionMode(value.mode);
                ValidateCondition(parameterInfo.type, mode, value.threshold, parameter);
                result.Add(new ResolvedCondition { parameter = parameter, mode = mode, threshold = value.threshold ?? 0f });
            }
            return result;
        }

        private static AnimatorConditionMode ParseConditionMode(string value)
        {
            switch ((value ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "if": return AnimatorConditionMode.If;
                case "if-not":
                case "ifnot": return AnimatorConditionMode.IfNot;
                case "greater": return AnimatorConditionMode.Greater;
                case "less": return AnimatorConditionMode.Less;
                case "equals": return AnimatorConditionMode.Equals;
                case "not-equal":
                case "notequal": return AnimatorConditionMode.NotEqual;
                default: throw new ArgumentException("conditions.mode must be if, if-not, greater, less, equals, or not-equal.");
            }
        }

        private static void ValidateCondition(AnimatorControllerParameterType parameterType, AnimatorConditionMode mode, float? threshold, string parameter)
        {
            var isBoolean = parameterType == AnimatorControllerParameterType.Bool || parameterType == AnimatorControllerParameterType.Trigger;
            if (isBoolean && mode != AnimatorConditionMode.If && mode != AnimatorConditionMode.IfNot)
                throw new ArgumentException("Animator parameter '" + parameter + "' accepts only if or if-not conditions.");
            if (!isBoolean && (mode == AnimatorConditionMode.If || mode == AnimatorConditionMode.IfNot))
                throw new ArgumentException("Animator parameter '" + parameter + "' requires a numeric comparison condition.");
            if (!isBoolean)
            {
                if (!threshold.HasValue || float.IsNaN(threshold.Value) || float.IsInfinity(threshold.Value))
                    throw new ArgumentException("Animator condition '" + parameter + "' requires a finite threshold.");
                if (parameterType == AnimatorControllerParameterType.Int && Math.Abs(threshold.Value - (float)Math.Round(threshold.Value)) > 0.0001f)
                    throw new ArgumentException("Integer Animator condition thresholds must be whole numbers.");
            }
            else if (threshold.HasValue && (float.IsNaN(threshold.Value) || float.IsInfinity(threshold.Value)))
            {
                throw new ArgumentException("Animator condition thresholds must be finite when supplied.");
            }
        }

        private static void ValidateTransitionTiming(AnimatorTransitionAddInput input)
        {
            if (input.exitTime.HasValue && (float.IsNaN(input.exitTime.Value) || float.IsInfinity(input.exitTime.Value) || input.exitTime.Value < 0f || input.exitTime.Value > 1f))
                throw new ArgumentException("exitTime must be finite and between zero and one.");
            if (input.duration.HasValue && (float.IsNaN(input.duration.Value) || float.IsInfinity(input.duration.Value) || input.duration.Value < 0f || input.duration.Value > 60f))
                throw new ArgumentException("duration must be finite and between zero and 60 seconds.");
        }

        private static AnimatorController LoadController(string path)
        {
            var result = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (result == null) throw new ArgumentException("controllerPath must identify an AnimatorController asset.");
            return result;
        }

        private static AnimatorControllerLayer RequireLayer(AnimatorController controller, string name)
        {
            var desired = RequireName(name, "layerName");
            foreach (var layer in controller.layers)
                if (string.Equals(layer.name, desired, StringComparison.Ordinal)) return layer;
            throw new ArgumentException("AnimatorController layer was not found: " + desired);
        }

        private static AnimatorState FindState(AnimatorStateMachine stateMachine, string name)
        {
            return stateMachine.states.Select(item => item.state).FirstOrDefault(state => state != null && string.Equals(state.name, name, StringComparison.Ordinal));
        }

        private static Vector3 DefaultStatePosition(AnimatorStateMachine stateMachine)
        {
            var count = stateMachine.states == null ? 0 : stateMachine.states.Length;
            return new Vector3(250f + count * 250f, 80f, 0f);
        }

        private static string RequireName(string value, string parameterName)
        {
            var result = (value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(result) || result.Length > 128 || result.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0)
                throw new ArgumentException(parameterName + " must be a non-empty name of at most 128 characters.");
            return result;
        }

        private static string NormalizeExistingAssetPath(string path, string extension)
        {
            var result = NormalizeAssetPath(path, extension);
            var fullPath = ToFullPath(result);
            if (!File.Exists(fullPath)) throw new ArgumentException("Asset file was not found: " + result);
            EnsureNoReparsePoint(AssetsRoot, fullPath);
            return result;
        }

        private static string NormalizeCreatableAssetPath(string path, string extension)
        {
            var result = NormalizeAssetPath(path, extension);
            var fullPath = ToFullPath(result);
            if (!IsContained(AssetsRoot, fullPath)) throw new ArgumentException("Asset path must stay under Assets/.");
            EnsureNoReparsePoint(AssetsRoot, fullPath);
            return result;
        }

        private static string NormalizeAssetPath(string path, string extension)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A project-relative path under Assets/ is required.");
            var result = path.Trim().Replace('\\', '/');
            if (Path.IsPathRooted(result) || result.Contains(":")) throw new ArgumentException("Asset paths must be project-relative under Assets/.");
            var parts = result.Split('/');
            if (parts.Length < 2 || !string.Equals(parts[0], "Assets", StringComparison.Ordinal) || parts.Any(part => string.IsNullOrEmpty(part) || part == "." || part == ".."))
                throw new ArgumentException("Asset paths must be normalized below Assets/ without traversal segments.");
            if (result.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Unity .meta files are not valid tool targets.");
            if (extension != null && !result.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Asset path must end with " + extension + ".");
            return result;
        }

        private static string AssetsRoot => Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        private static string ProjectRoot => Directory.GetParent(AssetsRoot).FullName;

        private static string ToFullPath(string assetPath)
        {
            var result = Path.GetFullPath(Path.Combine(ProjectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar)));
            if (!IsContained(AssetsRoot, result)) throw new ArgumentException("Asset path escapes the project Assets directory.");
            return result;
        }

        private static bool IsContained(string root, string path)
        {
            var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var normalizedPath = Path.GetFullPath(path);
            return string.Equals(normalizedRoot, normalizedPath, StringComparison.OrdinalIgnoreCase) || normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static void EnsureNoReparsePoint(string root, string fullPath)
        {
            if (!IsContained(root, fullPath)) throw new ArgumentException("Asset path must stay under Assets/.");
            var current = Path.GetFullPath(root);
            if (Directory.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new ArgumentException("The Assets root may not be a symbolic link or junction.");
            var relative = fullPath.Substring(current.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            foreach (var part in relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, part);
                if (!File.Exists(current) && !Directory.Exists(current)) break;
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new ArgumentException("Symbolic links and junctions are not permitted in tool paths.");
            }
        }
    }
}
