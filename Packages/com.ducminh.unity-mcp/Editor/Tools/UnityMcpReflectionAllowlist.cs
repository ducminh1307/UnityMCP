using System.Collections.Generic;
using UnityEngine;

namespace DucMinh.UnityMcp.Editor
{
    /// <summary>
    /// Project-local reflection policy. Broad base types are rejected by the caller.
    /// </summary>
    [CreateAssetMenu(menuName = "UnityMCP/Allowlist/Reflection", fileName = "UnityMcpReflectionAllowlist")]
    public sealed class UnityMcpReflectionAllowlist : ScriptableObject
    {
        [Tooltip("Explicit per-type capabilities. Broad base types such as UnityEngine.Object and Component are rejected by the bridge.")]
        public List<UnityMcpReflectionTypeRule> types = new List<UnityMcpReflectionTypeRule>();
    }
}
