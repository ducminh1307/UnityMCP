# Roadmap and acceptance gates

The canonical inventory is [tool-catalog.json](tool-catalog.json). Its 187 unique
IDs are grouped into 20 categories whose declared counts sum to 187. The first
implementation baseline is exactly 48 contracts, of which 20 built-in safe-read
tools are default-enabled. `python tools/validate_catalog.py` locks those sets and
fails on count drift, duplicates, invalid metadata, or accidental enablement.

`implemented` in the catalog is a v1 delivery target, not permission for Python
to synthesize an absent tool. Unity's live registry remains authoritative.

## Delivery waves

1. **Foundation and core 48:** descriptor discovery, stdio and loopback
   Streamable HTTP, authenticated Unity bridge, immutable registry snapshots,
   JSON-Schema validation, 20 safe reads, 20 opt-in mutations, custom scaffold
   and validation, common jobs, four runtime-oriented contracts, permission UI,
   and runtime manifest generation.
2. **Authoring workflows:** tests/build, remaining prefab and material tools,
   screenshots, UI, animation/timeline, audio, and input.
3. **Diagnostics:** profiler, memory, frame debugging, visual QA, and complexity
   analysis with strict output and lifetime bounds.
4. **Optional package packs:** Addressables, Localization, Terrain/2D, Cinemachine,
   Navigation, Visual Effect Graph, ProBuilder, and other dependency-gated tools.
5. **Unsafe capabilities:** reflection invocation, C# execution, package/build
   mutation, input simulation, and broad automation only after explicit threat
   review and granular policy controls.

## Test matrix

### Python gateway

- Descriptor validation, stale and ambiguous instance discovery, secret redaction.
- Deterministic registry ordering, ETag/304 caching, atomic swap, reload grace,
  modern and legacy tool-list-changed behavior.
- Exact input/output schema validation, error mapping, result content, jobs,
  cancellation, timeouts, response limits, and percent-encoded IDs.
- Real stdio subprocess and loopback Streamable HTTP sessions using MCP Inspector.

### Unity package

- EditMode tests for attribute discovery, duplicate/reserved names, typed schema
  generation, schema hashing, enabled-state policy, main-thread dispatch, dry-run,
  Undo grouping, job lifecycle, and audit redaction.
- Custom tool add/update/remove across domain reload; invalid signatures and schema
  providers remain visible only in the diagnostic catalog.
- Runtime tests on Windows, macOS, and Linux Development Players with Mono and
  IL2CPP preservation manifests.
- Production builds verify no listener starts and no descriptor is written.

### Security and interoperability

- Wrong/missing token, non-loopback peer, forged Host, stale revision, disabled or
  out-of-scope tool, path traversal/junction escape, oversized body/schema/result,
  queue pressure, timeout, cancellation, and malicious exception/output cases.
- Editor and Player run simultaneously with independent gateways and job spaces.
- CI compares documented IDs/status with registered implementation metadata and
  fails on schema or catalog drift.

## v1 acceptance

- A fresh project advertises exactly the available subset of the 20 safe-profile
  tools and no planned, disabled, invalid, or wrong-scope contract.
- A scaffolded custom tool compiles, appears disabled, is enabled by the user, then
  reaches clients through list-changed and executes without Python edits.
- Editor and Player never see tools outside their scope; runtime custom changes
  require a rebuild and production Players expose no bridge.
- Both MCP transports work without Node.js; all listeners are loopback-only and
  telemetry remains disabled.
- Catalog validation reports 187 tools, 48 implemented targets, and 20
  default-enabled tools; implementation/schema drift fails CI.
