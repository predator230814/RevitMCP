# Pattern intelligence live checklist — Revit 2026.5

- Status: Not executed. This checklist is not a merge verdict.
- Date: 2026-10-21
- Target: Revit 2026.5, read-only

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
