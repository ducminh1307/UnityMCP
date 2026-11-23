# Project custom commands

Custom commands are static C# methods annotated with `[CliGroupCommand]` or
`[CliCommand]` from `Unity.Pipeline.Commands` (`Unity.Pipeline.Attributes`).
`CommandRegistry` discovers them automatically on compilation in both the Unity
Editor (`TypeCache`) and Runtime Development Builds (reflection), exposing them
immediately to `unity command` and `unity mcp`.

## Minimal typed command

```csharp
using System;
using Unity.Pipeline.Commands;
using UnityEngine;

public static class ProjectStatusTool
{
    private const string Group = "project";

    [Serializable]
    public sealed class Output
    {
        public string label;
        public string productName;
        public string unityVersion;
        public bool isPlaying;
    }

    [CliGroupCommand(
        Group,
        "Report basic Unity project status.",
        Name = "status",
        Effect = CommandEffect.ReadOnly,
        Tags = new[] { "project" })]
    internal static ExitCode Status(
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
```

See the full sample in `Packages/com.ducminh.unity-mcp/Samples~/CustomTool/ProjectStatusTool.cs`.

Invoke after Unity finishes compiling:

```bash
unity command project status --label smoke
```

## Authoring conventions and rules

1. **Method signature**:
   - Declare a `static` method taking `CommandContext cmd` first and returning
     `ExitCode` (or `Task<ExitCode>` for multi-frame async commands using
     `await Task.Yield()` and observing `cmd.CancellationToken`).
   - Write single payloads with `cmd.Result.Write(obj)` or stream list items with
     `cmd.Result.WriteRecord(item)` (never both). Log errors before returning
     non-zero `ExitCode` values (`InvalidUsage`, `NotFound`, `Failed`, `TimedOut`,
     `Cancelled`).
2. **Parameters (`[CliArg]`)**:
   - Use kebab-case parameter names (`--target-path`, `--dry-run`, `--confirm`).
   - Declare required parameters before optional parameters.
   - For multi-field nested inputs, implement `IStructuredCommandInput` (from
     `Unity.Pipeline`) on a DTO class and annotate its properties with `[CliArg]`.
   - **Never** name a `[CliArg]` `format`, `json`, `fields`, `verbose`, or
     `omit-nulls` — those are reserved by the Pipeline executor.
3. **CommandEffect and safety**:
   - Always set `Effect`: `ReadOnly`, `Idempotent`, `Additive`, or `Destructive`.
   - Mutating commands should expose `[CliArg("dry-run", ...)] bool dryRun = false`
     to validate and preview changes without mutating state when `--dry-run true`.
   - Destructive or overwriting commands should set `Effect = CommandEffect.Destructive`
     and require `[CliArg("confirm", ...)] bool confirm = false` unless `dryRun` is `true`.
   - Wrap Editor scene/object mutations in `using (new AuthoringUndoScope("..."))`
     and confine caller-supplied asset paths with `ProjectPaths.Resolve(...)`
     (both in `Unity.Pipeline.Editor.Authoring`).
4. **Group naming & non-collision**:
   - Pick a project-specific group (for example `project` or `mygame`).
   - Never reuse `com.unity.pipeline`'s 23 built-in groups (`animation`, `asset`,
     `authoring`, `build`, `capture`, `component`, `console`, `diagnostics`,
     `editor`, `gameobject`, `job`, `lighting`, `material`, `navmesh`,
     `occlusion`, `package`, `pipeline`, `prefab`, `runtime`, `scene`, `script`,
     `settings`, `test`) or `com.ducminh.unity-mcp`'s 19 extension groups.
5. **Runtime vs Editor assemblies**:
   - Runtime command assemblies should reference **only** `Unity.Pipeline.Attributes`
     (never `Unity.Pipeline` or `UnityEditor`) so non-development Player builds
     compile cleanly when Runtime Pipeline is disabled.
   - Tag runtime-capable commands with `Tags = new[] { "runtime", ... }`.
   - Editor-only commands live in an `"includePlatforms": ["Editor"]` assembly
     and may reference `Unity.Pipeline` and `Unity.Pipeline.Editor` (or gate
     optionally with a `com.unity.pipeline` `versionDefines` constraint).
