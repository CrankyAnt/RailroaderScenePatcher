<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->

# Marker groups: Boot06 automatic local trial

This report records automatic T-47 trials in a fresh process loading the clean ordinary Sandbox save prepared in [Boot05](../../Boot05/MarkerGroups/Report.md), and in a later editor fixture load. The [authored probe log](MarkerGroupsProbe.txt) records both.

The operator armed the probe on the main menu before loading the save. The native player log identifies `crankyant/scene-operations-map` with `editTarget false`. In world generation 1, the probe logged its ten-second initial wait, one focus request, five seconds of stable origin and the expected map. It applied once and reported `native=True`, one matching registration and one native subscription observing `sy-db`. The post was visible after automatic application. There was no comparable close baseline view at startup, so this trial does not demonstrate a visible before-and-after round trip. The manual [Boot05 trial](../../Boot05/MarkerGroups/Report.md) separately recorded visible placement and restoration.

After the operator opened the native pause menu using the probe helper, native restoration returned `true` and recorded `nativeLast=True`. The native pose was `0.1389771` metres from the captured position and had zero logged rotation difference. The probe then wrote the captured pose and logged zero position, rotation, local-position and local-rotation errors, and restored the original nullable last-apply value `null`. These checks do not establish exact native pose restoration or restoration of all dependent state.

The operator chose Quit and then Discard; Save was not selected. The local clean save's SHA-256 remained `78D3ED746EE47F954A4568EF5C71216D2DCAB7360FDCF3E64B2498069D3711BB`. This trial shows one application after loading an ordinary save. It does not demonstrate persisted changes or repeated applications across loads.

For the editor trial, the operator armed the probe after the default editor world loaded and then opened the custom fixture. The native player log identifies the custom map with `editTarget true`. In world generation 5, the probe logged the initial wait, focus, stable readiness and one application returning `true`. The post was visible after application, but there was no comparable close baseline view. Native restoration again returned `true` with `nativeLast=True` and a `0.1389771` metre position difference before the probe wrote the captured pose. The subsequent recorded pose errors were zero. The operator used the pause helper, restored, then chose Quit from the editor and from the main menu. The editor exit offered no Save choice. The probe log contains no error or dirty-unload entry.

These two successful loads do not establish that ten seconds plus five seconds is a universal readiness guarantee. They do not establish persistence or an exact native restoration of every dependent state.

The pause helper button is present in the source but was clipped when all T-46 controls were displayed. It was accessible while T-47 was armed or its local trial was active. The helper is therefore not reliably visible in every panel state.

The [build manifest](../BuildIdentity.json) identifies the prepared MarkerGroups DLL as SHA-256 `B57EA4B1827DE260D43440E1B66D6B262BB382DB37EDA4150AED8BA9E8CB449C` and the frozen [local trial source](../AuthoredSources/MarkerGroups/LocalPlacementTrial.cs.txt) as `2E4EFC0624A93F1527A2DF9069331B65669C5DF4EB3B7259002DFA3D7BFD6E52`. The manifest identifies the prepared build, not every loaded asset. The original local probe log SHA-256 is `1DCF16400445BCDB79E7CFFC192C098E45A35486B776F015713BFCF77EA09B05`; its LF-normalized copy linked above is `3916B1E60F395819E74176BDF4A6C925048F7B9342C5723B73F8E76B2DC397F4`. The native player log and save remain local.
