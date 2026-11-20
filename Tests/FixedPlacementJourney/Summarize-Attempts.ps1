# SPDX-FileCopyrightText: 2026 CrankyAnt
# SPDX-License-Identifier: MIT
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ObservationPath,
    [Parameter(Mandatory)][int]$ProcessId,
    [Parameter(Mandatory)][int]$EngineRuntimeId,
    [Parameter(Mandatory)][string]$LoadId,
    [long]$ThroughSequence=[long]::MaxValue
)
$ErrorActionPreference='Stop'
$active=@();$unassigned=@();$replays=@()
foreach($line in Get-Content -LiteralPath $ObservationPath) {
    if(!$line){continue};$row=$line|ConvertFrom-Json
    if($row.processId -ne $ProcessId -or $row.sequence -gt $ThroughSequence){continue}
    if($row.event -eq 'replay-end' -and $row.loadId -ceq $LoadId){$replays+=@{sequence=$row.sequence;engineSame=$row.engineSame};continue}
    if($row.event -notin @('attempt-trace','unassigned-attempt-trace')){continue}
    $message=[string]$row.message
    if(!$message.Contains('{')){throw 'Invalid trace payload'}
    $trace=$message.Substring($message.IndexOf('{'))|ConvertFrom-Json
    if($trace.engineRuntimeId -ne $EngineRuntimeId){continue}
    $item=@{stage=$trace.stage;id=$trace.id;document=$trace.document;sequence=$row.sequence;sourceFrame=$row.sourceFrame;sourceUtc=$row.sourceUtc;error=$trace.error}
    if($row.event -eq 'attempt-trace' -and $row.loadId -ceq $LoadId -and $row.sourceLoadId -ceq $LoadId -and $row.sourceLifecycle -ceq 'active-world'){$active+=$item}else{$unassigned+=$item}
}
@{processId=$ProcessId;engineRuntimeId=$EngineRuntimeId;loadId=$LoadId;throughSequence=$ThroughSequence;activeCounts=@($active|Group-Object stage,id,document|ForEach-Object {@{stage=$_.Group[0].stage;id=$_.Group[0].id;document=$_.Group[0].document;count=$_.Count}});activeEvents=$active;unassignedEvents=$unassigned;replayResults=$replays;limit='Unassigned startup traces are retained separately and cannot prove same-load counts'}|ConvertTo-Json -Depth 30
