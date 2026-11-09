# Milestone 2.2 – Annotate: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Raffica PDF can add, select, change, delete, undo and redo markup, comments, drawings and text boxes, and save them (save, save as, signed-file warning, failure cards, close prompt) through the engine that Core already has.

**Architecture:** Core already holds the whole engine: `AnnotationEditor` (one per document, one undo history, `Changed` event, `SaveAsync`), `DrawingHitTest`, `SelectionHandles`, `DrawingTransform`, `StrokeSimplifier`, `ArrowGeometry`, `TextBoxSpec`, `AnnotationPalette`/`DrawPalette`, `SaveFailureException`, `SignaturePolicy`. Committed annotations render through PDFium, so the app draws only live previews and selection handles. 2.2 adds the app side: a per-tab `AnnotationSession` that owns the editor and drives the dirty state, a tool model, pointer interaction on `PageView`, the marks pane, the Annotate ribbon tab, the save controller with its cards, and the settings rows. Logic without a window (session, tool state, marks model, save controller, geometry of previews) is plain classes tested without Avalonia; controls are tested headless as in 2.1.

**Tech Stack:** .NET 10 Native AOT, Avalonia 12.1.4, xunit.v3 3.2.2 with `Avalonia.Headless.XUnit` (as `tests/Raffica.Pdf.Tests`), Core only for PDF work; no new package.

**Spec:** [Docs/superpowers/specs/2026-11-04-raffica-pdf-design.md](../specs/2026-11-04-raffica-pdf-design.md): sections 2 to 4 and 6 (saving, dirty state, security, signature wording), 7 (row 2.2), 8, 9. Frame rules: "App frame conventions (M1.2)" and "Raffica PDF: viewer conventions (M2.1)" in [Docs/DECISIONS.md](../../DECISIONS.md). Behaviour rules come from the predecessor's specs `2026-10-24-teilprojekt2-annotieren-design.md` and `2026-10-25-m10-zeichnen-formen-design.md` (read-only checkout `C:\VS-Code\Pagewing`, tag `v1.0.0`, read with `git show`); the rules a task uses are cited in English in that task.

## Global Constraints

- **Stack and layering:** `Raffica.Pdf` references `Raffica.Core` and `Raffica.Shell` only and knows no PDFium type. Every Core call is awaited, the UI thread never waits for PDFium. `AnnotationEditor.Changed` and the save `IProgress<double>` arrive on other threads: marshal to the UI thread before touching a control or `PdfTab`; never wait synchronously on the editor from a `Changed` or `PagesInvalidated` handler (deadlock).
- **Look:** direction D, PDF ink plum, shell tokens and controls only, no hex literal in a component style (`NoHexInComponentStylesTests`). Colour swatches show the colours of Core's palettes (`AnnotationPalette`, `DrawPalette`), which are data, not style literals. A new colour token is not planned; if one becomes necessary, the checklist in `CLAUDE.md` section "Shell" applies. Selected states are never shown by colour alone.
- **Strings:** every UI string in `Strings.en.resx` and `Strings.de.resx` of the app project with an accessor in `PdfStrings`; `TranslationTests` and `CommandCatalogTests` must stay green. No UI text calls a visible signature or a signed file "valid" or "legally binding".
- **Saving rules (spec 6):** incremental, atomic and checked (Core does it); no autosave, no recovery file; a failed save leaves the original untouched and the tab dirty with its history; a saved change cannot be undone (Core clears the history on save).
- **Security:** nothing in a PDF runs; annotation text is plain text, nothing is rendered as markup.
- **Not in this milestone:** form fields, signature images (`ApplySignatureImageAsync`), page operations, redaction, OCR, sticky notes, reply threads, shape fill, pressure-sensitive width, autosave, a full-rewrite option for annotation saves, installers, updater, macOS, anything beyond the predecessor's 1.0 features.
- **Done** means: `dotnet build Raffica.slnx -c Release` (0 warnings), `RAFFICA_REQUIRE_OCR=1 dotnet test Raffica.slnx -c Release --no-build --max-parallel-test-modules 1`, `dotnet format Raffica.slnx --verify-no-changes` and `dotnet run tools/roadmap.cs -- check` green, each acceptance criterion with its proof in `Docs/roadmap.json`. Local builds may need `-p:NuGetAudit=false` (known advisory, see the 2.1 evidence).
- **Commits:** Conventional Commits with the trailer `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`; stage files by name; `git show --stat HEAD` after each commit; noreply address only. Branch `feature/m22-annotate`; PR to `dev` at the end, merged when `ci` and `roadmap` are green. Tick roadmap tasks as they finish (`dotnet run tools/roadmap.cs -- generate`). Tests that save work on a temp copy of the fixture (the save replaces the file).

## Calls made while planning (author's calls, the owner can overturn them)

1. **One right pane, the marks pane.** The approved canvas (`Docs/design/direction-d/Pdf.dc.html`) shows one right pane "Markierungen" listing highlights, notes, signatures and pending redactions; it has no separate comments pane. The spec names both a 256 px marks pane and a 300 px comments pane. The existing empty `PdfWindow.CommentsPane` (300 px, field code 7) becomes the **marks pane at 256 px**; a mark's comment is shown and edited in its entry, as the predecessor's single panel did. The 300 px figure stays a shell metric for the other apps. Cheap to change: only Task 9 depends on it.
2. **Tool mode kept from the predecessor.** Choosing a tool in the Annotate tab starts the annotate mode; choosing the active tool again, "Done" or Esc ends it. Outside the mode the viewer behaves exactly as in 2.1 (text selection, links). Tool letters (I, R, E, L, A, T) work only inside the mode and never while a text field has focus.
3. **`PdfTab.IsDirty` stays the flag the UI reads;** the new `AnnotationSession` sets it (editor dirty or busy, or a pending draft). `MarkDirty`/`MarkClean` stay for the 2.1 tests.
4. **Core gaps are fixed in Core, with a Core test,** only where a task says "check first": hit-testing of text boxes, the "may annotate" permission bit.
5. **No new document-wide listing in Core:** the app's `MarksModel` loops `GetAnnotationsAsync` per page at background priority after the first sharp page and refreshes only the pages named in `PagesInvalidated`.
6. **Comments are `/Contents` of a markup or drawing** (Core's model); no sticky note, no popup, no replies. Annotations that have replies are read-only (`Editable == false`).
7. **Foreign marks:** marks written by other programs are listed; markup is selectable, commentable and deletable; drawings and text boxes Core marks `GeometryEditable == false` can only be commented (where editable) and deleted, with a lock icon and the reason.

## Review Focus

1. **The window or a tab is closed while a stroke, a nudge or a text-box draft is still in flight.** Expected: the pending edit is awaited, the tab counts as dirty, the close card asks. Pinned by `CloseWithPendingEditTests.ClosingRightAfterAMarkupStillAsks` (Task 2).
2. **Save fails** (read-only target, file changed by another program, encrypted file, disk full, verification failed). Expected: a card names the reason and says the original is unchanged, the tab stays dirty, history intact, nothing half-written. Pinned by `SaveControllerTests.EveryFailureReasonEndsInACardAndTheTabStaysDirty` (Task 11).
3. **The file is digitally signed** (DocMDP 1 or 2 against 3 or none). Expected: levels 1 and 2 warn with Cancel as the default, others show a note once per tab; wording never says valid or legally binding. Pinned by `SaveCardsTests.SignedWarningDefaultsToCancel` and `NoUiTextCallsAnythingLegallyBinding` (Task 11).
4. **A foreign or locked annotation is clicked, nudged or recoloured.** Expected: only select, comment and delete work; the lock reason is shown; no handle appears. Pinned by `SelectToolTests.LockedDrawingOffersOnlyCommentAndDelete` (Task 8).
5. **A selection or stroke spans pages, rotated pages, a CropBox offset, or the 10 000-annotation limit is hit.** Expected: one markup per page, geometry correct after reload, the limit reported on a card and nothing created. Pinned by `MarkupToolTests.SelectionAcrossPagesCreatesOneMarkupPerPage` and `AnnotationLimitTests.LimitIsReportedAndNothingIsCreated` (Tasks 5 and 6).

---

### Task 1: Roadmap, scope changes and decisions

**Done while planning (2026-11-04):** the steps below were carried out in the planning commit, together with this file. The implementer starts at Task 2 and only revisits the scope change and the DECISIONS entry if a call changes.

**Files:**
- Modify: `Docs/roadmap.json`, `Docs/ROADMAP.md` (generated), `Docs/DECISIONS.md`

- [x] **Step 1: Link this plan** in milestone 2.2 (`plan`), set `status` to `planned`; add the tasks of this plan's scope to the milestone text only where the existing four tasks do not already say it (do not anticipate later milestones).
- [x] **Step 2: Record scope changes** in `roadmap.json` (`scopeChanges`, context "Milestone 2.2"): calls 1 (marks pane replaces the comments pane slot), 2 and 6; and the predecessor items not rebuilt (no sticky notes, no reply threads, no shape fill, no pen pressure, no full-rewrite option for annotation saves).
- [x] **Step 3: Write** one `Docs/DECISIONS.md` entry "Raffica PDF: annotation conventions (M2.2)" with calls 1 to 7 (context, decision, rationale, rejected alternatives).
- [x] **Step 4: Run** `dotnet run tools/roadmap.cs -- generate`, then `-- check`. Expected: both pass.
- [x] **Step 5: Commit** `docs: plan milestone 2.2`.

### Task 2: Per-tab annotation session and dirty state

**Files:**
- Create: `src/Raffica.Pdf/Annotating/AnnotationSession.cs`
- Modify: `src/Raffica.Pdf/Documents/PdfTab.cs`, `src/Raffica.Pdf/Documents/CloseGuard.cs` (only if the flush needs a hook), `src/Raffica.Pdf/PdfWindow.axaml.cs`
- Test: `tests/Raffica.Pdf.Tests/Annotating/AnnotationSessionTests.cs`, `CloseWithPendingEditTests.cs`

**Interfaces:**
- Produces: `sealed class AnnotationSession : IDisposable` with ctor `(PdfTab tab, SynchronizationContext ui)`; `AnnotationEditor Editor`; `bool IsDirty` (editor dirty or busy or pending > 0); `bool CanUndo`, `bool CanRedo`; `event Action? StateChanged` (UI thread); `IDisposable BeginPending()` (a draft or stroke registers itself); `Task FlushAsync()` (awaits pending drafts, then `Editor.WhenIdleAsync()`); `Task UndoAsync()`, `Task RedoAsync()` (flush first). `PdfTab.Annotations` (lazy, created on first use, disposed with the tab). Tests may inject a fake `ui` context.

- [ ] **Step 1: Write the failing tests:** `ChangeMarksTheTabDirtyAndTheDotAppears` (apply a highlight on `text-rich.pdf`, then `tab.IsDirty` and `tab.Tab.IsDirty`), `ChangedFromAnotherThreadIsMarshalledToTheUiContext`, `BusyEditorCountsAsDirty`, `PendingDraftCountsAsDirtyUntilDisposed`, `UndoToTheSavedVersionIsStillDirty` (Core rule: undo then redo stays dirty), `FlushWaitsForAnInFlightStroke`, and in `CloseWithPendingEditTests` `ClosingRightAfterAMarkupStillAsks` (call close while the apply task is not awaited; expect the unsaved-changes card).
- [ ] **Step 2: Run** `dotnet test tests/Raffica.Pdf.Tests -c Release --filter-namespace "*Annotating*"`. Expected: FAIL (types missing); check the count is not zero.
- [ ] **Step 3: Implement** the session: subscribe to `Editor.Changed`, post to `ui`, recompute, call `PdfTab.MarkDirty()`/`MarkClean()` and raise `StateChanged`; `PdfWindow` calls `tab.Annotations.FlushAsync()` before the close prompt for that tab (in `PromptAsync`/`CanCloseAsync` path) and keeps `RefreshCommandStates()` wired to `StateChanged`.
- [ ] **Step 4: Run** the same command plus `--filter-namespace "*Documents*"` (the 2.1 close tests stay green). Expected: PASS.
- [ ] **Step 5: Commit** `feat(pdf): per-tab annotation session drives the dirty state`.

### Task 3: Tool model and annotation settings

**Files:**
- Create: `src/Raffica.Pdf/Annotating/AnnotateTool.cs`, `ToolState.cs`
- Modify: `src/Raffica.Pdf/Settings/PdfSettingsController.cs`, `src/Raffica.Pdf/PdfStrings.cs`, both `Strings.*.resx`
- Test: `tests/Raffica.Pdf.Tests/Annotating/ToolStateTests.cs`, `Settings/PdfSettingsTests.cs` (extend)

**Interfaces:**
- Produces: `enum AnnotateTool { None, Select, Highlight, Underline, StrikeOut, Freehand, Rectangle, Ellipse, Line, Arrow, TextBox }`; `sealed class ToolState` with ctor `(Func<AppSettings> read, Action<AppSettings> write)`; `AnnotateTool Tool { get; }`, `bool Choose(AnnotateTool)` (choosing the active tool again returns to `None`), `void End()`, `event Action? Changed`; `AnnotationColor MarkupColor(AnnotateTool)`; `ToolStyle StyleOf(AnnotateTool)`; `void SetColor(AnnotateTool, AnnotationColor)`; `void SetWidth(AnnotateTool, StrokeWidth)`; `string? Author` (setting `AuthorName`, blank is `null`).
- Consumes: Core's `AppSettings.StyleOf`, `WithToolStyle`, `HighlightColor`, `UnderlineColor`, `StrikeOutColor`, `AuthorName`; `PdfSettingsController.App` and its background-saving setter pattern (`RememberPrinter`).

- [ ] **Step 1: Write the failing tests:** `ChoosingTheActiveToolEndsTheMode`, `ColourAndWidthAreRememberedPerTool`, `MarkupDefaultsAreYellowBluePink` (255,235,59 / 124,196,250 / 255,143,199), `DrawingDefaultIsRedMedium` (220,30,30, 2 pt), `TextBoxUsesFontSizesInPlaceOfWidths` (12, 16, 24; default 12), `BlankAuthorIsNull`, and in the settings tests `AuthorNameRowIsReadBackAndLimitedTo128Characters`.
- [ ] **Step 2: Run** `--filter-class "*ToolStateTests"`. Expected: FAIL.
- [ ] **Step 3: Implement** `ToolState` over the two delegates (copy-on-write through `with`); add the "Author name" extra row to `PdfSettingsController.Open` (TextBox `PART_AuthorName`, `AutomationProperties.Name`, max length 128) and persist through the existing settings path; add strings.
- [ ] **Step 4: Run** the new tests and `--filter-class "*PdfSettingsTests"`. Expected: PASS.
- [ ] **Step 5: Commit** `feat(pdf): tool state and the author name setting`.

### Task 4: Save and undo commands, Annotate tab and the destructive flag

**Files:**
- Modify: `src/Raffica.Pdf/Commands/PdfCommandIds.cs`, `PdfCommandCatalog.cs`, `PdfCommands.cs`, `PdfCommandRules.cs`, `PdfShortcut.cs` (only if a helper is missing), `src/Raffica.Pdf/PdfRibbon.cs`, `PdfWindow.Commands.cs`, `src/Raffica.Shell/Styles/Icons.axaml` (new icons only if no existing one fits, each with a note in the catalog), both `Strings.*.resx`, `PdfStrings.cs`
- Test: `tests/Raffica.Pdf.Tests/Commands/CommandCatalogTests.cs`, `RibbonDefinitionTests.cs`, `ShortcutTests.cs`, `Annotating/AnnotateCommandRulesTests.cs`

**Interfaces:**
- Produces: command ids `file.save`, `file.saveAs`, `edit.undo`, `edit.redo`, `annotate.mode`, `annotate.highlight`, `annotate.underline`, `annotate.strikeOut`, `annotate.select`, `annotate.freehand`, `annotate.rectangle`, `annotate.ellipse`, `annotate.line`, `annotate.arrow`, `annotate.textBox`, `annotate.done`, `annotate.marks` (toggle of the right pane); a new ribbon tab "Annotate" (letter `A`, groups: Markup, Draw, Edit with the separated destructive group holding delete); `CatalogGroup` gets a `Destructive` flag passed through `PdfRibbon.Create` to the shell's `RibbonGroupDefinition(..., Destructive: true)`. Rules: `Save` needs a dirty tab and no running save or print, `SaveAs` needs a document, `Undo`/`Redo` need `CanUndo`/`CanRedo`, tool commands need a document and the "may annotate" permission (reason string when forbidden). `PdfCommandRules` gets delegates `Func<bool> isDirty`, `canUndo`, `canRedo`, `busySaving`, `annotateAllowed`.
- Shortcuts (predecessor's, ported): Ctrl+S, Ctrl+Shift+S, Ctrl+Z, Ctrl+Shift+Z and Ctrl+Y (redo, alternates), Ctrl+Shift+K (mode), Ctrl+Shift+H/U/X (markup, applied at once when a text selection exists), Ctrl+Shift+M (select). Save, undo and redo ignore key repeat only where the existing rule says so (`AllowsRepeat`: undo and redo may repeat, save may not). Undo and redo yield to a focused text field exactly like Copy and Select all in `OnShortcutKeyDown`. Esc is not in the catalog.

- [ ] **Step 1: Write the failing tests:** extend `RibbonDefinitionTests` (tab letters `["H","A","V"]` or the order the ribbon uses, new command count, field codes unique and prefix-free per tab, exactly one destructive group), `CommandCatalogTests` (name keys exist in en and de, no shortcut clashes), `ShortcutTests` (`CtrlSSavesOnlyWhenDirty`, `CtrlZYieldsToATextField`, `CtrlShiftZAndCtrlYRedo`, `KeyRepeatDoesNotSaveTwice`), `AnnotateCommandRulesTests` (`ToolsAreDisabledWithAReasonWhenTheDocumentForbidsAnnotating`).
- [ ] **Step 2: Run** `--filter-namespace "*Commands*"`. Expected: FAIL on the new expectations; the old hard-coded counts (29 commands, tab letters) are updated in the same step.
- [ ] **Step 3: Implement** ids, catalog entries (existing icon names where possible), `PdfCommands` slots and `RunOf` arms, rules arms and `ReasonFor`, the `Destructive` pass-through, `CreateRules()` and `CreateCommands()` wiring to the session and tool state (actions are stubs that call `ToolState.Choose` and the session until Tasks 5 to 11 fill them in); call `RefreshCommandStates()` on `ToolState.Changed` and `AnnotationSession.StateChanged`.
- [ ] **Step 4: Run** `--filter-namespace "*Commands*"` and `--filter-class "*PaletteSourceTests"` (new commands appear in the palette automatically). Expected: PASS.
- [ ] **Step 5: Commit** `feat(pdf): annotate ribbon tab, save and undo commands`.

### Task 5: Overlay layer, annotate interaction and the markup tools

**Files:**
- Create: `src/Raffica.Pdf/Annotating/AnnotateInteraction.cs`, `MarkupController.cs`, `src/Raffica.Pdf/Viewing/IPageOverlay.cs`
- Modify: `src/Raffica.Pdf/Viewing/PageView.cs` (an `Overlay` property drawn after `DrawMarks`, invalidated on its `Changed`), `src/Raffica.Pdf/Text/TextInteraction.cs` (yield pointer events while a tool is active), `PdfWindow.axaml.cs` (create per tab with the other per-tab state, `AnnotateOf(tab)` test seam)
- Test: `tests/Raffica.Pdf.Tests/Annotating/MarkupToolTests.cs`, `AnnotationLimitTests.cs`, `PermissionTests.cs`

**Interfaces:**
- Produces: `interface IPageOverlay { event Action? Changed; void Render(DrawingContext context, PageView view); }`; `AnnotateInteraction(PdfTab tab, PageView view, ToolState tools, SelectionController selection, DialogQueue dialogs)` attaches tunnel/bubble handlers and routes to the tool controllers; `MarkupController.ApplyAsync(AnnotateTool kind)` builds `lineRects` per page from `SelectionController.RectsOn(page)` and calls `Editor.ApplyMarkupAsync(page, kind, rects, color, null, tools.Author)` once per page, then clears the selection.
- Rules (predecessor): with a markup tool active, releasing after a drag, a double click (word) or triple click (line) applies the markup; a click under 4 DIP does nothing; a double click waits for the platform double-click time so a triple click makes one markup; a drag that starts on a link becomes a selection while a tool is active; trailing spaces are not part of the rects; nothing is read or created before the first sharp page; the mode is refused with a card naming the reason when the document forbids annotating (check first whether Core exposes the "may annotate" permission next to the print permission `PdfPrintInfo`; if not, add it to Core with a Core test in `tests/Raffica.Core.Tests`).

- [ ] **Step 1: Write the failing tests** (use `TextFixture`/`WindowFixture`, a manual clock, temp copy of `text-rich.pdf` and of a rotated and a CropBox fixture): `ReleaseAfterDragAppliesTheMarkupAndClearsTheSelection`, `DoubleClickMarksAWordAndTripleClickOneLine`, `ClickWithoutMovementDoesNothing`, `SelectionAcrossPagesCreatesOneMarkupPerPage`, `ColourComesFromTheToolStateAndAuthorIsSetWhenNotBlank`, `RotatedPageAndCropBoxMarkupSurvivesReload` (compare `QuadRects` after save and reopen), `ForbiddenDocumentRefusesTheModeWithACard`, `NothingIsCreatedBeforeTheFirstSharpPage`, `LimitIsReportedAndNothingIsCreated` (fixture or fake at 10 000: `PdfAnnotationLimitException` becomes an error card), `OutsideTheModeTextSelectionAndLinksWorkAsIn2_1` (the 2.1 `SelectionTests`/`LinkCardTests` stay green).
- [ ] **Step 2: Run** `--filter-namespace "*Annotating*"`. Expected: FAIL.
- [ ] **Step 3: Implement** the overlay hook and the interaction router; `TextInteraction` stays the default and is bypassed only while `ToolState.Tool != None`; Esc ends the mode (Esc order: stroke in progress, drag, selection, text selection, mode, search). Handle `OperationCanceledException` and the limit exception without throwing out of the handler.
- [ ] **Step 4: Run** `--filter-namespace "*Annotating*"` and `--filter-namespace "*Text*"`. Expected: PASS.
- [ ] **Step 5: Commit** `feat(pdf): markup tools on the page view`.

### Task 6: Drawing tools

**Files:**
- Create: `src/Raffica.Pdf/Annotating/DrawingController.cs`, `StrokePreview.cs`
- Modify: `AnnotateInteraction.cs`
- Test: `tests/Raffica.Pdf.Tests/Annotating/DrawingToolTests.cs`

**Interfaces:**
- Produces: `DrawingController` turns pointer input into `DrawingGeometry` (`Ink`, `Line`, `Arrow`, `Rectangle`, `Ellipse`) and calls `Editor.ApplyDrawingAsync(page, geometry, color, null, tools.Author)`; `StrokePreview : IPageOverlay` draws the live shape in page space with no Core call until release.
- Rules (predecessor): the tool stays active after a stroke; points live in page points of the start page, clamped to the page; points closer than 0.25 pt to the previous one are dropped, at most 20 000 points per stroke (`MaxStrokePoints`); on release `StrokeSimplifier.Simplify` (epsilon 0.5 pt) then `Smooth` run off the UI thread and strokes are applied in release order; a click shorter than 4 DIP draws nothing; mouse and pen draw (the pen captures the pointer so it does not scroll), touch scrolls and zooms; no pressure width; rectangle and ellipse are outlines, Shift makes a square or circle (`ArrowGeometry.SquareBox`); line and arrow Shift snaps to 45 degree steps (`ArrowGeometry.SnapToAngle`); widths thin, medium, thick are 1, 2, 4 pt, default red 220,30,30 medium; each stroke registers `AnnotationSession.BeginPending()` until it is applied.

- [ ] **Step 1: Write the failing tests:** `FreehandStrokeIsSimplifiedAndApplied`, `ClickShorterThan4DipDrawsNothing`, `PointsBeyondTheLimitAreIgnoredNotCrashed`, `PenDraws`, `TouchDoesNotDrawButScrolls`, `ShiftSnapsALineTo45Degrees`, `ShiftMakesASquareAndACircle`, `ArrowIsRecognisedAfterReload` (`AnnotationKind.Arrow` after save and reopen), `StrokesAreAppliedInReleaseOrder`, `StrokeStartingNearThePageEdgeStaysOnThePage`, `ToolStaysActiveAfterAStroke`, `EveryToolSurvivesSaveReopenAndUndo` (one theory over Freehand, Rectangle, Ellipse, Line, Arrow: draw, save to a temp copy, reopen, geometry equal within 0.5 pt, undo removes it).
- [ ] **Step 2: Run** `--filter-class "*DrawingToolTests"`. Expected: FAIL.
- [ ] **Step 3: Implement** the controller and preview; the simplify and smooth work runs in `Task.Run`, results are chained through one task per tab so order holds.
- [ ] **Step 4: Run** the same command. Expected: PASS.
- [ ] **Step 5: Commit** `feat(pdf): freehand and shape tools`.

### Task 7: Text box tool and editing field

**Files:**
- Create: `src/Raffica.Pdf/Annotating/TextBoxController.cs`, `TextBoxEditor.cs`
- Modify: `AnnotateInteraction.cs`, `src/Raffica.Pdf/Styles/PageView.axaml` (editor field style, shell tokens only), strings
- Test: `tests/Raffica.Pdf.Tests/Annotating/TextBoxToolTests.cs`

**Interfaces:**
- Produces: `TextBoxController` creates a box by drag (at least 16 pt in both directions) or a default 160 x 48 pt box at the click, shifted onto the page; `TextBoxEditor : UserControl` is the field placed over the box (Geist at `FontSize x zoom`, annotation colour on paper white, accent border); commit calls `Editor.ApplyTextBoxAsync(page, new TextBoxSpec(box, text, fontSize, color), tools.Author)` or `ReplaceTextBoxAsync` when editing an own box. Uses `TextBoxSpec.CheckText`/`NormalizeText`/`Validate`, `ToolFontSizes` 12/16/24.
- Rules (predecessor): Ctrl+Enter or leaving the field commits, Esc discards, Ctrl+Z belongs to the field; empty text creates nothing; a text outside the 318-character set (or a tab, an emoji) is rejected as a whole, the field stays open and names up to 8 offending characters, nothing is replaced or dropped; at most 2 000 characters and 200 lines; the box grows downward to fit at most to the page edge and never shrinks, text is never cut off; a draft registers `BeginPending()`; Enter on a selected own text box reopens it; foreign text boxes cannot be edited, only deleted.

- [ ] **Step 1: Write the failing tests:** `DragBelow16PointsMakesTheDefaultBox`, `CtrlEnterCommitsAndEscDiscards`, `EmptyTextCreatesNothing`, `RejectedCharactersKeepTheFieldOpenAndAreNamed`, `GermanUmlautsAndEuroSurvive` ("Grüße aus Köln – 5 €" after save and reopen), `BoxGrowsToFitTheTextAndNeverShrinks`, `DraftCountsAsDirtyAndIsFlushedBeforeSave`, `EnterReopensAnOwnTextBox`, `ForeignTextBoxCannotBeEdited`.
- [ ] **Step 2: Run** `--filter-class "*TextBoxToolTests"`. Expected: FAIL.
- [ ] **Step 3: Implement** the controller and the editor field; sizes replace widths in the style row for this tool.
- [ ] **Step 4: Run** the same command. Expected: PASS.
- [ ] **Step 5: Commit** `feat(pdf): text box tool`.

### Task 8: Select, move, resize, colour, delete

**Files:**
- Create: `src/Raffica.Pdf/Annotating/SelectController.cs`, `SelectionOverlay.cs`
- Modify: `AnnotateInteraction.cs`, (if the check below fails) `src/Raffica.Core/Annotations/DrawingHitTest.cs` with a test in `tests/Raffica.Core.Tests/Annotations/DrawingHitTestTests.cs`
- Test: `tests/Raffica.Pdf.Tests/Annotating/SelectToolTests.cs`

**Interfaces:**
- Produces: `SelectController` holds the selected `AnnotationLocator` (by `Name`, never by index) and a cache of the page's `PdfAnnotation` list refreshed on `PagesInvalidated`; picks with `DrawingHitTest.Pick(onPage, DisplayPoint, tolerance)` (6 DIP plus half the stroke width); `SelectionOverlay : IPageOverlay` draws the frame and the handles from `SelectionHandles.For(annotation)` (8 DIP drawn, 24 DIP hit area); move and resize use `DrawingTransform.Move/Resize/MoveEndpoint` and `SelectionHandles.ResizeBy` and commit one `Editor.ReplaceGeometryAsync`/`ReplaceTextBoxAsync` per drag; colour and width change commit one `Editor.UpdateAsync` (colour and width together are one step); delete is `Editor.DeleteAsync`.
- Rules (predecessor): check first whether `DrawingHitTest.Pick`/`IsSelectable` cover text boxes (a Core test decides; extend Core if not); only editable annotations are picked; drawings lie above markups, shapes hit on the outline only; markup has a frame but no handles; no handles for locked foreign drawings and text boxes, which keep select, comment, delete with a lock icon and the reason; minimum size 4 pt, kept on the page, Shift keeps the aspect ratio; Tab and Shift+Tab step through the page's selectable annotations in reading order (top, left, index) and never trap focus (after the last one the selection clears and focus leaves the view); arrow keys move 1 pt, Shift 10 pt, clamped to the page, presses within 400 ms are one history step; Delete and Backspace delete; Esc order as in Task 5; selection follows the `Name` across undo.

- [ ] **Step 1: Write the failing tests:** `PicksTheTopmostEditableAnnotation`, `RectangleHitsOnTheOutlineOnly`, `DragMovesAsOneUndoStep`, `ResizeKeepsMinimumSizeAndStaysOnThePage`, `NudgesWithin400msAreOneUndoStep`, `ColourAndWidthChangeIsOneUndoStep`, `DeleteThenUndoRestoresTheSameName`, `TabLeavesTheViewAfterTheLastAnnotation`, `LockedDrawingOffersOnlyCommentAndDelete` (fixture `drawings-foreign.pdf`), `TextBoxIsPickable`, `SelectionSurvivesUndoByName`, `UndoAndRedoShareOneHistoryAcrossAllKinds` (markup, drawing, text box mixed, 500-step cap is Core's and not re-tested).
- [ ] **Step 2: Run** `--filter-class "*SelectToolTests"`. Expected: FAIL.
- [ ] **Step 3: Implement** the controller and overlay; every commit goes through the session so pending state and dirty are right.
- [ ] **Step 4: Run** the same command plus `dotnet test tests/Raffica.Core.Tests -c Release --filter-namespace "*Annotations*"` if Core changed. Expected: PASS.
- [ ] **Step 5: Commit** `feat(pdf): select, move, resize and delete marks`.

### Task 9: Marks model and marks pane

**Files:**
- Create: `src/Raffica.Pdf/Annotating/MarksModel.cs`, `MarkEntry.cs`, `src/Raffica.Pdf/Panes/MarksPane.cs`
- Modify: `src/Raffica.Pdf/PdfWindow.axaml.cs` (the right pane becomes the marks pane, 256 px, per-tab like `TabPanes`), `src/Raffica.Pdf/Styles/Panes.axaml`, strings
- Test: `tests/Raffica.Pdf.Tests/Annotating/MarksModelTests.cs`, `Panes/MarksPaneTests.cs`

**Interfaces:**
- Produces: `sealed record MarkEntry(AnnotationLocator Locator, PdfAnnotation Annotation, string? Excerpt, string? LockReason)`; `enum MarkFilter { All, Highlight, Underline, StrikeOut, Drawings, Other }`; `MarksModel(PdfTab tab)` with `Task RefreshAsync(IReadOnlySet<int>? pages)` (null means all pages at background priority after the first sharp page), `IReadOnlyList<MarkRow> Rows(MarkFilter)`, `event Action? Changed`; `MarkRow` is either an entry or a group "Freehand, N strokes"; `MarksPane : UserControl` with a `ListBox.nav-list`, a filter `SelectBox`, one comment editor below the list acting on the selected entry, and a summary line.
- Rules (predecessor): lists every annotation except links, form fields and popups; others as "Other: <subtype>"; sort by page, `Bounds.Top`, `Bounds.Left`, index; five or more freehand strokes without a comment on a page collapse into one row (`GroupThreshold` 5), Enter or Space expands it, strokes with a comment are never grouped; entry shows page tag, kind (icon and text), author and date, excerpt of at most 120 characters (`AnnotationExcerpt.For`, markup only), the comment, colour; click or Enter selects the mark in the document and reveals its rectangle (`PageView.Reveal`); only pages named in `PagesInvalidated` are re-read so scroll position and selection stay; comment editing: multiline, Ctrl+Enter applies (empty removes the comment via `AnnotationEdit(Contents: "")`), Esc discards, Ctrl+Z stays in the field; non-editable entries show a read-only note; delete in the pane only inside the mode; empty document text "This document has no marks." The pane's help text says a deleted mark stays in earlier revisions of the file. Summary lines: "1 mark on page N", "N marks, all on page M", "N marks on M pages".

- [ ] **Step 1: Write the failing tests:** `PopupsAndRepliesAreNotListed`, `SortOrderIsPageTopLeftIndex`, `GroupsManyCommentlessStrokesAndNeverGroupsCommentedOnes`, `OnlyInvalidatedPagesAreReRead`, `NothingIsReadBeforeTheFirstSharpPage`, `FilterChangesTheSummary`, `ApplyingAnEmptyCommentRemovesIt`, `LockedEntryShowsTheReasonAndNoEditor`, `ClickRevealsTheMarkAndSelectsIt`, `PaneIsPerTabAndSurvivesASave`, `PaneJoinsTheF6RingWithFieldCode7`.
- [ ] **Step 2: Run** `--filter-namespace "*Panes*"` and `--filter-class "*MarksModelTests"`. Expected: FAIL.
- [ ] **Step 3: Implement** the model (page loop through `PdfDocument.GetAnnotationsAsync` at background priority) and the pane; reuse `NavList` focus helpers.
- [ ] **Step 4: Run** the same commands plus `--filter-class "*PaneFocusTests"`. Expected: PASS.
- [ ] **Step 5: Commit** `feat(pdf): marks pane with comments`.

### Task 10: Save controller

**Files:**
- Create: `src/Raffica.Pdf/Documents/SaveController.cs`
- Modify: `src/Raffica.Pdf/PdfWindow.axaml.cs` (the `CloseGuard` save delegate is `SaveController.SaveAsync` instead of `_ => Task.FromResult(false)`), `PdfWindow.Commands.cs`, `src/Raffica.Pdf/Printing/PrintCommand.cs` (mutual blocking)
- Test: `tests/Raffica.Pdf.Tests/Documents/SaveControllerTests.cs`, `SaveCommandTests.cs`

**Interfaces:**
- Produces: `SaveController(DialogQueue dialogs, Func<PdfTab, Task<string?>> pickSavePath, PdfEngine engine)` (the engine is the one that opened the file; reuse the way `OpenFlow` gets it); `Task<bool> SaveAsync(PdfTab tab)` (true when saved), `Task<bool> SaveAsAsync(PdfTab tab)`, `bool IsSaving`, `event Action? Changed`. Flow: `tab.Annotations.FlushAsync()`, evaluate `SignaturePolicy.Evaluate(await document.GetSignatureInfoAsync())`, show the signed card when needed, call `Editor.SaveAsync(engine, target, overwriteChangedOriginal, progress, token)`, on success `tab.MarkClean()`, update `PdfTab.Path` for Save As (and the tab title), keep the view's scroll, zoom, layout and selection (Core adopts the reopened document).
- Rules: Save is only offered when dirty; Save as as soon as a document is open; `OriginalChanged` asks overwrite or Save as through the changed-file card then retries with `overwriteChangedOriginal: true`; `SourceModified` and `EncryptedNotSupported` offer Cancel only (changes stay in the tab); any other `SaveFailureException`, and `OperationCanceledException`, leave the tab dirty with its history; progress is marshalled to the UI thread and cancel is wired to the job strip pattern of printing; Save is blocked while printing and printing while saving; the signed-file note for DocMDP 3 or none appears once per tab and document; a rewrite is never chosen (annotation saves are incremental only).

- [ ] **Step 1: Write the failing tests:** `SaveCleansTheTabAndKeepsScrollAndZoom`, `SaveAsChangesPathAndTitle`, `EveryFailureReasonEndsInACardAndTheTabStaysDirty` (a fake saver throwing each `SaveFailureReason`), `SourceModifiedKeepsTheTabAndItsEdits`, `ChangedOriginalAsksOverwriteOrSaveAs`, `CancelLeavesTargetUntouchedAndNoTempFile` (`.pw-*.tmp` absent), `SaveAndPrintBlockEachOther`, `CloseGuardSaveThenCloseSavesFirst`, `SavedFileReopensWithTheMarks`.
- [ ] **Step 2: Run** `--filter-namespace "*Documents*"`. Expected: FAIL.
- [ ] **Step 3: Implement** the controller and wire the commands and the guard.
- [ ] **Step 4: Run** `--filter-namespace "*Documents*"` and `--filter-namespace "*Printing*"`. Expected: PASS.
- [ ] **Step 5: Commit** `feat(pdf): save and save as through the core pipeline`.

### Task 11: Save cards and failure texts

**Files:**
- Create: `src/Raffica.Pdf/Dialogs/SignedFileCard.cs`, `ChangedOriginalCard.cs`, `SaveFailedCard.cs`
- Modify: `UnsavedChangesCard.cs` (locked "Close and discard changes" variant for tabs that cannot be saved), `PdfStrings.cs` (`SaveFailure(SaveFailureReason)` switch that throws `ArgumentOutOfRangeException` for an undefined value), both `Strings.*.resx`, `Styles/*` if a card needs one
- Test: `tests/Raffica.Pdf.Tests/Dialogs/SaveCardsTests.cs`

**Interfaces:**
- Produces: `SignedFileCard.Create(string fileName, SignatureAdvice advice, Action<SignedChoice> done)` with `enum SignedChoice { Cancel, SaveAs, SaveAnyway, Continue }`; `ChangedOriginalCard.Create(string fileName, Action<ChangedChoice> done)` with `enum ChangedChoice { Cancel, SaveAs, Overwrite }`; `SaveFailedCard.Create(string fileName, SaveFailureReason reason, Action done)`; all through `DialogQueue`, buttons `PART_*` as in `ErrorCard`.
- Wording (predecessor, translated): signed with DocMDP 1 or 2: title "This signature does not allow comments", text "After saving, programs such as Acrobat will show it as invalid.", choices Cancel (default), Save as, Save anyway; signed with DocMDP 3 or none: note "This document is signed" with "Raffica PDF only appends your changes; the signed bytes stay unchanged."; changed file: "Overwrite", "Save as" (default), "Cancel"; every failure text states that the original is unchanged. Dangerous choices ("Don't save", "Save anyway", "Overwrite", "Close and discard") are never the default and are disabled for the first 400 ms; Esc cancels.

- [ ] **Step 1: Write the failing tests:** `SignedWarningDefaultsToCancel`, `LevelThreeIsOnlyANote`, `DangerousChoicesAreDisabledFor400ms`, `EscCancelsEveryCard`, `EveryFailureReasonHasAnEnglishAndAGermanText` (iterates `Enum.GetValues<SaveFailureReason>`), `FailureTextsSayTheOriginalIsUnchanged`, `NoUiTextCallsAnythingLegallyBinding` (scans the en and de resources for "valid", "gültig", "legally binding", "rechtsverbindlich" in the signature texts), `UnsavedChangesCardForAnUnsavableTabHasOnlyCloseAndCancel`.
- [ ] **Step 2: Run** `--filter-class "*SaveCardsTests"`. Expected: FAIL.
- [ ] **Step 3: Implement** the cards and strings; the 400 ms lock uses the injectable clock of the other cards (`FakeTime` in tests).
- [ ] **Step 4: Run** the same command and `--filter-class "*TranslationTests"`. Expected: PASS.
- [ ] **Step 5: Commit** `feat(pdf): signed-file, changed-file and save failure cards`.

### Task 12: Dark page, print and review of the whole path

**Files:**
- Test only (code changes only for what the tests show): `tests/Raffica.Pdf.Tests/Annotating/EndToEndTests.cs`

- [ ] **Step 1: Write the tests:** `EveryToolSurvivesSaveReopenAndUndoThroughTheWindow` (the window path: open a temp copy, choose each tool by command id, draw, Ctrl+S, close, reopen, marks present, Ctrl+Z on a fresh edit removes only that edit), `DarkPageKeepsMarkupVisible` (render with the dark page on and compare the marked tile against the unmarked one), `PrintKeepsAnnotationsOnALightPage` (the print path with the fake service still passes `Annotations: true`), `UndoAfterSaveIsDisabled` (history cleared by Core).
- [ ] **Step 2: Run** `--filter-class "*EndToEndTests"`. Expected: PASS or a finding; a failing case is fixed in the task that owns the code, not weakened here.
- [ ] **Step 3: Run the full verification:** `dotnet build Raffica.slnx -c Release`, `RAFFICA_REQUIRE_OCR=1 dotnet test Raffica.slnx -c Release --no-build --max-parallel-test-modules 1`, `dotnet format Raffica.slnx --verify-no-changes`. Expected: 0 warnings, 0 failed.
- [ ] **Step 4: Commit** `test(pdf): annotate end to end`.

### Task 13: Close the milestone

**Files:**
- Modify: `Docs/roadmap.json`, `Docs/ROADMAP.md` (generated), `CHANGELOG.md` (`[Unreleased]`: annotate and save in Raffica PDF), `CLAUDE.md` (section "PDF app": the annotate files, the one right pane, the settings row), `Docs/DECISIONS.md` (only if a call changed while building)

- [ ] **Step 1: Tick** the four tasks of 2.2 and the first acceptance criterion with its proof (the command and the test counts from Task 12). Tick the second criterion only after the manual check: open a saved temp copy with marks of every kind in an independent viewer (for example Adobe Acrobat Reader, Chrome or Edge, Okular) on the owner's machines; record the viewer's name and version. Until then it stays unticked, listed under an "Open items" block with the exact steps, as in 2.1.
- [ ] **Step 2: Record** what could not be verified here (real pens and touch, high-DPI looks, a signed file in Acrobat, Linux and macOS window managers) in the open items with the command or check.
- [ ] **Step 3: Run** `dotnet run tools/roadmap.cs -- generate`, then `-- check`; set the milestone to `done` with `completedOn` only if every criterion is ticked, otherwise to `in-progress` and say so in the PR.
- [ ] **Step 4: Open the PR** to `dev` (`gh pr create`), wait for `ci` and `roadmap` green, merge (squash allowed) per `CLAUDE.md`.
- [ ] **Step 5: Commit** `docs: close milestone 2.2` (before the PR).
