<#
.SYNOPSIS
  Reads the page numbers Word shows for a DOCX file and compares them with the numbers Write's layout draws (Windows only, needs Word).

.DESCRIPTION
  For the milestone 4.4b Word check (Docs/write-word-check.md): the file is copied to a temporary file and opened read-only through Word's COM automation with
  repair switched off. For every page Word lays out, the first paragraph that starts on the page is read: Range.Information(wdActiveEndAdjustedPageNumber) says the number
  Word prints there (a number as a value, whatever its format). A page on which no paragraph starts (a blank page that an even-page or odd-page section makes, or one inside a long paragraph) cannot
  be reached and is reported as skipped, not compared. The expected numbers are in <file>.numbers.json next to the file (the test WordCheckSectionFilesTests writes
  them: one value per page, null for a page with no number). The exit code is 1 when the number of pages or any compared number differs, 2 on bad input, else 0.
  The text of a document is never printed.

.EXAMPLE
  pwsh tools/word-page-numbers.ps1 C:\temp\check\page-three-one.docx
#>
param([Parameter(Mandatory = $true)][string]$File)

$full = (Resolve-Path -LiteralPath $File -ErrorAction SilentlyContinue).Path
if (-not $full) { Write-Error 'Give an existing .docx file.'; exit 2 }
$jsonPath = [IO.Path]::ChangeExtension($full, '.numbers.json')
if (-not (Test-Path -LiteralPath $jsonPath)) { Write-Error "Missing $jsonPath (made by WordCheckSectionFilesTests)."; exit 2 }
$expected = @(Get-Content -LiteralPath $jsonPath -Raw | ConvertFrom-Json)

$word = New-Object -ComObject Word.Application
$word.Visible = $false
$word.DisplayAlerts = 0
$work = Join-Path ([IO.Path]::GetTempPath()) ('word-page-numbers-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
$differences = 0
try {
    $copy = Join-Path $work 'check.docx'
    Copy-Item -LiteralPath $full -Destination $copy
    $missing = [Type]::Missing
    $document = $word.Documents.Open($copy, $false, $true, $false, $missing, $missing, $false, $missing, $missing, $missing, $missing, $false, $false)
    $pages = $document.ComputeStatistics(2)
    '{0}: Word has {1} pages, Write has {2}' -f (Split-Path $full -Leaf), $pages, $expected.Count
    if ($pages -ne $expected.Count) { $differences++; 'DIFFERENT page count' }
    # The first paragraph that starts on each page: Word's own page number (absolute) and the number it prints there (adjusted: after restarts, as a value).
    $first = @{}
    $count = $document.Paragraphs.Count
    for ($i = 1; $i -le $count; $i++) {
        $range = $document.Paragraphs.Item($i).Range
        $range.Collapse(1)
        $absolute = [int]$range.Information(3)       # wdActiveEndPageNumber
        if (-not $first.ContainsKey($absolute)) { $first[$absolute] = [int]$range.Information(1) }   # wdActiveEndAdjustedPageNumber
    }
    $skipped = 0
    $compared = 0
    $top = [Math]::Max($pages, $expected.Count)
    for ($page = 1; $page -le $top; $page++) {
        $want = if ($page -le $expected.Count) { $expected[$page - 1] } else { 'no page' }
        if (-not $first.ContainsKey($page)) {
            $skipped++
            'page {0}: no paragraph starts on it in Word (Write draws {1}), not compared' -f $page, $(if ($null -eq $want) { 'none' } else { $want })
            continue
        }
        $adjusted = $first[$page]
        if ($null -eq $want) {
            $compared++
            'page {0}: Word {1}, Write draws no number (not compared)' -f $page, $adjusted
        }
        elseif ($want -is [string] -or [int]$want -ne $adjusted) {
            $compared++
            $differences++
            'page {0}: DIFFERENT, Word {1}, Write {2}' -f $page, $adjusted, $want
        }
        else { $compared++ }
    }
    '{0} pages compared, {1} skipped, {2} differences' -f $compared, $skipped, $differences
    $document.Close($false)
}
finally {
    $word.Quit()
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
if ($differences -gt 0) { exit 1 }
exit 0
