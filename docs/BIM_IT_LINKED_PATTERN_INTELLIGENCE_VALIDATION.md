# BIM-IT linked pattern intelligence live validation

This checklist is for a person at a running Revit 2026.5 session with an MEP host and a loaded architectural RVT link. Automated tests do not open Revit. This slice does not approve a write and does not place devices into a linked room.

Observed on 2026-10-26 against Release outputs from `43c9ae8`. GitHub CLI during the run was `QIB-Dave-CoffeeDrivenCoding`. It was switched back to `predator230814` after the run. This record is not a merge verdict.

The MEP host was open and BIM-IT Assistant was ready before the prompts. The attached page was accepted by the proposal tool. Pattern Review never opened. Neither prompt reached room discovery or adaptation.

First prompt, with no schema explanation:

> Voici mon croquis électrique d’un studio. Trouve tous les studios de ce type dans le modèle, même s’ils ne sont pas exactement identiques, et montre-moi comment tu adapterais cet aménagement. N’applique rien.

The model replied that it had applied nothing. It reported a room titled « Studio Unit 203 » and eight electrical symbols, three of them labeled GFCI/CTR near the counter. It then said the layout-rule validation failed, including after a correction attempt, so the studios were not searched and no adaptation was generated. Tool used: Propose room pattern.

Second prompt, stating the relations explicitly:

> Interprète le pattern comme suit : interrupteur associé à l’entrée, luminaire au centre, deux prises comme paire symétrique, et adapte ces relations aux studios.

The model restated those three relations and again reported that validation failed after another attempt. The proposal was not stored. The studios were not searched. Tool used: Propose room pattern.

The break is the semantic proposal. The model can describe the sketch and the requested relations in prose, and it cannot emit a `bimit_propose_room_pattern` payload that passes validation. Linked-room discovery, the six studios, and deterministic adaptation were not exercised.

After both prompts the MEP file was not modified, Undo had nothing to undo, and the Approval Pane did not open.

| Case | Result |
| --- | --- |
| 1. The host model contains no required local Rooms | Not shown. An MEP host was open. Local Rooms were not counted. |
| 2. BIM-IT discovers real Rooms from the Architecture link | Not reached. The proposal was rejected before the read. |
| 3. Attach a hand sketch | Observed. A page was attached and the proposal tool ran. |
| 4. A natural French prompt works without a schema explanation | Observed failure. The prompt was sent as written. Validation rejected the proposal. |
| 5. BIM-IT visibly explains what it understood | Observed in chat only. Pattern Review did not open, so the deterministic “BIM-IT understood” summary was not shown. The chat named Studio Unit 203, eight symbols, and three GFCI/CTR symbols near the counter. |
| 6. Snowdon-style rooms with inner loops remain usable | Not reached |
| 7. Target candidates may differ geometrically | Not reached |
| 8. A switch/entrance relation adapts to each target entrance | Not reached |
| 9. A room-centre device adapts independently | Not reached |
| 10. At least one relational/pair rule adapts across rotated or different layouts | Not reached |
| 11. Pattern Review shows deterministic projected results | Not reached. Pattern Review stayed closed. |
| 12. No Revit model mutation, no approval pane, and Undo unchanged | Observed. The MEP file was not modified. Undo had nothing to undo. The Approval Pane did not open. |
| 13. No hidden host/link identity or filesystem path | Not shown in Pattern Review. The chat text contained no spatial ref, UniqueId, element_ref, or `.rvt` path. |

## Robustness amendment, not yet retested

The morning failure stands. A later change on this branch treats semantic rules as optional. A structurally valid visual pattern is stored even when a relationship cannot be read. One `paired_symmetric` rule means one pair. Pattern Review says when semantic relationships were not established.

## Evening retest, 2026-10-26, commit `dc7a8e1`

Revit 2026 was started after the robustness build. The loaded add-in was `src\BimIt.Revit.Addin\bin\2026\Release\net8.0-windows\BimIt.Revit.Addin.dll`. GitHub CLI during the run was `QIB-Dave-CoffeeDrivenCoding`. The same MEP host, Architectural link, sketch, and natural French prompt were used. No second prompt was sent. Prepare, Review in Revit, and Apply were not pressed. This record is not a merge verdict.

The proposal was stored in that turn. Tool used: Propose room pattern. Pattern Review opened. The chat said the Revit model was not modified. Undo was empty. The Approval Pane did not open.

BIM-IT understood showed two accepted relationships: one receptacle on the nearest wall, and one switch on the nearest wall. The chat said the other sketch symbols were not translated into placement rules. The room previews still showed several switches and receptacles spread around the walls. No entrance was marked. No device sat at the room centre. No symmetric pair was shown.

Six linked studios from Snowdon Towers Sample Architectural were listed. Studio Units 203, 304, 404, and 504 were `lower` and `named_room`, at 0° . Units 203 and 304 were both `0.3538`. Unit 504 was opened and was also `lower`, `0°`, `named_room`. Two Story Studio Units 204 and 406 were `rejected` with reason `boundary_too_complex`. The link label was the sanitized sample name. The review did not show a spatial ref, a UniqueId, `element_ref`, or a filesystem path.

| Case | Evening result |
| --- | --- |
| 1. The host model contains no required local Rooms | Not counted. The same MEP host was open. |
| 2. BIM-IT discovers real Rooms from the Architecture link | Observed. Six studios were listed from the loaded link. |
| 3. Attach a hand sketch | Observed. The same sketch was attached. |
| 4. A natural French prompt works without a schema explanation | Observed for storage. The prompt was sent as written and the proposal was stored. |
| 5. BIM-IT visibly explains what it understood | Observed in Pattern Review for the two nearest-wall relationships. The other sketch relationships were not established. |
| 6. Snowdon-style rooms with inner loops remain usable | Partial. Units 203, 304, 404, and 504 stayed reviewable. Units 204 and 406 were rejected for `boundary_too_complex`, not for an inner loop alone. |
| 7. Target candidates may differ geometrically | Not shown. Units 203 and 304 scored `0.3538` at `0°` with the same symbol arrangement. |
| 8. A switch/entrance relation adapts to each target entrance | Not shown. The accepted switch relationship was the nearest wall. |
| 9. A room-centre device adapts independently | Not shown. |
| 10. At least one relational/pair rule adapts across rotated or different layouts | Not shown. No pair rule was established. No candidate was rotated. |
| 11. Pattern Review shows deterministic projected results | Observed. Pattern Review showed the understood lines, the sketch, and a plan for the selected studio. |
| 12. No Revit model mutation, no approval pane, and Undo unchanged | Observed. Undo was empty. The Approval Pane did not open. The buttons were not pressed. |
| 13. No hidden host/link identity or filesystem path | Observed in the opened review. The visible link text was the sanitized sample name. |

## Final mini-live, 2026-10-27, commit `8c11a86`

Revit 2026 was restarted on the cleaned-loop build. The loaded add-in was the Release 2026 development add-in. GitHub CLI during the run was `QIB-Dave-CoffeeDrivenCoding`. The same MEP host, Architectural link, and sketch were used. Prepare, Review in Revit, and Apply were not pressed. This record is not a merge verdict.

Part A used the same natural French prompt. The proposal was stored in that turn. Tool used: Propose room pattern. Pattern Review opened. Six linked studios were listed: 0 high, 6 lower, 0 rejected. Two Story Studio Units 204 and 406 were `lower`, not `boundary_too_complex`. Unit 406 was `lower · 0.6243 · 0°` and its preview showed the stepped outline. BIM-IT understood showed one relationship: one receptacle on the nearest wall. The chat said the sketch had eight symbols, that the `CTR` symbol stayed uncertain, and that nothing was applied.

Part B was a new chat with the same sketch. The prompt stated an entrance switch, a centred luminaire, a symmetric receptacle pair, and a data device on the wall opposite the entrance. The model repeated those four relations in prose. It said the entrance was not clear on the sketch, so it would keep the relations without assigning a position. It did not call Propose room pattern. Pattern Review did not open. The deterministic adaptation engine was not exercised. Entrance, room centre, pair, and opposite-wall placement were not shown on a linked studio.

Undo was not reopened during this restart. The chat for Part A said the model was not modified, and no approval pane was opened by either prompt.
