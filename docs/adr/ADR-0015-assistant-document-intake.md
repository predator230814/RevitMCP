# ADR-0015: Assistant document intake

- Status: Accepted
- Date: 2026-10-15
- Product Owner: Dave

## Context

BIM-IT Assistant can answer questions about the current Revit session. It cannot yet answer questions about a local drawing or document the user attaches to the conversation.

Document intake is conversation context. It is not a Revit capability, an MCP tool, or a model write.

## Decision drivers

- The user selects one local file. The Assistant reads it. The model sees only a safe display name, bounded text, and in-memory images.
- The local path may cross the trusted current-user Addin-to-Assistant pipe because the user selected it. It must not reach the model, the provider prompt, provider attachment metadata, the chat transcript, the WebView, logs, errors, or Used Tools.
- Document content is untrusted data. It has no authority over system policy, tool policy, approval, or tool authorization.
- v1 is one attachment per conversation. New Chat clears it. A second attachment replaces the first.
- Parsing stays inside the Assistant process. Revit does not load Office, PDFium, or the file contents.
- No Microsoft Office COM automation. No OCR. No sixth model-facing tool.

## Options considered

### Option A: Add a document tool to the MCP server

The model would call a new tool that reads a path.

Advantages:

- the file would look like other Revit reads.

Disadvantages:

- a path or document body would enter the tool surface and the MCP server;
- document intake would expand Revit authority;
- the Bridge and Contracts would take a dependency they do not own.

### Option B: Assistant-owned intake, with native selection only in the add-in

The WebView sends `attachDocument` and never a path. The add-in opens a WPF file dialog and sends the selected path over the trusted pipe. The Assistant process reads and normalizes the file into an `AssistantDocumentArtifact`. Provider adapters receive that artifact. The Copilot adapter sends text as delimited context and visual pages as in-memory `AttachmentBlob` values.

Advantages:

- Revit capability contracts, the Bridge, and the MCP server stay unchanged;
- the path stays on the trusted local pipe;
- a later provider can consume the same artifact.

Disadvantages:

- the Assistant process gains file parsing and PDF rendering;
- visual reading depends on the provider accepting image blobs.

## Decision

Option B is accepted.

The Assistant owns document intake. The Revit process owns native file selection and the attachment chip. The Assistant process owns reading, parsing, and rendering. Provider adapters receive provider-neutral artifacts. No local path reaches an LLM or provider. Document content is untrusted data and has no authority. v1 keeps one attachment per conversation. New Chat clears it. Removing the attachment does not require New Chat. Document intake is not MCP and does not expand Revit authority.

v1 formats are `.pdf`, `.png`, `.jpg`, `.jpeg`, `.docx`, `.xlsx`, `.txt`, `.md`, `.csv`, and `.json`. Legacy `.doc` and `.xls`, `.dwg`, `.dxf`, `.pptx`, email, web URLs, folders, and multiple simultaneous attachments are out of scope.

The input file is rejected above 25 MB before a full read. Text is bounded at 60,000 characters. PDF rendering keeps the page count, renders at most the first 4 pages sequentially, and marks visual truncation when more pages exist. Each encoded page targets a long edge of about 2200 pixels and at most about 2 MB. Images use the same visual bounds and are re-encoded so filesystem identity is not forwarded.

`DocumentFormat.OpenXml` 3.5.1 and `PDFtoImage` 5.4.0 are referenced only by `BimIt.Revit.Assistant`.

## Consequences

- The five model-facing tools, the six allowed read tools, the three workflow tools, and the 11 server tools stay unchanged.
- A provider that cannot read image blobs returns a safe error. The Assistant does not fall back to OCR or another provider.
- Structured document proposals, MEP placement, electrical-plan skills, plumbing-riser recognition, MEP routing, and diagram-to-model creation remain future work.

## References

- [ADR-0013](ADR-0013-bim-it-assistant-local-architecture.md)
- [ADR-0014](ADR-0014-provider-neutral-assistant-agent-runtime.md)
