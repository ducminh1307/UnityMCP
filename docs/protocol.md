# Unity bridge protocol v1

The Python gateway talks to one Unity process over HTTP/1.1 on loopback. This is
an internal versioned API, not an MCP endpoint. JSON is UTF-8. Path variables are
percent-encoded as individual URL segments.

## Authentication and common rules

Every request sends:

```http
Authorization: Bearer <descriptor token>
Host: 127.0.0.1:<descriptor port>
Accept: application/json
Content-Type: application/json
```

`Content-Type` is omitted when a request has no body. Unity rejects missing or
invalid bearer tokens, non-loopback peers, and unexpected Host values. Tokens
are never returned in MCP resources, responses, exceptions, or logs.

Request bodies are limited to 4 MiB. The gateway accepts at most 4 MiB for a tool
registry and 16 MiB for a tool or job response. Implementations may enforce lower
per-tool limits declared by policy.

## Instance descriptor

```json
{
  "port": 45123,
  "token": "random-secret-with-at-least-32-visible-characters",
  "pid": 12345,
  "projectId": "stable-project-id",
  "instanceId": "unique-process-instance-id",
  "kind": "editor",
  "buildId": "package-or-player-build-id"
}
```

`kind` is `editor` or `player`. Port is 1–65535; PID is positive. `instanceId`
is unique per process lifetime. The gateway publishes the descriptor without
`token` as the `unity://instance` resource.

## Health

`GET /api/v1/health` returns `200` and bridge state:

```json
{
  "status": "ok",
  "registryRevision": "42",
  "instance": {
    "port": 45123,
    "pid": 12345,
    "projectId": "stable-project-id",
    "instanceId": "unique-process-instance-id",
    "kind": "editor",
    "buildId": "package-or-player-build-id"
  }
}
```

The response is used to reject stale descriptors; its instance identity must
match the descriptor. During compilation or reload, the endpoint remains
available and may add a target-state field while retaining the same identity.

## Tool registry

`GET /api/v1/tools` returns:

```json
{
  "registryRevision": "42",
  "tools": [
    {
      "name": "project-enemy-spawn",
      "title": "Spawn project enemy",
      "description": "Spawn a project enemy.",
      "category": "project",
      "scopes": ["editor", "runtime"],
      "inputSchema": {"type": "object", "properties": {}},
      "outputSchema": {"type": "object", "properties": {}},
      "source": "project",
      "schemaHash": "sha256-hex-or-registry-hash",
      "safety": "write",
      "annotations": {},
      "mainThread": true,
      "supportsDryRun": true,
      "supportsCancel": false,
      "returnsJob": false,
      "timeoutMs": 30000,
      "implemented": true,
      "enabled": false,
      "valid": true,
      "status": "implemented",
      "packageDependency": null,
      "schemaRevision": 1
    }
  ]
}
```

The bridge returns a strong or weak `ETag`. A later request sends
`If-None-Match`; an unchanged registry returns `304` without a body. Ordering is
deterministic by tool name. Names are unique and use kebab-case. Safety is one of
`safe-read`, `write`, `destructive`, or `unsafe`; scope values are `editor` and
`runtime`. `schemaHash` changes whenever input or output schema changes.

`unity://tools` exposes the complete validated Unity catalog, including disabled
entries. MCP `tools/list` exposes only advertisable entries and preserves Unity's
input/output schemas.

## Invoke a tool

`POST /api/v1/tools/{name}/call` receives:

```json
{
  "arguments": {"enemyType": "grunt", "apply": false},
  "registryRevision": "42"
}
```

The revision prevents executing against a schema different from the one the MCP
client saw. A stale revision returns a typed retryable conflict; the gateway
refreshes the registry before a later call. The bridge rejects disabled,
unimplemented, invalid, or out-of-scope tools even if a caller bypasses Python.

Successful synchronous result:

```json
{
  "content": [{"type": "text", "text": "Preview complete"}],
  "structuredContent": {"wouldCreate": 1},
  "isError": false,
  "meta": {"dryRun": true}
}
```

Asynchronous result:

```json
{
  "content": [{"type": "text", "text": "Job started"}],
  "structuredContent": {"jobId": "job-123", "status": "queued"},
  "isError": false,
  "jobId": "job-123"
}
```

The gateway validates `arguments` before the call and `structuredContent` after
the call using the advertised JSON Schemas. Validation errors are reported as
tool errors rather than executing with partial or coerced input.

## Jobs

- `GET /api/v1/jobs/{jobId}` reads a job owned by this instance.
- `DELETE /api/v1/jobs/{jobId}` requests cancellation. Unsupported or already
  terminal jobs return a typed conflict rather than claiming cancellation.

Job payloads contain at least `jobId` and `status`; terminal states include a
normal result or sanitized error. The MCP resources use
`unity://jobs/{percent-encoded-jobId}`. Job IDs are opaque and never portable
between instances.

## Errors and HTTP mapping

Canonical bridge errors use the same `UnityMcpResult` envelope as tool calls,
with a stable code in `meta.errorCode`:

```json
{
  "content": [{"type": "text", "text": "Registry revision is stale; refresh tools/list before retrying."}],
  "isError": true,
  "meta": {"errorCode": "stale_registry"}
}
```

Use `400` for malformed/schema-invalid requests, `401` for authentication,
`403` for disabled or policy-denied tools, `404` for unknown tools/jobs, `409`
for target state or revision conflicts, `413` for size limits, `429` for bounded
queue pressure, `500` for sanitized execution failures, and `503` for reloading
or unavailable Unity state. Stack traces and filesystem secrets stay in the
redacted local audit log and are not sent to MCP clients.
