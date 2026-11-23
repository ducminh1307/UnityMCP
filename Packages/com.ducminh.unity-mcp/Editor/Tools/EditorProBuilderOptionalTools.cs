#if DM_PROBUILDER_PRESENT
#pragma warning disable UAC0005 // Editor tool reflection scans currently loaded ProBuilder types.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Editor.Authoring;
using Unity.Pipeline.Models;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DucMinh.UnityMcp.Editor
{
    [Serializable]
    public sealed class ProBuilderCreateOutput
    {
        public bool dryRun;
        public bool created;
        public string primitive;
        public string name;
        public int? gameObjectInstanceId;
        public int? proBuilderMeshInstanceId;
        public int? parentGameObjectInstanceId;
        public Vector3 size;
        public Vector3 worldPosition;
        public int vertexCount;
        public bool rollbackSupported;
        public List<ChangeJournalEntry> journal = new List<ChangeJournalEntry>();
    }

    [Serializable]
    public sealed class ProBuilderVertexPosition
    {
        public int index;
        public Vector3 before;
        public Vector3 after;
    }

    [Serializable]
    public sealed class ProBuilderEditOutput
    {
        public bool dryRun;
        public bool changed;
        public int proBuilderMeshInstanceId;
        public int vertexCount;
        public int editedVertexCount;
        public Vector3 localOffset;
        public List<ProBuilderVertexPosition> vertices = new List<ProBuilderVertexPosition>();
        public bool rollbackSupported;
        public List<ChangeJournalEntry> journal = new List<ChangeJournalEntry>();
    }

    /// <summary>
    /// Bounded ProBuilder integrations using reflection inside DM_PROBUILDER_PRESENT.
    /// Creation is limited to a cube and edit translates a bounded, validated list of local
    /// vertex indexes through ProBuilder's public VertexPositioning API.
    /// </summary>
    public static class EditorProBuilderOptionalTools
    {
        private const string Group = "probuilder";
        private const string ProBuilderMeshTypeName = "UnityEngine.ProBuilder.ProBuilderMesh";
        private const string ShapeGeneratorTypeName = "UnityEngine.ProBuilder.ShapeGenerator";
        private const string PivotLocationTypeName = "UnityEngine.ProBuilder.PivotLocation";
        private const string VertexPositioningTypeName = "UnityEngine.ProBuilder.VertexPositioning";
        private const int MaximumNameLength = 128;
        private const int MaximumVertexEdits = 256;
        private const float MinimumSize = 0.01f;
        private const float MaximumSize = 100000f;
        private const float MaximumWorldCoordinate = 1000000f;
        private const float MaximumOffsetMagnitude = 100000f;

        [CliGroupCommand(
            Group,
            "Create one ProBuilder cube with validated dimensions, transform, and optional scene parent.",
            Name = "create",
            Effect = CommandEffect.Additive,
            Tags = new[] { "probuilder", "geometry" })]
        internal static ExitCode Create(
            CommandContext cmd,
            [CliArg("primitive", "Primitive type to create (currently only 'cube').")] string primitive = "cube",
            [CliArg("name", "Name for the created ProBuilder GameObject.")] string name = "ProBuilder Cube",
            [CliArg("size", "Dimensions as [x,y,z], comma-separated x,y,z, or {x,y,z}. Default: 1,1,1.")] string size = null,
            [CliArg("world-position", "World position as [x,y,z], comma-separated x,y,z, or {x,y,z}. Default: 0,0,0.")] string worldPosition = null,
            [CliArg("parent-id", "Optional parent GameObject handle (globalId, hierarchyPath, or instanceId).")] ObjectRef parentId = null,
            [CliArg("dry-run", "If true, validate inputs and preview without creating the mesh.")] bool dryRun = false)
        {
            return Execute(cmd, () => CreateCore(
                primitive,
                name,
                RuntimeCommandHelpers.ParseVector3Nullable(size, "size") ?? Vector3.one,
                RuntimeCommandHelpers.ParseVector3Nullable(worldPosition, "world-position") ?? Vector3.zero,
                parentId,
                dryRun));
        }

        [CliGroupCommand(
            Group,
            "Translate a bounded, validated set of ProBuilderMesh local vertices using ProBuilder's VertexPositioning API.",
            Name = "edit",
            Effect = CommandEffect.Idempotent,
            Tags = new[] { "probuilder", "geometry" })]
        internal static ExitCode Edit(
            CommandContext cmd,
            [CliArg("probuilder-mesh-id", "Handle or instanceId of a loaded ProBuilderMesh component.", Required = true)] ObjectRef proBuilderMeshId,
            [CliArg("vertex-indexes-json", "Vertex indexes as a JSON array (e.g. \"[0,1,2]\") or comma-separated ints (e.g. \"0,1,2\").", Required = true)] string vertexIndexesJson,
            [CliArg("local-offset", "Local-space translation as [x,y,z], comma-separated x,y,z, or {x,y,z}.", Required = true)] string localOffset,
            [CliArg("dry-run", "If true, validate inputs and preview vertex deltas without editing the mesh.")] bool dryRun = false)
        {
            return Execute(cmd, () => EditCore(
                proBuilderMeshId,
                ParseVertexIndexes(vertexIndexesJson),
                RuntimeCommandHelpers.ParseVector3(localOffset, "local-offset"),
                dryRun));
        }

        internal static ProBuilderCreateOutput CreateCore(
            string primitive = "cube",
            string name = "ProBuilder Cube",
            Vector3? size = null,
            Vector3? worldPosition = null,
            ObjectRef parentId = null,
            bool dryRun = false)
        {
            var normalizedPrimitive = NormalizePrimitive(primitive);
            var normalizedName = RequireName(name, "name");
            var resolvedSize = RequirePositiveFiniteVector(size ?? Vector3.one, "size", MinimumSize, MaximumSize);
            var resolvedPosition = RequireBoundedFiniteVector(worldPosition ?? Vector3.zero, "world-position", MaximumWorldCoordinate);
            var parent = parentId != null && !parentId.IsEmpty ? RequireSceneGameObject(parentId, "parent-id") : null;

            var output = new ProBuilderCreateOutput
            {
                dryRun = dryRun,
                created = !dryRun,
                primitive = normalizedPrimitive,
                name = normalizedName,
                parentGameObjectInstanceId = parent == null ? (int?)null : UnityMcpObjectId.Get(parent),
                size = resolvedSize,
                worldPosition = resolvedPosition,
                rollbackSupported = true,
                journal = new List<ChangeJournalEntry> { new ChangeJournalEntry { operation = "create-probuilder-cube", after = normalizedName } }
            };
            if (dryRun) return output;

            object mesh;
            Component component;
            GameObject gameObject;
            using (new AuthoringUndoScope("UnityMCP Create ProBuilder Cube"))
            {
                mesh = GenerateCube(resolvedSize);
                if (mesh == null) throw new InvalidOperationException("ProBuilder did not return a ProBuilderMesh for the new cube.");
                component = mesh as Component;
                if (component == null) throw new InvalidOperationException("The installed ProBuilderMesh type is not a Unity Component.");
                gameObject = component.gameObject;
                Undo.RegisterCreatedObjectUndo(gameObject, "UnityMCP Create ProBuilder Cube");
                gameObject.name = normalizedName;
                if (parent != null) Undo.SetTransformParent(gameObject.transform, parent.transform, "UnityMCP Set ProBuilder Cube Parent");
                gameObject.transform.position = resolvedPosition;
                EditorUtility.SetDirty(component);
                EditorSceneManager.MarkSceneDirty(gameObject.scene);
            }

            output.gameObjectInstanceId = UnityMcpObjectId.Get(gameObject);
            output.proBuilderMeshInstanceId = UnityMcpObjectId.Get(component);
            output.vertexCount = ReadVertexPositions(mesh).Count;
            return output;
        }

        internal static ProBuilderEditOutput EditCore(
            ObjectRef proBuilderMeshId,
            List<int> vertexIndexes,
            Vector3 localOffset,
            bool dryRun = false)
        {
            var mesh = RequireProBuilderMesh(proBuilderMeshId);
            var component = (Component)mesh;
            var positions = ReadVertexPositions(mesh);
            var indexes = RequireVertexIndexes(vertexIndexes, positions.Count);
            var offset = RequireBoundedFiniteVector(localOffset, "local-offset", MaximumOffsetMagnitude);
            if (offset.sqrMagnitude <= Mathf.Epsilon) throw new ArgumentException("local-offset must not be zero.");

            var vertices = indexes.Select(index => new ProBuilderVertexPosition
            {
                index = index,
                before = positions[index],
                after = positions[index] + offset
            }).ToList();

            var output = new ProBuilderEditOutput
            {
                dryRun = dryRun,
                changed = !dryRun,
                proBuilderMeshInstanceId = UnityMcpObjectId.Get(component),
                vertexCount = positions.Count,
                editedVertexCount = indexes.Count,
                localOffset = offset,
                vertices = vertices,
                rollbackSupported = true,
                journal = new List<ChangeJournalEntry> { new ChangeJournalEntry { operation = "translate-probuilder-vertices", after = string.Join(",", indexes) } }
            };
            if (dryRun) return output;

            using (new AuthoringUndoScope("UnityMCP Edit ProBuilder Vertices"))
            {
                Undo.RecordObject(component, "UnityMCP Edit ProBuilder Vertices");
                var meshFilter = component.GetComponent<MeshFilter>();
                if (meshFilter != null && meshFilter.sharedMesh != null) Undo.RecordObject(meshFilter.sharedMesh, "UnityMCP Edit ProBuilder Vertices");
                TranslateVertices(mesh, indexes, offset);
                EditorUtility.SetDirty(component);
                if (meshFilter != null && meshFilter.sharedMesh != null) EditorUtility.SetDirty(meshFilter.sharedMesh);
                EditorSceneManager.MarkSceneDirty(component.gameObject.scene);
            }

            return output;
        }

        internal static List<int> ParseVertexIndexes(string vertexIndexesJson)
        {
            if (string.IsNullOrWhiteSpace(vertexIndexesJson))
                throw new ArgumentException("vertex-indexes-json is required.");

            var trimmed = vertexIndexesJson.Trim();
            if (trimmed.StartsWith("[", StringComparison.Ordinal))
            {
                JArray array;
                try { array = JArray.Parse(trimmed); }
                catch (Exception ex) { throw new ArgumentException("vertex-indexes-json is not a valid JSON int array: " + ex.Message, ex); }

                var parsed = new List<int>(array.Count);
                foreach (var item in array)
                {
                    if (item.Type != JTokenType.Integer)
                        throw new ArgumentException("vertex-indexes-json array entries must be integers.");
                    parsed.Add(item.Value<int>());
                }

                return parsed;
            }

            var parts = trimmed.Split(',');
            var result = new List<int>(parts.Length);
            foreach (var part in parts)
            {
                var token = part.Trim();
                if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
                    throw new ArgumentException("vertex-indexes-json contains invalid integer '" + token + "'.");
                result.Add(index);
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

        private static object GenerateCube(Vector3 size)
        {
            var generatorType = RequireType(ShapeGeneratorTypeName);
            var pivotType = RequireType(PivotLocationTypeName);
            var method = generatorType.GetMethod("GenerateCube", BindingFlags.Public | BindingFlags.Static, null, new[] { pivotType, typeof(Vector3) }, null);
            if (method == null) throw new InvalidOperationException("The installed ProBuilder package does not expose ShapeGenerator.GenerateCube(PivotLocation, Vector3).");
            var pivot = Enum.Parse(pivotType, "Center", true);
            return method.Invoke(null, new[] { pivot, (object)size });
        }

        private static object RequireProBuilderMesh(ObjectRef handle)
        {
            var meshType = RequireType(ProBuilderMeshTypeName);
            var resolved = ResolveObject(handle, "probuilder-mesh-id");
            var target = meshType.IsInstanceOfType(resolved)
                ? resolved
                : (resolved as GameObject)?.GetComponent(meshType);

            if (target == null || !meshType.IsInstanceOfType(target))
                throw new ArgumentException("probuilder-mesh-id must identify a loaded UnityEngine.ProBuilder.ProBuilderMesh component.");

            var component = target as Component;
            if (component == null || !component.gameObject.scene.IsValid() || !component.gameObject.scene.isLoaded)
                throw new ArgumentException("probuilder-mesh-id must identify a ProBuilderMesh in a loaded scene.");
            return target;
        }

        private static List<Vector3> ReadVertexPositions(object mesh)
        {
            var property = mesh.GetType().GetProperty("positions", BindingFlags.Public | BindingFlags.Instance);
            if (property == null || !property.CanRead) throw new InvalidOperationException("The installed ProBuilderMesh type does not expose a readable positions collection.");
            var source = property.GetValue(mesh, null) as IEnumerable;
            if (source == null) throw new InvalidOperationException("The ProBuilderMesh positions collection is unavailable.");
            var output = new List<Vector3>();
            foreach (var position in source)
            {
                if (!(position is Vector3 vector)) throw new InvalidOperationException("The ProBuilderMesh positions collection has an unexpected element type.");
                output.Add(vector);
            }

            return output;
        }

        private static void TranslateVertices(object mesh, List<int> indexes, Vector3 offset)
        {
            var meshType = RequireType(ProBuilderMeshTypeName);
            var positioningType = RequireType(VertexPositioningTypeName);
            var method = positioningType.GetMethod("TranslateVertices", BindingFlags.Public | BindingFlags.Static, null,
                new[] { meshType, typeof(IEnumerable<int>), typeof(Vector3) }, null);
            if (method == null) throw new InvalidOperationException("The installed ProBuilder package does not expose VertexPositioning.TranslateVertices(ProBuilderMesh, IEnumerable<int>, Vector3).");
            method.Invoke(null, new object[] { mesh, indexes.ToArray(), offset });
        }

        private static List<int> RequireVertexIndexes(List<int> input, int vertexCount)
        {
            if (input == null || input.Count == 0 || input.Count > MaximumVertexEdits)
                throw new ArgumentException("vertex-indexes-json must contain between 1 and " + MaximumVertexEdits + " entries.");
            var unique = new HashSet<int>();
            foreach (var index in input)
            {
                if (index < 0 || index >= vertexCount) throw new ArgumentException("vertex-indexes-json contains an index outside the ProBuilderMesh positions collection.");
                if (!unique.Add(index)) throw new ArgumentException("vertex-indexes-json must not contain duplicates.");
            }

            return input.OrderBy(index => index).ToList();
        }

        private static string NormalizePrimitive(string value)
        {
            if (!string.Equals((value ?? string.Empty).Trim(), "cube", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("primitive currently supports only 'cube'.");
            return "cube";
        }

        private static string RequireName(string value, string parameterName)
        {
            var normalized = (value ?? string.Empty).Trim();
            if (normalized.Length == 0 || normalized.Length > MaximumNameLength || normalized.Any(char.IsControl))
                throw new ArgumentException(parameterName + " must contain 1 to " + MaximumNameLength + " non-control characters.");
            return normalized;
        }

        private static Vector3 RequirePositiveFiniteVector(Vector3 value, string parameterName, float minimum, float maximum)
        {
            if (!IsFinite(value.x) || !IsFinite(value.y) || !IsFinite(value.z) || value.x < minimum || value.y < minimum || value.z < minimum || value.x > maximum || value.y > maximum || value.z > maximum)
                throw new ArgumentException(parameterName + " must have finite XYZ values between " + minimum + " and " + maximum + ".");
            return value;
        }

        private static Vector3 RequireBoundedFiniteVector(Vector3 value, string parameterName, float maximumMagnitude)
        {
            if (!IsFinite(value.x) || !IsFinite(value.y) || !IsFinite(value.z) || value.sqrMagnitude > maximumMagnitude * maximumMagnitude)
                throw new ArgumentException(parameterName + " must have finite XYZ values with magnitude at most " + maximumMagnitude + ".");
            return value;
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static GameObject RequireSceneGameObject(ObjectRef handle, string parameterName)
        {
            var value = ResolveObject(handle, parameterName);
            var target = value as GameObject ?? (value as Component)?.gameObject;
            if (target == null || !target.scene.IsValid() || !target.scene.isLoaded)
                throw new ArgumentException(parameterName + " must identify a GameObject in a loaded scene.");
            return target;
        }

        private static UnityEngine.Object ResolveObject(ObjectRef handle, string paramName)
        {
            if (handle == null || handle.IsEmpty)
                throw new ArgumentException(paramName + " is required.");

            if (ObjectResolver.TryResolve(handle, out var obj, out var error) && obj != null)
                return obj;

            var text = handle.ToString();
            if (!string.IsNullOrEmpty(text) && text.StartsWith("instanceId:", StringComparison.Ordinal)
                && int.TryParse(text.Substring("instanceId:".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out var legacyId))
            {
                var legacy = UnityMcpEditorObjectId.Resolve(legacyId);
                if (legacy != null) return legacy;
            }

            throw new ArgumentException(paramName + " could not be resolved: " + (error ?? "not found."));
        }

        private static Type RequireType(string fullName)
        {
            return EditorCommandHelpers.TryResolveOptionalType(fullName)
                ?? throw new InvalidOperationException("Required optional Unity package type is unavailable: " + fullName + ".");
        }
    }
}
#endif
