---
description: Always prioritize UnityMCP tools for any tasks interacting with or querying Unity before falling back to other methods.
always_on: true
---

# UnityMCP Project Rules

<!-- UnityMCP managed project rule. -->

Always prioritize project-scoped UnityMCP tools (`unity_*`) for any task, query, or operation involving Unity before attempting any other method.

## Interaction Guidelines

1. **Prioritize UnityMCP First**:
   - For inspecting or modifying live Unity state, open scenes, hierarchy, GameObjects, components, Inspector values, console logs, compilation status, play mode, tests, assets, or project settings, always invoke the corresponding UnityMCP tool first.
   - Do not guess live state, parse raw serialized `.unity`/`.prefab`/`.asset` files, or write temporary Editor scripts when an enabled UnityMCP tool can perform the action directly.

2. **Source of Truth**:
   - Treat UnityMCP results as the single source of truth for live Editor state, active scenes, hierarchy, console messages, and test results.

3. **Live Verification**:
   - After modifying C# scripts or project assets, verify the effects using UnityMCP tools (such as checking console messages, compilation errors, and running tests).

4. **Fallback Protocol**:
   - **Only** switch to alternative methods (such as reading project source files, inspecting code, or manual instructions) if:
     - The UnityMCP gateway or Editor bridge is not running or unreachable.
     - The required UnityMCP tool is disabled or not implemented.
     - The UnityMCP tool call returns an error.
   - When falling back, explicitly inform the user why the fallback method is being used instead of UnityMCP.

5. **Safe Execution**:
   - Inspect target state first, use dry-run mode (`apply: false`) when supported, and perform the smallest relevant operation before verifying the result.
