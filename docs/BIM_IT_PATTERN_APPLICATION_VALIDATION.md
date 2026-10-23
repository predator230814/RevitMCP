# BIM-IT pattern application live validation

This checklist is for a person at a running Revit 2026.5 session. Automated tests do not open Revit and do not approve a write. Cases 1–17 and 19–20 below were observed on 2026-10-23. Case 18 was not shown. This record is not a merge verdict.

Host account for the Copilot seat: `QIB-Dave-CoffeeDrivenCoding`. It was still active at the end of this run. Restore `predator230814` after the run.

Session: Revit 2026.5 build `26.5.0.55`, disposable model `Snowdon Towers Sample Architectural_Test.rvt`, sketch `MCP_Test_Room.png`. The receptacle-only generation used four high rooms, `M_Outlet-Duplex-GFI / Single`, and 300 mm. The table below is the record of what that session observed. Case 18 was not shown in Diagnostics. Adapting the same sketch into the named studios is a later change: this run found those rooms and placed nothing.

Retest at 12:16, same model, seven receptacles only, four high rooms, GFI Single, 300 mm. Prepare was ready for 4 rooms and 28 devices and created nothing. Review and Approve opened the existing pane. Apply returned “The pattern transaction failed.” Journal `0105` finished `BIM-IT Apply Room Pattern` without “Transaction Successful” and without a posted Revit error. Afterwards the document was unmodified and electrical fixtures were 0, so the rollback left nothing behind. That build still failed case 11. Do not retry that generation. The next build accepts a wall-hosted origin when it is on the chosen wall and at the right height; the gap between the in-room point and the wall is not a failure.

Retest at 13:10 with that build, eight receptacles, four high rooms, 32 devices. Apply failed again with the same assistant message. Journal `0106` shows the same regeneration note and no posted Revit error. The document stayed unmodified, with 0 electrical fixtures. A rolled-back placement on the same rooms showed the host, the level, the height, and the position along the wall already matched. Machine RM P02 and P03 faced into the room before regeneration and out of the room after it. `flipFacing` after regeneration kept them facing into the room. The follow-up build flips a hosted device back toward the room after regeneration, then checks again.

Retest at 13:52 with that build. Apply returned “Applied 32 pattern placements. Revit verification passed.” Journal `0107` records `Transaction Successful` for `BIM-IT Apply Room Pattern`. The document was modified. All 32 fixtures are wall-hosted, 300.0 mm above their level, eight in each of Elevator E1, Elevator E2, Machine RM P02, and Machine RM P03. A one-foot probe along each facing direction landed in that same room. A second Apply returned the same success text and created nothing: the fixture count stayed 32, and the journal still has one transaction. Prepare then returned “This pattern already has a placement result.” After view changes, one Undo of `BIM-IT Apply Room Pattern` removed all 32 fixtures. The document was unmodified afterwards. The earlier Undo had removed a view-visibility transaction, not the placement.

Named-room retest after 15:10, same unmodified model and the same sketch. The prompt asked for every studio and for receptacles only. Pattern Review listed six candidate rooms and zero high matches: Studio Unit 203, Two Story Studio Unit 204, Studio Unit 304, Two Story Studio Unit 406, Studio Unit 404, and Studio Unit 504. Every row was rejected with `multiple_boundary_loops`, and the checkboxes were disabled. Nothing was prepared or placed. A separate read of those six rooms found two finish-boundary loops on each. The inner loop is three or four segments. Keeping the outer loop, and letting the model state a relationship instead of only page points, stays out of this record.

| Case | Result |
| --- | --- |
| 1. Attach a hand-drawn room or electrical sketch | Observed |
| 2. Ask in French to find similar rooms, without a schema or coordinate explanation | Observed |
| 3. The pattern is accepted in that same user turn | Observed |
| 4. Pattern Review shows the detected devices and real matching rooms | Observed. 54 rooms: 5 high, 1 lower, 48 rejected |
| 5. Loaded Revit type candidates appear without refs | Observed |
| 6. The user selects a real electrical family type and a mounting height | Observed. GFI Single, 300 mm |
| 7. At least two matching rooms are chosen | Observed. Elevator E1, Elevator E2, Machine RM P02, Machine RM P03, each labeled 90° |
| 8. Prepare preview returns ready and creates no Revit element | Observed. 4 rooms, 32 devices |
| 9. Review opens the existing Revit Approval Pane | Observed after a fresh Prepare. An earlier review failed because the 10-minute intent had expired |
| 10. Apply before approval creates nothing | Observed earlier the same day |
| 11. Approve and Apply create exactly the requested placements | Observed at 13:52. “Applied 32 pattern placements. Revit verification passed.” The 12:16 and 13:10 builds rolled back and left 0 fixtures |
| 12. Boundary devices sit on the target-room walls, at the configured height, on the room-facing side | Observed. 32 wall-hosted fixtures, 300.0 mm above the level, facing probe inside the same room |
| 13. A rotated target room adapts, when one is available | Observed. Elevator E1, Elevator E2, Machine RM P02, and Machine RM P03 were labeled 90°, with eight devices in each |
| 14. Repeated Apply creates nothing | Observed. The second Apply repeated the success text. The fixture count stayed 32 and the journal kept one transaction |
| 15. The terminal pattern cannot prepare another preview | Observed |
| 16. One Undo removes the entire batch | Observed. Undo of `BIM-IT Apply Room Pattern` removed all 32. It was available after the sheet had focus |
| 17. Server tools = 17 | Observed in Diagnostics, including `revit_get_mep_device_types` and `revit_preview_room_pattern_placements` |
| 18. Model tools = 7 | Not shown. Diagnostics lists the server tools only |
| 19. No hidden Revit identity or path appears in the model, chat, or Pattern Review | Observed on the Pattern review card, the approval summary, and Diagnostics |
| 20. A prompt that names studios reads those rooms and does not place into a room the pattern cannot use | Observed after 15:10. Six studios, all rejected for `multiple_boundary_loops`. Nothing placed |

Automated tests on 2026-10-23, Release, passed: Contracts 351, Bridge 226, Server 524, Assistant 122, Add-in 571. Add-in Release builds for Revit 2025, 2026, and 2027 succeeded. Those tests do not open Revit and do not replace the session above.
