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
            // Reflection-only runtime integrations (for example the Input System) have no direct
            // C# assembly reference. Preserve their required assembly only when it is installed
            // in this project so IL2CPP does not strip the type before the registry availability
            // check runs in the Development Player.
            var assemblyNames = methods.Select(m => m.DeclaringType.Assembly.GetName().Name)
                .Concat(methods.Select(m => FindRequiredType(m.GetCustomAttribute<UnityMcpToolAttribute>(false)?.RequiredType))
                    .Where(type => type != null)
                    .Select(type => type.Assembly.GetName().Name))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            var lines = new List<string> { "<linker>" };
            foreach (var assemblyName in assemblyNames)
            {
                lines.Add($"  <assembly fullname=\"{SecurityElement.Escape(assemblyName)}\" preserve=\"all\" />");
            }
            lines.Add("</linker>");
            var changed = WriteIfChanged(ManifestAssetPath, manifest) | WriteIfChanged(LinkAssetPath, string.Join("\n", lines) + "\n");
            if (changed) AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        private static Type FindRequiredType(string requiredType)
        {
            if (string.IsNullOrWhiteSpace(requiredType)) return null;
            var direct = Type.GetType(requiredType, false);
            if (direct != null) return direct;
            var typeName = requiredType.Split(',')[0].Trim();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var type = assembly.GetType(typeName, false);
                    if (type != null) return type;
                }
                catch { }
            }
            return null;
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
