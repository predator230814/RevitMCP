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

## User-stated intent retest, 2026-10-27, commit `15d769e`

Revit 2026 was already open on the user-stated-intent build. GitHub CLI during the run was `QIB-Dave-CoffeeDrivenCoding`. The same MEP host, Architectural link, and sketch were used. This was a new chat and Part B only. Prepare, Review in Revit, and Apply were not pressed. This record is not a merge verdict.

The prompt was:

> Pour ce test, utilise cette logique d'aménagement même si le croquis ne permet pas de confirmer chaque relation : l'interrupteur est associé à l'entrée, le luminaire est au centre de la pièce, deux prises forment une paire symétrique et la prise data est sur le mur opposé à l'entrée. Trouve les studios et montre-moi comment BIM-IT adapterait cette logique. N'applique rien.

The proposal was stored in that turn. Tool used: Propose room pattern. The chat reported six studios, the two best at about `0.6287` and `0.6282`, and the other four at about `0.3580` or `0.3563`. It said the four relations were explicit user instructions, that BIM-IT reported no semantic warning, and that nothing was applied to the model.

Pattern Review opened. The heading was Pattern logic. The lines were marked User requested: two receptacles forming a symmetric pair (`pair-1`), one switch associated with the entrance, one ceiling luminaire near room centre, and one data/TV device on an opposite wall. There was no Observed block. Counts were 6 candidate rooms, 0 high, 6 lower, 0 rejected. The visible link text was Snowdon Towers Sample Architectural. The six studios were Two Story Studio Unit 406, Two Story Studio Unit 204, Studio Unit 504, Studio Unit 203, Studio Unit 304, and Studio Unit 404. No spatial ref, UniqueId, element_ref, or `.rvt` path was visible.

Two Story Studio Unit 406 L4 was `lower · 0.6287 · 0°`. Its stepped plan showed two receptacles on the upper arms and the switch and data stacked at the bottom of the stem. No luminaire was visible at the room centre.

Studio Unit 203 L2 was `lower · 0.3563 · 0° · named_room`. Its plan showed a rectangular outline with a notch on the right, one receptacle on the left wall, one receptacle on the right wall, an unlabeled point on the top edge, and the switch and data stacked on the bottom edge. No device sat at the room centre. The switch was not at the right-hand notch.

`named_room` is applied only when the matcher leaves a low-scoring room rejected for shape. The review text is taken from the stored proposal, which kept the four rules. The geometry passed to the matcher is `PageFrame.ForMatching`, and that frame copies the boundary, entrance, and devices only. The semantic rules are not on that frame, so the adapter did not run and the plans are the projected sketch. Entrance, room centre, pair, and opposite-wall placement were not shown.

After the review, Revit Undo was empty. The chat said nothing was applied. The standing assistant buttons were not pressed.

## Matching-frame retest, 2026-10-27, commit `8517dc6`

Revit 2026 was opened after the Release assistant was rebuilt. GitHub CLI during the run was `QIB-Dave-CoffeeDrivenCoding`. The same MEP host, Architectural link, and sketch were used. This was a new chat and Part B only, with the same prompt as the `15d769e` retest. Prepare, Review in Revit, and Apply were not pressed. This record is not a merge verdict.

The proposal was stored in that turn. Tool used: Propose room pattern. The chat said the four relations were taken from the instruction, that the search returned six studios, and that all six were rejected for `unresolved_rule`. It said nothing was applied.

Pattern Review opened. Pattern logic was marked User requested: two receptacles forming a symmetric pair (`prises-paire`), one switch associated with the entrance, one ceiling luminaire near room centre, and one data/TV device on an opposite wall. Counts were 6 candidate rooms, 0 high, 0 lower, 6 rejected. The visible link text was Snowdon Towers Sample Architectural. The list showed only the two highest-scoring rejected rooms, Two Story Studio Unit 406 and Two Story Studio Unit 204. Studio Unit 203 was not in the list. No spatial ref, UniqueId, element_ref, or `.rvt` path was visible.

Two Story Studio Unit 406 L4 was `rejected · 0.6328 · 0° · unresolved_rule`. Its stepped plan showed one luminaire and no switch, receptacle pair, or data device.

Two Story Studio Unit 204 L2 was `rejected · 0.6321 · 0° · unresolved_rule`. Its outline was a different step. Its plan also showed one interior luminaire and no switch, receptacle pair, or data device.

The plans are no longer the projected sketch. The room-centre luminaire was placed. The entrance switch, the symmetric pair, and the opposite-wall data were not placed. Those three relations need one deterministic door, and the proposal had no source-page entrance. Every visible studio stayed `unresolved_rule` rather than guessing a door. Entrance, pair, and opposite-wall adaptation were not shown.

After the review, Revit Undo was empty. The chat said nothing was applied. The standing assistant buttons were not pressed.

## Target-reasoning live, 2026-10-27 evening

Revit 2026 used the same MEP host, the loaded Snowdon Towers Sample Architectural link, and the same sketch. GitHub CLI during the run was `QIB-Dave-CoffeeDrivenCoding`. Before every attempt the running assistant was confirmed to be the Release build at `src\BimIt.Revit.Assistant\bin\Release\net10.0`, started after that build. The 2026 add-in and server were rebuilt at the start of the session. Every attempt used a new chat. Confirm, Prepare, Review in Revit, and Apply were not pressed. Revit Undo was empty after every attempt. This record is not a merge verdict.

### Test 1, first attempt

The proposal was stored. Pattern logic showed the four user-requested relations and the line `Target reasoning was unavailable (target_reasoning_unavailable).` Counts were 6 candidate rooms, 0 high, 6 lower, 0 rejected. The plans were the geometric projection. The fallback behaved as specified: no premature `unresolved_rule`, the geometric review kept, the provider not marked unavailable.

Cause found in code: the response reader expected snake_case keys without a snake_case naming policy, so every valid reply was refused. The prompt also did not state the reply shape. Both were fixed. The unavailable line now names `provider_error` or `unreadable_response`. The assistant was rebuilt.

### Test 1, second attempt

The target pass ran. Counts were 6 candidate rooms, 0 high, 0 lower, 6 rejected. Two Story Studio Unit 406 L4 was `rejected · 0.6391 · 0° · unresolved_rule`, with Target reasoning: Entrance: door connected to Corridor; Resolved: switch, luminaire; Unresolved: receptacle pair, data. The switch sat at the corridor door and the luminaire at the room centre.

Cause found in code: the old opposite-wall rule took the parallel edge farthest from the door. On a real L-shaped Revit outline with short wall jogs, that edge was a jog in a corner. The pair did not fit on it, and an inset toward the centroid could leave the room, so both points were correctly refused. The opposite wall is now the first boundary reached from the entrance moving into the room perpendicular to the entrance wall. The inset follows that wall's inward normal. A test reproduces the L-shaped case. The assistant was rebuilt.

### Test 1, third attempt

Prompt: “Pour ce test, utilise cette logique d'aménagement même si le croquis ne permet pas de confirmer chaque relation : l'interrupteur est associé à l'entrée, le luminaire est au centre de la pièce, deux prises forment une paire symétrique et la prise data est sur le mur opposé à l'entrée. Trouve les studios et montre-moi comment BIM-IT adapterait cette logique. N'applique rien.”

The proposal was stored. Tool used: Propose room pattern. Pattern logic showed the four relations under User requested, pair group `pair-1`. Counts were 6 candidate rooms, 0 high, 6 lower, 0 rejected. All six linked studios were listed: Two Story Studio Units 406 and 204, Studio Units 504, 203, 304, and 404, each `lower · 0° · link Snowdon Towers Sample Architectural`. No temporary key, spatial ref, UniqueId, element_ref, or `.rvt` path was visible.

Two Story Studio Unit 406 L4 was `lower · 0.6371 · 0°`. Target reasoning: Entrance: door connected to Corridor; Pair mapped onto the target wall; Data mapped opposite the entrance; Resolved: switch, luminaire, receptacle pair, data. The switch was at the bottom-left door. The luminaire was in the centre of the L. The receptacle pair and data were on the top wall above the entrance.

Studio Unit 203 L2 was `lower · 0.3633 · 0° · named_room`. Target reasoning had the same four lines and resolved all four relations. The switch was at the bottom-right door. The luminaire was at the room centre. The receptacle pair and data were on the top wall, at the right, facing that entrance.

The two layouts differ and follow each studio's own door and outline. They are not the scaled sketch. The chat said nothing was applied. On both plans the labels of the pair and data overlap on the top wall. The points are distinct.

### Test 2, natural prompt

Prompt: “Voici mon croquis électrique d'un studio. Trouve tous les studios de ce type dans le modèle, comprends la logique d'aménagement et montre-moi comment tu l'adapterais à chaque studio. N'applique rien.”

The source pass understood three observed relations: a receptacle pair on the upper side wall, a receptacle pair on the lower side wall, and an interior luminaire pair. It did not infer an entrance, switch, or data relation, and said GFCI and CTR were uncertain. Counts were 6 candidate rooms, 0 high, 4 lower, 2 rejected. Lower: Two Story Studio Unit 204 and Studio Units 203, 304, and 404. Rejected: Two Story Studio Unit 406 and Studio Unit 504.

Two Story Studio Unit 204 L2 was `lower · 0.6342 · 0°`. Target reasoning: Entrance: door connected to Corridor; Pair mapped onto the target wall; Resolved: luminaire, receptacle pair. All pairs were placed on the same short top wall facing the entrance, including the luminaire pair. That is not a meaningful adaptation of the side-wall pairs.

Two Story Studio Unit 406 L4 was `rejected · 0.6346 · 0° · unresolved_rule`. Target reasoning: Several possible entrance doors remain plausible; Unresolved: receptacle pair. No device was drawn.

The main chat answer was written before the target pass and said it could not show a per-studio adaptation. Pattern Review was completed after that answer. The chat said nothing was applied.

### Result

Test 1 shows target-aware semantic adaptation on linked studios. The target pass chose the corridor door. Deterministic code placed the entrance switch, room-centre luminaire, symmetric pair, and opposite-wall data on each studio's own geometry. Ambiguous doors stayed unresolved. No mutation occurred.

Open limits:

- A pair rule without a bound target edge goes to the wall facing the entrance, so several pair rules collapse onto one wall.
- A luminaire pair is placed as a boundary pair.
- The target pass did not supply separate edges for the observed side-wall pairs.
- The explanation calls every pair a receptacle pair.
- The main chat text can lag behind the final Pattern Review.
- Device labels overlap on short walls.
