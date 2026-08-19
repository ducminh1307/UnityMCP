using System.Collections.Generic;
using UnityEngine;

namespace DucMinh.UnityMcp.Editor
{
    /// <summary>
    /// Legacy configuration asset retained so existing projects can still deserialize it.
    /// Generic ScriptableObject tools no longer require this per-type allowlist; local tool
    /// enablement, dry-run/apply, and Assets/-scoped paths provide the permission boundary.
    /// </summary>
    public sealed class UnityMcpScriptableObjectAllowlist : ScriptableObject
    {
        public List<string> allowedTypeNames = new List<string>();
    }
}
