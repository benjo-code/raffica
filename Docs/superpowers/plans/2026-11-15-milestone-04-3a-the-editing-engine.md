# Milestone 4.3a – The editing engine Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Write edits text in every story of a DOCX (caret, selection, typing with IME, delete, clipboard, undo and redo, dirty state, Save, Save as, close prompt) with a keystroke ≤ 16 ms on the generated thesis.

**Architecture:** One definition of a paragraph's text (`ParagraphProjection`, Model) drives layout, positions and editing. The layout library gets a source map, hit test, caret geometry and a resumable engine (page-start records, per-block plan cache, state snapshots, the spike's stop-where-it-started rule) proven by an oracle: incremental equals a full layout over the whole corpus. A UI-free editing library (`Raffica.Write.Editing`) holds operations, rules, history and clipboard fragments; the app (`PageView`, `EditSession`) adds input, IME, save and the close guard behind one edit gate.

**Tech Stack:** .NET 10 (Native AOT), Avalonia 12 (`TextInputMethodClient`), SkiaSharp, HarfBuzzSharp, Open XML SDK package layer, MSTest-style runner (`dotnet test`, `--filter-class`).

**Spec:** `Docs/superpowers/specs/2026-11-15-raffica-write-editing-engine-design.md` (approved 2026-11-15). Parent spec: `Docs/superpowers/specs/2026-11-13-raffica-write-design.md`. Executors read both and `CLAUDE.md` sections "Write library (M4.1)" and "Write viewer (M4.2)".

**Roadmap:** the split of 4.3 into 4.3a, 4.3b, 4.3c, this plan's link and the status `planned` were made in `Docs/roadmap.json` while planning. The roadmap's ids are `4.3.1` (this plan), `4.3.2` and `4.3.3`, because its schema allows only dotted numbers; everything else says 4.3a, 4.3b and 4.3c. Tick tasks and criteria there as they are done (with evidence) and run `dotnet run tools/roadmap.cs -- generate`.

## Global Constraints

- Typing ≤ 16 ms per keystroke in the app on the generated thesis, median and 95th percentile, from the key event to the frame in which the page(s) in view are correct; pages further on and the other view are brought up to date in the background. A hit test ≤ 50 µs.
- `Raffica.Write.Model` references nothing; `Raffica.Write.Layout` references Model, SkiaSharp, HarfBuzzSharp and no Avalonia type; `Raffica.Write.Editing` references only Model; the app never references `Raffica.Pdf`. `ProjectGuardTests` pins all of it (the compiler drops unused references: the guard sees only used ones).
- No work before first use: no editor object, clipboard code or IME client exists before the first caret or key (`StartupCreatesNoLayoutOrFontWork` style guard).
- The model must not change while a layout or a save runs on it; every edit takes the edit gate (Task 14).
- Everything that does not understand stays in place as `UnknownContent`; `UnknownContent` is shared by reference between a block and its snapshot, never copied.
- A field result and every atomic unit is one unit; typing inside a tracked insertion is allowed and not tracked; a comment is deleted only by its own command.
- Every UI string in `Strings.en.resx` and `Strings.de.resx` ("Sie" form, `TranslationTests`, `GermanRegisterTests`); every command has its own Lucide icon key and a reason in `WriteCommandRules`; every card footer is `CardFooter.Build` (primary first, Cancel last); new colour tokens are classified in the contrast policy.
- Files are LF: after writing a file with the Write tool run `sed -i 's/\r$//' <file>` (CRLF fails `dotnet format`).
- Conventional Commits, no attribution lines, stage files by name (never `git add .`), check `git show --stat HEAD` against the expected list after each commit. Work on `feature/m43a-editing-engine`; never commit to `dev` or `main`.
- Tests: `dotnet test tests/Raffica.Write.Tests -c Release --filter-class "*Name"` and `dotnet test tests/Raffica.Write.App.Tests -c Release --filter-class "*Name"`; a filter that matches nothing selects nothing silently, so read the count. Done means `dotnet build Raffica.slnx -c Release`, the tests (with `RAFFICA_REQUIRE_OCR=1` after `dotnet run tools/native-fetch.cs -- --rid win-x64 --out artifacts/native/win-x64`), `dotnet format Raffica.slnx --verify-no-changes` and `dotnet run tools/roadmap.cs -- check` are green.
- The Native AOT publish of the app and the bench has no `IL[23][0-9]{3}` in its log (the Windows publish needs `Microsoft Visual Studio\Installer` on `PATH`).
- English everywhere in code, docs and commits; references in docs name files and types, not line numbers.

## Review Focus

The inputs the spec implies but no task's tests would otherwise exercise, most likely first. Each has a test in the task that owns the code.

1. **Grapheme clusters and surrogate pairs:** Backspace and arrow keys on "👍🏽", "é" as e + U+0301, and "fi" ligatures remove or step over one visible character, never half of one (Tasks 4, 10).
2. **Story boundaries:** Backspace at the start of a table cell, at the first paragraph after a table, in an empty cell, in an empty header and in a document with one empty paragraph never merges across the boundary, throws or loses a block (Task 10).
3. **Paste of odd or huge text:** a 5 MB plain text, text with `\r\n`, `\r`, tabs and control characters pastes in one history step within a bounded time, becomes paragraphs and tabs, and drops the control characters (Tasks 10, 13).
4. **A failed save:** a read-only target, a file that changed on disk and a target that appeared after the picker leave the document dirty, the history intact and the file untouched, with a card that says what happened (Task 19).
5. **Scroll, zoom and focus loss during a composition:** the candidate rectangle follows the caret after scrolling and zooming, a lost focus clears the preedit without writing it, and Esc leaves no residue in the model, the history or the dirty state (Task 18).

## File Structure

| Area | Files |
|---|---|
| Model (`src/Raffica.Write.Model/Text/`) | `StoryRef.cs`, `TextPosition.cs` (with `TextRange`), `IProjectionHost.cs`, `ParagraphProjection.cs`, `BlockChange.cs`, `DocumentEdits.cs`; `Clone` members on the block and inline types |
| Layout (`src/Raffica.Write.Layout/`) | `Pages/LayoutProjectionHost.cs`, `Pages/TextLine.cs`, `Pages/PageStart.cs`, `Pages/FillerState.cs`, `Pages/TextNavigator.cs`, `Pages/LayoutEdits.cs` (the `ApplyEdit` partial of `DocumentLayout`); changes to `BlockPlanner`, `ParagraphFlow`, `PageFiller`, `PageList`, `PageBuilder`, `LaidOutPage`, `NumberingCounter`, `NotePlanner`, `HeaderFooterPlan`, `ParagraphShaper`, `LineBox` (`GlyphRun`) |
| Editing (`src/Raffica.Write.Editing/`, new) | `TextEditor.cs`, `EditResult.cs`, `EditRules.cs`, `EditHistory.cs`, `ClipboardContent.cs` |
| Docx | `src/Raffica.Write.Docx/Fragments/DocxFragment.cs`; `DocxFile` save overloads |
| App (`src/Raffica.Write/`) | `Editing/EditSession.cs`, `Editing/EditLease.cs`, `Editing/EditingKeys.cs`, `Editing/WriteImeClient.cs`, `Viewing/PageView.Editing.cs`, `Documents/WriteSaveFlow.cs`, `Documents/CloseGuard.cs`, `Dialogs/UnsavedChangesCard.cs`, `Dialogs/ChangedOriginalCard.cs`, `Dialogs/SaveErrorCard.cs`; changes to `WriteTab`, `LayoutRunner`, `PageView`, `WriteWindow`, `WriteCommandIds/Catalog/Rules`, `WriteRibbon`, `WriteMacMenu`, `Measurement` |
| Tests | `tests/Raffica.Write.Tests/Model/*`, `Layout/*`, `Editing/*`; `tests/Raffica.Write.App.Tests/Editing/*`, `Documents/*`, `Dialogs/*` |
| Tools | `bench/write-layout` (`Edit.cs`), `bench/shell-probe/measure.cs` and `CheckCommand` |

---

### Task 1: Positions and the paragraph projection (Model)

**Files:**
- Create: `src/Raffica.Write.Model/Text/StoryRef.cs`, `TextPosition.cs`, `IProjectionHost.cs`, `ParagraphProjection.cs`
- Test: `tests/Raffica.Write.Tests/Model/ParagraphProjectionTests.cs`

**Interfaces:**
- Produces:
  - `enum StoryKind { Body, Note, HeaderFooter, Comment }`; `readonly record struct StoryRef(StoryKind Kind, string? Key)` (Key: note id, the document's relationship id of the header or footer, comment id; null for the body).
  - `readonly record struct TextPosition(StoryRef Story, Paragraph Paragraph, int Offset)`; `readonly record struct TextRange(TextPosition Start, TextPosition End)` (same story and, in a table, same cell; `IsEmpty`, `Normalized()`).
  - `interface IProjectionHost { string? FieldValue(string instruction); string? NoteMarkText(RunContent reference); bool IsHidden(Run run); }` and `ProjectionHost.Plain` (no values, no marks, nothing hidden).
  - `sealed class ParagraphProjection { static ParagraphProjection Build(Paragraph paragraph, IProjectionHost host); string Text; IReadOnlyList<ProjectionPiece> Pieces; ProjectionPiece? PieceAt(int offset); (int Start, int End)? AtomicAt(int offset); }` and `abstract record ProjectionPiece(int Start, int Length, Run? Run, RunContent? Content, int ContentOffset)` with kinds `TextPiece`, `TabPiece`, `BreakPiece(BreakKind)`, `AtomicPiece(AtomicKind: FieldResult | Picture | NoteReference | CommentReference | Placeholder)`.
  - Offsets are the coordinates of the layout's flat paragraph text: hidden text has no length, a field result is one atomic range of the length of its value, a picture, a break and a placeholder have length 1, the list label is not part of the text.

- [ ] **Step 1: Write the failing tests** in `ParagraphProjectionTests` (build paragraphs in code with `ProjectionHost.Plain` and a small fake host):
  - `PiecesCoverRunsInOrder`: runs "Hello", a tab, "world" → `Text == "Hello\tworld"`; kinds Text, Tab, Text; `Run` of each piece is reference-equal to the source run.
  - `HiddenTextHasNoLength`: host hides run 2 → its piece has `Length == 0`, `Text` lacks it, later offsets shift left.
  - `FieldResultIsOneAtomicUnit`: complex field `PAGE` with stored result "7": plain host → atomic range covers "7" and the instruction text is never in `Text`; fake host `FieldValue("PAGE") == "3"` → range covers "3". `AtomicAt` inside the range returns the whole range.
  - `PictureBreakNoteReferenceAndCommentReferenceAreAtomic`: each has length 1 and the matching `AtomicKind`; a page break is a `BreakPiece` of length 1.
  - `TrackedInsertionIsInTrackedDeletionIsOut`, `HyperlinkAndContentControlAreFlattened`, `BookmarkAndCommentRangeMarkersAddNoText`.
  - `PieceAtReturnsPieceAndInnerOffset` at every boundary (the piece that starts there; at the end of text the last piece).

- [ ] **Step 2: Run** `dotnet test tests/Raffica.Write.Tests -c Release --filter-class "*ParagraphProjectionTests"`. Expected: compile error (types missing).
- [ ] **Step 3: Implement** the types above in the files listed. Mirror the rules of `BlockPlanner.AddInlines`/`AddRun`/`FieldChange` (same visibility, field-frame and tracked-change rules) so Task 3 can replace them; the signature of `Build` and the offsets are the contract.
- [ ] **Step 4: Run** the filter again. Expected: all pass; read the count (≥ 8).
- [ ] **Step 5: Commit** `feat(write): paragraph projection and text positions in the model`.

---

### Task 2: Deep copy and the edit revision (Model)

**Files:**
- Create: `src/Raffica.Write.Model/Text/BlockChange.cs`, `DocumentEdits.cs`
- Modify: `Block.cs`, `Paragraph.cs`, `Table.cs`, `TableRow.cs`, `TableCell.cs`, `ContentControl.cs`, `Run.cs`, `Inline.cs`, `UnknownContent.cs`, `PropertyBag.cs`, `WordDocument.cs`
- Test: `tests/Raffica.Write.Tests/Model/CloneTests.cs`

**Interfaces:**
- Produces:
  - `abstract Block Clone()` on every block type and `Inline Clone()` on every inline type (a deep copy of the object graph; `UnknownContent` instances shared; `PropertyBag.Clone()` copies understood values and shares captured unknown content).
  - `sealed record BlockChange(StoryRef Story, IList<Block> Container, int First, int Removed, int Added, IReadOnlyList<Block> OldBlocks)` (the blocks `[First, First+Removed)` before the edit, as clones taken before the edit; `Container` is the live list).
  - `sealed class DocumentEdits { int Revision; int SavedRevision; bool IsDirty => Revision != SavedRevision; void Raise(); void MarkSaved(); }` and `WordDocument.Edits`.

- [ ] **Step 1: Write the failing tests** in `CloneTests`:
  - `CloneWritesTheSameXml`: for every body block of the generated thesis, write the original and its clone through the document-part writer (`Xml/PartXml`) and assert `XmlEquivalence` equal.
  - `CloneSharesNoMutableObjects`: no `Run`, `Paragraph` or `PropertyBag` of the clone is reference-equal to the original's; every `UnknownContent` is reference-equal.
  - `DirtyFollowsTheRevision`: `Raise()` makes `IsDirty` true; `MarkSaved()` makes it false; after `Raise()` twice, `Restore(savedRevision)` (used by undo) makes `IsDirty` false again, and `Restore(savedRevision + 1)` makes it true.
- [ ] **Step 2: Run** the class. Expected: compile error.
- [ ] **Step 3: Implement** the members; `DocumentEdits.Restore(int revision)` is internal to Model's public API for the history (undo restores the revision a step started from).
- [ ] **Step 4: Run** the class. Expected: pass.
- [ ] **Step 5: Commit** `feat(write): deep copy of blocks and a revision counter in the model`.

---

### Task 3: The layout reads paragraph text through the projection

**Files:**
- Create: `src/Raffica.Write.Layout/Pages/LayoutProjectionHost.cs`; `tests/Raffica.Write.Tests/Layout/LayoutGoldenTests.cs`, `tests/Raffica.Write.Tests/Layout/Golden/layout-golden.json`
- Modify: `src/Raffica.Write.Layout/Pages/BlockPlanner.cs` (`Input`, `AddInlines`, `AddRun`, `FieldChange`, `Decide`, `FieldFormat`, `AddUnknown`, `Append`, `FieldFrame`), `ParagraphPlan.MarkOffsets`

**Interfaces:**
- Consumes: Task 1 (`ParagraphProjection`, `IProjectionHost`).
- Produces: `internal sealed class LayoutProjectionHost(StyleResolver styles, PlanContext context, ResolvedParagraph owner) : IProjectionHost`; `ParagraphInput.Projection` (the projection the input was built from) used by Tasks 5, 6, 10.

- [ ] **Step 1: Capture the golden before changing any layout code.** Write `LayoutGoldenTests.EveryCorpusFileLaysOutAsBefore`: for every corpus file in `LayoutMode.Pages` and `LayoutMode.Flow`, lay out completely and compare, per file and mode, the page count and per page the SHA-256 of `LaidOutPage.Text` and `Items.Count` with `Golden/layout-golden.json`; with `RAFFICA_WRITE_GOLDEN=update` it rewrites the file instead. Run it with the update flag on the unchanged code and commit the JSON and the test alone: `test(write): golden layout record of the corpus`.
- [ ] **Step 2: Run** the test without the flag. Expected: pass (guards the golden itself).
- [ ] **Step 3: Replace** the inline walk of `BlockPlanner.Input` by `ParagraphProjection.Build(paragraph, host)`: each piece becomes the `ParagraphItem` it is today (`TextSpan` with `_styles.ForRun(owner, run)` of the piece's run, `BreakItem`, `InlineBox`, floats and unknown content as now). Field values and note registration still come from `Context.Fields` and `Context.Notes` through the host. Remove the code the projection replaced. `MarkOffsets` is derived from the pieces.
- [ ] **Step 4: Run** `--filter-class "*LayoutGoldenTests"` and the whole `Raffica.Write.Tests` suite. Expected: all pass with the golden unchanged (a difference is a bug in the port, not a reason to update the golden).
- [ ] **Step 5: Commit** `refactor(write): the layout builds paragraph text from the projection`.

---

### Task 4: Cluster map and caret stops in shaping

**Files:**
- Modify: `src/Raffica.Write.Layout/Paragraphs/ParagraphShaper.cs`, `LineBox.cs` (`GlyphRun`), `Justifier.cs`
- Test: `tests/Raffica.Write.Tests/Layout/CaretStopTests.cs`

**Interfaces:**
- Produces on `GlyphRun`: `IReadOnlyList<int> CaretStops` (text offsets, relative to the paragraph, at which a caret may stand inside this run: grapheme-cluster boundaries) and `float CaretX(int textOffset)` (x relative to the line, final after justification; offsets inside a ligature divide it equally; offsets that are not stops snap to the previous stop).

- [ ] **Step 1: Write the failing tests** in `CaretStopTests` (Carlito and Caladea shipped faces):
  - `EdgesAreTheRunEdges`: `CaretX(TextStart) == X` and `CaretX(TextEnd) == X + Width`.
  - `LigatureDividesEqually`: "office" in Carlito shaped with the `ffi` ligature has stops inside it at thirds of its advance (±0.01).
  - `CombiningMarkAndEmojiAreOneStop`: "é" (e + U+0301) and "👍🏽" have no stop between their code points.
  - `StopsAreMonotonicOnAJustifiedLine`: on a justified paragraph `CaretX` over `CaretStops` never decreases.
- [ ] **Step 2: Run** the class. Expected: compile error.
- [ ] **Step 3: Implement.** `ParagraphShaper` keeps the HarfBuzz cluster values per glyph (they are read there today and dropped); `GlyphRun` derives stops from them and from the grapheme boundaries (`StringInfo`) of its text; `Justifier.SetXs` updates the x values `CaretX` reads. No `PageItem` changes.
- [ ] **Step 4: Run** the class and `ParagraphLayoutTests`, `LayoutGoldenTests`. Expected: pass.
- [ ] **Step 5: Commit** `feat(write): cluster map and caret stops in shaped runs`.

---

### Task 5: Text records on laid-out pages

**Files:**
- Create: `src/Raffica.Write.Layout/Pages/TextLine.cs`
- Modify: `PageBuilder.cs`, `LaidOutPage.cs`, `ParagraphFlow.cs` (`Emit`), `BlockPlanner.cs` (`Story` property), `HeaderFooterPlan.cs`, `NotePlanner.cs` (set the story of their auxiliary planners), `Tables/TableLayouter.cs`
- Test: `tests/Raffica.Write.Tests/Layout/TextLineTests.cs`

**Interfaces:**
- Consumes: Tasks 1, 3, 4.
- Produces: `internal sealed record TextLine(StoryRef Story, Paragraph Paragraph, ParagraphProjection Projection, GlyphRun[] Runs, int Start, int End, float X, float Top, float Baseline, float Height)` (X, Top page coordinates of the line; `Start`/`End` projection offsets); `LaidOutPage.TextLines` (internal, `TextLine[]`, in reading order); `BlockPlanner.Story` (default `StoryRef(Body, null)`).

- [ ] **Step 1: Write the failing tests** in `TextLineTests`:
  - `EveryRegionHasRecords`: body paragraph, a table-cell paragraph, a footnote line and a header line of the thesis each appear in `TextLines` with the right `StoryRef` kind (Body, Body, Note, HeaderFooter).
  - `RecordsCoverThePageText`: concatenating the line texts of a page (`Projection.Text[Start..End]`, one per line, `\n` joined) equals `LaidOutPage.Text` after removing list labels, for every page of the thesis.
  - `ItemsAreUnchanged`: `LayoutGoldenTests` passes.
- [ ] **Step 2: Run** the class. Expected: compile error.
- [ ] **Step 3: Implement.** `ParagraphFlow.Emit` adds a record per line it emits through `PageBuilder.AddTextLine`; the planner puts its `Story` and the paragraph into `ParagraphInput.Source`; auxiliary planners of headers, footers and notes set their story.
- [ ] **Step 4: Run** the class, `LayoutGoldenTests`, `PageRendererTests`. Expected: pass.
- [ ] **Step 5: Commit** `feat(write): pages keep the text lines that map back to the model`.

---

### Task 6: Hit test, caret geometry and movement

**Files:**
- Create: `src/Raffica.Write.Layout/Pages/TextNavigator.cs`, `LayoutEdits.cs` (partial of `DocumentLayout`, API only in this task)
- Test: `tests/Raffica.Write.Tests/Layout/TextGeometryTests.cs`

**Interfaces:**
- Produces on `DocumentLayout` (public, UI-free, lengths in points):
  - `readonly record struct TextHit(int Page, TextPosition Position, bool Trailing)`; `TextHit? HitTest(int pageIndex, float x, float y)` (skips hyphen glyphs and list labels; atomic units snap to their edge; outside any line: the nearest line).
  - `readonly record struct CaretBox(int Page, float X, float Top, float Height)`; `CaretBox? Caret(TextPosition position, int pageHint)`.
  - `enum CaretMove { Left, Right, WordLeft, WordRight, LineStart, LineEnd, Up, Down, PageUp, PageDown, ParagraphStart, ParagraphEnd, DocumentStart, DocumentEnd }`; `TextPosition? Move(TextPosition from, CaretMove move, float? goalX, int pageHint)` (never leaves the story; `Up`/`Down` keep `goalX`).
  - `IReadOnlyList<TextBox> SelectionBoxes(TextRange range, int pageIndex)` with `readonly record struct TextBox(float X, float Y, float Width, float Height)`.

- [ ] **Step 1: Write the failing tests** in `TextGeometryTests` (thesis plus corpus files with cells, notes, headers):
  - `HitTestThenCaretRoundTrips`: 1,000 seeded points on random pages: `Caret(HitTest(p).Position)` has X within half a character width of the point's X when the point is on text, and the same line's Top.
  - `RightThenLeftIsIdentity` over the first 200 positions of three paragraphs, including a ligature word, "👍🏽" and a field result (steps over it as one unit).
  - `UpAndDownKeepTheGoalColumn`; `LineStartAndLineEndStayOnTheLine`; `WordRightFromHelloWorldGoesToSix` ("Hello world" offset 0 → 6); `MoveNeverLeavesTheStory` (Right at the end of a footnote stays there).
  - `SelectionBoxesCoverTwoLines` (one box per line, first starts at the start caret's X).
  - `HitTestIsFast`: 10,000 calls on the thesis, median per call < 50 µs (JIT build allowed to skip with a message; the bench of Task 20 is the binding one).
- [ ] **Step 2: Run** the class. Expected: compile error.
- [ ] **Step 3: Implement** `TextNavigator` over `LaidOutPage.TextLines` and `GlyphRun.CaretStops`/`CaretX`; `Caret` finds the page through a per-layout map from paragraph (reference comparer) to the pages that show it, filled as pages are published, with `pageHint` used first (headers appear on many pages).
- [ ] **Step 4: Run** the class. Expected: pass.
- [ ] **Step 5: Commit** `feat(write): hit test, caret geometry and caret movement in the layout`.

---

### Task 7: Plan cache keyed by block, and state snapshots

**Files:**
- Modify: `src/Raffica.Write.Layout/Pages/PageFiller.cs` (`SectionSource`), `BlockPlanner.cs`, `Styles/NumberingCounter.cs`, `Pages/NotePlanner.cs`, `Pages/FieldValues.cs`, `Pages/PlanContext`
- Test: `tests/Raffica.Write.Tests/Layout/StateSnapshotTests.cs`

**Interfaces:**
- Produces: `NumberingCounter.Snapshot()` returning an immutable `NumberingState` and `Restore(NumberingState)`; the same pair on `NotePlanner` (`NoteState`); `BlockPlanner.Forget(Block block)` and `BlockPlanner.PlanFor(Block block)` (plans keyed by `Block` reference, so an inserted or removed block shifts no cache entry); `FlatBlocks.From(...)` can be re-derived after a structural change.

- [ ] **Step 1: Write the failing tests** in `StateSnapshotTests`:
  - `NumberingRestoreRepeatsLabels`: plan 50 numbered paragraphs, snapshot after 20, plan 30 more, restore, plan the same 30 again → identical labels.
  - `NoteRestoreRepeatsNoteNumbers`: same for footnote numbers.
  - `ForgetReplansOnlyThatBlock`: after `Forget(b)` only `b` is planned again (count planner calls through a test seam).
  - `StructuralChangeKeepsOtherPlans`: inserting a block before `b` leaves `b`'s plan reachable and its `BodyIndex` re-derived.
- [ ] **Step 2: Run** the class. Expected: compile error.
- [ ] **Step 3: Implement.** Plans move from index-keyed memo to a `Dictionary<Block, BlockPlan>` with reference comparer; `BodyIndex` of a flow comes from the current `FlatBlocks`. Snapshots copy the counter dictionaries (small).
- [ ] **Step 4: Run** the class and the full `Raffica.Write.Tests` layout tests (`LayoutGoldenTests` unchanged). Expected: pass.
- [ ] **Step 5: Commit** `refactor(write): block plans keyed by block, snapshots of numbering and note state`.

---

### Task 8: A resumable page filler and `ApplyEdit`

**Files:**
- Create: `src/Raffica.Write.Layout/Pages/PageStart.cs`, `FillerState.cs`; `tests/Raffica.Write.Tests/Layout/LayoutOracle.cs`, `IncrementalLayoutTests.cs`
- Modify: `PageFiller.cs` (`Fill` becomes `FillFrom`), `PageList.cs`, `DocumentLayout.cs`/`LayoutEdits.cs`

**Interfaces:**
- Consumes: Tasks 2, 6, 7.
- Produces:
  - `readonly record struct PageStart(Block? Block, int Line, int SectionIndex)` per published page (`Block` null for a page that starts with a blank-page insertion), compared by block reference and line.
  - `FillerState`: the counters of `PageFiller.Fill` (`pageIndex`, `nextNumber`, `_sectionPages`, `_pageCount`, the revisable list), `NumberingState`, `NoteState`, saved at every page start.
  - `PageFiller.FillFrom(PageStart start, FillerState state)`: the old `Fill` is `FillFrom(first)`.
  - `PageList.Truncate(int count)` and `PageList.ReplaceRange(int first, LaidOutPage[] pages)`.
  - `public EditRefill DocumentLayout.ApplyEdit(BlockChange change, int throughPage)` with `readonly record struct EditRefill(int FirstPage, int LastPage, bool Restarted)`: synchronous, called with the edit gate held; lays out from the page whose start is at or before the edited block (walking back over keep-with-next) until the page start equals an old page's start and the saved state equals (then splices the old pages back and renumbers) or until `throughPage` is correct; pages beyond are truncated and continue through `LayOutRestAsync`.
  - `public IProjectionHost DocumentLayout.ProjectionHost(StoryRef story, Paragraph paragraph)` (the host the layout used for that paragraph: field values, note marks, hidden runs; the editing session passes it to `TextEditor`, Task 14, and the preedit of Task 18 goes through it).
  - Test helper `LayoutOracle.AssertEqualToFull(DocumentLayout incremental, WordDocument model)`: after `LayOutRestAsync` completed, a new `DocumentLayout` over the same model laid out fully must have the same page count and, per page, the same `Text`, `Number`, item count and item values (exact equality; provisional pages compared once complete).

- [ ] **Step 1: Write the failing tests** in `IncrementalLayoutTests` (thesis):
  - `IncrementalEqualsFullAfterTwentySeededInserts`: 20 seeded one-character and 400-character inserts into random paragraphs through a test-only `TestEdit.InsertText(paragraph, offset, text)` that changes the model and builds the `BlockChange`; after each `ApplyEdit(change, throughPage: page of the paragraph + 1)` and a finished `LayOutRestAsync`, `LayoutOracle.AssertEqualToFull`.
  - `RefillStopsWhereAPageStartedBefore`: a one-letter insert mid-chapter re-lays out ≤ 3 pages (test seam `PagesLaidOut`) and `Restarted` is false.
  - `PageThatMovesTheBreakRefillsFurther`: an insert that adds a line at a page end changes the next page's start and the refill continues until starts agree.
  - `ReadersSeeConsistentPages`: a reader thread reading `LaidOutPages` and `Page(i)` in a loop during 50 edits never throws and never sees a `LaidOutPage` whose index differs from its position.
- [ ] **Step 2: Run** the class. Expected: compile error / failures.
- [ ] **Step 3: Implement** `PageFiller` state at page starts, `FillFrom`, the stop rule of the spike (`spikes/write/layout/Layout.Common/PageBreaker.cs`, `Refill`: read for the algorithm, never copy code) and `ApplyEdit`; `Engine.Fill` stays the iterator for first-time layout and is built over `FillFrom`. `LaidOutPages` may now decrease on truncation: readers keep reading `Page(i)` per frame, never keep a page.
- [ ] **Step 4: Run** the class, then the whole layout tests. Expected: pass with `LayoutGoldenTests` unchanged.
- [ ] **Step 5: Commit** `feat(write): resumable page filler and incremental re-layout of an edit`.

---

### Task 9: Fallbacks, headers and notes, and the corpus oracle

**Files:**
- Modify: `HeaderFooterPlan.cs`, `NotePlanner.cs`, `LayoutEdits.cs`, `PageFiller.cs`
- Test: `tests/Raffica.Write.Tests/Layout/CorpusIncrementalTests.cs`

**Interfaces:**
- Consumes: Task 8.
- Produces: `HeaderFooterPlan.Invalidate(StoryRef part)`; `NotePlanner.Invalidate(string noteId)`; `public EditStats DocumentLayout.Stats` (`readonly record struct EditStats(int Edits, int Restarts)`); `EditRefill.Restarted` true when the refill restarted further back than the stop rule alone (a section break, a header or footer change, a changed page count, a carried float or note, a changed numbering state).

- [ ] **Step 1: Write the failing tests** in `CorpusIncrementalTests`:
  - `EveryCorpusFileStaysEqualToFullLayout`: for every corpus file and both modes, 25 seeded edits per file (inserts and deletes of text, paragraph splits and merges, edits inside cells, footnote text and header text where the file has them, half of them long enough to move page breaks); after each, `LayoutOracle.AssertEqualToFull`. One case per file (theory-style) so a failure names the file.
  - `HeaderEditRelaysEveryPageThatShowsIt`: editing the header text of the thesis changes it on every page; the edited page is correct after `ApplyEdit(..., throughPage: 3)` and the rest after the background pass; `Restarted` is true.
  - `FooterWithNumPagesIsNotAFallbackByItself`: an insert that keeps the page count leaves `Restarted` false and `IsProvisional` pages unchanged in count.
  - `SectionBreakEditRestarts`: an edit in the paragraph that carries a section break reports `Restarted`.
  - `FallbackStillShowsTheEditedPage`: with a forced restart, pages through `throughPage` are correct when `ApplyEdit` returns.
- [ ] **Step 2: Run** the class. Expected: failures (nothing invalidates chrome or notes yet).
- [ ] **Step 3: Implement** the invalidation and the fallback rules of spec section 6.3, and `Stats`. If exact equality proves impossible for positions, compare to 0.01 pt for positions only and record why in the Task 21 decision; never loosen the page count or text comparison.
- [ ] **Step 4: Run** the class and the full `Raffica.Write.Tests`. Expected: pass. **This is the gate of the layout half; do not start Task 14 before it is green.**
- [ ] **Step 5: Commit** `test(write): incremental layout equals full layout over the corpus`.

---

### Task 10: The editing library and its operations

**Files:**
- Create: `src/Raffica.Write.Editing/Raffica.Write.Editing.csproj` (references Model only), `TextEditor.cs`, `EditResult.cs`; `tests/Raffica.Write.Tests/Editing/TextEditorTests.cs`
- Modify: `Raffica.slnx`, `tests/Raffica.Write.Tests/ProjectGuardTests.cs`, `tests/Raffica.Write.Tests/Raffica.Write.Tests.csproj`

**Interfaces:**
- Consumes: Tasks 1, 2.
- Produces:
  - `sealed record EditResult(TextPosition Caret, IReadOnlyList<BlockChange> Changes)`.
  - `sealed class TextEditor(WordDocument document, Func<StoryRef, Paragraph, IProjectionHost> hosts)` with `EditResult InsertText(TextRange selection, string text)` (replaces the selection; `\n`, `\r\n`, `\r` split paragraphs, `\t` becomes a tab, other control characters are dropped), `EditResult Delete(TextRange selection)`, `EditResult? Backspace(TextPosition caret, DeleteUnit unit)`, `EditResult? DeleteForward(TextPosition caret, DeleteUnit unit)` with `enum DeleteUnit { Cluster, Word }`, `EditResult SplitParagraph(TextRange selection)`; each raises `document.Edits`.
  - Rules in this task: new text joins the run before the caret (the run after it at a paragraph start); a delete is by grapheme cluster; merging paragraphs: the first paragraph's properties win unless it is empty; nothing merges across a table cell, a table or a story boundary (the operation returns null or deletes within the boundary).

- [ ] **Step 1: Write the failing tests** in `TextEditorTests` (plus the guard line):
  - `InsertAtStartMiddleEnd`, `InsertReplacesSelection`, `InsertJoinsThePreviousRunAndKeepsItsProperties`, `EnterSplitsAndKeepsParagraphProperties`.
  - `BackspaceAtParagraphStartMergesWithTheFirstParagraphProperties`, `BackspaceInAnEmptyFirstParagraphTakesTheSecondProperties`.
  - Review Focus 1: `BackspaceRemovesWholeEmojiAndCombiningCluster` ("👍🏽", "é").
  - Review Focus 2: `BackspaceAtStartOfCellDoesNothing`, `BackspaceAfterATableKeepsTheTable`, `BackspaceInEmptyCellAndEmptyHeaderAndSingleEmptyParagraphDocumentDoNotThrow`.
  - Review Focus 3 (the operation part): `FiveMegabytePlainTextInsertsWithinOneSecond` (assert time < 1 s and the paragraph count), `CrLfCrAndLfSplitOnce`, `ControlCharactersAreDropped`, `TabBecomesTabContent`.
  - `EveryOperationRaisesTheRevisionOnce` and `ChangesDescribeTheReplacedBlocks` (`OldBlocks` are clones of the blocks before).
  - Guard: `EditingReferencesOnlyModel` in `ProjectGuardTests`.
- [ ] **Step 2: Run** the class. Expected: compile error.
- [ ] **Step 3: Implement** the project and operations; the paragraph text model is `ParagraphProjection` with the host from `hosts`; edits change `TextContent.Text` or split/merge the run list and paragraph, nothing else.
- [ ] **Step 4: Run** the class and `ProjectGuardTests`. Expected: pass.
- [ ] **Step 5: Commit** `feat(write): editing library with text operations`.

---

### Task 11: Editing rules

**Files:**
- Create: `src/Raffica.Write.Editing/EditRules.cs`
- Modify: `TextEditor.cs`
- Test: `tests/Raffica.Write.Tests/Editing/EditRulesTests.cs`

**Interfaces:**
- Consumes: Task 10.
- Produces: `EditResult` gains `TextRange? Selected` (the unit a first Backspace or Delete selects instead of deleting: the result of the field-unit rule).

- [ ] **Step 1: Write the failing tests** in `EditRulesTests`:
  - `FirstBackspaceNextToAFieldSelectsItSecondRemovesIt` (and the same for Delete forward); `TypingInsideAFieldResultInsertsBesideIt`.
  - `PictureAndNoteReferenceAreDeletedAsOneUnit`; `CommentReferenceSurvivesASelectionDelete` and `CommentStartAndEndMarkersCollapseAtTheDeletionPoint`.
  - `BookmarkMarkersMoveToTheNearestRemainingPosition`.
  - `TypingInsideATrackedInsertionEditsItsRunAndIsNotTracked` (no new `TrackedChange` appears).
  - `ContentControlKeepsItsElementWhenItsTextIsEdited` and `UnknownContentStaysInPlaceAndShared` (same `UnknownContent` instance after an edit around it).
- [ ] **Step 2: Run** the class. Expected: failures.
- [ ] **Step 3: Implement** the rules of spec section 4 inside `TextEditor` through `EditRules` helpers.
- [ ] **Step 4: Run** the class and `TextEditorTests`. Expected: pass.
- [ ] **Step 5: Commit** `feat(write): editing rules for fields, anchors and kept content`.

---

### Task 12: History and dirty state

**Files:**
- Create: `src/Raffica.Write.Editing/EditHistory.cs`; `tests/Raffica.Write.Tests/Editing/EditHistoryTests.cs`
- Modify: `TextEditor.cs`

**Interfaces:**
- Consumes: Tasks 2, 10.
- Produces: `sealed record HistoryLimits(int MaxSteps, long MaxBytes)` (defaults chosen and recorded by the builder after measuring on the thesis); `sealed class EditHistory(WordDocument document, TimeProvider time, HistoryLimits limits)` with `bool CanUndo`, `bool CanRedo`, `EditResult? Undo()`, `EditResult? Redo()`; `TextEditor.History`; each step records `Story`, the caret before and after, `OldBlocks` clones, the new blocks and the revision it started from; `Undo` restores the document's revision (so undoing to the saved state is not dirty).

- [ ] **Step 1: Write the failing tests** in `EditHistoryTests` (fake `TimeProvider`):
  - `TypingCoalescesIntoOneStepPerWordOrPause` (letters of one word: one step; a space starts the next; a 2 s pause splits).
  - `UndoRestoresTextAndCaretInItsStory` (edit in the body, then in a footnote: first undo returns the caret to the footnote, second to the body; one linear history).
  - `RedoIsClearedByANewEdit`; `UndoToTheSavedRevisionIsNotDirty` (`MarkSaved`, type, undo → `IsDirty` false).
  - `SelectAllDeleteIsOneStepAndUndoRestoresEveryBlock`; `NewestStepIsKeptEvenOverTheByteLimit` and `OldestStepsGoFirst`.
  - `StepsShareUnknownContent` (snapshot memory counts text and property bags only).
  - `CutIsOneStep` (copy-then-delete helper of Task 13 is not needed: delete + record).
- [ ] **Step 2: Run** the class. Expected: compile error.
- [ ] **Step 3: Implement** the history: steps are the `BlockChange` of an operation (clones before, clones after on undo); coalescing only merges consecutive single-position inserts in one paragraph.
- [ ] **Step 4: Run** the class and the editing tests. Expected: pass.
- [ ] **Step 5: Commit** `feat(write): undo and redo history with dirty state`.

---

### Task 13: Clipboard fragments

**Files:**
- Create: `src/Raffica.Write.Docx/Fragments/DocxFragment.cs`, `src/Raffica.Write.Editing/ClipboardContent.cs`; `tests/Raffica.Write.Tests/Editing/ClipboardTests.cs`, `tests/Raffica.Write.Tests/DocxFragmentTests.cs`
- Modify: `TextEditor.cs`

**Interfaces:**
- Consumes: Tasks 10, 12.
- Produces:
  - `static class DocxFragment { byte[] Write(IReadOnlyList<Paragraph> paragraphs); IReadOnlyList<Paragraph> Read(ReadOnlySpan<byte> bytes); }` (WordprocessingML paragraphs with their direct formatting; the same codecs as the document parts).
  - `sealed record ClipboardContent(string PlainText, byte[]? Fragment)`; `ClipboardContent TextEditor.Copy(TextRange range)`; `EditResult TextEditor.Paste(TextRange selection, ClipboardContent content, Func<string, bool> styleExists)` (the private fragment is used when present: same-document paste keeps everything; another document keeps direct formatting and maps a style id only where `styleExists`, else the target's default; a table, picture, note reference or field in the fragment pastes as its text; absent fragment: plain text as `InsertText`); `Cut` is `Copy` plus `Delete` as one history step.
  - Format ids: `text/plain` and `application/x-raffica-write-fragment`.

- [ ] **Step 1: Write the failing tests:**
  - `DocxFragmentTests.RoundTripsParagraphsWithDirectFormatting` (bold run, centered paragraph, unknown run content survive `Write`→`Read`).
  - `ClipboardTests.CopyPasteKeepsRunAndParagraphFormatting`, `PasteIntoAnotherDocumentMapsOnlyExistingStyles`, `FragmentWithTableAndPictureAndFieldPastesAsText`, `CutIsOneHistoryStep`, `PlainTextOutsideWriteBecomesParagraphsAndTabs`.
  - Review Focus 3: `FiveMegabyteFragmentlessPasteIsOneStepAndBounded` (history bytes and time).
- [ ] **Step 2: Run** the classes. Expected: compile error.
- [ ] **Step 3: Implement** the fragment codec on the existing part readers and writers (raw XML, no SDK DOM) and `Copy`/`Paste`.
- [ ] **Step 4: Run** the classes. Expected: pass.
- [ ] **Step 5: Commit** `feat(write): clipboard fragments and paste rules`.

---

### Task 14: The edit gate and the editing session

**Files:**
- Create: `src/Raffica.Write/Editing/EditLease.cs`, `EditSession.cs`; `tests/Raffica.Write.App.Tests/Editing/EditSessionTests.cs`
- Modify: `Viewing/LayoutRunner.cs`, `Documents/WriteTab.cs`

**Interfaces:**
- Consumes: Tasks 8, 9, 10–13.
- Produces:
  - `LayoutRunner.PauseForEdit(): EditLease` (`IDisposable`): cancels the background layout, blocks the UI thread until the pool task is idle (it stops at the end of the page it is on), and on dispose continues `LayOutRestAsync`. One lease at a time; a second request waits.
  - `LayoutRunner.DropFlowLayout()`: the writing view's layout is dropped when the pages layout is the active one (and the reverse), made again by `EnsureFlow` on a switch.
  - `sealed class EditSession(WriteTab tab)` (made on first use, `WriteTab.Editor`): `bool CanEdit` (full model ready, not faulted, no save running; false with a `EditBlockedReason` for the status hint: `StillOpening`, `Saving`, `Faulted`), `TextPosition Caret`, `TextRange Selection`, `EditResult? Apply(Func<TextEditor, EditResult?> operation, int throughPage)` (takes the lease, runs the operation, calls `DocumentLayout.ApplyEdit` for the active layout, drops the other layout, raises `Changed`), `event Action Changed`, and the queue for input that arrives during a save (replayed in arrival order).

- [ ] **Step 1: Write the failing tests** in `EditSessionTests` (headless, `WindowFixture`):
  - `NoPageIsLaidOutWhileLeased` (hold a lease on the thesis during background layout; `LaidOutPages` does not grow until disposed).
  - `EditRefusedWhileOnlyAPrefixIsOpen` (`CanEdit` false, reason `StillOpening`; model unchanged).
  - `EditUpdatesTheActiveLayoutAndDropsTheOther` (flow layout is dropped after an edit in the page view and rebuilt on the switch with the edited text).
  - `InputDuringASaveIsQueuedAndReplayedInOrder` (type "abc" during a simulated save; after it, "abc" is in the model once, in order).
  - `StartupCreatesNoEditSession` (the `WriteTab` has none until the first caret or key).
- [ ] **Step 2: Run** the class. Expected: compile error.
- [ ] **Step 3: Implement** the lease with a `SemaphoreSlim` and the runner's `PageTask` handle; `EditSession` owns the `TextEditor` (hosts from the layout's `LayoutProjectionHost` factory) and the selection.
- [ ] **Step 4: Run** the class and the existing `LayoutRunner` tests. Expected: pass.
- [ ] **Step 5: Commit** `feat(write): edit gate and editing session`.

---

### Task 15: Caret, selection and pointer in the page view

**Files:**
- Create: `src/Raffica.Write/Viewing/PageView.Editing.cs`; `tests/Raffica.Write.App.Tests/Viewing/PageViewEditingTests.cs`
- Modify: `Viewing/PageView.cs` (`OnPointerPressed`, `Render`), `Viewing/ViewAnchor.cs`, `Viewing/PageScroller.cs`

**Interfaces:**
- Consumes: Tasks 6, 14.
- Produces: `PageView.Caret` and `Selection` (read from the session), caret blink timer created on first caret, selection highlight drawn from `SelectionBoxes`, pointer rules (click, drag with auto-scroll, double click a word, triple click a paragraph, Shift+click extends; a drag that leaves a story or a cell stops at its edge), `PageView.EnsureCaretVisible()`, `ViewAnchor(int Block, int Character)` with a real character offset used by the view switch.

- [ ] **Step 1: Write the failing tests** (headless, thesis in a window):
  - `ClickPlacesTheCaretWhereTheHitTestSays`; `DragSelectsAndStopsAtTheCellEdge`; `DoubleClickSelectsAWordTripleAParagraph`; `ShiftClickExtends`.
  - `CaretIsKeptVisibleByScrolling` (move the caret 3 pages down, the view follows); `RenderTouchesNoVisual` (the existing rule: `Render` reads pages and invalidates nothing); `SwitchingViewKeepsTheCaretParagraph` (the anchor's character offset survives page view → writing view → page view).
  - `NoBlinkTimerBeforeTheFirstCaret`.
- [ ] **Step 2: Run** the class. Expected: failures.
- [ ] **Step 3: Implement.** Caret and selection are drawn by Avalonia over the Skia draw operation (not inside it); the colours use existing tokens (`Raffica.Brush.*`), a new token only with the contrast policy entry and `TokenValuesTests`, `Tokens.axaml` (Light and Dark), `Tokens.HighContrast.axaml` and the gallery list.
- [ ] **Step 4: Run** the class and `PageViewTests`. Expected: pass.
- [ ] **Step 5: Commit** `feat(write): caret, selection and pointer in the page view`.

---

### Task 16: Keyboard and typing

**Files:**
- Create: `src/Raffica.Write/Editing/EditingKeys.cs`; `tests/Raffica.Write.App.Tests/Editing/EditingKeysTests.cs`, `TypingTests.cs`
- Modify: `Viewing/PageView.Editing.cs` (`OnKeyDown`, `OnTextInput`)

**Interfaces:**
- Consumes: Tasks 6, 14, 15.
- Produces: `static class EditingKeys { EditAction? For(Key key, KeyModifiers modifiers, bool macOs); bool IsTextInput(KeyEventArgs e, string? text) }` mapping keys to `CaretMove`, delete units and the command ids of Task 17: Ctrl (⌘ on macOS) with arrows for words and lines (Option+arrow words, ⌘+arrow lines on macOS), Home/End, PageUp/PageDown, Ctrl+Z, Ctrl+Y and Ctrl+Shift+Z, Ctrl+A, Backspace and Delete with Ctrl for words; macOS keys checked against `ReservedShortcuts`.

- [ ] **Step 1: Write the failing tests:**
  - `EditingKeysTests`: a table of (key, modifiers, macOs) → action, including ⌘Z/⇧⌘Z on macOS and Option+Left word move; `ReservedShortcuts` audit passes for every macOS entry.
  - `TypingTests`: `TypingInsertsAtTheCaretAndAdvancesIt`, `EnterBackspaceDelete`, `ArrowKeysMoveAndShiftSelects`, `TypingReplacesTheSelection`.
  - `AltGrIsTextNotACommand`: a `Ctrl+Alt+Q` key event whose text is "@" inserts "@" and does not open the command search (Alt+Q alone still does).
  - `KeysAreRepeatSafe`: held Backspace deletes one cluster per repeat event.
  - `TypingBeforeTheFullModelShowsTheStillOpeningHint`.
- [ ] **Step 2: Run** the classes. Expected: compile error.
- [ ] **Step 3: Implement.** The window's key capture forwards Page and Home keys only when the focus is not in the outline list (existing `InOutline` rule); text arrives through `OnTextInput`, never from `Key`.
- [ ] **Step 4: Run** the classes and `WriteWindowTests`. Expected: pass.
- [ ] **Step 5: Commit** `feat(write): keyboard navigation and typing`.

---

### Task 17: Clipboard and edit commands

**Files:**
- Modify: `Commands/WriteCommandIds.cs`, `WriteCommandCatalog.cs`, `WriteCommandRules.cs`, `WriteRibbon.cs`, `Platform/MacOS/WriteMacMenu.cs`, `Resources/Strings.en.resx`, `Strings.de.resx`, the shell icon set (`src/Raffica.Shell/Styles/Icons.axaml`), the help list; `Viewing/PageView.Editing.cs`
- Test: `tests/Raffica.Write.App.Tests/Commands/EditCommandTests.cs`

**Interfaces:**
- Consumes: Tasks 12, 13, 14, 16.
- Produces: command ids `edit.undo`, `edit.redo`, `edit.cut`, `edit.copy`, `edit.paste`, `edit.selectAll` (Home tab, groups "Undo" and "Clipboard", field codes within the frame's rules) and the menu entries in the macOS Edit menu; the clipboard writes `text/plain` plus `application/x-raffica-write-fragment` and reads the fragment first, then text.

- [ ] **Step 1: Write the failing tests** in `EditCommandTests`:
  - `EveryEditCommandHasAnIconAndBothStrings` (the icon is its own key, `CommandIconTests` style).
  - `WriteCommandRulesGivesAReasonForEveryDisabledState` (walk every id in every state: no document, prefix only, nothing selected for cut and copy, empty history, empty clipboard).
  - `CopyThenPasteInsideWriteKeepsFormatting`; `CopyPlacesPlainTextOnTheSystemClipboard`; `PasteFromOutsideIsPlainText`; `UndoRedoCommandsRestoreTextAndSelection`.
  - `MacEditMenuHasUndoCutCopyPasteSelectAll` and `EditShortcutsAreNotReserved`.
  - `TranslationTests` and `GermanRegisterTests` pass with the new strings.
- [ ] **Step 2: Run** the class. Expected: failures.
- [ ] **Step 3: Implement** the commands with their reasons, icons (Lucide: undo-2, redo-2, scissors, copy, clipboard-paste, text-select) and German strings in the "Sie" form.
- [ ] **Step 4: Run** the class, `TranslationTests`, `WriteWindowTests`, `Raffica.Shell.Tests` (token and icon guards). Expected: pass.
- [ ] **Step 5: Commit** `feat(write): clipboard and undo commands`.

---

### Task 18: Input method

**Files:**
- Create: `src/Raffica.Write/Editing/WriteImeClient.cs`; `tests/Raffica.Write.App.Tests/Editing/ImeTests.cs`; `Docs/write-ime-check.md`
- Modify: `Viewing/PageView.Editing.cs`, `src/Raffica.Write.Layout/Pages/LayoutEdits.cs`, `LayoutProjectionHost.cs`

**Interfaces:**
- Consumes: Tasks 6, 14, 16.
- Produces: `PageView` answers `TextInputMethodClientRequested` with `WriteImeClient` (`SupportsPreedit`, `SupportsSurroundingText`, `SurroundingText` = the paragraph around the caret, `CursorRectangle` from `Caret(...)` in control coordinates, `Selection`, both `SetPreeditText` overloads, `RaiseCursorRectangleChanged` after every change including scroll and zoom); `DocumentLayout.SetPreedit(TextPosition at, string? text)` (the preedit is a transient piece of the paragraph's projection with an underline, never in the model); Write draws its own preedit caret at the reported cursor (end of the preedit when none); keys belong to the IME while a preedit exists; on commit the text goes through `InsertText`. With `RAFFICA_WRITE_IME_LOG=<path>` the client appends `preedit len=<n>`, `commit len=<n>` and `cancel` lines (lengths only, never text).

- [ ] **Step 1: Write the failing tests** in `ImeTests` (a test client calling the members like the platform):
  - `PreeditNeverTouchesTheModelTheHistoryOrTheRevision`.
  - `CommitInsertsOnceAndClearsThePreedit`; `CommitIsOneHistoryStepWithTypingCoalescing`.
  - Review Focus 5: `CursorRectangleFollowsTheCaretAfterScrollAndZoom`; `LostFocusClearsThePreeditWithoutWriting`; `EscCancelLeavesNoResidue` (model, history length and dirty state as before the composition).
  - `KeysAreIgnoredWhileComposing`; `PreeditCaretIsDrawnAtTheReportedCursor`; `SurroundingTextIsTheParagraph`.
  - `ImeLogHasLengthsOnly` (no composed text in the log file).
  - `NoImeClientBeforeTheFirstFocus` (startup guard).
- [ ] **Step 2: Run** the class. Expected: compile error.
- [ ] **Step 3: Implement** the client after `spikes/write/ime/ComposeBox.cs` (read, rewrite with tests); the transient preedit runs through `ApplyEdit` as a change that is not counted in `Edits`. Write `Docs/write-ime-check.md`: the owner's steps per system (Windows Japanese and Pinyin; Linux Wayland with Fcitx5, IBus, an X11 session; macOS Japanese and Chinese with the commit line in the log; a German layout typing `@`, `z`, `y`; the candidate window at the caret in a page of text), each with a result table to fill in.
- [ ] **Step 4: Run** the class and `PageViewEditingTests`. Expected: pass.
- [ ] **Step 5: Commit** `feat(write): input method client with Write's own preedit`.

---

### Task 19: Save, Save as and the close prompt

**Files:**
- Create: `src/Raffica.Write/Documents/WriteSaveFlow.cs`, `CloseGuard.cs`, `src/Raffica.Write/Dialogs/UnsavedChangesCard.cs`, `ChangedOriginalCard.cs`, `SaveErrorCard.cs`; `tests/Raffica.Write.App.Tests/Documents/SaveFlowTests.cs`, `CloseGuardTests.cs`
- Modify: `src/Raffica.Write.Docx/DocxFile.cs`, `src/Raffica.Core/Saving/AtomicFile.cs`, `WriteWindow.axaml.cs` (`OnWindowClosing`), `WriteTab.cs`, `WriteCommandIds/Catalog/Rules`, `WriteRibbon`, `WriteMacMenu`, both `Strings.*.resx`, `tests/Raffica.Write.Tests/DocxSaveTests.cs`

**Interfaces:**
- Consumes: Tasks 2, 12, 14.
- Produces:
  - `SaveFailureReason.TargetAppeared`; `DocxFile.SaveAsAsync(string path, bool replaceExisting, CancellationToken token)` (false: refuses with `TargetAppeared` when a file exists at the target at write time); `AtomicFile.MoveIntoPlace` reports a discarded `.pw-*.old` leftover, `DocxFile` surfaces it in a `SaveReport` and Save as shows a one-line note (decision: the 4.1 limit (a), "expose it before Save As").
  - `WriteSaveFlow`: `Task<SaveOutcome> SaveAsync(WriteTab)`, `SaveAsAsync(WriteTab)` (system picker through `IStorageProvider`; the picker confirms an overwrite, so `replaceExisting` is true only for an existing path the user chose); a save takes the edit lease; success calls `document.Edits.MarkSaved()` and records the new file stamp; failure keeps the document dirty and the history. Commands `file.save`, `file.saveAs` with icons, reasons (no document, prefix only, nothing changed for Save), ribbon and macOS menu entries; the tab shows a dirty mark (an accessible name too).
  - `ChangedOriginalCard` (Overwrite, Save as, Cancel; Overwrite passes `overwriteChangedOriginal`), `SaveErrorCard` (every `SaveFailureReason` and `DocxSaveException` reason worded in both languages, guard test as `PageLossWordingTests`), `UnsavedChangesCard` (Save, Don't save, Cancel).
  - `CloseGuard`: asked on window close, tab close (`file.close`), quit (⌘Q through `WindowHost`) for every dirty tab, answers in one place; `WriteWindow.OnWindowClosing` no longer confirms at once. Write has no update flow before 4.9, so the update question of the spec does not exist yet; record this in the Task 21 decision.

- [ ] **Step 1: Write the failing tests:**
  - `DocxSaveTests.SaveAsRefusesATargetThatAppearedAfterThePicker`; `DocxSaveTests.SaveAsReportsADiscardedLeftover` (Windows-only skip elsewhere, like the Unix-mode test).
  - `SaveFlowTests`: `SaveWritesTheEditedTextAndClearsDirty` (saved file read again holds the edit); `UndoBackToSavedIsNotDirty`; `SavingTakesTheEditLease`.
  - Review Focus 4: `ReadOnlyTargetKeepsDocumentDirtyAndShowsTheErrorCard`, `ChangedOnDiskAsksOverwriteSaveAsCancel` (each answer), `TargetAppearedAfterThePickerKeepsDirty`; history length unchanged and the file bytes untouched in each.
  - `CloseGuardTests`: `DirtyWindowCloseAsksAndEachAnswerBehaves` (Save saves then closes, Don't save closes, Cancel stays), `TwoDirtyTabsAreAskedInOrder`, `QuitAsksThroughTheHost`, `CleanWindowClosesAtOnce`, `CardsAreBuiltByCardFooter` (list the three cards in the Write card guard, create `tests/Raffica.Write.App.Tests/Dialogs/CardFooterTests.cs` modelled on the PDF one if none exists).
  - `TranslationTests` pass with the new strings.
- [ ] **Step 2: Run** the classes. Expected: compile error / failures.
- [ ] **Step 3: Implement** the pieces above; `AtomicFile` change is checked against the PDF save tests (`*Saving*` in `Raffica.Core.Tests`, `SaveControllerTests` in `Raffica.Pdf.Tests`) which must stay green.
- [ ] **Step 4: Run** the new classes, then `dotnet test tests/Raffica.Core.Tests -c Release --filter-class "*Saving*"` and `tests/Raffica.Pdf.Tests` `SaveControllerTests`. Expected: pass.
- [ ] **Step 5: Commit** `feat(write): save, save as and the close prompt`.

---

### Task 20: Measuring typing

**Files:**
- Create: `bench/write-layout/Edit.cs`
- Modify: `bench/write-layout/Program.cs`, `src/Raffica.Write/Measurement.cs`, `bench/shell-probe/measure.cs` (cases, `CheckCommand` `s_writeBudgets`, `selftest`), `.github/workflows/measure.yml` (target `write` runs the new case), `tests/Raffica.Write.App.Tests/MeasurementTests.cs`

**Interfaces:**
- Consumes: Tasks 8, 9, 14–16.
- Produces: `write-layout-bench edit --file <docx> [--runs n --csv]`: seeded keystrokes on the thesis in the middle of a long chapter, in a paragraph whose growth moves a page break, in the paragraph before a section break, at the end of a chapter, in a cell, in a footnote and in a header, each in the page view and the writing view; prints per case median, 95th percentile, restarts (`EditStats`) and the hit test per call; refuses a JIT build as `run` does. App switch `--measure-typing` (needs `--settings-file` and a file, one per run) injects the same cases through the key path and measures from the key event to the frame that shows the edited page(s); `measure.cs --target write` case `typing` and a `typing` limit of 16 ms in `s_writeBudgets` (judged on every platform that has an app row; pinned in `selftest`).

- [ ] **Step 1: Write the failing tests:** `MeasurementTests.TypingMeasurementNeedsSettingsFileAndFile`, `TypingMeasurementWritesOneRowPerCase`, and a `measure.cs selftest` assertion that `s_writeBudgets` holds `typing` = 16 ms and that a typing row above 16 ms is `FAIL` (run `dotnet run bench/shell-probe/measure.cs -- selftest`).
- [ ] **Step 2: Run** them. Expected: failures.
- [ ] **Step 3: Implement** the bench case, the app switch and the budget wiring. The first edit after open and after a view switch are separate rows, reported without a limit.
- [ ] **Step 4: Run** the tests and `selftest` (expected exit 0); then a Native AOT publish of `bench/write-layout` and `write-layout-bench edit --file testdata/write-corpus/thesis/thesis.docx` on this machine; record median and p95 per case in the Task 21 decision (limit 16 ms; the engine median is expected near the spike's 0.26 ms).
- [ ] **Step 5: Commit** `feat(write): typing measurement in the bench and the app`.

---

### Task 21: Guards, docs and the proof

**Files:**
- Create: `tests/Raffica.Write.App.Tests/Editing/StartupTests.cs`
- Modify: `Docs/DECISIONS.md`, `CLAUDE.md` (section "Write editing (M4.3a)"), `Docs/roadmap.json` and `Docs/ROADMAP.md` (generated), `.github/workflows/*` only if the new project needs a publish step, `tests/Raffica.Write.Tests/ProjectGuardTests.cs`

- [ ] **Step 1: Write the failing tests:** `StartupCreatesNoEditorWork` (opening the thesis and scrolling creates no `EditSession`, `TextEditor`, `EditHistory`, clipboard access or IME client), and `LayoutStillReferencesNoAvalonia` plus `AppStillReferencesNoPdfAssembly` in the guards.
- [ ] **Step 2: Run** them. Expected: pass or the one failing guard is fixed in its owner task.
- [ ] **Step 3: Write the docs.** `Docs/DECISIONS.md`: entry "Raffica Write: the editing engine (M4.3a)" (decision, measured numbers from Task 20, the exactness of the oracle, history limits chosen, what moved to 4.3b and 4.3c, the Write update-flow note of Task 19, known limits, the owner gates); `CLAUDE.md`: a section "Write editing (M4.3a)" in the style of the neighbouring ones (where, rules to remember, pitfalls found); `Docs/roadmap.json`: tick every finished task of 4.3a with evidence and the criteria that were checked (typing and incremental equality: command and result; the IME criterion stays open with `Docs/write-ime-check.md`), list the owner gates (IME on three systems incl. IBus and X11, typing on the Linux laptop and the Intel Mac, a look at the AOT build) as open items, keep status `in-progress`; `dotnet run tools/roadmap.cs -- generate`.
- [ ] **Step 4: Verify everything:** `dotnet build Raffica.slnx -c Release`; `dotnet run tools/native-fetch.cs -- --rid win-x64 --out artifacts/native/win-x64` then `RAFFICA_REQUIRE_OCR=1 dotnet test Raffica.slnx -c Release --no-build --max-parallel-test-modules 1`; `dotnet format Raffica.slnx --verify-no-changes`; `dotnet run tools/roadmap.cs -- check`; Native AOT publish of `src/Raffica.Write` and `bench/write-layout` with the log searched for `IL[23][0-9]{3}` (0 matches). Expected: all green; anything not run (Linux, macOS, real IME) is listed as open with its command.
- [ ] **Step 5: Commit** `docs(write): decision, project notes and roadmap for milestone 4.3a`; push the branch, open the pull request to `dev`, and merge it once all CI checks are green (CLAUDE.md "Git & branches").
