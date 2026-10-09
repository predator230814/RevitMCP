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
- `BRIDGE-0006-get-parameter-values-capability-rpc.md` — CAP-0005 typed-read RPC (`revit.get_parameter_values`) on bridge protocol version 6; v6 explicitly preserves the v2/v3/v4/v5 capability guarantees. Implemented. Typed Bridge and official MCP live validation on Revit 2026.5 are **PASS**.
- `BRIDGE-0007-get-mep-topology-capability-rpc.md` — CAP-0006 topology RPC (`revit.get_mep_topology`) on bridge protocol version 7; v7 explicitly preserves the v2..v6 capability guarantees. Implemented. Typed Bridge live validation on Revit 2026.5 is **PASS**. Protocol v8, v9, and v10 preserve that RPC.
- `BRIDGE-0008-preview-parameter-updates-capability-rpc.md` — CAP-0007 preview RPC (`revit.preview_parameter_updates`) on bridge protocol version 8. Implemented. Preview support is `{8,9,10}`.
- `BRIDGE-0009-request-parameter-update-review-capability-rpc.md` — implemented request-review RPC (`revit.request_parameter_update_review`) on protocol v9, preserved by v10. Support is `{9,10}`.
- `BRIDGE-0010-apply-parameter-updates-capability-rpc.md` — CAP-0008 apply RPC (`revit.apply_parameter_updates`) on protocol v10. Implemented. A full host advertises `[10,9,8,7,6,5,4,3,2,1]` only when apply is composed with the complete v9 prefix and get-warnings is absent. Protocol v11 is specified and implemented by BRIDGE-0011. Official MCP live functional validation on Revit 2026.5 build `26.5.0.55` at SHA `517bca8ddb8ecf3bab11cb14738ef8e0c9937353` is **PASS**. Induced Revit failure-dialog behavior remains deferred validation debt.
- `BRIDGE-0011-get-warnings-capability-rpc.md` — Implemented. CAP-0009 warnings RPC (`revit.get_warnings`) on bridge protocol version 11. A full host advertises `[11,10,9,8,7,6,5,4,3,2,1]` only when get-warnings is composed with the complete v10 prefix. Unknown v12 is unsupported. Live typed-Bridge validation and official MCP live validation have not been run.

## Principles

- Contracts below the MCP adapter remain independent of any specific LLM or MCP client.
- Bootstrap contracts remain intentionally small and stable.
- Revit model access is not performed merely to establish bridge liveness.
- New protocol surface is added only when a capability or operational requirement justifies it.
- Capability support is tied to explicitly documented protocol-version sets; numeric version ordering alone is never treated as a capability guarantee.
