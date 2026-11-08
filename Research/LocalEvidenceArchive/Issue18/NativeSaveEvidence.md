# Native sign save evidence

The disposable test placed a Speed sign, edited its text to 15, moved it, and used Save & Quit. The observed before-save SHA-256 is `7847FBE9B2AA772547B6F68DE7EBD28D7C6A04E290BB6EAE9EABA3062A1018C7`. The after-save snapshot SHA-256 is `AC214CD7A31B3B296D2C43B30F01A3BA598744500C44F874551FE2EF0DD878A8`.

Both `.railroad` files are ZIP archives containing a MessagePack `.rrs` member. A local read-only decoder inspected `snapshot.properties.signs`. Before, the map was empty. After, it contained one record with object key `player:0a278500`, `kind` `SpeedRestriction`, `text` `15`, `loc` `Szqa|b|51.90356`, position `(4490.3310546875, 529.0, 5411.541015625)`, and rotation `(-0.0, 250.0010528564453, -0.0)`. The field names are `px`, `py`, `pz`, `rx`, `ry`, and `rz`; units and reference frames were not independently established here. The key identifies the sign record and is not a player account identifier.

This establishes that the save file contains the record after saving. It does not on its own establish that a fresh load restores it, or isolate which of the placement, text, and move actions produced each field. No intermediate save was captured. The before and after archives came from the disposable profile; the after snapshot was copied for inspection and removed afterward. The full archive was not published.

Six JSON files in the original profile's `ModDev` directories were searched read-only for the exact sign key and location string after the test. Neither string appeared. No before-test hash baseline was available, so this inspection does not establish whether those map files changed or which system owns every native sign. Save-file storage is directly observed; a broader ownership conclusion requires code or runtime evidence.
