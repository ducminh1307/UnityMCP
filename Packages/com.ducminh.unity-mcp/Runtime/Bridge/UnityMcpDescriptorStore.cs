#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;

namespace DucMinh.UnityMcp
{
    internal static class UnityMcpDescriptorStore
    {
        public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UnityMCP", "instances");

        public static UnityMcpInstanceDescriptor Create(int port, string token, UnityMcpScope scope)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return new UnityMcpInstanceDescriptor
            {
                port = port,
                token = token,
                pid = Process.GetCurrentProcess().Id,
                projectId = Hash(projectRoot.Replace('\\', '/').ToLowerInvariant()),
                instanceId = Guid.NewGuid().ToString("N"),
                kind = scope == UnityMcpScope.Editor ? "editor" : "player",
                buildId = string.IsNullOrEmpty(Application.buildGUID) ? Hash(Application.unityVersion + ":" + projectRoot) : Application.buildGUID
            };
        }

        public static string Write(UnityMcpInstanceDescriptor descriptor)
        {
            Directory.CreateDirectory(DirectoryPath);
            HardenPermissions(DirectoryPath, true);
            PruneStale();
            var path = Path.Combine(DirectoryPath, descriptor.instanceId + ".json");
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonConvert.SerializeObject(descriptor, Formatting.Indented), new UTF8Encoding(false));
            HardenPermissions(temporary, false);
            if (File.Exists(path)) File.Delete(path);
            File.Move(temporary, path);
            HardenPermissions(path, false);
            return path;
        }

        public static void Delete(string path)
        {
            try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); } catch { }
        }

        private static void PruneStale()
        {
            foreach (var file in Directory.GetFiles(DirectoryPath, "*.json").Take(256))
            {
                try
                {
                    var descriptor = JsonConvert.DeserializeObject<UnityMcpInstanceDescriptor>(File.ReadAllText(file));
                    if (descriptor == null || descriptor.pid <= 0 || !IsRunning(descriptor.pid)) File.Delete(file);
                }
                catch { try { File.Delete(file); } catch { } }
            }
        }

        private static bool IsRunning(int pid)
        {
            try { return !Process.GetProcessById(pid).HasExited; } catch { return false; }
        }

        private static string Hash(string value)
        {
            using (var sha = SHA256.Create())
                return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(value)).Take(12).Select(b => b.ToString("x2")));
        }

        private static void HardenPermissions(string path, bool directory)
        {
#if UNITY_EDITOR_OSX || UNITY_EDITOR_LINUX || UNITY_STANDALONE_OSX || UNITY_STANDALONE_LINUX
            // 0700 for the descriptor directory; 0600 for bearer-token files.
            if (Chmod(path, directory ? 448u : 384u) != 0)
                throw new IOException("UnityMCP could not apply private descriptor permissions.");
#else
            // LocalApplicationData inherits the current user's profile ACL on Windows.
#endif
        }

#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
        [DllImport("libSystem.B.dylib", EntryPoint = "chmod", SetLastError = true)]
        private static extern int Chmod(string path, uint mode);
#elif UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX
        [DllImport("libc", EntryPoint = "chmod", SetLastError = true)]
        private static extern int Chmod(string path, uint mode);
#endif

    }
}
#endif
