# SPDX-FileCopyrightText: 2026 CrankyAnt
# SPDX-License-Identifier: MIT
[CmdletBinding()]
param(
    [switch]$Install,
    [switch]$RuntimeAuthorized
)
$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$managed = 'D:\SteamLibrary\steamapps\common\Railroader\Railroader_Data\Managed'
$scratch = 'C:\TMP\RailroaderMapResearch\SceneOperationBatchBuild'
$stage = 'C:\TMP\RailroaderMapResearch\SceneOperationBatchStaging'
$runtime = 'C:\TMP\RailroaderMapResearch\RuntimeHome'
$fixture = 'C:\TMP\RailroaderMapResearch\CaseLibrary\ModDev\SceneOperationsResearch'

function Assert-Hash([string]$Path, [string]$Expected) {
    if ((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ne $Expected) {
        throw "Identity changed: $Path. Reinspect before building or installing."
    }
}
Assert-Hash (Join-Path $managed 'Railroader.dll') '8E9ACFD583C1DA79655C970773D42F2179E999F62DC36A3C6BC3A7B94C27A91E'
Assert-Hash (Join-Path $managed 'Map.Runtime.dll') '9C52BD1FA8AA6D3D4417484F2002F985F6F4E01CBCD3C96E7675C4F19B8F0767'
Assert-Hash (Join-Path $fixture 'Data\Map.json') '36A99A8EBB6E2198C28D3C6D4AA2C859CE70B1F85E0D4E14243FA6837C9029AE'
if ($Install -and !$RuntimeAuthorized) { throw 'Installation requires authorization for the isolated runtime session. Pass -RuntimeAuthorized only within that authorization.' }
if ($Install -and (Get-Process -Name Railroader -ErrorAction SilentlyContinue)) { throw 'Close Railroader before installation.' }

$packs = @(
    @{ Folder = 'MarkerGroups'; Project = 'MarkerGroups\MarkerGroups.csproj'; Info = 'MarkerGroups\Info.json'; Assembly = 'MarkerGroups.dll' },
    @{ Folder = 'MilepostProbe'; Project = 'Milepost\MilepostProbe.csproj'; Info = 'Milepost\Info.json'; Assembly = 'MilepostProbe.dll' },
    @{ Folder = 'CrossingPadProbe'; Project = 'CrossingPad\CrossingPadProbe.csproj'; Info = 'CrossingPad\Info.json'; Assembly = 'CrossingPadProbe.dll' },
    @{ Folder = 'BridgeContactProbe'; Project = 'Geometry\BridgeContactProbe.csproj'; Info = 'Geometry\BridgeContactInfo.json'; Assembly = 'BridgeContactProbe.dll' },
    @{ Folder = 'TerrainOutputProbe'; Project = 'Geometry\TerrainOutputProbe.csproj'; Info = 'Geometry\TerrainOutputInfo.json'; Assembly = 'TerrainOutputProbe.dll' }
)
$identities = @()
Push-Location $repository
try {
    foreach ($pack in $packs) {
        $project = Join-Path $PSScriptRoot $pack.Project
        $build = Join-Path $scratch $pack.Folder
        & dotnet build $project "-p:GameManaged=$managed" "-p:BaseIntermediateOutputPath=$build\obj\" "-p:OutputPath=$build\out\" -warnaserror
        if ($LASTEXITCODE -ne 0) { throw "Build failed: $($pack.Project)" }
        $destination = Join-Path $stage $pack.Folder
        New-Item -ItemType Directory -Path $destination -Force | Out-Null
        $unexpected = @(Get-ChildItem -LiteralPath $destination -Force |
            Where-Object { $_.Name -notin @($pack.Assembly, 'Info.json') -or $_.PSIsContainer })
        if ($unexpected.Count) { throw "Unexpected staging contents: $destination. Inspect before continuing." }
        Copy-Item -LiteralPath (Join-Path $build "out\$($pack.Assembly)") -Destination $destination
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $pack.Info) -Destination (Join-Path $destination 'Info.json')
        $manifest = Get-Content -LiteralPath (Join-Path $destination 'Info.json') -Raw | ConvertFrom-Json
        if ($manifest.author -ne 'CrankyAnt' -or $manifest.name -ne 'CrankyAnt' -or @($manifest.assemblies).Count -ne 1 -or
            $manifest.assemblies[0] -ne $pack.Assembly) { throw "Unexpected pack manifest: $($pack.Folder)" }
        $identities += [ordered]@{
            folder = $pack.Folder
            assembly = $pack.Assembly
            dllSha256 = (Get-FileHash -LiteralPath (Join-Path $destination $pack.Assembly)).Hash
            infoSha256 = (Get-FileHash -LiteralPath (Join-Path $destination 'Info.json')).Hash
        }
    }
} finally { Pop-Location }

$sourceIdentities = @(Get-ChildItem -LiteralPath $PSScriptRoot -Recurse -File |
    Where-Object { $_.Extension -In '.cs', '.csproj', '.json', '.ps1' -and $_.Name -ne 'BuildIdentity.json' } |
    Sort-Object FullName | ForEach-Object {
        [ordered]@{ path = [IO.Path]::GetRelativePath($PSScriptRoot, $_.FullName).Replace('\', '/'); sha256 = (Get-FileHash -LiteralPath $_.FullName).Hash }
    })
$fixtureIdentities = @('Info.json', 'Definitions.json', 'Data\Map.json') | ForEach-Object {
    [ordered]@{ path = $_.Replace('\', '/'); sha256 = (Get-FileHash -LiteralPath (Join-Path $fixture $_)).Hash }
}
$identity = [ordered]@{
    evidenceKind = 'build-only; no loader or runtime trial'
    sourceFiles = $sourceIdentities
    fixtureFiles = @($fixtureIdentities)
    packs = $identities
}
$identity | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $stage 'BuildIdentity.json') -Encoding utf8NoBOM

if ($Install) {
    if (Get-Process -Name Railroader -ErrorAction SilentlyContinue) { throw 'Railroader started during preparation. Close it before installation.' }
    foreach ($folder in 'Mods', 'ModDev', 'Saves') {
        $path = Join-Path $runtime $folder
        if (!(Test-Path -LiteralPath $path -PathType Container)) { throw "Required isolated folder absent: $path" }
        if (Get-ChildItem -LiteralPath $path -Force) { throw "Isolated baseline is not neutral: $path" }
    }
    $preferences = Get-Content -LiteralPath (Join-Path $runtime 'Preferences.json') -Raw | ConvertFrom-Json
    if (@($preferences.mods).Count -ne 0 -or @($preferences.clientMods).Count -ne 0) { throw 'Isolated activation flags are not neutral.' }
    foreach ($pack in $packs) {
        $destination = Join-Path (Join-Path $runtime 'Mods') $pack.Folder
        New-Item -ItemType Directory -Path $destination | Out-Null
        $expected = $identities | Where-Object folder -EQ $pack.Folder
        foreach ($name in @($pack.Assembly, 'Info.json')) {
            $source = Join-Path (Join-Path $stage $pack.Folder) $name
            $hash = if ($name -eq 'Info.json') { $expected.infoSha256 } else { $expected.dllSha256 }
            Assert-Hash $source $hash
            Copy-Item -LiteralPath $source -Destination $destination
            Assert-Hash (Join-Path $destination $name) $hash
        }
    }
    Copy-Item -LiteralPath $fixture -Destination (Join-Path $runtime 'ModDev') -Recurse
    Write-Output 'Installed case files. Preferences are unchanged. Explicitly enable the selected case through the isolated profile before the single fresh launch.'
} else {
    Write-Output "Staged five packs at $stage. RuntimeHome remains unchanged."
}
