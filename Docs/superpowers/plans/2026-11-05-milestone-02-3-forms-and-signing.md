# Milestone 2.3 – Forms and signing: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Raffica PDF fills AcroForm fields (text, checkbox, radio, list, combo) with the keyboard or the mouse, warns about empty required fields before saving, and places a visible signature (drawn or imported, up to ten saved), all saved through the existing save pipeline.

**Architecture:** Core already holds everything below the UI: `PdfDocument.GetFormFieldsAsync`/`SetFormValueAsync` over PDFium's form engine (it writes the appearance and reads the value back), `AnnotationEditor.SetFormValueAsync` (one step in the same history as the annotations), `FormNavigation`, `FormValueRules`, `FormInfo` with its hints and `EmptyRequired`, `ViewerSession.SetFormHighlight`, `SignatureStore`, `SignatureCutout`, `SignaturePlacement`, `AnnotationEditor.ApplySignatureImageAsync` and `ApplyDrawingAsync` with `DrawingGeometry.Signature`. Printing of form values (also unsaved ones) is done in Core as well. 2.3 adds the app side: a per-tab `FormSession`, form fields that are live whenever no annotate tool is active (no separate fill mode), field editors over the page, markers and a badge, the required-field card in `SaveController`, and the signature library, its cards and its placement through the select tool. Logic without a window is plain classes tested without Avalonia; controls are tested headless as in 2.1 and 2.2.

**Tech Stack:** .NET 10 Native AOT, Avalonia 12.1.4, xunit.v3 3.2.2 with `Avalonia.Headless.XUnit` (as `tests/Raffica.Pdf.Tests`), Core only for PDF work; no new package.

**Spec:** [Docs/superpowers/specs/2026-11-04-raffica-pdf-design.md](../specs/2026-11-04-raffica-pdf-design.md): sections 2 to 4 and 6 (forms, signature wording, saving), 7 (row 2.3), 8, 9. Frame rules: "App frame conventions (M1.2)", "Raffica PDF: viewer conventions (M2.1)" and "Raffica PDF: annotation conventions (M2.2)" in [Docs/DECISIONS.md](../../DECISIONS.md). Behaviour rules come from the predecessor's specs `2026-10-26-m11-formulare-design.md` and `2026-10-29-m12-sichtbare-unterschrift-design.md` (read-only checkout `C:\VS-Code\Pagewing`, tag `v1.0.0`, read with `git show`); the rules a task uses are cited in English in that task. Where this plan departs from them it says so under "Calls made while planning".

## Global Constraints

- **Stack and layering:** `Raffica.Pdf` references `Raffica.Core` and `Raffica.Shell` only and knows no PDFium type. Every Core call is awaited, the UI thread never waits for PDFium. `AnnotationEditor.Changed`, `PagesInvalidated` and the save `IProgress<double>` arrive on other threads: marshal to the UI thread before touching a control or `PdfTab`; never wait synchronously on the editor from such a handler (deadlock). Do not hold a page handle across a form write (Core remark).
- **Look:** direction D, PDF ink plum, shell tokens and controls only, no hex literal in a component style (`NoHexInComponentStylesTests`). Required and read-only state is never shown by colour alone (a star and a lock with a tooltip and an automation name). A new colour token is not planned; if one becomes necessary, the checklist in `CLAUDE.md` section "Shell" applies. The field highlight colour is Core's (`FormHighlightColors`, drawn by PDFium).
- **Strings:** every UI string in `Strings.en.resx` and `Strings.de.resx` of the app project with an accessor in `PdfStrings`; `TranslationTests` and `CommandCatalogTests` must stay green. No UI text calls a visible signature or a signed file "valid" or "legally binding" (the guard test `NoUiTextCallsAnythingLegallyBinding` covers the new texts too). The signature hint is the predecessor's: "Replaces no handwritten signature where written form is required." and "This is an image of your signature, not a digital signature."
- **Forms (spec 6):** values are written by PDFium's form engine, never by the app; entry in Avalonia elements over the field; the limited character set of `FormCharacters`; a refused text is refused as a whole, never replaced or shortened; no calculation, format or check script runs (the app says so); buttons never run an action; XFA and digital signature fields are not supported.
- **Saving rules:** incremental, atomic and checked (Core does it); values and annotations go in one save; a saved change cannot be undone (Core clears the history); the required-field card never blocks.
- **Security and privacy:** nothing in a PDF runs. A saved signature never leaves the machine except inside the PDF the user saves; no log, error text or setting carries it. Imports are checked by size and header before decoding (`SignatureLimits`, `ImageHeader`).
- **Startup:** nothing of 2.3 is created before the first sharp page; a document without an AcroForm creates no form work and pays nothing (test); the signature folder is not read before the first use of the picker. Measurement runs (`--measure-*`) stay unchanged.
- **Not in this milestone:** certificate signatures, XFA, JavaScript, calculating fields without JavaScript, a "my data" profile, FDF/XFDF/CSV, form reset, flattening, typed-name signatures, initials, a placement ghost under the pointer, renaming a saved signature, pasting an image from the clipboard, page operations, redaction, OCR, installers, macOS.
- **Done** means: `dotnet build Raffica.slnx -c Release` (0 warnings), `RAFFICA_REQUIRE_OCR=1 dotnet test Raffica.slnx -c Release --no-build --max-parallel-test-modules 1`, `dotnet format Raffica.slnx --verify-no-changes` and `dotnet run tools/roadmap.cs -- check` green, each acceptance criterion with its proof in `Docs/roadmap.json`. Local builds may need `-p:NuGetAudit=false` (known advisory, see the 2.1 evidence).
- **Commits:** Conventional Commits with the trailer `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`; stage files by name; `git show --stat HEAD` after each commit; noreply address only. Branch `feature/m23-forms-and-signing`; PR to `dev` at the end, merged when `ci` and `roadmap` are green. Tick roadmap tasks as they finish (`dotnet run tools/roadmap.cs -- generate`). Tests that save work on a temp copy of the fixture; tests never write to the real signatures folder.

## Calls made while planning (author's calls, the owner can overturn them)

1. **No separate fill mode.** The predecessor has a "Fill form" mode with a hint, a toolbar and its own Esc. Here form fields are live whenever the document has fillable fields and no annotate tool is active; a click on a field starts editing, any other click behaves as in 2.1 (text selection, links). Choosing an annotate tool makes the fields inert and ends an open field edit first. Chrome and Edge work this way. The owner approved this on 2026-11-05. Cheap to change: only Task 5 decides who gets the press.
2. **Highlight** is a per-tab switch on the form badge, on by default for a document with fillable fields, not stored. PDFium draws it; the app draws stars (required) and locks (read-only or unsupported) and the focus ring.
3. **Required fields:** a counter on the badge, a "Next required field" command and the card before saving ("Go to first empty field" is the default, "Save anyway" never blocks), as the predecessor's card but with the counter and the command added.
4. **One notice line,** not one bar per hint: the badge names only what applies to this document (calculations or format scripts, XFA, signature fields, buttons), is dismissible and shown once per tab.
5. **Keyboard:** Tab and Shift+Tab inside the page view walk the fields in `FormNavigation` order and let the key pass at the ends (no focus trap, WCAG 2.1.2); Enter or Space acts on the focused field (toggle a check box or radio button, open an editor), Esc leaves it. Check boxes and radio buttons have no element of their own: the overlay draws the focus ring and the page view carries the automation name of the focused field.
6. **A Core permission is added:** `PdfDocument.MayFillFormsAsync` (the "fill forms" bit or the "annotate" bit, or an unencrypted or owner-opened document), tested in Core, because `MayAnnotateAsync` alone would lock forms of documents that allow only filling. DocMDP level 1 does not forbid filling by itself in the app; the signed-file card (Task 8) asks as for annotations.
7. **Slots on `PageView`:** the field editor reuses the existing `Field` slot (a field edit and a text box edit never exist together, a tool change commits or drops the first); a new `FormOverlay` slot draws the markers before `Overlay`; the `Badge` slot hosts a `BadgeHost` that shows the annotate badge while a tool is active and the form badge otherwise.
8. **Signing is one step:** the "Sign" command (Annotate tab, own group) opens the New-signature card straight away when no signature is saved, else the picker card with thumbnails; a pick places the signature in the centre of the visible part of the current page and selects it with the select tool, so handles and arrow keys finish the job. A drawn signature is an ink annotation with the subject "Signature" and keeps its colour changeable; an imported image offers neither colour nor width.
9. **The signature folder** is `UserDataPaths.SignaturesDirectory()`, but with `--settings-file` it lies in a `signatures` folder next to that file (`PdfSettingsFiles.SignaturesDirectoryFor`), so tests and measurement runs never touch the user's data.
10. **Printing:** no app work is planned for printing form values (Core prints strips with the current values, also unsaved ones); Task 12 checks that the print command flushes an open field edit first and that the Linux path carries unsaved values, and records any limit it finds.

## Review Focus

1. **A text the field cannot take** (an unsupported character, too long, a line break in a single-line field) is entered. Expected: the text stays in the editor with the reason named, nothing is written, the tab is dirty through `BeginUnresolved`, closing asks. Pinned by `FieldEditTests.RefusedTextStaysInTheEditorAndKeepsTheTabDirty` (Task 5).
2. **A field on a rotated page or on a page with a CropBox offset, at any zoom, scrolled partly out of view.** Expected: the editor sits exactly over the widget, follows zoom and scroll, and is hidden when the field is off screen. Pinned by `FieldEditorPlacementTests.EditorSitsOverTheFieldOnRotatedAndCropBoxPages` (Task 5).
3. **Save, print, close, tool change or undo while a field edit is open.** Expected: a valid text is committed first, a refused one stays and keeps the tab dirty, nothing is lost silently. Pinned by `FieldEditTests.SaveCommitsAnOpenFieldFirst` and `CloseWithOpenFieldStillAsks` (Tasks 5 and 8).
4. **Hostile, locked or signed forms:** `forms-hostile`, `forms-xfa`, `forms-calc`, `forms-signed`, a read-only field, a signature field, a button, a document that forbids filling. Expected: opens without a crash, locked fields show their reason and cannot be edited, the notice line names what the app does not do, buttons do nothing, a document that forbids filling offers no editing and says why. Pinned by `FormSafetyTests.HostileAndLockedFormsOfferOnlyWhatIsSafe` (Task 6).
5. **Signature inputs:** an 11th signature, a corrupt index, a file over 10 MiB or over 6000 pixels, a file that is not an image, an empty drawing, the history budget (`AnnotationHistoryFullException`) reached. Expected: a card names the reason, nothing is stored or placed, the library stays usable. Pinned by `SignatureCardTests.EveryRefusalEndsInAReasonAndNothingIsStored` (Task 10) and `SignaturePlacementTests.FullHistoryAsksToSaveFirst` (Task 11).

---

### Task 1: Roadmap, scope changes and decisions

**Done while planning (2026-11-05):** the steps below were carried out in the planning commit, together with this file. The implementer starts at Task 2 and only revisits the scope change and the DECISIONS entry if a call changes.

**Files:**
- Modify: `Docs/roadmap.json`, `Docs/ROADMAP.md` (generated), `Docs/DECISIONS.md`

- [x] **Step 1: Link this plan** in milestone 2.3 (`plan`), set `status` to `planned`.
- [x] **Step 2: Record scope changes** in `roadmap.json` (`scopeChanges`, context "Milestone 2.3"): calls 1 (no separate fill mode), 2 to 4 (highlight switch, required counter and command, one notice line), 6 (Core permission) and 8 (signing in one step, no ghost).
- [x] **Step 3: Write** one `Docs/DECISIONS.md` entry "Raffica PDF: forms and signing conventions (M2.3)" with calls 1 to 10 (context, decision, rationale, rejected alternatives).
- [x] **Step 4: Run** `dotnet run tools/roadmap.cs -- generate`, then `-- check`. Expected: both pass.
- [x] **Step 5: Commit** `docs: plan milestone 2.3`.

### Task 2: Seams in Core and in the settings files

**Files:**
- Modify: `src/Raffica.Core/Pdf/PdfDocument.cs` (or `PdfDocument.Forms.cs`), `src/Raffica.Pdf/Settings/PdfSettingsFiles.cs`, `src/Raffica.Pdf/PdfWindow.axaml.cs`, `PdfWindow.Commands.cs`
- Test: `tests/Raffica.Core.Tests/Forms/MayFillFormsTests.cs`, `tests/Raffica.Pdf.Tests/Settings/PdfSettingsFilesTests.cs` (extend)

**Interfaces:**
- Produces: `Task<bool> PdfDocument.MayFillFormsAsync(CancellationToken = default)` (same shape as `MayAnnotateAsync`: true for an unencrypted document, one opened with its owner password, or one whose permissions carry the fill-forms bit or the annotate bit); `static string PdfSettingsFiles.SignaturesDirectoryFor(string? settingsFile)` (a `signatures` folder next to the given settings file, else `UserDataPaths.SignaturesDirectory()`; a malformed path goes to the temp folder like `FolderOf`); in `PdfWindow` a per-tab `_fillAllowed` read next to `_annotateAllowed` (`bool?`, null until read) and `FillAllowedOf(PdfTab)`.

- [ ] **Step 1: Write the failing tests:** `MayFillFormsTests` (`UnencryptedDocumentMayFill`, `OwnerPasswordMayFill`, and with the fixture the `MayAnnotate` tests already use: `AnnotateBitAlsoAllowsFilling`, `NeitherBitForbidsFilling`; if no fixture has a fill-only permission, say so in the test file and cover the bit logic with the pure function the check calls), `PdfSettingsFilesTests.SignaturesFolderLiesNextToTheSettingsFile` and `MalformedSettingsPathGoesToTheTempFolder`.
- [ ] **Step 2: Run** `dotnet test tests/Raffica.Core.Tests -c Release --filter-class "*MayFillFormsTests"` and `dotnet test tests/Raffica.Pdf.Tests -c Release --filter-class "*PdfSettingsFilesTests"`. Expected: FAIL; check the counts are not zero.
- [ ] **Step 3: Implement** both seams. The permission is read at background priority on the document's thread next to `MayAnnotateAsync` (look at how `document.MayAnnotate` is computed and add the fill-forms bit beside it); `PdfWindow` reads it in `LoadPrintInfo` after the first sharp page, like `ReadAnnotatePermissionAsync`.
- [ ] **Step 4: Run** the same commands plus `--filter-namespace "*Settings*"`. Expected: PASS.
- [ ] **Step 5: Commit** `feat(core): may-fill-forms permission and a signatures folder next to the settings file`.

### Task 3: Per-tab form session

**Files:**
- Create: `src/Raffica.Pdf/Forms/FormSession.cs`, `src/Raffica.Pdf/Forms/FormErrors.cs`
- Modify: `src/Raffica.Pdf/Documents/PdfTab.cs` (lazy `Forms`, disposed with the tab), `src/Raffica.Pdf/Annotating/AnnotationSession.cs`, `src/Raffica.Pdf/Annotating/TextBoxController.cs`
- Test: `tests/Raffica.Pdf.Tests/Forms/FormSessionTests.cs`, `Annotating/AnnotationSessionTests.cs` (extend)

**Interfaces:**
- Consumes: `PdfDocument.HasAcroForm`, `GetFormInfoAsync()`, `GetFormFieldsAsync(int page)`, `GetEmptyRequiredFieldsAsync()`, `AnnotationEditor.SetFormValueAsync(string fieldName, FormFieldState state)` returning `FormChange?`, `FormValueRules.CheckText`, `ViewerSession.SetFormHighlight(bool)`, the `PagesInvalidated` event of the session.
- Produces: `sealed class FormSession : IDisposable` with ctor `(PdfTab tab, SynchronizationContext ui)`; `bool HasForm` (the sync `HasAcroForm`; false means no job is ever started); `FormInfo? Info` (null until read); `IReadOnlyList<FormField> FieldsOn(int page)` (the cache, empty when not loaded); `Task LoadPageAsync(int page)` (background priority, single-flight per page; refreshed for the pages of `PagesInvalidated`); `FormField? FieldAt(int page, double x, double y)` (the topmost widget under a point in page points, locked ones included); `IReadOnlyList<string> EmptyRequired` and `Task RefreshRequiredAsync()`; `bool Highlight { get; set; }` (sets `Session.SetFormHighlight`, applied once); `Task<FormEditResult> SetValueAsync(FormField field, FormFieldState state)` where `FormEditResult` is `Applied`, `Unchanged` or `Refused(FormEditProblem problem, string detail)` and `FormEditProblem` maps `FormValueProblem`, `FormWriteException` (restored), `InvalidOperationException` (locked), `FormFieldNotFoundException` and `AnnotationHistoryFullException`; `event Action? Changed` (UI thread). `AnnotationSession.DiscardDraft` becomes `IDisposable AddDiscardHandler(Func<bool> handler)` (the first handler that returns true wins; `UndoAsync`/`RedoAsync` call them in order), and `TextBoxController` uses it.

- [ ] **Step 1: Write the failing tests** (a temp copy of `forms-basic.pdf`): `NoFormMeansNoWork` (`text-rich.pdf`: `HasForm` false, `LoadPageAsync` returns at once and starts no job), `FieldsAreLoadedPerPageAtBackgroundPriority`, `FieldAtFindsTheTopmostWidgetAndLockedOnesToo`, `SetValueMakesOneUndoStepAndTheTabDirty`, `UnchangedValueIsNotAStep`, `RefusedTextReturnsTheProblemAndWritesNothing` (unsupported character, too long, line break), `LockedFieldIsRefused`, `PagesInvalidatedRefreshesTheCachedPage`, `RequiredListFollowsTheValues`, `HighlightIsAppliedOnceAndNotForADocumentWithoutAForm`, `ChangedIsMarshalledToTheUiContext`, and in the session tests `TwoDiscardHandlersAreAskedInOrderAndTheFirstThatDiscardsWins`.
- [ ] **Step 2: Run** `dotnet test tests/Raffica.Pdf.Tests -c Release --filter-namespace "*Forms*"` and `--filter-class "*AnnotationSessionTests"`. Expected: FAIL.
- [ ] **Step 3: Implement** the session over the tab's `AnnotationSession.Editor` (the same history), the error mapping in `FormErrors`, and the discard handler change.
- [ ] **Step 4: Run** the same commands plus `--filter-namespace "*Annotating*"` (the text box tests stay green). Expected: PASS.
- [ ] **Step 5: Commit** `feat(pdf): per-tab form session over the shared history`.

### Task 4: Markers, focus ring and the form overlay slot

**Files:**
- Create: `src/Raffica.Pdf/Forms/FormMarkers.cs`, `src/Raffica.Pdf/Forms/FormFocus.cs`
- Modify: `src/Raffica.Pdf/Viewing/PageView.Text.cs` (a `FormOverlay` property of type `IPageOverlay?`, drawn before `Overlay`, invalidated on its `Changed`), `src/Raffica.Pdf/PdfWindow.axaml.cs` (per-tab creation in `ShowActiveView`, `FormsOf(tab)` test seam), `src/Raffica.Shell/Styles/Icons.axaml` (a lock and a star only if no existing icon fits; otherwise draw the glyphs as geometry in the overlay from shell tokens)
- Test: `tests/Raffica.Pdf.Tests/Forms/FormMarkersTests.cs`, `FormFocusTests.cs`

**Interfaces:**
- Produces: `sealed class FormFocus` (per tab) with `FormWidgetKey? Current`, `FormField? CurrentField(FormSession)`, `bool Move(FormSession, bool forward)` over `FormNavigation.Next` (returns false at the ends so the caller lets the key pass), `void Set(FormWidgetKey?)`, `event Action? Changed`; `sealed class FormMarkers : IPageOverlay` ctor `(FormSession session, FormFocus focus)` drawing, for the visible pages and only when no annotate tool is active, a star at the corner of every empty required field, a lock at every locked field, and the focus ring around the focused widget (shape plus a 2 px line, not colour alone).
- Rules: markers read the session's cache only (no job); nothing is drawn or loaded before the first sharp page; the page view asks `LoadPageAsync` for the visible pages on `ViewChanged` (once per page).

- [ ] **Step 1: Write the failing tests:** `StarOnEmptyRequiredAndNoStarOnceFilled`, `LockOnReadOnlySignatureAndButtonFields`, `FocusRingFollowsTheFocusedWidget`, `NothingIsDrawnWhileAnAnnotateToolIsActive`, `MarkersFollowZoomAndScroll`, `MovePassesAtTheEndsInsteadOfTrappingTheFocus`, `MoveSkipsLockedWidgets`, `NothingIsLoadedBeforeTheFirstSharpPage`.
- [ ] **Step 2: Run** `--filter-namespace "*Forms*"`. Expected: FAIL.
- [ ] **Step 3: Implement** the overlay slot, the markers and the focus model; flip `Session.SetFormHighlight` once after the first sharp page for a document with a form (Task 7 adds the switch).
- [ ] **Step 4: Run** `--filter-namespace "*Forms*"` and `--filter-namespace "*Viewing*"`. Expected: PASS.
- [ ] **Step 5: Commit** `feat(pdf): form markers, focus ring and the form overlay slot`.

### Task 5: Field editors and pointer interaction

**Files:**
- Create: `src/Raffica.Pdf/Forms/FormInteraction.cs`, `FormFieldEditor.cs`, `FormEditorLayout.cs`
- Modify: `src/Raffica.Pdf/Text/TextInteraction.cs` (do nothing for a press that `FormInteraction` handled; it uses a tunnel handler and sets `Handled`), `src/Raffica.Pdf/PdfWindow.axaml.cs`, `PdfWindow.Commands.cs` (key routing), `src/Raffica.Pdf/Annotating/AnnotationSession.cs` (nothing new), both `Strings.*.resx`, `PdfStrings.cs`
- Test: `tests/Raffica.Pdf.Tests/Forms/FieldEditTests.cs`, `FieldEditorPlacementTests.cs`, `FieldKindTests.cs`

**Interfaces:**
- Consumes: `FormSession`, `FormFocus`, `PageView.PageAt(Point)`, `ToView(int, PageRect)`, `Field` and `FieldRect` slots, `IsOverScrollBar`, `AnnotationSession.BeginPending()`, `BeginUnresolved()`, `FlushRequested`, `AddDiscardHandler`, `FormValueRules`, the text box editor as the model for a field element (`TextBoxEditor`).
- Produces: `sealed class FormInteraction : IDisposable` ctor `(PdfTab tab, PageView view, ToolState tools, FormSession forms, FormFocus focus, DialogQueue dialogs)`; with `tools.Tool == None` and fill allowed, a left-button press on an unlocked widget focuses it and: toggles a check box or radio button (one history step), or opens `FormFieldEditor` for text, combo and list. `FormFieldEditor : UserControl` per kind: single-line and multi-line `TextBox` (password masked, `MaxLength` from the field, comb fields as one text), editable and non-editable combo, list with multi-select; the element is named from `FormField.DisplayName` (automation), sized to the widget at the zoom, and placed through `FieldRect`. Commit and keys (predecessor): the value is committed when the field loses focus or on Enter in a single-line field (then the focus moves to the next field); Enter in a multi-line field is a line break and Ctrl+Enter commits; Esc discards the draft; Ctrl+Z inside an open text field belongs to the field (the window already yields to a focused `TextBox`); a choice in a combo or list commits at once. A refused text stays in the editor with its reason in a live hint (the `TextBoxEditor` pattern) and holds `BeginUnresolved()`; a valid draft holds `BeginPending()`; both end on every path. `FlushRequested` commits a valid draft; the field edit is committed or dropped when the tool changes. A click outside the editor on a non-field behaves as in 2.1.

- [ ] **Step 1: Write the failing tests** (fixtures `forms-basic.pdf`, `forms-rotated.pdf`; headless `WindowFixture`, temp copies): `ClickOnATextFieldOpensTheEditorOverIt`, `LeavingTheFieldCommitsOneHistoryStep`, `EnterCommitsAndMovesOnInASingleLineField`, `EnterIsALineBreakAndCtrlEnterCommitsInAMultilineField`, `EscDiscardsTheDraft`, `RefusedTextStaysInTheEditorAndKeepsTheTabDirty` (unsupported character U+20AC is allowed, U+4E2D is not; too long; line break in single line), `CheckBoxAndRadioToggleWithOneStepEach`, `RadioGroupCannotGoBackToNoChoice` (Core rule: undo of the first choice fails and the step is dropped; the UI shows no error), `ComboAndListCommitTheChoice`, `MultiSelectListKeepsTheSet`, `PasswordFieldIsMasked`, `LockedFieldDoesNothingOnClick`, `UndoRestoresTheBeforeValue`, `ValuesAndAnAnnotationAreOneSave`, `SaveCommitsAnOpenFieldFirst`, `ToolChangeCommitsAnOpenField`, `FieldsAreInertWhileAnAnnotateToolIsActive`, `OutsideAFieldTextSelectionAndLinksWorkAsIn2_1`, `EditorSitsOverTheFieldOnRotatedAndCropBoxPages` (compare the editor's bounds with `ToView` of the widget at two zooms), `EditorFollowsScrollAndIsHiddenWhenTheFieldLeavesTheView`, `DarkPageKeepsTheEditorReadable` (light paper and dark text in the editor), `FillIsRefusedWithAReasonWhenTheDocumentForbidsIt`.
- [ ] **Step 2: Run** `--filter-namespace "*Forms*"`. Expected: FAIL.
- [ ] **Step 3: Implement** the interaction and editors. The press handler is a tunnel handler on the view; it takes the press only for a widget of an editable kind, never over a scroll bar, never for touch (touch scrolls and zooms the page, as in 2.2), and never while a dialog is open. The pending draft is registered on the UI thread and ended on commit, discard, refusal that becomes unresolved, tool change and tab close.
- [ ] **Step 4: Run** `--filter-namespace "*Forms*"`, `--filter-namespace "*Text*"` and `--filter-namespace "*Annotating*"`. Expected: PASS.
- [ ] **Step 5: Commit** `feat(pdf): fill form fields in elements over the page`.

### Task 6: Keyboard path, commands and the Forms ribbon tab

**Files:**
- Modify: `src/Raffica.Pdf/Commands/PdfCommandIds.cs`, `PdfCommandCatalog.cs`, `PdfCommands.cs`, `PdfCommandRules.cs`, `src/Raffica.Pdf/PdfRibbon.cs`, `PdfWindow.Commands.cs`, `PdfWindow.axaml.cs`, `src/Raffica.Pdf/Forms/FormInteraction.cs`, both `Strings.*.resx`, `PdfStrings.cs`
- Test: `tests/Raffica.Pdf.Tests/Forms/FormKeyboardTests.cs`, `FormSafetyTests.cs`, `Commands/CommandCatalogTests.cs`, `RibbonDefinitionTests.cs`, `ShortcutTests.cs` (extend)

**Interfaces:**
- Produces: command ids `forms.next`, `forms.previous`, `forms.nextRequired`, `forms.highlight`; a ribbon tab "Forms" (letter `F` if free, else `O`; the test `RibbonDefinitionTests` checks letters and field codes are unique and prefix-free) with one group "Fill" holding the four commands. Rules: the commands need a document with fillable fields (`FormSession.HasForm` and `FillAllowedOf(tab) != false`); `nextRequired` needs at least one empty required field; a reason string when fill is forbidden. Keys inside the page view with no tool active: Tab and Shift+Tab move the focus over `FormFocus.Move` (the first Tab from the page view enters the first field of the visible page; at the ends the key passes on), Enter or Space acts on the focused field (toggle, or open the editor), Esc clears the focus. The commands `forms.next` and `forms.previous` call the same move; `forms.nextRequired` goes to the next empty required field and scrolls it into view (`PageView.GoToPage` and `ToView`).

- [ ] **Step 1: Write the failing tests:** `TabWalksTheFieldsInAnnotsOrderAndPassesAtTheEnds`, `ShiftTabWalksBack`, `EnterAndSpaceActOnTheFocusedField`, `CompleteSampleFormFilledWithTheKeyboardOnly` (one headless run over `forms-basic.pdf`: every kind, then save, reopen, values equal), `NextRequiredScrollsTheFieldIntoView`, `CommandsAreDisabledWithAReasonWithoutAFormOrWhenFillingIsForbidden`, `TabKeyIsNotTakenWhileADialogIsOpen`, `FocusedFieldHasAnAutomationNameFromTheFieldsAlternateName`, in `FormSafetyTests` `HostileAndLockedFormsOfferOnlyWhatIsSafe` (theory over `forms-hostile`, `forms-xfa`, `forms-calc`, `forms-signed`: opens, no throw, locked fields cannot be edited and name their reason, a button click does nothing, a format-script field is written unchanged), and the catalog, ribbon and shortcut expectations (new command count, tab letters, key tips).
- [ ] **Step 2: Run** `--filter-namespace "*Forms*"` and `--filter-namespace "*Commands*"`. Expected: FAIL; the old hard-coded counts are updated in the same step.
- [ ] **Step 3: Implement** ids, catalog entries (existing icon names; stand-ins as in 2.2), rules, wiring and the key routing; call `RefreshCommandStates()` on `FormSession.Changed`.
- [ ] **Step 4: Run** the same commands plus `--filter-class "*PaletteSourceTests"`. Expected: PASS.
- [ ] **Step 5: Commit** `feat(pdf): keyboard path and the Forms ribbon tab`.

### Task 7: Form badge, notice line and the highlight switch

**Files:**
- Create: `src/Raffica.Pdf/Forms/FormBadge.cs`, `FormNotices.cs`, `src/Raffica.Pdf/Viewing/BadgeHost.cs`
- Modify: `src/Raffica.Pdf/PdfWindow.axaml.cs` (the `Badge` slot gets the host), `src/Raffica.Pdf/Annotating/AnnotateBadge.cs` (only if the host needs a visibility hook), both `Strings.*.resx`, `PdfStrings.cs`
- Test: `tests/Raffica.Pdf.Tests/Forms/FormBadgeTests.cs`, `FormNoticesTests.cs`

**Interfaces:**
- Produces: `static class FormNotices` with `IReadOnlyList<string> LinesFor(FormHint hints)` (one localized sentence per set flag: `Calculated` and `FormatScripts` "Calculations, formats and checks in this form need JavaScript and are not run.", `Xfa` "This is an XFA form; Raffica PDF does not support XFA forms.", `SignatureField` "Digital signature fields are not supported.", `Buttons` "Buttons in this form never run an action."); `FormBadge : UserControl` ctor `(FormSession session, FormFocus focus)` showing the notice (all applicable lines joined, one dismiss button, shown once per tab), the required counter ("2 required fields empty" with a button that runs `forms.nextRequired`; hidden at zero) and a `ToggleButton` "Highlight fields" bound to `FormSession.Highlight` (pressed state, automation name); `BadgeHost : Control` ctor `(AnnotateBadge annotate, FormBadge forms, ToolState tools)` that shows the annotate badge while `tools.Tool != None` and the form badge otherwise (and nothing for a document without a form), and implements what `PageView.IsInBadge` needs.
- Rules: the badge is built after the first sharp page; texts name only what applies, in both languages, and never say "valid"; the badge never covers the field being edited (it sits top middle as the annotate badge does).

- [ ] **Step 1: Write the failing tests:** `OneLineNamesOnlyWhatApplies` (per fixture: `forms-calc` names calculations, `forms-xfa` names XFA, a button form names buttons, a plain form shows none), `NoticeIsShownOnceAndDismissible`, `CounterFollowsTheEmptyRequiredFields`, `CounterButtonGoesToTheNextRequiredField`, `HighlightSwitchTogglesTheSessionAndRendersTheField` (pixel check of a widget with and without highlight), `BadgeIsHiddenForADocumentWithoutAForm`, `HostShowsTheAnnotateBadgeWhileAToolIsActive`, `NoTextSaysValidOrLegallyBinding`.
- [ ] **Step 2: Run** `--filter-namespace "*Forms*"`. Expected: FAIL.
- [ ] **Step 3: Implement** the badge, the notices and the host; both languages.
- [ ] **Step 4: Run** `--filter-namespace "*Forms*"`, `--filter-namespace "*Annotating*"` (the annotate badge tests stay green) and `--filter-class "*TranslationTests"`. Expected: PASS.
- [ ] **Step 5: Commit** `feat(pdf): form badge with the notice line, the required counter and the highlight switch`.

### Task 8: Required-field card, save hook and the signed-file wording

**Files:**
- Create: `src/Raffica.Pdf/Dialogs/RequiredFieldsCard.cs`
- Modify: `src/Raffica.Pdf/Documents/SaveController.cs`, `src/Raffica.Pdf/Dialogs/SignedFileCard.cs`, `src/Raffica.Pdf/PdfWindow.axaml.cs` (construct the controller with the prompt), both `Strings.*.resx`, `PdfStrings.cs`
- Test: `tests/Raffica.Pdf.Tests/Dialogs/RequiredFieldsCardTests.cs`, `Commands/SaveCommandTests.cs`, `Dialogs/SaveCardsTests.cs`, `Documents/SaveControllerTests.cs` (extend)

**Interfaces:**
- Consumes: `FormSession.RefreshRequiredAsync`, `EmptyRequired`, `AnnotationEditor.HasOnlyFormChanges`, `SignaturePolicy.Evaluate`.
- Produces: `enum RequiredChoice { GoToFirst, SaveAnyway }`; `RequiredFieldsCard.Create(IReadOnlyList<string> names, Action<RequiredChoice> done)` (default button "Go to first empty field", the other "Save anyway"; Esc means `GoToFirst`; lists at most five names and the count; never says the file is invalid); a new optional ctor parameter of `SaveController` `Func<PdfTab, IReadOnlyList<string>, Task<RequiredChoice>>? askRequired`. In `SaveCoreAsync` the card comes **after** the flush and the "editor not dirty" check and **before** the signed-file question; `GoToFirst` stops the save (returns false, the tab stays dirty) and the window goes to the first empty field, `SaveAnyway` goes on. Not asked for Save as when the editor holds no form change and not for a document without a form. `SignedFileCard.Create` takes `bool onlyFormChanges`: the text then says that only form fields were filled, which the signature's level may allow, and that the app cannot check whether the signature stays valid; the default stays Cancel for DocMDP 1 and 2; the words "valid" and "legally binding" do not appear in any form.

- [ ] **Step 1: Write the failing tests:** `RequiredCardOffersGoToFirstAsDefaultAndSaveAnywayNeverBlocks`, `GoToFirstStopsTheSaveAndMovesToTheField`, `SaveAnywayWritesTheFile`, `NoCardWithoutEmptyRequiredFields`, `NoCardForADocumentWithoutAForm`, `RequiredCardComesBeforeTheSignedFileQuestion`, `SignedCardNamesFormOnlyChanges` (default Cancel stays), `CloseWithOpenFieldStillAsks`, `NoUiTextCallsAnythingLegallyBinding` (extended to the new strings in both languages).
- [ ] **Step 2: Run** `--filter-namespace "*Dialogs*"` and `--filter-class "*SaveControllerTests"`. Expected: FAIL.
- [ ] **Step 3: Implement** the card, the hook and the wording.
- [ ] **Step 4: Run** the same commands plus `--filter-namespace "*Commands*"`. Expected: PASS.
- [ ] **Step 5: Commit** `feat(pdf): required-field card before saving and form-aware signed-file wording`.

### Task 9: Signature library and image codec

**Files:**
- Create: `src/Raffica.Pdf/Signing/SignatureLibrary.cs`, `SignatureImageCodec.cs`
- Modify: `src/Raffica.Pdf/PdfWindow.axaml.cs` (creates the library from `PdfSettingsFiles.SignaturesDirectoryFor(settingsFile)`, lazily)
- Test: `tests/Raffica.Pdf.Tests/Signing/SignatureLibraryTests.cs`, `SignatureImageCodecTests.cs`

**Interfaces:**
- Consumes: Core `SignatureStore` (`Load`, `Items`, `AddDrawn`, `AddImage`, `Remove`, `ReadPng`), `SavedSignature`, `SignatureLimits`, `ImageHeader.TryReadSize`/`IsComplete`, `SignatureImage`, `SignatureCutout` (`AutoThreshold`, `Apply`, `TrimToContent`, `FitWithin`), `PngEncoder`.
- Produces: `sealed class SignatureLibrary` ctor `(Func<string> directory)`; the folder is neither read nor created before `LoadAsync()`; `Task<IReadOnlyList<SavedSignature>> LoadAsync()`; `Task<SavedSignature> AddDrawnAsync(string name, IReadOnlyList<...> strokes, AnnotationColor color)` and `AddImageAsync(string name, SignatureImage cutout)` (both throw `SignatureLimitException` at eleven); `Task RemoveAsync(string id)`; `Task<SignatureImage> ReadImageAsync(string id)`; `event Action? Changed`. `static class SignatureImageCodec`: `Task<SignatureImageResult> DecodeAsync(Stream file)` where `SignatureImageResult` is `Ok(SignatureImage)` or `Problem(SignatureImageProblem)` with `SignatureImageProblem { TooLarge, TooManyPixels, Unreadable, Empty }`; the size and the header are checked before decoding (10 MiB, 6000 per side) and decoding runs off the UI thread; an image over 1200 px on its longest side is shrunk with `FitWithin`.

- [ ] **Step 1: Write the failing tests:** `LibraryDoesNotTouchTheFolderBeforeTheFirstUse`, `AddRemoveAndReloadKeepOrderAndLimit`, `EleventhSignatureThrowsTheLimitException`, `CorruptIndexIsMovedAsideAndTheLibraryStaysUsable`, `NamesAreRepairedAndLimitedTo60`, `FolderIsTheOneNextToTheSettingsFile` (never the real user folder), `FileOver10MiBIsTooLargeBeforeDecoding`, `HeaderOver6000PixelsIsRefusedBeforeDecoding`, `NotAnImageIsUnreadable`, `FullyTransparentOrBlankImageIsEmpty`, `LargeImageIsShrunkTo1200`, `PngWithAlphaAndJpegBothDecode`.
- [ ] **Step 2: Run** `--filter-namespace "*Signing*"`. Expected: FAIL.
- [ ] **Step 3: Implement** the wrapper (the store is Core's; the wrapper adds laziness, threading and the event) and the Avalonia decode (`Avalonia.Media.Imaging.Bitmap` to BGRA); no new package; check Native AOT under the CI publish step by a smoke test in `SignatureImageCodecTests` that decodes a PNG and a JPEG from the test fixtures.
- [ ] **Step 4: Run** `--filter-namespace "*Signing*"`. Expected: PASS.
- [ ] **Step 5: Commit** `feat(pdf): signature library and image codec`.

### Task 10: New-signature card (draw and image)

**Files:**
- Create: `src/Raffica.Pdf/Signing/NewSignatureCard.cs`, `SignaturePad.cs`, `SignaturePreview.cs`
- Modify: both `Strings.*.resx`, `PdfStrings.cs`
- Test: `tests/Raffica.Pdf.Tests/Signing/SignatureCardTests.cs`, `SignaturePadTests.cs`

**Interfaces:**
- Consumes: `SignatureLibrary`, `SignatureImageCodec`, `StrokeSimplifier`, `DrawPalette`, `SignatureCutout`, the dialog pattern of `NoticeCard`/`SignedFileCard`, the file picker through the window's storage provider (as Open does).
- Produces: `NewSignatureCard.Create(SignatureLibrary library, Func<Task<Stream?>> pickImage, Action<SavedSignature?> done)`: a card with two tabs "Draw" and "Image", a name field (default "Signature 1", numbered), a preview, Save and Cancel, and the hint (both sentences) visible in both tabs without scrolling. Draw: `SignaturePad : Control` with a guide line, mouse and pen, points simplified with `StrokeSimplifier`, Clear and Back, ink from `DrawPalette` (default black), stored as vector strokes in the unit square; Save is disabled while the pad is empty. Image: file picker for PNG and JPG, a cutout slider (brightness to alpha, starting at `AutoThreshold`) with the preview on a checkerboard, auto-trim, Save disabled until an image is loaded. A refusal (`SignatureLimitException`, any `SignatureImageProblem`) is shown in the card as a live hint with its reason and nothing is stored.

- [ ] **Step 1: Write the failing tests:** `HintIsVisibleInBothTabsWithoutScrolling`, `DrawnSignatureIsStoredAsStrokesInTheUnitSquare`, `SaveIsDisabledForAnEmptyPadAndForNoImage`, `ClearAndBackWork`, `CutoutSliderChangesTheAlphaPreview`, `ImageIsTrimmedToItsContent`, `EveryRefusalEndsInAReasonAndNothingIsStored` (eleventh signature, too large, too many pixels, unreadable, empty), `CancelStoresNothing`, `GermanTextFitsTheCard`, `EscCancels`, `NoTextSaysValidOrLegallyBinding`.
- [ ] **Step 2: Run** `--filter-namespace "*Signing*"`. Expected: FAIL.
- [ ] **Step 3: Implement** the card and the pad as one file each; the preview draws the strokes or the bitmap on the checkerboard from shell tokens.
- [ ] **Step 4: Run** `--filter-namespace "*Signing*"` and `--filter-class "*TranslationTests"`. Expected: PASS.
- [ ] **Step 5: Commit** `feat(pdf): new-signature card with a drawing pad and image import`.

### Task 11: Picker, placement and the Sign command

**Files:**
- Create: `src/Raffica.Pdf/Signing/SignaturePickerCard.cs`, `SignaturePlacer.cs`
- Modify: `src/Raffica.Pdf/Commands/PdfCommandIds.cs`, `PdfCommandCatalog.cs`, `PdfCommands.cs`, `PdfCommandRules.cs`, `src/Raffica.Pdf/PdfWindow.Commands.cs`, `src/Raffica.Pdf/Annotating/SelectController.cs` (only if a just-created annotation cannot be selected by locator), `MarksModel` (nothing planned: it already lists `AnnotationKind.Signature`), both `Strings.*.resx`, `PdfStrings.cs`
- Test: `tests/Raffica.Pdf.Tests/Signing/SignaturePickerTests.cs`, `SignaturePlacementTests.cs`, `Commands/CommandCatalogTests.cs` (extend)

**Interfaces:**
- Consumes: `SignatureLibrary`, `NewSignatureCard`, `SignaturePlacement.Fit(aspect, centre, pageW, pageH)`, `AnnotationEditor.ApplySignatureImageAsync(int page, PageRect box, SignatureImage image, string? author)` and `ApplyDrawingAsync(...)` with `DrawingGeometry.Signature(strokes, width)` (check the exact signatures and how the strokes are mapped into the placed box), `SelectController.SelectAsync(AnnotationLocator)`, `ToolState.Choose(AnnotateTool.Select)`, `AnnotationHistoryFullException`, `PageView.CurrentPage`, `SizeOfPage`, `PageBounds`.
- Produces: command `annotate.sign` in a new Annotate-tab group "Sign" (digit `5`, placed before the destructive group so that group stays last; a name key `CmdSign`); `SignaturePickerCard.Create(IReadOnlyList<SavedSignature> items, SignatureLibrary library, Action<PickerResult> done)` with thumbnails (`SignatureThumbnail`), "New signature…", Delete (with confirmation text, no arming needed) and the hint line; `PickerResult` is `Picked(SavedSignature)`, `New` or `Cancelled`. `SignaturePlacer.PlaceAsync(PdfTab tab, PageView view, SavedSignature signature)`: the centre of the visible part of the current page, `SignaturePlacement.Fit`, one history step through the right Core call (image: `ApplySignatureImageAsync`; drawn: `ApplyDrawingAsync` with the saved colour), then the select tool is chosen and the new annotation selected. With no saved signature the command opens the New-signature card directly; after saving there the signature is placed at once. The command is disabled with a reason when the document forbids annotating (a signature is an annotation) or has no page.

- [ ] **Step 1: Write the failing tests:** `SignWithoutSavedSignaturesOpensTheNewCardAndPlacesTheResult`, `PickerListsSavedSignaturesWithThumbnails`, `PickPlacesInTheCentreOfTheVisiblePartAndSelectsIt`, `PlacedSignatureIsAnnotationKindSignatureAndOneUndoStep`, `ArrowKeysMoveAndHandlesScaleWithAFixedAspect` (through the select tool), `DrawnSignatureKeepsItsColourChangeable` and `ImageSignatureOffersNoColourOrWidth`, `SignatureSurvivesSaveAndReopenOnARotatedAndACropBoxPage`, `PlacedTwiceIsTwoAnnotationsButOneStoredFile` (library unchanged), `DeleteRemovesFromTheLibraryNotFromDocuments`, `FullHistoryAsksToSaveFirst` (`AnnotationHistoryFullException` becomes a card and nothing is placed), `SignCommandIsDisabledWhenAnnotatingIsForbidden`, `SignedFileWarningStillAppliesOnSave`, `NoTextSaysValidOrLegallyBinding`, and the catalog expectations (command count, group digits, field codes).
- [ ] **Step 2: Run** `--filter-namespace "*Signing*"` and `--filter-namespace "*Commands*"`. Expected: FAIL.
- [ ] **Step 3: Implement** the picker, the placer and the command; the window starts the annotate mode through `ToolState` like the markup shortcuts do.
- [ ] **Step 4: Run** the same commands plus `--filter-namespace "*Annotating*"`. Expected: PASS.
- [ ] **Step 5: Commit** `feat(pdf): sign a document with a saved or new signature`.

### Task 12: Printing check, measurements and documentation

**Files:**
- Modify: `src/Raffica.Pdf/Printing/PrintCommand.cs` (only if the flush is missing), `CLAUDE.md` (section "Project-specific", new block "Forms and signing (M2.3)"), `Docs/DECISIONS.md` (measurements and any limit found), `CHANGELOG.md` (`[Unreleased]`)
- Test: `tests/Raffica.Pdf.Tests/Printing/PrintFormValuesTests.cs`, `Forms/FormOpenMeasurementTests.cs`

- [ ] **Step 1: Write the failing tests:** `PrintFlushesAnOpenFieldEditFirst`, `PrintedPageCarriesUnsavedFormValues` (Windows platform test, as `WindowsPrintToPdfTests`, skipped elsewhere; the Linux path is checked by reading how `CupsPrintService` obtains the file and asserting that a value set but not saved is in it, or the limit is recorded), `DocumentWithoutAFormCreatesNoFormWork`, and `FormOpenMeasurementTests.OpenAndLeaveAFieldAreMeasured` (skipped by default like the other measurement tests; opens a form with many fields and prints the time to the first sharp page and from leaving a field to the updated page).
- [ ] **Step 2: Run** `--filter-namespace "*Printing*"` and `--filter-class "*FormOpenMeasurementTests"`. Expected: the first two FAIL or skip with a recorded reason; the check counts are not zero.
- [ ] **Step 3: Implement** only what a failing test needs (the flush in `PrintCommand` if missing); run the measurement once and record the numbers in a `Docs/DECISIONS.md` entry without a budget; write the `CLAUDE.md` block (where things live: `Forms/*`, `Signing/*`, the folder helper, the keys) and the changelog line.
- [ ] **Step 4: Run** `dotnet test tests/Raffica.Pdf.Tests -c Release` (all) and `RAFFICA_REQUIRE_OCR=1 dotnet test Raffica.slnx -c Release --no-build --max-parallel-test-modules 1`. Expected: PASS.
- [ ] **Step 5: Commit** `docs(pdf): forms and signing notes, print check and measurements`.

### Task 13: Close the milestone

**Files:**
- Modify: `Docs/roadmap.json`, `Docs/ROADMAP.md` (generated), `Docs/DECISIONS.md` (amendments only), `CHANGELOG.md`

- [ ] **Step 1: Run** `dotnet build Raffica.slnx -c Release` (0 warnings; `-p:NuGetAudit=false` if the known advisory blocks it), `RAFFICA_REQUIRE_OCR=1 dotnet test Raffica.slnx -c Release --no-build --max-parallel-test-modules 1`, `dotnet format Raffica.slnx --verify-no-changes`, `dotnet run tools/roadmap.cs -- check`. Expected: all green; if not, fix the cause, never loosen a check.
- [ ] **Step 2: Tick** the four tasks and the criterion "Tests for each field kind and for the signature store pass" with the command and the result in `evidence`. The criterion "A filled form opens with the values in an independent viewer" is **not** ticked unless the manual check was made: add it to the backlog as "Manual check: independent viewer for filled forms and signatures (2.3)" with its steps (Core's `InteropSampleTests` write the sample files; open a temp copy in Raffica PDF, fill one field of every kind, place one drawn and one image signature, save, open the file in Acrobat Reader, Chrome, Edge or Okular, expect every value and signature in place), and list the other not-verified points in an "Open items" block (real pens and touch, high-DPI looks and German widths, IME and screen reader, a signed form saved in Acrobat, Linux and macOS window managers, a real printer with form values).
- [ ] **Step 3: Record** plan-versus-built changes under "Scope changes" (context "Milestone 2.3") and as an amendment to the DECISIONS entry.
- [ ] **Step 4: Set** the milestone `done` with `completedOn` only if every criterion is ticked or listed as open with its reason (as 2.1 and 2.2 did, on the owner's instruction); run `dotnet run tools/roadmap.cs -- generate` and `-- check`.
- [ ] **Step 5: Commit** `docs: close milestone 2.3`, open the PR to `dev`, merge when `ci` and `roadmap` are green.
