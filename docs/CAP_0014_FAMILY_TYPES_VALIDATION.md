# CAP-0014 family-type inventory validation

- Status: Not run
- Capability: [CAP-0014](capabilities/CAP-0014-revit-query-family-types.md)
- Bridge: [BRIDGE-0018](bridge/BRIDGE-0018-query-family-types-capability-rpc.md)
- Automated tests do not update the reference numbers below.

Live Revit validation has not been executed. This checklist records the future benchmark. It does not claim PASS and it does not record a live result.

## Reference model

The accepted benchmark for the first live run is:

```text
196 distinct families
1,205 FamilySymbol types
254 placed types
8,278 placed instances
87 used families
109 unused families
```

`used_family_count + unused_family_count` is `196`.

## Isolated MCP client

Run from an MCP client that talks only to BIM-IT Revit MCP. Do not use the Assistant, pyRevit, or another Revit automation host in the same measurement.

Conditions from CAP-0014:

- one Revit instance;
- the benchmark project is the active host project document;
- no second document is active;
- the call is `revit_query_family_types` with `document_id` from `revit_get_context`;
- no category, name, or usage filter on the benchmark query;
- follow `next_cursor` until `complete` is true;
- record the query-level summary from the first page and confirm later pages repeat that same summary;
- confirm the concatenated type rows contain 1,205 rows, no skipped row, and no repeated `type_ref`;
- confirm `tools/list` contains `revit_query_family_types` and the production count is 19;
- confirm a v21-only host returns `BRIDGE_PROTOCOL_INCOMPATIBLE` and does not call `revit_get_mep_device_types` or `revit_query_elements`.

## Checklist

- [ ] Revit version and build recorded
- [ ] Commit SHA recorded
- [ ] Active document identity recorded
- [ ] Unfiltered summary matches the reference numbers
- [ ] Full cursor walk returns every type once
- [ ] A model change after the first page returns `STALE_CURSOR`
- [ ] Restart without a cursor succeeds after that change
- [ ] `OST_Walls` is accepted even though it is outside the MEP placement allowlist
- [ ] No transaction, link traversal, or parameter read is observed
- [ ] Result stays inside the 120,000-character read budget

Do not mark this document PASS from automated tests.
