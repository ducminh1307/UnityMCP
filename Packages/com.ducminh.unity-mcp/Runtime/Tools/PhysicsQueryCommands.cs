using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Unity.Pipeline.Commands;
using UnityEngine;

namespace DucMinh.UnityMcp
{
    [Serializable]
    public sealed class PhysicsCollisionLayerInfo
    {
        public int layer;
        public string name;
        public List<int> ignoredWith = new List<int>();
    }

    [Serializable]
    public sealed class PhysicsCollisionMatrixOutput
    {
        public List<PhysicsCollisionLayerInfo> layers = new List<PhysicsCollisionLayerInfo>();
    }

    [Serializable]
    public sealed class PhysicsRaycastInput
    {
        [JsonConverter(typeof(RuntimeVector3JsonConverter))]
        public Vector3 origin;
        [JsonConverter(typeof(RuntimeVector3JsonConverter))]
        public Vector3 direction = Vector3.forward;
        public float maxDistance = 100f;
        public int layerMask = -1;
        public bool includeTriggers;
    }

    [Serializable]
    public sealed class PhysicsRaycastOutput
    {
        public bool hit;
        public int colliderInstanceId;
        public string gameObjectPath;
        [JsonConverter(typeof(RuntimeVector3JsonConverter))]
        public Vector3 point;
        [JsonConverter(typeof(RuntimeVector3JsonConverter))]
        public Vector3 normal;
        public float distance;
    }

    [Serializable]
    public sealed class PhysicsOverlapInput
    {
        public string shape = "sphere";
        [JsonConverter(typeof(RuntimeVector3JsonConverter))]
        public Vector3 center;
        public float radius = 1f;
        [JsonConverter(typeof(RuntimeVector3JsonConverter))]
        public Vector3 halfExtents = Vector3.one;
        public Quaternion? orientation;
        public int layerMask = -1;
        public bool includeTriggers;
        public int limit = 100;
    }

    [Serializable]
    public sealed class PhysicsOverlapOutput
    {
        public List<GameObjectSummary> matches = new List<GameObjectSummary>();
        public bool truncated;
    }

    /// <summary>
    /// Runtime and Editor 3D physics collision matrix and spatial query commands.
    /// </summary>
    public static class PhysicsQueryCommands
    {
        public const string Group = "physics_ext";

        [CliGroupCommand(
            Group,
            "Read the 3D physics layer collision matrix.",
            Name = "get_collision_matrix",
            Effect = CommandEffect.ReadOnly,
            Tags = new[] { "runtime", "physics" })]
        internal static ExitCode GetCollisionMatrixCommand(CommandContext cmd)
        {
            return RuntimeCommandHelpers.Execute(cmd, GetCollisionMatrix);
        }

        [CliGroupCommand(
            Group,
            "Set one 3D physics layer collision relationship.",
            Name = "set_collision_matrix",
            Effect = CommandEffect.Idempotent,
            Tags = new[] { "runtime", "physics" })]
        internal static ExitCode SetCollisionMatrixCommand(
            CommandContext cmd,
            [CliArg("layer-a", "First Unity layer index (0-31).", Required = true)] int layerA,
            [CliArg("layer-b", "Second Unity layer index (0-31).", Required = true)] int layerB,
            [CliArg("ignore", "Whether collisions between layer-a and layer-b should be ignored.", Required = true)] bool ignore,
            [CliArg("dry-run", "Validate and preview the change without modifying the collision matrix.")] bool dryRun = false)
        {
            return RuntimeCommandHelpers.Execute(cmd, () => SetCollisionMatrix(layerA, layerB, ignore, dryRun));
        }

        [CliGroupCommand(
            Group,
            "Perform a bounded 3D physics raycast.",
            Name = "raycast",
            Effect = CommandEffect.ReadOnly,
            Tags = new[] { "runtime", "physics" })]
        internal static ExitCode RaycastCommand(
            CommandContext cmd,
            [CliArg("origin", "Ray origin in world space as '[x,y,z]', 'x,y,z', or '{\"x\":0,\"y\":0,\"z\":0}'.", Required = true)] string origin,
            [CliArg("direction", "Ray direction vector as '[x,y,z]', 'x,y,z', or '{\"x\":0,\"y\":0,\"z\":1}'.", Required = true)] string direction,
            [CliArg("max-distance", "Maximum ray distance (>= 0).")] float maxDistance = 100f,
            [CliArg("layer-mask", "Unity Physics layer bitmask (-1 selects all layers).")] int layerMask = -1,
            [CliArg("include-triggers", "Whether the raycast can hit trigger colliders.")] bool includeTriggers = false)
        {
            return RuntimeCommandHelpers.Execute(cmd, () => Raycast(new PhysicsRaycastInput
            {
                origin = RuntimeCommandHelpers.ParseVector3(origin, "origin"),
                direction = RuntimeCommandHelpers.ParseVector3(direction, "direction"),
                maxDistance = maxDistance,
                layerMask = layerMask,
                includeTriggers = includeTriggers
            }));
        }

        [CliGroupCommand(
            Group,
            "Perform a bounded sphere or box overlap query.",
            Name = "overlap",
            Effect = CommandEffect.ReadOnly,
            Tags = new[] { "runtime", "physics" })]
        internal static ExitCode OverlapCommand(
            CommandContext cmd,
            [CliArg("shape", "Overlap volume shape: 'sphere' or 'box'.", Required = true)] string shape,
            [CliArg("center", "Overlap center in world space as '[x,y,z]', 'x,y,z', or '{\"x\":0,\"y\":0,\"z\":0}'.", Required = true)] string center,
            [CliArg("radius", "Sphere radius (>= 0) when shape is 'sphere'.")] float radius = 1f,
            [CliArg("half-extents", "Box half-extents as '[x,y,z]', 'x,y,z', or '{\"x\":1,\"y\":1,\"z\":1}' when shape is 'box'.")] string halfExtents = null,
            [CliArg("orientation", "Optional box rotation quaternion as '[x,y,z,w]', 'x,y,z,w', or '{\"x\":0,\"y\":0,\"z\":0,\"w\":1}'.")] string orientation = null,
            [CliArg("layer-mask", "Unity Physics layer bitmask (-1 selects all layers).")] int layerMask = -1,
            [CliArg("include-triggers", "Whether the overlap can hit trigger colliders.")] bool includeTriggers = false,
            [CliArg("limit", "Maximum number of matched GameObjects to return (1-1000).")] int limit = 100)
        {
            return RuntimeCommandHelpers.Execute(cmd, () => Overlap(new PhysicsOverlapInput
            {
                shape = shape,
                center = RuntimeCommandHelpers.ParseVector3(center, "center"),
                radius = radius,
                halfExtents = RuntimeCommandHelpers.ParseVector3Nullable(halfExtents, "half-extents") ?? Vector3.one,
                orientation = RuntimeCommandHelpers.ParseQuaternionNullable(orientation, "orientation"),
                layerMask = layerMask,
                includeTriggers = includeTriggers,
                limit = limit
            }));
        }

        internal static PhysicsCollisionMatrixOutput GetCollisionMatrix()
        {
            var output = new PhysicsCollisionMatrixOutput();
            for (var layer = 0; layer < 32; layer++)
            {
                var info = new PhysicsCollisionLayerInfo
                {
                    layer = layer,
                    name = LayerMask.LayerToName(layer)
                };
                for (var other = 0; other < 32; other++)
                {
                    if (Physics.GetIgnoreLayerCollision(layer, other))
                        info.ignoredWith.Add(other);
                }
                output.layers.Add(info);
            }
            return output;
        }

        internal static RuntimeChangeResult SetCollisionMatrix(int layerA, int layerB, bool ignore, bool dryRun = false)
        {
            ValidateLayer(layerA, nameof(layerA));
            ValidateLayer(layerB, nameof(layerB));

            if (!dryRun)
                Physics.IgnoreLayerCollision(layerA, layerB, ignore);

            var action = ignore ? "Ignore" : "Enable";
            return RuntimeCommandHelpers.Change(dryRun, $"{action} collisions between layers {layerA} and {layerB}.");
        }

        internal static PhysicsRaycastOutput Raycast(PhysicsRaycastInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));

            RuntimeCommandHelpers.RequireFinite(input.origin, nameof(input.origin));
            RuntimeCommandHelpers.RequireFinite(input.direction, nameof(input.direction));
            if (float.IsNaN(input.maxDistance) || float.IsInfinity(input.maxDistance) || input.maxDistance < 0f)
                throw new ArgumentOutOfRangeException(nameof(input.maxDistance), "maxDistance must be finite and non-negative.");
            if (input.direction.sqrMagnitude < 0.000001f)
                throw new ArgumentException("direction must be non-zero.");

            var trigger = input.includeTriggers ? QueryTriggerInteraction.Collide : QueryTriggerInteraction.Ignore;
            if (!Physics.Raycast(input.origin, input.direction.normalized, out var hit, input.maxDistance, input.layerMask, trigger))
                return new PhysicsRaycastOutput();

            return new PhysicsRaycastOutput
            {
                hit = true,
                colliderInstanceId = UnityMcpObjectId.Get(hit.collider),
                gameObjectPath = RuntimeCommandHelpers.HierarchyPath(hit.collider.gameObject),
                point = hit.point,
                normal = hit.normal,
                distance = hit.distance
            };
        }

        internal static PhysicsOverlapOutput Overlap(PhysicsOverlapInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));

            RuntimeCommandHelpers.RequireFinite(input.center, nameof(input.center));
            var clampedLimit = Mathf.Clamp(input.limit, 1, 1000);
            var trigger = input.includeTriggers ? QueryTriggerInteraction.Collide : QueryTriggerInteraction.Ignore;
            Collider[] colliders;

            if (string.Equals(input.shape, "box", StringComparison.OrdinalIgnoreCase))
            {
                RuntimeCommandHelpers.RequireFinite(input.halfExtents, nameof(input.halfExtents));
                if (input.halfExtents.x < 0f || input.halfExtents.y < 0f || input.halfExtents.z < 0f)
                    throw new ArgumentOutOfRangeException(nameof(input.halfExtents), "halfExtents components must be non-negative.");

                var rotation = input.orientation ?? Quaternion.identity;
                RuntimeCommandHelpers.RequireFinite(rotation.x, nameof(input.orientation));
                RuntimeCommandHelpers.RequireFinite(rotation.y, nameof(input.orientation));
                RuntimeCommandHelpers.RequireFinite(rotation.z, nameof(input.orientation));
                RuntimeCommandHelpers.RequireFinite(rotation.w, nameof(input.orientation));

                colliders = Physics.OverlapBox(input.center, input.halfExtents, rotation, input.layerMask, trigger);
            }
            else if (string.Equals(input.shape, "sphere", StringComparison.OrdinalIgnoreCase))
            {
                if (float.IsNaN(input.radius) || float.IsInfinity(input.radius) || input.radius < 0f)
                    throw new ArgumentOutOfRangeException(nameof(input.radius), "radius must be finite and non-negative.");

                colliders = Physics.OverlapSphere(input.center, input.radius, input.layerMask, trigger);
            }
            else
            {
                throw new ArgumentException("shape must be 'sphere' or 'box'.");
            }

            var output = new PhysicsOverlapOutput();
            var seenGameObjects = new HashSet<int>();
            foreach (var collider in colliders.Where(value => value != null).OrderBy(UnityMcpObjectId.Get))
            {
                var gameObject = collider.gameObject;
                if (!seenGameObjects.Add(UnityMcpObjectId.Get(gameObject)))
                    continue;

                if (output.matches.Count >= clampedLimit)
                {
                    output.truncated = true;
                    break;
                }

                output.matches.Add(RuntimeCommandHelpers.Summarize(gameObject));
            }

            return output;
        }

        private static void ValidateLayer(int layer, string paramName)
        {
            if (layer < 0 || layer > 31)
                throw new ArgumentOutOfRangeException(paramName, $"{paramName} must be between 0 and 31.");
        }
    }
}
