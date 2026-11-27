# SPDX-FileCopyrightText: 2026 CrankyAnt
# SPDX-License-Identifier: MIT
# Checks what the code and the documentation say about each other, without building the code:
# - every guide file that the code links to, a ".md" string literal in Src/**/*.cs, exists under docs/guides/scene-patches;
# - neither the glossary nor a guide names a family "Track marker", and the glossary's Family entry names Generic track marker.
param([string]$Root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$failures = [Collections.Generic.List[string]]::new()
function Fail([string]$message) { $failures.Add($message) }

# The fewest guide links the code holds today is well above this; the check cannot pass on a scan that finds none.
$minimumGuideLinks = 20
$guideFolder = Join-Path $Root 'docs/guides/scene-patches'
$guideName = '^(?<file>[a-z0-9]+(-[a-z0-9]+)*\.md)(#[a-z0-9]+(-[a-z0-9]+)*)?$'
$guideUrl = '^https://github\.com/CrankyAnt/RailroaderScenePatcher/blob/main/docs/guides/scene-patches/(?<file>[a-z0-9]+(-[a-z0-9]+)*\.md)(#[a-z0-9]+(-[a-z0-9]+)*)?$'

# The tokens of a C# source that can hold a ".md": comments, which the scan skips, and string literals. One pass reads
# them in order, so a quote inside a comment or a comment marker inside a literal is read as what it is. An interpolated
# literal is one token with its holes; the code has no literal inside a hole.
$token = [regex]::new(@'
(?<comment>//[^\n]*|/\*[\s\S]*?\*/)
|(?<char>'(?:[^'\\\n]|\\.)+')
|(?<raw>\$*"{3}[\s\S]*?"{3})
|(?<verbatim>(?:\$@|@\$|@)"(?<text>(?:[^"]|"")*)")
|(?<regular>\$?"(?<text>(?:[^"\\\n]|\\.)*)")
'@, [Text.RegularExpressions.RegexOptions]::IgnorePatternWhitespace)

# Guide links in the code.
$sources = Get-ChildItem -LiteralPath (Join-Path $Root 'Src') -Recurse -File -Filter '*.cs' |
    Where-Object { ($_.FullName.Substring($Root.Length) -split '[\\/]') -notcontains 'bin' -and ($_.FullName.Substring($Root.Length) -split '[\\/]') -notcontains 'obj' }
$links = 0
$linked = [Collections.Generic.SortedSet[string]]::new([StringComparer]::Ordinal)
foreach ($source in $sources) {
    $relative = $source.FullName.Substring($Root.Length + 1).Replace('\', '/')
    $text = [IO.File]::ReadAllText($source.FullName)
    $code = [Text.StringBuilder]::new()
    $last = 0
    foreach ($m in $token.Matches($text)) {
        [void]$code.Append($text, $last, $m.Index - $last)
        $last = $m.Index + $m.Length
        if ($m.Groups['comment'].Success -or $m.Groups['char'].Success) { continue }
        $literal = if ($m.Groups['raw'].Success) { $m.Value } else { $m.Groups['text'].Value }
        if ($literal -notmatch '\.md(?![A-Za-z0-9_])') { continue }
        $match = [regex]::Match($literal, $guideName)
        if (-not $match.Success) { $match = [regex]::Match($literal, $guideUrl) }
        if ($m.Groups['raw'].Success -or -not $match.Success) { Fail "${relative}: the literal $($m.Value) names a .md file but is no whole guide link"; continue }
        $links++
        $file = $match.Groups['file'].Value
        [void]$linked.Add($file)
        if (-not (Test-Path -LiteralPath (Join-Path $guideFolder $file) -PathType Leaf)) { Fail "${relative}: the guide $file that the code links to is not in docs/guides/scene-patches" }
    }
    [void]$code.Append($text, $last, $text.Length - $last)
    # A ".md" in code outside every literal and comment is a link the scan cannot read, such as a name built from parts.
    if ($code.ToString() -match '\.md(?![A-Za-z0-9_])') { Fail "${relative}: a .md outside a whole string literal, which the guide link check cannot read" }
}
if ($links -lt $minimumGuideLinks) { Fail "The guide link check found $links guide links in Src, fewer than $minimumGuideLinks; it no longer reads the code's links" }

# The family name "Track marker" in the glossary and the guides.
$glossaryPath = Join-Path $Root 'GLOSSARY.md'
$documents = @(Get-Item -LiteralPath $glossaryPath) + @(Get-ChildItem -LiteralPath (Join-Path $Root 'docs/guides') -Recurse -File -Filter '*.md')
$family = '(?<![Gg]eneric )(\*\*|`)?Track marker(\*\*|`)? family'
foreach ($document in $documents) {
    $number = 0
    foreach ($line in [IO.File]::ReadAllLines($document.FullName)) {
        $number++
        if ($line -cmatch $family) { Fail "$($document.Name):${number}: names a family Track marker; the family is Generic track marker: $line" }
    }
}
$glossary = [IO.File]::ReadAllText($glossaryPath).Replace("`r`n", "`n")
$at = $glossary.IndexOf('**Family**', [StringComparison]::Ordinal)
if ($at -lt 0) { Fail 'GLOSSARY.md has no **Family** entry' }
else {
    $entry = $glossary.Substring($at)
    $end = $entry.IndexOf("`n**", 2, [StringComparison]::Ordinal)
    if ($end -ge 0) { $entry = $entry.Substring(0, $end) }
    if (-not $entry.Contains('Generic track marker')) { Fail "The glossary's Family entry does not name the family Generic track marker" }
}

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Host "FAIL: $_" }
    exit 1
}
Write-Host "PASS: the code's $links guide links name $($linked.Count) guides that exist, and the glossary and guides name the family Generic track marker"
