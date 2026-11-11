# SERVER-0015: Read-operation tools

- Status: Accepted. Not implemented.
- Date: 2026-11-11
- Capability: [CAP-0016](../capabilities/CAP-0016-revit-read-operations.md)
- Bridge: [BRIDGE-0020](../bridge/BRIDGE-0020-read-operations-capability-rpc.md)

## Tools

Two stdio MCP tools:

| Name | Title | Hints |
|---|---|---|
| `revit_lookup` | Lookup Revit read operations | `readOnlyHint=true`, `openWorldHint=false`. `destructiveHint` is not set. |
| `revit_read` | Read a registered Revit operation | `readOnlyHint=true`, `openWorldHint=false`. `destructiveHint` is not set. |

The Server holds no operation list, no operation schemas, and no catalogue of operations from other builds. It validates the generic envelopes, routes to the selected instance, applies the existing capability timeout, and maps errors.

### `revit_lookup` description

State, concisely and in this order:

1. it lists or describes BIM-IT registered read operations on the connected Revit host;
2. it does not describe the Revit API;
3. omit `operation` to search, with an optional `query`, `offset`, and `limit`; search returns summaries and no schemas;
4. pass `operation` to get the full descriptor, including input and output schemas; `contract_version` is optional in that mode;
5. a reachable Revit host is required, and an active document is not.

### `revit_read` description

State, concisely and in this order:

1. it runs one registered read operation on the active host project;
2. `operation` and `contract_version` come from `revit_lookup`; there is no default version;
3. `document_id` comes from `revit_get_context`;
4. `inputs` must match the operation schema from lookup;
5. `page_size` and `cursor` are envelope fields for operations whose `pagination` is `cursor`, and they are rejected for `pagination` `none`;
6. follow `next_cursor` with the same operation, version, and inputs until `complete` is true.

## Routing

`instance_id` is optional and uses the same selection rules as `revit_query_family_types`. Eligibility is `BridgeProtocol.SupportsReadOperations`, which is protocol 24 only. A selected protocol of 23 or 25 is not eligible. The advertised version list is not the eligibility test.

The Server removes `instance_id` before the Bridge call.

Without `instance_id`, the Server selects only a Ready host whose negotiated protocol supports `{24}`. With an explicit `instance_id`, a Ready host at protocol 23 returns `BRIDGE_PROTOCOL_INCOMPATIBLE`. No other capability is called in its place.

Both tools remain in `tools/list` when the selected host cannot negotiate protocol 24. The failure is the protocol error above, not omission of the tool.

## Envelopes

Both input schemas are closed objects: `additionalProperties` is false. A JSON `null` for an optional field means the field is omitted, and the schema accepts that null. Bounds apply to the non-null value, as the existing validator already does. Exact and search mode treat null exactly as absence. Required `document_id`, `operation`, `contract_version`, and `inputs` do not accept null.

`revit_lookup` input:

```json
{
  "type": "object",
  "additionalProperties": false,
  "properties": {
    "instance_id": { "type": ["string", "null"], "minLength": 1 },
    "operation": { "type": ["string", "null"], "minLength": 1, "maxLength": 64 },
    "contract_version": { "type": ["integer", "null"], "minimum": 1 },
    "query": { "type": ["string", "null"], "minLength": 1, "maxLength": 256 },
    "offset": { "type": ["integer", "null"], "minimum": 0 },
    "limit": { "type": ["integer", "null"], "minimum": 1, "maximum": 50 }
  }
}
```

`revit_read` input:

```json
{
  "type": "object",
  "additionalProperties": false,
  "required": ["document_id", "operation", "contract_version", "inputs"],
  "properties": {
    "instance_id": { "type": ["string", "null"], "minLength": 1 },
    "document_id": { "type": "string", "minLength": 1 },
    "operation": { "type": "string", "minLength": 1, "maxLength": 64 },
    "contract_version": { "type": "integer", "minimum": 1 },
    "inputs": { "type": "object" },
    "page_size": { "type": ["integer", "null"], "minimum": 1, "maximum": 200 },
    "cursor": { "type": ["string", "null"], "minLength": 1, "maxLength": 2048 }
  }
}
```

`inputs` is an open object in this envelope. The supported subset cannot express "closed, but the properties depend on the host registry" without `oneOf`. The Addin enforces the operation schema. Unknown properties of `inputs` therefore pass this envelope and fail as `INVALID_READ_OPERATION_INPUT`.

The MCP output schema for each tool describes the CAP-0016 envelope and is a closed object. `revit_read.result` is typed as `object` and is not closed here, because the payload belongs to the operation. On `revit_lookup`, `input_schema` and `output_schema` are typed as `object` and are not closed here, because they are schema documents. Search responses omit those two properties. The output schema allows them to be absent.

`ClosedSchemaArgumentValidator` delegates instance checks to `JsonSchemaSubset` and remains the MCP argument gate. Extraction must not start rejecting current production tool schemas. Those schemas use `description`, `title`, and `default`, which CAP-0016 allow-lists as annotations, and they rely on unknown keywords being ignored during instance validation.

## Catalog

Production `tools/list` contains exactly these 22 tools:

```text
revit_apply_mep_device_placements
revit_apply_parameter_updates
revit_describe_parameters
revit_get_context
revit_get_elements
revit_get_mep_device_types
revit_get_mep_topology
revit_get_parameter_values
revit_get_room_layouts
revit_get_selection
revit_get_warnings
revit_lookup
revit_preview_mep_device_placements
revit_preview_parameter_updates
revit_preview_room_pattern_placements
revit_query_elements
revit_query_family_types
revit_query_parameter_bindings
revit_read
revit_read_spatial_rooms
revit_request_mep_device_placement_review
revit_request_parameter_update_review
```

No write tool is added. `revit_query_family_types` and `revit_query_parameter_bindings` are unchanged.

## Errors

Envelope failures caught by the MCP schemas map to `INVALID_REQUEST`. Mode combinations that the lookup schema cannot express map to `INVALID_READ_OPERATION_LOOKUP` from the Addin. The Server does not rewrite capability codes.

Routing codes stay `NO_REVIT_INSTANCE`, `INSTANCE_REQUIRED`, `INSTANCE_NOT_FOUND`, and `INSTANCE_UNAVAILABLE`.

Capability and bridge codes passed through:

```text
UNKNOWN_READ_OPERATION
READ_OPERATION_VERSION_UNSUPPORTED
INVALID_READ_OPERATION_LOOKUP
INVALID_READ_OPERATION_INPUT
READ_OPERATION_RESULT_INVALID
NO_ACTIVE_DOCUMENT
DOCUMENT_CONTEXT_CHANGED
UNSUPPORTED_DOCUMENT_KIND
INVALID_CURSOR
STALE_CURSOR
RESULT_TOO_LARGE
REVIT_EXECUTION_TIMEOUT
REVIT_EXECUTION_FAILED
BRIDGE_PROTOCOL_INCOMPATIBLE
```

The timeout is the existing capability timeout. Structured content remains authoritative. Error text does not add a stack trace, a path, or raw Revit exception text.

## Acceptance

- `tools/list` is exactly the 22 names above.
- A protocol 23 host fails both tools with `BRIDGE_PROTOCOL_INCOMPATIBLE` and still serves `revit_query_parameter_bindings`.
- Lookup and read schemas reject unknown envelope fields before the Bridge call.
- An unknown property inside `inputs` reaches the Addin and returns `INVALID_READ_OPERATION_INPUT`.
- The Server assembly gains no operation descriptor and no reference to a workset type.
