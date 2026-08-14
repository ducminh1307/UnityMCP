---
name: unity-mcp
description: Proactively use the project-scoped UnityMCP tools for every task or question involving the current Unity project, live Editor state, scenes, GameObjects, components, assets, packages, console, play mode, tests, builds, animation, UI, input, rendering, profiling, or Unity changes and verification. Trigger even when the user does not mention MCP. Prefer UnityMCP over guessing from files whenever live Unity data or an Editor operation is relevant. Do not trigger for tasks unrelated to Unity.
---

# UnityMCP

<!-- UnityMCP managed project skill. -->

Use the UnityMCP server configured for this project without requiring the user to ask for MCP explicitly.

## Workflow

1. Identify the connected project server whose name begins with `unity_` and inspect its available tools.
2. For work that depends on live Editor or project state, call `unity-status` first, then query the narrowest relevant UnityMCP read tools before answering or changing anything.
3. Treat UnityMCP results as the source of truth for open scenes, hierarchy, objects, components, assets, packages, console messages, play mode, jobs, tests, builds, and other live Unity state. Do not infer these from files when an enabled MCP tool can answer.
4. For changes, inspect the target state first, use dry-run behavior when the tool supports it, execute the smallest relevant operation, and verify the result through UnityMCP.
5. For source-code edits, use normal repository tools as needed, then use UnityMCP to check compilation, console state, tests, assets, or scene effects when relevant.
6. Use only tools currently advertised by the server. Never fabricate a result, silently substitute another open Unity project, enable disabled tools, or alter the local UnityMCP enablement profile.
7. If the gateway or required tool is unavailable, state that clearly and use the safest project-file fallback that can still answer the request.

Keep potentially destructive or unsafe operations explicit and scoped. Follow each tool's confirmation, `apply`, dry-run, and safety contract.
