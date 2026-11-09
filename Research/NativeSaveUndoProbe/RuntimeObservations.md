# Native Save and Undo runtime observations

## Scope and result

On Railroader 0.6.2 build 26335745, the probe and disposable layered map loaded in an isolated game home. The test used [Probe.cs](Probe.cs), the accompanying fixture and Harmony 2.4.2. The installed `Railroader.dll` SHA-256 was `8E9ACFD583C1DA79655C970773D42F2179E999F62DC36A3C6BC3A7B94C27A91E`. The probe uses internal methods and is not a production implementation.

The trial showed a custom two-object change joining the native history and a custom file write occurring on the native Save path. The unmodified editor dirty accounting nevertheless reported a clean document and disabled Save after undoing a saved edit and making a different edit. This prevents relying on that dirty flag alone for the selected persistence guarantee. The test did not evaluate a correction, choose product policy or prove that all native Save paths can be safely coordinated.

## Undo and redo

The baseline was custom state `(0, 0)`, count 0, clean. Applying the probe operation assigned positions to two temporary Unity objects and changed its tracked state to `(1, 2)`. The editor showed one named undo entry, count 1 and dirty. The native Undo button invoked the inverse, reporting `(0, 0)`, count 0 and clean; native Redo reported `(1, 2)`, count 1 and dirty. The probe did not independently read the Unity transforms back after assignment, so the reported values establish the inverse calls, not an independent measurement of the object positions.

For a shared-history check, the native editor created a folder, the probe applied its operation from `(3, 6)` to `(4, 8)`, and the editor created another folder. The first native Undo removed the latter folder and exposed the custom operation as the next undo. The second Undo restored `(3, 6)` and exposed the first folder creation as the next undo. The earlier redo check exercised both tracked values. This is a fixed probe operation; it does not prove correct inverse handling for arbitrary scene graphs.

## Save and controlled failures

The Harmony prefix on `MapEditor.Save` wrote a fixed-value test file before the game's native map write. The editor Save button persisted `(1, 2)` and the native map, then the displayed dirty flag became false. A subsequent custom change to `(2, 4)` made the editor dirty. The probe deliberately threw an `IOException` before its custom write. The existing button displayed `Save Failed`, and retained dirty state. The prefix threw before its custom write; the file contents were not independently inspected between the failed attempt and retry. After disabling the injected failure, retry wrote `(2, 4)` and returned the editor to clean.

The next custom change produced `(3, 6)`. The probe opened the disposable native map with `FileShare.None`; Save wrote the custom state and then the native map write failed with a sharing violation. The editor displayed `Save Failed` and remained dirty. Releasing the lock and retrying saved successfully and returned the editor to clean. This establishes partial success and retry for these specific write points, not atomicity or exhaustive failure recovery. Terrain saving runs before this hook and was not fault-injected.

## Dirty-count collision

After a native folder creation, Save completed at change count 4 with custom data `(3, 6)`. Undo removed the saved folder and reduced the count to 3. A different custom operation changed live state to `(4, 8)` and returned the count to 4. The probe reported `IsDirty=False`; the editor removed its unsaved marker and disabled Save. The custom file still contained `(3, 6)`, and the saved native map still contained the folder that was absent in the editor. The probe's snapshot recorded `state=4,8 dirty=False count=4`, with the custom operation as the next Undo and no Redo. This matches the separately inspected counter implementation.

## Probe limits

The probe installed its prefix on each world load without removing the prior prefix at world unload. Opening the map therefore installed it twice, and the successful Save logs show two custom writes per click. The controlled custom exception stopped the first prefix. Both failure observations and the counter collision remain observations of this exact duplicated-hook experiment; they do not prove a single-registration lifecycle or production-grade integration. Future work must fix and verify that lifecycle separately.

The diagnostic writes fixed test values and does not parse product JSON. The test did not cover retained scene hierarchy, terrain write failure, application termination during Save, cross-mod hook interactions or game versions other than the identified build. No replacement history manager was tested.
