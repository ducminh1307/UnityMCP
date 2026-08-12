using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace DucMinh.UnityMcp.Editor
{
    internal sealed class EditorEnablementStore : IUnityMcpEnablementStore
    {
        private readonly string prefix;
        public EditorEnablementStore()
        {
            using (var sha = SHA256.Create())
                prefix = "DucMinh.UnityMcp." + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Application.dataPath))).Replace("-", "").Substring(0, 16) + ".tool.";
        }

        public bool? GetOverride(string toolName)
        {
            var key = prefix + toolName;
            if (!EditorPrefs.HasKey(key)) return null;
            return EditorPrefs.GetBool(key);
        }

        public void SetOverride(string toolName, bool enabled) => EditorPrefs.SetBool(prefix + toolName, enabled);
    }

    [InitializeOnLoad]
    internal static class UnityMcpEditorBootstrap
    {
        private static UnityMcpHttpServer server;
        private const string SessionDescriptorKey = "DucMinh.UnityMcp.EditorDescriptor";
        internal static UnityMcpRegistry Registry { get; private set; }

        static UnityMcpEditorBootstrap()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.quitting += Stop;
            EditorApplication.update += UnityMcpMainThread.Pump;
            EditorApplication.delayCall += Start;
        }

        private static void Start()
        {
            if (server != null) return;
            UnityMcpMainThread.Initialize(false);
            UnityMcpRegistry.DiscoveryOverride = () => TypeCache.GetMethodsWithAttribute<UnityMcpToolAttribute>().Cast<MethodInfo>();
            Registry = new UnityMcpRegistry(UnityMcpScope.Editor, new EditorEnablementStore());
            Registry.Reload();
            server = new UnityMcpHttpServer(Registry, UnityMcpScope.Editor);
            UnityMcpInstanceDescriptor preferred = null;
            var stored = SessionState.GetString(SessionDescriptorKey, string.Empty);
            if (!string.IsNullOrEmpty(stored))
            {
                try { preferred = JsonConvert.DeserializeObject<UnityMcpInstanceDescriptor>(stored); } catch { }
            }
            try { server.Start(preferred); }
            catch when (preferred != null) { server.Dispose(); server = new UnityMcpHttpServer(Registry, UnityMcpScope.Editor); server.Start(); }
            SessionState.SetString(SessionDescriptorKey, JsonConvert.SerializeObject(server.Descriptor));
        }

        private static void Stop()
        {
            server?.Dispose();
            server = null;
        }
    }

    public sealed class UnityMcpToolsWindow : EditorWindow
    {
        private const string RuntimeProfilePath = "Assets/UnityMCP/Resources/UnityMcpRuntimeProfile.asset";
        private Vector2 scroll;

        [MenuItem("Window/UnityMCP/Tools")]
        public static void Open() => GetWindow<UnityMcpToolsWindow>("UnityMCP Tools");

        private void OnGUI()
        {
            var registry = UnityMcpEditorBootstrap.Registry;
            if (registry == null) { EditorGUILayout.HelpBox("UnityMCP is starting.", MessageType.Info); return; }
            EditorGUILayout.HelpBox("Tool enablement is stored locally for this user and project. Custom and mutating tools start disabled.", MessageType.Info);
            if (GUILayout.Button("Reload tool registry")) registry.Reload();
            var runtimeProfile = AssetDatabase.LoadAssetAtPath<UnityMcpRuntimeProfile>(RuntimeProfilePath);
            if (runtimeProfile == null)
            {
                if (GUILayout.Button("Create Development Player runtime profile")) CreateRuntimeProfile();
            }
            else if (GUILayout.Button("Select Development Player runtime profile")) Selection.activeObject = runtimeProfile;
            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var group in registry.Tools.GroupBy(t => t.category))
            {
                EditorGUILayout.LabelField(group.Key, EditorStyles.boldLabel);
                foreach (var tool in group)
                {
                    var enabled = EditorGUILayout.ToggleLeft($"{tool.name}  [{tool.safety}]", tool.enabled);
                    if (enabled != tool.enabled) registry.SetEnabled(tool.name, enabled);
                }
                EditorGUILayout.Space(4);
            }
            EditorGUILayout.EndScrollView();
        }

        [MenuItem("Assets/Create/UnityMCP/Development Player Runtime Profile")]
        private static void CreateRuntimeProfile()
        {
            var existing = AssetDatabase.LoadAssetAtPath<UnityMcpRuntimeProfile>(RuntimeProfilePath);
            if (existing != null) { Selection.activeObject = existing; EditorGUIUtility.PingObject(existing); return; }
            System.IO.Directory.CreateDirectory(System.IO.Path.GetFullPath("Assets/UnityMCP/Resources"));
            var profile = CreateInstance<UnityMcpRuntimeProfile>();
            AssetDatabase.CreateAsset(profile, RuntimeProfilePath);
            AssetDatabase.SaveAssets();
            Selection.activeObject = profile;
            EditorGUIUtility.PingObject(profile);
        }
    }
}
