# Write: formatting and styles (milestone 4.3.2, "4.3b") Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Text in Write can be formatted (character, paragraph, styles) and a formatting inspector shows every direct override with a row that removes exactly that override.

**Architecture:** Direct formatting rides the 4.3a path (one `BlockChange` per operation through `EditSession.Apply`, so history, incremental layout and the oracle tests apply unchanged). A style edit ("Update style to match") is a new history step holding old and new `Style`, and the layout restarts on a fresh engine (`DocumentLayout.Restyle`). Effective values (for toggles) come from the layout's own `StyleResolver` through a Model interface, so "equal to the layout" holds by construction.

**Tech Stack:** .NET 10, C#, Avalonia 12 (shell controls), xunit v3 (`dotnet test` with `--filter-class`), Open XML property bags (`PropertyBag` over `XElement`).

**Spec:** `Docs/superpowers/specs/2026-11-16-raffica-write-formatting-design.md` (approved 2026-11-16). Deviation from its section 5 part 1, allowed by its section 7: the effective-value reader is an interface in Model (`IEffectiveFormat`) implemented in Layout over `StyleResolver`, not a second reader in Model; no equality test against the resolver is needed because it is the resolver.

## Global Constraints

- Everything in English (code, comments, docs, commits); every UI string in `Strings.en.resx` **and** `Strings.de.resx` of `src/Raffica.Write/Resources` (German in the "Sie" form); `TranslationTests` fails otherwise. Convert every file the Write tool or a `.resx` edit writes to LF before committing (`dotnet format` fails on CRLF).
- Every command has its own Lucide icon (`WriteCommandTests.EveryCommandHasItsOwnIcon`), a reason in `WriteCommandRules.ReasonFor` for every disabled state, and an entry in the macOS menu (`WriteMacMenu`); every card footer goes through `CardFooter.Build`; no hex literals in component styles (`NoHexInComponentStylesTests`).
- Every model change goes through the lease and `EditSession.Apply`: one `BlockChange` per operation, `OldBlocks` are read-only history snapshots, `EditHistory` clears itself after an edit behind its back. Never cache a position across an edit.
- Incremental layout equals a full layout exactly (`LayoutOracle.AssertEqualToFull`, no tolerance); a new fast path needs an oracle case first.
- `Raffica.Write.Editing` references only `Raffica.Write.Model` (`ProjectGuardTests.EditingReferencesOnlyModel`); Layout references no Avalonia type; the app never references `Raffica.Pdf`.
- A keystroke-sized edit stays inside 16.0 ms CPU (the `typing` budget); a style update is measured and recorded, not judged.
- Nothing of the pane, the pickers or the pending format exists before first use (`StartupTests.StartupCreatesNoEditorWork` extends to it).
- Build `dotnet build Raffica.slnx -c Release`; tests `dotnet test Raffica.slnx -c Release --no-build --max-parallel-test-modules 1` with `RAFFICA_REQUIRE_OCR=1`; one class `dotnet test tests/Raffica.Write.Tests -c Release --filter-class "*Name"` (check the count, a filter that matches nothing selects nothing silently); format `dotnet format Raffica.slnx --verify-no-changes`; roadmap `dotnet run tools/roadmap.cs -- check`. Warnings are not suppressed, tests not skipped.
- Roadmap rules: tick a task right away, an acceptance criterion only after it was checked, with the proof in `evidence`; whatever needs the owner's machines stays open with its command.

## Review Focus

1. **Select all, then format, on the 311-page thesis:** must finish in seconds, be one undo step, and neither blow the 16 MiB history cap silently nor hang the UI (Task 3, `SelectAllBoldOnTheThesisIsOneUndoStepAndFast`).
2. **Runs inside containers:** a range crossing a tracked-change wrapper, a hyperlink, a field (result and instruction runs) and unknown inline content must format the runs, keep the containers and the field instruction text, and save and reopen (Task 2, `FormattingKeepsContainersFieldsAndUnknownContent`).
3. **Empty paragraph and caret formatting:** a command at a caret in an empty paragraph, a caret move that clears the pending format, and an IME commit that must not take a stale pending format (Tasks 2, 7).
4. **A file with no styles part or a `pStyle` the file does not define:** applying, updating and the inspector must not throw; "Update style to match" is disabled with a reason; an undefined id is listed as its id (Tasks 3, 5, 11).
5. **Undo, redo and the dirty state around a style step:** undo to the saved revision is clean again; a document changed behind the history clears it; redo after a new edit is gone; a style step ends typing coalescing (Task 5).

---

### Task 1: Effective formatting from the layout's resolver

**Files:**
- Create: `src/Raffica.Write.Model/Formatting/EffectiveRun.cs`, `src/Raffica.Write.Model/Formatting/IEffectiveFormat.cs`, `src/Raffica.Write.Layout/Styles/LayoutEffectiveFormat.cs`
- Test: `tests/Raffica.Write.Tests/Layout/EffectiveFormatTests.cs`

**Interfaces:**
- Produces (Model, namespace `Raffica.Write.Model`): `public sealed record EffectiveRun(bool Bold, bool Italic, bool Underline, bool Strike, bool SmallCaps, bool Caps, VerticalPosition Position)`; `public enum VerticalPosition { Baseline, Superscript, Subscript }`; `public interface IEffectiveFormat { EffectiveRun ForRun(StoryRef story, Paragraph paragraph, Run run); EffectiveRun ForMark(StoryRef story, Paragraph paragraph); }`.
- Produces (Layout): `public sealed class LayoutEffectiveFormat(WordDocument document) : IEffectiveFormat`, built on a **new** `StyleResolver(document.Styles, document.Settings, document.Numbering)` per instance (the resolver snapshots styles, so the app makes a new instance after a style change; Task 7 does). A paragraph inside a table cell is resolved the way the layout resolves it (`StyleResolver.ForTable`, then the cell's format into `ForParagraph(paragraph, cell)`); the instance finds the enclosing cell by walking the story's blocks and caches nothing across calls.

- [ ] **Step 1: Write the failing tests** in `EffectiveFormatTests`: `BoldFromTheParagraphStyleIsEffective` (style `Heading1` with `w:b`, run without properties → `Bold` true), `ExplicitOffInARunCancelsTheStyle` (`w:b w:val="0"` → false), `CharacterStyleAndBasedOnChainAreRead`, `BoldFromATableStyleHeaderRowIsEffectiveInsideTheCell` (a table whose style bolds the first row; a run in it → true; the same paragraph outside a table → false), `UnderlineOfAnyKindIsOn`, `SuperscriptAndSubscriptMapToPosition`, `ForMarkReadsTheParagraphMark`, and `EveryRunOfEveryCorpusFileMatchesTheLayoutsResolvedRun`: for each corpus file (use the loader of `CorpusLayoutTests`), body paragraphs outside tables, compare `ForRun` with `new StyleResolver(...).ForRun(ForParagraph(p), run)` field by field (`Bold`, `Italic`, `Underline != None`, `Strike`, `SmallCaps`, `Caps`, `VerticalAlignment`).
- [ ] **Step 2: Run** `dotnet test tests/Raffica.Write.Tests -c Release --filter-class "*EffectiveFormatTests"` → FAIL (types missing).
- [ ] **Step 3: Implement** the Model types and `LayoutEffectiveFormat` (map `ResolvedRun` to `EffectiveRun`; for cells reuse the call sequence of `Tables/*` that builds a cell format, do not copy its logic).
- [ ] **Step 4: Run** the class → PASS; run `*StyleResolverTests` and `*LayoutGoldenTests` → still green (Layout behaviour unchanged).
- [ ] **Step 5: Commit** `feat(write): effective run formatting through the layout's resolver (M4.3b)`.

### Task 2: Character formatting operations

**Files:**
- Create: `src/Raffica.Write.Editing/PropertyChange.cs`, `FormatChanges.cs`, `RunMerger.cs`, `TextEditor.CharacterFormat.cs` (partial of `TextEditor`)
- Modify: `src/Raffica.Write.Editing/TextEditor.cs` (constructor gets `IEffectiveFormat? format = null`, kept in a private field; document the order of the optional parameters)
- Test: `tests/Raffica.Write.Tests/Editing/CharacterFormattingTests.cs`

**Interfaces:**
- Consumes: `IEffectiveFormat` (Task 1); `InlineSplitter.Split(Paragraph, int item, int chars)`; `ParagraphMap`; the private `Resolve(TextRange)` / `Span` / `CloneBlocks` of `TextEditor`.
- Produces: `public readonly record struct PropertyChange(XName Name, XElement? Value)` (set the child, or remove it when `Value` is null); `public enum CharacterToggle { Bold, Italic, Underline, Strike, SmallCaps, AllCaps, Superscript, Subscript }`; static `FormatChanges.Font(string family)` (writes `ascii` and `hAnsi`, removes `asciiTheme` and `hAnsiTheme` on the same `w:rFonts`, keeps `eastAsia` and `cs`), `Size(float points)` (`w:sz` and `w:szCs` in half-points), `Color(uint rgb)` (`w:val`, theme attributes removed), `Highlight(string? name)`; on `TextEditor`: `EditResult ApplyCharacterChanges(TextRange selection, IReadOnlyList<PropertyChange> changes)`, `EditResult ToggleCharacter(TextRange selection, CharacterToggle toggle)`, `EditResult InsertText(TextRange selection, string text, IReadOnlyList<PropertyChange> typingFormat)` (the text goes into a new run that carries the neighbouring run's properties plus `typingFormat`; the plain `InsertText` is unchanged); `internal static class RunMerger { bool CanMerge(Run a, Run b); }`.

- [ ] **Step 1: Write the failing tests** in `CharacterFormattingTests`: `BoldOnAWordSplitsTheRunAndMergesItBack` (select "bc" of "abcd" → runs "a", "bc"(b), "d"; toggle again → one run "abcd" with no `w:b`), `ToggleOnAMixedSelectionTurnsEverythingOn`, `ToggleOffAStyleValueWritesExplicitOff` (style bold, toggle → `w:b w:val="0"`), `ToggleOffADirectValueRemovesTheElement`, `SuperscriptAndSubscriptExcludeEachOther`, `FontWritesAsciiAndHAnsiAndDropsThemeAttributes`, `ColorDropsThemeAttributes`, `NoRunsAreMergedThatDifferInAttributesIgnoringRsid` and `ARunWithUnknownContentIsNeverMerged`, `OnlyTouchedRunsAreMerged`, `TypingFormatWinsOverTheNeighbourRun`, `AnEmptyParagraphFormatsTheParagraphMark`, `OneBlockChangePerOperationAndOneUndoStep` (assert `Changes.Count == 1`, `History.Undo()` restores the paragraph XML exactly, `History.Redo()` repeats it), `FormattingKeepsContainersFieldsAndUnknownContent` (Review Focus 2: corpus file with tracked changes, a hyperlink and a complex field; field instruction text unchanged, containers kept, save and reopen equal), `ASelectionAcrossStoriesOrACellEdgeChangesNothing`.
- [ ] **Step 2: Run** the class → FAIL.
- [ ] **Step 3: Implement.** Split at both edges with `InlineSplitter`, apply the changes to the covered runs' `Properties` (`PropertyBag.Set`/`Remove`, children placed by `SchemaOrder`), then merge only the runs this operation touched; toggles read `IEffectiveFormat.ForRun` for every covered run (null format → throw `InvalidOperationException` naming the missing resolver). Record one history step via `History.Record` exactly as `InsertText` does (typed = null, so no coalescing).
- [ ] **Step 4: Run** `*CharacterFormattingTests`, `*TextEditorTests`, `*EditHistoryTests` → PASS.
- [ ] **Step 5: Commit** `feat(write): character formatting operations (M4.3b)`.

### Task 3: Paragraph formatting and style application

**Files:**
- Create: `src/Raffica.Write.Editing/ParagraphChanges.cs`, `TextEditor.ParagraphFormat.cs`
- Test: `tests/Raffica.Write.Tests/Editing/ParagraphFormattingTests.cs`

**Interfaces:**
- Consumes: `PropertyChange` (Task 2).
- Produces: static `ParagraphChanges.Alignment(ParagraphJustification)` (enum `Left, Center, Right, Justify`), `Indent(float? leftPt, float? rightPt, float? firstLinePt, float? hangingPt)` (a null keeps that attribute, hanging and first line exclude each other), `Spacing(float? beforePt, float? afterPt, float? linePt, LineRuleKind? rule)`, `Switch(ParagraphSwitch which, bool on)` (enum `KeepNext, KeepLines, PageBreakBefore, WidowControl, ContextualSpacing`); on `TextEditor`: `EditResult ApplyParagraphChanges(TextRange selection, IReadOnlyList<PropertyChange> changes)`, `EditResult ApplyParagraphStyle(TextRange selection, string? styleId)`, `EditResult ApplyCharacterStyle(TextRange selection, string? styleId)` (null removes the `rStyle` or `pStyle`).

- [ ] **Step 1: Write the failing tests:** `AlignmentSetsJcOnEveryTouchedParagraph`, `IndentKeepsTheAttributesItDoesNotName`, `HangingAndFirstLineExcludeEachOther`, `SpacingWritesTwipsAndLineRule`, `ChoosingTheStyleValueStillWritesTheOverride` (spec section 4), `ApplyParagraphStyleKeepsDirectOverrides`, `ApplyParagraphStyleToAnUndefinedIdIsAllowedAndKept`, `ApplyCharacterStyleNoneRemovesRStyle`, `ParagraphsInCellsNotesAndHeadersFormat` (each story kind), `AFileWithoutAStylesPartDoesNotThrow` (Review Focus 4), `SelectAllBoldOnTheThesisIsOneUndoStepAndFast` (Review Focus 1: thesis, `ToggleCharacter` over the whole body; asserts one history step, undo restores, and the whole call is under 5 s; if the step would exceed `HistoryLimits.MaxBytes` the operation is refused with `EditResult` without changes and the test pins that refusal instead; record which of the two happens in the decision of Task 14).
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement** with the same pattern as Task 2 (clone the touched blocks, mutate `Paragraph.Properties`, `Edits.Raise()`, one `BlockChange` over the contiguous block range, `History.Record`). **Step 4: Run** the class → PASS. **Step 5: Commit** `feat(write): paragraph formatting and style application (M4.3b)`.

### Task 4: The formatting inspector, override removal and Clear direct formatting

**Files:**
- Create: `src/Raffica.Write.Editing/FormattingInspector.cs`, `OverrideRow.cs`, `TextEditor.Overrides.cs`
- Test: `tests/Raffica.Write.Tests/Editing/FormattingInspectorTests.cs`, `OverrideRemovalTests.cs`

**Interfaces:**
- Consumes: Tasks 2 and 3.
- Produces: `public enum OverrideScope { Paragraph, Run, ParagraphMark }`; `public enum OverridePresence { All, Partly, Mixed }`; `public sealed record OverrideRow(OverrideScope Scope, XName Element, OverridePresence Presence, string? Value)` (`Value` is the element's `w:val`, or its attributes joined as `name=value` pairs when it has no `w:val`; the row carries **no** English sentence, the app owns the wording in Task 12); `public sealed record InspectorView(string? ParagraphStyleId, string? CharacterStyleId, bool ParagraphStyleMixed, bool CharacterStyleMixed, IReadOnlyList<OverrideRow> Rows)`; `public static class FormattingInspector { InspectorView Read(WordDocument document, TextRange selection); }`; on `TextEditor`: `EditResult RemoveOverride(TextRange selection, OverrideScope scope, XName element)` and `EditResult ClearDirectFormatting(TextRange selection)`.
- Row rules (spec section 4): rows are the direct children of a paragraph's `pPr` and its runs' `rPr` **except** `pStyle`, `rStyle`, `numPr`, `sectPr`, the mark's `rPr` (rows with scope `ParagraphMark` only for a paragraph without text), `rPrChange`, `pPrChange`; presence `All` when every touched paragraph/run has the element with an equal serialized value, `Mixed` when all have it with different values, `Partly` when only some have it. Removal never merges runs. Clear keeps `pStyle`, `rStyle`, `numPr`, `sectPr`, `*Change`, and the mark's `rPr` unless the paragraph is empty or the selection covers it.

- [ ] **Step 1: Write the failing tests.** `FormattingInspectorTests`: `ListsTheStylesAndEveryDirectRow`, `ExcludesTheStructuralElements`, `ParagraphMarkRowsOnlyForAnEmptyParagraph`, `PartlyAndMixedOverSeveralParagraphsAndRuns`, `UnknownElementsGetARowNamedByTheElement`. `OverrideRemovalTests` (the **acceptance criterion**): `RemovingARowRemovesExactlyThatElement` (a paragraph and a selection with eight different overrides; for each row remove it, serialize the whole document with `DocxFile`'s writer, and assert it equals the serialization before **minus that one element** (a helper that deletes the element from the XML text of the "before" document)), `RemovingNeverMergesRuns` (two runs that become equal stay two), `NothingElseChangesNotTheStyleNorTheOtherRows`, `EveryOverrideOfEveryParagraphAndRunInTheCorpusRemovesOnlyItself` (corpus loop, each override removed one at a time on a clone of the document, same assertion, in the order the rows list), `ClearRemovesAllRowsKeepsStylesAndList`, `ClearKeepsTheMarkOfAParagraphWithText`, `RemoveAndClearAreOneUndoStep`.
- [ ] **Step 2: Run** `*FormattingInspectorTests` and `*OverrideRemovalTests` → FAIL. **Step 3: Implement** (pure reads in `FormattingInspector`, writes in `TextEditor.Overrides.cs` with the Task 2 pattern; "partly" removal acts on every run that has the element). **Step 4: Run** both classes → PASS (the corpus test takes a while; run it once in Release and note the time). **Step 5: Commit** `feat(write): formatting inspector and override removal (M4.3b)`.

### Task 5: Style steps in the history and "Update style to match"

**Files:**
- Create: `src/Raffica.Write.Editing/StyleChange.cs`, `TextEditor.UpdateStyle.cs`
- Modify: `src/Raffica.Write.Model/Style.cs` (add `Clone()`), `src/Raffica.Write.Model/StyleSheet.cs` (add `bool Replace(Style old, Style replacement)`, in place at the same index), `src/Raffica.Write.Editing/EditResult.cs`, `src/Raffica.Write.Editing/EditHistory.cs`
- Test: `tests/Raffica.Write.Tests/Editing/StyleHistoryTests.cs`, `UpdateStyleToMatchTests.cs`, `tests/Raffica.Write.Tests/StylesNumberingSettingsTests.cs` (clone)

**Interfaces:**
- Produces: `public sealed record StyleChange(Style Old, Style New)` (read-only snapshots, `Old` is a clone); `EditResult(TextPosition Caret, IReadOnlyList<BlockChange> Changes, TextRange? Selected = null, StyleChange? Style = null)`; `Style Style.Clone()` (copies the three bags, every string and the kept children/attributes); `EditHistory.Step` gets `StyleChange? Style`: undo puts `Old` back with `StyleSheet.Replace`, redo `New`, in the same step as the block swap; `Record` accepts a result with zero block changes when `Style` is set; `SizeOf` counts a style (strings plus its bags); a style step sets `_open = null`. `TextEditor`: `EditResult? UpdateStyleToMatch(TextRange selection)` returning null (changes nothing) for the refusals below.
- Semantics (spec section 4): acts on the first paragraph of the selection; refused when the selection touches paragraphs of different styles or the style id is not defined in the file; writes the paragraph's direct paragraph overrides and the character overrides present on every run of the paragraph (equal values) into the style's `ParagraphProperties` and `RunProperties`, removes exactly those from the paragraph and its runs, leaves `basedOn`, `next`, `link` and every other paragraph untouched; one undo step containing the style swap and the block change.

- [ ] **Step 1: Write the failing tests.** `StyleHistoryTests`: `UndoRestoresTheStyleAndTheParagraphTogether`, `RedoRepeatsBoth`, `AStyleStepEndsTypingCoalescing`, `UndoToTheSavedRevisionIsCleanAgain`, `ADocumentChangedBehindTheHistoryClearsIt`, `ANewEditAfterUndoDropsTheRedo` (Review Focus 5), `TheStyleCountsTowardsTheByteLimit`. `UpdateStyleToMatchTests`: `DirectOverridesMoveIntoTheStyleAndLeaveTheParagraph`, `OnlyCharacterOverridesOnEveryRunMove`, `OtherParagraphsOfTheStyleKeepTheirDirectFormatting`, `BasedOnNextAndLinkAreUntouched`, `AMixedStyleSelectionIsRefused`, `AnUndefinedStyleIsRefused` and `AFileWithoutAStylesPartIsRefused` (Review Focus 4), `StyleClone` in `StylesNumberingSettingsTests` (clone equals the original by serialization and shares no object).
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** Generalize `Undo`, `Redo` and `Finish` of `EditHistory` so the block swap is optional; keep `Validate`, the revision bookkeeping and `Trim` as they are. **Step 4: Run** the three classes plus `*EditHistoryTests` → PASS. **Step 5: Commit** `feat(write): style steps in the history and Update style to match (M4.3b)`.

### Task 6: The layout restyles

**Files:**
- Modify: `src/Raffica.Write.Layout/Pages/LayoutEdits.cs` (public `Restyle`), `tests/Raffica.Write.Tests/Layout/CorpusIncrementalTests.cs`, `tests/Raffica.Write.Tests/Editing/RealEditorOracleTests.cs`
- Test: `tests/Raffica.Write.Tests/Layout/RestyleTests.cs`

**Interfaces:**
- Produces: `public EditRefill DocumentLayout.Restyle(int throughPage)`: under the layout gate (`_gate`), faults like `ApplyEdit`, calls the private `Restart(throughPage)` (a fresh `Engine.Build` creates a new `StyleResolver` over `document.Styles`), counts an edit and a restart in `EditStats`, returns the `EditRefill`.

- [ ] **Step 1: Write the failing tests.** `RestyleTests`: `ChangingAStyleAndRestylingEqualsAFullLayout` (thesis, change `Heading1` size, `Restyle(2)`, drain, `LayoutOracle.AssertEqualToFull`), `WithoutRestyleTheLayoutDoesNotSeeAStyleChange` (documents why the call exists), `RestyleCountsARestart`, `RestyleAfterAFaultThrows`. Extend `CorpusIncrementalTests` seeded edits with formatting steps (character toggle on a random range, a paragraph alignment, a paragraph style, an `UpdateStyleToMatch` followed by `Restyle`) for every corpus file, both views, and the forced steps list of `RealEditorOracleTests` with the same four.
- [ ] **Step 2: Run** `*RestyleTests` → FAIL; **Step 3: Implement** `Restyle`; **Step 4: Run** `*RestyleTests`, `*CorpusIncrementalTests`, `*RealEditorOracleTests`, `*IncrementalCoverageTests` → PASS (no tolerance added anywhere). **Step 5: Commit** `feat(write): DocumentLayout.Restyle and formatting oracle steps (M4.3b)`.

### Task 7: The session applies style steps, follows the selection and holds the pending format

**Files:**
- Create: `src/Raffica.Write/Editing/PendingFormat.cs`
- Modify: `src/Raffica.Write/Editing/EditSession.cs`, `src/Raffica.Write/Documents/WriteTab.cs` (the effective-format instance), `src/Raffica.Write/Viewing/PageView.Editing.cs` (typing consults the pending format)
- Test: `tests/Raffica.Write.App.Tests/Editing/FormattingSessionTests.cs`

**Interfaces:**
- Consumes: `EditResult.Style`, `DocumentLayout.Restyle`, `IEffectiveFormat`.
- Produces: `EditSession.Apply` calls `Restyle(throughPage)` on the active layout (and drops the other view's layout as it does for blocks) when `result.Style` is set, then applies the block changes; `event Action? SelectionChanged` on `EditSession` raised by `SetSelection` when the selection really changed (positions or reference differ), never during a replayed save queue batch more than once; `internal sealed class PendingFormat { IReadOnlyList<PropertyChange> Changes { get; } void Set(IReadOnlyList<PropertyChange>); void Clear(); }` owned by the session, cleared on `SelectionChanged`, on any `Apply` that is not a typing insert, on Esc and on `Changed`; the `TextEditor` is made with a `LayoutEffectiveFormat` over the current document and the session makes a new one after every style step, undo or redo that carries a `Style`.

- [ ] **Step 1: Write the failing tests:** `AStyleResultRestylesTheActiveLayoutAndDropsTheOther`, `SelectionChangedFiresOnlyForARealChange`, `ThePendingFormatAppliesToTheNextTypedTextOnly`, `ACaretMoveClearsThePendingFormat`, `AnImeCommitDoesNotTakeAStaleFormat` (Review Focus 3), `PendingFormatNeverReachesTheModelHistoryOrRevision` (revision and `History.UndoCount` unchanged by setting it), `ToggleAfterARestyleSeesTheNewStyle` (new effective instance), `FormattingWhileSavingIsQueuedLikeTyping`, `AFaultedSessionRefusesFormatting`.
- [ ] **Step 2: Run** `dotnet test tests/Raffica.Write.App.Tests -c Release --filter-class "*FormattingSessionTests"` → FAIL. **Step 3: Implement.** **Step 4: Run** it plus `*EditSessionTests`, `*TypingTests`, `*ImeTests` → PASS. **Step 5: Commit** `feat(write): session support for style steps, selection events and the pending format (M4.3b)`.

### Task 8: Commands, rules, strings and menu entries

**Files:**
- Modify: `src/Raffica.Write/Commands/WriteCommandIds.cs`, `WriteCommandCatalog.cs`, `WriteCommandRules.cs`, `EditCommandState.cs`, `src/Raffica.Write/Platform/MacOS/WriteMacMenu.cs`, `src/Raffica.Write/Resources/Strings.en.resx`, `Strings.de.resx`, the Lucide key list of the shell if a new icon is missing (`src/Raffica.Shell/Styles/Icons.axaml`)
- Test: `tests/Raffica.Write.App.Tests/Commands/FormatCommandTests.cs`, existing `WriteCommandTests.cs`, `TranslationTests.cs`

**Interfaces:**
- Produces: ids `format.bold`, `format.italic`, `format.underline`, `format.strike`, `format.superscript`, `format.subscript`, `format.smallCaps`, `format.allCaps`, `format.alignLeft`, `format.alignCenter`, `format.alignRight`, `format.justify`, `format.indentMore`, `format.indentLess`, `format.keepNext`, `format.keepLines`, `format.pageBreakBefore`, `format.widowControl`, `format.clearDirect`, `format.updateStyle`, `view.formatting`; the value commands with a payload (font, size, size up/down, colour, highlight, spacing before/after, line spacing, paragraph style, character style) are ids too (`format.font`, `format.size`, `format.sizeUp`, `format.sizeDown`, `format.color`, `format.highlight`, `format.spaceBefore`, `format.spaceAfter`, `format.lineSpacing`, `format.paragraphStyle`, `format.characterStyle`) whose payload the picker passes to the action. Home tab groups `GroupFont` (digit 5), `GroupParagraph` (6), `GroupStyles` (7); View group `GroupPanes` gets `view.formatting` (toggle). `EditCommandState` gains `InFormattingScope` facts the rules need (`HasStylesPart`, `StyleDefined`, `StyleMixed`); `ReasonFor` gives a reason for each disabled state (blocked/saving/faulted as the edit commands, "style not defined in the file", "selection mixes styles", "no formatting to clear").

- [ ] **Step 1: Write the failing tests:** `EveryFormatCommandHasItsOwnLucideIcon` (extend the existing pin), `EveryFormatCommandHasAReasonInEveryDisabledState` (walk every id in every `EditBlockedReason` and style state like `PdfCommandRulesTests`), `UpdateStyleIsDisabledWithoutADefinedStyleAndWhenMixed`, `FieldCodesAreUnique`, `EveryFormatMacMenuItemExists`, and the `TranslationTests` run (every new key in both files; German uses "Sie" and passes the `Wording` guards that Write has, or the shell's).
- [ ] **Step 2: Run** `*FormatCommandTests`, `*WriteCommandTests`, `*TranslationTests` → FAIL. **Step 3: Implement** (icon choices are the builder's: one Lucide glyph per meaning, none shared; wording of the toggle commands names the state change, not "toggle"). **Step 4: Run** the three → PASS. **Step 5: Commit** `feat(write): formatting commands, rules and strings (M4.3b)`.

### Task 9: Keys and command handlers in the page view

**Files:**
- Modify: `src/Raffica.Write/Viewing/PageView.Keys.cs`, `PageView.Commands.cs`, `src/Raffica.Write/WriteWindow.axaml.cs` (action wiring in `CreateActions`), `src/Raffica.Write/Editing/EditingKeys.cs`
- Test: `tests/Raffica.Write.App.Tests/Editing/FormattingKeysTests.cs`, `tests/Raffica.Write.App.Tests/Viewing/PageViewFormattingTests.cs`

**Interfaces:**
- Consumes: the commands of Task 8, `EditSession.Apply`, `PendingFormat`.
- Produces: shortcuts Ctrl+B, Ctrl+I, Ctrl+U, Ctrl+E, Ctrl+L, Ctrl+R, Ctrl+J (⌘ on macOS) owned by the page view like the clipboard keys (so `WriteCommandCatalog.Find` skips them, as for `edit.*`: extend `WriteCommandIds.IsEditing` or add `IsFormatting`); AltGr is text, never a shortcut; a character command with a collapsed selection sets the `PendingFormat` (and, in an empty paragraph, also formats the mark through `ApplyCharacterChanges`), with a selection runs through `session.Apply`; each handler passes `throughPage` like the clipboard handlers.

- [ ] **Step 1: Write the failing tests:** `CtrlBoldTogglesTheSelection`, `CtrlBoldAtACaretSetsThePendingFormat`, `AltGrAndCtrlAltAreTextNotShortcuts`, `NoFormattingKeyIsInTheReservedOrExistingKeys` (check the new keys against `Raffica.Pdf`'s `ReservedShortcuts` list **by value** copied into the test, since Write must not reference Pdf, and against the keys of Write's catalog), `MacUsesTheCommandModifier`, `AFormattingKeyInTheOutlineListBelongsToTheList` (`InOutline`), `KeyRepeatDoesNotRepeatAToggle`, `FormattingInAHeaderRunsThroughTheSameSessionPath`.
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** both classes plus `*EditingKeysTests`, `*TypingTests` → PASS. **Step 5: Commit** `feat(write): formatting keys and handlers in the page view (M4.3b)`.

### Task 10: A picker item in the shell ribbon

**Files:**
- Create: `src/Raffica.Shell/Frame/RibbonPicker.cs`, `src/Raffica.Shell/Styles/Frame/RibbonPicker.axaml`
- Modify: `src/Raffica.Shell/Frame/RibbonDefinition.cs`, `CommandDefinition.cs` (a picker kind next to the button kind), `RibbonView.cs`, `src/Raffica.Shell/Resources/Strings.en.resx`/`.de.resx` if the control has its own strings; the gallery's frame demo gets one picker so it can be looked at
- Test: `tests/Raffica.Shell.Tests/RibbonPickerTests.cs`

**Interfaces:**
- Produces: a ribbon item `RibbonPicker` built on the shell's `SelectBox` with: an item list (`IReadOnlyList<PickerItem(string Id, string Label)>`), a selected id or "mixed" display, an editable text variant for the size (numbers only, Enter commits, Esc reverts), `PickerChosen` callback (id or typed text), enabled state and disabled reason like `RibbonButton`, key tip support (`BadgePlacement`, `BadgeFit` pass), UIA name and value, compact-bar behaviour (collapses to the picker's label in the overflow like a button), tokens only (no hex).

- [ ] **Step 1: Write the failing tests:** `ThePickerListsItemsAndReportsTheChosenId`, `MixedShowsNoSelection`, `TheSizeBoxCommitsOnEnterAndRevertsOnEscape`, `ADisabledPickerShowsItsReason`, `TheKeyTipFitsTheBadge`, `TheCompactBarKeepsThePickerReachable`, `NoHexInComponentStylesTests` (existing, must stay green), `TheGalleryShowsThePicker`. A live look at the AOT gallery on a real display is an owner gate.
- [ ] **Step 2: Run** `dotnet test tests/Raffica.Shell.Tests -c Release --filter-class "*RibbonPickerTests"` → FAIL. **Step 3: Implement.** **Step 4: Run** the shell tests fully (`dotnet test tests/Raffica.Shell.Tests -c Release`) → PASS. **Step 5: Commit** `feat(shell): a picker item for the ribbon (M4.3b)`.

### Task 11: The Home groups, the pickers and the style lists

**Files:**
- Create: `src/Raffica.Write/Commands/StylePickerSource.cs`, `FontPickerSource.cs`
- Modify: `src/Raffica.Write/Commands/WriteRibbon.cs`, `src/Raffica.Write/WriteWindow.View.cs`
- Test: `tests/Raffica.Write.App.Tests/Commands/StylePickerSourceTests.cs`, `tests/Raffica.Write.App.Tests/WriteWindowFormattingTests.cs`

**Interfaces:**
- Produces: `internal static class StylePickerSource { IReadOnlyList<PickerItem> Paragraph(WordDocument document); IReadOnlyList<PickerItem> Character(WordDocument document); }` (rules of spec section 4: `qFormat` or in use, not `semiHidden`/`hidden`, `uiPriority` then name, the default style first; an id the file does not define but a paragraph uses is listed as its id; a document without a styles part gives an empty list and the picker is disabled with a reason); `FontPickerSource.Families(FontCatalog)` (shipped faces first, then installed families, names only, no font loading); `WriteRibbon` builds Font, Paragraph and Styles groups from the catalog and the pickers with the actions of Task 9; the pickers refresh on `EditSession.SelectionChanged` (debounced like the pane, read on the UI thread) and show "mixed" for mixed values.

- [ ] **Step 1: Write the failing tests:** `ParagraphStylesAreListedByTheRules` (hidden and unused latent styles excluded, order), `AnUndefinedButUsedStyleIsListedAsItsId` and `NoStylesPartGivesAnEmptyDisabledPicker` (Review Focus 4), `FontListNeverLoadsAFace` (no font file read), `ThePickerShowsMixedForAMixedSelection`, `ChoosingAStyleInThePickerAppliesItInOneUndoStep`, `TheRibbonHasTheThreeNewGroupsWithTheirFieldCodes`, `StartupCreatesNoPickerWork` (added to `StartupTests`).
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** the new classes plus `*WriteWindowTests`, `*StartupTests` → PASS. **Step 5: Commit** `feat(write): Home formatting groups and pickers (M4.3b)`.

### Task 12: The formatting pane

**Files:**
- Create: `src/Raffica.Write/Panes/FormattingPane.cs`, `FormattingRowWording.cs`, `src/Raffica.Write/WriteWindow.Formatting.cs`
- Modify: `src/Raffica.Write/WriteWindow.View.cs` (`view.formatting` toggle, `IsCommandOn`), `src/Raffica.Write/Resources/Strings.en.resx`/`.de.resx`
- Test: `tests/Raffica.Write.App.Tests/Panes/FormattingPaneTests.cs`

**Interfaces:**
- Consumes: `FormattingInspector.Read`, `TextEditor.RemoveOverride`/`ClearDirectFormatting`/`UpdateStyleToMatch`, `EditSession.SelectionChanged` and `Changed`.
- Produces: `FormattingPane : UserControl` in `Frame.RightPane` (256 px, `Pane` with `PaneFocus` so F6 reaches it), one per tab made on first open (`WriteWindow.Formatting`), refreshed 100 ms after the last selection or edit change on the UI thread only; `FormattingRowWording.Describe(OverrideRow)` maps every element Write knows to its English/German wording ("Bold", "Space after 12 pt", "Font Carlito"; sizes and spacing in points) and names any other element by its local name; the pane has the two style lines, the rows each with a remove button (accessible name "Remove <wording>"), the buttons "Update style to match" and "Clear direct formatting", the one-time note that the style is shared, disabled reasons from `WriteCommandRules`, and never shows document text.

- [ ] **Step 1: Write the failing tests:** `ThePaneShowsStyleLinesAndRows`, `RemovingARowRemovesExactlyThatOverrideInTheDocument` (through the pane, compares the model as in Task 4), `PartlyAndMixedRowsShowTheirState`, `TheButtonsRunTheCommandsAsOneUndoStep`, `ThePaneFollowsTheCaretAndEdits` (debounce, UI thread), `ThePaneDoesNotExistBeforeFirstOpen` (added to `StartupTests.StartupCreatesNoEditorWork`), `EveryKnownElementHasWordingInBothLanguages` (guard like `PageLossWordingTests`), `TheSharedStyleNoteShowsOncePerTab`, `AFaultedSessionDisablesTheButtonsWithAReason`, `F6ReachesThePane`.
- [ ] **Step 2: Run** `*FormattingPaneTests` → FAIL. **Step 3: Implement.** **Step 4: Run** it plus `*OutlinePaneTests`, `*StartupTests`, `*TranslationTests` → PASS. **Step 5: Commit** `feat(write): the formatting inspector pane (M4.3b)`.

### Task 13: Measuring a formatting edit

**Files:**
- Modify: `bench/write-layout/*` (`edit` command gets `format` cases), `src/Raffica.Write/Measurement*.cs` (switch `--measure-format`), `bench/shell-probe/measure.cs` (case `format`, limit 16.0 ms in `s_writeBudgets` on every platform, judged on CPU cost like `typing`; `selftest` updated)
- Test: `tests/Raffica.Write.Tests/Layout/EngineBenchTests.cs`, `tests/Raffica.Write.App.Tests/MeasurementTests.cs`

**Interfaces:**
- Produces: bench cases `format-word` (toggle bold on one word), `format-paragraph` (alignment of one paragraph) next to the typing cases, same places as `EditCases`/`TypingPlaces` (change together); app switch `--measure-format` (needs `--settings-file` and a file, like the other measurement switches, one per run).

- [ ] **Step 1: Write the failing tests:** `FormatCasesExistInTheBenchAndRun`, `MeasureFormatNeedsASettingsFileAndAFile`, `TheFormatCaseIsJudgedAgainst16Ms` (in `measure.cs selftest`, run `dotnet run bench/shell-probe/measure.cs -- selftest` → ok), `TheBenchAndTheAppUseTheSamePlaces`.
- [ ] **Step 2: Run** the two classes → FAIL. **Step 3: Implement** by extending the typing machinery (do not copy it). **Step 4: Run** the classes and the selftest → PASS; **then measure** on a Native AOT publish on the Windows PC: `write-layout-bench edit --file testdata/write-corpus/thesis/thesis.docx` and `RafficaWrite --measure-format` (3 launches), plus the style update (`Restyle`) time to the pages in view and to the full layout; record the numbers for Task 14. The Linux laptop and the Intel Mac are owner gates (`gh workflow run measure.yml --ref <ref> -f platform=<windows|linux|macos> -f target=write -f note="<machine>" -f ref=<ref>`). **Step 5: Commit** `feat(write): measure a formatting edit (M4.3b)`.

### Task 14: Documentation, roadmap and the full proof

**Files:**
- Modify: `Docs/DECISIONS.md` (entry "Raffica Write: formatting and styles (M4.3b)": the design, the deviation about `IEffectiveFormat`, the select-all outcome of Task 3, the numbers of Task 13, every known limit of spec section 6), `CLAUDE.md` (section "Write formatting (M4.3b)" in the style of the neighbouring ones: where, rules to remember, pitfalls, owner gates), `Docs/roadmap.json` and generated `Docs/ROADMAP.md`, `Docs/write-word-check.md` (rows for formatting and a style update), `CHANGELOG.md` (`[Unreleased]`: formatting, styles and the inspector in Write)

- [ ] **Step 1:** Run the full proof and record each result: `dotnet build Raffica.slnx -c Release` (0 warnings), `RAFFICA_REQUIRE_OCR=1 dotnet test Raffica.slnx -c Release --no-build --max-parallel-test-modules 1` (totals, failures, skips), `dotnet format Raffica.slnx --verify-no-changes` (exit 0), `dotnet run tools/roadmap.cs -- check`; Native AOT publish of `Raffica.Write` and `bench/write-layout` with the log search `IL[23][0-9]{3}` (0 hits).
- [ ] **Step 2:** Edit `Docs/roadmap.json`: tick the task of 4.3.2 and the criterion "Every override row of the inspector removes exactly that override and nothing else (tests)" with `evidence` naming `OverrideRemovalTests` (both the row test and the corpus test) and the full-run totals; list as open, with their commands, the owner gates (a look at the AOT app on a real display: pickers, pane, key tips; Word accepts a file after formatting and a style update, `Docs/write-word-check.md`; the format case on the Linux laptop and the Intel Mac). Status stays `in-progress` while an owner gate is open, as 4.3.1 does. Run `dotnet run tools/roadmap.cs -- generate` then `-- check`.
- [ ] **Step 3:** Commit docs and roadmap together (`docs: M4.3b decision, roadmap and notes`), then open the PR to `dev` (squash allowed once CI is green; Claude merges it itself per `CLAUDE.md`).
