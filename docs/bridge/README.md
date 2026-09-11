# RevitMCP Bridge Specifications

Bridge specifications define versioned technical contracts between `RevitMCP.Server` and `RevitMCP.Addin` that implement accepted architecture decisions.

They are not architecture decision records. ADRs explain why major architectural choices were made; bridge specifications define the concrete transport-neutral contracts needed to implement those choices.

## Naming

Bridge specifications use the form:

```text
BRIDGE-0001-short-name.md
```

## Current specifications

- `BRIDGE-0001-handshake-and-version-negotiation.md` — minimum bootstrap handshake used to validate a discovered Revit instance and select a compatible bridge protocol version.
- `BRIDGE-0002-get-context-capability-rpc.md` — first Revit capability RPC (`revit.get_context`) on bridge protocol version 2, with version 1 handshake compatibility.
- `BRIDGE-0003-query-elements-capability-rpc.md` — CAP-0002 query RPC (`revit.query_elements`) on bridge protocol version 3; v3 explicitly preserves the v2 `revit.get_context` guarantee.
- `BRIDGE-0004-get-elements-capability-rpc.md` — CAP-0003 inspection RPC (`revit.get_elements`) on bridge protocol version 4; v4 explicitly preserves the v2/v3 capability guarantees.
- `BRIDGE-0005-describe-parameters-capability-rpc.md` — CAP-0004 discovery RPC (`revit.describe_parameters`) on bridge protocol version 5; v5 explicitly preserves the v2/v3/v4 capability guarantees.

## Principles

- Contracts below the MCP adapter remain independent of any specific LLM or MCP client.
- Bootstrap contracts remain intentionally small and stable.
- Revit model access is not performed merely to establish bridge liveness.
- New protocol surface is added only when a capability or operational requirement justifies it.
- Capability support is tied to explicitly documented protocol-version sets; numeric version ordering alone is never treated as a capability guarantee.
