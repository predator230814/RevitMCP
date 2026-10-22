# Pattern intelligence live checklist — Revit 2026.5

- Status: Exercised on 2026-10-22. This record is not a merge verdict and does not authorize pattern application.
- Date: 2026-10-22
- Target: Revit 2026.5, read-only
- Build: local Release outputs from `52696703aa17927467f847aa6fb6ad559cc16d6a`. The Revit About build number was not read during this session.

Run this against a project that contains several bedrooms of one ordinary rectangular type, including at least one rotated or mirrored instance when the model has one. Attach a hand sketch or image of one bedroom electrical layout. Do not save the model as part of the check.

1. Attach the sketch or image in BIM-IT Assistant.
2. Ask BIM-IT to interpret that one room and find the same room type. An example prompt is: “Voici mon croquis électrique d’une chambre. Trouve les chambres du même type dans Revit et montre-moi comment tu appliquerais ce pattern.”
3. Pattern Review shows the attached page, the interpreted room boundary, and the detected device markers and roles.
4. BIM-IT reads real Revit rooms through `revit_get_room_layouts`. Diagnostics lists 15 server tools.
5. More than one genuinely similar room is identified.
6. A rotated or mirrored occurrence is labeled with that adaptation when the model contains one.
7. The WebView shows projected device markers for a selected representative room.
8. No Revit element is created or modified. Undo is unchanged by this request.
9. The chat and Pattern Review do not show a filesystem path, `instance_id`, `document_id`, or `element_ref`.
10. The model surface remains the seven tools. The three raw MEP write tools stay hidden. No approval pane opens.

Record the Revit build, the git SHA of the build, and each case as observed or not observed. A pass of this list still does not authorize pattern application.

## Execution — 2026-10-22

Host: Revit 2026.5. The About build number was not read. The active document was Snowdon Towers Sample Architectural, opened from the Revit 2026 samples. The active view was the document starting view; the model described it as sheet Cover. GitHub CLI during the run was the company account `QIB-Dave-CoffeeDrivenCoding`. The provider chip showed `GitHub Copilot · auto`. Revit/Bridge, Assistant, and MCP were ready. The file was not saved.

| Case | Result |
| --- | --- |
| 1. Attach one page | Observed. Chip `MCP_Test_Room.png`, `Image · 1 page`. No filesystem path. |
| 2. Interpret and find the same room type | The first prompt called `bimit_propose_room_pattern` and the tool rejected the payload, so nothing was stored. A second prompt that restated the normalized coordinate and role contract stored one boundary and 8 devices. |
| 3. Pattern Review shows the page, boundary, and device roles | Observed. The sketch, a room outline, and `receptacle` markers were visible. |
| 4. Real rooms and 15 server tools | Observed. Pattern Review reported 54 candidate rooms. Diagnostics listed `Tools: 15`, including `revit_get_room_layouts`. |
| 5. More than one similar room | Observed as geometry, not as bedrooms. The document has no bedroom room type. Five matches were high and one was lower. The visible high matches were Elevator E1, Machine RM P02, and Machine RM P03. |
| 6. Rotation or mirror label | Rotation observed. Elevator E1, Machine RM P02, and Machine RM P03 were labeled `90°`. No displayed match was labeled mirrored. |
| 7. Projected device markers | Observed for Elevator E1 Parking: `high · 0.9439 · 90°`, with `receptacle` markers on the room outline. |
| 8. No Revit edit | Observed. The answer said the model was not modified. Undo stayed disabled because no Revit command had run. |
| 9. No path or hidden identity | Observed in the shown chat and Pattern Review. Labels were room names. No filesystem path, `instance_id`, `document_id`, or `element_ref` was visible. |
| 10. Model surface and approval | No approval pane opened. The successful turn reported only `Used: Propose room pattern`. Diagnostics is the server list, so it includes the three raw MEP write tools; those tools were not offered to the model and were not used. The pane does not print the seven model-tool names. |
