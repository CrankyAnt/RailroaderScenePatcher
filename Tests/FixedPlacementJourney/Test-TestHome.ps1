# SPDX-FileCopyrightText: 2026 CrankyAnt
# SPDX-License-Identifier: MIT
$ErrorActionPreference='Stop'
$sandbox=Join-Path ([IO.Path]::GetTempPath()) ('Issue39HomeTest-'+[guid]::NewGuid().ToString('N'))
$homePath=Join-Path $sandbox 'RuntimeHome'
function Check($condition,[string]$message){if(!$condition){throw $message}}
try {
    foreach($name in @('Mods','ModDev','Saves')){[IO.Directory]::CreateDirectory((Join-Path $homePath $name))|Out-Null}
    [IO.File]::WriteAllText((Join-Path $homePath 'Preferences.json'),'{"settings":{"mods.allow-dll":1,"untouched":"keep"},"mods":[],"clientMods":[]}')
    $product=Join-Path $sandbox 'Product';[IO.Directory]::CreateDirectory($product)|Out-Null
    foreach($name in @('RailroaderScenePatcher.dll','RailroaderScenePatcher.Core.dll','0Harmony.dll')){[IO.File]::WriteAllText((Join-Path $product $name),'Synthetic test bytes, never loaded')}
    [IO.File]::WriteAllText((Join-Path $product 'Info.json'),'{"identifier":"crankyant/scene-patcher"}')
    $observer=Join-Path $sandbox 'NativeObservation.dll';[IO.File]::WriteAllText($observer,'Synthetic observer bytes, never loaded')
    $fixtures=Join-Path $sandbox 'Fixtures'
    & "$PSScriptRoot/Prepare-Fixtures.ps1" -Bootstrap -OutputRoot $fixtures
    $common=@{RunId='Offline';TestSandbox=$sandbox}
    & "$PSScriptRoot/TestHome.ps1" @common -Action Install -ProductOutput $product -ObserverDll $observer -FixtureSource (Join-Path $fixtures 'Baseline')
    $ownedDll=Join-Path $homePath 'Mods/Issue39ScenePatcher/RailroaderScenePatcher.dll'
    Check (Test-Path $ownedDll) 'Owned DLL must be installed'
    $stopped=$false
    try {& "$PSScriptRoot/TestHome.ps1" @common -Action Install -ProductOutput $product -ObserverDll $observer -FixtureSource (Join-Path $fixtures 'Baseline')} catch {$stopped=$true}
    Check $stopped 'Existing receipt must stop another install'
    $receiptPath=Join-Path $sandbox 'Evidence/Offline/ownership.json';$originalReceipt=[IO.File]::ReadAllText($receiptPath)
    $badReceipt=$originalReceipt|ConvertFrom-Json -AsHashtable;$badReceipt.files['Mods/Issue39ScenePatcher/../../Preferences.json']=@{sourceSha256='not-owned'}
    [IO.File]::WriteAllText($receiptPath,($badReceipt|ConvertTo-Json -Depth 30))
    $stopped=$false;try{& "$PSScriptRoot/TestHome.ps1" @common -Action Cleanup}catch{$stopped=$true}
    Check ($stopped -and (Test-Path (Join-Path $homePath 'Preferences.json'))) 'A traversing ownership receipt must be rejected before reading or deleting its target'
    [IO.File]::WriteAllText($receiptPath,$originalReceipt)
    $save=Join-Path $homePath 'Saves/Issue39-Disposable.railroad';[IO.File]::WriteAllText($save,'Synthetic save')
    & "$PSScriptRoot/TestHome.ps1" @common -Action RegisterSave -SavePath $save
    $sentinel=Join-Path $homePath 'Mods/Issue39Observation/unowned.txt';[IO.File]::WriteAllText($sentinel,'Preserve unexpected content')
    $stopped=$false;try{& "$PSScriptRoot/TestHome.ps1" @common -Action Cleanup}catch{$stopped=$true}
    Check ($stopped -and (Test-Path $sentinel) -and (Test-Path $ownedDll)) 'Unexpected content must stop cleanup before owned copies are deleted'
    Remove-Item -LiteralPath $sentinel
    & "$PSScriptRoot/TestHome.ps1" @common -Action SelectVariant -FixtureSource (Join-Path $fixtures 'Baseline')
    & "$PSScriptRoot/TestHome.ps1" @common -Action Cleanup
    & "$PSScriptRoot/TestHome.ps1" @common -Action Cleanup
    Check (!(Test-Path $ownedDll) -and !(Test-Path $save)) 'Only owned copies should be removed'
    $p=Get-Content (Join-Path $homePath 'Preferences.json') -Raw | ConvertFrom-Json -AsHashtable
    Check ($p.settings.untouched -ceq 'keep' -and $p.settings['mods.allow-dll'] -eq 1 -and @($p.mods).Count -eq 0 -and @($p.clientMods).Count -eq 0) 'Isolated preference values must remain neutral and unrelated settings unchanged'
    Check (Test-Path (Join-Path $sandbox 'Evidence/Offline/final.json')) 'Retained end inventory must exist'
    'PASS: synthetic home install, collision refusal, exact variant switch, unknown-file preservation and scoped cleanup'
} finally {
    $full=[IO.Path]::GetFullPath($sandbox)
    if([IO.Path]::GetFileName($full) -notmatch '^Issue39HomeTest-[a-f0-9]{32}$' -or ![IO.Path]::GetDirectoryName($full).Equals([IO.Path]::GetTempPath().TrimEnd('\','/'),[StringComparison]::OrdinalIgnoreCase)){throw "Unsafe synthetic cleanup $full"}
    if(Test-Path -LiteralPath $full){Remove-Item -LiteralPath $full -Recurse -Force}
}
