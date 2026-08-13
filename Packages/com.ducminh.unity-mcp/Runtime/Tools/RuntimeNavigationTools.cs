using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace DucMinh.UnityMcp
{
    [Serializable] public sealed class NavMeshPathCalculateInput
    {
        public Vector3 source;
        public Vector3 destination;
        /// <summary>Unity NavMesh area bitmask. -1 selects all areas.</summary>
        public int areaMask = -1;
        public int maxCorners = 256;
    }

    [Serializable] public sealed class NavMeshPathCalculateOutput
    {
        public bool calculated;
        public string status;
        public int cornerCount;
        public bool truncated;
        public float length;
        public List<Vector3> corners = new List<Vector3>();
    }

    /// <summary>
    /// Read-only path calculation against NavMesh data already loaded in the target. It neither
    /// bakes nor modifies NavMesh data, so it is useful in a Development Player and the Editor.
    /// </summary>
    public static class RuntimeNavigationTools
    {
        private const int MaximumCorners = 1024;

        [UnityMcpTool("navmesh-path-calculate", Description = "Calculate a bounded NavMesh path between world positions using already-loaded NavMesh data.", Category = "physics-navigation", Scope = UnityMcpScope.All, Safety = UnityMcpSafety.SafeRead, RequiredType = "UnityEngine.AI.NavMesh")]
        public static NavMeshPathCalculateOutput NavMeshPathCalculate(NavMeshPathCalculateInput input)
        {
            if (input.areaMask == 0) throw new ArgumentException("areaMask must select at least one NavMesh area.");
            if (input.maxCorners < 1 || input.maxCorners > MaximumCorners) throw new ArgumentException("maxCorners must be between 1 and " + MaximumCorners + ".");
            if (!IsFinite(input.source) || !IsFinite(input.destination)) throw new ArgumentException("source and destination must contain finite world coordinates.");

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
            for (var index = 0; index < count; index++) output.corners.Add(allCorners[index]);
            for (var index = 1; index < allCorners.Length; index++) output.length += Vector3.Distance(allCorners[index - 1], allCorners[index]);
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

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }
    }
}
