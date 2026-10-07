# RevitMCP lifecycle specifications

Lifecycle specifications define how the Revit add-in initializes, publishes readiness, and shuts down while respecting Autodesk Revit API execution-context constraints and the accepted bridge/execution architecture.

These specifications refine accepted ADRs and execution/bridge contracts. They do not define capability behavior or MCP-facing tool contracts.

Accepted specifications:

- `LIFECYCLE-0001-revit-addin-bootstrap-and-shutdown.md` — minimal Revit add-in bootstrap, readiness publication, startup failure, and shutdown ordering.
- `LIFECYCLE-0002-ephemeral-write-intent-store.md` — Addin-owned ephemeral store for a CAP-0007 preview intent. Accepted. The in-process store, successful document-close cleanup, and shutdown cleanup are implemented. CAP-0007 is implemented end-to-end through SERVER-0007 and Bridge protocol v8, and that path is live-validated. No Revit model mutation.
- `LIFECYCLE-0003-revit-local-approval-provider-lifecycle.md` — Revit-local trusted approval provider lifecycle. Accepted on 2026-10-05. Acceptance does not authorize production provider integration, production UI, CAP-0008, or a Revit write.
- `LIFECYCLE-0004-controlled-apply-attempt-lifecycle.md` — Addin-owned apply-attempt store for Accepted CAP-0008. Accepted on 2026-10-07. Conforms to Accepted ADR-0011. Not implemented yet. Acceptance does not claim an existing Revit write implementation.
