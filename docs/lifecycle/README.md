# RevitMCP lifecycle specifications

Lifecycle specifications define how the Revit add-in initializes, publishes readiness, and shuts down while respecting Autodesk Revit API execution-context constraints and the accepted bridge/execution architecture.

These specifications refine accepted ADRs and execution/bridge contracts. They do not define capability behavior or MCP-facing tool contracts.

Accepted specifications:

- `LIFECYCLE-0001-revit-addin-bootstrap-and-shutdown.md` — minimal Revit add-in bootstrap, readiness publication, startup failure, and shutdown ordering.

Proposed specifications:

- `LIFECYCLE-0002-ephemeral-write-intent-store.md` — Addin-owned ephemeral store for a CAP-0007 preview intent. Not implemented. No Revit model mutation.
