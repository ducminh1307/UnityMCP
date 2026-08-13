using System.Collections.Generic;
using UnityEngine;

namespace DucMinh.UnityMcp.Editor
{
    /// <summary>
    /// A developer-owned list of named, reviewed project commands. This asset is deliberately
    /// not created or changed by UnityMCP: the Editor user must create and review it locally.
    /// </summary>
    [CreateAssetMenu(menuName = "UnityMCP/Allowlist/C# Commands", fileName = "UnityMcpCSharpCommandAllowlist")]
    public sealed class UnityMcpCSharpCommandAllowlist : ScriptableObject
    {
        [Tooltip("Every command must name one exact public static method in a project assembly compiled from Assets/ source files.")]
        public List<UnityMcpCSharpCommandRule> commands = new List<UnityMcpCSharpCommandRule>();
    }
}
