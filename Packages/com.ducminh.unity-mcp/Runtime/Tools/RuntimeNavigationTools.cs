using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Unity.Pipeline.Commands;
using UnityEngine;
using UnityEngine.AI;

namespace DucMinh.UnityMcp
{
    [Serializable]
    public sealed class NavMeshPathCalculateInput
    {
        [JsonConverter(typeof(RuntimeVector3JsonConverter))]
        public Vector3 source;
        [JsonConverter(typeof(RuntimeVector3JsonConverter))]
        public Vector3 destination;
        /// <summary>Unity NavMesh area bitmask. -1 selects all areas.</summary>
        public int areaMask = -1;
        public int maxCorners = 256;
    }

    [Serializable]
    public sealed class NavMeshPathCalculateOutput
    {
        public bool calculated;
        public string status;
        public int cornerCount;
        public bool truncated;
        public float length;
        [JsonProperty(ItemConverterType = typeof(RuntimeVector3JsonConverter))]
        public List<Vector3> corners = new List<Vector3>();
    }

    /// <summary>
    /// Read-only path calculation against NavMesh data already loaded in the target. It neither
    /// bakes nor modifies NavMesh data, so it is useful in a Development Player and the Editor.
    /// </summary>
    public static class RuntimeNavigationTools
    {
        public const string Group = "navmesh_ext";
        private const int MaximumCorners = 1024;

        [CliGroupCommand(
            Group,
            "Calculate a bounded NavMesh path between world positions using already-loaded NavMesh data.",
            Name = "calculate_path",
            Effect = CommandEffect.ReadOnly,
            Tags = new[] { "runtime", "navigation" })]
        internal static ExitCode CalculatePathCommand(
            CommandContext cmd,
            [CliArg("source", "Start position in world space as '[x,y,z]', 'x,y,z', or '{\"x\":0,\"y\":0,\"z\":0}'.", Required = true)] string source,
            [CliArg("destination", "Destination position in world space as '[x,y,z]', 'x,y,z', or '{\"x\":0,\"y\":0,\"z\":0}'.", Required = true)] string destination,
            [CliArg("area-mask", "Unity NavMesh area bitmask (-1 selects all areas).")] int areaMask = -1,
            [CliArg("max-corners", "Maximum number of path corners to return (1-1024).")] int maxCorners = 256)
        {
            return RuntimeCommandHelpers.Execute(cmd, () => CalculatePath(new NavMeshPathCalculateInput
            {
                source = RuntimeCommandHelpers.ParseVector3(source, "source"),
                destination = RuntimeCommandHelpers.ParseVector3(destination, "destination"),
                areaMask = areaMask,
                maxCorners = maxCorners
            }));
        }

        internal static NavMeshPathCalculateOutput CalculatePath(NavMeshPathCalculateInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (input.areaMask == 0) throw new ArgumentException("areaMask must select at least one NavMesh area.");
            if (input.maxCorners < 1 || input.maxCorners > MaximumCorners)
                throw new ArgumentException("maxCorners must be between 1 and " + MaximumCorners + ".");

            RuntimeCommandHelpers.RequireFinite(input.source, nameof(input.source));
            RuntimeCommandHelpers.RequireFinite(input.destination, nameof(input.destination));

            var path = new NavMeshPath();
            var calculated = NavMesh.CalculatePath(input.source, input.destination, input.areaMask, path);
            var allCorners = path.corners ?? Array.Empty<Vector3>();
            var output = new NavMeshPathCalculateOutput
            {
                calculated = calculated,
                status = ToStatus(path.status),
                cornerCount = allCorners.Length,
                truncated = allCorners.Length > input.maxCorners
            };

            var count = Math.Min(allCorners.Length, input.maxCorners);
            for (var index = 0; index < count; index++)
                output.corners.Add(allCorners[index]);
            for (var index = 1; index < allCorners.Length; index++)
                output.length += Vector3.Distance(allCorners[index - 1], allCorners[index]);

            return output;
        }

        private static string ToStatus(NavMeshPathStatus status)
        {
            switch (status)
            {
                case NavMeshPathStatus.PathComplete: return "complete";
                case NavMeshPathStatus.PathPartial: return "partial";
                default: return "invalid";
            }
        }
    }
}
