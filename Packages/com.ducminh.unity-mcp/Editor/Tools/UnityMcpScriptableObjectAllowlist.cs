using System.Collections.Generic;
using UnityEngine;

namespace DucMinh.UnityMcp.Editor
{
    /// <summary>
    /// An explicit local opt-in list for ScriptableObject tooling. Create this asset from
    /// Assets/Create/UnityMCP/ScriptableObject Allowlist, then enter full type names.
    /// It deliberately lives in the Editor assembly: it is a local Editor permission,
    /// not a runtime dependency of project ScriptableObjects.
    /// </summary>
    [CreateAssetMenu(menuName = "UnityMCP/ScriptableObject Allowlist", fileName = "UnityMcpScriptableObjectAllowlist")]
    public sealed class UnityMcpScriptableObjectAllowlist : ScriptableObject
    {
        public List<string> allowedTypeNames = new List<string>();
    }
}
