# ADR-0027: Layered Revit capability admission

- Status: Accepted
- Date: 2026-11-24
- Accepted: 2026-11-24 by Dave, Product Owner
- Product Owner: Dave
- Contract: [CAP-0019](../capabilities/CAP-0019-revit-api-symbol-discovery.md)

Accepted architecture. This ADR records the order in which Revit behavior is admitted so the same strategy does not have to be rediscovered. It does not specify an executor. [ADR-0028](ADR-0028-bounded-generated-csharp-read-executor.md) is the later read-executor authorization this ADR allowed.

## Context

[ADR-0022](ADR-0022-agent-skills-and-knowledge-architecture.md) separates MCP tools, Skills, and the Revit execution kernel. A Skill over generic capabilities is preferred when the workflow can be expressed by orchestration.

[ADR-0023](ADR-0023-declarative-revit-read-query-architecture.md) §1 prohibits model-supplied Python, C#, Dynamo, macro, expression, or Revit API source in the production capability layer. [ADR-0028](ADR-0028-bounded-generated-csharp-read-executor.md) is the narrow exception for CAP-0020. The prohibition remains in force everywhere else.

[ADR-0024](ADR-0024-registry-driven-compiled-revit-read-operations.md) keeps new compiled reads behind `revit_lookup` and `revit_read` instead of adding one MCP tool per fact.

[ADR-0025](ADR-0025-bounded-revit-api-property-access.md) accepted bounded property reflection (`revit_api.metadata` v1 and `revit_api.element_properties` v1). Flexible or generated C# stays Research Only there.

The isolated C# query-runtime spike, preserved by PR #131 and recorded in [FINDINGS.md](../../spikes/BimIt.Revit.QueryRuntimeSpike/FINDINGS.md), concluded that a bounded compiler is viable with constraints. That spike is research evidence. It does not authorize production execution. Its host probe was not a Revit/EXEC-0001 run, and the mixed-mode Revit API assembly cannot execute in the ordinary test host.

## Decision

Revit behavior is admitted in this order:

1. Existing generic MCP primitives first.
2. A Skill plus those generic primitives when the workflow can be expressed by orchestration.
3. Bounded flexible C# is the intended future long-tail mechanism when ordinary primitives are insufficient.
4. Compiled specialized capabilities are reserved for stable engineering semantics, geometry or topology, special identity, specialized performance, controlled writes, or other cases that genuinely justify trusted product code.

Do not rebuild the Revit API method by method as MCP tools or compiled read operations.

CAP-0019 `revit_api.symbols` v1 is a descriptive read of the Revit API assembly already loaded by the connected Revit process. It is registered behind the existing `revit_lookup` and `revit_read` tools. It exists so a model can check live signatures instead of guessing them. It does not accept source, compile source, emit or load IL, invoke members, read model elements, or mutate Revit.

## What this ADR does not authorize

- ADR-0023 §1 still prohibits model-supplied code outside CAP-0020. ADR-0028 is the exception, and only for that capability.
- ADR-0025 flexible or generated C# remains Research Only as a property-access mechanism.
- This ADR does not specify the executor. ADR-0028 does, for one read-only surface. The EXEC-0001 gate is PR #133.
- CAP-0019 added no MCP tool, Bridge RPC, or protocol version. ADR-0028 plans `revit_run_csharp_query` and protocol 26. Neither is implemented. Production `tools/list` stays 22. Bridge protocol stays 25.

## Implementation note (2026-11-25)

This note does not change the admission order. On 2026-11-24 step 3 was still future, and `revit_run_csharp_query` was not implemented. [CAP-0020](../capabilities/CAP-0020-bounded-generated-csharp-read.md) is now that step: live-complete, default-off, and limited to policy version 1 on loaded `RevitAPI` 26.5.0.0. Evaluate it when a required read-only long-tail fact is not expressible by an existing primitive or by composition, and the query is compatible with [ADR-0028](ADR-0028-bounded-generated-csharp-read-executor.md). Skip it when the fact is unnecessary or outside that policy. Production `tools/list` is 23. Bridge protocol is 26. The sentences above that say otherwise are the acceptance-time facts. ADR-0025 generated C# remains Research Only as a property-access mechanism.

## Consequences

- New Revit work starts by asking whether an existing primitive or a Skill already expresses the workflow.
- A compiled operation is a deliberate exception with a stated reason from item 4, not the default shape of API coverage.
- Symbol discovery is available as `revit_api.symbols` v1 ([CAP-0019](../capabilities/CAP-0019-revit-api-symbol-discovery.md)). It describes the loaded API. It is not an execution capability.
- The query-runtime spike remains research evidence under `spikes/`. Production projects do not reference it.
