using System.Collections.Generic;
using UnityEngine;

namespace DucMinh.UnityMcp
{
    [CreateAssetMenu(menuName = "UnityMCP/Runtime Profile", fileName = "UnityMcpRuntimeProfile")]
    public sealed class UnityMcpRuntimeProfile : ScriptableObject, IUnityMcpEnablementStore
    {
        public bool serverEnabled = true;
        [SerializeField] private List<string> enabledTools = new List<string>();
        [SerializeField] private List<string> disabledTools = new List<string>();

        public bool? GetOverride(string toolName)
        {
            if (enabledTools.Contains(toolName)) return true;
            if (disabledTools.Contains(toolName)) return false;
            return null;
        }

        public void SetOverride(string toolName, bool enabled)
        {
            enabledTools.Remove(toolName);
            disabledTools.Remove(toolName);
            (enabled ? enabledTools : disabledTools).Add(toolName);
        }
    }
}
