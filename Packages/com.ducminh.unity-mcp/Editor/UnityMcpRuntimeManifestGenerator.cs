using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace DucMinh.UnityMcp.Editor
{
    [InitializeOnLoad]
    internal sealed class UnityMcpRuntimeManifestGenerator : IPreprocessBuildWithReport
    {
        private const string DirectoryAssetPath = "Assets/UnityMCP/Generated/Resources";
        private const string ManifestAssetPath = DirectoryAssetPath + "/UnityMcpRuntimeManifest.json";
        private const string LinkAssetPath = "Assets/UnityMCP/Generated/link.xml";
        private static bool queued;
        public int callbackOrder => -10000;

        static UnityMcpRuntimeManifestGenerator()
        {
            EditorApplication.delayCall += QueueGenerate;
            UnityEditor.Compilation.CompilationPipeline.compilationFinished += _ => QueueGenerate();
        }

        public void OnPreprocessBuild(BuildReport report) => Generate();

        private static void QueueGenerate()
        {
            if (queued || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            queued = true;
            EditorApplication.delayCall += () => { queued = false; Generate(); };
        }

        internal static void Generate()
        {
            var methods = TypeCache.GetMethodsWithAttribute<UnityMcpToolAttribute>()
                .Where(m => m.IsStatic && (m.GetCustomAttribute<UnityMcpToolAttribute>(false).Scope & UnityMcpScope.Runtime) != 0)
                .OrderBy(m => m.DeclaringType?.FullName).ThenBy(m => m.Name).ToArray();
            var entries = methods.Select(m => new
            {
                assembly = m.DeclaringType.Assembly.GetName().Name,
                type = m.DeclaringType.FullName,
                method = m.Name,
                tool = m.GetCustomAttribute<UnityMcpToolAttribute>(false).Name,
                parameters = m.GetParameters().Select(p => p.ParameterType.AssemblyQualifiedName).ToArray()
            }).ToArray();
            var manifest = JsonConvert.SerializeObject(new { entries }, Formatting.Indented);
            var assemblies = methods.GroupBy(m => m.DeclaringType.Assembly.GetName().Name).OrderBy(g => g.Key);
            var lines = new List<string> { "<linker>" };
            foreach (var assembly in assemblies)
            {
                lines.Add($"  <assembly fullname=\"{SecurityElement.Escape(assembly.Key)}\" preserve=\"all\" />");
            }
            lines.Add("</linker>");
            var changed = WriteIfChanged(ManifestAssetPath, manifest) | WriteIfChanged(LinkAssetPath, string.Join("\n", lines) + "\n");
            if (changed) AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        private static bool WriteIfChanged(string assetPath, string content)
        {
            var fullPath = Path.GetFullPath(assetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            if (File.Exists(fullPath) && File.ReadAllText(fullPath) == content) return false;
            File.WriteAllText(fullPath, content, new System.Text.UTF8Encoding(false));
            return true;
        }
    }
}
