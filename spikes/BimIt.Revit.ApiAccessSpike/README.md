# Generic Revit API access spike

Research spike. This is not a production capability and it does not change ADR-0023 or ADR-0024.

The question is whether one bounded generic reader can return long-tail Revit facts for an agent without a compiled operation per Revit class, and without accepting source code.

## What an opted-in build adds

Normal Addin builds register the accepted production operations, including `project.user_worksets` v1, `revit_api.metadata` v1, and `revit_api.element_properties` v1. They do not register the experimental ids. Bridge protocol stays 24, and `tools/list` stays the production 22 tools.

Pass `-p:BimItApiAccessSpike=true` to compile the sources in this directory into `BimIt.Revit.Addin` and register two extra read operations:

- `experimental.revit_api_lookup` v1
- `experimental.revit_api_read` v1

They are discovered through the existing `revit_lookup` and executed through the existing `revit_read`. There is no new MCP tool, Bridge RPC, or protocol version. The spike build writes to `src/BimIt.Revit.Addin/bin/<year>/Spike/` so it does not replace the normal Release output.

The generic implementation does not name the benchmark classes. Tests name them. The same resolver and the same collector handle every concrete `Element` subclass.

## Build and test

From the repository root, with the repository `dotnet`:

```text
dotnet test spikes/BimIt.Revit.ApiAccessSpike.Tests/BimIt.Revit.ApiAccessSpike.Tests.csproj --configuration Release
dotnet build src/BimIt.Revit.Addin/BimIt.Revit.Addin.csproj -c Release -p:RevitVersion=2026 -p:BimItApiAccessSpike=true
dotnet build src/BimIt.Revit.Addin/BimIt.Revit.Addin.csproj -c Release -p:RevitVersion=2026
```

The last command is the normal build. It must not contain the experimental operation ids.

## `experimental.revit_api_lookup` v1

Informational metadata from the Revit API assembly already loaded in the Revit process. The caller cannot name an assembly. Nothing is loaded, compiled, or executed.

Inputs are a closed object. Pass `query` (substring, 2 to 128 characters) or `type_name` (exact public full name), and not both.

Search returns type summaries and the member names that matched. It does not return property or method schemas. Exact `type_name` returns public instance properties and public instance methods. Projectable properties are listed before rejected ones, then by ordinal name, at most 80. Methods are at most 40. Every method has `executable: false`.

A type summary reports `type_name`, `base_type_name`, `is_element`, `is_abstract`, and `can_collect`. `can_collect` is true only for a concrete `Element` subclass.

## `experimental.revit_api_read` v1

Reads instances of one discovered concrete `Element` subclass.

```json
{
  "type_name": "<full name copied from lookup>",
  "properties": ["<property>", "<property>"]
}
```

The example is the shape, not a built-in type. The reader:

- resolves `type_name` only in the loaded Revit API assembly;
- requires a concrete `Element` subclass;
- collects instances with `FilteredElementCollector.OfClass(Type)`;
- orders by `UniqueId` and does not return that id or any `ElementId` integer;
- invokes only the public instance getters named in `properties`;
- accepts at most 16 properties and at most 2000 matched elements;
- pages through the existing cursor envelope.

Scalar values are `string`, `boolean`, `integer`, `number`, a named `enum`, or `null`. Each value is `{ "name", "kind" }` plus the one field that matches the kind (`text`, `boolean`, `integer`, or `number`). An unsupported declared type, an unknown property, a setter-only property, an indexer, a static property, or a property declared outside the Revit API assembly fails the request before any row is returned. A non-finite number fails the same way.

Getters on properties that also have setters may be read. The setter is never invoked. Methods are never invoked.

## Safety limit

Reflection is not a security boundary. A getter can run arbitrary code inside the Revit API, including code that changes the document. The spike does not open a `Transaction`, `SubTransaction`, or `TransactionGroup`. It records `Document.IsModified` around the collection and the getter calls and fails the read if that flag changes. That detects a modification; it does not prevent one that Revit does not report through the flag.

The read runs on the existing EXEC-0001 queue, uses the active-document guard, and stays inside the result budget. There is no source input and no expression language.

## Isolation

Live model facts for this spike come only from `BimIt.Revit.Assistant` talking to `BimIt.Revit.Server`, which calls the spike-enabled Addin. No other MCP server, script host, or direct Revit API console is a source of those facts. Repository builds, unit tests, and API metadata inspection do not count as the live model.

## Classification

Live class: **A — promising**. The record is [VALIDATION.md](VALIDATION.md).

One generic collector and one scalar projector read Levels, Phases, and Design Options on Revit 2026.5. The live run did not add a type-specific reader. Class A is not a production approval. A getter can still change the document without flipping `Document.IsModified`, and reflection is not a security boundary.
