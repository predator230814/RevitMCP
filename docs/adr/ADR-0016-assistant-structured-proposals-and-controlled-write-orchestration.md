# ADR-0016: BIM-IT Assistant structured proposals and controlled-write orchestration

- Status: Proposed
- Date: 2026-10-18
- Product Owner: Dave

## Context

BIM-IT Revit MCP can preview, review, and apply a controlled MEP device placement. The focused level-based gate passed on Revit 2026.5. The record is [BIM_IT_MEP_DEVICE_PLACEMENT_WRITE_VALIDATION.md](../BIM_IT_MEP_DEVICE_PLACEMENT_WRITE_VALIDATION.md). That capability stays a raw MCP write. The Assistant model still has five read tools.

ADR-0015 accepts one untrusted document attachment and leaves structured proposals as future work. A drawing, a PDF, or a model reply is not a Revit coordinate and is not an approval.

This ADR proposes how the Assistant may later carry a placement from conversation context to the existing write tools. It does not implement that path.

## Decision drivers

- The Revit capability core stays free of Assistant and LLM policy.
- The model must not receive `revit_preview_mep_device_placements`, `revit_request_mep_device_placement_review`, or `revit_apply_mep_device_placements`.
- The model must not invent `instance_id`, `document_id`, or a template element identity.
- PDF pixels, image pixels, and sheet coordinates are not Revit coordinates.
- Model approval is never sufficient. ADR-0008, ADR-0010, and ADR-0011 stay in force.
- External MCP clients keep calling BIM-IT Revit MCP directly.
- No new Server, Bridge, or Addin capability, and no arbitrary C# or Revit API execution.

## Options considered

### Option A: Give the three raw write tools to the model

Advantages:

- the model could call the tools that already exist.

Disadvantages:

- the model would choose instance, document, and template identities;
- a prompt or a PDF could be treated as approval or as a coordinate;
- the Assistant would stop being a boundary and become a pass-through.

### Option B: Assistant-owned proposal, then the existing controlled write

The model may later call one Assistant capability, `bimit_propose_mep_device_placements`. That call stores a bounded, session-local, untrusted proposal. The Assistant process, not the model, resolves the current Revit instance, the active document, and the selected template. A later deterministic Assistant sequence calls the existing MCP preview, shows the proposal to the user, requests the existing Revit-local review, and calls apply only after that trusted approval.

Advantages:

- Revit capabilities, Bridge protocol, and `tools/list` stay unchanged;
- external clients are unaffected;
- approval, stale checks, audit, and replay stay on the accepted write path.

Disadvantages:

- the Assistant process must later be allowed to call three write tools that the model still cannot see;
- calibration and proposal lifetime become Assistant state.

### Option C: A new Revit capability that accepts document or image coordinates

Advantages:

- one tool could try to place devices from a drawing.

Disadvantages:

- the capability core would interpret untrusted document geometry;
- it would add a Server, Bridge, or Addin contract this decision refuses.

## Decision

Option B is proposed. It is not implemented.

BIM-IT Assistant remains the orchestration owner outside the Revit capability core. The three raw MEP placement tools may be called by the Assistant process during a future orchestrated sequence. They are not model-facing tools. They are not added to the five model tools, the six allowed read tools, or the three workflow tools.

`bimit_propose_mep_device_placements` is the one future Assistant-owned model capability for this decision. It creates only a bounded, session-local, untrusted structured proposal. It cannot approve, preview, or mutate Revit.

The Assistant resolves the exact `instance_id`, `document_id`, and selected template. The model does not supply those identities. If the Assistant cannot resolve exactly one current instance, one active document, and one selected template, it does not create a Revit preview intent.

Proposal coordinates are project-axis offsets from that selected template: X, Y, and vertical. A PDF pixel, an image pixel, a sheet coordinate, or a geographic coordinate is not a Revit coordinate. If a valid project-axis basis cannot be established, the proposal state is `needs_calibration` and no Revit preview intent is created.

The future deterministic sequence is:

```text
structured proposal
  -> MCP preview
  -> user-visible proposal
  -> request Revit-local review
  -> trusted local approval
  -> MCP apply
  -> verification
```

A model statement that the user approves does not satisfy ADR-0008 or ADR-0010. The existing approval pane, stale checks, audit, and exactly-once apply stay unchanged.

One conversation holds one active proposal. New Chat clears it. Replacing or removing the source attachment invalidates it.

External MCP clients remain able to call BIM-IT Revit MCP directly. This ADR adds no Server, Bridge, or Addin capability and does not authorize arbitrary C# or Revit API execution. It does not choose a general multi-service orchestration technology.

### Proposed v1 proposal contract

The contract is architectural. It is not a tool schema and not a Bridge message.

- One to twenty placements.
- One human-readable length unit for the whole proposal. The unit is not internal feet and not a pixel unit. Plan offsets stay within the existing 100 metre bound. The vertical offset stays within the existing 10 metre bound.
- Each placement has signed project-axis `offset_x`, `offset_y`, and `vertical_offset` in that unit.
- A placement may carry optional bounded source evidence and an optional confidence. Evidence and confidence are informational. They are not coordinates and they are not approval.
- The proposal state is exactly `ready` or `needs_calibration`.
- `ready` means the Assistant has a resolved instance, document, and selected template, and a valid project-axis basis for the offsets.
- `needs_calibration` creates no Revit preview intent.
- The proposal does not contain `instance_id`, `document_id`, a template element reference, or a Revit intent reference.

## Consequences

- Production `tools/list` stays 14. The five model-facing tools stay five until a later accepted implementation task adds `bimit_propose_mep_device_placements`.
- The Assistant process does not gain write-tool authority from this ADR. A later task must make that process call explicit and must keep the tools out of the model.
- ADR-0015 remains accepted. This ADR does not supersede it. Structured proposals stay future work until an implementation task is accepted.
- Wall-hosted and level-work-plane placement remain existing MCP strategies. This ADR does not make either one an Assistant live gate.

## References

- [ADR-0007](ADR-0007-federated-mcp-boundaries-and-orchestration.md)
- [ADR-0008](ADR-0008-controlled-write-safety-model.md)
- [ADR-0010](ADR-0010-revit-local-trusted-approval-authority.md)
- [ADR-0011](ADR-0011-v1-controlled-write-audit.md)
- [ADR-0013](ADR-0013-bim-it-assistant-local-architecture.md)
- [ADR-0014](ADR-0014-provider-neutral-assistant-agent-runtime.md)
- [ADR-0015](ADR-0015-assistant-document-intake.md)
- [BIM_IT_MEP_DEVICE_PLACEMENT_WRITE_VALIDATION.md](../BIM_IT_MEP_DEVICE_PLACEMENT_WRITE_VALIDATION.md)
