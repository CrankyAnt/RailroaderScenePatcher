# Crossing gate first-activation hypothesis

This note records the observed first-activation gate movement and a matching static mechanism during issue 21 research. The original crossing reproduced the movement without adding the detection circuit in that session. The observer was loaded and the world used a custom fixture with an added graph. A separate uninstrumented base-map control is still needed to assess those factors.

## Sources and scope

The inspected installed assemblies are `Railroader_Data/Managed/Railroader.dll`, SHA-256 `8E9ACFD583C1DA79655C970773D42F2179E999F62DC36A3C6BC3A7B94C27A91E`, and `Railroader_Data/Managed/KeyValue.Runtime.dll`, SHA-256 `5C6B3BD5AC0B3DE4484E56FC9B379521F1B336B0D04649DC1A8F3CB23937CE53`. Method and field names below locate the inspected metadata and method bodies. The findings apply to these builds. Runtime observations are retained in [original-circuit control diagnostics](Evidence/NativeControl.diagnostics.log), [samples](Evidence/NativeControl.csv) and [provenance](Evidence/NativeControl.provenance.json). The observer implementation is [GateObserver.cs](CrossingDetectionProbe/GateObserver.cs). The control used probe module version ID `11d8775c-8192-48f4-b28e-3375e6e8871a`.

## Observed behavior

During the first physical activation after a restart with the additional circuit, the observer recorded both raised arms moving approximately 79.63 degrees in one frame while `lights=True`, `gates=False`, and `bell=True`. Both then opened over several seconds. The gates output became true later and the arms closed normally. The frame observer did not record a true gates output before the jump.

The subsequent original-circuit control reproduced the physical jump with no detection-circuit addition in that boot. At frame `15371` in the local log, both arms moved 79.63409 degrees with the same output values. The previous frame had no `gates` key, which read as null and false, and each playable had time `-213.3647` and speed `-1`. The current frame had a present boolean false key, playable time `4.958333`, speed `-1`, and clip length `4.958333`; both clips were non-looping and `invert` was false. The observer records frames, not the intervening callback or the exact order of intra-frame operations.

## Static mechanism

`Track.Crossings.GradeCrossingController.<LoopCoroutine>d__36.MoveNext` compares desired lights, gates and bell values with the current outputs. When any differs, it writes all three, including a false gates value when the lights first become true. Its signal properties use `KeyValue.Runtime.KeyValueObject` indexers.

`KeyValue.Runtime.KeyValueStorage.Get` returns a null value for an absent key, and `Value.BoolValue` converts null to false. `KeyValueStorage.Set` suppresses a write equal to an existing value, but an absent key has no existing value to compare. Thus the first explicit false write can add the gates key and notify its observers even though the preceding read was false. `KeyValueStorage.NotifyObservers` selects observers for the written key. Sources: `KeyValueObject.get_Item`, `set_Item`, `KeyValueStorage.Get`, `Set`, `NotifyObservers`, and `Value.get_BoolValue`.

`RollingStock.Controls.KeyValueBoolAnimator.OnEnableWithProperties` creates a playable when needed, positions it at the target time for the current boolean, sets its speed, starts it and observes the configured key. For a non-looping clip, `Speed(false)` is negative and `TargetTime(false)` is zero; inversion changes the boolean before these calculations. `PropertyChanged` checks the current playable time. If it is below zero or above the clip length, it sets the time to the opposite boolean's target before setting the speed for the new value. `Helpers.Animation.PlayableHandle` forwards its time and speed accessors to the underlying playable. Sources: the named animator methods, `Game.GameBehaviour.OnEnable`, `PlayableHandle.get_Time`, `set_Time`, `get_Speed`, and `set_Speed`.

The control's recorded key transition and playable values match this mechanism: an absent key reads false, the time is below zero, and the first observed false value coincides with a reset to clip length and the physical jump. The inspected code supplies a plausible callback path for that reset. The frame observations do not directly intercept the callback or prove the order of the operations inside that frame.

## Discriminating observation

The control measured key presence through `KeyValueObject.Keys`, the `Get("gates")` value's `Type`, `IsNull`, and `BoolValue`, and each consumer's `invert`, `animationClip.length`, `animationClip.isLooping`, and playable `Time`, `Speed`, and `PlayState`. These readings support the proposed chain. An in-range time at the actual notification or evidence of another writer resetting the playable would challenge its attribution. An uninstrumented base-map first passage can test whether the physical behavior also occurs without the probe and custom fixture, but would not by itself identify the callback.
