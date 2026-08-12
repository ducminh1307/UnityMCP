#if DEVELOPMENT_BUILD && !UNITY_EDITOR && (UNITY_STANDALONE_WIN || UNITY_STANDALONE_OSX || UNITY_STANDALONE_LINUX)
using UnityEngine;

namespace DucMinh.UnityMcp
{
    internal static class UnityMcpRuntimeBootstrap
    {
        private static UnityMcpHttpServer server;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Start()
        {
            if (server != null) return;
            var profile = Resources.Load<UnityMcpRuntimeProfile>("UnityMcpRuntimeProfile");
            if (profile == null || !profile.serverEnabled) return;
            UnityMcpRegistry.DiscoveryOverride = UnityMcpRuntimeManifest.Discover;
            UnityMcpMainThread.Initialize(true);
            var registry = new UnityMcpRegistry(UnityMcpScope.Runtime, profile);
            registry.Reload();
            server = new UnityMcpHttpServer(registry, UnityMcpScope.Runtime);
            server.Start();
            Application.quitting += Stop;
        }

        private static void Stop()
        {
            server?.Dispose();
            server = null;
        }
    }
}
#endif
