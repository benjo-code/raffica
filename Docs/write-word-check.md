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
| 2026-11-22 | Word 16.0 (build 16.0.20625, Microsoft 365, de), Windows 11, read through Word automation (`ListString` of every list paragraph, not a person looking) | lists.docx | compared with `ReferenceNumbering` and the drawn labels | none (read-only, repair off) | yes: 12 of 12 list paragraphs equal (bullets as "is a bullet"), 0 differences | Restart at the fourth item and the multilevel 1. / 1.1. / 1.1.1. / 2. read as in Write. |
| 2026-11-22 | as above | numbered-headings.docx | as above | none | yes: 5 of 5 equal, Heading 1 numbered 1. to 5. in the order after the move and the promote | No Write bug found. |
| 2026-11-22 | as above | numbered-thesis-chapter-move.docx | as above | none | yes: 540 of 540 equal (300 outline-numbered, 120 simple-numbered, 120 bullets), chapter order after the move consecutive in Word and in Write | A person's look at the rendered page is still open. |

## Sections, page numbers and page setup (milestone 4.4b)

The question for what 4.4b writes: **does Word open a file with sections, restarted and formatted page numbers, different first pages, left and right pages, landscape sections and columns without a repair prompt, and does it show the number on every page that Write draws?** CI proves that after every section command (and its undo and redo) the layout equals a full layout and the numbers drawn on the pages equal an independent count of the model (`SectionOracleTests`, `ReferencePageNumbers`, `DrawnPageNumbers`), also for the saved file read again. It does not run Word. The rules of that count that were not settled against Word are answered below (see "Rules not yet seen in Word, now seen" in `Docs/DECISIONS.md`, "Raffica Write: sections, page numbers and page setup (M4.4b)"); the last block of files below asks Word about them.

The files are made by a test through the editor and `DocxFile.SaveAsync`; each gets a `<name>.numbers.json` with the number every page of Write's layout draws (nothing is written without the folder variable):

```
$env:RAFFICA_WORD_CHECK_DIR = '<folder>'
dotnet test tests/Raffica.Write.Tests -c Release --filter-class "*WordCheckSectionFilesTests" --filter-class "*WordCheckFilesTests"
pwsh tools/word-open-check.ps1 <folder>\page-three-one.docx <folder>\appendix-a1.docx ...        # repair prompt, one line per file
pwsh tools/word-page-numbers.ps1 <folder>\page-three-one.docx                                    # Word's number of every page against numbers.json (one file per call)
```

`tools/word-open-check.ps1` takes explicit file names (it does not expand a wildcard under `pwsh -File`; pass `(Get-ChildItem <folder> -Filter *.docx).FullName` from PowerShell). `tools/word-page-numbers.ps1` reads, for every physical page, the first paragraph that starts on it and asks Word for `Range.Information(wdActiveEndAdjustedPageNumber)` (the number Word prints, as a value whatever its format); a page on which no paragraph starts (a blank page, or one inside a long paragraph) is skipped, not compared; exit 1 on a difference. It does not read the footer text or the format of a number.

| File | What it holds |
| --- | --- |
| `page-three-one.docx` | A blank document (A4), 100 paragraphs (6 pages): numbering from page 3, start 1, Arabic. |
| `restart-page-four.docx` | The same with the numbering from page 4 (the restart on an even physical page: the forum report says Word then makes an odd-page break). |
| `appendix-a1.docx` | The generated thesis with the last chapter numbered from "A-1" (start 1, prefix "A-"). |
| `roman-front-matter.docx` | Pages 1 and 2 in lower-case Roman, then Arabic from 1 on page 3. |
| `first-page.docx` | Numbered pages with "different first page" switched on afterwards (page 1 has an empty own footer). |
| `left-right.docx` | Numbered pages with left and right pages switched on. |
| `landscape-section.docx` | Landscape from page 2, portrait again from a later page (three sections). |
| `two-columns.docx` | Two columns with a line from page 2 on (a continuous section). |
| `numbered-thesis-chapter-move.docx` | As in 4.4a, but chapter four starts a section of its own first, so the moved chapter crosses a section break. |
| `continuous-start-same-paper.docx` | A continuous section that starts in the middle of page 2 with `w:start` 5 (same paper). |
| `continuous-start-other-paper.docx` | The same, landscape (other paper). |
| `continuous-start-page-top.docx` | A continuous section whose first block is at the top of page 3, `w:start` 5. |
| `even-page-start-two.docx` | An even-page section at the top of page 3 with `w:start` 2 (the right parity). |
| `odd-page-start-one.docx` | An odd-page section at the top of page 3 with `w:start` 1 (the right parity). |
| `even-page-start-one.docx` | An even-page section at the top of page 2 with `w:start` 1. |
| `odd-page-start-two.docx` | An odd-page section at the top of page 3 with `w:start` 2. |
| `blank-page-roman.docx` | An even-page section at the top of page 3 (a blank page is needed) with `w:fmt` lowerRoman. |
| `trailing-page-break-split.docx` | A paragraph that ends in a `w:br` page break, then a split (numbering from the next paragraph). |

| Date | Word version and system | File | Repair prompt | Numbers as in Write (pages compared / differences) | Notes and findings |
| --- | --- | --- | --- | --- | --- |
| 2026-11-23 | Word 16.0 (build 16.0.20625.20108, Microsoft 365), Windows 11, read through Word automation (not a person looking), read-only, repair off | the first 15 files of the table (all but `continuous-start-page-top`, `even-page-start-two` and `odd-page-start-one`, which were added after the findings below) and the 4.4a files `lists.docx` and `numbered-headings.docx`, 17 files in all | none: all 17 opened (`word-open-check.ps1` exit 0); the three added files open too (checked by the page-number script, which opens them) | see the rows below | The thesis files have 320 and 321 pages in Word and 309 and 310 in Write: the fonts or the compatibility differ (the 4.4a row already recorded 321 and a page count that was not explained), so a comparison by physical page index is only meaningful before the first difference in pagination. |
| 2026-11-23 | as above | page-three-one.docx | none | 6 / 0 | Numbers 1 2 1 2 3 4 as in Write. The one-command "1 on page 3" opens as intended. |
| 2026-11-23 | as above | restart-page-four.docx | none | 6 / 0 | 1 2 3 1 2 3 as in Write and no extra page: Word did not turn the restart into an odd-page break in a file Write saved (the oracle's rule 4 holds). |
| 2026-11-23 | as above | appendix-a1.docx | none | not comparable by page index (320 against 309 pages) | Read by section instead: Word has 4 sections; section 3 (the appendix) starts on physical page 313 with restart on, start 1 and a footer "A-" followed by the page field; section 4 starts on page 317 with its own restart. Write has the appendix on page 302 and the following section on page 306, so both are 4 pages long in both. The cached result of the field reads `0` in the footer text (Write writes the cached value; Word computes the number when it lays out). A person has not looked at "A-1" on the rendered page. |
| 2026-11-23 | as above | roman-front-matter.docx | none | 6 / 0 | 1 2 1 2 3 4; the format (Roman) is not readable by the script. |
| 2026-11-23 | as above | first-page.docx | none | 4 / 0 | Numbers 1 2 3 4 as in Write; whether page 1 shows no number is not read by the script. |
| 2026-11-23 | as above | left-right.docx | none | 4 / 0 | As in Write. |
| 2026-11-23 | as above | landscape-section.docx | none | 6 / 0 | As in Write. |
| 2026-11-23 | as above | two-columns.docx | none | 5 / 0 | As in Write (5 pages in both). |
| 2026-11-23 | as above | numbered-thesis-chapter-move.docx | none | not comparable by page index (321 against 310 pages) | Opens without repair with the section break inside the moved chapter. A person's look is open. |
| 2026-11-23 | as above | continuous-start-other-paper.docx | none | 4 / 0 | 1 2 5 6: a continuous section on other paper restarts on a new page, as the oracle says. |
| 2026-11-23 | as above | continuous-start-same-paper.docx | none | first run 4 / **2 differences**, after the fix 4 / 0 | **Finding, fixed.** Word 1 2 6 7, Write (then) 1 2 3 4: the oracle said a continuous section on the same paper never restarts. Word reads the first paragraph of the section on page 2 as page 2 and page 3 as 6: a start of a continuous section that begins in the middle of a page leaves that page's number and the next page is start + 1. The layout (`PageFiller`) and `ReferencePageNumbers` follow Word now; re-read in Word after the fix: Write 1 2 6 7, 0 differences. |
| 2026-11-23 | as above | continuous-start-page-top.docx | none | 4 / 0 | Added after the finding above. A continuous section whose first block is at the top of page 3 begins that page with its start: Word 1 2 5 6 (the page-top case), Write (before and after the fix) 1 2 3 4 then 1 2 5 6; after the fix 0 differences. |
| 2026-11-23 | as above | even-page-start-one.docx | none | 6 / **6 differences and a different page count** | **Finding, not fixed (backlog).** Even-page section at the top of page 2 with start 1 (wrong parity). Word has 6 pages numbered 1 2 3 4 5 6 (the start is ignored, no blank page, the count goes on); Write has 7 pages numbered 1 1 2 3 4 5 6 (a blank page, then a restart). |
| 2026-11-23 | as above | odd-page-start-two.docx | none | 6 / **5 differences and a different page count** | **Finding, not fixed (backlog).** Odd-page section at the top of page 3 with start 2 (wrong parity): Word 1 2 3 4 5 6 on 6 pages, Write 1 2 2 3 4 5 6 on 7. The same cause. |
| 2026-11-23 | as above | even-page-start-two.docx | none | 6 / 0 | Experiment for the rule. Even-page section at the top of page 3 with start 2 (right parity, an odd physical page): Word 1 2 2 3 4 5 on 6 pages, as Write. Word applies a start of the right parity. |
| 2026-11-23 | as above | odd-page-start-one.docx | none | 6 / 0 | Odd-page section at the top of page 3 with start 1 (right parity): Word 1 2 1 2 3 4, as Write. |
| 2026-11-23 | as above | blank-page-roman.docx | none | 6 / 0 (page 4 skipped) | Word has the blank page 4 as Write does (7 pages in both) and the other numbers agree; **the number format of the blank page is open** (a blank page has no paragraph for the script to read). |
| 2026-11-23 | as above | trailing-page-break-split.docx | none | 2 / **2 differences and a different page count** | **Confirms the known limit.** Word has 2 pages, Write 3: a split after a paragraph that ends in a `w:br` page break shows an extra blank page in Write and none in Word. |

**The rule the four files of the even-page and odd-page starts suggest (observation, not a layout change):** Word applies a `w:start` of the parity the section type needs (even for an even-page section, odd for an odd-page one) and ignores a start of the other parity, continuing the count and adding no blank page; whether a blank page is added then depends on the parity of the number the count gives (`blank-page-roman.docx`: the count gives 3 for an even-page section, a blank page, 7 pages in both). Four files make a rule only for the physical pages tested (the section at the top of page 2 or 3 of a 6-page document); other positions and a start of 0 are untested. Write adds the blank page for a wrong-parity start and restarts; the oracle skips those pages (`SectionSteps.CheckNumbers`).

What is not read by the scripts and stays open for a person: the look of the rendered page (the chip is Write's, Word shows no marker), whether the first page shows no number, the Roman and letter formats, the prefix "A-" on the rendered page, whether "Review > Compare" of the moved thesis shows a moved chapter and nothing else, and the pane of Word's own section list.

## Tables, pictures and notes (milestone 4.5a)

The question for what 4.5a writes: **does Word open a file with tables (merged cells, borders, widths, a repeated header row), pictures (inline, floating, in a table cell, pasted, resized, wrapped, moved, deleted, undone and redone) and footnotes and endnotes without a repair prompt, and does it show the same tables, notes and pictures that Write holds?** CI proves that after every table, picture and note command and its undo and redo the layout equals a full layout (also the object map), that the recovery copy equals a full write and that the saved file read again agrees (`ObjectOracleTests`, `SnapshotCacheOracleTests`, `MediaPartTests`). It does not run Word.

The files are made by a test through the editor (the calls the app makes) and `DocxFile.SaveAsync`; each gets a `<name>.facts.txt` with what the model holds (a `summary` line with the number of tables, footnotes, endnotes, inline and floating pictures, one `picture` line each with kind, size in points and description, and `rows=` lines with the rows, cells and columns of every table). A picture file also asserts that the package holds one media part for each picture the document still shows (a deleted or undone picture leaves none). Nothing is written without the folder variable:

```
$env:RAFFICA_WORD_CHECK_DIR = '<folder>'
dotnet test tests/Raffica.Write.Tests -c Release --filter-class "*WordCheckObjectFilesTests"
pwsh tools/word-open-check.ps1 (Get-ChildItem <folder> -Filter *.docx).FullName      # repair prompt, one line per file
pwsh tools/word-object-counts.ps1 <folder>\picture-wrap.docx                          # Word's counts against facts.txt (one file per call, exit = number of differences)
```

`tools/word-object-counts.ps1` opens the file read-only through Word automation with repair off and compares `Document.Tables`, `Footnotes`, `Endnotes`, `InlineShapes` and `Shapes`, per picture the wrap kind (`WrapFormat.Type`: square, top and bottom, in front, behind; inline for an inline shape), the size in points (within 0.6) and `AlternativeText`, and per table the rows, cells and columns (`Range.Cells.Count`; the column count is skipped where Word cannot read it for a vertical merge). It opens the document window visible (the application stays hidden): with a hidden window Word returns an empty `AlternativeText` for every picture.

| File | What it holds |
| --- | --- |
| `footnote.docx`, `endnote.docx` | A blank document (A4) with one sentence and a footnote or an endnote typed into the note (the first note makes the notes part). |
| `notes-in-table.docx` | A two-cell table with a footnote in one cell and an endnote in the other, and a footnote in the paragraph behind it. |
| `table-basic.docx` | A 4 by 3 table with text in every cell and a paragraph kept behind it. |
| `table-merged.docx` | A 5 by 6 table with a merge across, a merge down and a merge both ways, a row inserted inside the vertical merge, a column inside the span, rows and a column deleted through merges and a merged cell split. |
| `table-borders.docx` | Borders (all, box, outside, none, double, dashed, dotted, sides off), shading from the fixed colour list, a header row and row heights (at least and exactly). |
| `table-widths.docx` | A percentage table and an autofit table: column widths written from the drawn widths, a row moved up and down, the table width set, the columns distributed (the first write converts both to fixed layout). |
| `table-header-multipage.docx` | A 120-row table with a repeated header row (the layout shows the heading on page 2). |
| `table-in-content-control.docx` | A table made inside a block content control. |
| `table-after-notes.docx` | A table behind a paragraph with a footnote, with a footnote and an endnote inside its cells. |
| `picture-inline.docx` | Inline pictures inside a sentence, at its end, at the start of a paragraph and a tall narrow one, with and without a description. |
| `picture-in-cell.docx` | Pictures in a 3 by 3 table: one too wide for its cell (fitted to the cell), a small one behind the text of a cell and one before it. |
| `picture-resize.docx` | A picture resized to half and to one and a half times (ratio kept), a cropped picture (an `a:srcRect` of 10 percent each side) resized and reset to its original size, and the properties card's one step (size, description with `<`, `&`, quotes and umlauts, and wrap square). |
| `picture-alt-text.docx` | A description set afterwards, changed (with special characters), removed, and one of 500 characters. |
| `picture-wrap.docx` | Square, top and bottom, behind text and in front of text; inline to square and back to inline; square to top and bottom to behind. |
| `picture-move.docx` | Floating pictures moved to a later and an earlier paragraph with offsets, nudged by arrow keys, and moved inside their own paragraph. |
| `picture-paste.docx` | A picture pasted at a caret, over a selected word and over selected text in a table cell. |
| `picture-deleted.docx` | An inline and a floating picture deleted, one of each kept (only two media parts). |
| `picture-undo-redo.docx` | An insert undone and redone, a resize and its insert undone and redone, an insert undone and replaced by another picture (three pictures, three media parts). |
| `picture-delete-undo.docx` | A deletion undone, and a deletion undone and redone (one picture, one media part). |
| `picture-table-notes.docx` | A picture, a table with a picture in a cell and an endnote in another, a footnote and a floating picture behind the table, together. |

| Date | Word version and system | File | Repair prompt | Counts as in Write (differences) | Notes and findings |
| --- | --- | --- | --- | --- | --- |
| 2026-11-25 | Word 16.0 (build 16.0.20625, Microsoft 365), Windows 11, read through Word automation (not a person looking), read-only, repair off | all 21 files above | none: all 21 opened (`word-open-check.ps1` exit 0) | 0 differences in every file (`word-object-counts.ps1` exit 0, 188 lines compared, 0 `DIFF`) | The 7 table files and 3 note files were also opened by script on 2026-11-24 and 2026-11-25 while the tables were built (no repair); this run is the first with the counts. |
| 2026-11-25 | as above | footnote.docx, endnote.docx, notes-in-table.docx | none | 0 (footnotes 1, endnotes 1; 2 footnotes and 1 endnote and a 1 by 2 table) | The notes part, the separators and the settings entries Write makes are accepted. |
| 2026-11-25 | as above | table-basic.docx, table-merged.docx, table-borders.docx, table-widths.docx, table-header-multipage.docx, table-in-content-control.docx, table-after-notes.docx | none | 0 (rows, cells and columns of every table: 3/12/4, 5/21/5 with a merged grid, 5/20/4, 3/9/3 and 4/12/3, 120/360/3, 3/9/3, 3/9/3) | Word's cell count equals the model's with a merged cell counted once. A person has not looked at the borders, shading, widths or the repeated header on the rendered pages. |
| 2026-11-25 | as above | picture-inline.docx, picture-in-cell.docx | none | 0 (4 inline pictures, and 3 in a table with the large one at 140.4 x 105.3 pt, fitted to the cell) | Sizes equal within 0.6 pt, descriptions equal. |
| 2026-11-25 | as above | picture-resize.docx, picture-alt-text.docx | none | 0 (3 inline and 1 square; 4 inline with a 500-character and a special-character description) | The cropped picture, resized and reset, has the size Write computes; the crop is kept. A description with `<`, `&` and quotes reads back as written. |
| 2026-11-25 | as above | picture-wrap.docx, picture-move.docx | none | 0 (1 inline and 5 floating: square, top and bottom, behind, in front, behind; 3 floating moved: in front, square and top and bottom) | Word reports the wrap kind of every picture as Write wrote it (`WrapFormat.Type`). **Not read by the script: the position of a floating picture on the page.** |
| 2026-11-25 | as above | picture-paste.docx, picture-deleted.docx, picture-undo-redo.docx, picture-delete-undo.docx | none | 0 (3 inline in a table; 1 inline and 1 square; 3 inline; 1 inline) | No media part for a deleted or undone picture, and Word shows exactly the pictures the model holds after undo and redo. |
| 2026-11-25 | as above | picture-table-notes.docx | none | 0 (1 table of 3 by 2 cells, 1 footnote, 1 endnote, 2 inline and 1 floating picture) | A picture, a table and both kinds of note in one document. |

What is not read by the scripts and stays open for a person: the look of the rendered pages (a table's borders, shading, widths and repeated header; a picture's place, a floating picture's position and the text around it; the note numbers at the page bottom and at the end), whether "Review > Compare" of a file against its saved copy shows only what was made, a picture pasted from a real clipboard, a picture in a header or footer (not made by Write), and whether Word's picture "Format" pane shows the same alt text.

## Fields, captions and tables of contents (milestone 4.5b)

The question for what 4.5b writes: **does Word open a file with bookmarks, captions, cross-references and tables of contents without a repair prompt, and does Word's own field update give the same results as the ones Write stored, apart from page numbers?** CI proves that after every field command and its undo and redo the stored results equal an independent count made from the model, the layout equals a full layout and the saved file read again agrees (`FieldOracleTests`, `SnapshotCacheOracleTests`, `TocStructuralRefreshTests`). It does not run Word.

The files are made by a test through the editor (the calls the app makes), the app's refresh over the layout's labels and pages, and `DocxFile.SaveAsync`. The files named `probe-*` are written by hand and ask Word how it computes a case. A file's `<name>.skip.json` lists the instructions where a difference is expected (a field Write does not update, or a recorded difference). Nothing is written without the folder variable:

```
$env:RAFFICA_WORD_CHECK_DIR = '<folder>'
dotnet test tests/Raffica.Write.Tests -c Release --filter-class "*WordCheckFieldFilesTests"
pwsh tools/word-open-check.ps1 (Get-ChildItem <folder> -Filter *.docx).FullName
pwsh tools/word-field-results.ps1 (Get-ChildItem <folder> -Filter *.docx -Exclude probe-control.docx).FullName
pwsh tools/word-field-results.ps1 <folder>\probe-control.docx        # must exit 1 (see below)
```

`tools/word-field-results.ps1` opens a copy of each file visibly through Word automation with repair off, reads the result of every field of the body as stored, updates the tables of contents and of figures and then all fields twice, and compares. A difference is classed as **page** (a `PAGEREF` result, or the number after the last tab of a list line: Word's pagination may differ), **expected** (listed in the skip file) or **DIFFERENT**; the exit code is 1 for a DIFFERENT result, a different number of fields or a file it could not read. **Negative control:** `probe-control.docx` stores a wrong result for every supported field kind and the script must report each (5 DIFFERENT and 1 page difference); this shows that the "stored" values are read before Word updates anything. A field that Word updates by itself while it opens a file (`DATE` in `fields-mixed.docx`) shows no difference for that reason.

| File | What it holds |
| --- | --- |
| `fields-captions.docx` | Figure, table and equation captions with and without chapter numbers, a table caption above a table, captions inserted before others (renumbered). |
| `fields-cross-references.docx` | A cross-reference of every kind to every target: heading (number, text, page), caption (label and number, number, text, page), footnote and endnote (number, page), bookmark (text, page). |
| `fields-contents.docx` | A table of contents of the levels 1 to 3, a list of figures and a list of tables (hyphen leader) over three numbered chapters. |
| `fields-contents-options.docx` | Contents without page numbers, with a plain tab and no right alignment, and of level 2 alone with an underscore leader. |
| `fields-stale-contents.docx` | A table of contents after a heading was added, one renamed, one removed and the pages moved. |
| `fields-bookmarks.docx` | A bookmark, references to its text and page after the text changed, chapter captions. |
| `fields-thesis.docx` | The corpus thesis with one more heading, its table of contents brought current. |
| `fields-mixed.docx` | Supported fields beside unsupported ones (`DATE`, `SEQ \h`, `PAGEREF \p`, `REF \w`). |
| `fields-removed-contents.docx`, `fields-removed-every-list.docx` | Remove table of contents: the table of contents removed while the list of figures stays, then the list of figures too; a cross-reference to a heading stays. |
| `probe-removed-contents*.docx` | Removing hand-written lists: a `_Toc` bookmark a `PAGEREF` outside still uses, another bookmark inside an entry with a `REF` to it, a list alone in a content control and in a table cell, a list after a table at the end of the body, of a cell and of a content control, and a list between two tables. |
| `fields-deleted-bookmarks.docx` | Delete bookmark: a bookmark in a paragraph (with references to its text and page), one over two paragraphs, a point and one in a table cell; one bookmark stays. |
| `probe-deleted-bookmarks.docx` | Deleting bookmarks as files hold them: an end marker between two paragraphs, markers inside a link and a content control. |
| `probe-chapter-numbers-*.docx` | `SEQ` restarts with `\s`, `\r`, `\c`, `\n`, formats, `STYLEREF 1 \s` and `REF \n`, `\r` over heading labels with a trailing period, a word and brackets. |
| `probe-label-*.docx` | `STYLEREF 1 \s` over level texts and number formats (`Chapter %1:` in capital Roman, `Appendix %1`, `Part 2.%1:`, `A-%1-`, `[ %1 ] .`, `%1.0`, and headings on a second list level). |
| `probe-seq-restarts.docx`, `probe-seq-odd.docx` | When a number restarts, what counts, hidden and unsupported switches, bookmark arguments, nested fields, headings in cells, deleted and inserted headings. |
| `probe-seq-tracked.docx`, `probe-seq-moved.docx` | `SEQ` in tracked insertions, deletions and moves, and the list of figures over them. |
| `probe-self-reference.docx` | A `REF` to a bookmark that holds the reference itself. |
| `probe-picture-heading.docx` | A heading that is a picture and no text, in the thesis. |
| `probe-foreign-bookmarks.docx` | Bookmarks Write does not read (between blocks, in a table row) and bookmarks of a footnote and a footer, with the highest ids and generated names, followed by Write's own bookmark, table of contents and cross-reference: every id and name in the saved file is used once. |
| `probe-control.docx` | The negative control. |

| Date | Word version and system | Files | Repair prompt | Field results | Notes |
| --- | --- | --- | --- | --- | --- |
| 2026-11-27 | Word 16.0 (build 16.0.20625.20142, Microsoft 365, German), Windows 11, read through Word automation (not a person looking), repair off | the 27 files that existed then (no removal cases) | none: all 27 opened (`word-open-check.ps1` exit 0) | `word-field-results.ps1` on 26 files: exit 0, no DIFFERENT result; `probe-control.docx`: exit 1 as it must | After the engine was changed to follow the findings below (the first run of this day found them). |
| 2026-11-27 | as above | fields-captions, fields-bookmarks, fields-contents-options, fields-stale-contents, fields-thesis, all `probe-chapter-numbers-*` and `probe-label-*`, probe-seq-tracked | none | every result equal (probe-seq-tracked: the fields inside the changes are listed as expected and Word left the deleted one unchanged) | |
| 2026-11-27 | as above | fields-contents.docx | none | 3 page differences (two headings and a figure: Write puts them on page 7 or 8, Word one page earlier), everything else equal | |
| 2026-11-27 | as above | fields-cross-references.docx | none | 1 page difference (`PAGEREF`, Write 3, Word 2), everything else equal | |
| 2026-11-27 | as above | fields-mixed.docx | none | 3 expected: `SEQ \h` shows nothing, `PAGEREF \p` shows "above", `REF \w` shows "0"; Write keeps the stored results and marks the fields | |
| 2026-11-27 | as above | 36 files: the 27 and the first versions of the 9 for Remove table of contents and Delete bookmark | `probe-removed-contents-cell-ends-in-table.docx`: Word did not open it ("Die Datei ist möglicherweise beschädigt.") | not run on the refused file | Word also read the two tables of `probe-removed-contents-between-tables.docx` as one. Cause: Remove table of contents left a table last in the cell, or two tables together; fixed, an empty paragraph stays there. |
| 2026-11-27 | as above | all 36 files, made again with the fix | none: all 36 opened (`word-open-check.ps1` exit 0) | `word-field-results.ps1` on 35 files: exit 0, no DIFFERENT result; `probe-control.docx`: exit 1 as it must. Of the new files only `probe-removed-contents.docx` has an expected difference: the `REF` to the bookmark that stood in the removed entry (Word: "Fehler! Verweisquelle konnte nicht gefunden werden."; Write keeps the stored result). | Word reads two tables in the between-tables file (Word automation). The references of `fields-deleted-bookmarks.docx` still work: they point at Write's own hidden bookmarks. |
| 2026-11-27 | as above | all 37 files, made again after the fixes of the whole-branch review (`probe-foreign-bookmarks.docx` is new; a table of contents that would end its body, cell or control now leaves a paragraph after it) | none: all 37 opened (`word-open-check.ps1` exit 0) | `word-field-results.ps1` on 36 files: exit 0, no DIFFERENT result; the page and expected differences are those of the rows above (3 pages in `fields-contents`, 1 in `fields-cross-references`; 2 fields of `probe-foreign-bookmarks.docx` equal); `probe-control.docx`: exit 1 as it must (5 DIFFERENT, 1 page) | Word automation does not say which bookmarks it kept; that the ids and names are unique is checked on the saved XML by the test. |

### What Word does (found on 2026-11-27; Write follows unless said otherwise)

| Case | Word 16 | Write |
| --- | --- | --- |
| `STYLEREF 1 \s` | The heading's number from the first to the last placeholder of its level text, without the text around it: `Chapter I:` gives `I`, `(1)` gives `1`, `Part 2.1:` gives `1`, `01.0` gives `01`, `Sec 1 - 2)` gives `1 - 2`. | Follows (it showed the whole label without a trailing period before this check). |
| `REF \n` and `\r` to a numbered heading | The whole label without trailing periods (`Chapter 1:` stays, `1.1.` gives `1.1`). | Follows (as before). |
| `SEQ \s n` | Restarts only when a heading of level n or higher (a lower number) stands between the field and the previous field of the same label; a heading of a deeper level does not restart. `\r` or any other field of the label in between uses up the heading. | Follows (before: any heading of exactly level n since the last field with `\s`). |
| `SEQ \c` | Repeats the current number; at a new start it shows 0 and the next field shows 1. With `\n \c` Word counts on. | Follows. |
| `SEQ \r abc`, `\s 0`, `\s abc` | `\r` with no number sets 0; an unreadable `\s` restarts nothing. | Follows. |
| Which `SEQ` fields count | Every one of the label that is not deleted or moved away: hidden ones (`\h`), other formats (`\* CardText`, `\* Ordinal`, `\#`), fields nested in another field, fields in a tracked insertion or in moved-in text. One that names a bookmark (`SEQ T Mark`) or has an unknown switch (`\q`, Word shows an error) does not count. | Follows for counting; fields Write does not update keep their stored result (the results of hidden, other-format, nested and tracked fields differ from Word's, the numbers after them agree). |
| A list of figures | Lists captions in tracked insertions and moved-in text; not deleted ones, not hidden ones (`\h` without a format). | Follows. |
| A heading that is only a picture | Listed in the table of contents with a copy of the picture. | **Differs:** a heading with no text is left out (recorded, `probe-picture-heading.docx`). |
| A `REF` to a bookmark that holds the reference | "Error! Not a valid bookmark self-reference." (German: "Fehler! Ungültiger Eigenverweis auf Textmarke."). | **Differs:** the stored result stays and the field counts as not updated. |
| A heading in a deleted or inserted paragraph, a heading in a table cell, an outline level without a heading style | The first three restart `\s`; the outline level does not. | Follows. |
| Fields inside a tracked change | Updated by Word. | **Differs:** Write keeps their stored results (spec limit). |
| A table last in a table cell | Word does not open the file. | Follows: Remove table of contents leaves an empty paragraph (before the second check it left the table last). |
| Two tables with nothing between them | Read as one table. | Follows: Remove table of contents leaves an empty paragraph between them. |
| A table last in the body or in a content control | Opens, with a paragraph after the table. | Remove table of contents leaves an empty paragraph there too. |

What is not read by the scripts and stays open for a person: the look of the rendered pages (a table of contents' leaders and indents, the links), clicking a link of a list or a reference, Word's own "Update field" on a page someone looks at, and a thesis-sized comparison of page numbers (the pagination differs, see the thesis rows of 4.4b).

## Checking a file from a script

On a Windows machine with Word, `pwsh tools/word-open-check.ps1 <files>` opens each file through Word's COM automation (read-only, repair off, on a temporary copy) and prints whether Word accepted it; the exit code is the number of files Word refused. It answers the repair-prompt question for any number of files at once and is how the cause of the first finding was bisected (`Docs/DECISIONS.md`, "one paragraph in a text, combo box, drop-down list or date control"). It does not look at the result, so the "looks as in Write" columns stay the owner's. `pwsh tools/word-object-counts.ps1 <file>` (4.5a) goes one step further for the files that have a `.facts.txt`: it compares the tables, notes and pictures Word reports with the model's (see the section above); `tools/word-page-numbers.ps1` does it for page numbers (4.4b).

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
