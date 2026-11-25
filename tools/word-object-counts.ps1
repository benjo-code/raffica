<#
.SYNOPSIS
  Compares the tables, notes and pictures Word reports for a DOCX file with the facts the model held when Write saved it (Windows only, needs Word).

.DESCRIPTION
  The Word check of milestone 4.5a (Docs/write-word-check.md): WordCheckObjectFilesTests writes <name>.docx and <name>.docx.facts.txt (the counts of tables,
  footnotes, endnotes, inline and floating pictures, one line per picture with its kind, size in points and description, and the rows, cells and columns of
  every table). This script opens the file read-only through Word's COM automation with repair switched off (on a temporary copy), reads the same numbers
  from Word (Document.Tables, Footnotes, Endnotes, InlineShapes, Shapes with WrapFormat.Type, Range.Cells) and prints every difference. A file that Word
  would repair fails to open and counts as a difference. The exit code is the number of differences. The text of a document is never printed (the
  description of a picture is, as the facts file holds it: the files are made by the test, not by a person).

.EXAMPLE
  pwsh tools/word-object-counts.ps1 C:\temp\check\picture-wrap.docx
#>
param([Parameter(Mandatory = $true)][string]$File)

$full = (Resolve-Path -LiteralPath $File).Path
$factsPath = $full + '.facts.txt'
if (-not (Test-Path -LiteralPath $factsPath)) { Write-Error "No facts file next to $full."; exit 2 }

$summary = @{}
$pictures = New-Object System.Collections.Generic.List[object]
$tables = New-Object System.Collections.Generic.List[object]
foreach ($line in Get-Content -LiteralPath $factsPath -Encoding UTF8) {
    if ($line -match '^summary (.*)$') {
        foreach ($m in [regex]::Matches($Matches[1], '(\w+)=(\d+)')) { $summary[$m.Groups[1].Value] = [int]$m.Groups[2].Value }
    }
    elseif ($line -match '^picture kind=(\w+) width=([\d.]+) height=([\d.]+) alt=(.*)$') {
        $pictures.Add([pscustomobject]@{ Kind = $Matches[1]; Width = [double]::Parse($Matches[2], [Globalization.CultureInfo]::InvariantCulture); Height = [double]::Parse($Matches[3], [Globalization.CultureInfo]::InvariantCulture); Alt = $Matches[4] })
    }
    elseif ($line -match '^rows=(\d+) cells=(\d+) tcs=(\d+) columns=(\d+)$') {
        $tables.Add([pscustomobject]@{ Rows = [int]$Matches[1]; Cells = [int]$Matches[2]; Columns = [int]$Matches[4] })
    }
}

function Get-WrapKind([int]$type) {
    switch ($type) { 0 { 'square' } 1 { 'square' } 2 { 'square' } 3 { 'front' } 4 { 'topbottom' } 5 { 'behind' } 7 { 'inline' } default { "wrap$type" } }
}

$differences = 0
function Report([string]$what, $expected, $actual) {
    if ("$expected" -ceq "$actual") { '  ok   {0}: {1}' -f $what, $expected }
    else { $script:differences++; '  DIFF {0}: model {1}, Word {2}' -f $what, $expected, $actual }
}

$work = Join-Path ([IO.Path]::GetTempPath()) ('word-object-counts-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
$word = New-Object -ComObject Word.Application
$word.Visible = $false
$word.DisplayAlerts = 0
try {
    $copy = Join-Path $work 'check.docx'
    Copy-Item -LiteralPath $full -Destination $copy
    $missing = [Type]::Missing
    try {
        # The document window is opened visible (the 12th argument; the application itself stays hidden): with a hidden document window Word returns an empty AlternativeText for every picture.
        $document = $word.Documents.Open($copy, $false, $true, $false, $missing, $missing, $false, $missing, $missing, $missing, $missing, $true, $false)
    }
    catch {
        $differences++
        '{0}: Word did not open it - {1}' -f (Split-Path $full -Leaf), $_.Exception.Message.Trim()
        exit $differences
    }

    '{0}: opened' -f (Split-Path $full -Leaf)
    if ($summary.Count -gt 0) {
        Report 'tables' $summary['tables'] $document.Tables.Count
        Report 'footnotes' $summary['footnotes'] $document.Footnotes.Count
        Report 'endnotes' $summary['endnotes'] $document.Endnotes.Count
        Report 'inline pictures' $summary['inline'] $document.InlineShapes.Count
        Report 'floating pictures' $summary['floating'] $document.Shapes.Count
    }

    $actual = New-Object System.Collections.Generic.List[object]
    foreach ($shape in $document.InlineShapes) {
        $actual.Add([pscustomobject]@{ Kind = 'inline'; Width = [double]$shape.Width; Height = [double]$shape.Height; Alt = [string]$shape.AlternativeText })
    }
    foreach ($shape in $document.Shapes) {
        $actual.Add([pscustomobject]@{ Kind = (Get-WrapKind ([int]$shape.WrapFormat.Type)); Width = [double]$shape.Width; Height = [double]$shape.Height; Alt = [string]$shape.AlternativeText })
    }

    $expectedSorted = @($pictures | Sort-Object Kind, @{ Expression = { [int]$_.Width } }, @{ Expression = { [int]$_.Height } }, Alt)
    $actualSorted = @($actual | Sort-Object Kind, @{ Expression = { [int]$_.Width } }, @{ Expression = { [int]$_.Height } }, Alt)
    if ($expectedSorted.Count -eq $actualSorted.Count) {
        for ($i = 0; $i -lt $expectedSorted.Count; $i++) {
            $e = $expectedSorted[$i]; $a = $actualSorted[$i]
            $sameSize = ([math]::Abs($e.Width - $a.Width) -le 0.6) -and ([math]::Abs($e.Height - $a.Height) -le 0.6)
            $alt = if ($e.Alt.Length -gt 40) { $e.Alt.Substring(0, 40) + '...' } else { $e.Alt }
            if ($e.Kind -ceq $a.Kind -and $e.Alt -ceq $a.Alt -and $sameSize) {
                '  ok   picture {0} {1}x{2} pt "{3}"' -f $e.Kind, $e.Width, $e.Height, $alt
            }
            else {
                $differences++
                '  DIFF picture: model {0} {1}x{2} pt "{3}", Word {4} {5}x{6} pt "{7}"' -f $e.Kind, $e.Width, $e.Height, $alt, $a.Kind, [math]::Round($a.Width, 1), [math]::Round($a.Height, 1), $a.Alt
            }
        }
    }
    elseif ($summary.Count -gt 0) {
        $differences++
        '  DIFF pictures: model lists {0}, Word {1}' -f $expectedSorted.Count, $actualSorted.Count
    }

    if ($tables.Count -gt 0) {
        Report 'tables with facts' $tables.Count $document.Tables.Count
        for ($i = 0; $i -lt [math]::Min($tables.Count, $document.Tables.Count); $i++) {
            $t = $document.Tables.Item($i + 1)
            Report "table $($i + 1) rows" $tables[$i].Rows $t.Rows.Count
            Report "table $($i + 1) cells" $tables[$i].Cells $t.Range.Cells.Count
            try { $columns = $t.Columns.Count } catch { $columns = $null }
            if ($null -ne $columns) { Report "table $($i + 1) columns" $tables[$i].Columns $columns }
            else { '  --   table {0} columns: not readable in Word (vertical merge)' -f ($i + 1) }
        }
    }
    $document.Close($false)
}
finally {
    $word.Quit()
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
exit $differences
