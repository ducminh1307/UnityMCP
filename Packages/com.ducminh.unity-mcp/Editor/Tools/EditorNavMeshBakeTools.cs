using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace DucMinh.UnityMcp.Editor
{
    [Serializable]
    public sealed class NavMeshBakeInput
    {
        /// <summary>Instance ID of a loaded Unity.AI.Navigation.NavMeshSurface component.</summary>
        public int surfaceInstanceId;
        public bool apply;
    }

    [Serializable]
    public sealed class NavMeshBakeOutput
    {
        public bool dryRun;
        public bool accepted;
        public string jobId;
        public string status;
        public string summary;
    }

    /// <summary>
    /// Optional AI Navigation integration. The component is discovered through reflection so this
    /// package compiles without com.unity.ai.navigation and is not listed until it is installed.
    /// </summary>
    public static class EditorNavMeshBakeTools
    {
        [UnityMcpTool("navmesh-bake", Description = "Bake one allowlisted NavMeshSurface component as an Editor job; dry-run unless apply is true.", Category = "physics-navigation", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Unsafe, SupportsDryRun = true, ReturnsJob = true, RequiredType = "Unity.AI.Navigation.NavMeshSurface", TimeoutMs = 600000)]
        public static NavMeshBakeOutput NavMeshBake(NavMeshBakeInput input, UnityMcpContext context)
        {
            var surface = RequireSurface(input.surfaceInstanceId, out var buildMethod);
            if (context.DryRun)
            {
                return new NavMeshBakeOutput
                {
                    dryRun = true,
                    status = "dry-run",
                    summary = "Dry run: bake NavMeshSurface '" + surface.name + "'."
                };
            }

            var handle = EditorWorkflowJobRunner.Start(new NavMeshBakeOperation(surface, buildMethod));
            return new NavMeshBakeOutput
            {
                accepted = true,
                jobId = handle.jobId,
                status = handle.status,
                summary = "NavMesh bake queued. It may keep the Unity Editor busy while Unity builds navigation data."
            };
        }

        private static Component RequireSurface(int instanceId, out MethodInfo buildMethod)
        {
            var requiredType = FindType("Unity.AI.Navigation.NavMeshSurface")
                ?? throw new InvalidOperationException("The AI Navigation package is not available.");
            var target = EditorUtility.EntityIdToObject((EntityId)instanceId) as Component;
            if (target == null || !requiredType.IsInstanceOfType(target))
                throw new ArgumentException("surfaceInstanceId must identify a loaded Unity.AI.Navigation.NavMeshSurface component.");
            buildMethod = requiredType.GetMethod("BuildNavMesh", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);
            if (buildMethod == null) throw new InvalidOperationException("The installed NavMeshSurface does not expose BuildNavMesh().");
            return target;
        }

        private static Type FindType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var result = assembly.GetType(fullName, false);
                    if (result != null) return result;
                }
                catch { }
            }
            return null;
        }

        private sealed class NavMeshBakeOperation : IEditorWorkflowOperation
        {
            private readonly Component surface;
            private readonly MethodInfo buildMethod;
            private bool started;

            public NavMeshBakeOperation(Component surface, MethodInfo buildMethod)
            {
                this.surface = surface;
                this.buildMethod = buildMethod;
            }

            // BuildNavMesh is synchronous in the public package API. Cancellation is respected
            // before it begins; once Unity starts its synchronous build it must run to completion.
            public bool DrainWhenCancelled => false;

            public bool Tick(UnityMcpJob job)
            {
                if (job.IsCancellationRequested) return true;
                if (started) return true;
                started = true;
                job.status = "running";
                buildMethod.Invoke(surface, null);
                EditorWorkflowJobRunner.Succeed(job, new { status = "succeeded", surfaceInstanceId = surface.GetInstanceID() });
                return true;
            }
        }
    }
}
