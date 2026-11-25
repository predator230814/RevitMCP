# ADR-0025: Bounded Revit API property access

- Status: Accepted
- Date: 2026-11-19
- Accepted: 2026-11-19 by Dave, Product Owner, with the Tech Lead decisions recorded below
- Product Owner: Dave
- Contract: [CAP-0017](../capabilities/CAP-0017-bounded-revit-api-property-access.md)

Accepted architecture. Implemented by `revit_api.metadata` v1 and `revit_api.element_properties` v1. Live Revit validation has not been run. ADR-0023 stays Accepted and unchanged. ADR-0024 stays Accepted and is amended only as §9 describes.

## Context

[ADR-0023](ADR-0023-declarative-revit-read-query-architecture.md) keeps model-supplied Python, C#, Dynamo, and Revit API source out of the production capability layer. [ADR-0024](ADR-0024-registry-driven-compiled-revit-read-operations.md) then stopped the cost of one MCP tool, one Bridge RPC, and one protocol version per factual read. New facts become compiled operations behind `revit_lookup` and `revit_read`. The first production operation is `project.user_worksets` v1 ([CAP-0016](../capabilities/CAP-0016-revit-read-operations.md)).

That registry is still one compiled wrapper per fact. Levels, phases, and design options would each have been another operation. ADR-0024 already names that list as the growth the registry was meant to slow, and it leaves generic reflection execution out of scope. It allows reflection later only as informational API lookup, never as the execution selector, and never through `revit_lookup`.

PR #123 tested a narrower question on Revit 2026.5. One generic collector, `FilteredElementCollector.OfClass(Type)`, and one scalar property projector, with no branch for the benchmark classes, read:

- `Level`: `Name`, `Elevation`
- `Phase`: `Name`
- `DesignOption`: `Name`, `IsPrimary`

The live path was BIM-IT Assistant to BIM-IT MCP only. The record is [VALIDATION.md](../../spikes/BimIt.Revit.ApiAccessSpike/VALIDATION.md). The classification is **A — promising**. Class A is not a production approval. The spike ids (`experimental.revit_api_lookup`, `experimental.revit_api_read`) stay out of the production registry.

The same run showed the limits this ADR keeps:

- one Revit version and one model;
- `Elevation` came back as a raw API number (internal feet). The schedule displayed millimeters. That match is not a rule that every `double` is feet;
- some Element subclasses cannot be collected with `OfClass`;
- a trusted getter is not a sandbox. The spike's `Document.IsModified` check detects a reported change. It does not prevent one.

## Decision drivers

- A long-tail factual read should not require a compiled operation per Revit class when a bounded property read can return it.
- ADR-0023 §1 stays in force for arbitrary and model-supplied code outside CAP-0020.
- ADR-0024 stays the registry. `revit_lookup` stays a lookup of BIM-IT operations. It does not become a Revit API browser.
- No new MCP tool, no new Bridge RPC, no protocol bump.
- The only executable member is a public instance property getter on the Revit API assembly already loaded by the process.
- No methods, no generated C#, no generic write.
- Numbers stay raw. BIM-IT does not invent a unit for a `double`.
- Opaque identity stays on ADR-0006. This ADR does not create a second identity scheme.
- Results stay bounded (ADR-0005) and reuse the CAP-0016 cursor, revision, and 120,000-character budget.

## Options considered

### Option A: Keep writing one compiled operation per fact

This is ADR-0024 as accepted. It is safe and already live for worksets.

- Advantages: every reader is reviewed source. No reflection.
- Disadvantages: levels, phases, design options, and the next hundred scalar facts each cost a wrapper. The Class A spike says that cost is not required for this class of fact.

### Option B: Bounded reflection of trusted Revit API property getters, registered as two operations

Two compiled operations join the ADR-0024 registry. One describes Element types and scalar properties. One collects instances with `OfClass` and invokes only the requested getters. The agent discovers them with `revit_lookup` and runs them with `revit_read`.

- Advantages: one implementation covers the benchmark classes and later scalar facts. The MCP surface stays fixed. Methods stay unexposed and unexecutable.
- Disadvantages: reflection is not a security boundary. A getter can do more than return a value. `OfClass` does not cover every Element subclass. Units are not known generically. Agents lose per-class static typing, which ADR-0024 already accepted for registered operations.

### Option C: Generated or flexible C#

A later research path for joins, traversal, and calculations. It is not required for the scalar facts the spike read.

- Advantages: reaches APIs that a property list cannot express.
- Disadvantages: it is model-shaped code execution, which ADR-0023 §1 rejects for production. The Class A result removes the reason to adopt it for basic scalar Element facts.

### Chosen direction

**Option B is accepted.** Implementation is a separate change. Option C stays Research Only.

## Decision

### 1. Two registered operations, no new tool

When implemented, the production registry contains the approved compiled operations, including `project.user_worksets` v1, plus:

| Operation | Version | Role |
|---|---:|---|
| `revit_api.metadata` | 1 | Discover Element subclasses and the scalar-read policy for their properties |
| `revit_api.element_properties` | 1 | Read requested scalar properties of instances of one concrete Element subclass |

These ids replace the spike ids only after a separate implementation is approved. A normal build does not register the spike ids.

Both operations are `effects: read`, `context: document`, `pagination: cursor`. They are discovered through `revit_lookup` and executed through `revit_read` on Bridge protocol 24. `revit_lookup` still lists BIM-IT operations only. API discovery is `revit_api.metadata`, not `revit_lookup`.

There is no new MCP tool, no new Bridge RPC, and no protocol bump. `tools/list` stays the production 22 until that implementation lands, and this ADR does not change that list by itself.

### 2. Trusted assembly

The only reflection source is the `RevitAPI.dll` already loaded in the current Revit process, anchored by `typeof(Element).Assembly`.

The caller cannot name an assembly. BIM-IT does not call `Assembly.Load`, does not reflect `RevitAPIUI`, and does not reflect an external or plugin assembly.

### 3. What may execute

The only member invocation this ADR permits is a property getter that is all of the following:

- public;
- instance;
- zero-argument;
- declared by a type in the trusted assembly;
- declared to return a scalar the contract accepts.

A property that also has a setter may be read. `Element.Name` is the reason: rejecting every writable property would make the benchmark facts unreadable. The setter is never invoked.

Rejected, and never invoked:

- methods, constructors, and events;
- setters and indexers;
- static properties and delegates;
- nested member access and caller expressions;
- reflection outside the trusted assembly;
- source in C#, Python, Dynamo, or any script.

No general reflection executor is authorized. Metadata v1 does not list or search methods. The spike showed that method metadata is not required for this reader, and publishing it spends tokens and invites a later widening. Search does cover public instance property names, so a word such as a property name can discover the owning Element type. Exact `type_name` mode remains the way to read the full property list. The match rule is plural equivalence in CAP-0017, not a stemmer and not a per-class alias table.

### 4. Collection

`revit_api.element_properties` collects with `FilteredElementCollector(document).OfClass(resolvedType)` and with no other class-specific collector, filter, or fallback.

Autodesk documents that `OfClass` applies an `ElementClassFilter`, and that a small subset of Element subclasses exist in the API but not in Revit's native object model. Those types throw `ArgumentException` from `OfClass`. The 2026 `ElementClassFilter` remarks name that subset, including subclasses of `Material`, `CurveElement`, `ConnectorElement`, and `HostedSweep`, and types such as `Room`, `Space`, and `Area`. v1 returns a deterministic unsupported-type failure for them. It does not add `RoomFilter`, `SpaceFilter`, or any other dedicated filter to widen coverage. It does not keep a handwritten allow-list of those types. The failure is the exception `OfClass` raises, so a later Revit build is covered by the same path.

`OfClass` also returns instances of derived classes. v1 does not label each row with its runtime type.

### 5. Scalar values and units

The contract accepts `string`, `boolean`, signed in-range integral values, enum names, finite `float` and `double`, and `null`.

A numeric property is a raw Revit API number. The reader must not infer a physical unit from a `double`. The response and the metadata must not assert a unit unless a future mechanism supplies one from an authoritative source. The `Level.Elevation` result is the motivating example, not a universal `double = feet` rule.

### 6. Mutation boundary

The operation is read-only. Reflection is not a security boundary, and this ADR does not describe a sandbox.

Trust model:

- only getters on Autodesk's already-loaded Revit API assembly are invoked;
- BIM-IT opens no `Transaction`, `SubTransaction`, or `TransactionGroup`;
- before `revit_api.element_properties` collects or invokes getters, `Document.IsModifiable` must be false;
- if it is true, the operation fails immediately, invokes no getter, and does not wait, retry, or queue;
- it must still be false when the operation completes;
- a getter failure or a safety failure returns no partial result;
- CAP-0016 document revision and cursor rules still apply.

Autodesk documents that a model change requires an open transaction, and that an attempt to modify a document that is not modifiable throws `ModificationOutsideTransactionException`. `Document.IsModifiable` is not a permanent file state. It can be true while a transaction is open. It can also be false during an open transaction, including regeneration and failure processing. **`IsModifiable == false` is not proof that no Revit transaction exists.** The check is an additional mutation guard. It is not a universal transaction detector, and it is not a sandbox. It does not prove that a getter has no external or in-process side effect.

`Document.IsModified` may be recorded as a diagnostic. It is not the pass/fail boundary.

### 7. Identity

Every `revit_api.element_properties` row includes `element_ref`.

The value uses the existing ADR-0006 semantics and the same BIM-IT identity path element-query capabilities already use. It is opaque and meaningful only with the returned `document_id`. This ADR does not create another encoder or another identity format.

The row does not return a raw `ElementId`. It does not return `UniqueId` as its own field. Ordering may use that same stable Revit identity internally.

`element_ref` is correct identity for capabilities that already accept an ADR-0006 reference. It does not make every existing BIM-IT tool able to read every Element subclass.

### 8. Admission rule

Before adding a compiled factual read operation, ask whether the fact can be obtained safely through `revit_api.element_properties`.

Use a compiled operation, or a dedicated tool under ADR-0024 §6, only when the need involves at least one of:

- BIM-semantic aggregation;
- joins or relationships;
- geometry or topology;
- special identity or chaining semantics;
- performance that needs specialized access;
- a source the generic property reader cannot reach safely, including types `OfClass` rejects;
- a controlled write.

That is the anti-wrapper rule. A scalar name on an Element subclass that `OfClass` accepts is not, by itself, a reason for a new operation.

### 9. Relationship to ADR-0023 and ADR-0024

- **ADR-0023 remains correct for arbitrary and model-supplied code.** This ADR does not supersede ADR-0023. [ADR-0028](ADR-0028-bounded-generated-csharp-read-executor.md) later narrows §1 for CAP-0020 only. Generated C# is still not the property-access mechanism of this ADR. Python, Dynamo, expressions, and Revit API source outside CAP-0020 stay prohibited.
- **ADR-0024 remains the compiled registry.** Operations are still explicit, versioned, schema-checked, and executed only through `revit_read`. `revit_lookup` remains a registry lookup. ADR-0024 §4 said reflection must not be the execution selector and must not be reached through `revit_lookup`. This ADR does not relax that sentence for arbitrary members. It adds two registered operations whose compiled code may reflect one trusted assembly and invoke the getters named above. The agent still selects a registered operation id, not a method.
- **ADR-0025 narrowly permits that getter reflection as a new read mechanism.** That is the only amendment to ADR-0024. It does not amend ADR-0023 §1.
- **Not authorized:** arbitrary API method execution, generic writes, generated C#, arbitrary assemblies, `RevitAPIUI` reflection, and class-specific fallback collectors.

The Class A spike is the evidence for this narrow change. The accepted decisions below are the Tech Lead record.

### 10. Generated C#

Generated or flexible C# stays Research Only as a substitute for bounded property reads. The Class A result means it is not needed for basic scalar Element facts. [ADR-0028](ADR-0028-bounded-generated-csharp-read-executor.md) is the later read-executor ADR. This ADR does not implement that executor and does not authorize property access through generated C#.

## Consequences

### Positive

- Scalar Element facts stop requiring one wrapper per class.
- The MCP catalogue, the Bridge protocol, and the Server stay as CAP-0016 left them.
- Agents can discover an Element type from its simple name or from a public instance property name, using the plural-equivalence rule in CAP-0017. There is no per-class alias table.
- Each property row carries an ADR-0006 `element_ref` for capabilities that already accept one.
- Method metadata is not part of the v1 surface.

### Negative / costs

- Getter purity is still unproven. The `IsModifiable` guard is an additional mutation guard. It is not a sandbox, and `false` does not prove that no transaction is open.
- `OfClass` coverage is incomplete by Autodesk's own contract. Those types need a compiled reader later, or they stay unavailable.
- Numeric unit is not part of the generic result. `float` and `double` stay raw API numbers. Agents can misread one if a Skill or a prompt invents a unit.
- `element_ref` does not extend every existing tool to every Element subclass. A later call succeeds only when that capability already supports the element.
- Search can return more than the type the person meant, including every type that exposes a matched property name. The 200-type cap and the exact `type_name` step are the control.
- A read fails immediately while `Document.IsModifiable` is true. It does not wait, retry, or queue.

## Accepted decisions

Accepted on 2026-11-19. These are the Tech Lead decisions. The caps are v1 safety and performance limits, not permanent product constants.

1. **`Document.IsModifiable`.** Fail closed immediately when the flag is true before collection. Do not wait, retry, or queue. BIM-IT opens no transaction. The flag must remain false through completion. `false` is an additional mutation guard. It is not proof that no transaction exists, not a universal transaction detector, and not a sandbox.
2. **`element_ref`.** Every `revit_api.element_properties` row includes the existing ADR-0006 `element_ref`. No second encoder. No raw `ElementId`. No separate `UniqueId` field.
3. **Search corpus.** Metadata search covers simple Element type names and public instance property names. It does not search methods and it invokes no getter. A property hit returns the owning type and the matched property names. Exact `type_name` mode returns the full property metadata.
4. **Plurals.** A query token matches an API token by exact equality, a trailing `s`, a trailing `es`, or `y` / `ies`. This is plural equivalence, not a stemmer. `classes` matches `Class`. `properties` matches `Property`.
5. **Caps.** At most 16 requested properties, 2,000 matched elements, 200 metadata matches, 80 listed properties in exact metadata, 4,096 characters per returned string, the existing 120,000-character structured-result budget, and CAP-0016 page size at most 200.
6. **Units.** `float` and `double` results are raw Revit API numbers. v1 asserts no physical unit. A future unit needs a separately reviewed authoritative Revit mechanism. The benchmark conversion is not a rule.

## References

- [CAP-0017](../capabilities/CAP-0017-bounded-revit-api-property-access.md)
- [ADR-0023](ADR-0023-declarative-revit-read-query-architecture.md), [ADR-0024](ADR-0024-registry-driven-compiled-revit-read-operations.md), [ADR-0005](ADR-0005-agent-context-and-token-efficiency.md), [ADR-0006](ADR-0006-document-and-element-reference-identity.md)
- [CAP-0016](../capabilities/CAP-0016-revit-read-operations.md)
- Spike: [README](../../spikes/BimIt.Revit.ApiAccessSpike/README.md), [VALIDATION.md](../../spikes/BimIt.Revit.ApiAccessSpike/VALIDATION.md), PR #123
- Autodesk Revit 2026 `FilteredElementCollector.OfClass`: https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/b0a5f22c-6951-c3af-cd29-1f28f574035d.htm
- Autodesk Revit 2026 `ElementClassFilter` remarks, including the unsupported-type list: https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/4b7fb6d7-cb9c-d556-56fc-003a0b8a51b7.htm
- Autodesk Revit API Developer's Guide, Transactions (model changes require an open transaction): https://help.autodesk.com/cloudhelp/2025/ENU/Revit-API/files/Revit_API_Developers_Guide/Basic_Interaction_with_Revit_Elements/Revit_API_Revit_API_Developers_Guide_Basic_Interaction_with_Revit_Elements_Transactions_html.html
- `Document.IsModifiable` remarks: a model change requires an open transaction, and a modification outside that state throws `ModificationOutsideTransactionException`. The flag can also be false during an open transaction, including regeneration and failure processing. `false` is therefore not proof that no transaction exists.
