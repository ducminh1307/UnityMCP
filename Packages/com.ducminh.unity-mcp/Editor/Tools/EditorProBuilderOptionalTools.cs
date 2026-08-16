using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DucMinh.UnityMcp.Editor
{
    [Serializable] public sealed class ProBuilderCreateInput
    {
        /// <summary>Only "cube" is currently supported, using ProBuilder's public GenerateCube API.</summary>
        public string primitive = "cube";
        public string name = "ProBuilder Cube";
        public Vector3 size = Vector3.one;
        public Vector3 worldPosition = Vector3.zero;
        /// <summary>Optional loaded scene GameObject that becomes the parent.</summary>
        public int? parentGameObjectInstanceId;
        public bool apply;
    }

    [Serializable] public sealed class ProBuilderCreateOutput
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

    [Serializable] public sealed class ProBuilderVertexPosition
    {
        public int index;
        public Vector3 before;
        public Vector3 after;
    }

    [Serializable] public sealed class ProBuilderEditInput
    {
        /// <summary>Instance ID of a loaded ProBuilderMesh component.</summary>
        public int proBuilderMeshInstanceId;
        /// <summary>Unique vertex indexes in the ProBuilderMesh positions collection; at most 256 per call.</summary>
        public List<int> vertexIndexes = new List<int>();
        /// <summary>Finite local-space translation applied to exactly the listed vertices.</summary>
        public Vector3 localOffset;
        public bool apply;
    }

    [Serializable] public sealed class ProBuilderEditOutput
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
    /// Bounded ProBuilder integrations with no compile-time package reference. Creation is
    /// deliberately limited to a cube, and edit is deliberately limited to translating a
    /// bounded, validated list of local vertex indexes through the documented public API.
    /// </summary>
    public static class EditorProBuilderOptionalTools
    {
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

        [UnityMcpTool("probuilder-create", Description = "Create one ProBuilder cube with validated dimensions, transform, and optional scene parent; dry-run unless apply is true.", Category = "probuilder", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Write, SupportsDryRun = true, RequiredType = ShapeGeneratorTypeName)]
        public static ProBuilderCreateOutput ProBuilderCreate(ProBuilderCreateInput input, UnityMcpContext context)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            var primitive = NormalizePrimitive(input.primitive);
            var name = RequireName(input.name, "name");
            var size = RequirePositiveFiniteVector(input.size, "size", MinimumSize, MaximumSize);
            var position = RequireBoundedFiniteVector(input.worldPosition, "worldPosition", MaximumWorldCoordinate);
            var parent = input.parentGameObjectInstanceId.HasValue
                ? RequireSceneGameObject(input.parentGameObjectInstanceId.Value, "parentGameObjectInstanceId")
                : null;
            var output = new ProBuilderCreateOutput
            {
                dryRun = context.DryRun,
                created = !context.DryRun,
                primitive = primitive,
                name = name,
                parentGameObjectInstanceId = parent == null ? (int?)null : parent.GetInstanceID(),
                size = size,
                worldPosition = position,
                rollbackSupported = true,
                journal = new List<ChangeJournalEntry> { new ChangeJournalEntry { operation = "create-probuilder-cube", after = name } }
            };
            if (context.DryRun) return output;

            var mesh = GenerateCube(size);
            if (mesh == null) throw new InvalidOperationException("ProBuilder did not return a ProBuilderMesh for the new cube.");
            var component = mesh as Component;
            if (component == null) throw new InvalidOperationException("The installed ProBuilderMesh type is not a Unity Component.");
            var gameObject = component.gameObject;
            Undo.RegisterCreatedObjectUndo(gameObject, "UnityMCP Create ProBuilder Cube");
            gameObject.name = name;
            if (parent != null) Undo.SetTransformParent(gameObject.transform, parent.transform, "UnityMCP Set ProBuilder Cube Parent");
            gameObject.transform.position = position;
            EditorUtility.SetDirty(component);
            EditorSceneManager.MarkSceneDirty(gameObject.scene);

            output.gameObjectInstanceId = gameObject.GetInstanceID();
            output.proBuilderMeshInstanceId = component.GetInstanceID();
            output.vertexCount = ReadVertexPositions(mesh).Count;
            return output;
        }

        [UnityMcpTool("probuilder-edit", Description = "Translate a bounded, validated set of ProBuilderMesh local vertices using ProBuilder's public VertexPositioning API; dry-run unless apply is true.", Category = "probuilder", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Write, SupportsDryRun = true, RequiredType = ProBuilderMeshTypeName)]
        public static ProBuilderEditOutput ProBuilderEdit(ProBuilderEditInput input, UnityMcpContext context)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            var mesh = RequireProBuilderMesh(input.proBuilderMeshInstanceId);
            var component = mesh as Component;
            var positions = ReadVertexPositions(mesh);
            var indexes = RequireVertexIndexes(input.vertexIndexes, positions.Count);
            var offset = RequireBoundedFiniteVector(input.localOffset, "localOffset", MaximumOffsetMagnitude);
            if (offset.sqrMagnitude <= Mathf.Epsilon) throw new ArgumentException("localOffset must not be zero.");
            var vertices = indexes.Select(index => new ProBuilderVertexPosition { index = index, before = positions[index], after = positions[index] + offset }).ToList();
            var output = new ProBuilderEditOutput
            {
                dryRun = context.DryRun,
                changed = !context.DryRun,
                proBuilderMeshInstanceId = component.GetInstanceID(),
                vertexCount = positions.Count,
                editedVertexCount = indexes.Count,
                localOffset = offset,
                vertices = vertices,
                rollbackSupported = true,
                journal = new List<ChangeJournalEntry> { new ChangeJournalEntry { operation = "translate-probuilder-vertices", after = string.Join(",", indexes) } }
            };
            if (context.DryRun) return output;

            Undo.RecordObject(component, "UnityMCP Edit ProBuilder Vertices");
            var meshFilter = component.GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null) Undo.RecordObject(meshFilter.sharedMesh, "UnityMCP Edit ProBuilder Vertices");
            TranslateVertices(mesh, indexes, offset);
            EditorUtility.SetDirty(component);
            if (meshFilter != null && meshFilter.sharedMesh != null) EditorUtility.SetDirty(meshFilter.sharedMesh);
            EditorSceneManager.MarkSceneDirty(component.gameObject.scene);
            return output;
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

        private static object RequireProBuilderMesh(int instanceId)
        {
            var meshType = RequireType(ProBuilderMeshTypeName);
            var target = EditorUtility.EntityIdToObject((EntityId)instanceId);
            if (target == null || !meshType.IsInstanceOfType(target))
                throw new ArgumentException("proBuilderMeshInstanceId must identify a loaded UnityEngine.ProBuilder.ProBuilderMesh component.");
            var component = target as Component;
            if (component == null || !component.gameObject.scene.IsValid() || !component.gameObject.scene.isLoaded)
                throw new ArgumentException("proBuilderMeshInstanceId must identify a ProBuilderMesh in a loaded scene.");
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
                throw new ArgumentException("vertexIndexes must contain between 1 and " + MaximumVertexEdits + " entries.");
            var unique = new HashSet<int>();
            foreach (var index in input)
            {
                if (index < 0 || index >= vertexCount) throw new ArgumentException("vertexIndexes contains an index outside the ProBuilderMesh positions collection.");
                if (!unique.Add(index)) throw new ArgumentException("vertexIndexes must not contain duplicates.");
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

        private static GameObject RequireSceneGameObject(int instanceId, string parameterName)
        {
            var target = EditorUtility.EntityIdToObject((EntityId)instanceId) as GameObject;
            if (target == null || !target.scene.IsValid() || !target.scene.isLoaded)
                throw new ArgumentException(parameterName + " must identify a GameObject in a loaded scene.");
            return target;
        }

        private static Type RequireType(string fullName)
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
            throw new InvalidOperationException("Required optional Unity package type is unavailable: " + fullName + ".");
        }
    }
}
