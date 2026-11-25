# Raffica Write 4.5a: Tables, Pictures, Footnotes and Endnotes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A writer can insert and edit tables, pictures (from a file or pasted from the clipboard), footnotes and endnotes in Write, with the pointer (border, row, cell-range, picture-handle and floating-picture gestures) or with cards, keys and commands, and Word opens every file Write saves without repair.

**Architecture:** The layout publishes an immutable `ObjectMap` per page (picture boxes with identity, table grids with borders, rows and cells); the session holds an `ObjectSelection` beside the text range; the view draws handles and drag outlines over the page and a drag edits once, on release. Every operation is one history step through the lease and `EditSession.Apply`; a change that adds notes, media or styles returns an `ObjectChange` (like `Sections` and `Style`) that the session routes and the history can undo. The package layer learns two new things: a notes part made on first use and media parts for new pictures.

**Tech Stack:** .NET 10 Native AOT, Avalonia 12, Open XML SDK 3.x (package layer only), SkiaSharp (decode and PNG encode), xunit v3 on Microsoft Testing Platform.

**Spec:** `Docs/superpowers/specs/2026-11-24-raffica-write-4-5a-tables-pictures-and-notes-design.md` (approved 2026-11-24, with "Rules settled after the review" and clipboard paste of pictures). Subproject spec: `Docs/superpowers/specs/2026-11-13-raffica-write-design.md` (section 6). Branch: `feature/m45-long-document-tools`. Read the spec's goals, "Constraints that bind" and "Rules settled after the review" before any task; they are the values this plan cites.

## Global Constraints

- .NET 10 Native AOT, English source, German localization in the "Sie" form; every new command has its own Lucide icon, a reason in every disabled state (`WriteCommandRules`), strings in both `.resx` files (LF), a line in `WriteMacMenu`; no warning suppressed; `dotnet format Raffica.slnx --verify-no-changes` clean; **no key uses Ctrl plus Alt** (AltGr is text), so notes have no key; cards go through `CardFooter` and are listed in `CardFooterTests.Entries` **and** the expected list of `CloseGuardTests.CardsAreBuiltByCardFooter`.
- **The model is the vocabulary:** typed accessors and views in `Raffica.Write.Model`, no second storage; unknown content stays as `UnknownContent`; children placed by `SchemaOrder`; every table operation re-derives the position ints and replaces the table block as one `BlockChange`.
- Every model change goes through the lease and `EditSession.Apply`, one history step per command or gesture, every raise followed by `result.Attribute(document)`; a new raise site gets a case in `SnapshotCacheOracleTests` and in the oracle steps. A prefix document (`IsPrefix`) refuses every command of this milestone; untitled tabs and recovered copies accept them. During a save every command, gesture and key is queued and replayed (4.3a save queue); a press that would start a gesture during a save only selects.
- **Incremental equals full** (`LayoutOracle.AssertEqualToFull`, exact, including the new `ObjectMap`); a change of styles goes through `DocumentLayout.Restyle`, a change of notes through the note-aware `ApplyEdit` path, never a new fast path without a corpus oracle case first.
- The object map is immutable per `LaidOutPage`, read on the UI thread with `TryGetPage`; handles, guides and the drag outline are drawn by the view and never enter a `LaidOutPage` or print; `Render` touches no visual; a drag changes the model once, on release; Esc and `PointerCaptureLost` end a drag with no change.
- **Where objects go (spec rule 1):** a table in the body and in multi-paragraph content controls only; a picture and a note reference in the body and table cells only; none of the three in headers, footers, notes, comments or text, combo box, drop-down and date content controls; refusals show a reason.
- **Pictures:** PNG, JPEG, GIF, BMP, TIFF; at most 50 MiB and 32,000,000 decoded pixels per picture; header probed before decode (`ImageHeader`); bytes untrusted; no bytes, file name or alt text in a log line or exception message; `ImageCache.Clear` and a prefetch after an insert or paste; only `pic:pic` drawings are objects; a rotated picture is not resizable; a cropped one resizes with its crop kept. Floating defaults are Word's (spec rule 3); nudge 1 pt, Shift 10 pt, one history step per second of nudges.
- **Tables:** a new table is full text width, equal columns, `TableGrid` style (single 0.5 pt borders), one empty paragraph per cell, a paragraph kept after it at the end of the body; at most 63 columns and 500 rows; merge concatenates non-empty paragraphs; split only restores a merged cell; delete row and column follow spec rule 4 without a confirmation; a border drag moves the border between two columns, the right table edge changes the last column and the table width; the first write converts autofit or percentage to fixed layout (spec rule 5); nested tables are refused by every command.
- **Notes:** ids allocated and not reused while the reference lives; the first note makes the notes part with separator entries and the settings references; the document's own `w:footnotePr` and `w:endnotePr` are never rewritten otherwise; deleting the reference deletes the note in the same step; styles `TableGrid`, `FootnoteReference`, `EndnoteReference` and `EndnoteText` are created on first use as companions of the step (the blank document's 24 styles do not change).
- **Paste precedence:** Write's own fragment, then an image, then text (a clipboard with an image and text pastes the text); a bitmap becomes PNG through Skia at 96 DPI capped to the text width; no card, empty alt text; one history step.
- Start cost: nothing of tables, pictures, notes, the Insert and Table tabs' pickers, the grid picker or the object map exists before first use (`StartupCreatesNoObjectWork`); `WriteRibbon.HomeFullWidthAtLeast` is unchanged (`RibbonStartupTests`); `--measure-startup` is compared with the commit before 4.5a. Typing stays at most 16 ms (judged `typing` metric, panes open); a pointer move during a drag costs at most 16 ms p95 of CPU.
- Out of scope: table styles gallery, text to table, sort, formulas, nested table creation, tables in headers, footers and notes, captions, cropping, rotation, effects, shapes and text boxes, drag and drop of image files, an image file pasted from the file manager, linked pictures, notes options dialog, footnote-to-endnote conversion, Ctrl plus Alt keys, the owner gates of earlier milestones.

## Review Focus

1. A Word-made table with merged cells in both directions, `gridSpan`, `vMerge`, autofit and percentage widths: every row and column command and a border drag keep the grid equal to the cells, lose no text, and the saved file reads back the same (Tasks 6, 7, 9; `EveryRowAndColumnCommandKeepsTheGridOnTheCorpusTables`, `AFirstWidthEditConvertsAnAutofitTableToFixed`).
2. A document with no notes part, one with only separator notes, and one whose notes have ids 5 and 9: the first insert makes a valid part, the second takes a fresh id, undo and redo of the first insert round-trip, delete of the reference removes the note, Word opens all three results (Tasks 1, 2).
3. A cropped, a rotated, an anchored and an inline picture, and a picture the layout cannot read: selection, resize, move and delete never change anything but what they promise, and an unreadable `w:drawing` stays a placeholder that cannot be selected as a picture (Tasks 4, 11, 12).
4. Hostile and oversized pictures: a 60 MiB file, a 10000 by 10000 PNG, a truncated or lying header, a zero-byte clipboard image, a 1-pixel image: refused or inserted with the reason, no exception message holds bytes, name or alt text (Tasks 11, 13).
5. A gesture interrupted: Esc, capture lost, a pane hide, a save starting, a window closing, a document replaced by undo mid-drag: the model is unchanged, the selection is recoverable by the next click, no handle stays drawn (Tasks 5, 9, 12).

---

## File structure

| File | Responsibility |
|---|---|
| `Docs/roadmap.json`, `Docs/ROADMAP.md` | plan link, status `in-progress`, ticks and proof |
| `src/Raffica.Write.Model/MediaStore.cs`, `PictureView.cs`, `TableGridMap.cs`, `WordDocument.cs` | pending media with relationship ids, the read-only picture and table-grid views |
| `src/Raffica.Write.Docx/DocxFile.cs`, `Xml/NotesXml.cs`, `Xml/MediaPartWriter.cs`, `Xml/DrawingBuilder.cs`, `Xml/SettingsXml.cs`, `BodySnapshotCache.cs` | created notes parts and media parts, the guard, the recovery copy, drawing XML |
| `src/Raffica.Write.Editing/ObjectChange.cs`, `EditResult.cs`, `EditHistory.cs` | the payload for notes, media and style companions and its undo |
| `src/Raffica.Write.Editing/Notes/*`, `Tables/*`, `Pictures/*`, `TextEditor.Notes.cs`, `.Tables.cs`, `.Pictures.cs`, `.Clipboard.cs` | the operations and their facts |
| `src/Raffica.Write.Layout/Pages/ObjectMap.cs`, `LaidOutPage.cs`, `LayoutEdits.cs`, `Floats/FloatSpec.cs` | the object map, `HitObject`, notes in `ApplyEdit`, anchor round trip |
| `src/Raffica.Write/Editing/ObjectSelection.cs`, `EditSession.cs`, `IEditClipboard.cs` | object selection, image on the clipboard |
| `src/Raffica.Write/Viewing/PageView.Objects.cs`, `ObjectHandles.cs`, `ObjectDrag.cs`, `PageView.Keys.cs`, `PageView.Editing.cs` | hit testing, handles, drag state machine, keys |
| `src/Raffica.Write/Commands/*`, `WriteRibbon.cs`, `WriteStrings.cs`, `Resources/Strings.*.resx`, `Platform/MacOS/WriteMacMenu.cs`, `Dialogs/*Card.cs`, `WriteWindow.Objects.cs` | Insert and Table tabs, commands, rules, cards, wiring |
| `tests/Raffica.Write.Tests/Support/*`, `Layout/ObjectSteps.cs`, `ObjectOracleTests.cs`, `Editing/*`, `tests/Raffica.Write.App.Tests/*` | oracles and tests |
| `bench/write-layout-bench`, `src/Raffica.Write/Measurement.Objects.cs`, `bench/shell-probe/measure.cs`, `.github/workflows/measure.yml` | `objects` and `gesture` cases |
| `Docs/DECISIONS.md`, `CLAUDE.md`, `CHANGELOG.md`, `Docs/write-word-check.md` | records |

---

## Stage 1: notes (proves the package support)

### Task 1: A notes part made on first use

**Files:** Modify `src/Raffica.Write.Docx/DocxFile.cs`, `Xml/NotesXml.cs`, `Xml/SettingsXml.cs`, `src/Raffica.Write.Model/WordDocument.cs`. Test: `tests/Raffica.Write.Tests/Docx/NotesPartCreationTests.cs`.

**Interfaces:** Produces `public static class NotesParts { public static NoteCollection Ensure(WordDocument document, NoteKind kind); }` in `src/Raffica.Write.Model` that returns the existing collection or makes one with the separator (`w:id` -1) and continuation separator (0) notes; the save writes the part as a created part with its relationship, content type and the `w:footnotePr` or `w:endnotePr` separator references in the settings part.

- [ ] **Step 1:** Write `NotesPartCreationTests`: `ABlankDocumentHasNoFootnotesPart`, `EnsureMakesSeparatorsAndTheSaveWritesAValidPart` (save, reopen with `DocxFile.Open`, `Footnotes` holds separator and continuation, settings lists both), `EnsureOnADocumentWithSeparatorsOnlyReusesThem`, `TheCreatedPartPassesTheSaveGuardAndTheRecoveryCopy` (`SaveSnapshotAsync` too), `NoOtherPartChangedByteForByte`.
- [ ] **Step 2:** Run `dotnet test tests/Raffica.Write.Tests -c Release --filter-class "*NotesPartCreationTests"`; Expected: FAIL (type missing). Implement; the guard's `StructureKept` must allow the created part, its content-type `Override` and the main relationship. Run; Expected: PASS and the count equals the number of tests.
- [ ] **Step 3:** Commit `feat(write): make the notes part on first use (M4.5a)`.

### Task 2: Insert and delete a note, undoable, laid out incrementally

**Files:** Create `src/Raffica.Write.Editing/ObjectChange.cs`, `Notes/NoteOperations.cs`, `TextEditor.Notes.cs`. Modify `EditResult.cs` (`ObjectChange? Objects = null`, included in `Attribute`), `EditHistory.cs`, `StyleChange` use for companions, `src/Raffica.Write.Layout/Pages/LayoutEdits.cs`, `NotePlanner.cs`, `tests/Raffica.Write.Tests/Layout/ObjectSteps.cs` (new, `NoteKind` steps). Test: `Editing/NoteEditingTests.cs`, `Layout/ObjectOracleTests.cs`, `SnapshotCacheOracleTests.cs`.

**Interfaces:** Produces `public sealed record ObjectChange(IReadOnlyList<Note> NotesAdded, IReadOnlyList<Note> NotesRemoved, IReadOnlyList<MediaRef> MediaAdded, IReadOnlyList<MediaRef> MediaRemoved)` and `public EditResult? TextEditor.InsertNote(TextPosition caret, NoteKind kind)` (null with a refusal reason through `NoteRules.Refusal(caret): NoteRefusal?`; result caret inside the new note's first paragraph); deleting a range that holds a `FootnoteReference` or `EndnoteReference` removes the note in the same step.

- [ ] **Step 1:** Write `NoteEditingTests` (`InsertFootnoteAddsReferenceNoteAndStyles`, `TheSecondNoteGetsAFreshId`, `UndoRemovesNoteAndReferenceTogether` and redo, `DeletingTheReferenceDeletesTheNote`, `RefusedInHeaderFooterNoteCommentAndRestrictedControls`, `PrefixDocumentRefuses`, `TheStylesAreCompanionsOfTheStep`) and `ObjectOracleTests.NoteStepsEqualAFullLayoutOnTheCorpus` with kinds `InsertFootnote`, `InsertEndnote`, `DeleteReference`, undo, redo (exact equality, the drawn note numbers equal a count of references in document order).
- [ ] **Step 2:** Run both classes; Expected: FAIL. Implement; the session routes `Objects` to `DocumentLayout.ApplyEdit` with the changed paragraph and the note state (plan-cache and filler state keys already include note state); styles added as companions go through `Restyle`. Add the case to `SnapshotCacheOracleTests`. Run; Expected: PASS.
- [ ] **Step 3:** Commit `feat(write): insert and delete footnotes and endnotes (M4.5a)`.

### Task 3: Note commands, the Insert tab and Back to the text

**Files:** Create `src/Raffica.Write/WriteWindow.Objects.cs`. Modify `Commands/WriteCommandIds.cs`, `WriteCommandCatalog.cs`, `WriteCommandRules.cs`, `WriteRibbon.cs`, `WriteStrings.cs`, `Resources/Strings.en.resx`, `Strings.de.resx`, `Platform/MacOS/WriteMacMenu.cs`, `Viewing/PageView.Editing.cs` (double click on a reference and on a note number). Test: `Commands/NoteCommandTests.cs`, `Viewing/PageViewNoteTests.cs`, `Commands/ObjectRibbonTests.cs`.

**Interfaces:** Produces ids `note.footnote`, `note.endnote`, `note.back`; the Insert tab (key tip `I`, groups Table, Picture, Notes; Table and Picture groups disabled-with-reason until their tasks); `PageView.GoToNote(TextPosition reference)` and `PageView.BackToReference()`.

- [ ] **Step 1:** Write the tests: every disabled state has a reason (`FormatCommandTests` pattern), `TheWritingViewRefusesNotesWithItsReason`, `AfterInsertTheCaretIsInTheNote`, `BackReturnsToTheReferenceAndEscapeToo`, `DoubleClickOnTheReferenceJumpsToTheNote`, `TheHomeBandHintIsUnchanged` (`RibbonStartupTests`), `StartupCreatesNoObjectWork` (counters at 0 after the first sharp page).
- [ ] **Step 2:** Run `--filter-class` for each; Expected: FAIL. Implement (icons from the Lucide set, strings in both resx, LF). Run; Expected: PASS.
- [ ] **Step 3:** Commit `feat(write): footnote and endnote commands and the Insert tab (M4.5a)`.

**Stage 1 checkpoint:** `dotnet build Raffica.slnx -c Release` and `dotnet test tests/Raffica.Write.Tests -c Release --filter-class "*Note*"` green; run `pwsh tools/word-open-check.ps1` on a file made by the new `WordCheckObjectFilesTests` (Task 14 completes it; write its note files here: `footnote.docx`, `endnote.docx`, `notes-in-table.docx`).

## Stage 2: the object foundation

### Task 4: The object map and `HitObject`

**Files:** Create `src/Raffica.Write.Layout/Pages/ObjectMap.cs`. Modify `LaidOutPage.cs`, `PageBuilder.cs`, `Tables/TableLayouter.cs`, `Floats/FloatPlanner.cs`, `LayoutEdits.cs`, `ImageItem` (an `ObjectId`). Test: `tests/Raffica.Write.Tests/Layout/ObjectMapTests.cs`, `LayoutOracle.cs` (compares the map).

**Interfaces:** Produces, in `src/Raffica.Write.Model/Text/ObjectId.cs` (Model, because Editing may reference only Model), `public sealed record ObjectId(StoryRef Story, int Paragraph, int Run, int Content)` (a picture's identity: the paragraph, the run in it and the drawing in the run; re-read after every edit like a `TextPosition`), and in Layout `public sealed record PictureBox(ObjectId Id, float X, float Y, float Width, float Height, bool Floating, bool Resizable)`, `public sealed record TableGrid(StoryRef Story, int BlockIndex, float X, float Y, float Width, float Height, IReadOnlyList<float> ColumnBorders, IReadOnlyList<float> RowEdges, IReadOnlyList<CellRect> Cells)`, `public sealed record CellRect(int Row, int Column, int RowSpan, int ColumnSpan, float X, float Y, float Width, float Height)`, `LaidOutPage.Objects: ObjectMap` and `DocumentLayout.HitObject(int page, float x, float y, float tolerance): ObjectHit?` (`ObjectHit` = picture, picture handle slot, column border index, row edge index, cell, or table margin).

- [ ] **Step 1:** Write `ObjectMapTests`: `AnInlinePictureGetsABoxWithItsIdentity`, `AFloatingPictureIsMarkedFloating`, `ARotatedPictureIsNotResizable`, `AShapeIsNeverAPictureBox`, `ATableListsEveryColumnBorderAndRowEdge`, `AMergedCellIsOneRectOfItsSpan`, `ARepeatedHeaderRowIsReportedOncePerPageItAppearsOn`, `HitObjectPrefersAHandleOverThePictureOverTheBorder`, `ADocumentWithoutObjectsHasTheSharedEmptyMap`, and the oracle `TheMapOfAnIncrementalLayoutEqualsTheMapOfAFullLayoutOnEveryCorpusFile`.
- [ ] **Step 2:** Run `--filter-class "*ObjectMapTests"`; Expected: FAIL. Implement; the map is built with the page from the items the layouter already places (no second pass); pages with no object share one empty map. Run the class and `*LayoutOracle*`; Expected: PASS, and the thesis layout time (`write-layout-bench check --root . --budget-ms 3000`) is unchanged within noise.
- [ ] **Step 3:** Commit `feat(write): publish an object map with every laid-out page (M4.5a)`.

### Task 5: Object selection, handles and the drag state machine

**Files:** Create `src/Raffica.Write/Editing/ObjectSelection.cs`, `Viewing/ObjectHandles.cs`, `ObjectDrag.cs`, `PageView.Objects.cs`. Modify `EditSession.cs`, `PageView.Editing.cs`, `PageView.Keys.cs`. Test: `tests/Raffica.Write.App.Tests/Viewing/ObjectHandlesTests.cs`, `ObjectDragTests.cs`, `PageViewObjectTests.cs`, `Editing/ObjectSelectionTests.cs`.

**Interfaces:** Produces `abstract record ObjectSelection` with `PictureSelection(ObjectId Id)` and `CellSelection(TableRef Table, CellRange Range)`; `EditSession.ObjectSelection`, `SetObjectSelection(ObjectSelection?)`, event `ObjectSelectionChanged`; a text selection clears the object selection and the reverse; `ObjectHandles.Slots(PictureBox, float scale): IReadOnlyList<HandleSlot>` (eight handles, 8 DIP squares with a 12 DIP hit area); `ObjectDrag` states `Idle`, `Pressed`, `Dragging(kind)`, `Releasing` with `Cancel()` for Esc, capture lost, pane hide, save start and document replacement.

- [ ] **Step 1:** Write the tests: `HandlesSitOnTheCornersAndEdges`, `TheHitAreaIsLargerThanTheDrawnHandle`, `EscapeEndsADragWithNoChange`, `CaptureLostEndsADragWithNoChange`, `ASaveStartingMidDragCancelsIt`, `AnUndoMidDragCancelsIt`, `ClickingAPictureSelectsIt`, `ClickingTextClearsTheObjectSelection`, `ShiftClickExtendsACellRange`, `ShiftArrowsExtendACellRange`, `ClickingTheLeftMarginSelectsARowTheTopEdgeAColumnAndTheCornerTheTable` (and the same by the commands `table.selectRow`, `table.selectColumn`, `table.selectTable`, ids added to Task 8), `DeleteWithAPictureSelectedRemovesIt` (calls the task 11 operation through a fake), `AnObjectSelectionIsAnnouncedWithItsAltTextOrTablePosition` (automation text), `NothingIsBuiltBeforeTheFirstObjectIsHit`.
- [ ] **Step 2:** Run each class; Expected: FAIL. Implement; the overlay is drawn by the view over the page after `PageRenderer.Draw` (no visual touched in the render pass, a guard test as for the section chips). Run; Expected: PASS.
- [ ] **Step 3:** Commit `feat(write): select pictures and cell ranges and draw their handles (M4.5a)`.

## Stage 3: tables

### Task 6: The table grid view and the structure operations

**Files:** Create `src/Raffica.Write.Model/TableGridMap.cs`, `src/Raffica.Write.Editing/Tables/TableOperations.cs`, `TableRef.cs`, `CellRange.cs`, `TextEditor.Tables.cs`. Modify `EditHistory.cs` (container-relative points, as the list operations). Test: `Model/TableGridMapTests.cs`, `Editing/TableStructureTests.cs`, `Layout/ObjectOracleTests.cs` (table kinds).

**Interfaces:** Produces `TableGridMap.Of(Table): TableGridMap` (rows by grid columns; merged regions; no storage of its own), and on `TextEditor`: `EditResult? InsertTable(TextPosition at, int columns, int rows)`, `AddRow(TableRef, int index, bool after)`, `AddColumn(TableRef, int index, bool after)`, `DeleteRows(CellRange)`, `DeleteColumns(CellRange)`, `DeleteTable(TableRef)`, `MergeCells(CellRange)`, `SplitCell(TableRef, int row, int column)`, `MoveRow(TableRef, int row, int delta)`; `TableRef(StoryRef Story, int BlockIndex)`, `CellRange(TableRef Table, int FirstRow, int FirstColumn, int LastRow, int LastColumn)`; every operation returns null with `TableRules.Refusal(...)` for nested tables, headers, footers, notes, restricted controls, a prefix document and limits (63 columns, 500 rows).

- [ ] **Step 1:** Write `TableStructureTests` with one test per spec rule: `InsertMakesAFullWidthEqualColumnTableWithAParagraphAfterIt`, `TheTableGridStyleIsACompanionOfTheStep`, `AddRowInsideAVerticalMergeExtendsTheMerge`, `AddColumnInsideAColumnSpanExtendsTheSpan`, `DeleteRowPassesTheMergeStartToTheNextRow`, `DeleteColumnShrinksASpanAndRemovesASingleColumnCell`, `MergeConcatenatesNonEmptyParagraphsInOrder`, `SplitRestoresTheOriginalCellsAndKeepsContentInTheFirst`, `SplitOfAnUnmergedCellIsRefusedWithAReason`, `MoveRowUpAndDownAreOneStepAndUndo`, `ANestedTableRefusesEveryCommand`, `TheLastTableAtTheEndOfTheBodyKeepsAParagraphAfterIt`, `UndoAndRedoRestoreTheExactBytes` and the oracle kinds in `ObjectSteps` (`InsertTable`, `AddRow`, `AddColumn`, `DeleteRows`, `DeleteColumns`, `Merge`, `Split`, `MoveRow`) over the corpus and generated tables, each ending in `AssertEqualToFull` and a model-grid check (`TableGridMap` agrees with the layout's `TableGrid`).
- [ ] **Step 2:** Run the three classes; Expected: FAIL. Implement; each operation builds the new table, replaces the block with one `BlockChange` and re-derives the position ints; the history point is container-relative. Add the cases to `SnapshotCacheOracleTests`. Run; Expected: PASS.
- [ ] **Step 3:** Commit `feat(write): table structure operations (M4.5a)`.

### Task 7: Borders, shading, header row and widths

**Files:** Create `src/Raffica.Write.Editing/Tables/TableFormatting.cs`. Modify `TextEditor.Tables.cs`, `src/Raffica.Write.Layout/Tables/GridSolver.cs` (only to expose what the round trip test needs). Test: `Editing/TableFormattingTests.cs`, `Layout/TableWidthRoundTripTests.cs`.

**Interfaces:** Produces `EditResult? SetBorders(CellRange, BorderSpec)` (`BorderSpec` = presets none, all, outside, box; per side; colour from `ColorChoices`; width in eighths of a point; line style single, double, dashed, dotted), `SetShading(CellRange, uint? colour)`, `SetHeaderRow(TableRef, bool)` (`w:tblHeader` on row 0), `SetColumnWidths(TableRef, IReadOnlyList<int> twips)`, `SetTableWidth(TableRef, int twips)`, `SetRowHeight(TableRef, int row, int twips, bool exact)` (`w:trHeight`, at least or exact; the card and the command search reach it), `DistributeColumns(TableRef)`; the first width write converts autofit and percentage tables to fixed layout (`w:tblLayout fixed`, `w:tblW` dxa, every `w:tcW` dxa, grid equal to the cells).

- [ ] **Step 1:** Write `TableFormattingTests` (`BoxBordersWriteTheFourOuterSides`, `InsideBordersAreAppliedByAllNotByBox`, `ASharedBorderIsWrittenOnBothCells`, `ShadingUsesTheFixedColourList`, `HeaderRowRepeatsOnEachPageInTheLayout`, `DistributeGivesEqualColumnsThatSumToTheTableWidth`, `AFirstWidthEditConvertsAnAutofitTableToFixed`, `ReadingNeverConverts`, `UndoRestoresThePercentageWidths`) and `TableWidthRoundTripTests.TheEditorWritesWhatGridSolverReadsBackForEveryTableKindInTheCorpus` (autofit, fixed, percentage, merged).
- [ ] **Step 2:** Run; Expected: FAIL. Implement (merge attributes into existing elements, children by `SchemaOrder`, as the character formatting did). Run; Expected: PASS; add the kinds to `ObjectSteps`.
- [ ] **Step 3:** Commit `feat(write): table borders, shading, header row and widths (M4.5a)`.

### Task 8: The Table tab, the grid picker, the cards and the Tab rules

**Files:** Create `src/Raffica.Write/Commands/TablePickers.cs`, `TableFactsSource.cs`, `Dialogs/TableInsertCard.cs`, `TablePropertiesCard.cs`, `TableBordersCard.cs`. Modify `WriteRibbon.cs`, `WriteCommandRules.cs`, `WriteStrings.cs`, resx files, `WriteMacMenu.cs`, `Viewing/PageView.Keys.cs` (Tab). Test: `Commands/TableCommandTests.cs`, `Dialogs/TableCardsTests.cs`, `Viewing/PageViewTableKeysTests.cs`, `CardFooterTests.cs`, `CloseGuardTests.cs`.

**Interfaces:** Produces the ids of the spec (`table.insert`, `table.rowAbove`, `table.rowBelow`, `table.colLeft`, `table.colRight`, `table.deleteRows`, `table.deleteColumns`, `table.delete`, `table.merge`, `table.split`, `table.borders`, `table.shading`, `table.headerRow`, `table.properties`, `table.distributeColumns`, `table.rowUp`, `table.rowDown`, `table.selectRow`, `table.selectColumn`, `table.selectTable`), the Table tab (key tip `T`), the grid picker (10 by 8, a card for more), `TabOperation` extended: in a table cell Tab and Shift+Tab move between cells and Tab in the last cell adds a row, with the list rule of 4.4a deciding first only when the caret is at the start of a list item with text.

- [ ] **Step 1:** Write the tests: a reason for every disabled state, `TheGridPickerIsBuiltOnFirstOpenOnly`, `TabMovesToTheNextCellAndAddsARowInTheLast`, `TabInAListInACellFollowsTheListRuleAtTheStartOfAnItem` (a case table with every combination of list, table and caret place), `ShiftTabMovesBack`, `TheCardsAreBuiltByCardFooter`, `EveryTableCommandIsDisabledInAPrefixDocumentAndDuringASaveQueuedNotLost`, German strings exist (`TranslationTests`).
- [ ] **Step 2:** Run; Expected: FAIL. Implement; every command its own Lucide icon. Run; Expected: PASS.
- [ ] **Step 3:** Commit `feat(write): the Table tab, cards and table keys (M4.5a)`.

### Task 9: Table gestures

**Files:** Modify `src/Raffica.Write/Viewing/ObjectDrag.cs`, `PageView.Objects.cs`, `ObjectHandles.cs`. Test: `tests/Raffica.Write.App.Tests/Viewing/TableGestureTests.cs`.

**Interfaces:** Consumes `DocumentLayout.HitObject`, `TextEditor.SetColumnWidths`, `MoveRow`, `SetObjectSelection`. Produces drag kinds `ColumnBorder`, `TableEdge`, `Row` (by the handle in the left margin) and `CellRange` (press in a cell, drag into another) each with an outline drawn by the view and one `Apply` on release.

- [ ] **Step 1:** Write `TableGestureTests`: `DraggingABorderMovesItBetweenTwoColumnsAndKeepsTheTableWidth`, `DraggingTheRightEdgeChangesTheLastColumnAndTheTableWidth`, `ABorderHasAMinimumColumnWidth`, `DraggingARowMovesItAsOneStepAndUndoes`, `DraggingAcrossCellsSelectsTheRangeAndShowsNoModelChange`, `TheCursorChangesOverABorderAndTheHandle`, `NoGestureStartsDuringASaveAndAPressOnlySelects`, `EscapeCancelsEachDragKind`, `ADragOnAMergedCellSelectsItsWholeSpan`, `AGestureOnANestedTableIsRefused`, and `ThePointerMoveCostsAtMost16msP95OnTheThesis` (a measurement test marked as in `WallClockGroup`).
- [ ] **Step 2:** Run; Expected: FAIL. Implement (the outline is a view overlay; hit test only on moves). Run; Expected: PASS.
- [ ] **Step 3:** Commit `feat(write): drag borders, rows and cell ranges (M4.5a)`.

**Stage 3 checkpoint:** `dotnet test tests/Raffica.Write.Tests -c Release --filter-class "*Table*"` and `--filter-class "*ObjectOracleTests"` green; write `WordCheckObjectFilesTests` rows for tables (`table-basic.docx`, `table-merged.docx`, `table-borders.docx`, `table-widths.docx`) and run `pwsh tools/word-open-check.ps1`.

## Stage 4: pictures

### Task 10: Media in the package

**Files:** Create `src/Raffica.Write.Model/MediaStore.cs`, `src/Raffica.Write.Docx/Xml/MediaPartWriter.cs`. Modify `WordDocument.cs`, `DocxFile.cs` (created binary parts, the guard, `SaveSnapshotAsync`), `BodySnapshotCache.cs`, `src/Raffica.Write/Documents/PackageImageSource.cs`. Test: `Docx/MediaPartTests.cs`, `DocxSnapshotGuardTests.cs`, `Documents/PackageImageSourceTests.cs`.

**Interfaces:** Produces `public sealed record MediaRef(string RelationshipId, string ContentType, string Extension)`, `MediaStore.Add(ReadOnlyMemory<byte> bytes, string contentType): MediaRef` (relationship id unique among the main part's, seeded at open), `TryGet(string id): ReadOnlyMemory<byte>?`, `Remove(MediaRef)`; `WordDocument.Media`; the save writes each used entry as `/word/media/imageN.<ext>` with its content-type `Default` and an image relationship from the main part, and writes none for an unused entry.

- [ ] **Step 1:** Write `MediaPartTests`: `ANewPictureIsWrittenAsAMediaPartWithItsRelationship`, `AnUndonePictureLeavesNoMediaPartInTheNextSave`, `TheRelationshipIdIsNeverUsedTwice`, `TwoPicturesWithTheSameBytesMayShareOnePart` (optional, test only the safe case), `TheGuardAllowsTheCreatedPartAndNothingElse`, `TheRecoveryCopyCarriesThePicture`, `NoDanglingRelationshipAfterAnySave`, `PackageImageSourceReadsPendingMediaBeforeThePackage`.
- [ ] **Step 2:** Run; Expected: FAIL. Implement; the guard's created-part rules extend to binary parts (`IsContentTypeOfCreated`, `IsRelationshipToCreated`). Run; Expected: PASS.
- [ ] **Step 3:** Commit `feat(write): write new pictures as media parts (M4.5a)`.

### Task 11: Picture operations

**Files:** Create `src/Raffica.Write.Model/PictureView.cs`, `src/Raffica.Write.Docx/Xml/DrawingBuilder.cs`, `src/Raffica.Write.Editing/Pictures/PictureOperations.cs`, `TextEditor.Pictures.cs`. Modify `Floats/FloatSpec.cs` (anchor round trip). Test: `Model/PictureViewTests.cs`, `Editing/PictureEditingTests.cs`, `Layout/FloatRoundTripTests.cs`, `Layout/ObjectOracleTests.cs`.

**Interfaces:** Produces `PictureView.Of(Run, int content): PictureInfo?` (`ExtentEmu`, `Rotation`, `Crop`, `Anchor`, `AltText`, `Media`), and on `TextEditor`: `EditResult? InsertPicture(TextPosition at, ReadOnlyMemory<byte> bytes, string? altText)`, `ResizePicture(ObjectId, long widthEmu, long heightEmu)` (writes `wp:extent` and `a:ext` only), `SetAltText(ObjectId, string)`, `SetPictureWrap(ObjectId, PictureWrap)` (inline, square, top and bottom, in front, behind; the inline-to-floating change writes Word's defaults of rule 3, `relativeHeight` above every other drawing, `docPr` ids unique), `MovePicture(ObjectId, TextPosition anchorParagraph, long dxEmu, long dyEmu)`, `NudgePicture(ObjectId, int dxPt, int dyPt)`, `ResetPictureSize(ObjectId)`, `DeletePicture(ObjectId)`; `PictureRules.Refusal(...)`; the byte checks (50 MiB, 32,000,000 pixels, header probe) happen before any change.

- [ ] **Step 1:** Write `PictureEditingTests` (one per rule: `InsertWritesAnInlineDrawingWithAUniqueDocPrId`, `AltTextIsStored`, `ResizeKeepsTheCropAndEveryOtherChild`, `ARotatedPictureRefusesResize`, `InlineToFloatingWritesWordsDefaults`, `FloatingToInlineRestoresTheInlineForm`, `MoveRecomputesTheOffsetsAgainstTheNewAnchorParagraph`, `ADropInACellNoteHeaderOrFooterIsRefused`, `NudgeCoalescesPerSecond`, `OversizedOrLyingBytesAreRefusedWithoutAChange`, `NoMessageHoldsBytesNameOrAltText`, `UndoRemovesTheMediaFromTheNextSave`), `FloatRoundTripTests.TheLayoutReadsBackExactlyWhatTheEditorWrote` and the oracle kinds `InsertPicture`, `Resize`, `Wrap`, `Move`, `Nudge`, `DeletePicture` in `ObjectSteps`; a cropped corpus picture is in the set.
- [ ] **Step 2:** Run; Expected: FAIL. Implement; the result carries `ObjectChange.MediaAdded`; inserting calls `ImageCache.Clear` through the session and triggers the prefetch. Add `SnapshotCacheOracleTests` cases. Run; Expected: PASS.
- [ ] **Step 3:** Commit `feat(write): insert, resize, wrap, move and delete pictures (M4.5a)`.

### Task 12: Picture commands, cards and gestures

**Files:** Create `src/Raffica.Write/Dialogs/PictureInsertCard.cs`, `PicturePropertiesCard.cs`, `Commands/PictureFactsSource.cs`. Modify `WriteRibbon.cs`, `WriteCommandRules.cs`, strings, resx, `WriteMacMenu.cs`, `Viewing/ObjectDrag.cs`, `PageView.Keys.cs`. Test: `Commands/PictureCommandTests.cs`, `Dialogs/PictureCardsTests.cs`, `Viewing/PictureGestureTests.cs`.

**Interfaces:** Produces `picture.insert` (system file picker through the existing picker service, then `PictureInsertCard` with the optional alt text field; Cancel aborts), `picture.properties`, `picture.wrap`, `picture.reset`, `picture.delete`; corner and edge resize handles (aspect ratio kept, Shift frees it), the floating drag with a no-drop cursor over cells, notes, headers and footers, arrow nudge (1 pt, Shift 10 pt) when a picture is selected, Delete.

- [ ] **Step 1:** Write the tests: reasons for every disabled state, `CancelOnTheInsertCardChangesNothing`, `AnEmptyAltTextIsAllowedAndEditableLater`, `CornerDragKeepsTheRatioAndShiftFreesIt`, `ResizeHasAMinimumSize`, `ADragOfAFloatingPictureMovesItsAnchorToTheParagraphUnderThePointer`, `ADropOnACellShowsNoDropAndDoesNothing`, `ArrowKeysNudgeASelectedPicture`, `EscapeCancelsAResize`, `NoGestureDuringASave`.
- [ ] **Step 2:** Run; Expected: FAIL. Implement. Run; Expected: PASS.
- [ ] **Step 3:** Commit `feat(write): picture commands, cards and gestures (M4.5a)`.

### Task 13: Paste a picture from the clipboard

**Files:** Modify `src/Raffica.Write/Editing/IEditClipboard.cs` (`ReadAsync` returns a content with an optional `ImageData`), `Raffica.Write.Editing/ClipboardContent.cs`, `TextEditor.Clipboard.cs`, `Viewing/PageView.Commands.cs`. Test: `Editing/ImagePasteTests.cs`, `Viewing/PageViewPasteTests.cs`.

**Interfaces:** Produces `ClipboardContent.Image: ReadOnlyMemory<byte>?` (PNG bytes; the Avalonia clipboard reads PNG first, then the system bitmap format, converted to PNG with Skia on a pool thread) and the paste order fragment, image, text; `TextEditor.Paste` calls `InsertPicture(at, png, null)` for an image at 96 DPI capped to the text width.

- [ ] **Step 1:** Write the tests with a fake clipboard: `AnImageOnlyClipboardInsertsAPictureAtTheCaret`, `ASelectionIsReplacedByThePictureAsByText`, `AnImageWithTextPastesTheText`, `OwnFragmentWinsOverAnImage`, `TheSizeIsThePixelSizeAt96DpiCappedToTheTextWidth`, `AZeroByteOrTruncatedImagePastesNothingAndSaysWhy`, `AnImageInAHeaderFallsThroughToTextOrNothing`, `OnePasteIsOneUndoStep`, `NoMessageHoldsBytes`.
- [ ] **Step 2:** Run; Expected: FAIL. Implement. Run; Expected: PASS. Real clipboards are an owner gate (Task 14).
- [ ] **Step 3:** Commit `feat(write): paste a picture from the clipboard (M4.5a)`.

## Close

### Task 14: Measuring, Word files, records and the gates

**Files:** Create `src/Raffica.Write/Measurement.Objects.cs`, `tests/Raffica.Write.Tests/Editing/WordCheckObjectFilesTests.cs`. Modify `bench/write-layout-bench` (`edit` rows), `bench/shell-probe/measure.cs` (cases `objects` and `gesture`, the names lists change together with the app's places and the bench's cases), `.github/workflows/measure.yml`, `Docs/write-word-check.md`, `Docs/DECISIONS.md` (decision "Raffica Write: tables, pictures and notes (M4.5a)" with every known limit), `CLAUDE.md` (section "Write objects (M4.5a)"), `CHANGELOG.md`, `Docs/roadmap.json`, `Docs/ROADMAP.md`.

- [ ] **Step 1:** Add `--measure-objects` (cases `table-insert`, `table-add-row`, `table-merge`, `table-border-set`, `picture-insert`, `picture-paste`, `picture-resize`, `picture-float-move`, `footnote-insert`, `footnote-delete` on the thesis, undone outside the timing) and `--measure-gesture` (pointer moves of each drag kind); `measure.cs` judges `objects` against a fixed limit (budget times 1.25 of the first measurement, entered in `s_writeBudgets` with its decision entry) and `gesture` at 16 ms p95; `typing` with the table or picture pane open counts into `typing`; `selftest` passes with the new names.
- [ ] **Step 2:** Write `WordCheckObjectFilesTests` (does nothing without `RAFFICA_WORD_CHECK_DIR`) writing files for every command through the editor; run `pwsh tools/word-open-check.ps1 <files>` on the Windows PC and record the rows; check Word shows each table, picture and note as Write does.
- [ ] **Step 3:** Run the full gate: `dotnet build Raffica.slnx -c Release`, `RAFFICA_REQUIRE_OCR=1 dotnet test Raffica.slnx -c Release --no-build --max-parallel-test-modules 1`, `dotnet format Raffica.slnx --verify-no-changes`, `dotnet run tools/roadmap.cs -- check`; also measure `--measure-startup` against the commit before 4.5a (three interleaved rounds, ten runs) and record it; Native AOT publish of the app with the log searched for `IL[23][0-9]{3}`. Expected: all green, no regression beyond noise.
- [ ] **Step 4:** Tick the roadmap tasks and criteria with proof (test names, commands, results), set 4.5.1 `done` only when the owner gates are recorded; the open gates are named with their commands: Word opens the files (owner script), a look at the AOT app on Windows, the Linux laptop and the Intel Mac (handles, outline, cursors, grid picker), `gh workflow run measure.yml --ref dev -f platform=<linux|macos> -f target=write -f note="<machine>"` with the new cases, and the real clipboard with an image on each platform. Write the decision, the `CLAUDE.md` section and the `CHANGELOG.md` entry; convert every new text file to LF.
- [ ] **Step 5:** Commit `docs(write): record milestone 4.5a (M4.5a)`; open the PR to `dev` and merge when CI is green.
