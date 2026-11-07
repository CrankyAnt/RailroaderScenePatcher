# East Whittier standalone test

Game 0.6.2 (26335745), only tct26/eastwhittiermowyard 1.0.0 enabled. Fresh process loading existing MapModScopeSandbox, no editor. Data.rl modifyMap executed, map loaded successfully.

Three warnings reproduced:
- starter-ewh-btrk3 references Stky03ew3 at 94.7 m.
- starter-ewh-btrk2 references Stky0sco4 at 31.1 m.
- starter-ewh-btrk1 references Stky0sco4 at 60.0 m.

The mod Data/Map.json explicitly deletes Stky03ew3 and Stky0sco4 with null values. These segment definitions exist in native base Map.json. Neither the mod JSON nor searched core JSON/Relay files define these marker IDs. The runtime reports them as TrackMarkers. Their functional role and scene ownership were not inspected in this test; do not infer a particular role solely from the starter-ewh names.

This is a second mod reproducing the TrackMarker warning family, not a new object family. No additional WRN/ERR/Exception lines appeared at capture. Existing base-game store_5_phys warning is also present. No map data or save edits were performed.
