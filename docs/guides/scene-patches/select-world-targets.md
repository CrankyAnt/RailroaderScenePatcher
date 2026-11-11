<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Select targets in the world

Open **Scene patches** on the right side of the map editor. Choose **Select scene object in world** and click a blue object marking. The markings use the positions of supported inventory objects, including inactive objects. Check the candidate's name, scene, path, component, and any identifiers in its binding details. Choose **Confirm identity** to select that object for inspection. For an editable object, the placement fields then show the inspected pose.

You can click the square marking, the outlined object bounds, or a displayed identity label. Labels keep a fixed screen text size on their opaque label backing. Markings and labels stay outside native windows and controls.

Expanded object bounds come from enabled renderers on active objects. An inactive object remains selectable through its drawn square marker and identity label. When the source is also offered in an overlap, its label retains **Choice** until you explicitly choose it as the **Candidate**.

With a scene object inspected, start **Choose replacement object**, **Choose replacement segment**, or **Choose replacement node**. The yellow source marking and the source name in the panel identify the object the choice belongs to. For a segment, point at its track in the world. For a node, click its blue marking. The panel shows the segment ID and both endpoint IDs, or the node ID and map position.

Hovering a track shows the selectable ribbon around its curve, covering the rails and ties with a minimum screen width at a distance. Clicking a shared endpoint or overlapping ribbons offers every matching candidate for an explicit choice.

The green marking identifies the candidate under consideration. If several candidates overlap at the pointer, choose one from the list by its identity or move the camera and pick again. Use **Focus candidate** to check the location. You can navigate outside the panel and inspect other inventory entries while the choice stays open for its original source.

Use **Confirm identity** after checking the candidate. The panel records the confirmed identity in the loaded map context. If the candidate is missing, ambiguous, or has changed since the pick, select it again after checking the world. If the source or loaded map context changed, cancel and start again.

Use **Cancel selection** to close a pending choice. A pending choice does not change the scene, patch document, or completed edit history. Confirmation records a choice for an editor operation. The [v1 document reference](scene-patch-document-v1.md) describes the supported patch fields and application behavior.

Confirm or cancel the choice before applying placement or activity changes in **Scene patches**. Completed edits still use the map editor's **Undo**, **Redo**, and **Save** controls. See [edit and save fixed placement in the map editor](editing-placement-in-the-editor.md).
