#if DEVELOPMENT_BUILD && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace DucMinh.UnityMcp
{
    [Serializable] internal sealed class RuntimeManifestData { public List<RuntimeManifestEntry> entries = new List<RuntimeManifestEntry>(); }
    [Serializable] internal sealed class RuntimeManifestEntry { public string assembly; public string type; public string method; public string tool; public List<string> parameters = new List<string>(); }

    internal static class UnityMcpRuntimeManifest
    {
        public static IEnumerable<MethodInfo> Discover()
        {
            var asset = Resources.Load<TextAsset>("UnityMcpRuntimeManifest");
            if (asset == null)
            {
                Debug.LogError("UnityMCP runtime manifest is missing. Rebuild the Development Player after the Editor package generated it.");
                return Array.Empty<MethodInfo>();
            }
            var data = JsonUtility.FromJson<RuntimeManifestData>(asset.text);
            if (data?.entries == null) return Array.Empty<MethodInfo>();
            var result = new List<MethodInfo>();
            foreach (var entry in data.entries)
            {
                try
                {
                    var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == entry.assembly);
                    var type = assembly?.GetType(entry.type, false);
                    var method = type?.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                        .FirstOrDefault(m => m.Name == entry.method && m.GetCustomAttribute<UnityMcpToolAttribute>(false)?.Name == entry.tool &&
                                             m.GetParameters().Select(p => p.ParameterType.AssemblyQualifiedName).SequenceEqual(entry.parameters ?? new List<string>()));
                    if (method != null) result.Add(method);
                    else Debug.LogError($"UnityMCP manifest entry could not be resolved: {entry.type}.{entry.method} ({entry.tool}).");
                }
                catch (Exception exception) { Debug.LogError("UnityMCP manifest entry failed: " + exception.Message); }
            }
            return result;
        }
    }
}
#endif
