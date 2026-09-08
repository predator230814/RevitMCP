# RevitMCP Vision

## Mission

RevitMCP will provide a reliable, maintainable, and vendor-neutral capability layer for Autodesk Revit that AI agents can use safely and effectively.

## Product goals

RevitMCP should:

- expose useful Revit capabilities through standards-based interfaces;
- support MCP-compatible clients and LLMs whenever technically possible;
- avoid unnecessary dependency on OpenAI, Anthropic, Cursor, or any other model vendor;
- support engineering-oriented BIM workflows, with particular attention to MEP and structural use cases;
- support read operations first, then controlled write operations with explicit safety boundaries;
- complement Autodesk's official Revit MCP instead of duplicating it without a clear reason;
- remain open to Autodesk Platform Services, MCP Apps, WebMCP, and other relevant standards as they mature;
- favor deterministic, structured, agent-usable capabilities over thin API wrappers.

## Non-goals

At this stage, RevitMCP is not intended to:

- become the largest possible catalog of Revit API endpoints;
- couple Revit workflows to a single AI client or model provider;
- bypass Revit's execution, transaction, or security constraints;
- prematurely implement every emerging MCP extension or web-agent standard;
- duplicate capabilities already provided well by Autodesk unless interoperability, specialization, or workflow needs justify it.

## Product principles

1. **Repository over memory** — the repository is the authoritative project state.
2. **Capability over API mirroring** — tools should represent coherent agent capabilities.
3. **Safety by operation class** — read, write, and destructive actions must remain distinguishable.
4. **Vendor neutrality** — the core should not depend on one LLM or client.
5. **Interoperability first** — design for standards and coexistence with Autodesk and third-party tooling.
6. **Small, reviewable evolution** — architecture should grow through explicit decisions and validated increments.
7. **Real Revit validation** — Revit-specific behavior is not complete until validated in the application when required.
