# Server specifications

This directory contains technical contracts for the standalone `RevitMCP.Server` process.

Accepted specifications:

- [SERVER-0001](SERVER-0001-stdio-get-context-tool.md) — initial `stdio` MCP server, deterministic local Revit instance routing, and MCP exposure of CAP-0001 `revit_get_context`.
- [SERVER-0002](SERVER-0002-stdio-query-elements-tool.md) — adds CAP-0002 `revit_query_elements`, bridge protocol-v3 capability gating, document-context guarding, and preservation of CAP-0001 on explicitly compatible v2/v3 bridges.
- [SERVER-0003](SERVER-0003-stdio-get-elements-tool.md) — adds CAP-0003 `revit_get_elements`, bounded projection/partial-result semantics, bridge protocol-v4 capability gating, and preservation of CAP-0001/CAP-0002 on explicitly compatible v4 bridges.
- [SERVER-0004](SERVER-0004-stdio-describe-parameters-tool.md) — CAP-0004 `revit_describe_parameters`, bridge protocol-v5 capability gating, closed identity/data-type output variants, and preservation of CAP-0001/CAP-0002/CAP-0003 on explicitly compatible v5 bridges. Implemented. Official MCP-client-to-Revit-2026.5 live validation is **PASS**.
- [SERVER-0005](SERVER-0005-stdio-get-parameter-values-tool.md) — CAP-0005 `revit_get_parameter_values`, bridge protocol-v6 capability gating, closed typed-value output variants, and preservation of CAP-0001..CAP-0004 on explicitly compatible v6 bridges. Implemented and merged. Official MCP-client-to-Revit-2026.5 live validation is **PASS**.
- [SERVER-0006](SERVER-0006-stdio-get-mep-topology-tool.md) — CAP-0006 `revit_get_mep_topology`, bridge protocol-v7 capability gating, closed deterministic physical graph output, and preservation of CAP-0001..CAP-0005 on explicitly compatible v7 bridges. Implemented. Its own live validation recorded `tools/list` as exactly 6. Official MCP-client-to-Revit-2026.5 live validation is **PASS**. Later tools have increased `tools/list`.
- [SERVER-0007](SERVER-0007-stdio-preview-parameter-updates-tool.md) — **Accepted** and implemented. CAP-0007 `revit_preview_parameter_updates`. Official MCP-client to stdio Server to Bridge v8 to Revit 2026.5 validation is **PASS** at SHA `1ca66811734cec00401e627162a7b2e4d0a136b1`. Cases A/B/C and the reread are **PASS**. Case D was not run because the selected candidate was a string.
- [SERVER-0008](SERVER-0008-stdio-request-parameter-update-review-tool.md) — implemented `revit_request_parameter_update_review`. Exact instance routing. The call is not approval.
- [SERVER-0009](SERVER-0009-stdio-apply-parameter-updates-tool.md) — CAP-0008 `revit_apply_parameter_updates` on protocol v10. Implemented in the Draft PR. Expected `tools/list` count is 9. Live Revit write validation is pending. Do not merge until that gate passes.

Server specifications refine accepted ADRs and capability contracts. They must not introduce Autodesk Revit API dependencies into the Server or silently change lower-layer Bridge/Addin contracts.
