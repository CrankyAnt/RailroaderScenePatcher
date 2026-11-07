param(
    [Parameter(Mandatory = $true)][string]$ModRoot,
    [Parameter(Mandatory = $true)][string]$BaseMapPath
)

$ErrorActionPreference = 'Stop'
$mapPath = Join-Path $modRoot 'Data\Map.json'
$basePath = $BaseMapPath
$base = Get-Content -LiteralPath $basePath -Raw | ConvertFrom-Json -AsHashtable
$patch = Get-Content -LiteralPath $mapPath -Raw | ConvertFrom-Json -AsHashtable
if ($patch.graph.segments.Sozu -ne $null) { throw 'Expected the saved Sozu deletion trial.' }
Copy-Item -LiteralPath $mapPath -Destination (Join-Path $PSScriptRoot 'BeforeBatch.Map.json')
if (!$patch.graph.Contains('nodes')) { $patch.graph.nodes = [ordered]@{} }
$cases = [System.Collections.Generic.List[object]]::new()
function Add-Case($id, $family, $target, $action, $expected) {
    $cases.Add([ordered]@{ id=$id; family=$family; target=$target; action=$action; expected=$expected; result='Pending reload and observation' })
}
function Remove-Segment($id) {
    if (!$base.graph.segments.Contains($id)) { throw "Unknown base segment: $id" }
    $patch.graph.segments[$id] = $null
}
function Move-SegmentNodes($id, $dx, $dz) {
    $segment = $base.graph.segments[$id]
    if (!$segment) { throw "Unknown base segment: $id" }
    foreach ($nodeId in @($segment.a, $segment.b)) {
        if ($patch.graph.nodes.Contains($nodeId)) { throw "Overlapping node trial: $nodeId" }
        $position = @($base.graph.nodes[$nodeId].position)
        $position[0] += $dx
        $position[2] += $dz
        $patch.graph.nodes[$nodeId] = [ordered]@{position=$position}
    }
}
Add-Case 'I01' 'Milepost marker' 'Mjd8, T-45.1 Origin' 'Delete Sozu, preserving both endpoint nodes. Already performed in editor.' 'Missing marker reference on reload; physical post survives. No runtime repair applied.'
Move-SegmentNodes 'Shuo' 20.0 0.0
Add-Case 'I02' 'Whistle marker, surviving segment ID' 'M5pp, Whistle Sign E at Sylva' 'Move Nmzo and N51l by +20 m X; retain Shuo and its ID.' 'Marker may resolve without warning while scene model retains old pose. Inspect physical and logical locations separately.'
Remove-Segment 'Sjdl'
Add-Case 'I03' 'Speed sign marker' 'Mwp1, World/Large Scenery/Alarka Signs/Speed Sign' 'Delete Sjdl.' 'Missing marker reference; visible sign may survive.'
Remove-Segment 'S9sc'
Add-Case 'I04' 'Whistle marker, deleted segment' 'Mm8w, Ela crossing whistle sign E' 'Delete S9sc.' 'Missing marker reference; compare with I02 surviving-ID case.'
Remove-Segment 'Sj7c'
Add-Case 'I05' 'Unprotected crossing pad' 'Mg74, World/Roads Wilmot/Crossing Unprotected/Crossing Pad' 'Delete Sj7c.' 'Missing crossing marker reference; inspect pad mesh and associated crossing signs separately.'
Remove-Segment 'Sz8t'
Add-Case 'I06' 'Protected crossing scene spans' 'Bryson Everett controller 30941, Pipr island and Pws9 approachB' 'Delete Sz8t; leave second circuit track intact.' 'Invalid endpoints for Pipr and Pws9 are expected candidates. Other routing/CTC effects must be attributed separately. Train behavior is not tested by loading alone.'
Move-SegmentNodes '31lvmw' 20.0 0.0
Add-Case 'I07' 'Retained scene bridge' 'World/Large Scenery/Ela/Ela Bridge, GameObject 37' 'Move bridge segment 31lvmw endpoints 5hqgfi and qocgc2 by +20 m X.' 'Scene bridge may remain at its old position. Segment association inferred from scene position and base graph; visual confirmation required. No collision claim.'
foreach ($suffix in @('A','B')) {
    $nodeId = "NCrankyAntInventoryBoard$suffix"
    if ($base.graph.nodes.Contains($nodeId)) { throw "Unexpected existing node: $nodeId" }
    $x = if ($suffix -eq 'A') {25680.0} else {25705.0}
    $patch.graph.nodes[$nodeId] = [ordered]@{position=@($x,633.5,441.8128); rotation=@(0.0,90.0,0.0)}
}
$patch.graph.segments['SCrankyAntInventoryBoard'] = [ordered]@{a='NCrankyAntInventoryBoardA'; b='NCrankyAntInventoryBoardB'; groupId=''; speedLimit=10}
Add-Case 'I08' 'Standalone scene location board' 'World/Signs/Loc: Sylva Int, GameObject 778' 'Add a short isolated track through the stored board position.' 'Board stays in place with no missing track reference, because it has no TrackMarker. Visual obstruction test; one deliberately disconnected track island is expected.'
$changedNodes = @($patch.graph.nodes.Keys | Where-Object {$base.graph.nodes.Contains($_)})
$affected = @($base.graph.segments.GetEnumerator() | Where-Object {$_.Value.a -in $changedNodes -or $_.Value.b -in $changedNodes} | ForEach-Object {$_.Key})
$manifest = [ordered]@{
    map='crankyant/inventory'; game='Installed experimental 0.6.2'; cases=$cases
    deletedSegments=@($patch.graph.segments.Keys | Where-Object {$null -eq $patch.graph.segments[$_]})
    movedNodes=$changedNodes; geometryAffectedBaseSegments=$affected
    note='No scene object or marker repair is applied. Adjacent segments share moved nodes, so their geometry changes too. Normal saves and other mod packages are untouched.'
}
$encoded = $patch | ConvertTo-Json -Depth 60
$encoded | Set-Content -LiteralPath $mapPath -Encoding utf8
$encoded | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'Batch.Map.json') -Encoding utf8
$manifest | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'BatchManifest.json') -Encoding utf8
$verified = Get-Content -LiteralPath $mapPath -Raw | ConvertFrom-Json -AsHashtable
if ($verified.graph.nodes.Count -ne 6 -or $verified.graph.segments.Count -ne 6) { throw 'Unexpected batch record counts.' }
$cases | ForEach-Object { [pscustomobject]$_ } | Select-Object id,family,action | Format-Table -Wrap
