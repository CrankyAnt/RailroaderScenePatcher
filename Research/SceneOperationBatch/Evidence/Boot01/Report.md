# Boot01 runtime evidence

Railroader 0.6.2 build 26335745 ran with Unity 6000.3.26f1 in the disposable RuntimeHome. The native extract records a D3D12 info queue query failure; the run continued. The installed probe DLL hashes differ from the preparation build manifest at commit e1ccd8ea69fa40fc27d4e09301cb8b5905665991. Their exact source identity is unverified. Their installed DLL SHA-256 values were:

- BridgeContactProbe.dll: `02DFBF670A9B98E3624C468E414818125BDCFD3FECAC9B08882730E2C1418F9E`
- TerrainOutputProbe.dll: `66CAF1F9DA47FE270D8014DBCE121141D06F8E28BB6371E57F4D9880DA4052D9`

The manifest records bridge DLL SHA-256 9B645F9C9D1E8945922F7F36C8E1C9E4094F91A35C2BBBE535A7587EBF505649 and terrain DLL SHA-256 F94AA81881D34A3238415D4CB21ED806EB915D623B43C31935325B02C1938780. The cause of the mismatch is unverified.

## Bridge

The baseline bridge root game position was (8616.002, 545.980042, 7274.759). The move changed its game X by 5 and its rotation by approximately 15 degrees. A second move produced the same recorded root pose. The observed origin remained (8500, 0, 7000). The approach root was captured, but its `entries.targets` were not captured. This run cannot establish their state.

A spawned ball recorded an actual bridge collision at baseline, after moving, and after restoration. The contact points were respectively (114.303543, 553.238, 271.220917), (118.446533, 553.238, 271.780731), and (114.303543, 553.238, 271.220917). The preceding ray hit is not the contact measurement.

When the root was inactive, renderers remained enabled but were not visible; collider components remained enabled and their bounds had zero size. The culler still reported Visible. After restoration, the root was active, the culler token was present and its sphere returned to its baseline center. The probe reported exact local transforms and activity, matching collider shapes, and world pose within 0.01 m and 0.01 degrees. It unloaded with dirty=False.

The operator observed the bridge move and rotate, disappear while disabled, and visibly return in the local images. These observations do not prove every dependent game system updated.

## Board and terrain output

The Sylva sign moved by 5 game units and rotated, was disabled, and returned to its baseline local position and rotation. The origin remained (25500, 0, 0), and the probe reported originChanged=False. The operator observed the board move, disappear and return in the local images.

After move, disable and restore, native tile builds 65, 66 and 67 respectively covered the same tile centered at (25750, 0, 250). Immediately after each operation the probe recorded pending=1 and idle=True; later samples recorded pending=0 and idle=True. The later TerrainData instance IDs were -362266, -362512 and -362754, compared with baseline -341756. Both sampled sites retained their respective baseline digests: `6E94B93E476E7FFE9E897901551F1BEA4115EEA2A7665159779CCE4A830D4BFD` and `C3DF6C24B1F8E684C9FECB722A685470AB46D7F6AEEEA85A8B57942B188E7021`.

Every sample reported zero TerrainData tree instances within the sampled radius. Detail and GPU output were not sampled. These observations do not establish GPUI tree output or a rendered-tree change. The probe unloaded with dirty=False.

## Provenance

The authored probe logs retain all source lines. They are UTF-8 without BOM and use LF. The terrain log replaces each distinct GUID consistently with a per-log numbered alias in first-occurrence order. The bridge log had no GUIDs and required no byte change. The native extract retains source lines 6, 16, 40 and 41 with line numbers, and excludes the rest of the native log. It uses UTF-8 without BOM and LF. SHA-256 values below identify exact bytes.

| File | Source lines | Original SHA-256 | Retained SHA-256 |
| --- | --- | --- | --- |
| [BridgeContactProbe.txt](BridgeContactProbe.txt) | 1-851 | D7D91449B03AC8B600A8AA19878E2D687C9E4359BAB12FE3E366C30B8F4125F9 | D7D91449B03AC8B600A8AA19878E2D687C9E4359BAB12FE3E366C30B8F4125F9 |
| [TerrainOutputProbe.txt](TerrainOutputProbe.txt) | 1-393 | 2228D7FE842D2A64FF01455E99DFED538A1EFB37A4EAB32DE179F74E926D6EDA | 357B68A2E708552239502E11CDDC27C82132D2DF84823738C1ACC826BBF68CC6 |
| [Player-extract.txt](Player-extract.txt) | 6, 16, 40-41 | A937F7EB819ED77C874EF82A3BB4BC6D9B47296786CB1A2E493F9CBE530F7197 | 39AB1B0247A39A9D92098F52E97BAF0F8D302EA7CDE87FF264A4A6B1E8AE7D8D |

The local screenshots are not included. Their SHA-256 values are:

| Image | SHA-256 |
| --- | --- |
| BridgeBaseline.jpg | 09677A6F6CCFBEAFF8CD62C7FE69409C02FB90CB5ACEB75BBAC8F17A7A8430FA |
| BridgeMoved.jpg | A2D891E0849DF002244E0C4E6CCC21E3DA9E60D2789487F33679D446D0A1892A |
| BridgeDisabled.jpg | 654D9A281D66E3BC74BB49F7AAF1B3AF65CB9C1FA398E555AE2D692F3866297D |
| BridgeRestored.jpg | 98EDDA2A3237EB84EFFC330AFB7320717B3AACD8A0CD968733CD0008789B312F |
| BoardBaseline.jpg | EAE6F510CCF5C350AAFFF34AC50A4FC498B00F05B3B474DE9421FEB379B91653 |
| BoardMoved.jpg | E607F602BAEDF659372CBDA74AF4547CEEE3E5D1D48688B8AB3029775BBAC4C2 |
| BoardDisabled.jpg | C70A96B17976A33EF9606B96C91BCCE0D79B81AF70CFC07E9166FC1B441FC856 |
| BoardRestored.jpg | 6B901A8A33A4E4A3DCDDC4E3AD563B3574E1565CFD5517ADBE7A1F9304CFDBCE |
