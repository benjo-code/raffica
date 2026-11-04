# Milestone 2.1 – Viewer: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Raffica PDF (`RafficaPdf`) opens, shows, searches and prints PDFs on the shared frame, in English and German, within speed budgets that were measured for it.

**Architecture:** A new app project `src/Raffica.Pdf` references only `Raffica.Core` and `Raffica.Shell`. Core already holds the engine side (`ViewerSession`, `DocumentLayout`, `ViewportPlanner`, `RenderScheduler`, `RenderCache`, `PageTextSource`, `DocumentSearch`, `TextFlow`, `RecentFiles`, `SingleInstance`, `IPrintService`); the app adds what is missing: the page view control with the bitmap bridge, the tab and document model, the panes, the open flow, the commands, the platform print services and the measurement switches. Logic that needs no window (workspace, open flow, view state, search session, print command) lives in plain classes tested without Avalonia; controls are tested headless with `Avalonia.Headless.XUnit`. The shell gets three small additive extensions first (Tasks 2 and 3), so the frame's behaviour for other apps does not change.

**Tech Stack:** .NET 10 Native AOT, Avalonia 12.1.4, xunit.v3 (the app tests pin 3.2.2 like `Raffica.Shell.Tests`, see `Docs/DECISIONS.md` "Avalonia.Headless.XUnit with xunit.v3 3.2.2 in the shell tests only"), `System.Text.Json` source generation where JSON is written, PDFium through Core only.

**Spec:** [Docs/superpowers/specs/2026-11-04-raffica-pdf-design.md](../specs/2026-11-04-raffica-pdf-design.md): sections 2 to 7 (milestone 2.1), 8 and 9. Rules of the frame: [2026-11-03-design-system-shell-design.md](../specs/2026-11-03-design-system-shell-design.md) and the entry "App frame conventions (M1.2)" in [Docs/DECISIONS.md](../../DECISIONS.md). Behaviour rules of the viewer come from the predecessor's roadmap (read-only checkout `C:\VS-Code\Pagewing`, tag `v1.0.0`, milestones 3 to 7) and are cited in English where a task uses them.

## Global Constraints

- **Stack:** .NET 10 with Native AOT, Avalonia 12, compiled bindings, no reflection, no new package except what a task names. PDFium runs on its one dedicated thread inside Core; the UI thread never waits for it (every Core call is awaited).
- **Dependencies of the app:** `Raffica.Pdf` references `Raffica.Core` and `Raffica.Shell` only and knows no PDFium type; platform code (print, default app, single-instance transport, updater) stays in the app's `Platform/` folder, nothing else is Windows- or Linux-only.
- **Look:** direction D with the PDF ink plum; the shell's tokens and controls only, no hex literal in component styles; pages pane 204 px, comments pane 300 px (the marks pane is 2.2); the shell spec's "Deviations from the boards" wins over the canvas.
- **Strings:** every UI string exists in `Strings.en.resx` and `Strings.de.resx` of the app project; a missing key throws; `TranslationTests` requires a German value for each key.
- **Naming:** the predecessor's name appears nowhere under `src/`, `tests/`, `tools/`, `bench/` or `.github/` (write "the predecessor"); only the port commits of Tasks 14 and 15 may contain it, see there.
- **Saving rules (spec 6):** 2.1 has no command that changes a document; the document model carries the dirty state, its marker on the tab and in the status bar and the close prompt (Save, Don't save, Cancel). There is no autosave, no recovery file.
- **Security (spec 6):** no JavaScript, launch action or embedded file ever runs; an external link opens only after a click and a confirmation card that shows the URL; the only network access of the product is the updater (not in 2.1).
- **Printing (spec 6):** Windows through the Win32 print dialog and `FPDF_RenderPage` (Core's `PrintPageToDeviceAsync`), Linux through CUPS with an own Avalonia dialog; always light; annotations printed; a document that forbids printing locks the command; jobs run in the background with progress and cancel.
- **Speed (spec 5):** budget = measured median of the PDF app rounded as in M1 (time to 10 ms, memory to 5 MiB, size to 1 MiB), limit = budget x 1.25 once; Windows and Linux are binding, macOS is indicative; heavy work (thumbnails, text index, OCR) starts only after the first sharp page; a miss is a finding for the owner, budgets are never loosened.
- **Not in this milestone:** any command that changes a document, saving, the marks and comments panes, forms, page organisation, redaction, OCR, installers, default-app registration, the updater and its settings row, macOS, anything new beyond the predecessor's 1.0 features.
- **Done** means: `dotnet build Raffica.slnx -c Release` (0 warnings), the test command of `CLAUDE.md` with `RAFFICA_REQUIRE_OCR=1`, `dotnet format Raffica.slnx --verify-no-changes` and `dotnet run tools/roadmap.cs -- check` are green, and each acceptance criterion carries its proof in `Docs/roadmap.json`.
- **Commits:** Conventional Commits with the trailer `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`; stage files by name; `git show --stat HEAD` after each commit; the noreply address only. Branch `feature/m21-viewer`; PR to `dev` at the end, merged when `ci` and `roadmap` are green. Tick the roadmap tasks as they finish (`dotnet run tools/roadmap.cs -- generate`).

## Calls made while planning (author's calls, the owner can overturn them)

1. **Names:** project `src/Raffica.Pdf` (`AssemblyName` `RafficaPdf`, root namespace `Raffica.Pdf`), tests `tests/Raffica.Pdf.Tests`. Matches `RafficaGallery` and the CLI `raffica`.
2. **Two settings files, no merge:** the shell's `SettingsStore` (`Raffica/Pdf/settings.json`, `ShellSettings`: theme, language, compact bar) and Core's `SettingsStore`/`AppSettings` (`<UserDataPaths.ConfigDirectory()>/pdf-settings.json`: page appearance, default layout and zoom mode, last printer, print scaling). The overlapping `Theme`, `Language` and `Density` fields of `AppSettings` are not read by the app. Removing the overlap is a backlog entry. Recent files and positions use Core's `RecentFilesStore` (`recent.json` in the same folder).
3. **Settings dialog extension:** `SettingsDialogOptions(bool ShowAutosave = true, IReadOnlyList<SettingsExtraRow>? ExtraRows = null)`, where the app builds the editors itself and reads them in its `onOk`; when the autosave row is hidden the dialog returns `current.Autosave` unchanged.
4. **Palette entries:** the frame's search gets an optional `ExtraResults` provider for entries that are not ribbon commands (open tabs, recent files, outline entries, "Go to page N"); the ribbon commands stay the only thing the frame itself searches. Pagewing's palette had these groups and the spec lists "the command palette with tabs and contents".
5. **Tab semantics:** a file that is already open is selected, not opened twice (the predecessor opened a new tab; two tabs of one file would both hold dirty state in 2.2). Tab dragging between windows (predecessor 4.1) is decided in the inventory (Task 1): the frame's `DocumentTabs` has no drag-out, so it is listed as open for after 2.6 unless the owner asks for it.
6. **Test PDF generator:** not imported in 2.1. The fixtures under `testdata/pdfs` (`text-rich`, `text-dense`, `thousand-pages`, `outline`, `rotated-page`, `wide-page`, `cropbox`, `dark-sample`, `encrypted_hello_world_r3`, `print-mixed`, `not-a-pdf`, `empty`) cover every viewer test; a task that needs a new fixture says so. Recorded in `Docs/DECISIONS.md` (Task 1).
7. **Backlog item adopted:** "the selected state of document tabs is not exposed to assistive technology" is done in Task 3, because the viewer is the first app that lives in the tab row. The other accessibility items stay in the backlog.
8. **Budget measurement needs the owner's machines:** `measure.yml` runs on runners the owner starts by hand. The code, the check and the CI steps are built and tested here; the recorded medians, the reference measurements (predecessor 1.0 and Chrome) and the "within budget on Windows and Linux" criterion are produced by runs the owner starts (Task 16 names the commands). They are listed as open until then.
9. **One plan, large milestone:** 2.1 has 17 tasks. The spec allows splitting (extra ids) if this is too large; the natural cut is after Task 11 (viewing and searching) with printing and budgets as the second part. Not split unless the owner asks.

## Review Focus

1. **A file that cannot be shown is passed as argument, dropped, sent by a second instance or chosen in the dialog** (wrong password, truncated, not a PDF, empty, unreadable, vanished meanwhile). Expected: a card names the problem, the app and its other tabs stay alive, nothing half-open is left behind. Pinned by `OpenFlowTests.EveryOpenErrorEndsInACardAndNoTab` (Task 6).
2. **A second instance sends hostile or odd paths** (relative, nonexistent, 64 at once, a URL, a directory, a non-PDF). Expected: only existing regular files open, the rest is reported, the listener never throws. Pinned by `InstanceHandoverTests.OddPathsAreReportedAndNeverThrow` (Task 7).
3. **The user jumps to the end of the 1000-page file** (End key, scroll bar drag, "Go to page 1000") while renders are pending. Expected: only the visible pages and a bounded number of previews are requested, old requests are dropped, memory stays inside the cache budget. Pinned by `PageViewScrollTests.JumpToTheEndPlansOnlyTheVisiblePages` (Task 8).
4. **An external link has a deceptive or dangerous address** (look-alike host, `javascript:`, `file:`, very long URL). Expected: dangerous targets are not clickable, deceptive ones show Core's `ExternalLinkWarning` on the card, the address is opened exactly as displayed and only after the confirmation. Pinned by `LinkCardTests.DangerousTargetsAreNotClickableAndDeceptiveOnesWarn` (Task 11).
5. **Printing with no printer, a locked document, a printer that fails on page 3, or Cancel in the middle.** Expected: the command is disabled with a reason, or the job card reports the failed pages and the app stays usable, Cancel stops the job and leaves no temporary file. Pinned by `PrintCommandTests.FailedPagesAreReportedAndCancelStopsTheJob` (Task 15).

---

### Task 1: Inventory, decisions and the roadmap scope changes

**Files:**
- Modify: `Docs/roadmap.json`, `Docs/ROADMAP.md` (generated), `Docs/DECISIONS.md`
- Read only: `C:\VS-Code\Pagewing\Docs\ROADMAP.md`, `README.md` (tag `v1.0.0`, run `git -C C:\VS-Code\Pagewing show v1.0.0:Docs/ROADMAP.md` so the two commits after the tag are not read as 1.0)

**Interfaces:**
- Produces: for milestones 2.1 to 2.6, a checklist in `roadmap.json` (the milestone's `tasks`) with one entry per predecessor feature, each marked with the predecessor milestone it came from (`(P3)`, `(P4)`, ...); features that will not be rebuilt are in the 2.6 milestone's evidence as "open, with the reason".

- [ ] **Step 1: Read** the predecessor's milestones 3 to 16 and its README feature list from the tag; list every user-visible feature of the viewer group (P3 open, single instance, scroll, zoom; P4 tabs, thumbnails, outline, layouts, zoom modes, go to page, document info in the status bar; P5 search, copy, links, CLI search; P6 dark mode and dark page, shortcuts and the help card (F1), palette, recent files, settings, high contrast; P7 print) and of the later groups.
- [ ] **Step 2: Enter** the checklist: viewer features not already named in the 2.1 task list become sub-entries of the matching 2.1 task text or new 2.1 tasks; features of P9 to P16 are distributed over 2.2 to 2.5. Decide and write down for each viewer feature that is not in the spec's list: done in 2.1, moved, or open with a reason (at least: tabs between windows (P4.1), the F1 help card with the shortcut overview, document info in the status bar, `raffica search` CLI (already in Core)).
- [ ] **Step 3: Record scope changes** in `roadmap.json` (`scopeChanges`): the three shell extensions (settings dialog options and extra rows, dirty marker and selected state on `DocumentTab`, `SearchField.ExtraResults`), the file-already-open rule, the generator decision, the split option; link this plan in milestone 2.1 and set its status to `planned`.
- [ ] **Step 4: Write** one `Docs/DECISIONS.md` entry "Raffica PDF: viewer conventions (M2.1)" with calls 1 to 6 above (context, decision, rationale, rejected alternatives).
- [ ] **Step 5: Run** `dotnet run tools/roadmap.cs -- generate` then `-- check`. Expected: both pass, `ROADMAP.md` regenerated.
- [ ] **Step 6: Commit** `docs: plan milestone 2.1 and record the predecessor inventory`.

### Task 2: Shell: settings dialog options

**Files:**
- Modify: `src/Raffica.Shell/Frame/SettingsDialog.cs`
- Create: `src/Raffica.Shell/Frame/SettingsDialogOptions.cs`
- Test: `tests/Raffica.Shell.Tests/Frame/SettingsDialogTests.cs`

**Interfaces:**
- Produces: `sealed record SettingsExtraRow(string Label, Control Editor)`; `sealed record SettingsDialogOptions(bool ShowAutosave = true, IReadOnlyList<SettingsExtraRow>? ExtraRows = null)`; `SettingsDialog.Create(ShellSettings current, Action<ShellSettings> onOk, Action onCancel, SettingsDialogOptions? options = null)` (the existing overload keeps working; `null` options give today's dialog).

- [ ] **Step 1: Write the failing tests** (extend the existing class): `DefaultOptionsKeepTheDialogAsItWas` (autosave row present, no extra rows), `AutosaveRowCanBeHidden` (`PART_Autosave` absent), `HiddenAutosaveKeepsTheStoredValueOnOk` (`current.Autosave = false`, OK returns `Autosave == false`), `ExtraRowsAppearAfterTheCompactRowInOrder` (labels as `secondary` text above their editors, same layout as `Field`), `ExtraEditorsKeepTheirStateWhenOkIsPressed`.
- [ ] **Step 2: Run** `dotnet test tests/Raffica.Shell.Tests -c Release --filter-namespace "*Frame*"`. Expected: FAIL (options type missing).
- [ ] **Step 3: Implement** the records and the extra parameter; the extra rows go into the dialog body after the checkbox group, each built with the existing `Field` helper.
- [ ] **Step 4: Run** the same command plus `dotnet build Raffica.slnx -c Release`. Expected: PASS, gallery unchanged (its tests still pass).
- [ ] **Step 5: Commit** `feat(shell): let an app hide the autosave row and add settings rows`.

### Task 3: Shell: dirty marker and selected state on tabs, extra search results

**Files:**
- Modify: `src/Raffica.Shell/Frame/DocumentTab.cs`, `DocumentTabs.cs`, `SearchField.cs`, `src/Raffica.Shell/Styles/Frame/*.axaml` (tab and search styles), `Strings.en.resx` and `Strings.de.resx` of the shell (`TabUnsavedChanges`)
- Create: `src/Raffica.Shell/Frame/SearchEntry.cs`
- Test: `tests/Raffica.Shell.Tests/Frame/DocumentTabsTests.cs`, `SearchFieldTests.cs`

**Interfaces:**
- Produces: `DocumentTab.IsDirty { get; set; }` (raises `INotifyPropertyChanged`; the record keeps reference equality); `DocumentTabItem` shows a dot in `Text.Secondary` before the close button when dirty, same width either way, automation name suffix from `ShellStrings.TabUnsavedChanges` ("Unsaved changes" / "Ungespeicherte Änderungen"); the tab item exposes its selected state to automation.
- Produces: `sealed record SearchEntry(string Group, string Name, Action Run, string? Detail = null)`; `SearchField.ExtraResults : Func<string, IReadOnlyList<SearchEntry>>?` (styled property); results of the provider are listed after the commands, under their `Group` heading, at most 8 per group, chosen with Up/Down/Enter like commands.

- [ ] **Step 1: Write the failing tests:** `DocumentTabsTests.DirtyTabShowsAMarkAndKeepsItsWidth`, `DirtyMarkIsInTheAutomationName`, `SelectedTabIsExposedToAutomation`; `SearchFieldTests.ExtraResultsAppearAfterCommandsUnderTheirGroup`, `ExtraResultsAreCappedAtEightPerGroup`, `EnterRunsTheChosenEntry`, `NoProviderChangesNothing`, `ProviderIsNotCalledForAnEmptyQuery`.
- [ ] **Step 2: Run** `--filter-namespace "*Frame*"`. Expected: FAIL.
- [ ] **Step 3: Implement** the property, the marker (tokens only), the automation state and the provider; strings in both resx files.
- [ ] **Step 4: Run** the namespace, then `--filter-namespace "*Tokens*"` and `TranslationTests`. Expected: PASS.
- [ ] **Step 5: Commit** `feat(shell): dirty marker and selected state on tabs, extra search results`.

### Task 4: App project, executable, window skeleton, ribbon and start view

**Files:**
- Create: `src/Raffica.Pdf/Raffica.Pdf.csproj`, `Program.cs`, `App.axaml`, `App.axaml.cs`, `PdfOptions.cs`, `PdfStrings.cs`, `PdfWindow.axaml(.cs)`, `PdfRibbon.cs`, `Resources/Strings.en.resx`, `Strings.de.resx`; `tests/Raffica.Pdf.Tests/Raffica.Pdf.Tests.csproj` plus `TestCulture.cs` (copy the serial-run and culture-reset attributes of `tests/Raffica.Shell.Tests/TestCulture.cs`)
- Modify: `Raffica.slnx` (both projects), `.github/workflows/ci.yml` (publish and smoke-test the app like the gallery: `--print-string`), `Directory.Packages.props` only if a package is missing
- Test: `tests/Raffica.Pdf.Tests/AppProjectTests.cs`, `RibbonDefinitionTests.cs`, `StartViewTests.cs`, `TranslationTests.cs` (assert both resx files hold the same keys)

**Interfaces:**
- Produces: `RafficaPdf` as `WinExe`, `PublishAot`, `InvariantGlobalization=false`, `SatelliteResourceLanguages en;de`, `AllowUnsafeBlocks` (platform code later), `TrimmerRootAssembly` for `Raffica.Shell` and `Avalonia.Themes.Fluent`, resources embedded with logical names `Raffica.Pdf.Strings.en.resources` / `.de.resources`, mirrored from `Raffica.Gallery.csproj`. `ProjectReference` to `Raffica.Core` and `Raffica.Shell` only.
- Produces: `Program.Main` whose **first statement** is `_ = AppContext.GetData("System.GC.Server");` (the Native AOT workaround of `src/Raffica.Cli/Program.cs`); `PdfOptions.Parse(string[] args)` with `IReadOnlyList<string> Files`, `--lang en|de`, `--theme light|dark|system`, `--high-contrast`, `--settings-file <path>`, `--print-string <key>` (prints one string and exits, for the CI smoke test) and the measurement switches added in Task 16.
- Produces: `PdfStrings` (as `GalleryStrings`: culture, `Get(key)`, one typed property per key; a missing key throws).
- Produces: `static class PdfRibbon { static RibbonDefinition Create(PdfCommands commands); }` with tabs **Home** (letter `H`: groups File (open `11`, print `12`), Find (search `21`, next `22`, previous `23`), Go to (go to page `31`), Pages (previous `41`, next `42`, first `43`, last `44`)) and **View** (letter `V`: groups Zoom (zoom in `11`, out `12`, fit width `13`, fit page `14`, actual size `15`), Layout (continuous `21`, single page `22`, two pages `23`, two pages with cover `24`), Page (dark page `31`), Panes (pages `41`, outline `42`)). `PdfCommands` is the record of `Action`s Task 12 fills; until then `Run` is null and the commands are disabled. The frame's rules hold (`Validate` passes: unique codes, no prefix codes).
- Produces: `PdfWindow` hosting `AppFrame` with the shell `TitleBar` (tabs, search), the ribbon band, a left pane slot (pages 204 px), a right pane slot (reserved, closed), the document slot, the status bar and the `StartView` (shown while no tab is open) with `OwnApp = StartApp.Pdf`.

- [ ] **Step 1: Write the failing tests.** `AppProjectTests.TheAppReferencesOnlyCoreAndShell` (the compiled assembly's `GetReferencedAssemblies()` contains neither `Raffica.Pdfium` nor `Raffica.Ocr`; the csproj has exactly two `ProjectReference` items), `ProgramStartsWithTheAppContextWorkaround` (reads `Program.cs`, first statement), `PrintStringPrintsAnEnglishAndAGermanValue`; `RibbonDefinitionTests.TheRibbonIsValid`, `EveryCommandHasAFieldCodeAndAName`, `TabsAreHomeAndViewWithLettersHAndV`; `StartViewTests.StartViewIsShownWithoutTabsAndHidesWithOne`; `TranslationTests.BothLanguagesHaveTheSameKeys`.
- [ ] **Step 2: Run** `dotnet test tests/Raffica.Pdf.Tests -c Release`. Expected: FAIL (projects missing).
- [ ] **Step 3: Implement** the projects and the window; theme and language through `ShellTheme.Apply` and `PdfStrings.Culture` from `ShellSettings` (default path `SettingsStore.DefaultPath("Pdf")`, or `--settings-file`), ink `PDF` plum.
- [ ] **Step 4: Run** the tests, `dotnet build Raffica.slnx -c Release` (0 warnings) and `dotnet publish src/Raffica.Pdf -c Release -r win-x64` on Windows. Expected: PASS; the published `RafficaPdf.exe --print-string <a key>` prints the English text when its output is redirected.
- [ ] **Step 5: Commit** `feat(pdf): add the app project, window skeleton, ribbon and start view`.

### Task 5: Document and tab model with the dirty state and the close prompt

**Files:**
- Create: `src/Raffica.Pdf/Documents/PdfTab.cs`, `Workspace.cs`, `CloseGuard.cs`, `src/Raffica.Pdf/Dialogs/UnsavedChangesCard.cs`
- Modify: `PdfWindow.axaml.cs` (bind tabs, status bar save cell), both resx files
- Test: `tests/Raffica.Pdf.Tests/Documents/WorkspaceTests.cs`, `CloseGuardTests.cs`, `TabBindingTests.cs`

**Interfaces:**
- Produces: `sealed class PdfTab : IDisposable` with `ViewerSession Session`, `DocumentTab Tab` (the shell model), `string Title`, `string? Path`, `bool IsDirty { get; }`, `void MarkDirty()`, `void MarkClean()`, `event Action? DirtyChanged`. `MarkDirty` also sets `Tab.IsDirty`. Nothing in 2.1 calls `MarkDirty` outside tests.
- Produces: `enum CloseChoice { Save, DontSave, Cancel }`; `sealed class CloseGuard(Func<PdfTab, Task<CloseChoice>> prompt, Func<PdfTab, Task<bool>> save)` with `Task<bool> CanCloseAsync(PdfTab tab)`: a clean tab closes without a prompt; a dirty tab asks once; `Save` closes only if `save` returned true; `Cancel` keeps the tab; and `Task<bool> CanCloseAllAsync(IEnumerable<PdfTab>)` that stops at the first Cancel.
- Produces: `sealed class Workspace` over Core's `TabList<PdfTab>`: `PdfTab? Find(string path)` (full-path, case-insensitive on Windows), `PdfTab Add(ViewerSession session, string path)`, `Task<bool> CloseAsync(PdfTab tab)` (asks the guard, then `Session.Dispose`, then removes), `IReadOnlyList<PdfTab> Tabs`, `PdfTab? Active`, `Select(PdfTab)`, `SelectNext()`, `SelectPrevious()`, and `ActiveChanged`; selecting calls `Session.SetActive(true)` for the new tab and `false` for the old (Core's tab budget).
- Produces: `UnsavedChangesCard.Create(string fileName, Action<CloseChoice> chosen)` as a shell `Dialog` with the three buttons (Save primary).

- [ ] **Step 1: Write the failing tests:** `WorkspaceTests.OpeningTheSameFileSelectsTheExistingTab`, `ClosingTheActiveTabSelectsTheNeighbour`, `ActiveTabAloneHoldsTheFullRenderBudget` (uses a fake `ViewerSession` constructed with the test constructor of Core, or a `PdfTab` over a real session on `hello_world.pdf`; asserts `SetActive` order), `ClosingTheLastTabLeavesTheWorkspaceEmpty`; `CloseGuardTests.CleanTabClosesWithoutAPrompt`, `DirtyTabAsksOnce`, `SaveThatFailsKeepsTheTab`, `CancelKeepsTheTab`, `CloseAllStopsAtTheFirstCancel`; `TabBindingTests.DirtyStateShowsOnTheTabAndInTheStatusBar` (`MarkDirty` sets the shell tab's marker and the status bar `SaveState` to the German or English "Unsaved changes" text; `MarkClean` clears both).
- [ ] **Step 2: Run** `dotnet test tests/Raffica.Pdf.Tests -c Release --filter-namespace "*Documents*"`. Expected: FAIL.
- [ ] **Step 3: Implement** the classes and the binding; the window asks `CloseGuard.CanCloseAllAsync` on `Closing` and cancels the close when it returns false.
- [ ] **Step 4: Run** the namespace. Expected: PASS.
- [ ] **Step 5: Commit** `feat(pdf): add the document and tab model with the dirty state and the close prompt`.

### Task 6: Open flow: argument, dialog, drag and drop, password, errors

**Files:**
- Create: `src/Raffica.Pdf/Documents/OpenFlow.cs`, `src/Raffica.Pdf/Dialogs/PasswordCard.cs`, `ErrorCard.cs`
- Modify: `Program.cs` / `App.axaml.cs` (open the files of `PdfOptions.Files` after the window is shown), `PdfWindow.axaml.cs` (drop target, file dialog), both resx files
- Test: `tests/Raffica.Pdf.Tests/Documents/OpenFlowTests.cs`, `PasswordCardTests.cs`, `DropTests.cs`

**Interfaces:**
- Consumes: Task 5 `Workspace`, `PdfTab`; Core `ViewerSession.OpenAsync(string path, string? password = null)`, `ViewerState` (`Ready`, `PasswordRequired`, `PasswordIncorrect`, `Failed`), `PdfOpenError`.
- Produces: `sealed class OpenFlow(Workspace workspace, Func<PdfEngine> engine, Func<string, bool, Task<string?>> askPassword, Action<string, PdfOpenError> showError)` with `Task<PdfTab?> OpenAsync(string path)`: an already open file is selected and returned; the password is asked on `PasswordRequired` and again (with the "incorrect" hint, `bool` = true) on `PasswordIncorrect`, Cancel returns null; any other error calls `showError(path, error)`; no tab exists for a file that did not reach `Ready`. `OpenFlow.OpenManyAsync(IEnumerable<string>)` opens sequentially, the last one successfully opened is active.
- Produces: `PasswordCard.Create(string fileName, bool incorrect, Action<string?> done)`, `ErrorCard.Create(string fileName, PdfOpenError error, Action done)` with one German and one English text per `PdfOpenError` value; the file dialog uses `TopLevel.StorageProvider.OpenFilePickerAsync` (filter `*.pdf`), drag and drop accepts files (`DataFormat.File`), non-PDF extensions are offered to `OpenFlow` anyway so that Core's `NotAPDF` error is shown, not silently ignored.

- [ ] **Step 1: Write the failing tests** (real `PdfEngine`, fixtures from `TestData.Pdf`): `OpenFlowTests.OpeningAFileAddsAndSelectsATab`, `AlreadyOpenFileIsSelectedNotOpenedTwice`, `PasswordIsAskedAndTheRightOneOpens` (`encrypted_hello_world_r3.pdf`, password `hôtel`), `WrongPasswordAsksAgainWithTheIncorrectHint`, `CancelingThePasswordLeavesNoTab`, `EveryOpenErrorEndsInACardAndNoTab` (missing file, `not-a-pdf.pdf`, `empty.pdf`, a directory, a file deleted between check and open: five cases, asserting `showError` got the `PdfOpenError` and `Workspace.Tabs` is unchanged), `SeveralFilesOpenInOrderAndTheLastIsActive`; `PasswordCardTests.TheCardHasFieldAndOkAndCancelAndEnterConfirms`; `DropTests.DroppedFilesReachTheOpenFlow`.
- [ ] **Step 2: Run** `--filter-namespace "*Documents*"`. Expected: FAIL.
- [ ] **Step 3: Implement** `OpenFlow` and the cards (tokens only, automation names, focus on the field); wire argument, dialog (Ctrl+O is added in Task 12) and drop.
- [ ] **Step 4: Run** the namespace. Expected: PASS. Manual (record in the evidence): `RafficaPdf.exe testdata\pdfs\text-rich.pdf` opens the file.
- [ ] **Step 5: Commit** `feat(pdf): open files by argument, dialog and drop, with passwords and error cards`.

### Task 7: Single instance, recent files, position per file, stores

**Files:**
- Create: `src/Raffica.Pdf/Hosting/InstanceHandover.cs`, `src/Raffica.Pdf/Settings/PdfSettingsFiles.cs`, `RecentService.cs`
- Modify: `Program.cs`, `App.axaml.cs`, `PdfWindow.axaml.cs` (start view recent list), both resx files
- Test: `tests/Raffica.Pdf.Tests/Hosting/InstanceHandoverTests.cs`, `Settings/RecentServiceTests.cs`

**Interfaces:**
- Consumes: Core `SingleInstance.TryBecomePrimary(string name)`, `SingleInstance.TrySend(string name, IReadOnlyList<string> paths, TimeSpan timeout)`, `StartListening(Action<IReadOnlyList<string>>)`, `SingleInstance.DefaultName`; `RecentFiles`, `RecentFilesStore(path).Load()/TrySave(RecentFiles)`, `RememberedView(ViewState View, double Zoom, ZoomMode ZoomMode, PageLayoutMode Layout)`.
- Produces: `static class InstanceHandover { static bool TryHandOver(string name, IReadOnlyList<string> files, TimeSpan timeout); static IReadOnlyList<string> Accept(IReadOnlyList<string> paths, Action<string> report); }` where `Accept` resolves each path to a full path, keeps only existing regular files, reports the rest and never throws. `Program.Main`: without files and no instance running the app starts normally; with files and a running instance the files are sent and the process exits 0 before any Avalonia start; a failed send within 1 s starts a normal instance.
- Produces: `PdfSettingsFiles` with `static string ShellPath` (`SettingsStore.DefaultPath("Pdf")`), `PdfSettingsPath` (`<UserDataPaths.ConfigDirectory()>/pdf-settings.json`), `RecentPath` (`.../recent.json`), each overridable by the `--settings-file` folder so measurement runs never touch the user's files.
- Produces: `sealed class RecentService(RecentFilesStore store)` loaded **after the first sharp page** (never in the start path): `Task LoadAsync()`, `IReadOnlyList<Raffica.Shell.Frame.RecentFile> ForStartView(int count)` (5), `ForPalette(int count)` (10), `RememberedView? ViewOf(string path)`, `void Touch(string path)`, `void Remember(string path, RememberedView view)` (on tab close and window close), `void Remove(string path)`, `void Clear()`; a missing, corrupt or oversized file gives an empty list.

- [ ] **Step 1: Write the failing tests:** `InstanceHandoverTests.SecondInstanceSendsItsFilesToTheFirst` (real pipe with a unique test name), `NoRunningInstanceMeansNoHandover`, `OddPathsAreReportedAndNeverThrow` (relative path, nonexistent, directory, URL string, 64 paths, a path with spaces, a non-PDF file that exists), `RelativePathsAreResolvedAgainstTheSendersFolder` (the sender resolves before sending); `RecentServiceTests.RecentListSurvivesARestart`, `CorruptFileGivesAnEmptyList`, `PositionIsRememberedPerFile`, `NothingIsReadBeforeLoadAsync`, `ClearAndRemoveWork`, `RecentFilesNeverTouchTheUsersFolderWhenASettingsFileIsGiven`.
- [ ] **Step 2: Run** `--filter-namespace "*Hosting*"` and `"*Settings*"`. Expected: FAIL.
- [ ] **Step 3: Implement** the classes and wire them: the listener posts to the UI thread and calls `OpenFlow.OpenManyAsync`, then activates and raises the window.
- [ ] **Step 4: Run** the namespaces. Expected: PASS. Manual (record): start the app with `text-rich.pdf`, run `RafficaPdf.exe hello_world.pdf` (relative path) in another shell: exit code 0, still one process, the file is a second tab.
- [ ] **Step 5: Commit** `feat(pdf): single instance, recent files and the position per file`.

### Task 8: Page view: bitmap bridge, continuous scroll, render scheduling, dark page

**Files:**
- Create: `src/Raffica.Pdf/Viewing/TileBitmaps.cs`, `PageView.cs`, `PageViewScroll.cs`, `src/Raffica.Pdf/Styles/PageView.axaml`
- Modify: `PdfWindow.axaml.cs` (document slot shows the active tab's `PageView`)
- Test: `tests/Raffica.Pdf.Tests/Viewing/TileBitmapsTests.cs`, `PageViewScrollTests.cs`, `PageViewRenderTests.cs`

**Interfaces:**
- Consumes: Core `ViewerSession` (`Document`, `Cache`, `Scheduler`, `LoadedSizes`, events `Changed`, `PagesInvalidated`), `DocumentLayout` (`Zoom`, `Mode`, `SetSizes`, `GetPageRect`, `GetVisiblePages`, `ContentWidth`, `ContentHeight`), `ViewportPlanner.Plan(DocumentLayout layout, DipRect viewport, double renderScaling) -> IReadOnlyList<RenderRequest>`, `RenderScheduler.Update(...)`, `TileReady(TileKey)`, `RenderCache.TryGet(TileKey, out RenderedImage)`, `TileKey(Page, ScaleKey, Column, Row)`, `PageTheme`.
- Produces: `sealed class TileBitmaps(int maxBytes)` converting a `RenderedImage` (BGRA, opaque, top row first, `Stride`) to a `WriteableBitmap` (`PixelFormat.Bgra8888`, `AlphaFormat.Opaque`), keeping an LRU keyed by `TileKey` that never holds more than `maxBytes`: `Bitmap? Get(TileKey key, RenderCache cache)`, `void Evict(IEnumerable<int> pages)`, `int Count`, `long Bytes`.
- Produces: `PageView : Control` for one `PdfTab`: `PdfTab? Tab` (styled), `double Zoom`, `PageLayoutMode Layout`, `ZoomMode ZoomMode`, `int CurrentPage`, `PageTheme PageTheme`, `Vector Offset`, `event Action? ViewChanged`, `void GoToPage(int page)`, `ViewState CaptureView()`, `void RestoreView(ViewState view, double zoom, ZoomMode mode, PageLayoutMode layout)`, `bool FirstSharpPageShown` and `event Action? FirstSharpPage`. It paints page backgrounds, previews (scaled up) and sharp tiles from the cache, calls `Scheduler.Update` with the plan after every scroll, zoom or size change, marshals `TileReady` to the UI thread and invalidates only the affected page rects, and paints the dark page's background from `PageThemeColors.DarkBackground` (no image inversion: the pixels already come dark from Core).
- Produces: scrolling with mouse wheel (smooth, one step 48 DIP), PageUp/PageDown/Home/End/arrow keys, scroll bars from the shell theme, middle-button and touch pan are not built. `PageViewScroll` is the pure part (wheel and key offsets, clamping, anchor preserved on resize).

- [ ] **Step 1: Write the failing tests:** `TileBitmapsTests.PixelsAreConvertedBgraWithoutSwappingChannels` (a 2 x 2 image with four known colours read back from the bitmap), `StaysWithinTheByteBudgetAndEvictsLeastRecentlyUsed`, `EvictRemovesAllTilesOfAPage`; `PageViewScrollTests.WheelStepIs48DipAndClampsAtBothEnds`, `HomeAndEndGoToTheFirstAndLastPage`, `JumpToTheEndPlansOnlyTheVisiblePages` (open `thousand-pages.pdf`, `GoToPage(999)`, assert the requests handed to the scheduler are at most the visible pages plus `ViewportPlanner.MaxPreviewRequests` and none belongs to page 0), `ResizeKeepsTheTopAnchor`; `PageViewRenderTests.FirstSharpPageFiresOnceAfterThePageIsPainted` (`text-basic.pdf`), `DarkPageUsesTheDarkBackgroundAndKeepsImagesInColour` (`dark-sample.pdf`, sample a pixel of the page background and one of an image area), `InvalidatedPagesAreRerendered`, `NothingIsRequestedForATabThatIsNotActive`.
- [ ] **Step 2: Run** `--filter-namespace "*Viewing*"`. Expected: FAIL.
- [ ] **Step 3: Implement** the bridge and the control; the control owns no PDFium state, only the tab's session. Use `ILogicalScrollable` only if the plain `OnPointerWheelChanged` plus own scroll bars cannot do it; keep one drawing pass (`Render`) for pages, previews and tiles.
- [ ] **Step 4: Run** the namespace, then the render-regression project to confirm Core's output is unchanged: `dotnet test tests/Raffica.Render.Tests -c Release`. Expected: PASS.
- [ ] **Step 5: Commit** `feat(pdf): add the page view with continuous scroll, render scheduling and the dark page`.

### Task 9: Zoom modes, layouts, go to page, status bar, per-tab view state

**Files:**
- Create: `src/Raffica.Pdf/Viewing/ViewCommands.cs`, `src/Raffica.Pdf/Dialogs/GoToPageCard.cs`
- Modify: `PageView.cs`, `PdfWindow.axaml.cs` (status bar cells), both resx files
- Test: `tests/Raffica.Pdf.Tests/Viewing/ZoomTests.cs`, `LayoutTests.cs`, `TabViewStateTests.cs`, `StatusBarBindingTests.cs`

**Interfaces:**
- Consumes: Task 8 `PageView`; Core `ZoomSteps.Next/Previous/Clamp`, `ZoomMode`, `PageLayoutMode`, `DocumentLayout.FitZoom(ZoomMode mode, int page, double viewportWidth, double viewportHeight)`, `ViewState`, `RememberedView`.
- Produces: `PageView.ZoomIn()`, `ZoomOut()`, `SetZoomMode(ZoomMode)`, `SetZoom(double)`, `SetLayout(PageLayoutMode)`; Ctrl+wheel and Ctrl+plus/minus zoom around the pointer; fit modes follow window resizes; a manual zoom switches the mode to `Free`; the status bar shows the zoom text (`ZoomText`, `Zoom`), "Page n of N" (`Position`), the page count and the page size in the `Count` cell, and drives `PageView` back when its zoom slider moves.
- Produces: each `PdfTab` keeps its own zoom, mode, layout and `ViewState`; switching tabs restores them; new tabs start with `AppSettings.DefaultLayout` and `DefaultZoomMode`, or the remembered position of that file from `RecentService`.
- Produces: `GoToPageCard.Create(int pageCount, int current, Action<int?> done)` (Ctrl+G), accepts 1 to `pageCount`, rejects anything else with an inline hint.
- Produces (inventory, Task 1): `DocumentInfoCard.Create(DocumentInfo info, Action done)` in `src/Raffica.Pdf/Dialogs/DocumentInfoCard.cs` showing file name, page count, page size, PDF version, file size and metadata from Core's `DocumentInfoReader.ReadAsync(document, fullPath, ct)`; opened by activating the status bar's `Count` cell when that cell is interactive, otherwise only through the palette entry "Document info" (Task 11 adds it to `PdfPaletteSource`); test `DocumentInfoCardTests.ShowsTheFieldsOfTheDocument` in `tests/Raffica.Pdf.Tests/Viewing/`.

- [ ] **Step 1: Write the failing tests:** `ZoomTests.StepsFollowCoreZoomSteps`, `ZoomIsClampedToCoreMinAndMax` (0.1 to 8), `FitWidthFollowsTheWindowWidth`, `ManualZoomLeavesFitMode`, `CtrlWheelZoomKeepsThePointUnderThePointerStable`, `ZoomOnAnOddPageStaysFinite` (`rotated-page.pdf`, `wide-page.pdf`, `cropbox.pdf` at 10 % and 800 %: no NaN or zero-sized layout); `LayoutTests.SwitchingToTwoPagesKeepsTheCurrentPage`, `SinglePageShowsOneRow`, `CoverLayoutPutsPageOneAlone`, `LayoutIsKeptPerTab`; `TabViewStateTests.SwitchingTabsRestoresPageZoomAndPosition`, `NewTabStartsWithTheDefaults`, `ARememberedFileOpensAtItsPosition`; `StatusBarBindingTests.PositionAndZoomFollowTheView`, `SliderChangesTheZoom`; `GoToPageCardTests.AcceptsOnlyPagesInRange`.
- [ ] **Step 2: Run** `--filter-namespace "*Viewing*"`. Expected: FAIL.
- [ ] **Step 3: Implement** commands and bindings.
- [ ] **Step 4: Run** the namespace. Expected: PASS.
- [ ] **Step 5: Commit** `feat(pdf): zoom modes, layouts, go to page and per-tab view state`.

### Task 10: Pages pane and outline pane

**Files:**
- Create: `src/Raffica.Pdf/Panes/PagesPane.cs`, `OutlinePane.cs`, `src/Raffica.Pdf/Styles/Panes.axaml`
- Modify: `PdfWindow.axaml.cs` (left pane switcher, F6 ring), both resx files
- Test: `tests/Raffica.Pdf.Tests/Panes/PagesPaneTests.cs`, `OutlinePaneTests.cs`, `PaneFocusTests.cs`

**Interfaces:**
- Consumes: Core `ViewerSession.EnsureThumbnails() -> ThumbnailSource?` (`Cache`, `Scheduler`, `PageTheme`), `ThumbnailPlanner`, `PdfDocument.GetOutlineAsync() -> IReadOnlyList<PdfOutlineItem>` (`Title`, `PageIndex`, `IsOpen`, `Children`); shell `Pane`, `PaneRing`.
- Produces: `PagesPane` (204 px, virtualised list of thumbnails, current page marked, click and Enter go to the page, thumbnails requested only for visible items and only after the first sharp page); `OutlinePane` (tree, keyboard: arrows, Enter jumps, Right/Left expand and collapse, entries without a page are not jumpable); the pane switcher shows one at a time; closing both collapses the left slot; `Ctrl+Shift+1` and `Ctrl+Shift+2` open them; both panes take part in F6 (navigation, document).

- [ ] **Step 1: Write the failing tests:** `PagesPaneTests.ClickingAThumbnailGoesToItsPage`, `OnlyVisibleThumbnailsAreRequested` (1000 pages: the scheduler's pending count stays under a bound), `ThumbnailsStartAfterTheFirstSharpPage`, `CurrentPageFollowsTheView`; `OutlinePaneTests.ClickingAnEntryJumpsToItsPage` (`outline.pdf`), `KeyboardReachesEveryEntryOfALargeOutline`, `EntryWithoutAPageDoesNotJump`, `CyclicOutlineDoesNotHang` (`outline-cycle.pdf`), `NoOutlineShowsTheEmptyHint`; `PaneFocusTests.F6VisitsNavigationThenDocument` and `ClosedPanesAreSkipped`.
- [ ] **Step 2: Run** `--filter-namespace "*Panes*"`. Expected: FAIL.
- [ ] **Step 3: Implement** both panes; the outline is read once per tab on first show.
- [ ] **Step 4: Run** the namespace. Expected: PASS.
- [ ] **Step 5: Commit** `feat(pdf): add the pages pane and the outline pane`.

### Task 11: Search, selection and copy, links

**Files:**
- Create: `src/Raffica.Pdf/Text/SearchSession.cs`, `SearchBar.cs`, `SelectionController.cs`, `LinkController.cs`, `src/Raffica.Pdf/Dialogs/LinkCard.cs`
- Modify: `PageView.cs` (overlay layer for hits, selection and links; cursors), `PdfWindow.axaml.cs`, both resx files
- Test: `tests/Raffica.Pdf.Tests/Text/SearchSessionTests.cs`, `SelectionTests.cs`, `CopyTests.cs`, `LinkCardTests.cs`

**Interfaces:**
- Consumes: Core `DocumentSearch.SearchAsync(PdfDocument doc, string query, SearchOptions options, int startPage, CancellationToken ct) -> IAsyncEnumerable<SearchPageResult>` (`PageIndex`, `Hits`, `PagesDone`, `PageCount`, `LimitReached`; `MaxHits` 10 000), `SearchHit` (`PageIndex`, `Rects`, `Snippet`), `PageTextSource` (`EnsurePageTexts()`), `PageText` (`CaretAt`, `WordAt`, `LineAt`, `RectsOf`, `LinkAt`), `TextSelection`, `TextFlow.Plain/Raw/Html`, `PageLink` with `PageTarget(int PageIndex, double? Top)` and `UriTarget(string Url)`, `ExternalLink.TryParse(string url, out ExternalLink link)` and `ExternalLinkWarning`.
- Produces: `SearchSession(PdfTab tab)`: `void Start(string query, SearchOptions options)` (cancels the previous run, starts at the current page and wraps), `IReadOnlyList<SearchHit> Hits`, `int Current`, `void Next()`, `void Previous()`, `string Summary` ("12 hits" / "12 Treffer", "More than 10,000 hits"), `event Action Changed`, `void Cancel()`. The title-bar field stays the command palette (Ctrl+K); document search has its own bar (`SearchBar`, Ctrl+F, Enter or F3 next, Shift+F3 previous, Esc closes) in the notification slot, with match case and whole word toggles. Highlights are drawn by `PageView` for visible pages, the current hit in the ink colour, and the view scrolls to the current hit. Search runs in the background with Core's cooperative scheduling; closing the tab or the bar cancels it.
- Produces: `SelectionController` (mouse drag selects by character, double click a word, triple click a line, I-beam cursor over text, Ctrl+A selects the page text of the shown pages' flow, Ctrl+C copies `TextFlow.Plain` as `text/plain` and `TextFlow.Html` as `text/html`), `LinkController` (hand cursor over a link, internal links jump to the page and `Top`, external links show `LinkCard` with the address; only `Open` after a 400 ms arming delay starts the system launcher; Esc and Cancel never open anything).
- Produces: `PdfPaletteSource` for `SearchField.ExtraResults`: open tabs, recent files (10), outline entries (up to 50), "Document info" (opens Task 9's `DocumentInfoCard`) and "Go to page N" when the query is a number, each as `SearchEntry` (Task 3).

- [ ] **Step 1: Write the failing tests:** `SearchSessionTests.FindsEveryOccurrenceWithItsRectangle` (`text-rich.pdf`), `StartsOnTheCurrentPageAndWraps`, `NextAndPreviousCycle`, `NewQueryCancelsTheOldRun`, `ClosingTheTabCancelsTheRun`, `EmptyOrWhitespaceQueryClearsTheHits`, `QueryWithRegexCharactersIsLiteral` (`.*`, `(`, `[`), `LimitIsReportedAt10000Hits` (`text-dense.pdf`, query `a`), `GermanAndEnglishSummary`; `SelectionTests.DragSelectsTheTextBetweenTheCarets`, `DoubleClickSelectsAWord`, `SelectionAcrossTwoPagesFollowsReadingOrder`; `CopyTests.CopyPutsPlainAndHtmlOnTheClipboard`; `LinkCardTests.AnExternalLinkOpensOnlyAfterConfirmation` (launcher called once, with the displayed address), `CancelAndEscapeNeverOpen`, `OpenIsIgnoredDuringTheArmingDelay`, `DangerousTargetsAreNotClickableAndDeceptiveOnesWarn` (`javascript:`, `file:`, a look-alike host, a 5 000 character URL), `InternalLinkJumpsWithoutTheLauncher`, `ADoubleClickNeverReachesOpen`; `PaletteSourceTests.ListsTabsRecentOutlineAndGoToPage`.
- [ ] **Step 2: Run** `--filter-namespace "*Text*"`. Expected: FAIL.
- [ ] **Step 3: Implement** the sessions, controllers, overlay and cards; the launcher is injected (`Action<Uri>`), the default uses `Process.Start` with `UseShellExecute` only for the validated `http`, `https` and `mailto` address.
- [ ] **Step 4: Run** the namespace. Expected: PASS.
- [ ] **Step 5: Commit** `feat(pdf): search, selection and copy, links and palette entries`.

### Task 12: Commands, shortcuts and field codes

**Files:**
- Create: `src/Raffica.Pdf/Commands/PdfCommandCatalog.cs`
- Modify: `PdfRibbon.cs`, `PdfWindow.axaml.cs`, both resx files
- Test: `tests/Raffica.Pdf.Tests/Commands/CommandCatalogTests.cs`, `ShortcutTests.cs`, `FieldCodeIntegrationTests.cs`

**Interfaces:**
- Consumes: Tasks 4 to 11.
- Produces: `PdfCommandCatalog` as the single source of every command's id, name, icon key, field code and shortcut, filled into `PdfCommands`; enabled state follows the active tab (no tab: only Open; print disabled when `GetPrintInfoAsync().PrintAllowed` is false, with the reason in the tooltip). Shortcuts: Open Ctrl+O, Close tab Ctrl+W, Next/Previous tab Ctrl+Tab and Ctrl+Shift+Tab, Print Ctrl+P, Find Ctrl+F, Next/Previous hit F3 and Shift+F3, Go to page Ctrl+G, Zoom in/out Ctrl+Plus and Ctrl+Minus, Actual size Ctrl+0, Fit width Ctrl+1, Fit page Ctrl+2, Dark page Ctrl+Shift+D, Settings Ctrl+Comma, Pages pane and Outline pane Ctrl+Shift+1 and Ctrl+Shift+2. Every command is reachable through its field code and through the search field (Ctrl+K).
- Produces (inventory, Task 1): `HelpCard.Create(IReadOnlyList<HelpEntry> entries, Action done)` with `sealed record HelpEntry(string Group, string Name, string Shortcut)` in `src/Raffica.Pdf/Dialogs/HelpCard.cs`, opened by F1 and listing every catalog command that has a shortcut, grouped as the ribbon is, shortcuts formatted for the platform; test `CommandCatalogTests.HelpCardListsEveryShortcutOfTheCatalog`.
- Wiring rule (pre-flight rulings R4, R5): Print (Ctrl+P) and Settings (Ctrl+Comma) are registered with a null `Run` until Tasks 15 and 13 wire them; the per-tab dark page toggle (Ctrl+Shift+D) is implemented here and writes no setting.

- [ ] **Step 1: Write the failing tests:** `CommandCatalogTests.EveryCommandHasANameInBothLanguages`, `ShortcutsAreUnique`, `EveryViewerActionHasAShortcutOrAFieldCode`, `NoDocumentDisablesEverythingButOpen`, `PrintIsDisabledForALockedDocumentWithAReason` (a fixture that forbids printing: if none exists in `testdata/pdfs`, test the enable rule with a fake `PdfPrintInfo(false, true)` source); `ShortcutTests.CtrlWClosesTheActiveTabThroughTheGuard`, `CtrlTabCyclesTabs`, `ShortcutsAreIgnoredWhileADialogIsOpen`; `FieldCodeIntegrationTests.AltHVRunsZoomInAndOpensTheViewTab` (e.g. Alt, `V`, `1`, `1` zooms in).
- [ ] **Step 2: Run** `--filter-namespace "*Commands*"`. Expected: FAIL.
- [ ] **Step 3: Implement** the catalog and key handling (the platform command modifier from `PlatformSettings.HotkeyConfiguration`, as the frame does).
- [ ] **Step 4: Run** the namespace, then the whole project. Expected: PASS.
- [ ] **Step 5: Commit** `feat(pdf): commands, shortcuts and field codes`.

### Task 13: Settings that apply to PDF

**Files:**
- Create: `src/Raffica.Pdf/Settings/PdfSettingsController.cs`
- Modify: `PdfWindow.axaml.cs` (Ctrl+Comma opens the dialog), both resx files
- Test: `tests/Raffica.Pdf.Tests/Settings/PdfSettingsTests.cs`

**Interfaces:**
- Consumes: Task 2 `SettingsDialogOptions`; shell `SettingsStore`, `ShellSettings`; Core `SettingsStore`, `AppSettings` (`PageAppearance`, `DefaultLayout`, `DefaultZoomMode`), `PageAppearanceSetting`.
- Produces: `PdfSettingsController.Open()` shows the dialog with `ShowAutosave: false` and three extra rows (page appearance Light/Dark as a `SegmentedControl`, default layout and default zoom as `SelectBox`es with the four layouts and the four zoom modes); OK stores both files, applies theme and compact bar at once, applies page appearance to open tabs through `Session.SetPageTheme`, and the new defaults to tabs opened later; language applies after a restart (the shell dialog says so).

- [ ] **Step 1: Write the failing tests:** `PdfSettingsTests.DialogHasNoAutosaveRow`, `ExtraRowsShowTheStoredValues`, `OkWritesBothFilesAndAppliesTheTheme`, `PageAppearanceReachesOpenTabs`, `DefaultsApplyToTheNextTabOnly`, `CorruptSettingsFilesGiveDefaults`, `SettingsFileSwitchNeverTouchesTheUsersFolder`.
- [ ] **Step 2: Run** `--filter-namespace "*Settings*"`. Expected: FAIL.
- [ ] **Step 3: Implement** the controller; `Ctrl+Shift+D` toggles the dark page of the active tab only (per-tab switch, spec 2 goal 1) without writing the setting.
- [ ] **Step 4: Run** the namespace. Expected: PASS.
- [ ] **Step 5: Commit** `feat(pdf): settings that apply to PDF`.

### Task 14: Printing on Windows

**Files:**
- Create (port, see steps): `src/Raffica.Pdf/Platform/Windows/WindowsPrintService.cs`, `PrintDialogNative.cs`, `Gdi.cs`, `DevMode.cs`; `src/Raffica.Pdf/Platform/PlatformPrintServices.cs` (`static IPrintService Create()` picking the service by OS)
- Test: `tests/Raffica.Pdf.Tests/Platform/WindowsPrintNativeTests.cs`, `WindowsPrintToPdfTests.cs` (the second runs only on Windows)

**Interfaces:**
- Consumes: Core `IPrintService` (`IsAvailable`, `HasSystemDialog`, `ShowDialogAsync(PrintRequest, CancellationToken)`, `GetPrintersAsync`, `SupportsDuplexAsync`, `PrintAsync(PrintJobRequest, IProgress<PrintProgress>, CancellationToken)`), `PdfDocument.PrintPageToDeviceAsync(int page, nint hdc, DevicePlacement placement, CancellationToken ct)`, `PrintPlan.Create`, `PrintPlacement.Plan`.
- Produces: `WindowsPrintService : IPrintService`.

- [ ] **Step 1: Import commit (unchanged).** From the read-only checkout at tag `v1.0.0`, copy only `src/Pagewing.App/Platform/Windows/{WindowsPrintService,PrintDialogNative,Gdi,DevMode}.cs` and their tests `WindowsPrintNativeTests.cs` and `WindowsPrintToPdfTests.cs` into the paths above using `git -C C:\VS-Code\Pagewing archive v1.0.0 <paths>` extracted into a new empty folder outside the repo, then copied byte for byte. Commit `chore: import the predecessor's Windows print service unchanged` (this commit does not build and `NoPagewingLeftoverTests` fails on it, as in M0; it is squash-merged with the branch, so only the adaptation commit counts).
- [ ] **Step 2: Adapt** (second commit): rename namespaces and types to `Raffica.Pdf.Platform.Windows`, translate German comments, drop everything that is not print (the update helper, file type registry and default-app service are not imported), fix the `using`s to the Core namespaces, keep `[LibraryImport]`. Run `dotnet test tests/Raffica.Pdf.Tests -c Release --filter-namespace "*Platform*"`. Expected on Windows: PASS (the print-to-PDF tests print into "Microsoft Print to PDF" and check the page count); on Linux the Windows tests skip.
- [ ] **Step 3: Verify** `NoPagewingLeftoverTests` passes again and the build has 0 warnings. Commit `feat(pdf): print on Windows through the Win32 dialog and FPDF_RenderPage`.

### Task 15: Printing on Linux, print command and job card

**Files:**
- Create (port): `src/Raffica.Pdf/Platform/Linux/CupsNative.cs`, `CupsOptions.cs`, `CupsPrintService.cs`
- Create (new UI): `src/Raffica.Pdf/Printing/PrintCommand.cs`, `PrintCard.cs` (the Linux dialog on the shell's controls: printer, pages, copies, collate, paper size, duplex, orientation), `PrintJobCard.cs`
- Modify: `PdfCommands` wiring, both resx files, `AppSettings.LastPrinter` and `PrintScaling` use
- Test: `tests/Raffica.Pdf.Tests/Platform/CupsOptionsTests.cs`, `CupsPrintServiceTests.cs` (gated by `RAFFICA_TEST_CUPS_PRINTER` and `RAFFICA_TEST_CUPS_OUTPUT`, skipped when unset), `tests/Raffica.Pdf.Tests/Printing/PrintCommandTests.cs`, `PrintCardTests.cs`

**Interfaces:**
- Consumes: Task 14 `PlatformPrintServices.Create()`; Core `PrintRequest`, `PrintTicket`, `PrintJobRequest`, `PrintProgress(Done, Total, FailedPages)`, `PrintSettings`, `PrintScaling`, `PrinterInfo`, `PdfPrintInfo`.
- Produces: `PrintCommand(IPrintService service, Func<PdfTab?> active, ...)` with `Task RunAsync()`: refuses a document that forbids printing, asks the dialog (system dialog on Windows, `PrintCard` on Linux), runs the job in the background with `IProgress<PrintProgress>`, shows `PrintJobCard` (progress, Cancel), reports failed pages when done, never blocks the UI thread; always light, annotations printed (the options Core's service already sets).

- [ ] **Step 1: Import commit (unchanged)** of the three Linux files and their tests from `v1.0.0`, as in Task 14 step 1. Commit `chore: import the predecessor's CUPS print service unchanged`.
- [ ] **Step 2: Adapt** (second commit): namespaces, English comments, only print-related code; run `dotnet test ... --filter-namespace "*Platform*"` on Linux (with a CUPS-PDF queue and the two variables set). Expected: PASS (range 1-2 with 2 copies sorted gives 4 pages; an encrypted file prints and leaves no temporary copy; a forbidding document is refused and nothing is sent). Commit `feat(pdf): print on Linux through CUPS`.
- [ ] **Step 3: Write the failing tests** for the new UI with a fake `IPrintService`: `PrintCommandTests.LockedDocumentIsRefusedWithAReasonAndNothingIsSent`, `NoPrinterDisablesTheCommandWithAReason`, `ProgressReachesTheJobCard`, `FailedPagesAreReportedAndCancelStopsTheJob` (fake fails page 3, then a second run is cancelled after page 2; asserts the report lists page 3, the fake saw no page after cancel, no temporary file), `TheUiThreadIsNotBlocked` (the fake waits on a gate while the test still pumps the dispatcher), `LastPrinterIsRemembered`; `PrintCardTests.RangeAndCopiesAreValidated`, `DuplexRowHiddenWhenUnsupported`, `EnterPrintsEscCancels`.
- [ ] **Step 4: Implement** the command and cards (tokens only; the Linux dialog is built fresh, the predecessor's `PrintCard` view is not ported); run `--filter-namespace "*Printing*"`. Expected: PASS.
- [ ] **Step 5: Commit** `feat(pdf): print command, Linux print dialog and job card`.

### Task 16: Speed budgets, measurement switches and the cause check

**Files:**
- Modify: `src/Raffica.Pdf/PdfOptions.cs`, `App.axaml.cs`; `bench/shell-probe/measure.cs` (new `--target pdf`, cases and limits); `.github/workflows/measure.yml` (`target` choice `pdf`, `EXE`, `PROJECT`, `FONTS`, the 1000-page and the second-file cases); `.github/workflows/ci.yml` (step "Size of the published PDF app", smoke test); `Docs/DECISIONS.md`
- Create: `src/Raffica.Pdf/Measurement.cs`; `Docs/measurements/pdf-viewer-<date>/` (CSVs and machine notes, once the owner's runs exist)
- Test: `tests/Raffica.Pdf.Tests/MeasurementTests.cs`; `dotnet run bench/shell-probe/measure.cs -- selftest`

**Interfaces:**
- Produces: switches `--measure-startup` (prints `first-page` when the first sharp tile of page 1 of the given file is painted, second animation frame after it is in the cache), `--measure-second` (the running instance prints `second-page` for a file handed over by a second process, timed from the arrival of the handover), `--measure-scroll` (600 wheel steps through the open file, prints `scroll p99-ms=… max-ms=…` from the gaps of animation frames), `--measure-memory` (as the gallery: `memory-bytes <n>` two seconds after the first page) and `--measure-input` is not repeated; every line in the invariant culture; a run that does not finish exits with 3 after 30 s; measurement runs never read the user's settings or recent files (`--settings-file`).
- Produces: `measure.cs --target pdf` cases `cold`, `warm`, `thousand` (`thousand-pages.pdf`), `second`, `scroll`, `memory` and `size`, and `check --target pdf --platform windows|linux` with one limit table (single copy in `CheckCommand`, pinned by `selftest`), as for the gallery.

- [ ] **Step 1: Write the failing tests:** `MeasurementTests.FirstPageLineIsWrittenOnceAfterTheFirstSharpTile`, `ResultLinesUseTheInvariantCulture`, `MeasurementNeverReadsOrWritesTheUsersSettings`, `ScrollLineHasP99AndMax`; extend the `selftest` of `measure.cs` for the new target (arguments of a pdf run, CSV mode `pdf`, `check` verdicts at, above and below a limit, missing metric fails, macOS indicative).
- [ ] **Step 2: Run** `dotnet test tests/Raffica.Pdf.Tests -c Release --filter-namespace "*Measurement*"` and `dotnet run bench/shell-probe/measure.cs -- selftest`. Expected: FAIL.
- [ ] **Step 3: Implement** the switches, the script target and the workflow changes; the cases and their order follow spec section 5 (cold and warm start with a file, a further file in a running instance, a 1000-page file to the first page, scrolling a long document, memory with a large file open, published size). Native AOT publish of the app must stay free of warnings.
- [ ] **Step 4: Run** the tests, the selftest and the local size check `dotnet run bench/shell-probe/measure.cs -- size --publish-dir artifacts/pdf/win-x64 --fonts-dir src/Raffica.Shell/Assets/Fonts --out <csv> --target pdf` after `dotnet publish src/Raffica.Pdf -c Release -r win-x64 -o artifacts/pdf/win-x64`. Expected: PASS, size recorded.
- [ ] **Step 5: Cause check first.** Run a local cold and warm start of the published app next to the predecessor's published `v1.0.0` build on the same machine (development VM timings are only relative: its compositor is slow and is flagged "Invalid measurement"). If the app is slower, look for the cause before raising a finding: defer the PDFium initialisation until after the window's first frame, defer building the ribbon's overflow popups and the pane controls until first use, read recent files only after the first sharp page, check that no pane or palette is built at start. Each change is a commit with its before and after numbers in the message.
- [ ] **Step 6: Owner runs (open until done, listed in the evidence).** The owner starts the runners and runs, for each machine: `gh workflow run measure.yml --ref feature/m21-viewer -f platform=windows -f target=pdf -f note="<GPU, resolution, scaling, power>"` and the same for `linux`; the predecessor 1.0 and Chrome are measured as references on the same machines with the method of the predecessor's decision entry "Vergleichsmessung und endgültige Geschwindigkeitsbudgets" (read in the checkout at execution time). Then record in `Docs/DECISIONS.md` the entry "Raffica PDF speed budgets (M2.1)": medians per machine and case, budgets rounded as in M1, limits budget x 1.25, the reference numbers, and a finding for the owner for every case where the app is slower than the predecessor or above a limit. Put the CSVs under `Docs/measurements/pdf-viewer-<date>/`. Put the limits into `CheckCommand` and re-run `selftest`.
- [ ] **Step 7: Run** `dotnet run bench/shell-probe/measure.cs -- check --target pdf --platform windows --in "<CSVs>"` and the same for `linux`. Expected: both exit 0 (a miss is a finding, never a changed limit).
- [ ] **Step 8: Commit** `feat(pdf): measurement switches, PDF speed budgets and checks` (budgets in a second commit `docs: record the PDF speed budgets`).

### Task 17: Close-out

**Files:**
- Modify: `Docs/roadmap.json`, `Docs/ROADMAP.md` (generated), `Docs/DECISIONS.md` (corrections to the viewer entry), `CHANGELOG.md` (`[Unreleased]`: "Raffica PDF viewer: open, tabs, scroll, zoom, search, links, print"), `CLAUDE.md` (project-specific: the app project, its settings files and switches)

- [ ] **Step 1: Run the full verification** and keep the output: `dotnet build Raffica.slnx -c Release`, `$env:RAFFICA_REQUIRE_OCR=1; dotnet test Raffica.slnx -c Release --no-build --max-parallel-test-modules 1` (after `dotnet run tools/native-fetch.cs -- --rid win-x64 --out artifacts/native/win-x64`), `dotnet format Raffica.slnx --verify-no-changes`, `dotnet run tools/roadmap.cs -- check`.
- [ ] **Step 2: Tick** the acceptance criteria with proof: the render-regression and viewer tests (command and counts); the budgets recorded and `measure.yml` within them (run ids, or "open: owner's runs", with the command of Task 16 step 6); the app opens an argument file and a second file in the running instance (manual check from Task 7, recorded with date and machine); every predecessor feature of the viewer group is done or open in the inventory (Task 1, compared by hand). Everything that needs the owner's machines (printers on Windows and Linux, `WindowsPrintToPdfTests` on a real Windows desktop, the Linux CUPS tests, the hardware measurements, the title bar on Wayland) is listed as open with its command.
- [ ] **Step 3: Set** milestone 2.1 to `done` with the date only when every criterion is ticked or explicitly open-with-reason as in milestone 1.2; otherwise leave `in-progress` and say what is missing. Regenerate the roadmap.
- [ ] **Step 4: Open the PR** to `dev` (`gh pr create --base dev`), wait for `ci` and `roadmap`, merge (squash) when green. The PR body ends with the attribution line from the session reminder.
