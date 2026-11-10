# SPDX-FileCopyrightText: 2026 CrankyAnt
# SPDX-License-Identifier: MIT
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutputRoot,
    [switch]$Bootstrap,
    [string]$ObservationPath,
    [long]$Sequence,
    [long]$ConfirmSequence,
    [string]$InspectorPath,
    [string]$LoadId,
    [string]$ExpectedGameSha256
)
$ErrorActionPreference='Stop'
$mapId='crankyant/fixed-placement-fixture-map'
$boardPath='World/Signs/Loc: Sylva Int'
if (![IO.Path]::IsPathFullyQualified($OutputRoot) -or (Test-Path -LiteralPath $OutputRoot)) {throw 'OutputRoot must be a new absolute directory'}
function Save-Json($Value,[string]$Path) {
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Path)) | Out-Null
    [IO.File]::WriteAllText($Path,($Value | ConvertTo-Json -Depth 60).Replace("`r`n","`n")+"`n",[Text.UTF8Encoding]::new($false))
}
function Write-Fixture([string]$Name,[array]$Modifications) {
    $destination=Join-Path $OutputRoot $Name
    Save-Json @{identifier='crankyant/fixed-placement-fixture';name='CrankyAnt';author='CrankyAnt';version='0.0.1';dependencies=@{required=@('rr/game >=0.6.2','crankyant/scene-patcher >=0.1.0')}} (Join-Path $destination 'Info.json')
    Save-Json @{objects=@(@{identifier=$mapId;metadata=@{name='Issue39 fixed placement';description='Disposable native test';credits='CrankyAnt';tags=@()};definition=@{kind='Map';mapData='./Map.json';terrainTileSets=@('rr/nc-bryson');mapLayers=@('rr/bushnell-whittier');components=@{}}})} (Join-Path $destination 'Definitions.json')
    Save-Json @{} (Join-Path $destination 'Data/Map.json')
    Save-Json @{formatVersion=1;sourceMap=@{mapId=$mapId};modifications=$Modifications} (Join-Path $destination 'Data/scene-patches.json')
}
if ($Bootstrap) {
    if ($ObservationPath -or $Sequence -or $LoadId) {throw 'Bootstrap does not accept a claimed baseline'}
    Write-Fixture 'Baseline' @()
    return
}
if (!$ObservationPath -or !$Sequence -or $ConfirmSequence -le $Sequence -or !$InspectorPath -or !$LoadId -or $ExpectedGameSha256 -notmatch '^[A-Fa-f0-9]{64}$') {throw 'Select the exact baseline and confirmation sequences, inspector export, load ID and independently checked Railroader.dll hash'}
$rows=@(Get-Content -LiteralPath $ObservationPath | Where-Object {$_ -ne ''} | ForEach-Object {$_ | ConvertFrom-Json})
$selected=@($rows | Where-Object {$_.sequence -eq $Sequence -and $_.loadId -ceq $LoadId})
if ($selected.Count -ne 1) {throw 'The baseline selection is not unique'}
$b=$selected[0]
$confirm=@($rows | Where-Object {$_.sequence -eq $ConfirmSequence -and $_.loadId -ceq $LoadId})
if($confirm.Count -ne 1){throw 'The confirming baseline selection is not unique'}
$confirmation=$confirm[0]
$inspector=Get-Content -LiteralPath $InspectorPath -Raw | ConvertFrom-Json
if($inspector.sourceMap.mapId -cne $mapId -or $inspector.target.scene -cne $b.board.scene -or $inspector.target.path -cne $b.board.path -or $inspector.target.component -cne $b.board.component -or $inspector.target.structure -cne $b.board.structure){throw 'The native inspector and observer disagree about target identity'}
if ($b.event -cne 'snapshot' -or $b.label -cne 'baseline' -or $b.schemaVersion -ne 1 -or $b.evidenceKind -cne 'native-observation' -or $b.mapId -cne $mapId -or $b.railroaderSha256 -ine $ExpectedGameSha256) {throw 'Unexpected baseline provenance, map or game build'}
if (@($b.measurementErrors).Count -ne 0 -or $null -eq $b.measurementErrors) {throw 'Incomplete baseline measurements'}
if ($b.board.matches -ne 1 -or !$b.board.eligible -or $b.board.scene -cne 'BushnellWhittier-MapManager' -or $b.board.path -cne $boardPath -or $b.board.component -cne 'Helpers.Culling.RendererCuller' -or $b.board.structure -cne 'independent-board-v1' -or !$b.board.activeSelf -or !$b.board.activeInHierarchy) {throw 'Missing, ambiguous or changed board pattern'}
if ($b.marker.matches -ne 1 -or $b.marker.id -cne 'Mnau' -or @($b.marker.registrations).Count -ne 1 -or !$b.marker.registrations[0].objectMatches -or !$b.marker.registrations[0].locationMatches -or !$b.culler.member -or @($b.mask.modifiers).Count -eq 0 -or @($b.mask.modifiers | Where-Object {!$_.registered -or $_.descriptorType -cne 'Map.Runtime.CurveMaskDescriptor'}).Count -ne 0 -or !$b.terrain.idle -or $b.terrain.pendingInvalidationCount -ne 0) {throw 'Control, registration or terrain baseline not ready'}
if (@($b.generatedTrees).Count -ne 2 -or @($b.generatedTrees | Where-Object {!$_.handoffSame -or !$_.loadedSame -or !$_.isLoaded -or !$_.managerPresent}).Count -ne 0){throw 'Generated terrain output is not ready for baseline comparison'}
if($confirmation.event -cne 'snapshot' -or $confirmation.label -cne 'baseline' -or $confirmation.evidenceKind -cne 'native-observation' -or $confirmation.mapId -cne $mapId -or $confirmation.railroaderSha256 -ine $ExpectedGameSha256 -or @($confirmation.measurementErrors).Count -ne 0 -or !$confirmation.terrain.idle -or $confirmation.terrain.pendingInvalidationCount -ne 0){throw 'Confirming baseline is incomplete or not settled'}
foreach($field in @('processId','engineRuntimeId','graphInstanceId')){if(!$b.$field -or $b.$field -ne $confirmation.$field){throw "Baseline identity changed: $field"}}
foreach($field in @('instanceId','sceneHandle')){if(!$b.board.$field -or $b.board.$field -ne $confirmation.board.$field){throw "Board identity changed: $field"}}
if(!$confirmation.board.eligible -or $confirmation.board.matches -ne 1 -or !$confirmation.culler.member -or $confirmation.marker.id -cne 'Mnau' -or $confirmation.marker.matches -ne 1){throw 'Pattern or control changed between baseline snapshots'}
foreach($field in @('prefix','mileage','text','enabled','segmentId','distance','end')){if($b.marker.$field -cne $confirmation.marker.$field){throw "Unrelated control changed: $field"}}
if(@($confirmation.marker.registrations).Count -ne 1 -or !$confirmation.marker.registrations[0].objectMatches -or !$confirmation.marker.registrations[0].locationMatches -or $b.marker.registrations[0].bucket -cne $confirmation.marker.registrations[0].bucket){throw 'Unrelated control registration changed'}
foreach($name in @('mapPosition','worldPosition','rotation')) {
    $count=if($name -eq 'rotation'){4}else{3}
    if (@($b.board.$name).Count -ne $count) {throw "Missing baseline $name"}
    foreach($value in $b.board.$name) {if ($null -eq $value -or [double]::IsNaN([double]$value) -or [double]::IsInfinity([double]$value)) {throw 'Nonfinite baseline pose'}}
}
foreach($name in @('offset','gameZero','boardMapToWorldRoundtrip')) {if (@($b.origin.$name).Count -ne 3) {throw "Missing origin $name"}}
for($i=0;$i -lt 3;$i++) {
    if ([math]::Abs($b.board.worldPosition[$i]-$b.origin.boardMapToWorldRoundtrip[$i]) -gt 0.01 -or [math]::Abs(($b.board.mapPosition[$i]-$b.board.worldPosition[$i])-$b.origin.gameZero[$i]) -gt 0.01) {throw 'Measured map/world/origin relationship is inconsistent'}
    if ([math]::Abs($b.board.mapPosition[$i]-$confirmation.board.mapPosition[$i]) -gt 0.01 -or [math]::Abs($b.board.mapPosition[$i]-$inspector.placement.position[$i]) -gt 0.01){throw 'Unexpected baseline movement or inspector pose mismatch'}
}
function Rotation-Agrees($a,$other) {
    if(@($other).Count -ne 4){return $false}
    $dot=0.0;$aNorm=0.0;$otherNorm=0.0
    for($i=0;$i -lt 4;$i++){$dot+=$a[$i]*$other[$i];$aNorm+=$a[$i]*$a[$i];$otherNorm+=$other[$i]*$other[$i]}
    if($aNorm -le 0 -or $otherNorm -le 0){return $false}
    return ([math]::Abs($dot)/[math]::Sqrt($aNorm*$otherNorm) -ge [math]::Cos([math]::PI/180*0.02/2))
}
if(!(Rotation-Agrees $b.board.rotation $confirmation.board.rotation) -or !(Rotation-Agrees $b.board.rotation $inspector.placement.rotation)){throw 'Unexpected baseline quaternion or inspector rotation mismatch'}
foreach($row in $rows | Where-Object {$_.loadId -ceq $LoadId -and $_.sequence -lt $ConfirmSequence -and $_.event -in @('attempt-trace','unassigned-attempt-trace')}) {
    $text=[string]$row.message
    if ($text.Contains('{')) { $trace=$text.Substring($text.IndexOf('{')) | ConvertFrom-Json; if($trace.stage -eq 'mutation') {throw 'The selected baseline was preceded by a patch mutation in this load'}}
}
$p=@($b.board.mapPosition | ForEach-Object {[double]$_})
$q=@($b.board.rotation | ForEach-Object {[double]$_})
$norm=[math]::Sqrt(($q | ForEach-Object {$_*$_} | Measure-Object -Sum).Sum)
if ([math]::Abs($norm-1) -gt 0.0001) {throw 'Unexpected non-unit baseline quaternion'}
$s=[math]::Sin([math]::PI/24);$c=[math]::Cos([math]::PI/24)
$turned=@(($c*$q[0]+$s*$q[2]),($c*$q[1]+$s*$q[3]),($c*$q[2]-$s*$q[0]),($c*$q[3]-$s*$q[1]))
$target=@{scene='BushnellWhittier-MapManager';path=$boardPath;component='Helpers.Culling.RendererCuller';structure='independent-board-v1'}
function Modification([string]$Id,[double]$Dx,[array]$Rotation,[array]$Depends=@(),[switch]$Missing) {
    $identity=$target.Clone();if($Missing){$identity.path=$boardPath+'/__missing_issue39'}
    return @{id=$Id;target=$identity;placement=@{mode='fixed';position=@(($p[0]+$Dx),$p[1],$p[2]);rotation=$Rotation};dependsOn=$Depends}
}
$good=Modification 'place-sylva-board' 5 $turned
$bad=Modification 'missing-target' 25 $turned -Missing
$dependent=Modification 'blocked-dependent' 50 $turned @('missing-target')
Write-Fixture 'Baseline' @()
Write-Fixture 'Valid' @($good)
Write-Fixture 'InvalidIndependent' @($bad,$dependent,$good)
Write-Fixture 'InvalidIndependentReordered' @($good,$dependent,$bad)
Write-Fixture 'RepairSameLoad' @((Modification 'missing-target' 25 $turned),$dependent,$good)
Write-Fixture 'Restoration' @((Modification 'restore-measured-board' 0 $q))
Save-Json @{schemaVersion=1;snapshot=$b} (Join-Path $OutputRoot 'Baseline.json')
Save-Json @{schemaVersion=1;sourceObservation=[IO.Path]::GetFullPath($ObservationPath);sourceSha256=(Get-FileHash -LiteralPath $ObservationPath -Algorithm SHA256).Hash;inspectorSha256=(Get-FileHash -LiteralPath $InspectorPath -Algorithm SHA256).Hash;sequence=$Sequence;confirmSequence=$ConfirmSequence;loadId=$LoadId;processId=$b.processId;gameSha256=$ExpectedGameSha256;translationMap=@(5,0,0);rotationRule='map-Y +15 degrees, qDesired = qY15 * qBaseline';baselinePosition=$p;baselineRotation=$q;desiredPosition=$good.placement.position;desiredRotation=$turned;positionToleranceMetres=0.01;rotationToleranceDegrees=0.02} (Join-Path $OutputRoot 'derivation.json')
