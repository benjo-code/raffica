<#
.SYNOPSIS
  Opens DOCX files in Microsoft Word and says for each whether Word accepted it (Windows only, needs Word).

.DESCRIPTION
  The owner's check for "Word shows no repair prompt for a file Write saved" without clicking: each file is copied to a temporary
  file and opened read-only through Word's COM automation with repair switched off, so a file Word would repair fails here.
  The exit code is the number of files Word did not open. Nothing is written next to the files and the text of a document is never
  printed (only the file name, the paragraph and page counts, or Word's error message).

.EXAMPLE
  pwsh tools/word-open-check.ps1 C:\temp\check\*.docx
#>
param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Files)

if (-not $Files) { Write-Error 'Give one or more .docx files.'; exit 2 }
$word = New-Object -ComObject Word.Application
$word.Visible = $false
$word.DisplayAlerts = 0
$failed = 0
$work = Join-Path ([IO.Path]::GetTempPath()) ('word-open-check-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
try {
    foreach ($file in $Files) {
        $full = (Resolve-Path -LiteralPath $file).Path
        $copy = Join-Path $work ([guid]::NewGuid().ToString('N') + '.docx')
        Copy-Item -LiteralPath $full -Destination $copy
        try {
            $missing = [Type]::Missing
            # Open(FileName, ConfirmConversions, ReadOnly, AddToRecentFiles, PasswordDocument, PasswordTemplate, Revert,
            #      WritePasswordDocument, WritePasswordTemplate, Format, Encoding, Visible, OpenAndRepair)
            $document = $word.Documents.Open($copy, $false, $true, $false, $missing, $missing, $false, $missing, $missing, $missing, $missing, $false, $false)
            '{0}: opened ({1} paragraphs, {2} pages)' -f (Split-Path $full -Leaf), $document.Paragraphs.Count, $document.ComputeStatistics(2)
            $document.Close($false)
        }
        catch {
            $failed++
            '{0}: Word did not open it - {1}' -f (Split-Path $full -Leaf), $_.Exception.Message.Trim()
        }
    }
}
finally {
    $word.Quit()
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
exit $failed
