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

The 2026-10-26 failure stands. A later change on this branch treats semantic rules as optional. A structurally valid visual pattern is stored even when a relationship cannot be read. One `paired_symmetric` rule means one pair. Pattern Review says when semantic relationships were not established.

The focused live mini-gate has not been re-run. Dave will run it. No case above is marked passed because of this amendment.
