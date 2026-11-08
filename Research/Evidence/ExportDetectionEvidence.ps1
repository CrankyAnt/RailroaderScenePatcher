# SPDX-FileCopyrightText: 2026 CrankyAnt
# SPDX-License-Identifier: MIT
param(
    [Parameter(Mandatory)][string]$InputPath,
    [Parameter(Mandatory)][string]$OutputPrefix,
    [ValidatePattern('^[A-Fa-f0-9]{64}$')][string]$OriginalRawSha256
)
$ErrorActionPreference = 'Stop'
$utf8 = [System.Text.UTF8Encoding]::new($false, $true)
$source = [IO.Path]::GetFullPath($InputPath)
$bytes = [IO.File]::ReadAllBytes($source)
$content = $utf8.GetString($bytes)
if ($content.Contains("`r") -or !$content.EndsWith("`n")) { throw 'Input must be UTF-8 with LF line endings and a final LF.' }
$lines = $content.Substring(0, $content.Length - 1).Split("`n")
$names = @('Pcyi','Pipr','Pws9','Pdlh','P6id','P1db','CrankyAntDetection21_A','CrankyAntDetection21_I','CrankyAntDetection21_B','lights','gates','bell')
$original = $names[0..5]
$added = $names[6..8]
$indicators = $names[9..11]
$csv = [Collections.Generic.List[string]]::new()
$csv.Add(('sourceLine,timestamp,phase,' + ($names -join ',')))
$diagnostics = [Collections.Generic.List[string]]::new()
$counts = [ordered]@{ baseline = 0; added = 0 }
$sampleCount = 0
for ($i = 0; $i -lt $lines.Count; $i++) {
    $line = $lines[$i]
    if ($line -notmatch '\bsample\b') { $diagnostics.Add("$($i + 1)`t$line"); continue }
    if ($line -cnotmatch '^(\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d\.\d+Z) sample phase=(baseline|added) (.+)$') { throw "Invalid sample at line $($i + 1)." }
    $timestamp, $phase, $fields = $Matches[1], $Matches[2], $Matches[3]
    $expected = @($original) + $(if ($phase -ceq 'added') { @($added) }) + @($indicators)
    $tokens = $fields.Split(' ')
    if ($tokens.Count -ne $expected.Count) { throw "Unexpected field count at line $($i + 1)." }
    $values = @{}
    for ($j = 0; $j -lt $tokens.Count; $j++) {
        if ($tokens[$j] -cnotmatch '^([A-Za-z0-9_]+)=(True|False)$' -or $Matches[1] -cne $expected[$j]) { throw "Invalid sample field at line $($i + 1)." }
        $values[$Matches[1]] = $Matches[2]
    }
    $row = @([string]($i + 1), $timestamp, $phase) + @($names | ForEach-Object { if ($values.ContainsKey($_)) { $values[$_] } else { '' } })
    $csv.Add(($row -join ','))
    $counts[$phase]++
    $sampleCount++
}
$csvPath = [IO.Path]::GetFullPath("$OutputPrefix.csv")
$diagnosticPath = [IO.Path]::GetFullPath("$OutputPrefix.diagnostics.log")
$provenancePath = [IO.Path]::GetFullPath("$OutputPrefix.provenance.json")
[IO.File]::WriteAllText($csvPath, (($csv -join "`n") + "`n"), $utf8)
[IO.File]::WriteAllText($diagnosticPath, (($diagnostics -join "`n") + $(if ($diagnostics.Count) { "`n" })), $utf8)
# Reconstruct from the written files, including source order and exact field spelling.
$reconstructed = [string[]]::new($lines.Count)
foreach ($entry in [IO.File]::ReadAllLines($diagnosticPath, $utf8)) {
    $parts = $entry.Split("`t", 2)
    $reconstructed[[int]$parts[0] - 1] = $parts[1]
}
foreach ($row in (Import-Csv -LiteralPath $csvPath)) {
    $fieldNames = @($original) + $(if ($row.phase -ceq 'added') { @($added) }) + @($indicators)
    $fieldText = ($fieldNames | ForEach-Object { "$_=$($row.$_)" }) -join ' '
    $reconstructed[[int]$row.sourceLine - 1] = "$($row.timestamp) sample phase=$($row.phase) $fieldText"
}
$rebuilt = $utf8.GetBytes((($reconstructed -join "`n") + "`n"))
$hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))
if ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($rebuilt)) -cne $hash) { throw 'Reconstructed input hash differs.' }
$relative = [IO.Path]::GetRelativePath((Get-Location).Path, $source).Replace('\','/')
$provenance = [ordered]@{
    sourcePath = $relative
    inputSha256 = $hash
    csvSha256 = (Get-FileHash -LiteralPath $csvPath -Algorithm SHA256).Hash
    diagnosticSha256 = (Get-FileHash -LiteralPath $diagnosticPath -Algorithm SHA256).Hash
    originalRawSha256 = if ($OriginalRawSha256) { $OriginalRawSha256.ToUpperInvariant() } else { $null }
    inputLines = $lines.Count
    sampleLines = $sampleCount
    nonSampleLines = $diagnostics.Count
    phaseCounts = $counts
    reconstructionSha256 = $hash
}
[IO.File]::WriteAllText($provenancePath, (($provenance | ConvertTo-Json -Depth 4) + "`n"), $utf8)
$provenance | ConvertTo-Json -Depth 4
