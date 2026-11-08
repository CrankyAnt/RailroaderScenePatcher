<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->

# Selected bridge telemetry, Boot02

Selected lines retain UTC timestamps and runtime instance identifiers. Repeated dependency records are intentional. The full log remains local.

```text
2026-11-08T21:36:15.3377733Z scenery dependency segment[0] requested=31lvmw id=31lvmw groupId=ela-bridge groupEnabled=True construction=Complete unconstructed=NotStartedBridgeSteel
2026-11-08T21:36:15.3393237Z scenery dependency entry[0] state=NeedsAbutments condition=NoOverlap targetCount=1
2026-11-08T21:36:15.3398792Z scenery dependency target[0,0] id=201804 path=World/Large Scenery/Ela/Ela Bridge/Abutments
2026-11-08T21:36:15.3424248Z scenery dependency entry[1] state=NeedsBridgeSteel condition=NoOverlap targetCount=1
2026-11-08T21:36:15.3424248Z scenery dependency target[1,0] id=201824 path=World/Large Scenery/Ela/Ela Bridge/Steel
2026-11-08T21:36:15.3570734Z scenery dependency segment[0] requested=31lvmw id=31lvmw groupId=ela-bridge groupEnabled=True construction=Complete unconstructed=NotStartedBridgeSteel
2026-11-08T21:36:15.3580714Z scenery dependency entry[0] state=NeedsAbutments condition=NoOverlap targetCount=1
2026-11-08T21:36:15.3580714Z scenery dependency target[0,0] id=201804 path=World/Large Scenery/Ela/Ela Bridge/Abutments
2026-11-08T21:36:15.3605821Z scenery dependency entry[1] state=NeedsBridgeSteel condition=NoOverlap targetCount=1
2026-11-08T21:36:15.3615815Z scenery dependency target[1,0] id=201824 path=World/Large Scenery/Ela/Ela Bridge/Steel
2026-11-08T21:36:15.3721602Z baseline scenery dependencies equalBaseline=True
2026-11-08T21:36:15.3737174Z baseline node=World/Large Scenery/Ela/Ela Bridge id=218428 localPosition=-641.313,3.58599854,-283.084 localRotation=0,0.975078046,0,-0.221862465 localScale=1,1,1 gamePosition=8616.002,545.980042,7274.759 activeSelf=True activeHierarchy=True positionError=0 gamePositionError=0 worldRotationErrorDegrees=0 rotationErrorDegrees=0 scaleError=0
2026-11-08T21:36:31.7839651Z ball spawned target=World/Large Scenery/Ela/Ela Bridge/Steel/Collider rayHit=114.302734,553.238,271.219238 rayQueryIsNotContact=true fixedDeltaTime=0.0199999921 timeScale=1 physicsSimulationMode=FixedUpdate
2026-11-08T21:36:32.0815218Z OnCollisionEnter count=1 onBridge=True other=World/Large Scenery/Ela/Ela Bridge/Steel/Collider contactCount=1 frame=48521
2026-11-08T21:36:37.2448953Z OnCollisionEnter count=2 onBridge=False other=World/Track/segment-31lvmw-(8598.8, 546.9, 7238.8)-(8629.8, 546.6, 7303.6)/seg-31lvmw/Collider contactCount=1 frame=48831
2026-11-08T21:36:46.9231047Z ball remove requested contacts=2
2026-11-08T21:37:02.8611993Z scenery dependency segment[0] requested=31lvmw id=31lvmw groupId=ela-bridge groupEnabled=True construction=Complete unconstructed=NotStartedBridgeSteel
2026-11-08T21:37:02.8611993Z scenery dependency entry[0] state=NeedsAbutments condition=NoOverlap targetCount=1
2026-11-08T21:37:02.8611993Z scenery dependency target[0,0] id=201804 path=World/Large Scenery/Ela/Ela Bridge/Abutments
2026-11-08T21:37:02.8641981Z scenery dependency entry[1] state=NeedsBridgeSteel condition=NoOverlap targetCount=1
2026-11-08T21:37:02.8641981Z scenery dependency target[1,0] id=201824 path=World/Large Scenery/Ela/Ela Bridge/Steel
2026-11-08T21:37:02.8727397Z moved immediate scenery dependencies equalBaseline=True
2026-11-08T21:37:02.8737469Z moved immediate node=World/Large Scenery/Ela/Ela Bridge id=218428 localPosition=-636.3125,3.58599854,-283.083984 localRotation=0,0.937777162,0,-0.347237617 localScale=1,1,1 gamePosition=8621.002,545.980042,7274.759 activeSelf=True activeHierarchy=True positionError=5.00048828 gamePositionError=5 worldRotationErrorDegrees=14.9999275 rotationErrorDegrees=14.9999275 scaleError=0
2026-11-08T21:37:16.2356390Z ball spawned target=World/Large Scenery/Ela/Ela Bridge/Steel/Collider rayHit=118.445313,553.238,271.7793 rayQueryIsNotContact=true fixedDeltaTime=0.0199999921 timeScale=1 physicsSimulationMode=FixedUpdate
2026-11-08T21:37:16.5336017Z OnCollisionEnter count=1 onBridge=True other=World/Large Scenery/Ela/Ela Bridge/Steel/Collider contactCount=1 frame=51172
2026-11-08T21:37:22.3000483Z OnCollisionEnter count=2 onBridge=False other=MapManager/Terrain 017 014 contactCount=1 frame=51518
2026-11-08T21:37:27.1515982Z ball remove requested contacts=2
2026-11-08T21:37:38.6204004Z scenery dependency segment[0] requested=31lvmw id=31lvmw groupId=ela-bridge groupEnabled=True construction=Complete unconstructed=NotStartedBridgeSteel
2026-11-08T21:37:38.6204004Z scenery dependency entry[0] state=NeedsAbutments condition=NoOverlap targetCount=1
2026-11-08T21:37:38.6219078Z scenery dependency target[0,0] id=201804 path=World/Large Scenery/Ela/Ela Bridge/Abutments
2026-11-08T21:37:38.6229149Z scenery dependency entry[1] state=NeedsBridgeSteel condition=NoOverlap targetCount=1
2026-11-08T21:37:38.6241598Z scenery dependency target[1,0] id=201824 path=World/Large Scenery/Ela/Ela Bridge/Steel
2026-11-08T21:37:38.6317750Z moved immediate scenery dependencies equalBaseline=True
2026-11-08T21:37:38.6327825Z moved immediate node=World/Large Scenery/Ela/Ela Bridge id=218428 localPosition=-636.3125,3.58599854,-283.083984 localRotation=0,0.937777162,0,-0.347237617 localScale=1,1,1 gamePosition=8621.002,545.980042,7274.759 activeSelf=True activeHierarchy=True positionError=5.00048828 gamePositionError=5 worldRotationErrorDegrees=14.9999275 rotationErrorDegrees=14.9999275 scaleError=0
2026-11-08T21:37:53.3359906Z scenery dependency segment[0] requested=31lvmw id=31lvmw groupId=ela-bridge groupEnabled=True construction=Complete unconstructed=NotStartedBridgeSteel
2026-11-08T21:37:53.3359906Z scenery dependency entry[0] state=NeedsAbutments condition=NoOverlap targetCount=1
2026-11-08T21:37:53.3369906Z scenery dependency target[0,0] id=201804 path=World/Large Scenery/Ela/Ela Bridge/Abutments
2026-11-08T21:37:53.3389907Z scenery dependency entry[1] state=NeedsBridgeSteel condition=NoOverlap targetCount=1
2026-11-08T21:37:53.3389907Z scenery dependency target[1,0] id=201824 path=World/Large Scenery/Ela/Ela Bridge/Steel
2026-11-08T21:37:53.3470130Z activity immediate scenery dependencies equalBaseline=True
2026-11-08T21:37:53.3480124Z activity immediate node=World/Large Scenery/Ela/Ela Bridge id=218428 localPosition=-636.3125,3.58599854,-283.083984 localRotation=0,0.937777162,0,-0.347237617 localScale=1,1,1 gamePosition=8621.002,545.980042,7274.759 activeSelf=False activeHierarchy=False positionError=5.00048828 gamePositionError=5 worldRotationErrorDegrees=14.9999275 rotationErrorDegrees=14.9999275 scaleError=0
2026-11-08T21:38:05.0331007Z scenery dependency segment[0] requested=31lvmw id=31lvmw groupId=ela-bridge groupEnabled=True construction=Complete unconstructed=NotStartedBridgeSteel
2026-11-08T21:38:05.0331007Z scenery dependency entry[0] state=NeedsAbutments condition=NoOverlap targetCount=1
2026-11-08T21:38:05.0331007Z scenery dependency target[0,0] id=201804 path=World/Large Scenery/Ela/Ela Bridge/Abutments
2026-11-08T21:38:05.0363230Z scenery dependency entry[1] state=NeedsBridgeSteel condition=NoOverlap targetCount=1
2026-11-08T21:38:05.0363230Z scenery dependency target[1,0] id=201824 path=World/Large Scenery/Ela/Ela Bridge/Steel
2026-11-08T21:38:05.0437542Z restored immediate scenery dependencies equalBaseline=True
2026-11-08T21:38:05.0447545Z restored immediate node=World/Large Scenery/Ela/Ela Bridge id=218428 localPosition=-641.313,3.58599854,-283.084 localRotation=0,0.975078046,0,-0.221862465 localScale=1,1,1 gamePosition=8616.002,545.980042,7274.759 activeSelf=True activeHierarchy=True positionError=0 gamePositionError=0 worldRotationErrorDegrees=0 rotationErrorDegrees=0 scaleError=0
2026-11-08T21:38:05.1012868Z restoration exactLocalTransformsAndActivity=True colliderShapes=True worldPoseWithin0.01mAnd0.01degrees=True
2026-11-08T21:38:19.0281012Z scenery dependency segment[0] requested=31lvmw id=31lvmw groupId=ela-bridge groupEnabled=True construction=Complete unconstructed=NotStartedBridgeSteel
2026-11-08T21:38:19.0281012Z scenery dependency entry[0] state=NeedsAbutments condition=NoOverlap targetCount=1
2026-11-08T21:38:19.0296048Z scenery dependency target[0,0] id=201804 path=World/Large Scenery/Ela/Ela Bridge/Abutments
2026-11-08T21:38:19.0316082Z scenery dependency entry[1] state=NeedsBridgeSteel condition=NoOverlap targetCount=1
2026-11-08T21:38:19.0316082Z scenery dependency target[1,0] id=201824 path=World/Large Scenery/Ela/Ela Bridge/Steel
2026-11-08T21:38:19.0393835Z current scenery dependencies equalBaseline=True
2026-11-08T21:38:19.0398872Z current node=World/Large Scenery/Ela/Ela Bridge id=218428 localPosition=-641.313,3.58599854,-283.084 localRotation=0,0.975078046,0,-0.221862465 localScale=1,1,1 gamePosition=8616.002,545.980042,7274.759 activeSelf=True activeHierarchy=True positionError=0 gamePositionError=0 worldRotationErrorDegrees=0 rotationErrorDegrees=0 scaleError=0
2026-11-08T21:38:30.1730620Z ball spawned target=World/Large Scenery/Ela/Ela Bridge/Steel/Collider rayHit=114.302734,553.238,271.219238 rayQueryIsNotContact=true fixedDeltaTime=0.0199999921 timeScale=1 physicsSimulationMode=FixedUpdate
2026-11-08T21:38:30.4734234Z OnCollisionEnter count=1 onBridge=True other=World/Large Scenery/Ela/Ela Bridge/Steel/Collider contactCount=1 frame=55596
2026-11-08T21:38:35.6397938Z OnCollisionEnter count=2 onBridge=False other=World/Track/segment-31lvmw-(8598.8, 546.9, 7238.8)-(8629.8, 546.6, 7303.6)/seg-31lvmw/Collider contactCount=1 frame=55906
2026-11-08T21:38:41.7745764Z ball remove requested contacts=2
```
