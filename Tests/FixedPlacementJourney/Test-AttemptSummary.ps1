# SPDX-FileCopyrightText: 2026 CrankyAnt
# SPDX-License-Identifier: MIT
$ErrorActionPreference='Stop'
$path=Join-Path ([IO.Path]::GetTempPath()) ('Issue39TraceTest-'+[guid]::NewGuid().ToString('N')+'.jsonl')
try {
    $lines=@()
    foreach($stage in @('resolve','retry-blocked','mutation')) {
        $unassigned=$stage -eq 'mutation'
        $payload=@{stage=$stage;id='missing-target';document='fixture';engineRuntimeId=42}|ConvertTo-Json -Compress
        $lines+=@{event=$(if($unassigned){'unassigned-attempt-trace'}else{'attempt-trace'});processId=123;engineRuntimeId=42;loadId='current';sourceLoadId=$(if($unassigned){$null}else{'current'});sourceLifecycle=$(if($unassigned){'unassigned'}else{'active-world'});sequence=$lines.Count+1;message='[Issue39Attempt] '+$payload}|ConvertTo-Json -Compress
    }
    [IO.File]::WriteAllText($path,($lines -join "`n")+"`n")
    $result=& "$PSScriptRoot/Summarize-Attempts.ps1" -ObservationPath $path -ProcessId 123 -EngineRuntimeId 42 -LoadId current|ConvertFrom-Json
    if(@($result.activeEvents).Count -ne 2 -or @($result.unassignedEvents).Count -ne 1 -or @($result.activeCounts|Where-Object stage -eq 'mutation').Count -ne 0){throw 'Unassigned startup events must not be counted as current-world mutation attempts'}
    'PASS: active-world attempts and unassigned startup traces remain distinct'
} finally {if(Test-Path -LiteralPath $path){Remove-Item -LiteralPath $path}}
