# Raffica Write: Word check of an edited, saved file

The owner's checklist for the one thing CI cannot answer for milestone 4.3a: whether **Microsoft Word accepts a file that Write edited and saved**, without a repair prompt and with the text and the formatting it should have. CI proves that the saved file is read again by Write and lays out like the edited model (`RealEditorOracleTests`, `SaveFlowTests.AnEditedThesisSavedThroughTheFlowLaysOutAsTheEditedModelDoes`), and that every part Write did not touch is byte-identical. It does not run Word. The first owner pass on Windows (2026-11-21, Word for Microsoft 365, German, Windows 11) is recorded in the result tables below; the first row of the edited-file table found a repair prompt that is fixed (decision "Raffica Write: one paragraph in a text, combo box, drop-down list or date control (M4.3a)"), the owner confirmed the fix with a fresh save the same day. Nothing was checked on a Mac.

## What is known not to be renumbered in 4.3a

Write does not renumber identifiers after an edit. Splitting a paragraph (Enter) gives the second half a copy of the first half's `w14:paraId`, `w14:textId`, revision ids (`w:id` of a tracked change) and content control ids (`w:id` of `w:sdtPr`), so a saved file can hold duplicates. A paste clears `w14:paraId` and `w14:textId` of the pasted paragraphs; a split does not. Whether Word tolerates the duplicates, and whether it repairs or renumbers them silently, is exactly what this check finds out. Duplicates that make Word show a repair prompt are a finding for a later milestone, not a surprise.

## The files

Use copies. Open each in Write (a Native AOT build), edit as listed, save, then open the **saved** file in Word.

| File | Why |
| --- | --- |
| `testdata/write-corpus/thesis/thesis.docx` | The generated thesis: numbered headings, fields, a table of contents, footnotes, tables, headers and footers, sections. |
| `testdata/write-corpus/poi-bug54849/poi-bug54849.docx` | Block content controls, bookmarks, endnotes, footnotes, a table. |
| `testdata/write-corpus/poi-60316/poi-60316.docx` | Block content controls, fields, bookmarks, numbering, text boxes, floating pictures. |

## Edits

For each file, one session:

1. Type a few words in the middle of a paragraph, and in a paragraph **inside a content control** (poi-bug54849 and poi-60316; in the thesis, inside a table cell instead).
2. Press Enter in the middle of a paragraph, twice in a row, and once inside a content control.
3. Select from the middle of one paragraph to the middle of a paragraph three or four paragraphs later and delete; undo; redo.
4. Copy a paragraph and paste it two pages later; paste it once inside a content control.
5. Backspace at the start of a paragraph (merge), and Backspace after a field (the first press selects the field, the second removes it).
6. Edit the footnote text of one footnote, and the text of a header or footer (double click into it), where the file has them.
7. Save (Ctrl+S). Close the file in Write.

Then once more with **Save as** to a new name (Ctrl+Shift+S) after a short edit, and open that file too.

## Formatting and a style update (milestone 4.3b)

The same question for what 4.3b writes: **does Word accept a file after direct formatting and after "Update style to match"?** CI proves that the saved file is read again by Write and lays out like the edited model (`RealEditorOracleTests` with the formatting steps), and that removing an override equals "before minus that element" on the serialized XML (`OverrideRemovalTests`). It does not run Word. The result rows below are empty on purpose.

Use copies of `testdata/write-corpus/thesis/thesis.docx` and `testdata/write-corpus/poi-60316/poi-60316.docx`. In Write (a Native AOT build):

1. **Formatting:** select a few words and make them bold, then italic, underlined, with a font, a size, a text colour and a highlight; select three paragraphs across a table cell edge or a content control (poi-60316) and set alignment, an indent, spacing before and after and a line spacing; apply a paragraph style and a character style; type a word after Ctrl+B at a caret; select all and make it bold (the thesis takes about 2 s); in the formatting pane remove one row of a paragraph and one row of a selection, then Clear direct formatting on one paragraph. Save (Ctrl+S) and Save as to a new name.
2. **Style update:** in the thesis, give one body paragraph an alignment, a spacing and a bold run that differ from its style, then use "Update style to match" and save. Several other paragraphs of that style change. Save and Save as.

In Word, open each saved file and record: a repair prompt (exact text, any prompt is a finding); whether the formatting looks as in Write (bold, font, size, colour, highlight, alignment, indents, spacing, the styles in the style gallery); whether the paragraphs of the updated style look as in Write; whether Word's "Review > Compare" of the original against the saved file shows only the formatting changes you made; whether the duplicate `w14:paraId` values after a split (see above) are tolerated.

| Date | Word version and system | File | Action (formatting, style update) | Repair prompt (text) | Formatting as in Write | Styles | Compare shows only my changes | Notes and findings |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 2026-11-21 | Word for Microsoft 365 (de), Windows 11 | thesis.docx | formatting (bold, italic, font, size, colour, highlight, alignment, styles, pane removals), Save and Save as | none | yes (owner: everything worked) | yes | not recorded | Opens by script too (`tools/word-open-check.ps1`). |
| 2026-11-21 | Word for Microsoft 365 (de), Windows 11 | thesis.docx | Update style to match, Save and Save as | none | yes (owner: everything worked) | yes | not recorded | The updated style makes Word lay the file out on 411 pages against 322 (Word, fonts of the file not installed): the owner changed the body style (not compared in detail). |

## New document and recovery (milestone 4.3c)

Two questions for what 4.3c writes: **does Word open a blank document made by Write, and a recovery copy that was restored and saved, without a repair prompt, and does the style set look as designed?** CI proves that the blank document passes the Open XML SDK's schema validation, that Write reads it again and lays it out as one empty page (`BlankDocumentTests`, `BlankDocumentLayoutTests`), that a recovery copy equals what a save of the same model writes (`SnapshotCacheOracleTests`, `DocxSnapshotGuardTests`) and that a killed process leaves a copy that opens with the typed text (`KilledProcessRecoveryTests`). It does not run Word. The result rows below are empty on purpose.

In Write (a Native AOT build):

1. **Blank document:** Home > New document (Ctrl+N). Type a heading line and apply Title, Heading 1, Heading 2 and Heading 3 to different paragraphs from the style picker, type body text, a caption line (Caption) and a contents-style line (TOC 1); make one word bold and apply the Hyperlink character style to another word if the character style picker lists it. Save as `blank-check.docx` (the first Save asks for a name). Do it once with the region set to the United States or Canada (US Letter) and once with another region (A4), if you can switch.
2. **Recovery copy:** in a new untitled document type a few paragraphs and make some text bold; wait 35 seconds (or switch to another window, which writes a copy at once); end the process (Task Manager "End task", or `kill -9`). Start Write again, choose Restore in the card, check the text, and Save as `restored-check.docx`. Do the same once with a copy of `testdata/write-corpus/thesis/thesis.docx` (edit a heading, wait, kill, restore, Save as).

In Word, open each saved file and record: a repair prompt (exact text, any prompt is a finding); whether the styles appear in the style gallery with the look in `Docs/DECISIONS.md`, "Raffica Write: new documents and recovery (M4.3c)" (Carlito 11 pt body with 8 pt after and line 1.1; Caladea bold headings from 20 pt down, blue for Heading 1 to 3 and Title, dark slate below; Title 28 pt; Caption italic 9 pt grey; Hyperlink blue underlined); whether the page size and margins are as expected (A4 with 2.5 cm, or Letter with 1 inch); whether Heading 1 to 9 show in the navigation pane as outline levels 1 to 9 and carry no numbers; whether the restored text and formatting equal what was typed; whether Word's "Review > Compare" of the thesis against the restored copy shows only the edit you made.

| Date | Word version and system | File | Action (blank document, restored copy) | Repair prompt (text) | Style set as designed | Page size and margins | Text and formatting as typed | Notes and findings |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 2026-11-21 | Word for Microsoft 365 (de), Windows 11 | blank-check.docx | blank document: Title, Heading 1 to 3, caption, body text, Save as | none | owner: looks normal | not recorded | text and formatting as typed | Region and paper size not varied. |
| 2026-11-21 | Word for Microsoft 365 (de), Windows 11 | restored-check.docx, thesis_restore.docx | recovery copy: kill, Restore, Save as (an untitled document and the thesis) | none | owner: looks normal | not recorded | edits are there | Both also open by script. |

## Headings, lists and the outline (milestone 4.4a)

The question for what 4.4a writes: **does Word open a file with numbered headings, lists and a moved chapter without a repair prompt, and do the numbers read as in Write?** CI proves that the layout equals a full layout after every structure operation and that the numbers drawn on the pages equal an independent count of the model (`StructureOracleTests`, `RealEditorOracleTests`, `ReferenceNumberingTests`), also for the saved file read again. It does not run Word, and the script check below does not look at the numbers.

The files are made by a test through the editor and `DocxFile.SaveAsync` (nothing is written without the folder variable):

```
$env:RAFFICA_WORD_CHECK_DIR = '<folder>'
dotnet test tests/Raffica.Write.Tests -c Release --filter-class "*WordCheckFilesTests"
pwsh tools/word-open-check.ps1 <folder>\numbered-headings.docx <folder>\lists.docx <folder>\numbered-thesis-chapter-move.docx
```

| File | What it holds |
| --- | --- |
| `numbered-headings.docx` | A blank document (A4) with four chapters and one sub-heading: the numbering switched on, the chapter "Methods" (with its sub-heading and body paragraphs) moved in front of "Discussion", and the sub-heading promoted to a chapter. |
| `lists.docx` | A bullet list of three, a numbered list of five restarted at the fourth item, and a multilevel numbered list made by changing levels (what Tab does). |
| `numbered-thesis-chapter-move.docx` | The generated thesis (3689 paragraphs): the second chapter moved in front of the last, the first sub-heading promoted with its sub-headings, the heading numbering switched off and on again. |

In Word, open each file and record: a repair prompt (exact text, any prompt is a finding); whether the headings show their numbers (1, 1.1 and so on) in the order of the document after the move and the promote; whether the numbers of the lists read 1 2 3, then the restart at the fourth item and 1.1, 1.1.1 in the multilevel list, and the bullets look right (Write draws every bullet round); whether the thesis numbers are continuous after the moved chapter; whether the navigation pane shows the new order; whether Word's "Review > Compare" of the thesis against `testdata/write-corpus/thesis/thesis.docx` shows the moved chapter as moved and nothing else.

| Date | Word version and system | File | Action | Repair prompt (text) | Numbers as in Write | Notes and findings |
| --- | --- | --- | --- | --- | --- | --- |
| 2026-11-22 | Word for Microsoft 365 (de), Windows 11 (script `tools/word-open-check.ps1`, read-only, repair off) | numbered-headings.docx | opened by script | none (opened, 10 paragraphs, 1 page) | OPEN: a person has not looked | The script checks the repair prompt only. |
| 2026-11-22 | Word for Microsoft 365 (de), Windows 11 (script) | lists.docx | opened by script | none (opened, 15 paragraphs, 1 page) | OPEN: a person has not looked | As above. |
| 2026-11-22 | Word for Microsoft 365 (de), Windows 11 (script) | numbered-thesis-chapter-move.docx | opened by script | none (opened, 3689 paragraphs, 321 pages) | OPEN: a person has not looked | The page count (321) is not explained and the owner compares it in Word; a guess, not checked: the promoted Heading 2 becomes a Heading 1, which may carry page-break-before. |

## Checking a file from a script

On a Windows machine with Word, `pwsh tools/word-open-check.ps1 <files>` opens each file through Word's COM automation (read-only, repair off, on a temporary copy) and prints whether Word accepted it; the exit code is the number of files Word refused. It answers the repair-prompt question for any number of files at once and is how the cause of the first finding was bisected (`Docs/DECISIONS.md`, "one paragraph in a text, combo box, drop-down list or date control"). It does not look at the result, so the "looks as in Write" columns stay the owner's.

## In Word

Open the saved file. Record, per file and per save kind:

- Did Word show a repair prompt ("Word found unreadable content...")? **Any prompt is a finding**; record its exact text.
- Is the edited text where it should be, in the right paragraph, with the formatting of the paragraph it was typed in?
- Do the fields (page numbers, the table of contents, cross references) show a value, and does Update Field work?
- Do the content controls still exist as controls (click into one; in poi-60316 and poi-bug54849)?
- Do footnotes and endnotes still point to the right text, and is the text you edited in the right note?
- Do headers, footers, numbering, tables and pictures look as in the original?
- Does Word's "Review > Compare" (original against saved) show only the edits you made?

## Results

| Date | Word version and system | File | Save or Save as | Repair prompt (text) | Text and formatting | Fields | Content controls | Notes and findings |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 2026-11-21 | Word for Microsoft 365 (de), Windows 11 | thesis.docx | Save and Save as after the edits above | none reported | owner: no finding | fields not recorded | no content controls | The saved files open by script (`tools/word-open-check.ps1`). Backspace after a field was tried on a line after a table, where it does not apply. |
| 2026-11-21 | Word for Microsoft 365 (de), Windows 11 | poi-bug54849.docx | Save, then Save as | Ctrl+S: a repair prompt (unreadable content); Save as: the first file did not open at all, the second one opened with no prompt | not recorded | not recorded | not recorded | Cause: Enter inside a plain-text content control made a second paragraph; combo box, drop-down list and date controls fail the same way. Fixed (decision "one paragraph in a text, combo box, drop-down list or date control"); the owner checked a fresh save with the fixed build on 2026-11-21 and considers it done. Backspace at the start of a paragraph after a table or content control does not merge, by design (the owner reported it as not working). |
| 2026-11-21 | Word for Microsoft 365 (de), Windows 11 | poi-60316.docx | Save and Save as | none | looks completely different from Write | not recorded | not recorded | A known limit, not an editing bug: the left column is a group of text boxes, Write draws only the first text box of a group and lays floats out as boxes (decision "the layout library and the viewer (M4.2)", known limits). |
