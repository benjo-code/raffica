# Raffica Write: Word check of an edited, saved file

The owner's checklist for the one thing CI cannot answer for milestone 4.3a: whether **Microsoft Word accepts a file that Write edited and saved**, without a repair prompt and with the text and the formatting it should have. CI proves that the saved file is read again by Write and lays out like the edited model (`RealEditorOracleTests`, `SaveFlowTests.AnEditedThesisSavedThroughTheFlowLaysOutAsTheEditedModelDoes`), and that every part Write did not touch is byte-identical. It does not run Word. **Nothing in this file has been checked yet**: the result table is empty on purpose, and the roadmap keeps its open item until the table is filled in.

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
|  |  |  |  |  |  |  |  |  |
