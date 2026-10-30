# BIM-IT linked pattern application live validation

A person ran these cases at a Revit 2026.5 session on 2026-10-29 and 2026-10-30. Automated tests do not open Revit and do not approve a write. This record is not a merge verdict.

Decision: [ADR-0021](adr/ADR-0021-controlled-pattern-application-against-linked-architecture.md).

## Setup

| Item | Value |
| --- | --- |
| Revit | 2026.5, build 26.5.0.55 |
| Host | `Snowdon Towers Sample Electrical`, no local Room for the target studios |
| Link | `Snowdon Towers Sample Architectural`, top-level RVT link |
| Code | Uncommitted working tree on `feature/pr95-linked-pattern-write`, based on `b0ed948`. Add-in, Server, and Assistant rebuilt in Release with Revit closed after each fix below |
| Bridge protocol | 21 |
| Wall types | `Duplex Receptacle / Standard` (`WorkPlaneBased`). `Tt_INS_Prise_Courant_Normale_Murale(P) / 15A` (`OneLevelBased`, office standard), loaded by hand before Case E |
| Interior type | `Bollard Light / Incandescent - 277V` (`OneLevelBased`) |
| Verification | pyRevit read-only queries in the same session |

Each case used a new chat with the same sketch: a studio with two wall receptacles as a symmetric pair on the wall opposite the entrance. Case C used one luminaire at the room centre.

## Case A — linked wall-face write

Two Story Studio Unit 204. Studio Unit 203 was unresolved in that chat.

| Step | Expected | Result |
| --- | --- | --- |
| 1 | The MEP host has no Room for the target studio | Pass |
| 2 | The architecture link is loaded | Pass |
| 3 | Pattern Review resolves the studio from the link, with at least two wall devices | Pass, 204 |
| 4 | The wall role offers the loaded `work_plane_based` type; `one_level_based_hosted` options are disabled for that linked room | Pass |
| 5 | Prepare | Pass |
| 6 | Preview is ready | Pass |
| 7 | Undo history and element counts show no model change before approval | Pass |
| 8 | Review opens the native Revit Approval Pane with the linked room count, “WorkPlaneBased linked wall-face placement”, and the link label, and no id, ref, or path | Pass |
| 9 | Apply before approval creates nothing | Pass, “Revit-local approval is still required.” |
| 10 | Press Approve in the Revit pane | Pass |
| 11 | Apply locally | Pass, “Applied 2 pattern placements. Revit verification passed.” |
| 12 | Exactly the expected devices exist in the MEP host | Pass, 538 → 540 Electrical Fixtures |
| 13 | Each device `HostFace` refers to the link instance and a linked Wall | Pass, linked Wall “Exterior - 12 5/8" Rainscreen…” |
| 14 | Locations follow the linked Room geometry | Pass, 3.0 m apart on the wall opposite the entrance |
| 15 | Mounting heights are correct | Pass, 300.0 mm above L2 |
| 16 | Devices face into the linked Room | Pass, upright, work-plane normal (0, −1, 0) into 204 |
| 17 | The architecture link and its document are unchanged | Pass |
| 18 | A second Apply returns the same result and creates nothing | Pass, still 540. The repeated text is misleading, see findings |
| 19 | Prepare is blocked for the terminal generation | Pass |
| 20 | One Undo of `BIM-IT Apply Room Pattern` removes the whole batch | Pass, 540 → 538 |

## Case B — two different linked rooms

Studio Unit 203 and Two Story Studio Unit 204, `Duplex Receptacle / Standard`, 300 mm.

| Expectation | Result |
| --- | --- |
| Each room's placements follow that room's own adapted geometry | Pass, two receptacles in each room |
| Linked wall faces differ where the geometry differs | Not exercised. Both studios face the same linked exterior Wall, so all four devices share its face: plan normal (0, −1, 0), elevation 2.764 m, 300 mm above L2, 1.2 m apart within each Room |
| One controlled batch and one approval | Pass, “Pattern preview is ready for 2 rooms and 4 devices.” |
| Exact total count | Pass, 538 → 542 |
| One Undo removes every device | Pass, one Undo entry, 542 → 538 |

## Case C — linked interior item

Studio Unit 203 and Two Story Studio Unit 204, one luminaire at the room centre, `Bollard Light / Incandescent - 277V`, 2400 mm. The receptacle role that the proposal added was omitted in Pattern Review.

| Expectation | Result |
| --- | --- |
| Host Level resolved by elevation, not by name | Pass, L2 |
| The device is created in the MEP host at the target and height | Pass after two fixes. Elevation from Level 2400 mm, geometry base 2400 mm, unhosted, one in each Room |
| The linked document is unchanged | Pass |
| One Undo removes every device | Pass |

The first attempt used `Recessed Lamp - Round - LED / 4" Trimmed Downlight` (`WorkPlaneBased`) at 2400 mm. The transaction rolled back, so nothing was created. The second attempt placed the Bollard Light's insertion point at 2400 mm. Its Elevation from Level showed 1638 mm, because that family keeps its insertion point 762 mm above its base. Both are fixed, see findings.

## Case D — stale link

A second proposal, prepared and approved, then the link instance was moved before Apply.

| Expectation | Result |
| --- | --- |
| Apply returns stale | Pass, “This pattern preview is stale.” |
| Zero new devices | Pass |
| No `BIM-IT Apply Room Pattern` transaction | Pass, audit `apply_status: stale`, `transaction_status: none` |
| The test model is restored afterwards | Pass, Undo of the move. Link origin back to (0, 0, 0) |

## Case E — level-based office wall family

Not in the original checklist. Studio Unit 203 and Two Story Studio Unit 204, `Tt_INS_Prise_Courant_Normale_Murale(P) / 15A`, 300 mm, found with the role-card type search.

| Expectation | Result |
| --- | --- |
| The type search finds a loaded type beyond the first catalog page | Pass, 62 types for “Tt_INS” |
| `BoundaryLevel` placement is prepared | Pass, 2 rooms and 4 devices |
| The devices are created on the host Level at the mounting height | Pass after one fix. L2, Elevation from Level 300 mm, unhosted |
| Each device stands on the Room-facing finish plane | Pass, all four on y = 12.710 m. A point 30 mm in front is in the Room, 30 mm behind is not |
| Each device faces into the Room | Pass, `FacingOrientation` (0, −1) |
| One Undo removes every device | Pass, one Undo entry |

The first Apply rolled back. Revit reads the point Z of a new level-based instance as an offset above its Level, and its location stays stale until regeneration. The move doubled the offset, so verification failed. This is fixed.

## Findings

Fixed during the live run:

- The first catalog page could hide a type. The role card offered at most 8 types out of 80 per category, and the office family had 62 types among 108 Electrical Fixture types. Each role card now has a type search.
- Only `high` rooms counted for role selection, so a selected `lower` room had no role card. Selected rooms now drive the roles.
- A short non-wall edge next to a device made the wall `ambiguous_wall`. Only wall-backed edges are candidates now.
- A face device was laid on its side. The reference direction is now horizontal along the face, for linked and host faces.
- An office `OneLevelBased` wall family could not be placed on a linked wall. `BoundaryLevel` adds that path.
- A new level-based instance was moved from a stale location, which doubled its height. The document is regenerated before the move.
- A `WorkPlaneBased` interior instance hosted by a Level plane cannot be raised, and its `LevelId` is invalid. A non-zero height is refused before preview with a clear message, and the Level is read from the host.
- The mounting height of a level-based family is now its Elevation from Level, for creation, verification, and occupancy. The 13 Bollard Lights already modelled in the project follow the same convention.
- A proposal added a role that the user had excluded (“aucune prise”) and tagged it user-requested. The model instructions now say that the user's explicit scope overrides the drawing. Each role card can also **Omit this role**. The instruction guides the model and does not guarantee the result, so Omit remains the human safety net.

Open:

- Target reasoning varies between chats for the same sketch and message: resolved or unresolved studios, `high` or `lower` bands, two or eight receptacles, and spacing of 1.2 m, 2.3 m, 2.9 m, or 3.0 m.
- A preview failure does not name the room or the device, for example “A projected point is outside the target room.”
- A second Apply repeats the first result text. It should say that the pattern was already applied and nothing was created.
- The room-name filter only searches the Room name parameter.
- The apply schema caps `created_count` at 20.
- An interior luminaire at ceiling height needs a typed height today. Deriving it from the linked ceiling is a separate decision.
- Families must be loaded by hand. BIM-IT does not load them.
