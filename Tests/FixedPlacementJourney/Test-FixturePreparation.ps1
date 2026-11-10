# SPDX-FileCopyrightText: 2026 CrankyAnt
# SPDX-License-Identifier: MIT
$ErrorActionPreference = 'Stop'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('Issue39FixtureTest-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($testRoot) | Out-Null
try {
    $baseline = @{event='snapshot';schemaVersion=1;evidenceKind='native-observation';label='baseline';sequence=7;loadId='test-load';processId=123;railroaderSha256=('A'*64);mapId='crankyant/fixed-placement-fixture-map';measurementErrors=@();board=@{matches=1;eligible=$true;scene='BushnellWhittier-MapManager';path='World/Signs/Loc: Sylva Int';component='Helpers.Culling.RendererCuller';structure='independent-board-v1';mapPosition=@(100,200,300);worldPosition=@(-3900,200,-4700);rotation=@(0,0.7071067811865476,0,0.7071067811865476);activeSelf=$true;activeInHierarchy=$true};origin=@{offset=@(4000,0,5000);gameZero=@(4000,0,5000);boardMapToWorldRoundtrip=@(-3900,200,-4700)};marker=@{matches=1;id='Mnau'};culler=@{member=$true};mask=@{modifiers=@(@{registered=$true})};terrain=@{idle=$true;pendingInvalidationCount=0}}
    $observations=Join-Path $testRoot 'synthetic-observations.jsonl'
    $baseline.engineRuntimeId=42;$baseline.graphInstanceId=43;$baseline.board.instanceId=44;$baseline.board.sceneHandle='45'
    $baseline.marker.registrations=@(@{objectMatches=$true;locationMatches=$true});$baseline.mask.modifiers[0].descriptorType='Map.Runtime.CurveMaskDescriptor'
    $baseline.generatedTrees=@(@{handoffSame=$true;loadedSame=$true;isLoaded=$true;managerPresent=$true},@{handoffSame=$true;loadedSame=$true;isLoaded=$true;managerPresent=$true})
    $baseline | ConvertTo-Json -Depth 30 -Compress | Set-Content -LiteralPath $observations -Encoding utf8
    $confirmation=$baseline|ConvertTo-Json -Depth 30|ConvertFrom-Json -AsHashtable;$confirmation.sequence=8
    $confirmation|ConvertTo-Json -Depth 30 -Compress|Add-Content -LiteralPath $observations -Encoding utf8
    $inspector=Join-Path $testRoot 'inspector.json'
    @{sourceMap=@{mapId=$baseline.mapId};target=@{scene=$baseline.board.scene;path=$baseline.board.path;component=$baseline.board.component;structure=$baseline.board.structure};placement=@{position=$baseline.board.mapPosition;rotation=$baseline.board.rotation}}|ConvertTo-Json -Depth 30|Set-Content -LiteralPath $inspector -Encoding utf8
    $out=Join-Path $testRoot 'generated'
    & "$PSScriptRoot/Prepare-Fixtures.ps1" -ObservationPath $observations -Sequence 7 -ConfirmSequence 8 -InspectorPath $inspector -LoadId test-load -ExpectedGameSha256 ('A'*64) -OutputRoot $out
    $valid=Get-Content (Join-Path $out 'Valid/Data/scene-patches.json') -Raw | ConvertFrom-Json
    if (($valid.modifications[0].placement.position -join ',') -ne '105,200,300') {throw 'The 5m map-X translation was not derived from the selected baseline'}
    $q=$valid.modifications[0].placement.rotation
    if ([math]::Abs($q[1]-0.7933533402912352) -gt 1e-10 -or [math]::Abs($q[3]-0.6087614290087207) -gt 1e-10) {throw 'Expected 105 degree map-Y quaternion'}
    $bad=Get-Content (Join-Path $out 'InvalidIndependent/Data/scene-patches.json') -Raw | ConvertFrom-Json
    if ($bad.modifications.Count -ne 3 -or $bad.modifications[0].id -ne 'missing-target' -or $bad.modifications[1].dependsOn[0] -ne 'missing-target') {throw 'Missing, dependent and independent variants must be concrete'}
    $restore=Get-Content (Join-Path $out 'Restoration/Data/scene-patches.json') -Raw | ConvertFrom-Json
    if (($restore.modifications[0].placement.position -join ',') -ne '100,200,300') {throw 'Restoration must use the measured baseline'}
    # A tilted baseline distinguishes map-Y pre-multiplication from local-Y rotation.
    $baseline.board.rotation=@(0.7071067811865476,0,0,0.7071067811865476)
    $confirmation.board.rotation=$baseline.board.rotation
    $baseline|ConvertTo-Json -Depth 30 -Compress|Set-Content -LiteralPath $observations -Encoding utf8
    $confirmation|ConvertTo-Json -Depth 30 -Compress|Add-Content -LiteralPath $observations -Encoding utf8
    $inspectorData=Get-Content -LiteralPath $inspector -Raw|ConvertFrom-Json -AsHashtable;$inspectorData.placement.rotation=$baseline.board.rotation
    $inspectorData|ConvertTo-Json -Depth 30|Set-Content -LiteralPath $inspector -Encoding utf8
    $tilted=Join-Path $testRoot 'tilted'
    & "$PSScriptRoot/Prepare-Fixtures.ps1" -ObservationPath $observations -Sequence 7 -ConfirmSequence 8 -InspectorPath $inspector -LoadId test-load -ExpectedGameSha256 ('A'*64) -OutputRoot $tilted
    $rot=(Get-Content (Join-Path $tilted 'Valid/Data/scene-patches.json') -Raw|ConvertFrom-Json).modifications[0].placement.rotation
    $expected=@(0.7010573846499779,0.09229595564125725,-0.09229595564125725,0.7010573846499779)
    for($i=0;$i -lt 4;$i++){if([math]::Abs($rot[$i]-$expected[$i]) -gt 1e-10){throw 'Rotation must be around map Y, not the tilted local Y axis'}}
    $baseline.board.eligible=$false
    $baseline | ConvertTo-Json -Depth 30 -Compress | Set-Content -LiteralPath $observations -Encoding utf8
    $confirmation|ConvertTo-Json -Depth 30 -Compress|Add-Content -LiteralPath $observations -Encoding utf8
    $rejected=$false
    try { & "$PSScriptRoot/Prepare-Fixtures.ps1" -ObservationPath $observations -Sequence 7 -ConfirmSequence 8 -InspectorPath $inspector -LoadId test-load -ExpectedGameSha256 ('A'*64) -OutputRoot (Join-Path $testRoot 'rejected') } catch {$rejected=$true}
    if (!$rejected -or (Test-Path (Join-Path $testRoot 'rejected'))) {throw 'An unexpected pattern must stop without output'}
    'PASS: synthetic baseline derives concrete fixed-placement variants and rejects an unexpected pattern'
} finally {
    $absolute=[IO.Path]::GetFullPath($testRoot)
    if (![IO.Path]::GetFileName($absolute).StartsWith('Issue39FixtureTest-') -or !($absolute.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()),[StringComparison]::OrdinalIgnoreCase))) {throw "Unsafe test cleanup path $absolute"}
    Remove-Item -LiteralPath $absolute -Recurse -Force
}
