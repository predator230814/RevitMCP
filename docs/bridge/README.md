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

## Principles

- Contracts below the MCP adapter remain independent of any specific LLM or MCP client.
- Bootstrap contracts remain intentionally small and stable.
- Revit model access is not performed merely to establish bridge liveness.
- New protocol surface is added only when a capability or operational requirement justifies it.
