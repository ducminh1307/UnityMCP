using System.Collections.Generic;
using UnityEngine;

namespace DucMinh.UnityMcp.Editor
{
    /// <summary>
    /// Project-local menu policy. It is not created or modified by an MCP call.
    /// </summary>
    [CreateAssetMenu(menuName = "UnityMCP/Allowlist/Menu", fileName = "UnityMcpMenuAllowlist")]
    public sealed class UnityMcpMenuAllowlist : ScriptableObject
    {
        [Tooltip("Exact Unity menu item paths that UnityMCP may execute after the tool is explicitly enabled.")]
        public List<string> allowedMenuItems = new List<string>();
    }
}
