<#
.SYNOPSIS
  Updates every field of DOCX files in Microsoft Word and compares Word's results with the results stored in the file (Windows only, needs Word).

.DESCRIPTION
  For the milestone 4.5b Word check (Docs/write-word-check.md): each file is copied to a temporary file and opened visibly through Word's COM automation with repair switched off. The result of every field of the
  body is read as stored (before any update), then the tables of contents and of figures are updated, then all fields twice (a reference may stand before what it refers to), and the results are read again. The
  fields inside a table of contents are compared as part of its lines. Differences are printed with the field's instruction and both results, and classed:
    page      only a page number differs (a PAGEREF result, or the number after the last tab of a table of contents line); counted, never a failure, because Word's pagination may differ;
    expected  the instruction is listed in <file>.skip.json (a field Write does not update, or a recorded difference; WordCheckFieldFilesTests writes the list); never a failure;
    DIFFERENT everything else.
  The copy is closed without saving. The exit code is 1 when a file has a DIFFERENT result, a different number of fields or could not be read, 2 on bad input, else 0. It prints result texts, so run it on the
  files made for the check, not on private documents.

.EXAMPLE
  pwsh tools/word-field-results.ps1 C:\temp\check\fields-contents.docx C:\temp\check\fields-captions.docx
#>
param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Files)

if (-not $Files) { Write-Error 'Give one or more .docx files.'; exit 2 }
$paths = foreach ($file in $Files) {
    $full = (Resolve-Path -LiteralPath $file -ErrorAction SilentlyContinue).Path
    if (-not $full) { Write-Error "Not a file: $file"; exit 2 }
    $full
}

function Format-Code([string]$code) { return (($code -replace '\s+', ' ').Trim()) }

function Format-Result([string]$text) {
    if ($null -eq $text) { return '' }
    return ($text -replace "`r`n?", "`n" -replace "`v", "`n").TrimEnd("`n")
}

function Format-Shown([string]$text) {
    $shown = $text -replace "`n", ' | ' -replace "`t", '\t'
    if ($shown.Length -gt 200) { $shown = $shown.Substring(0, 200) + '...' }
    return $shown
}

# The top-level fields of the body in document order: code, type and result. A field that lies inside the result of a table of contents belongs to that table.
function Read-Fields($document) {
    $all = @()
    foreach ($field in $document.Fields) {
        $code = Format-Code $field.Code.Text
        $result = $null
        $start = [int]$field.Code.Start
        $end = $start
        try {
            $range = $field.Result
            if ($null -ne $range) { $result = $range.Text; $end = [int]$range.End }
        }
        catch { $result = $null }
        $all += [pscustomobject]@{ Code = $code; Start = $start; End = $end; Result = (Format-Result $result) }
    }
    $lists = @($all | Where-Object { $_.Code -match '^TOC\b' })
    return @($all | Where-Object {
            $field = $_
            -not ($lists | Where-Object { $field.Start -gt $_.Start -and $field.Start -lt $_.End })
        })
}

$pageNumber = '^(\d+|[ivxlcdm]+|[IVXLCDM]+|[A-Z]-\d+)$'

$word = New-Object -ComObject Word.Application
$word.Visible = $true
$word.DisplayAlerts = 0
$work = Join-Path ([IO.Path]::GetTempPath()) ('word-field-results-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
$failed = 0
try {
    foreach ($full in $paths) {
        $name = Split-Path $full -Leaf
        $skipPath = [IO.Path]::ChangeExtension($full, '.skip.json')
        $expected = @()
        if (Test-Path -LiteralPath $skipPath) { $expected = @(Get-Content -LiteralPath $skipPath -Raw | ConvertFrom-Json | ForEach-Object { Format-Code $_ }) }
        $copy = Join-Path $work ([guid]::NewGuid().ToString('N') + '.docx')
        Copy-Item -LiteralPath $full -Destination $copy
        $document = $null
        try {
            $missing = [Type]::Missing
            # Open(FileName, ConfirmConversions, ReadOnly, AddToRecentFiles, PasswordDocument, PasswordTemplate, Revert,
            #      WritePasswordDocument, WritePasswordTemplate, Format, Encoding, Visible, OpenAndRepair)
            $document = $word.Documents.Open($copy, $false, $false, $false, $missing, $missing, $false, $missing, $missing, $missing, $missing, $true, $false)
            $document.ActiveWindow.View.ShowFieldCodes = $false
            $stored = @(Read-Fields $document)
            foreach ($list in @($document.TablesOfContents)) { $list.Update() }
            foreach ($list in @($document.TablesOfFigures)) { $list.Update() }
            [void]$document.Fields.Update()
            [void]$document.Fields.Update()
            $updated = @(Read-Fields $document)

            $different = 0
            $pages = 0
            $known = 0
            if ($stored.Count -ne $updated.Count) {
                $different++
                '{0}: DIFFERENT number of fields, stored {1}, Word {2}' -f $name, $stored.Count, $updated.Count
                '  stored: ' + (($stored | ForEach-Object { $_.Code }) -join ' ; ')
                '  Word:   ' + (($updated | ForEach-Object { $_.Code }) -join ' ; ')
            }
            for ($i = 0; $i -lt [Math]::Min($stored.Count, $updated.Count); $i++) {
                $before = $stored[$i]
                $after = $updated[$i]
                $isExpected = $expected -contains $before.Code
                if ($before.Code -cne $after.Code) {
                    if ($isExpected) { $known++ } else { $different++ }
                    '{0}: field {1} {2}: instruction stored "{3}", Word "{4}"' -f $name, ($i + 1), $(if ($isExpected) { 'expected' } else { 'DIFFERENT' }), $before.Code, $after.Code
                    continue
                }
                if ($before.Result -ceq $after.Result) { continue }
                if ($isExpected) {
                    $known++
                    '{0}: field {1} expected "{2}": stored "{3}", Word "{4}"' -f $name, ($i + 1), $before.Code, (Format-Shown $before.Result), (Format-Shown $after.Result)
                    continue
                }
                if ($before.Code -match '^PAGEREF\b' -and $before.Result -cmatch $pageNumber -and $after.Result -cmatch $pageNumber) {
                    $pages++
                    '{0}: field {1} page "{2}": stored {3}, Word {4}' -f $name, ($i + 1), $before.Code, $before.Result, $after.Result
                    continue
                }
                if ($before.Code -match '^TOC\b') {
                    $mine = @($before.Result -split "`n")
                    $theirs = @($after.Result -split "`n")
                    if ($mine.Count -ne $theirs.Count) {
                        $different++
                        '{0}: field {1} DIFFERENT "{2}": stored {3} lines, Word {4} lines' -f $name, ($i + 1), $before.Code, $mine.Count, $theirs.Count
                    }
                    for ($line = 0; $line -lt [Math]::Max($mine.Count, $theirs.Count); $line++) {
                        $a = if ($line -lt $mine.Count) { $mine[$line] } else { '(no line)' }
                        $b = if ($line -lt $theirs.Count) { $theirs[$line] } else { '(no line)' }
                        if ($a -ceq $b) { continue }
                        $left = if ($a -match '^(.*)\t([^\t]*)$') { , @($Matches[1], $Matches[2]) } else { $null }
                        $right = if ($b -match '^(.*)\t([^\t]*)$') { , @($Matches[1], $Matches[2]) } else { $null }
                        if ($left -and $right -and $left[0] -ceq $right[0] -and $left[1] -cmatch $pageNumber -and $right[1] -cmatch $pageNumber) {
                            $pages++
                            '{0}: field {1} page, line {2} "{3}": stored {4}, Word {5}' -f $name, ($i + 1), ($line + 1), (Format-Shown $left[0]), $left[1], $right[1]
                        }
                        else {
                            if ($mine.Count -eq $theirs.Count) { $different++ }
                            '{0}: field {1} DIFFERENT, line {2}: stored "{3}", Word "{4}"' -f $name, ($i + 1), ($line + 1), (Format-Shown $a), (Format-Shown $b)
                        }
                    }
                    continue
                }
                $different++
                '{0}: field {1} DIFFERENT "{2}": stored "{3}", Word "{4}"' -f $name, ($i + 1), $before.Code, (Format-Shown $before.Result), (Format-Shown $after.Result)
            }
            '{0}: {1} fields compared, {2} page differences, {3} expected differences, {4} DIFFERENT' -f $name, [Math]::Min($stored.Count, $updated.Count), $pages, $known, $different
            if ($different -gt 0) { $failed++ }
        }
        catch {
            $failed++
            '{0}: Word could not read it - {1}' -f $name, $_.Exception.Message.Trim()
        }
        finally {
            if ($null -ne $document) { $document.Close($false) }
        }
    }
}
finally {
    $word.Quit()
    [void][Runtime.InteropServices.Marshal]::ReleaseComObject($word)
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
if ($failed -gt 0) { exit 1 }
exit 0
