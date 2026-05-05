---
title: API, CLI, And MCP
nav_order: 6.5
---

# API, CLI, And MCP

Rusty XR uses three related surfaces:

```text
API
  reusable contracts, schemas, service methods, and broker endpoints
CLI
  human and scriptable commands over those operations
MCP
  AI-agent-facing tools that wrap the same operations with explicit safety rules
```

The Windows Companion now exposes the first shared operation catalog:

```powershell
dotnet run --project src\RustyXr.Companion.Cli -- api surface
dotnet run --project src\RustyXr.Companion.Cli -- api surface --json
dotnet run --project src\RustyXr.Companion.Cli -- api surface --mcp-tools
```

It also exposes a dispatch planner. The planner returns the command that would
run for a known operation, plus the safety gate state. It does not execute the
command:

```powershell
dotnet run --project src\RustyXr.Companion.Cli -- api plan --operation broker.status --host 127.0.0.1 --port 8765
dotnet run --project src\RustyXr.Companion.Cli -- api plan --operation catalog.verify --path .\catalog.json --app rusty-xr-quest-broker --serial ABC123 --install --json
dotnet run --project src\RustyXr.Companion.Cli -- api plan --operation catalog.verify --path .\catalog.json --app rusty-xr-quest-broker --serial ABC123 --install --allow-side-effects --json
dotnet run --project src\RustyXr.Companion.Cli -- api plan --operation broker.h264_proxy_probe --serial ABC123 --packetCount 4 --json
dotnet run --project src\RustyXr.Companion.Cli -- api plan --operation broker.h264_proxy_start --remoteHost 192.168.1.25 --remotePort 8879 --json
```

Filter by owner when an agent needs only one lane:

```powershell
dotnet run --project src\RustyXr.Companion.Cli -- api surface --owner windows --mcp-tools
dotnet run --project src\RustyXr.Companion.Cli -- api surface --owner android --mcp-tools
dotnet run --project src\RustyXr.Companion.Cli -- api surface --owner core --mcp-tools
```

## Current Split

| Layer | Owner | Examples |
| --- | --- | --- |
| API contracts | Rusty XR core and Companion core services | catalog schema, workspace status, broker status, diagnostics models |
| CLI | Windows Companion | `doctor`, `devices`, `catalog verify`, `broker status`, `broker compare`, `broker h264-proxy-probe` |
| Phone command API | Android Companion | time-limited `AgentCommandActivity` reports for Quest install, launch, stop, foreground, and Polar PMD smoke checks |
| MCP wrapper | Windows Companion MCP server | generated tool names and input schemas from `api surface --mcp-tools`, read-only calls, and blocked side-effect plans |

The operation catalog is intentionally source-level and dependency-light. It
feeds the MCP server a stable, public-safe tool list and inspectable command
planner instead of making it infer commands from prose help output.

## Safety Rules For MCP Wrappers

- Treat `read-only` and `read-only-device` operations as inspect-only.
- Require explicit user intent before `device-state-changing` operations such
  as install, launch, stop, profile apply, or catalog verification with launch
  enabled.
- Keep `phone-agent-gated` operations behind the Android app's time-limited
  Agent Command Mode.
- Keep generated reports, diagnostics, screenshots, media payloads, and APKs in
  ignored output folders.
- Use catalog ids, device serials, and user-supplied paths from the current
  request or structured config. Do not hard-code private target apps.

## First MCP Server Shape

The first MCP server lives in `src/RustyXr.Companion.Mcp`:

```powershell
dotnet run --project src\RustyXr.Companion.Mcp
```

It is a stdio JSON-RPC server using the MCP `2025-11-25` protocol revision. It
binds tools to the operation catalog and planner:

```text
MCP tools/list
  -> CompanionOperationSurface.ToMcpToolList()

MCP tool call
  -> validate inputs and safety gate
  -> CompanionOperationPlanner.CreatePlan()
  -> execute read-only core operations directly where available
  -> return blocked dispatch plans for side-effecting operations
  -> keep planned CLI or adb commands limited to known operations
```

The initial server executes these read-only calls:

- `rusty_xr_api_surface`
- `rusty_xr_operation_plan`
- `rusty_xr_workspace_guide`
- `rusty_xr_list_devices`
- `rusty_xr_broker_status`
- `rusty_xr_read_catalog`

For install, launch, catalog verification, local-write diagnostics, broker
compare, broker H.264 proxy operations, media inspection, and Android phone
agent commands, the server returns an operation plan marked as blocked instead
of executing the command.

Android phone operations should remain command-activity based until a dedicated
phone-side service API is designed. That keeps PC agents from bypassing the
phone app's visible command gate.

References:

- [MCP tools specification](https://modelcontextprotocol.io/specification/2025-11-25/server/tools)
- [MCP transports specification](https://modelcontextprotocol.io/specification/2025-11-25/basic/transports)
