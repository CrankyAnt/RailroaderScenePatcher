# SPDX-FileCopyrightText: 2026 CrankyAnt
# SPDX-License-Identifier: MIT
param(
    [Parameter(Mandatory = $true)][string]$InputLog,
    [Parameter(Mandatory = $true)][string]$OutputFile
)
$ErrorActionPreference = 'Stop'
$lines = [IO.File]::ReadAllLines((Resolve-Path -LiteralPath $InputLog))
$selected = [Collections.Generic.List[string]]::new()
$selected.Add('# Selected probe lines. L numbers are one-based source log lines.')
for ($i = 0; $i -lt $lines.Length; $i++) {
    $line = $lines[$i]
    if ($line -match 'loaded assembly=|baseline controller=|baseline set=|gate-observer (begin|device|elapsedSeconds=.*reason=)|coverage id=|added membership=|removed; original references|deferred destruction verified|sample phase=|TELEMETRY ERROR|GATE OBSERVER ERROR|LEAVING cleanup| failed; see log') {
        $selected.Add(('L{0}: {1}' -f ($i + 1), $line))
    }
}
[IO.File]::WriteAllText([IO.Path]::GetFullPath($OutputFile), (($selected -join "`n") + "`n"), [Text.UTF8Encoding]::new($false))
[pscustomobject]@{
    SourceLines = $lines.Length
    SelectedLines = $selected.Count - 1
    SourceSHA256 = (Get-FileHash -LiteralPath $InputLog -Algorithm SHA256).Hash
    OutputSHA256 = (Get-FileHash -LiteralPath $OutputFile -Algorithm SHA256).Hash
}
