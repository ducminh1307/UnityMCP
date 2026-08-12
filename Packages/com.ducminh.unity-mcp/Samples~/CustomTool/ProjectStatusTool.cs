using System;
using DucMinh.UnityMcp;
using UnityEngine;

public static class ProjectStatusTool
{
    [Serializable]
    public sealed class Input
    {
        public string label = "project";
    }

    [Serializable]
    public sealed class Output
    {
        public string label;
        public string productName;
        public string unityVersion;
        public bool isPlaying;
    }

    [UnityMcpTool(
        "project-status-sample",
        Title = "Project status sample",
        Description = "Demonstrates a typed project-local custom tool.",
        Category = "project",
        Scope = UnityMcpScope.All,
        Safety = UnityMcpSafety.SafeRead)]
    public static UnityMcpResult<Output> Run(Input input, UnityMcpContext context)
    {
        return new UnityMcpResult<Output>
        {
            structuredContent = new Output
            {
                label = input.label,
                productName = Application.productName,
                unityVersion = Application.unityVersion,
                isPlaying = Application.isPlaying
            }
        };
    }
}
