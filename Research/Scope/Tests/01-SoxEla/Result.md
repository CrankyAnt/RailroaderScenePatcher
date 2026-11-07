# Sox Ela standalone test

Installed game: 0.6.2 (26335745). Only sox/ser 1.0.0 enabled. Unmodified native pack; no repair DLL in Mods or ModDev. Route: fresh process, load existing MapModScopeSandbox save, no editor.

The log confirms sox/ser/Data.rl modifyMap executed. ActiveMapDefinition reports layers (none), so that field alone does not establish that no Data.rl modifications ran.

Four warnings reproduced: TrackMarker M5c0 on wk44ps, M5ib on Sqy8, Muef on Sqc5, and Ela-water-column failing its Skbi terrain-snapped anchor. These match the known failures. No additional WRN/ERR/Exception lines were present at capture. This does not test every geographic tile or feature activation.
