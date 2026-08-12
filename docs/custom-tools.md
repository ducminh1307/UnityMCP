# Project custom tools

Custom tools are static C# methods discovered and described by Unity. They are
first-class MCP tools: after compilation and local enablement they appear directly
in `tools/list`, and no Python registration or project Python plug-in is required.

## Minimal typed tool

```csharp
using DucMinh.UnityMcp;

public sealed class SpawnEnemyArgs
{
    public string enemyType;
    public bool apply;
}

public sealed class SpawnEnemyResult
{
    public int created;
    public string objectPath;
}

public static class ProjectEnemyTools
{
    [UnityMcpTool(
        "project-enemy-spawn",
        Description = "Spawn a project enemy.",
        Category = "project",
        Scope = UnityMcpScope.Editor | UnityMcpScope.Runtime,
        Safety = UnityMcpSafety.Write,
        SupportsDryRun = true)]
    public static UnityMcpResult<SpawnEnemyResult> SpawnEnemy(
        SpawnEnemyArgs input,
        UnityMcpContext context)
    {
        if (context.DryRun || !input.apply)
            return new UnityMcpResult<SpawnEnemyResult>
            {
                structuredContent = new SpawnEnemyResult { created = 0 },
                message = "Preview only."
            };

        // Project implementation goes here.
        return new UnityMcpResult<SpawnEnemyResult>
        {
            structuredContent = new SpawnEnemyResult { created = 1 }
        };
    }
}
```

The method must be public, static, non-generic, and have one supported input DTO
plus an optional `UnityMcpContext`. Tool names are globally unique kebab-case IDs.
Duplicate/reserved names, invalid signatures, or schemas beyond configured limits
are reported by Unity diagnostics or `custom-tool-validate` and are never
advertised. Descriptors that reach the gateway but fail its stricter schema
policy are quarantined individually in `unity://tools`.

Supported typed shapes include primitive values, enums, nullable values,
arrays/lists, nested DTOs, and supported Unity value types such as vectors and
colors. Arbitrary `JObject` input is rejected unless `SchemaProvider` supplies a
deterministic schema. Return values may be synchronous, `Task<T>`,
`UnityMcpResult`, `UnityMcpResult<T>`, or a declared job handle. Cancellable tools
receive `context.CancellationToken` and must cooperate with cancellation.

## Discovery and enablement

Editor discovery uses `TypeCache`. Runtime discovery uses a generated manifest
and linker preservation metadata so the selected methods survive IL2CPP stripping.
A runtime custom tool must exist before the Development Player is built; adding or
changing it requires rebuilding that Player.

Every newly discovered custom tool starts disabled, regardless of its safety
attribute. The user enables it in the UnityMCP Editor window for the current user
and project. The registry revision changes and connected MCP clients receive a
tool-list-changed notification. A tool cannot enable itself and neither the
gateway nor an MCP client may change the local profile.

Runtime enablement comes from a profile explicitly selected and baked into the
Development Build. The bridge is not compiled or started in a production build.

## Scaffolding workflow

The disabled `custom-tool-scaffold` built-in accepts a structured specification
and generates a C# skeleton under one of:

- `Assets/UnityMCP/CustomTools/Editor/`
- `Assets/UnityMCP/CustomTools/Runtime/`

The destination is selected from requested scope and is path-contained. Scaffold
does not invent business logic or enable the result. The intended workflow is:

1. preview the generated paths and declarations (`apply: false`);
2. explicitly apply the scaffold;
3. implement project behavior and compile;
4. call `custom-tool-validate` or inspect Unity diagnostics;
5. review the generated JSON Schemas and safety declaration;
6. enable the tool locally in the Editor window; and
7. rebuild a Player if runtime support changed.

Changing a public DTO or return type changes `schemaHash` and registry revision.
Clients must refresh before calling the new contract.

## Design requirements

- Read tools must not mutate Unity state and may use `SafeRead` only after review.
- Write/destructive tools should support dry-run where a meaningful preview exists.
- Editor scene mutations use Unity Undo APIs and a named Undo group.
- Asset mutations return a change journal. Do not claim rollback for Unity asset
  operations that cannot be made transactional.
- Validate all paths against explicit project roots; never accept arbitrary absolute
  paths from arguments.
- Bound collection sizes, recursion depth, output size, and execution time.
- Put secrets neither in schemas/results nor in exception messages.
