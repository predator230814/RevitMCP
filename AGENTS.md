# RevitMCP Agent Instructions

## Purpose

This file defines the canonical working rules for AI coding agents contributing to RevitMCP.

The repository is the source of truth. Conversation memory, model memory, and previous chat sessions are not authoritative project state.

## Source-of-truth order

When information conflicts, use this order:

1. Current repository code
2. Repository architecture documentation and accepted ADRs
3. GitHub Issues and accepted specifications
4. Explicit product decisions recorded in the repository
5. Current task instructions
6. AI memory or assumptions

If a conflict remains, report it instead of guessing.

## Before changing anything

1. Read this file.
2. Read `docs/VISION.md`.
3. Read `docs/ARCHITECTURE.md`.
4. Read `docs/CURRENT_STATE.md`.
5. Read all ADRs relevant to the task.
6. Inspect the existing implementation before proposing changes.

Do not reconstruct architecture from memory when the repository contains the answer.

## Working rules

- Work on one small, reviewable task at a time.
- Do not modify unrelated code.
- Do not introduce a new architectural pattern without discussing its tradeoffs.
- Record significant architecture decisions as ADRs.
- Prefer deterministic structured contracts and explicit error models.
- Keep read, write, and destructive operations clearly distinguishable.
- Design MCP capabilities for agent usability, not as one-to-one wrappers around Revit API methods.
- Prefer coherent batch operations when appropriate.
- Keep core Revit capabilities independent of any specific LLM or AI client.
- Treat Autodesk's official Revit MCP as an ecosystem component to interoperate with or complement, not something to duplicate automatically.
- Keep WebMCP, MCP Apps, Autodesk Platform Services, and future standards in consideration without coupling the core to them prematurely.

## Implementation tasks

A coding task should normally contain:

- objective;
- relevant repository references;
- scope and non-scope;
- behavioral contract;
- acceptance criteria;
- applicable tests or Revit validation steps.

Task prompts should describe the task, not repeat the entire project history.

## Completion

Before considering a task complete:

1. Build the affected projects when applicable.
2. Run applicable automated tests.
3. Report anything that could not be validated automatically.
4. Validate Revit-specific behavior in Revit when required.
5. Update documentation when the implementation changes documented behavior or architecture.

Never assume AI-generated code is correct merely because it compiles.
