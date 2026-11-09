# Native Save and Undo probe v2 observations

The test used [the v2 probe](Probe.cs) from commit `438dd6b`, the accompanying disposable layered fixture and Harmony 2.4.2 in Railroader 0.6.2 build 26335745. The installed `Railroader.dll` SHA-256 was `8E9ACFD583C1DA79655C970773D42F2179E999F62DC36A3C6BC3A7B94C27A91E`. These internal hooks and the probe are not a production implementation.

## Registration and history

The probe reported one owned `MapEditor.Save` prefix in the initial editor world and one after opening the fixture. Each successful Save in the recorded log produced one prefix call and one custom write.

The probe read `localPosition` from each temporary Unity object. Their baseline positions were A `(0, 0, 0)` and B `(0, 1, 0)`. One custom operation registered one native Undo entry and set A to `(1, 0, 0)` and B to `(2, 1, 0)`. After a successful Save, native Undo restored both baseline positions; native Redo restored both applied positions. At the restored checkpoint the augmented `IsDirty` getter reported false, but the native window title still displayed an unsaved marker. The title therefore did not match the getter in this sequence. The cause was not instrumented; notification ordering is a candidate explanation, not an observed cause. The earlier [v1 shared-history trial](../NativeSaveUndoProbe/RuntimeObservations.md) placed custom history between native folder edits.

## Same-count branch and Save

After creating and saving a native folder at count 2, native Undo removed it and reduced the count to 1. A different custom operation advanced the count back to 2 and changed the actual positions to A `(2, 0, 0)` and B `(4, 1, 0)`. Unlike the unmodified v1 trial, the v2 augmented getter reported dirty and the window displayed its unsaved marker. The native Save button remained usable: clicking it wrote the custom file as `2,4`, wrote the native map without the folder, and returned the displayed probe dirty status to false. This bounded observation demonstrates the branch guard on this sequence; it does not establish universal change detection.

## Controlled failure and retry

A further operation produced actual positions A `(3, 0, 0)` and B `(6, 1, 0)`. The probe's controlled custom-write exception caused the existing Save Failed dialog. The editor remained dirty, the custom file still contained `2,4`, and the native map remained `{}`. After disabling the fault, retry saved `3,6` and the probe reported clean.

The next operation produced A `(4, 0, 0)` and B `(8, 1, 0)`. The probe opened the disposable map with `FileShare.None`. The custom file was written as `4,8` before the native write failed with a sharing violation. Save Failed appeared and dirty remained true; the successful-save checkpoint hook did not run on that failed attempt. Releasing the lock and retrying completed the native Save and returned the probe to clean. These are controlled write points, not a transaction or crash-recovery test. Terrain writing precedes the hook and was not fault-injected.

## Limits

The v2 probe preserves native history actions and augments dirty accounting with tokens for the native undo-group sequence plus two custom values. It depends on internal stack and lifecycle details. The retained-stack limit, non-history mutations, other mods, exceptions during history actions, UI refresh ordering and other game builds were not resolved. In particular, the observed stale title marker means this exact probe does not establish fully consistent native clean-state presentation after returning to a saved state. The trial does not validate a complete patch document, arbitrary scene hierarchies or production behavior.
