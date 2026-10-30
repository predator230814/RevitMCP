# BIM-IT Skills

Decision record: [ADR-0022](adr/ADR-0022-agent-skills-and-knowledge-architecture.md).

## Tools, Skills, and Standard Packs

| | What it is | Example |
|---|---|---|
| **Tool** | An executable capability with a schema, served by BIM-IT Revit MCP. Tools read Revit or run a controlled write. | `revit_read_spatial_rooms` |
| **Skill** | A folder of instructions that tells an agent how to do a task with existing tools. Guidance only. | `revit-model-query` |
| **Standard Pack** | Versioned firm or project knowledge: role mappings, preferred family types, mounting heights, symbols. Not implemented yet. | a future firm electrical pack |

Rule: if a workflow can be written as a Skill over generic tools, write a Skill. Do not add a specialised Revit tool for it.

## Folder format

```
my-skill/
├── SKILL.md
└── references/          optional companion files
    └── rules.md
```

`SKILL.md` begins with YAML front matter:

```
---
name: my-skill
description: Use when the user wants to … (what it does and when to use it)
---

# Instructions in Markdown
```

- `name`: 1 to 64 lowercase letters, digits, and single hyphens. It must equal the folder name.
- `description`: 1 to 1024 characters. The agent decides from this line whether to load the Skill, so say what it does and when to use it.
- Other portable fields such as `license` or `metadata` are allowed and ignored.

This is the open Agent Skills format. The same folder works in Claude Code and other Skill-aware agents.

## Sources

| Badge | Where | Notes |
|---|---|---|
| BUILT-IN | Beside `BimIt.Revit.Assistant.exe`, from [`skills/built-in/`](../skills/built-in) | Read-only. Updated with BIM-IT releases. |
| CUSTOM | `%APPDATA%\BIM-IT\skills\custom\` | One folder per Skill. Import, edit, or delete in Explorer. |
| EXTERNAL | Folders added in Settings → Skills → Add skills folder | OneDrive, SharePoint-synced, network, or Git folders. Each direct subfolder with a `SKILL.md` is a Skill. Up to 8 folders. |

Settings and disabled Skills are stored in `%APPDATA%\BIM-IT\skills\settings.json`.

Built-in Skills:

- `revit-model-query`: how to inspect, find, and compare model data with the generic read tools.
- `revit-safe-write`: the controlled-write sequence. Chat approval is not Revit approval.

## Status

| Status | Meaning |
|---|---|
| ready | Valid and enabled. The Assistant lists it. |
| disabled | Valid, turned off in Settings. |
| invalid | Fails a rule below. The diagnostic says which. |
| conflict | Another enabled Skill has the same name. Neither is used until one is disabled or removed. No source wins automatically. |
| unavailable | The Skill or its folder cannot be read right now. |

Each Skill shows a SHA-256 over its files. It changes when any file content, file name, or file set changes.

## Trust and limits

Skills are guidance. They never grant Revit write or approval authority, and BIM-IT never runs Skill files. A Skill may show code as Markdown text.

- Allowed files: `.md`, `.txt`, `.yaml`, `.yml`, `.json`, `.csv`. Any other file makes the Skill invalid.
- Hidden dot files and folders are ignored and never imported.
- Rejected: symbolic links and junctions, paths outside the Skill folder, unreadable files, non-UTF-8 text.
- Limits: `SKILL.md` ≤ 128 KiB, whole Skill ≤ 1 MiB, ≤ 32 files, ≤ 4 folder levels, name ≤ 64 characters.
- OneDrive and SharePoint placeholder files are allowed.
- Paths are never sent to the model or shown in the Assistant page.

## How the Assistant uses Skills

1. At the start of each turn BIM-IT reloads Skills.
2. The model sees each ready Skill's name, description, and badge. Not its content.
3. When a task matches, the model calls `bimit_read_skill` with the Skill name, and optionally a companion file such as `references/rules.md`.
4. BIM-IT revalidates the folder and returns the text, the source, and the hash.
5. The model follows the Skill with the normal BIM-IT tools. Your explicit instructions in chat still win.

`bimit_read_skill` is an Assistant capability, not a Revit MCP tool. It reads only validated files inside one ready Skill.

## How a firm adds Skills without rebuilding BIM-IT

1. Create a shared folder, for example a SharePoint library synced through OneDrive, or a Git repository.
2. Put one folder per Skill in it, each with a `SKILL.md`.
3. Each user opens BIM-IT Assistant → Settings → Skills → Add skills folder, and picks that folder.
4. New or changed Skills appear on Reload or on the next chat turn.

For a one-off Skill, use Settings → Skills → Import Skill and choose a folder that contains `SKILL.md`. BIM-IT validates it and copies it into CUSTOM.

## Using the same Skills from another agent

The folders are portable. To use `revit-model-query` from Claude Code against BIM-IT Revit MCP, copy the folder to `.claude/skills/revit-model-query/` in the working folder (or to `~/.claude/skills/`), and add `BimIt.Revit.Server.exe` as a stdio MCP server. See [BIM_IT_SKILLS_CLAUDE_CODE_VALIDATION.md](BIM_IT_SKILLS_CLAUDE_CODE_VALIDATION.md).

## Future: MCP Skills extension

The registry is independent of the Assistant. A later adapter can publish the same Skills through the MCP `io.modelcontextprotocol/skills` extension, so any MCP client can discover them from BIM-IT. That is not implemented yet.
