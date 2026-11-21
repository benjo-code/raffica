# Raffica Write 4.4a: Headings, Lists and the Outline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A writer can number the headings in one command, make and level lists, and move, promote and demote chapters in the outline pane, and no number on the page is ever stale.

**Architecture:** Heading numbering is a style edit (Heading 1 to 9 carry `numPr`; the numbering part gets a definition and a `w:num` when the document has none), applied as one history step that carries several styles. Lists are paragraph changes (`numPr` per paragraph) on the document's own definitions. A chapter move is a history step of its own that stores indices and swaps the same block objects; the layout is told the spanned range through the existing `BlockChange` path. The UI-free parts live in `Raffica.Write.Editing`; the commands, keys and the pane are in `Raffica.Write`.

**Tech Stack:** .NET 10 Native AOT, Avalonia 12, Open XML SDK 3.x (package layer only), xunit v3 on Microsoft Testing Platform.

**Spec:** `Docs/superpowers/specs/2026-11-21-raffica-write-4-4a-headings-lists-and-outline-design.md` (approved 2026-11-21, with "Settled after the builder check"). Subproject spec: `Docs/superpowers/specs/2026-11-13-raffica-write-design.md` (section 6 "Structure"). Branch: `feature/m04-4a-headings-lists-and-outline`. Read the sections "Write library (M4.1)", "Write editing (M4.3a)", "Write formatting (M4.3b)" and "Write recovery (M4.3c)" of `CLAUDE.md` first: the rules there (lease, attribution, oracle, start cost, Home band width hint) bind every task.

## Global Constraints

- .NET 10 Native AOT, English source, German localization in the "Sie" form; every new command has its own Lucide icon, a reason in every disabled state (`WriteCommandRules`), strings in both `.resx` files (LF), and a line in `WriteMacMenu`; no warning suppressed; `dotnet format Raffica.slnx --verify-no-changes` clean. The Write tool makes CRLF files: convert to LF before each commit.
- Heading numbering is **linked to the styles**: `w:numPr` in the paragraph properties of the styles `Heading1` to `Heading9` (found by built-in id and name `heading 1` to `heading 9`), `w:pStyle` on each level of the definition; `ilvl` in the style from level 2 on; switching off removes the style `numPr` and leaves the definition. The whole switch is one undo step and goes through `DocumentLayout.Restyle`.
- A list command acts like paragraph formatting: one `BlockChange` per operation through the lease and `EditSession.Apply`; `EditResult.KeepSelection` as for formatting.
- The list used: a `w:num` whose abstract definition has no `w:pStyle` on any level and whose level 0 is a bullet or a number; the one the List Bullet or List Number style points at wins, else the first by position; else Write's own (copy of the blank document's bullet and numbered definitions). A toggle reuses a `w:num` this editor created that nothing references; restart always adds one with `w:startOverride` 1. Undo never removes numbering definitions; no clean-up of unused instances.
- A paragraph in the Normal style also gets List Paragraph when it becomes a list item; any other style is kept.
- Keys: Tab and Shift+Tab change the level only at the start of a list paragraph or over a selection that holds list paragraphs; Enter on an empty list paragraph ends the list; Backspace at the start of a list paragraph removes the list first. Everything else stays as today.
- The subtree of a heading: the top-level body blocks from the heading up to, not including, the next listed heading of the same or a higher level (or the end of the body). Body only. A subtree that holds a paragraph with a section break, a heading inside a block content control, and a heading in a table are not movable. A drop inside the subtree does nothing. After a move the caret is at the start of the moved heading and its row stays selected.
- Promote and demote change the heading and every heading in its subtree by one level, through the style `HeadingN` or a direct `w:outlineLvl`, as one `BlockChange` over the range from the heading to the last heading of its subtree; any other level source, or a result outside levels 1 to 9, disables the command with a reason.
- Keys: Alt+Shift with Up and Down move, with Left and Right promote and demote (Windows and Linux; never Ctrl+Alt, AltGr stays text); macOS takes a key only if the `ReservedShortcuts` audit has a free one. Commands: `structure.numberHeadings`, `list.bullets`, `list.numbers`, `list.restart`, `list.continue`, `outline.moveUp`, `outline.moveDown`, `outline.promote`, `outline.demote`.
- Every `DocumentEdits.Raise` and `Restore` is followed by `DocumentEdits.Attribute` with the edit's changes (the move included, in the editor and in undo and redo); a new raise site gets a case in `SnapshotCacheOracleTests`.
- Incremental equals full: `LayoutOracle.AssertEqualToFull`, exact equality; a new layout fast path needs a corpus oracle case first.
- Start cost: nothing of these features exists before first use (a test in the style of `StartupCreatesNoEditorWork`); the Home band's width hint stays true (`WriteRibbon.HomeFullWidthAtLeast`, `RibbonStartupTests`; raise it with its test only when the real band grows).
- Typing, format and structure: worst p95 at most 16.0 ms where judged; a limit is never loosened. The move aim is at most 100 ms from command to first visible page on the Linux laptop; the limit is median times 1.25, a miss is a finding for the owner.
- Out of scope: sections, page numbering, headers and footers, page setup (4.4b); captions and tables of contents (4.5); collapsing the outline; list galleries; recording a move as a tracked change; the owner gates of earlier milestones.

## Review Focus

1. A heading with its own direct `numPr` (also `numId` 0) while the numbering switch is on or off: the paragraph is left alone and the label is what the file says (Task 5, `DirectNumPrOnAHeadingIsNeverTouched`).
2. A Word file whose Heading styles already carry a list: the switch shows on, writes nothing, and off removes only the style links (Task 5, `StylesThatAlreadyCarryAListAreOnAndWriteNothing`).
3. A move or promote on a document with an empty body, a single heading, a heading as the last block, or two adjacent headings: no exception, the command is disabled with a reason where nothing can move (Tasks 7, 9, 10).
4. Undo and redo of every operation in any order after a save and after typing: revision and dirty state follow the history, labels equal the reference count (Task 13).
5. A selection that reaches a table or a block content control when a list command runs: the paragraphs inside are changed like formatting does, a range across a control edge is refused with a reason (Task 6, `ListToggleAcrossAControlEdgeIsRefused`).

---

## File structure

| File | Responsibility |
|---|---|
| `Docs/roadmap.json`, `Docs/ROADMAP.md` | split 4.4 into 4.4.1 and 4.4.2, plan link, scope change |
| `src/Raffica.Write.Model/Formatting/IEffectiveFormat.cs` | add `ForParagraph` and `EffectiveParagraph` (style id, numbering id and level, outline level) |
| `src/Raffica.Write.Layout/Styles/LayoutEffectiveFormat.cs` | implement `ForParagraph` over `StyleResolver` |
| `src/Raffica.Write.Editing/Lists/ListKind.cs`, `ListCatalog.cs`, `WriteListDefinitions.cs` | find or create lists, Write's own definitions, the ledger of created `w:num` |
| `src/Raffica.Write.Editing/HeadingNumbering.cs`, `StyleChange.cs`, `EditHistory.cs` | the numbering switch, several styles in one step |
| `src/Raffica.Write.Editing/TextEditor.Lists.cs` | toggle, level, restart, continue, end list, remove list at start |
| `src/Raffica.Write.Editing/BlockMove.cs`, `TextEditor.Move.cs`, `EditHistory.cs`, `EditResult.cs` | the move step |
| `src/Raffica.Write.Editing/TextEditor.Outline.cs` | promote and demote |
| `src/Raffica.Write.Layout/Pages/LayoutEdits.cs` | a rotated range in `ApplyEdit` (only if the oracle needs it) |
| `src/Raffica.Write/Panes/OutlineTree.cs` | pure subtree, movability and level-source reading over `OutlineHeading` |
| `src/Raffica.Write/Panes/OutlinePane.cs`, `OutlinePane.Drag.cs` | drag, drop line, keys, header buttons |
| `src/Raffica.Write/Commands/*`, `WriteRibbon.cs`, `WriteStrings.cs`, `Resources/Strings.*.resx`, `Platform/MacOS/WriteMacMenu.cs`, `Viewing/PageView.Structure.cs`, `Viewing/PageView.Keys.cs`, `WriteWindow.Structure.cs` | commands, rules, ribbon, keys, wiring |
| `tests/Raffica.Write.Tests/Support/ReferenceNumbering.cs`, `Layout/StructureSteps.cs`, `Editing/*` | independent count, oracle steps, editor tests |
| `bench/write-layout-bench`, `src/Raffica.Write/Measurement.Structure.cs`, `bench/shell-probe/measure.cs` | the `structure` case |
| `Docs/DECISIONS.md`, `CLAUDE.md`, `CHANGELOG.md`, `Docs/write-word-check.md` | records |

---

### Task 1: Split the roadmap entry and link this plan

**Files:** Modify `Docs/roadmap.json`, regenerate `Docs/ROADMAP.md`.

**Interfaces:** Produces the ids `4.4.1` (this milestone, `status` `in-progress`, `plan` `superpowers/plans/2026-11-21-milestone-04-4a-headings-lists-and-outline.md`, prerequisite `4.3.3`) and `4.4.2` (open, prerequisite `4.4.1`) that later tasks tick.

- [ ] **Step 1:** Replace the milestone `4.4` by `4.4.1` "Headings, lists and the outline (4.4a)" with tasks: headings, lists and numbering on the document's numbering definitions; outline pane move, promote and demote (drag and keys); measure a chapter move. Acceptance: "Moving and deleting headings never leaves stale numbers (tests)"; "A chapter move on the generated thesis takes at most the limit set from the median (aim 100 ms on the Linux laptop; measured)"; "Word opens a file with numbered headings, lists and a moved chapter with no repair prompt and the numbers read as in Write (owner, recorded)"; "A look at the Native AOT app on Windows, the Linux laptop and the Intel Mac (owner)". Add `4.4.2` "Sections, page numbers, headers and footers, page setup (4.4b)" with the old tasks 3 and 4 and the old second acceptance ("the page-3 \"1\" and the \"A-1\" appendix made with the one command open in Word as intended (owner, recorded)"). Change the prerequisite of `4.5` from `4.4` to `4.4.2`. Add a "Scope changes" block: 4.4 split, and the outline task text now says "heading with its content" for drag and keys.
- [ ] **Step 2:** Run `dotnet run tools/roadmap.cs -- generate` then `dotnet run tools/roadmap.cs -- check`. Expected: check exits 0.
- [ ] **Step 3:** Commit `docs: split roadmap milestone 4.4 into 4.4.1 and 4.4.2 and link the 4.4a plan (M4.4)` (files: `Docs/roadmap.json`, `Docs/ROADMAP.md`, this plan).

### Task 2: The independent reference count

**Files:** Create `tests/Raffica.Write.Tests/Support/ReferenceNumbering.cs`, `tests/Raffica.Write.Tests/Layout/ReferenceNumberingTests.cs`.

**Interfaces:** Produces `internal static class ReferenceNumbering { public static IReadOnlyList<(Paragraph Paragraph, string Label)> Labels(WordDocument document); }` in document order over the body (tables and content controls entered), counting from the model alone: effective `numPr` is the paragraph's own, else its style chain's (`basedOn`); abstract definitions share counters; a deeper level restarts when a shallower one counts; `w:startOverride` applies the first time a `w:num` is used at that level; formats decimal, lowerLetter, upperLetter, lowerRoman, upperRoman, bullet (label is the `lvlText` as written); `lvlText` `%1` to `%9` substituted. It must not call `NumberingCounter` or `StyleResolver`.

- [ ] **Step 1:** Write `ReferenceNumberingTests.TheReferenceMatchesTheLayoutOnEveryCorpusFile`: for each corpus file, the labels the layout draws (`LaidOutPage` items with `Number`/list label text, in order; reuse `PageTestKit`) equal `ReferenceNumbering.Labels` for every numbered paragraph; assert the count compared is above 0 for at least the thesis. Also `ASharedAbstractDefinitionSharesCounters`, `AStartOverrideRestartsOnce`, `ADeeperLevelRestarts` on small hand-made documents.
- [ ] **Step 2:** Run `dotnet test tests/Raffica.Write.Tests -c Release --filter-class "*ReferenceNumberingTests"`; Expected: FAIL (type missing). Implement the class. Run again; Expected: PASS and the reported test count equals the number of tests (a filter that matches nothing selects nothing silently).
- [ ] **Step 3:** If the reference and the layout differ on a corpus file, decide from Word's documented behaviour which is right and fix that side, noting the case in the test; do not weaken the comparison. Commit `test(write): an independent count of list numbers (M4.4a)`.

### Task 3: Paragraph facts for the editor

**Files:** Modify `src/Raffica.Write.Model/Formatting/IEffectiveFormat.cs`, `src/Raffica.Write.Layout/Styles/LayoutEffectiveFormat.cs`, `src/Raffica.Write/Editing/EditSession.cs` (the `RefreshableFormat` wrapper at the end of the file). Test: `tests/Raffica.Write.Tests/Layout/EffectiveFormatTests.cs`.

**Interfaces:** Produces in Model `public sealed record EffectiveParagraph(string? StyleId, int NumberingId, int NumberingLevel, int? OutlineLevel)` and `IEffectiveFormat.ForParagraph(StoryRef story, Paragraph paragraph)`. `NumberingId` 0 means no list; `OutlineLevel` is 0 to 8 or null. Consumes `ResolvedParagraph.NumberingId`, `.NumberingLevel`, `.OutlineLevel`.

- [ ] **Step 1:** Add `EffectiveFormatTests.ForParagraphEqualsThePlannerForEveryParagraphOfTheCorpus`: for every paragraph of every corpus file, `ForParagraph` equals the three values `BlockPlanner.PlanFor` resolves (same pattern as the existing run test), including a paragraph in a table cell.
- [ ] **Step 2:** Run the class; Expected: FAIL (does not compile). Implement `ForParagraph` in `LayoutEffectiveFormat` through `ResolveParagraph` (the table-cell index of 4.3b), and forward it in `RefreshableFormat`. Fix every other `IEffectiveFormat` implementation (test fakes) so the solution builds.
- [ ] **Step 3:** Run `dotnet build Raffica.slnx -c Release` (0 warnings) and the class; Expected: PASS. Commit `feat(write): the editor can ask a paragraph's list and outline level (M4.4a)`.

### Task 4: Lists in the numbering part

**Files:** Create `src/Raffica.Write.Editing/Lists/ListKind.cs`, `ListCatalog.cs`, `WriteListDefinitions.cs`. Test: `tests/Raffica.Write.Tests/Editing/ListCatalogTests.cs`.

**Interfaces:** Produces `public enum ListKind { Bullet, Numbered }` and `public sealed class ListCatalog(WordDocument document)` with `int FindList(ListKind kind)` (0 when none), `int EnsureList(ListKind kind)` (find, else add Write's definition and a `w:num`, return the id), `int NewRun(ListKind kind)` (a `w:num` for a fresh run: reuse an unreferenced one this catalog created, else add one on the same abstract definition), `int Restart(int numId, int level)` (adds a `w:num` on the same abstract definition with `StartOverride` 1 at `level`, returns it), `bool IsReferenced(int numId)` (a scan of body, notes, headers, footers and style sheet), `int HeadingList()` (find or add the heading definition: multilevel decimal `%1.` to `%1.%2.%3...` nine levels, each with `w:pStyle` `Heading1` to `Heading9`, hanging indents of the blank document's definition 0). `WriteListDefinitions` builds the three definitions with Model types. New children are placed by `SchemaOrder`; ids are `max + 1`.

- [ ] **Step 1:** Write `ListCatalogTests`: `FindListPrefersTheStylePointedAt` (List Bullet style with `numPr` 7 wins over an earlier bullet list), `FindListSkipsDefinitionsLinkedToStyles`, `EnsureListAddsWritesOwnDefinitionOnce` (second call returns the same id; the numbering part gains exactly one abstract definition and one instance), `NewRunReusesAnUnreferencedCreatedInstance`, `NewRunNeverReusesAnInstanceThatIsReferenced`, `RestartAddsAnInstanceWithStartOverride1`, and `WritesOwnDefinitionsEqualTheBlankDocumentsLevelByLevel` (format, text, justification, indent and run fonts of every level equal those of `BlankDocument.Create` parsed through `DocxFile.Open(bytes)`, so the two cannot drift).
- [ ] **Step 2:** Run `--filter-class "*ListCatalogTests"`; Expected: FAIL. Implement. Run again; Expected: PASS.
- [ ] **Step 3:** Commit `feat(write): find and add numbering definitions for lists (M4.4a)`.

### Task 5: The numbering switch for headings

**Files:** Create `src/Raffica.Write.Editing/HeadingNumbering.cs`. Modify `StyleChange.cs`, `EditHistory.cs` (`TrySwapStyle`, `SizeOf`), `EditResult.cs` only if needed. Test: `tests/Raffica.Write.Tests/Editing/HeadingNumberingTests.cs`, `Layout/RestyleTests.cs`.

**Interfaces:** Consumes `ListCatalog.HeadingList()`. Produces `StyleChange` with an init property `IReadOnlyList<StyleChange> Companions` (default empty) and `IEnumerable<StyleChange> All` (itself, then the companions); `TrySwapStyle` swaps every item of `All`, all or nothing. Produces `public enum HeadingNumberingState { On, Off, Unavailable }`, `public static class HeadingNumbering { public static HeadingNumberingState StateOf(WordDocument document); }` and `public EditResult? TextEditor.SetHeadingNumbering(bool on)` (null when nothing changes) returning an `EditResult` whose `Changes` is empty, whose `Style` is the first style change with the other eight as `Companions`, and whose caret is the current caret. `Unavailable` when the document lacks the style ids `Heading1` to `Heading9` named `heading 1` to `heading 9`.

- [ ] **Step 1:** Write `HeadingNumberingTests`: `SwitchingOnNumbersTheNineHeadingStylesAndLinksTheLevels` (each style's `numPr` has the list's id, `ilvl` from level 2 on; each level of the definition has `pStyle`), `SwitchingOffRemovesOnlyTheStyleLinks` (definition and instance stay), `StylesThatAlreadyCarryAListAreOnAndWriteNothing` (no new definition or instance, `SetHeadingNumbering(true)` returns null), `DirectNumPrOnAHeadingIsNeverTouched`, `AFileWithoutHeadingStylesIsUnavailable`, `OneUndoStepRestoresAllNineStyles` and `RedoFindsTheDefinitionUndoLeft`, `TheStepNeverCoalescesWithTyping`, `HistoryBytesCountEveryCompanion`.
- [ ] **Step 2:** Add to `RestyleTests`: `SwitchingHeadingNumbersOnGivesTheLayoutNumbers` (after `Restyle`, the page labels of the Heading 1 paragraphs of a small document read `1.`, `2.` and Heading 2 `1.1.`; equal to `ReferenceNumbering`) and the same after undo (no labels) and redo.
- [ ] **Step 3:** Run both classes; Expected: FAIL. Implement; the editor writes the nine style changes in one pass under the `Raise`/`Attribute` rule (an empty change list is attributed). Confirm `EditSession.Run` calls `Restyle` for a result with `Style` and that the layout's numbering counter is rebuilt (the new engine makes a new `NumberingCounter`); fix if the old counter's cached facts survive.
- [ ] **Step 4:** Run both classes and `--filter-class "*StyleHistoryTests"` (unchanged behaviour); Expected: PASS. Commit `feat(write): number the headings in one undoable step (M4.4a)`.

### Task 6: List operations in the editor

**Files:** Create `src/Raffica.Write.Editing/TextEditor.Lists.cs`. Modify `TextEditor.cs` (`SplitParagraph` and `Backspace` for the empty list item and the list at the start). Test: `tests/Raffica.Write.Tests/Editing/ListEditingTests.cs`.

**Interfaces:** Consumes `ListCatalog`, `IEffectiveFormat.ForParagraph`, `FormatParagraphs`. Produces `TextEditor.ToggleList(TextRange selection, ListKind kind): EditResult`, `ChangeListLevel(TextRange selection, int delta): EditResult?` (levels clamp to 0 to 8, null when no list paragraph is touched), `RestartNumbering(TextPosition at): EditResult?`, `ContinueNumbering(TextPosition at): EditResult?`, and `ListFacts ReadListFacts(TextRange selection)` with `bool InList`, `ListKind? Kind`, `bool CanRestart`, `bool CanContinue` and a `Reason` for each disabled case. A `ListCatalog` lives on the editor and is created on first use.

- [ ] **Step 1:** Write `ListEditingTests` (assert on the model and on `ReferenceNumbering`): `TogglingBulletsOnPlainParagraphsGivesThemNumPrAndListParagraph`, `TogglingTheSameKindAgainRemovesNumPrAndKeepsTheStyle`, `TogglingTheOtherKindSwitchesTheList`, `AHeadingKeepsItsStyleWhenItBecomesAListItem`, `ARunOfParagraphsGetsOneNewInstanceSoItNumbersFromOne`, `TabLevelChangeFollowsTheLevelIndent`, `RestartGivesTheRestOfTheListANewInstanceWithStartOverride`, `ContinuePointsBackAtThePreviousInstance`, `EnterOnAnEmptyListParagraphEndsTheList`, `BackspaceAtTheStartRemovesTheListFirstAndKeepsTheText`, `EachOperationIsOneUndoStep`, `ListToggleAcrossAControlEdgeIsRefused` (content-control rule of 4.3a), `ListsInTableCellsWork`.
- [ ] **Step 2:** Run `--filter-class "*ListEditingTests"`; Expected: FAIL. Implement each as a `FormatParagraphs` variant (one `BlockChange`, `Raise`, `Attribute`, `History.Record`); the new-run and restart instances are made before the change and are not in the history.
- [ ] **Step 3:** Run the class plus `--filter-class "*TextEditorTests"` and `"*ParagraphFormattingTests"`; Expected: PASS. Commit `feat(write): bullet and numbered lists in the editor (M4.4a)`.

### Task 7: The move step

**Files:** Create `src/Raffica.Write.Editing/BlockMove.cs`, `TextEditor.Move.cs`. Modify `EditResult.cs`, `EditHistory.cs`. Test: `tests/Raffica.Write.Tests/Editing/BlockMoveTests.cs`, `SnapshotCacheOracleTests.cs`.

**Interfaces:** Produces `public sealed record BlockMove(StoryRef Story, int First, int Count, int Target)` (body only; `Target` is the index in the list before the move at which the subtree lands; legal when `Target <= First` or `Target >= First + Count`), `EditResult` gets a trailing optional `BlockMove? Move = null`, and `public EditResult? TextEditor.MoveBlocks(int first, int count, int target)` (null for an illegal or no-op move). A move result has one `BlockChange` over the spanned range (`First = min`, `Removed = Added = span`, `OldBlocks` = the pre-move block objects, not clones) and the caret at the start of the first moved paragraph. The history step stores story, path, first, count, target and revisions, no blocks, with `Bytes` near zero; undo and redo run the inverse rotation on the same objects and return a result with the same shape; the step never coalesces; `Record` handles `result.Move`.

- [ ] **Step 1:** Write `BlockMoveTests`: `MovingASubtreeDownAndUpKeepsEveryBlockObject` (`ReferenceEquals` of every block before and after), `AMoveToItsOwnPlaceIsNull`, `ADropInsideTheSubtreeIsNull`, `UndoRestoresTheOrderAndTheRevision`, `RedoMovesAgain`, `MovedBlocksKeepTheirParaIdsBookmarksAndCommentAnchors`, `TheHistoryStepCostsNoBlockClones` (bytes under 1 KiB for a 300-block move), `TheStepDoesNotCoalesceWithTyping`, `MovingTheFirstChapterToTheEndOfAThousandBlocksIsOneStep`. In `SnapshotCacheOracleTests` add a forced move step: the snapshot bytes after a move, undo and redo equal the user-save writer's main part.
- [ ] **Step 2:** Run both classes; Expected: FAIL. Implement; rotate with `List<Block>` range moves (no per-block clone), `Raise`, `Attribute(result.Changes)`, `History.Record`; teach `Undo` and `Redo` the move kind next to the style kind (`Resolve(step)` for the body path).
- [ ] **Step 3:** Run both classes and `--filter-class "*EditHistoryTests"`; Expected: PASS. Commit `feat(write): a chapter move as a history step of its own (M4.4a)`.

### Task 8: The layout takes a rotated range

**Files:** Modify `src/Raffica.Write.Layout/Pages/LayoutEdits.cs` only if the oracle fails. Test: `tests/Raffica.Write.Tests/Layout/StructureSteps.cs` (first part), `Layout/CorpusIncrementalTests.cs`.

**Interfaces:** Consumes `DocumentLayout.ApplyEdit(BlockChange, int)` and `Restyle(int)`. Produces `StructureSteps` with `Move` (random legal subtree move on the body), registered like `FormattingSteps` (`RanPerKind`, `Compared`).

- [ ] **Step 1:** Add the `Move` step and a case to `CorpusIncrementalTests` (every corpus file with at least two headings, plus the thesis, after each of three random moves and after undo and redo: `LayoutOracle.AssertEqualToFull`, exact; page numbers, labels and the block tables included). Add `AMoveAcrossASectionBreakStillEqualsAFullLayout` (the move region contains a section-break paragraph that is not in the subtree).
- [ ] **Step 2:** Run `--filter-class "*CorpusIncrementalTests"`; Expected: FAIL if plan caches or the filler state keep a block at its old position, else PASS. If it fails, in `Refill` treat a change whose `OldBlocks` order differs from the container's blocks as one that forgets the plan of every block in the range (`Forget`) and resumes from the first block of the range; do not add a tolerance to the oracle.
- [ ] **Step 3:** Run the class again; Expected: PASS. Commit `test(write): the layout equals a full layout after a chapter move (M4.4a)` (with the fix if one was needed).

### Task 9: Promote and demote

**Files:** Create `src/Raffica.Write.Editing/TextEditor.Outline.cs`. Test: `tests/Raffica.Write.Tests/Editing/OutlineLevelTests.cs`.

**Interfaces:** Consumes `IEffectiveFormat.ForParagraph`. Produces `TextEditor.ShiftOutlineLevels(int firstBlock, int lastBlock, int delta): EditResult?` over body blocks, every paragraph in the range whose `OutlineLevel` is set is shifted by `delta` (-1 or +1): `HeadingN` becomes `Heading(N+delta)` by `StyleId`, a direct `w:outlineLvl` is rewritten, any other source or a level outside 0 to 8 makes the whole call return null before anything changes; one `BlockChange` over the range, `KeepSelection`. And `OutlineShiftFacts ReadOutlineShift(int firstBlock, int lastBlock, int delta)` with `bool Allowed` and a `Reason` (`NotAHeadingStyle`, `OutOfRange`, `NothingToShift`).

- [ ] **Step 1:** Write `OutlineLevelTests`: `PromotingShiftsTheHeadingAndItsSubHeadingsByOne`, `DemotingAHeading9IsRefused`, `PromotingAHeading1IsRefused`, `ADirectOutlineLevelIsRewritten`, `AMixedSubtreeOfStyleAndDirectLevelsShifts`, `ACustomStyleLevelSourceIsRefusedBeforeAnyChange` (the document is unchanged and the revision did not rise), `OneUndoStepRestoresEveryHeading`, `BodyTextInTheRangeIsUntouched`.
- [ ] **Step 2:** Run the class; Expected: FAIL. Implement. Run; Expected: PASS. Commit `feat(write): promote and demote headings with their sub-headings (M4.4a)`.

### Task 10: The outline tree

**Files:** Create `src/Raffica.Write/Panes/OutlineTree.cs`. Modify `OutlineSource.cs` only to expose what the tree needs. Test: `tests/Raffica.Write.App.Tests/Panes/OutlineTreeTests.cs`.

**Interfaces:** Consumes `OutlineSource.ReadHeadings(WordDocument): IReadOnlyList<OutlineHeading>`. Produces `internal static class OutlineTree` with `Subtree Of(WordDocument document, IReadOnlyList<OutlineHeading> headings, int row)` where `internal readonly record struct Subtree(int FirstBlock, int Count, int LastHeadingBlock, MoveBlock? Blocked)`, and `internal enum MoveBlock { None, SectionBreakInside, InsideContentControl, NotAtTopLevel }`; and `int DropTarget(IReadOnlyList<OutlineHeading> headings, WordDocument document, int row, DropPlace place)` (`DropPlace { Before, After }`; a drop after the last row lands at the end of the body; returns -1 for a drop inside the subtree). Pure; reads the model on the UI thread only.

- [ ] **Step 1:** Write `OutlineTreeTests`: `ASubtreeEndsAtTheNextHeadingOfTheSameOrAHigherLevel`, `TheLastSubtreeRunsToTheEndOfTheBody`, `TablesAndBlocksBetweenHeadingsBelongToTheHeadingAbove`, `ASectionBreakParagraphBlocksTheMove`, `AHeadingInAControlIsBlocked`, `DropInsideTheSubtreeIsMinusOne`, `DropAfterTheLastRowIsTheEndOfTheBody`, `AnEmptyBodyHasNoRows`.
- [ ] **Step 2:** Run `dotnet test tests/Raffica.Write.App.Tests -c Release --filter-class "*OutlineTreeTests"`; Expected: FAIL; implement; Expected: PASS. Commit `feat(write): the subtree of a heading (M4.4a)`.

### Task 11: Commands, keys, ribbon and strings

**Files:** Modify `Commands/WriteCommandIds.cs`, `WriteCommandCatalog.cs`, `WriteCommandRules.cs`, `WriteRibbon.cs`, `WriteStrings.cs`, `Resources/Strings.en.resx`, `Resources/Strings.de.resx`, `Platform/MacOS/WriteMacMenu.cs`, `Viewing/PageView.Keys.cs`, `Viewing/PageView.Formatting.cs`, `WriteWindow.View.cs`, the `ReservedShortcuts` audit and `NavigationHelp` (find them by `grep`); create `Viewing/PageView.Structure.cs`, `WriteWindow.Structure.cs`. Test: `Commands/StructureCommandTests.cs`, `Viewing/PageViewStructureTests.cs`, `WriteWindowStructureTests.cs`, existing `RibbonStartupTests`, `FormatCommandTests`-style guards.

**Interfaces:** Consumes Tasks 4 to 10. Produces the nine command ids of the constraints; `list.bullets`, `list.numbers` and `structure.numberHeadings` are toggles (`CheckedWhen` from `ReadListFacts` and `HeadingNumbering.StateOf`); the outline commands act on the pane's selected row, or on the heading at the caret when the pane is closed; each runs through `EditSession.Apply` with `session.Pending` cleared like any non-typing operation.

- [ ] **Step 1:** Write the tests first. `StructureCommandTests`: `EveryNewCommandHasItsOwnIconAndBothLanguagesAndAMacMenuLine`, `EveryDisabledStateHasAReason` (walk every id in every state: no document, saving, faulted, not in a list, nothing to continue, unavailable heading styles, blocked move, out-of-range level), `NumberHeadingsIsCheckedWhenTheStylesCarryAList`. `PageViewStructureTests`: `TabAtTheStartOfAListParagraphChangesTheLevel`, `TabInTheMiddleOfTheTextInsertsATab`, `ShiftTabAtTheStartOfALevel0ItemKeepsTheLevel`, `EnterOnAnEmptyListItemEndsTheList`, `BackspaceAtTheStartOfAListItemRemovesTheList`, `AltShiftArrowsRunTheOutlineCommandsWithTheCaretInAHeading`, `AltGrIsTextNotACommand`. `WriteWindowStructureTests`: `ClickingBulletsTogglesTheListAndUndoRestores`, `TheMoveIsOneUndoStepAndTheCaretIsAtTheMovedHeading`. `RibbonStartupTests` stays green; `StartupCreatesNoStructureWork` pins that no `ListCatalog`, `OutlineTree` or move object exists before first use.
- [ ] **Step 2:** Run the new classes; Expected: FAIL. Implement: catalog entries and icons (nine distinct Lucide keys, none shared: `List`, `ListOrdered`, `ListRestart`, `ListEnd`... check `Icons.axaml` and `CommandIconTests` for free ones; `Heading` for the numbering switch, `ArrowUp`, `ArrowDown`, `IndentDecrease`, `IndentIncrease` or others unused), field codes unique in `WriteRibbon` (pinned by the ribbon tests) in the Home paragraph group, key handling in `PageView.Keys.cs` ahead of the plain `Tab` insert, strings `Cmd*`/`Desc*`/reasons in both resx (LF, "Sie" form in German), `WriteMacMenu` lines, macOS keys only where `ReservedShortcuts` is free (pinned by `ReservedShortcutTests`).
- [ ] **Step 3:** If the real Home band is now wider than `HomeFullWidthAtLeast`, raise the hint and its pinned test; check the bullets of a blank document draw as bullets in the page view (`PageRendererTests` pixel or glyph check) and fix `NumberingCounter.BulletText` for any glyph that falls back.
- [ ] **Step 4:** Run the new classes, `RibbonStartupTests`, `CommandIconTests`, `TranslationTests`, `CardFooterTests`; Expected: PASS. Commit `feat(write): list, numbering and outline commands, keys and wording (M4.4a)`.

### Task 12: The outline pane drags and moves

**Files:** Create `src/Raffica.Write/Panes/OutlinePane.Drag.cs`; modify `OutlinePane.cs`, `Styles/*.axaml` for the drop line and header buttons, `WriteWindow.Outline.cs`. Test: `tests/Raffica.Write.App.Tests/Panes/OutlinePaneMoveTests.cs`.

**Interfaces:** Consumes `OutlineTree`, `EditSession.Apply`, `TextEditor.MoveBlocks`, `ShiftOutlineLevels`. Produces `OutlinePane` header buttons for move up, move down, promote, demote (the four outline commands), a pointer drag of a row with a drop line between rows and an accessible name ("Move heading <text> below <text>", both languages, never longer than the 300-character entry text), and keys in the list: Alt+Shift+Up/Down/Left/Right. A move keeps the moved row selected and focused; the pane reads again by the 200 ms rule; a blocked row's buttons and drag are disabled with the reason as tooltip and name.

- [ ] **Step 1:** Write `OutlinePaneMoveTests` (headless, pointer events injected): `DraggingARowBelowAnotherMovesTheHeadingWithItsContent`, `TheDropLineShowsOnlyOnLegalPlaces`, `ADropInsideTheOwnSubtreeDoesNothing`, `AltShiftDownMovesTheSubtreeByOneRow`, `PromoteAndDemoteChangeTheRowLevelAfterTheRefresh`, `ABlockedRowShowsItsReason`, `ATypedKeyInTheListStaysTheListsOnly` (the 4.2 rule `InOutline`), `AMoveDuringASaveIsQueuedAndReplayed`, `NothingIsReadBeforeThePaneIsShown`.
- [ ] **Step 2:** Run the class; Expected: FAIL. Implement. Run; Expected: PASS. Run `--filter-class "*OutlinePane*"` for the earlier pane tests; Expected: PASS.
- [ ] **Step 3:** Commit `feat(write): move, promote and demote chapters in the outline pane (M4.4a)`.

### Task 13: The structure oracle over the corpus

**Files:** Complete `tests/Raffica.Write.Tests/Layout/StructureSteps.cs`; modify `RealEditorOracleTests.cs`, `CorpusIncrementalTests.cs`. Test: `tests/Raffica.Write.Tests/Layout/StructureOracleTests.cs`.

**Interfaces:** Consumes everything above. `StructureSteps` kinds: `Move`, `Promote`, `Demote`, `DeleteHeading`, `DeleteRangeOverHeadings`, `InsertHeading`, `ToggleHeadingNumbering`, `ToggleList`, `ChangeLevel`, `Restart`, `Undo`, `Redo`; each ends with `LayoutOracle.AssertEqualToFull` and an equality of the page labels with `ReferenceNumbering.Labels`.

- [ ] **Step 1:** Write `StructureOracleTests`: `EveryStepKindRanOnTheCorpus` (`RanPerKind` has every kind above 0; a guard like 4.3b's), `LabelsEqualTheReferenceAfterEveryStep` (seeded FNV-1a, never `GetHashCode`, 30 steps on each corpus file with headings and on three generated documents with numbered headings on), `UndoAllThenRedoAllReturnsToTheSameLabels`, `TheSavedFileOfTheFinalStateReopensWithTheSameLabels`, and the thesis `SelectAllThenNumberingOnAndOffIsFastAndOneStepEach` in `WallClockGroup` with a 5 s bound.
- [ ] **Step 2:** Run `--filter-class "*StructureOracleTests"`; Expected: FAIL at first; fix what it finds in the layout or the editor (never the oracle). Run until PASS. Then run the whole Write test project: `dotnet test tests/Raffica.Write.Tests -c Release --no-build --max-parallel-test-modules 1`; Expected: all green.
- [ ] **Step 3:** Commit `test(write): the structure oracle keeps every number current (M4.4a)`.

### Task 14: Measure the structure operations

**Files:** Create `src/Raffica.Write/Measurement.Structure.cs`; modify `bench/write-layout-bench` (`edit` prints structure rows), `bench/shell-probe/measure.cs` (case `structure`, rows `structure/*`), `.github/workflows/measure.yml` (the case and the engine step), `Docs/DECISIONS.md` later. Test: `tests/Raffica.Write.App.Tests/MeasurementTests.cs`, `bench` check tests where they exist.

**Interfaces:** Produces the app switch `--measure-structure` (needs `--settings-file` and a file, like `--measure-format`) and rows `structure/move-first-to-last`, `structure/move-middle`, `structure/move-last-to-first`, `structure/numbering-on`, `structure/numbering-off`, `structure/toggle-list` with median and p95 for the command-to-first-visible-page time; only the three move rows are judged once a limit exists; `typing` and `format` re-run with the pane open through the existing `typing-pane` rows.

- [ ] **Step 1:** Write the measurement test that runs `--measure-structure` headless on a small file and checks the row names and that no row is empty; Run; Expected: FAIL; implement the switch and the bench rows on the `format` pattern (cases picked by one shared helper so the bench and the app change together).
- [ ] **Step 2:** Run on the thesis from a Native AOT publish on the Windows PC (`dotnet publish` of `src/Raffica.Write` with `Microsoft Visual Studio\Installer` on `PATH`; search the log for `IL[23][0-9]{3}`): `RafficaWrite --measure-structure --settings-file <tmp> thesis.docx` three launches; record the medians in the task notes. The limit entry in `s_writeBudgets` follows the owner's runs (open); until then the rows are recorded and unjudged and `measure.cs check` says so.
- [ ] **Step 3:** Commit `feat(write): measure chapter moves, the numbering switch and list toggles (M4.4a)`.

### Task 15: Records, Word-check files and the final run

**Files:** Modify `Docs/DECISIONS.md` (entry "Raffica Write: headings, lists and the outline (M4.4a)" at the top, with context, decision, rationale, rejected alternatives, known limits and open gates as the 4.3 entries), `CLAUDE.md` (a "Write structure (M4.4a)" section in the style of the 4.3 sections: where, rules to remember, pitfalls, known limits, owner gates), `CHANGELOG.md` (`[Unreleased]`: number headings, lists, move and promote in the outline), `Docs/write-word-check.md` (rows for the 4.4a files), `Docs/roadmap.json` (tick tasks only for what ran; criteria with proof; the gates stay open), `Docs/ROADMAP.md`.

- [ ] **Step 1:** Make the owner's Word-check files with a test helper that writes to a folder given by `RAFFICA_WORD_CHECK_DIR` (skips without it): `numbered-headings.docx` (switch on, a chapter moved, a promote), `lists.docx` (bullet, numbered with restart, multilevel by Tab), made through the editor and saved through `DocxFile.SaveAsync`; run `pwsh tools/word-open-check.ps1 <files>` and record the result; the owner's look in Word is an open gate with this command.
- [ ] **Step 2:** Write the records, regenerate the roadmap (`dotnet run tools/roadmap.cs -- generate`), convert changed `.md`, `.resx` and `.cs` files to LF.
- [ ] **Step 3:** Final proof, each command with its result named in the commit message: `dotnet build Raffica.slnx -c Release` (0 warnings), `dotnet test Raffica.slnx -c Release --no-build --max-parallel-test-modules 1` with `RAFFICA_REQUIRE_OCR=1` after `dotnet run tools/native-fetch.cs -- --rid win-x64 --out artifacts/native/win-x64`, `dotnet format Raffica.slnx --verify-no-changes`, `dotnet run tools/roadmap.cs -- check`, the AOT publish log search for `IL[23][0-9]{3}` (none).
- [ ] **Step 4:** Commit `docs: record Write 4.4a, headings, lists and the outline (M4.4)`; open the PR to `dev`; merge it once CI is green (feature branch to `dev`, squash allowed).

---

## Self-review

- **Spec coverage:** heading numbering (Tasks 4, 5), lists with toggles, level, restart, continue, Enter and Backspace rules (Tasks 4, 6, 11), outline move, promote, demote, drag, keys, blocked cases (Tasks 7, 9, 10, 12), never-stale proof (Tasks 2, 8, 13), measure (14), gates and records (15), roadmap split (1). Bullet rendering check and the pane's reading of a heading's own `numPr`: Task 11 step 3 and Task 5 `DirectNumPrOnAHeadingIsNeverTouched`; the formatting pane's row for it is left as it is (it keeps `numPr` out of the override rows), to be checked in Task 11 and noted in DECISIONS if it differs.
- **Types:** `ListCatalog`, `ListKind`, `EffectiveParagraph`, `BlockMove`, `StyleChange.Companions`, `OutlineTree.Subtree`, `ShiftOutlineLevels` and `MoveBlocks` carry the same names in every task.
- **Open at execution time (to be settled by reading the code, not by the owner):** the shape of `ReadListFacts` and `OutlineShiftFacts` reasons as `WriteCommandRules` wants them; free Lucide icon keys and field codes; whether `Refill` needs the Task 8 fix.
