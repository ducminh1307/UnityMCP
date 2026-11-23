using System;
using Unity.Pipeline.Commands;
using UnityEngine;

public static class ProjectStatusTool
{
    [Serializable]
    public sealed class Output
    {
        public string label;
        public string productName;
        public string unityVersion;
        public bool isPlaying;
    }

    [CliGroupCommand(
        "project",
        "Report basic Unity project status as a sample custom CLI extension command.",
        Name = "status",
        Effect = CommandEffect.ReadOnly,
        Tags = new[] { "project", "sample" })]
    public static ExitCode Status(
        CommandContext cmd,
        [CliArg("label", "Optional label included in the status output.", DefaultValue = "project")]
        string label = "project")
    {
        cmd.Result.Write(new Output
        {
            label = string.IsNullOrWhiteSpace(label) ? "project" : label,
            productName = Application.productName,
            unityVersion = Application.unityVersion,
            isPlaying = Application.isPlaying
        });
        return ExitCode.Success;
    }
}
