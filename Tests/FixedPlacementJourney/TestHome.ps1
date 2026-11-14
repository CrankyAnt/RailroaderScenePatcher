# SPDX-FileCopyrightText: 2026 CrankyAnt
# SPDX-License-Identifier: MIT
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Inventory','Install','SelectVariant','RegisterSave','Collect','Cleanup')][string]$Action,
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9][A-Za-z0-9-]{0,63}$')][string]$RunId,
    [string]$ProductOutput,[string]$ObserverDll,[string]$FixtureSource,
    [string]$BaselineReference,[string]$SavePath,
    [switch]$AllowRunning,
    [string]$TestSandbox
)
$ErrorActionPreference='Stop'
$homePath='C:\TMP\RailroaderMapResearch\RuntimeHome'
$evidenceBase='C:\TMP\RailroaderMapResearch\Observations\Issue39'
if ($TestSandbox) {
    $full=[IO.Path]::GetFullPath($TestSandbox)
    if (![IO.Path]::IsPathFullyQualified($TestSandbox) -or [IO.Path]::GetFileName($full) -notmatch '^Issue39HomeTest-[a-f0-9]{32}$' -or ![IO.Path]::GetDirectoryName($full).Equals([IO.Path]::GetTempPath().TrimEnd('\','/'),[StringComparison]::OrdinalIgnoreCase)) {throw 'TestSandbox must be a dedicated Issue39HomeTest GUID directory directly under TEMP'}
    $homePath=Join-Path $full 'RuntimeHome';$evidenceBase=Join-Path $full 'Evidence'
}
$homePath=[IO.Path]::GetFullPath($homePath)
$run=Join-Path $evidenceBase $RunId
$receiptPath=Join-Path $run 'ownership.json'
$prefsPath=Join-Path $homePath 'Preferences.json'
$dirs=@('Mods/Issue39ScenePatcher','Mods/Issue39Observation','ModDev/Issue39FixedPlacement')
function Plain([string]$Path) {
    $cursor=[IO.Path]::GetFullPath($Path)
    while($cursor) {
        if ((Test-Path -LiteralPath $cursor) -and ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {throw "Reparse point is not allowed: $cursor"}
        $cursor=[IO.Path]::GetDirectoryName($cursor)
    }
}
function Inside([string]$Path) {
    $absolute=[IO.Path]::GetFullPath($Path)
    if (!$absolute.StartsWith($homePath+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) {throw "Path outside isolated home: $absolute"}
    Plain $absolute
    return $absolute
}
function Write-Json($Value,[string]$Path) {
    Plain $Path;[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Path)) | Out-Null
    [IO.File]::WriteAllText($Path,($Value|ConvertTo-Json -Depth 60).Replace("`r`n","`n")+"`n",[Text.UTF8Encoding]::new($false))
}
function Prefs { Plain $prefsPath; return Get-Content -LiteralPath $prefsPath -Raw | ConvertFrom-Json -AsHashtable }
function Neutral($p) { return ($p.ContainsKey('mods') -and $p.ContainsKey('clientMods') -and @($p.mods).Count -eq 0 -and @($p.clientMods).Count -eq 0) }
function Inventory {
    $files=@();$entries=@()
    foreach($name in @('Mods','ModDev','Saves')) {
        $root=Inside (Join-Path $homePath $name)
        foreach($entry in Get-ChildItem -LiteralPath $root -Force) {$entries+=@{scope=$name;name=$entry.Name;directory=$entry.PSIsContainer}}
        foreach($file in Get-ChildItem -LiteralPath $root -Recurse -File -Force) {Plain $file.FullName;$files+=@{path=[IO.Path]::GetRelativePath($homePath,$file.FullName);length=$file.Length;sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash}}
    }
    $p=Prefs
    return @{schemaVersion=1;utc=[DateTime]::UtcNow.ToString('O');home=$homePath;entries=$entries;files=$files;preferences=@{sha256=(Get-FileHash -LiteralPath $prefsPath).Hash;allowDll=$p.settings['mods.allow-dll'];modsCount=@($p.mods).Count;clientModsCount=@($p.clientMods).Count;activationNeutral=(Neutral $p)}}
}
function Receipt {
    Plain $receiptPath
    $value=Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json -AsHashtable
    if ($value.home -cne $homePath -or $value.runId -cne $RunId -or $value.schemaVersion -ne 1) {throw 'Unexpected ownership receipt'}
    foreach($relative in $value.files.Keys) {
        $relative=$relative.Replace('\','/')
        if (!($dirs | Where-Object {$relative.StartsWith($_+'/',[StringComparison]::Ordinal)}) -and $relative -notmatch '^Saves/Issue39-[A-Za-z0-9-]+\.railroad$') {throw 'Receipt contains a path outside this test set'}
        $resolved=Inside (Join-Path $homePath $relative)
        if([IO.Path]::GetRelativePath($homePath,$resolved).Replace('\','/') -cne $relative){throw 'Receipt contains a noncanonical traversal path'}
    }
    return $value
}
Plain $homePath;Plain $run
$processes=@(Get-CimInstance Win32_Process -Filter "Name='Railroader.exe'")
if ($processes.Count) {
    if ($Action -ne 'SelectVariant' -or !$AllowRunning -or $TestSandbox -or $processes.Count -ne 1) {throw 'Railroader must be closed for this action'}
    $matches=[regex]::Matches($processes[0].CommandLine,'(?i)(?:^|\s)-homedir\s+(?:"([^"]+)"|(\S+))')
    if ($matches.Count -ne 1) {throw 'Ambiguous running homedir'}
    $value=if($matches[0].Groups[1].Success){$matches[0].Groups[1].Value}else{$matches[0].Groups[2].Value}
    if (![IO.Path]::GetFullPath($value).TrimEnd('\','/').Equals($homePath,[StringComparison]::OrdinalIgnoreCase)) {throw 'Running game is not the isolated test home'}
}
if($Action -eq 'Inventory') {
    $inventory=Inventory
    Write-Json $inventory (Join-Path $run ('inventory-'+[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffffffZ')+'.json'))
    $inventory | ConvertTo-Json -Depth 20
    return
}
if($Action -eq 'Install') {
    if(Test-Path -LiteralPath $receiptPath){throw 'RunId already has an ownership receipt'}
    $initial=Inventory
    if ($initial.entries.Count -ne 0 -or !$initial.preferences.activationNeutral) {throw 'Initial isolated home is not neutral; preserve it and stop'}
    if(!$TestSandbox -and (Test-Path 'D:\SteamLibrary\steamapps\common\Railroader\Mods') -and @(Get-ChildItem 'D:\SteamLibrary\steamapps\common\Railroader\Mods' -Force -ErrorAction Stop).Count){throw 'Install-root Mods is not empty; stop without touching it'}
    $sourceFiles=@{}
    foreach($name in @('Info.json','RailroaderScenePatcher.dll','RailroaderScenePatcher.Core.dll','0Harmony.dll')) {$sourceFiles['Mods/Issue39ScenePatcher/'+$name]=Join-Path $ProductOutput $name}
    $sourceFiles['Mods/Issue39Observation/NativeObservation.dll']=$ObserverDll
    $sourceFiles['Mods/Issue39Observation/Info.json']=[IO.Path]::GetFullPath("$PSScriptRoot/../NativeObservation/Info.json")
    foreach($name in @('Info.json','Definitions.json','Data/Map.json','Data/scene-patches.json')) {$sourceFiles['ModDev/Issue39FixedPlacement/'+$name]=Join-Path $FixtureSource $name}
    foreach($relative in $sourceFiles.Keys) {
        $source=$sourceFiles[$relative];if(![IO.Path]::IsPathFullyQualified($source) -or !(Test-Path -LiteralPath $source -PathType Leaf)){throw "Missing absolute source $source"};Plain $source
        $destination=Inside (Join-Path $homePath $relative);if(Test-Path -LiteralPath $destination){throw "Refusing existing target $destination"}
    }
    $fixtureInfo=Get-Content (Join-Path $FixtureSource 'Info.json') -Raw | ConvertFrom-Json
    if($fixtureInfo.identifier -cne 'crankyant/fixed-placement-fixture'){throw 'Wrong fixture identity'}
    $p=Prefs
    $receipt=@{schemaVersion=1;home=$homePath;runId=$RunId;files=@{};initial=$initial;initialAllowDll=$p.settings['mods.allow-dll'];state='installing'}
    Write-Json $initial (Join-Path $run 'initial.json');Write-Json $receipt $receiptPath
    foreach($relative in $sourceFiles.Keys) {
        $destination=Inside (Join-Path $homePath $relative)
        # Record ownership before writing, so interrupted copies remain narrowly removable.
        $receipt.files[$relative]=@{sourceSha256=(Get-FileHash -LiteralPath $sourceFiles[$relative]).Hash}
        Write-Json $receipt $receiptPath
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
        [IO.File]::Copy($sourceFiles[$relative],$destination,$false)
    }
    $receipt.state='installed';Write-Json $receipt $receiptPath
    Write-Json (Inventory) (Join-Path $run 'installed.json')
    'Installed only the three test copies; no activation or permission changed'
    return
}
$receipt=Receipt
if($Action -eq 'SelectVariant') {
    if(!$FixtureSource){throw 'FixtureSource is required'}
    $info=Get-Content (Join-Path $FixtureSource 'Info.json') -Raw | ConvertFrom-Json
    if($info.identifier -cne 'crankyant/fixed-placement-fixture'){throw 'Wrong variant identity'}
    $source=[IO.Path]::GetFullPath((Join-Path $FixtureSource 'Data/scene-patches.json'));Plain $source
    $document=Get-Content -LiteralPath $source -Raw | ConvertFrom-Json
    if($document.sourceMap.mapId -cne 'crankyant/fixed-placement-fixture-map'){throw 'Wrong variant map'}
    foreach($pair in @(@{source=$source;relative='ModDev/Issue39FixedPlacement/Data/scene-patches.json'}) + $(if($BaselineReference){@(@{source=[IO.Path]::GetFullPath($BaselineReference);relative='Mods/Issue39Observation/Baseline.json'})}else{@()})) {
        Plain $pair.source;$destination=Inside (Join-Path $homePath $pair.relative)
        $receipt.files[$pair.relative]=@{sourceSha256=(Get-FileHash -LiteralPath $pair.source).Hash};Write-Json $receipt $receiptPath
        [IO.File]::Copy($pair.source,$destination,$true)
    }
    Write-Json @{utc=[DateTime]::UtcNow.ToString('O');variant=[IO.Path]::GetFileName($FixtureSource);patchSha256=(Get-FileHash (Join-Path $homePath 'ModDev/Issue39FixedPlacement/Data/scene-patches.json')).Hash;running=($processes.Count -ne 0)} (Join-Path $run ('variant-'+[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffffffZ')+'.json'))
    return
}
if($Action -eq 'RegisterSave') {
    $absolute=Inside $SavePath;$relative=[IO.Path]::GetRelativePath($homePath,$absolute).Replace('\','/')
    if($relative -notmatch '^Saves/Issue39-[A-Za-z0-9-]+\.railroad$' -or !(Test-Path -LiteralPath $absolute -PathType Leaf) -or @($receipt.initial.files | Where-Object {$_.path.Replace('\','/') -ceq $relative}).Count){throw 'Save must be a new named Issue39 disposable save file'}
    $receipt.files[$relative]=@{sourceSha256=(Get-FileHash -LiteralPath $absolute).Hash};Write-Json $receipt $receiptPath
    return
}
# Collect before cleanup. Only explicit owned files and known observer output are copied.
$observerPath=Inside (Join-Path $homePath 'Mods/Issue39Observation')
foreach($file in $(if(Test-Path -LiteralPath $observerPath){@(Get-ChildItem -LiteralPath $observerPath -Recurse -File -Force)}else{@()})) {
    $relative=[IO.Path]::GetRelativePath($homePath,$file.FullName).Replace('\','/');Plain $file.FullName
    if($relative -match '^Mods/Issue39Observation/Evidence/[a-f0-9]{32}/observations\.jsonl$') {$receipt.files[$relative]=@{generated=$true}}
}
foreach($relative in $receipt.files.Keys) {
    $source=Inside (Join-Path $homePath $relative)
    if(Test-Path -LiteralPath $source -PathType Leaf){$destination=Join-Path $run ('retained/'+$relative);Plain $destination;[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination))|Out-Null;[IO.File]::Copy($source,$destination,$true)}
}
Write-Json $receipt $receiptPath;Write-Json (Inventory) (Join-Path $run 'before-cleanup.json')
if($Action -eq 'Collect'){return}
if($Action -eq 'Cleanup') {
    $p=Prefs
    if(!(Neutral $p)){throw 'Disable the isolated test activations through root first; mods and clientMods must both be empty. No preferences were changed.'}
    $current=Inventory
    foreach($entry in $current.files) {if(!$receipt.files.ContainsKey($entry.path.Replace('\','/'))){throw "Unexpected file preserved before deletion: $($entry.path)"}}
    foreach($entry in $current.entries) {
        $relative=$entry.scope+'/'+$entry.name
        if($relative -notin $dirs -and !$receipt.files.ContainsKey($relative)){throw "Unexpected entry preserved before deletion: $relative"}
    }
    foreach($dir in $dirs) {
        $path=Inside (Join-Path $homePath $dir)
        if(!(Test-Path -LiteralPath $path)){continue}
        foreach($file in Get-ChildItem -LiteralPath $path -Recurse -File -Force) {
            $relative=[IO.Path]::GetRelativePath($homePath,$file.FullName).Replace('\','/');if(!$receipt.files.ContainsKey($relative)){throw "Unexpected file preserved: $relative"}
        }
    }
    foreach($relative in $receipt.files.Keys) {$path=Inside (Join-Path $homePath $relative);if(Test-Path -LiteralPath $path -PathType Leaf){Remove-Item -LiteralPath $path -Force}}
    foreach($dir in $dirs) {
        $path=Inside (Join-Path $homePath $dir)
        if(Test-Path -LiteralPath $path){foreach($child in Get-ChildItem -LiteralPath $path -Directory -Recurse -Force | Sort-Object {$_.FullName.Length} -Descending){$null=Inside $child.FullName;[IO.Directory]::Delete($child.FullName,$false)};[IO.Directory]::Delete($path,$false)}
    }
    if($p.settings['mods.allow-dll'] -ne $receipt.initialAllowDll){
        $hash=(Get-FileHash -LiteralPath $prefsPath).Hash;$p.settings['mods.allow-dll']=$receipt.initialAllowDll
        if((Get-FileHash -LiteralPath $prefsPath).Hash -cne $hash){throw 'Preferences changed concurrently; stop'}
        Write-Json $p $prefsPath
    }
    $final=Inventory;Write-Json $final (Join-Path $run 'final.json');$receipt.state='cleaned';Write-Json $receipt $receiptPath
    if($final.entries.Count -ne 0 -or !$final.preferences.activationNeutral -or $final.preferences.allowDll -ne $receipt.initialAllowDll){throw 'Final home differs from neutral initial state; preserve evidence and stop'}
    'Cleanup verified: active folders empty, activations empty, original isolated DLL permission retained'
}
