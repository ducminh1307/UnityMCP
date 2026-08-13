using System.Collections.Generic;
using UnityEngine;

namespace DucMinh.UnityMcp.Editor
{
    /// <summary>
    /// Project-local policy for the batch executor. It is never created or edited by
    /// an MCP call, so a developer must explicitly approve every batchable tool.
    /// </summary>
    [CreateAssetMenu(menuName = "UnityMCP/Allowlist/Batch", fileName = "UnityMcpBatchAllowlist")]
    public sealed class UnityMcpBatchAllowlist : ScriptableObject
    {
        public List<string> allowedToolNames = new List<string>();
    }
}
