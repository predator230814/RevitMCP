# BRIDGE-0023: C# query policy version 2 protocol

- Status: Implemented. Protocol 27 is the current transport for CAP-0020 policy version 2.
- Date: 2026-11-27
- Capability: [CAP-0020](../capabilities/CAP-0020-bounded-generated-csharp-read.md)
- Architecture: [ADR-0028](../adr/ADR-0028-bounded-generated-csharp-read-executor.md)
- Historical transport: [BRIDGE-0022](BRIDGE-0022-bounded-csharp-query-rpc.md)

## Why the protocol changes

BRIDGE-0001 requires a new protocol version when a newer Server requires a guarantee that an older Addin does not provide under the same protocol version. Policy version 2 is that guarantee. A policy-v2 Server must not negotiate protocol 26 with a policy-v1 Addin and then execute `revit.run_csharp_query` under policy version 1.

Rejecting the result after execution is not the control. The peers must fail closed before the call.

## RPC

`revit.run_csharp_query`

On a current binary, support is the explicit set `{27}`. `SupportsRunCSharpQuery` is true only for 27. Numeric `version >= N` checks are not used. Protocol 26 remains the historical policy version 1 transport in BRIDGE-0022. Current binaries do not advertise it and do not treat it as permission to run the query.

## Advertisement

A full current host advertises `[27, 25, 24, ..., 1]` only when `IRevitRunCSharpQueryService` is composed with the complete protocol-25 prefix. The set skips 26. A complete v25 host without that service still advertises `[25..1]`.

The historical policy version 1 set `[26, 25, ..., 1]` stays named `RunCSharpQueryVersions`. It is not `SupportedVersions`.

Negotiation is the highest version present in both sets:

| Server versions | Addin versions | Selected | `revit.run_csharp_query` |
| --- | --- | --- | --- |
| current `[27, 25, ..., 1]` | historical `[26, 25, ..., 1]` | 25 | unavailable before execution |
| historical `[26, 25, ..., 1]` | current `[27, 25, ..., 1]` | 25 | unavailable before execution |
| historical pair | historical pair | 26 | policy version 1 behavior of that pair |
| current pair | current pair | 27 | policy version 2 |

`addin_version` is not an authorization input. Unknown protocol 28 supports nothing.

## What protocol 27 preserves

Protocol 27 preserves every protocol-25 capability by explicit membership, including predicate-bearing `revit.query_elements`. It does not add an MCP tool, a Revit capability, or a Bridge method. Production `tools/list` stays 23. The local activation schema stays version 1 and default-off. Advertisement is method availability, not the local enablement state.

The 2026-11-26 Snowdon gate and the 2026-11-27 electrical-local dogfood negotiated protocol 26. Those records keep that fact. They are not rewritten as protocol 27.
