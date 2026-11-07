# Sylva editor lookup attempt

Opened connor/basegame1 from kater/sylvainterchange ModDev through Edit Map. No map mutation or Save action was performed.

UI observations: searching Mjd8 returns no rows. Sozu and Shuo also return no rows. Native object IDs can be searched and selected; Focus navigates to their positions. Used OKatSylInt_circlemask near the milepost and sy-paperboard-powerhouse / sy-wye-3 near the whistle sign. The inherited scene marker itself cannot be selected through the inspected outline.

Code confirms the outline does not index scene TrackMarkers. Track filtering creates a Track category row rather than per-segment search results. Earlier suggestions that marker IDs or individual segment IDs could be searched directly in this UI were incorrect.

Scene metadata refinement: Mjd8's object T-45.1 Origin has child MilePost mesh and text-label renderers, so it is not merely a nonvisual reference. Its original world root position is (25581.4043,629.1284,363.7289). Parents Mile Posts and World are identity transforms. New nearby track and the selected flattening mask are around 634 m elevation. Burial under new terrain is plausible, not visually established or measured at the exact point.

M5pp is World/Roads Sylva/Crossing Unprotected - Allen (1)/Whistle Sign E. Summing its identity-rotation parent translations gives original root (25201.1465,622.7342,-778.0820). Its Sign-Whistle child has a mesh renderer. Its track reference remains Shuo|a|58.6945267. Curve reconstruction puts its original logical track point near (25203.7656,623.3010,-779.0686) and the modified logical point near (25210.3670,623.4152,-760.3369), about 19.9 m apart. These are offline curve approximations, not runtime transform observations. Do not claim the visible board was positively identified in the viewport solely from these coordinates.

## Follow-up viewport verification

After the user supplied an overview and camera-control guidance, selected SKatSylInt_7k2b in the viewport, checked its endpoints against package data, then selected adjacent Shuo. Focus brought the actual linked segment into view. The small whistle board beside the selected segment matched the scene location; the user then rotated/zoomed the view and the W face was clearly visible between Shuo and its adjacent new track. The board is no longer merely an unlocated scene-data lead. It remains physically placed by the old layout while the logical location on modified Shuo differs. No gameplay whistle timing test was run.

For Mjd8, navigated near OKatSylInt_circlemask, selected nearby nodes and checked their displayed coordinates. Finally selected NKatSylInt_bi0o at the turntable rim; editor displays position (25582.12,634,363.017) and states its position is controlled by turntable KatSylInt_turntable1. Focus left this node in view. Its horizontal distance from the stored milepost root (25581.4043,629.1284,363.7289) is 1.012 m, with 4.872 m vertical separation. The original milepost position is beneath the new turntable installation according to these coordinates. Its mesh was not directly observed underground. It is not simply a missing visible whitepost beside the surviving mainline.

No edits or Save actions were made. All four ModDev package files were checked against the installed original again after navigation.

## User excavation observation

The user reports removing the turntable and several masks, then adding/lowering a flattening circle mask until the physical milepost labelled 45 became visible. This directly confirms that the object remains present beneath the current surface. The depth was not measured. These were user edits, not agent edits.

The original unmodified Sylva package adds OKatSylInt_circlemask at Y=634 with radius 50/falloff 40 and flattenHeightmap=true, approximately 45 m horizontally from the post. It also adds a nearby rotated rectangle mask at Y=633.475342 and a curve mask based at Y=634, both flattening. A simple base-object-origin search within 220 m returned no base mask entries, but this does not establish the baseline terrain height or exclude large/distant masks, track deformation or baked terrain. A clean base-map comparison remains necessary before attributing the entire burial depth to this mod.

Y is the vertical coordinate; Z is horizontal. Physical burial alone does not deactivate a TrackMarker or prove that its graph registration/consumers are inactive.

## Clean base-map excavation

Opened crankyant/inventory with rr/bushnell-whittier and rr/nc-bryson. The user's circle initially centered at (25667.56,628,409.107), approximately 97.38 m horizontally from the stored post position. Moved that existing unsaved circle to (25581.404,628,363.729), preserving its other settings. Selected and verified base node Nam9 at (25625.11,633.794,403.653), then segment Sozu between N86n and Nam9. Sozu is the nearest segment; a straight chord approximation places its centerline about 5.32 m horizontally from the post, while exact curved distance was not measured. Nam9 itself is 59.20 m away.

The post remained hidden with Sozu set to Ground. Temporarily switching Sozu to Bridge removed its terrain embankment and visibly exposed the white post exactly above the circle center. Restored Sozu to Ground immediately afterward. No Save action was performed. This directly demonstrates that the post exists and is covered by the base track's embankment in this editor baseline too; burial cannot be attributed solely to the Sylva rework. It does not measure the original unmodified terrain elevation or establish author intent.
