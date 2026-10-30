# ADR-0022: Agent Skills and knowledge architecture

- Status: Proposed
- Date: 2026-10-30
- Product Owner: Dave

## Context

Every BIM-IT workflow so far has grown the Revit add-in, the Server, or the Assistant runtime: a new capability, a new Bridge protocol, a new model tool. That does not scale. Firms will want electrical layouts, room standards, project conventions, and discipline packs. If each one needs a recompiled Revit add-in, BIM-IT becomes one large add-in that owns every workflow, and a firm cannot change its own procedure without a BIM-IT release.

Agent products have converged on a portable answer: an *Agent Skill* is a folder with a `SKILL.md` whose YAML front matter names and describes it. The agent sees the name and description, loads the body only when the task matches, and composes existing tools. The MCP community is standardising the same shape as the `io.modelcontextprotocol/skills` extension.

ADR-0007 keeps the Revit capability service specialised. ADR-0008 to ADR-0011 keep controlled writes on the preview, trusted local approval, apply, and audit path. ADR-0014 keeps the provider replaceable and keeps provider-native skills disabled. ADR-0015 keeps local paths out of the WebView and the model.

## Decision drivers

- New business workflows should be added without recompiling the Revit add-in.
- The Revit execution kernel stays the only authority for Revit truth and mutation.
- Skill guidance can never grant write or approval authority.
- Firms need provenance: which Skill, from where, at what content.
- Two Skills with the same name must never silently replace each other.
- The format must be portable: the same folder should work in Claude Code, another MCP client, and BIM-IT Assistant.
- No provider lock-in, and no dependency on SDK support for the MCP Skills extension.

## Concepts

| Concept | What it is | Who owns it |
|---|---|---|
| MCP Tool | An executable capability with a schema. `revit_read_spatial_rooms` is one. | BIM-IT Revit MCP Server and the Revit add-in |
| MCP Resource / knowledge file | Information an agent may read. | Its publisher |
| Agent Skill | Reusable instructions describing how to accomplish a task using capabilities and knowledge. A folder with `SKILL.md`. | BIM-IT (built-in), the user (custom), or a firm (external) |
| Standard Pack | Versioned company or project knowledge and mappings: roles, preferred family types, mounting heights, symbols. | A firm or project. Not implemented here. |
| LLM | Planning, semantic reasoning, orchestration. | The provider |
| Revit Execution Kernel | Trusted Revit truth, transactions, references, geometry, approval, stale revalidation, audit, verification. | The Revit add-in |

Product rule: **do not add a specialised Revit capability when the workflow can be expressed as a Skill using existing generic capabilities.** `get_room_data` is a capability. `get_studios_like_this_sketch` is agent reasoning, so it belongs in a Skill. When a Skill cannot be written because a generic fact is missing, the missing generic primitive is recorded and decided on its own merits.

## Options considered

### Option A: Keep adding specialised tools and Assistant code per workflow

Familiar, but every workflow needs a release, the add-in grows without bound, and firms cannot own their procedures.

### Option B: Use provider-native skills (for example Copilot skill directories)

Little code, but it couples Skills to one provider, cannot show provenance in BIM-IT Settings, and conflicts with ADR-0014, which keeps provider skills, plugins, and config discovery off.

### Option C: A provider-neutral Skill registry outside the add-in, using the open Agent Skills folder format

BIM-IT reads Skill folders itself, validates them with fixed bounds, hashes them, and exposes them to its own model runtime by progressive disclosure. External MCP clients use the same folders through their own Skill mechanism. A future adapter can publish the same registry through `io.modelcontextprotocol/skills`.

## Decision

Option C is proposed.

### Format

A Skill is a folder containing `SKILL.md`. The file starts with YAML front matter. Required fields are `name` and `description`, as in the portable Agent Skills format. BIM-IT adds no required field. `name` is 1 to 64 lowercase letters, digits, and single hyphens, and must equal the folder name. `description` is 1 to 1024 characters. Other portable fields (`license`, `compatibility`, `metadata`, `allowed-tools`) are accepted and ignored. Companion files may sit in the same folder.

### Sources

| Source | Location | Who changes it |
|---|---|---|
| `built_in` | `skills\` beside `BimIt.Revit.Assistant.exe`, shipped from `skills/built-in/` in this repository | BIM-IT releases. Read-only in Settings. |
| `custom` | `%APPDATA%\BIM-IT\skills\custom\`, one folder per Skill | The user: import, edit in Explorer, remove |
| `external` | Folders the user adds in Settings, for example a OneDrive, SharePoint-synced, network, or Git folder | The firm or project. BIM-IT only reads them. |

External paths come only from Settings and are stored in `%APPDATA%\BIM-IT\skills\settings.json`, together with disabled Skill keys. No BIM-IT, Tetra Tech, or company path is hardcoded. At most 8 external folders are allowed.

### Conflicts

If two enabled Skills have the same name, both become `conflict` and neither is offered to the model. There is no source precedence. The user resolves it by disabling or removing one. Explicit user instructions in chat still outrank any Skill.

### Trust and filesystem bounds

Skills are guidance, not authority. BIM-IT never executes Skill content. Allowed files are `.md`, `.txt`, `.yaml`, `.yml`, `.json`, and `.csv`. Any other file, including scripts, binaries, and files with no extension, makes the Skill `invalid`. Hidden dot entries are ignored and never copied.

The reader fails closed on symbolic links and junctions (any entry with a link target), entries outside the Skill folder, `..` and absolute child paths, unreadable files, non-UTF-8 or NUL content, more than 4 directory levels, more than 32 files, more than 256 directory entries, `SKILL.md` over 128 KiB, and a Skill over 1 MiB. Cloud placeholders (OneDrive, SharePoint) are reparse points without a link target. They read as ordinary files inside the folder, so they are allowed.

### Provenance

Every listed Skill carries `name`, `description`, `source`, `source_id`, `enabled`, `sha256`, and `status` (`ready`, `disabled`, `invalid`, `conflict`, `unavailable`). The hash is SHA-256 over every validated file in ordinal path order, including each relative path and length, so any content, file, or rename change changes it. Filesystem paths never reach the model, the WebView, or logs.

### Assistant integration

- The registry reloads when Settings opens, when the user clicks Reload, and at the start of every user turn.
- The model sees only enabled `ready` Skills as name, description, and source type, in a `<bimit_skills>` block sent when that list changes. Full content is never injected by default.
- One Assistant runtime capability, `bimit_read_skill`, reads `SKILL.md` or one validated companion file from one `ready` Skill. It revalidates the folder before returning bounded text (64,000 characters) with the source and hash. It is not a Revit MCP tool and cannot read anything else.
- Provider instructions say: inspect Skill summaries, load a matching Skill, follow it while respecting the user's explicit request, and remember that Skills grant no write or approval authority and that writes still go through the Revit Approval pane.
- The model's Revit reads are the generic MCP reads under their MCP names (see the amendment below). Server `tools/list` is unchanged at 18.
- Each Skill read records a structured `AssistantActivity` (`read_skill`, Skill name, status). A future activity surface can render "Reading skill: …" from it. It records actions and status, never reasoning.

### Settings → Skills

The Assistant WebView gains a Settings page with Skills: list, badges, status, diagnostics, `SKILL.md` view, hash, reference files, enable and disable, reload, open custom folder, add and remove an external folder, and import a Skill folder. The WebView never sends or receives a path. The add-in opens a native folder picker and forwards the chosen path over the existing current-user pipe, as ADR-0015 does for documents. Import validates first, then writes exactly the validated bytes into `custom`. It never overwrites an existing custom Skill. ZIP import is not supported.

### Placement

`BimIt.Revit.Skills` is a new provider-neutral library with no Revit, MCP SDK, or provider dependency. Only `BimIt.Revit.Assistant` references it. The Revit add-in gains no Skill parsing, no standards, and no business logic. Its only change is relaying the Settings messages and opening a folder picker.

### MCP Skills seam

The registry is independent of BIM-IT Assistant. A later adapter in `BimIt.Revit.Server`, or a separate Skills MCP service, can publish the same catalog and reads through the official `io.modelcontextprotocol/skills` extension when the C# MCP SDK supports it. No private Skill protocol is added now.

### Amendment before acceptance: the Assistant sees the generic MCP read surface

The PR #96 live run invalidated one assumption inherited from [ADR-0014](ADR-0014-provider-neutral-assistant-agent-runtime.md): that the Assistant model can work through a tiny bespoke read surface. The shipped `revit-model-query` Skill says that rooms in loaded Revit links are read with `revit_read_spatial_rooms`. Claude Code, connected straight to `BimIt.Revit.Server`, followed it and succeeded. BIM-IT Assistant loaded the same Skill but could not follow it, because its model saw only `bimit_get_model_context`, `bimit_get_warnings`, `bimit_get_active_view_elements`, `bimit_inspect_selection`, and `bimit_explore_selected_mep`. That was a model tool-surface mismatch, not a missing linked-room capability. A Skill written against generic MCP capabilities is not portable to an agent that cannot see those capabilities.

Decision: the Assistant model sees the same generic read-only Revit MCP tools an external MCP agent sees, under their MCP names.

```
external MCP client:  Skill → MCP tool
BIM-IT Assistant:     Skill → same MCP tool name → Assistant trusted read facade → existing MCP client → BimIt.Revit.Server
```

- The read surface is discovered from the Server's `tools/list` metadata through the Assistant's existing MCP client. A tool is exposed only when its name starts with `revit_`, BIM-IT does not classify it as a trusted write, `readOnlyHint` is true, `destructiveHint` is not true, `openWorldHint` is explicitly false, its input schema is an object, and it accepts `instance_id`. Anything missing or contradictory fails closed. A name alone never qualifies a tool.
- The Server stays the source of truth for each schema. The Assistant removes only `instance_id` and `document_id` from the properties and the required list. Every other argument and requirement is kept.
- On each call the Assistant injects the exact bound `instance_id`. When the tool takes `document_id`, it reads `revit_get_context` for that call and injects the active `document_id`. A required document with no active document stops the call. The model cannot supply either identity. A model argument outside the provider schema is rejected before MCP.
- The current surface is 11 reads: `revit_get_context`, `revit_query_elements`, `revit_get_elements`, `revit_describe_parameters`, `revit_get_parameter_values`, `revit_get_mep_topology`, `revit_get_warnings`, `revit_get_selection`, `revit_get_room_layouts`, `revit_get_mep_device_types`, `revit_read_spatial_rooms`. A test derives it from the real Server metadata.
- The five `bimit_*` read wrappers are no longer model-facing. Quick actions keep their deterministic workflows.
- Assistant-local tools stay: `bimit_propose_mep_device_placements`, `bimit_propose_room_pattern`, `bimit_read_skill`.
- The invariant is no longer a fixed tool count. It is: all trusted generic MCP reads, plus the explicit Assistant-local untrusted tools, plus zero trusted write tools. The seven preview, review, and apply tools stay unreachable from the provider. The local controlled-write orchestration is unchanged.
- Used Tools in the chat show generic reads by their MCP names, plus "Read skill".

This amendment partially supersedes ADR-0014 for model-facing read exposure only. ADR-0014 still holds for provider neutrality, the Assistant-owned MCP client, exact Revit instance binding, write isolation, and the approval boundary. The provider is still not connected to the Server.

## Consequences

- Firms add workflows by publishing Skill folders. New Skills need no C# change and no rebuild.
- The Revit add-in stays the execution kernel. Business logic does not grow there.
- Controlled-write security is unchanged. A Skill cannot preview, approve, or apply.
- Skill quality is now a content concern. BIM-IT validates shape and bounds, not the correctness of advice.
- Standard Packs remain future work. A Skill can point to reference files, but structured standards, role mappings, and Tetra Tech standards are not implemented.
- The direct Claude Code validation in [BIM_IT_SKILLS_CLAUDE_CODE_VALIDATION.md](../BIM_IT_SKILLS_CLAUDE_CODE_VALIDATION.md) measures how usable the generic MCP surface is to a strong external agent, and lists missing generic primitives without adding them.

## References

- [ADR-0007](ADR-0007-federated-mcp-boundaries-and-orchestration.md)
- [ADR-0008](ADR-0008-controlled-write-safety-model.md)
- [ADR-0010](ADR-0010-revit-local-trusted-approval-authority.md)
- [ADR-0013](ADR-0013-bim-it-assistant-local-architecture.md)
- [ADR-0014](ADR-0014-provider-neutral-assistant-agent-runtime.md)
- [ADR-0015](ADR-0015-assistant-document-intake.md)
- [SKILLS.md](../SKILLS.md)
- Agent Skills open format (`SKILL.md` with `name` and `description` front matter)
- MCP Skills extension, `io.modelcontextprotocol/skills`
