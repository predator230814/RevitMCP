# ADR-0012: BIM-IT product family and specialized MCP service identity

- Status: Accepted
- Date: 2026-10-13
- Product Owner: Dave

## Context

RevitMCP began as the name of this repository and of its local Revit capability service. The product direction is now a family of specialized services under BIM-IT, with a separate conversational assistant that can use one or more of those services later.

ADR-0007 remains authoritative: the Revit capability service must not become a universal BIM or Autodesk mega-server. This ADR records the accepted product-family names. It does not rewrite ADR-0001 through ADR-0011. Those records keep the RevitMCP name used when they were accepted. RevitMCP was renamed to BIM-IT Revit MCP.

Repository rename on GitHub is a repository-owner operation and is not part of this decision's code change.

## Decision drivers

- Keep one specialized Revit MCP service with its own process, security, and failure model.
- Leave Civil 3D and Autodesk cloud services as future specialized services, not as features of the Revit server.
- Keep the conversational product separate from the Revit capability host.
- Rename technical projects where that can be done without changing protocol or storage identifiers.

## Options considered

### Option A: Keep the RevitMCP name for every future Autodesk product

One name would cover Revit, Civil 3D, and cloud services.

Advantages:

- no rename;
- existing documents stay literal.

Disadvantages:

- the name would imply one product and invite the mega-server ADR-0007 rejects.

### Option B: BIM-IT family with specialized MCP services and a separate Assistant

User-facing names:

- BIM-IT Revit MCP
- BIM-IT Civil 3D MCP, future
- BIM-IT Autodesk Cloud MCP, future; internal service topology remains undecided
- BIM-IT Assistant, the conversational experience

Technical project and assembly names:

- `BimIt.Revit.Contracts`
- `BimIt.Revit.Bridge`
- `BimIt.Revit.Addin`
- `BimIt.Revit.Server`
- `BimIt.Revit.Assistant`

Advantages:

- matches the accepted product family;
- keeps Revit execution in one specialized service;
- leaves room for later services and providers without coupling them to the Revit core.

Disadvantages:

- project paths and assembly names change;
- historical documents and the new tree use different names unless a reader checks this ADR.

## Decision

Option B is accepted.

The following identifiers stay as they are. They are protocol, storage, or audit identifiers, not branding:

- MCP tool names such as `revit_get_context` and `revit_get_warnings`
- Bridge JSON-RPC method names such as `revit.get_context`
- Bridge protocol versions and their semantics
- accepted capability contracts
- the existing Approval DockablePane GUID
- controlled-write audit domain strings `revitmcp-audit-v1:`
- Named Pipe names `revitmcp.bridge.v1...`
- the LocalAppData registration and audit folder name `RevitMCP`
- the approval WebView host `revitmcp-approval.local`
- handshake `client_name` values `RevitMCP.Server` and `RevitMCP.Discovery`
- the MCP server implementation name `RevitMCP.Server`
- the controlled-write transaction name `RevitMCP Apply Parameter Updates`

BIM-IT Civil 3D MCP and BIM-IT Autodesk Cloud MCP are names only. This ADR does not implement them and does not decide cloud service topology.

## Consequences

- Current code uses the `BimIt.Revit` namespaces and assembly names.
- External MCP clients still launch the Revit stdio server directly. The server implementation name remains `RevitMCP.Server`.
- GitHub repository links in historical documents stay valid until an owner renames the repository.
- Later product work must not fold Civil 3D or Autodesk cloud capability into `BimIt.Revit.Server`.
