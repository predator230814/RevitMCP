# ADR-0017: Document-space MEP placement calibration

- Status: Accepted
- Date: 2026-10-21
- Product Owner: Dave

## Context

ADR-0016 lets the Assistant store one untrusted MEP device placement proposal and, after a local project-axis confirmation, call the existing preview, review, and apply tools. That confirmation covers offsets the model already expressed on Revit project axes.

A drawing is different. ADR-0015 accepts one PDF or image as conversation context. Positions on that page are not Revit coordinates. The model, the file, the MCP client, and the Assistant WebView cannot turn those positions into a trusted placement.

This ADR decides the only local path that may carry a document-page proposal onto the ADR-0016 controlled-write path. It does not add a write capability.

## Decision drivers

- Document and image coordinates stay untrusted until a person establishes them.
- The model surface stays six tools. Server `tools/list` stays 14.
- Calibration is not approval. ADR-0008, ADR-0010, and ADR-0011 stay in force.
- The Revit capability core does not interpret page geometry.
- The Assistant computes the page-to-project transform. The model does not.
- A calibration click never creates a Revit transaction.
- The WebView never receives a filesystem path or a Revit identity.

## Options considered

### Option A: Let the model assert that a page position is calibrated

Advantages:

- no extra local interaction.

Disadvantages:

- a prompt or a PDF could claim a coordinate basis;
- the Assistant would treat untrusted document geometry as Revit geometry.

### Option B: Add a seventh model tool or a new Server capability for page coordinates

Advantages:

- the model could name the calibration itself.

Disadvantages:

- it would enlarge the model surface or the capability core;
- it would move document interpretation into MCP.

### Option C: One local two-point calibration, then the existing controlled write

The current proposal must already be bound to one attachment page, one Revit instance, one document, and one selected template. The Assistant shows a bounded raster of that page. The person clicks source point A at the drawing location of the selected template, then source point B. The Addin resolves that template natively and asks Revit to pick the matching point B. The Addin returns only the target A-to-B vector in metres. The Assistant computes one orientation-preserving 2D similarity and then calls the existing preview client.

Advantages:

- document coordinates stay untrusted until both human actions occur;
- preview, review, and apply stay on the ADR-0016 path;
- no new MCP tool is required.

Disadvantages:

- the person must know two corresponding points;
- the calibration is invalid as soon as the proposal, attachment, page, document, or template changes.

## Decision

Option C is accepted.

`bimit_propose_mep_device_placements` keeps its name and gains a closed `coordinate_space` discriminator:

- `project_offsets` keeps `offset_x`, `offset_y`, and `vertical_offset`. Confirm project axes & preview remains the local action for that mode.
- `document_page` requires `page_number` and, on each placement, `page_x` and `page_y` in the range 0 to 1. `page_x` runs left to right. `page_y` runs top to bottom. `vertical_offset` stays in the proposal length unit. Calibrate plan & preview is the only local action for that mode.

Mixed fields and unknown fields are rejected. The model cannot set a calibrated or ready flag.

The visual page stores the raster width and height that the Assistant encoded. Similarity math uses that aspect ratio:

```text
x = page_x * raster_width
y = (1 - page_y) * raster_height
```

Normalized X and Y are not treated as equal physical axes.

The calibration page shown in the WebView comes from that visual page. It carries no path and no instance, document, template, or intent identity. It uses a short-lived opaque session ref and accepts exactly two ordered clicks. The native host sees only those normalized coordinates. The preview image is at most 128 KiB. The global IPC frame limit is unchanged. There is no generic file server and no browser-native bridge.

The Addin performs the Revit point pick on the existing external-event dispatcher. It is not an MCP tool and the model cannot call it. The pick validates the active document and the bound template, requires a point-like template location, and uses `Selection.PickPoint` with a bounded snap set. Cancel, a missing work plane, a document or template mismatch, and an API failure produce no vector. The pick opens no `Transaction`, `SubTransaction`, or `TransactionGroup`.

A calibration is bound to the proposal generation, the attachment artifact, the page number, the raster width and height, the Revit instance, the document, and the template. Source points must be finite and inside 0 to 1. In the trusted raster their separation must be at least the greater of 8 pixels and 1 percent of the visual page long edge. The target vector must be finite and non-zero. The similarity must be orientation-preserving, with a finite positive scale and no reflection. Transformed plan offsets stay inside 100 m and vertical offsets inside 10 m. Replacing or removing the attachment, storing a new proposal, New Chat, Assistant shutdown, or a document or template mismatch drops the calibration.

When the transform succeeds, the Assistant calls the existing preview once. Review and Apply stay separate local clicks. The Revit-local Approval Pane remains the only approval authority. A terminal apply still blocks another calibration or preview of that proposal generation.

Still refused: a seventh model tool, a new Server or Bridge capability, arbitrary Revit API or C# execution, shell access, MCP Apps, HTTP transport, auto-approval, and auto-apply.

## Consequences

- Document-page proposals stay `needs_calibration` until this local sequence succeeds.
- Project-offset proposals keep the existing confirmation path.
- The Assistant, not the model, owns the similarity and the conversion into the proposal length unit.
- A wrong local action for the proposal's coordinate space fails closed and does not preview.

## References

- [ADR-0008](ADR-0008-controlled-write-safety-model.md)
- [ADR-0010](ADR-0010-revit-local-trusted-approval-authority.md)
- [ADR-0015](ADR-0015-assistant-document-intake.md)
- [ADR-0016](ADR-0016-assistant-structured-proposals-and-controlled-write-orchestration.md)
- [BIM_IT_ASSISTANT_PROPOSAL_VALIDATION.md](../BIM_IT_ASSISTANT_PROPOSAL_VALIDATION.md)
