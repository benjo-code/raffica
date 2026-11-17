# Raffica Write 4.3c: New Documents and Recovery Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Write can start a blank document from its own style set, a crash loses at most about 30 s of typing (recovery copy plus a restore offer), and the picture cache has a budget, a pixel cap and no decoding on the render thread.

**Architecture:** A blank DOCX is generated as bytes in `Raffica.Write.Docx` and opened through the ordinary `DocxFile.Open(bytes)`, so an untitled tab is a normal tab with no path. A recovery copy is a full DOCX written with the existing guarded writer (`DocxFile.SaveAsync` to another path) under the 4.3a edit lease, plus a JSON sidecar; a UI-free `AutosaveScheduler` over an `IRecoverySubject` drives it, and `RecoveryFlow` offers and restores at the next start. `ImageCache` gains limits, LRU eviction with deferred disposal, `TryGet` and `Warm`/`Prefetch`.

**Tech Stack:** .NET 10 Native AOT, Avalonia 12, SkiaSharp, Open XML SDK 3.x (package layer only), xunit v3 (Microsoft Testing Platform).

**Spec:** `Docs/superpowers/specs/2026-11-17-raffica-write-4-3c-new-documents-and-recovery-design.md` (approved 2026-11-17). The subproject spec is `Docs/superpowers/specs/2026-11-13-raffica-write-design.md` (section 6). Branch: `feature/m04-3c-new-documents-and-recovery`. Read the sections "Write library (M4.1)", "Write viewer (M4.2)", "Write editing (M4.3a)" and "Write formatting (M4.3b)" of `CLAUDE.md` before starting; the rules there (lease, `EditSession.Apply`, pool-thread dispatcher capture, start-cost guards, LF line endings) bind every task.

## Global Constraints

- Blank document: Normal, Heading 1 to 9, Title, Caption, TOC 1 to 9, Footnote Text, List Paragraph, Hyperlink, plus the numbering definitions; body Carlito 11 pt, headings Caladea; A4 with 2.5 cm margins, US Letter with 1 inch margins when `RegionInfo` is US or CA; Heading 1 to 9 are outline levels 0 to 8 and carry no `numPr`.
- Untitled title: "Untitled", "Untitled 2", ... (smallest free number in the window), German "Unbenannt", "Unbenannt 2"; never in the recent list until saved.
- Recovery copy every 30 s while unsaved changes exist, and on window deactivation; the user's file is written only on Save; the timer starts at the first edit, never at startup; nothing of the store, timer or blank document exists before first use (`StartupCreatesNoRecoveryWork`).
- A copy is skipped while the session is faulted, a save runs, only a prefix is open, or its revision equals the last one written; a failed copy is silent (no card, no dirty mark, no blocked typing); a copy never calls `MarkSaved` and never changes the origin, stamp or snapshot of the `DocxFile`.
- The recovery folder is `<AppFiles.ConfigDirectoryFor(settingsFile)>/recovery` (follows `--settings-file`); tests and measurements never touch the user's folder; no document text in file names, logs, sidecars or exception messages.
- Offer: once per process, by the first window shown (after its first sharp page, or after `Opened` when it has no file), never in a measurement run; Restore opens dirty tabs bound to no path (Save is Save as with the old name suggested); a copy is deleted only after its tab was saved or closed with Save or Don't save, or on Discard; an unreadable copy is reported and kept; an entry whose owner process (pid plus start time) is alive and is not this process is skipped, never offered, never deleted.
- Autosave setting: the stored `ShellSettings.Autosave` value, shown in Write's dialog under Write's own label (en "Keep a recovery copy of unsaved work", de "Wiederherstellungskopie ungespeicherter Arbeit anlegen"); off writes no copies, existing copies are still offered.
- `ImageCache` defaults: byte budget 256 MiB of decoded pixels, cap 32,000,000 pixels per picture, cap always at most half the budget; the render thread never decodes in the app.
- Typing, format and the new `typing-snapshot` case: worst p95 at most 16.0 ms (CPU cost, the 4.3a reading). A limit is never loosened.
- Every UI string in English and German ("Sie" form), every command its own Lucide icon and a reason in each disabled state, every card through `CardFooter`; both `.resx` files LF; `dotnet format Raffica.slnx --verify-no-changes` clean; no warning suppressed.
- Out of scope: a shared crash guard for Write (moves to 4.9, recorded in the roadmap's scope changes), templates, the owner gates of 4.2, 4.3a and 4.3b.

## Review Focus

1. The recovery folder is unwritable or the disk is full during a copy: typing continues, the previous copy stays whole, no card (Task 5, `FailedSnapshotLeavesThePreviousCopyAndIsSilent`).
2. The recovery folder holds unrelated files, a truncated or non-JSON sidecar, or a sidecar whose DOCX is missing: the offer neither crashes nor deletes anything it did not write (Task 4, `GarbageInTheFolderIsIgnoredAndNeverDeleted`).
3. The timer fires while only a prefix is open, the session is faulted, or a save runs: no copy, no exception (Task 6, `TickSkipsWhatCannotBeSnapshotted`).
4. Fifty leftover copies after a crash: the card stays usable and lists a bounded number (Task 8, `ManyEntriesAreListedBounded`).
5. An IME composition is open when a copy is due: the pool-thread lease and the UI-thread preedit lease never deadlock (Task 6, `SnapshotDuringAPreeditDoesNotDeadlock`).

---

## File structure

| File | Responsibility |
|---|---|
| `src/Raffica.Write.Docx/Blank/BlankDocument.cs`, `BlankPaper.cs`, `BlankStyleSet.cs` | Bytes of a blank DOCX, the paper choice, the style and numbering data |
| `src/Raffica.Write/Documents/WriteTab.cs`, `WriteWorkspace.cs`, `OpenFlow.cs`, `DocumentOpener.cs`, `WriteSaveFlow.cs`, `CloseGuard.cs` | Pathless tabs, new-document open, save of an untitled tab |
| `src/Raffica.Write/Documents/UntitledNames.cs` | The smallest free "Untitled N" |
| `src/Raffica.Write/Documents/ModelReadLease.cs` | The lease hooks around `DocxFile`'s model read, shared by save and snapshot |
| `src/Raffica.Write/Recovery/RecoveryEntry.cs`, `RecoveryStore.cs`, `RecoveryLiveness.cs` | The folder, the sidecar, who owns a copy |
| `src/Raffica.Write/Recovery/IRecoverySubject.cs`, `RecoverySnapshot.cs`, `AutosaveScheduler.cs`, `IRecoveryClock.cs`, `WriteTabRecoverySubject.cs` | What is snapshotted, how, and when |
| `src/Raffica.Write/Recovery/RecoveryFlow.cs`, `RecoveryCoordinator.cs`, `Dialogs/RecoveryCard.cs` | The offer, Restore, Discard |
| `src/Raffica.Write.Layout/Floats/ImageCache.cs` (+ `ImageHeader.cs`) | Limits, LRU, `TryGet`, `Warm`, `Prefetch` |
| `tests/Raffica.Write.RecoveryHarness` (new exe) | Child process killed by the acceptance test |
| `Docs/*`, `CLAUDE.md`, `CHANGELOG.md` | Roadmap, decision, notes (Task 13) |

---

### Task 1: The blank document and its style set

**Files:**
- Create: `src/Raffica.Write.Docx/Blank/BlankDocument.cs`, `BlankPaper.cs`, `BlankStyleSet.cs`
- Test: `tests/Raffica.Write.Tests/BlankDocumentTests.cs`, `tests/Raffica.Write.Tests/Layout/BlankDocumentLayoutTests.cs`

**Interfaces:**
- Produces: `public readonly record struct PaperSize(int WidthTwips, int HeightTwips, int MarginTwips)` with `PaperSize.A4` (11906, 16838, 1417) and `PaperSize.Letter` (12240, 15840, 1440); `public static class BlankPaper { public static PaperSize For(RegionInfo region); }` (US and CA give Letter); `public static class BlankDocument { public static byte[] Create(PaperSize paper); }`; `BlankStyleSet.Ids` (the 33 style ids: `Normal`, `Heading1`..`Heading9`, `Title`, `Caption`, `TOC1`..`TOC9`, `FootnoteText`, `ListParagraph`, `Hyperlink`).

- [ ] **Step 1: Write the failing tests** in `BlankDocumentTests`: `EveryBuiltInStyleExists` (open `BlankDocument.Create(PaperSize.A4)` with `DocxFile.Open(bytes)`, assert every id of `BlankStyleSet.Ids` resolves in `Model.Styles`); `HeadingsAreOutlineLevelsZeroToEightWithoutNumbering` (Heading n has outline level n-1 through `StyleResolver`, and no `numPr`); `NormalIsCarlito11AndHeadingsAreCaladea` (resolved fonts and size 22 half-points through `StyleResolver` with `document.Numbering` as third argument); `RegionChoosesPaper` (`US`, `CA` give `Letter`; `DE`, `GB` give `A4`; section page size and margins in the model match); `HeadingColoursReachContrast` (every heading and Title colour has WCAG contrast at least 4.5:1 against white); `SaveAndReopenKeepsTheModel` (save to a temp path with `DocxFile.SaveAsync`, reopen, same style ids, one empty paragraph, same section); `SavedBlankDocumentPassesSchemaValidation` (`DocumentFormat.OpenXml.Validation.OpenXmlValidator` reports no error on the saved file). In `BlankDocumentLayoutTests`: `ABlankDocumentLaysOutAsOneEmptyPage` (`DocumentLayout.Create` over the model, `LayOutRestAsync`, `IsComplete`, page count 1).
- [ ] **Step 2: Run to verify they fail:** `dotnet test tests/Raffica.Write.Tests -c Release --filter-class "*BlankDocument*"`. Expected: compile errors for the missing types.
- [ ] **Step 3: Implement the three types.** `Create` writes a minimal package with `System.IO.Packaging`-free raw zip entries (`[Content_Types].xml`, `_rels/.rels`, `word/document.xml`, `word/_rels/document.xml.rels`, `word/styles.xml`, `word/numbering.xml`, `word/settings.xml`, `word/fontTable.xml`, `docProps/core.xml`, `docProps/app.xml`), deterministic bytes (fixed entry timestamps). Numbering: one multi-level decimal definition (nine levels, `%1.`, `%1.%2.` ...) not linked to any style, plus a bullet and a number list definition for the list commands of 4.4 (the spec's "one multi-level definition for headings and the bullet and number lists" is read this way; record it in the decision). The look (sizes, spacing, heading colours, Caption and Hyperlink looks) is one design pass against `PRODUCT.md` and `Docs/design/direction-d`; write the chosen values and why into the DECISIONS entry of Task 13.
- [ ] **Step 4: Run again; all pass.** Same command; check the count is 8.
- [ ] **Step 5: Commit** `feat(write): blank document and built-in style set (M4.3c)`.

---

### Task 2: Untitled tabs

**Files:**
- Modify: `src/Raffica.Write/Documents/WriteTab.cs`, `WriteWorkspace.cs`, `OpenFlow.cs`, `DocumentOpener.cs`, `WriteSaveFlow.cs`; `src/Raffica.Write/Commands/SaveCommandState.cs`; `src/Raffica.Write/WriteWindow.axaml.cs`, `WriteWindow.Save.cs`, `WriteWindow.View.cs`; the shell `DocumentTab` if its `Path` is not nullable
- Create: `src/Raffica.Write/Documents/UntitledNames.cs`; strings `UntitledName`, `UntitledNumberedFormat` in Write's `Strings.en.resx` and `Strings.de.resx` ("Untitled", "Untitled {0}"; "Unbenannt", "Unbenannt {0}")
- Test: `tests/Raffica.Write.App.Tests/Documents/UntitledTabTests.cs`, extend `SaveFlowTests`, `CloseGuardTests`

**Interfaces:**
- Produces: `WriteTab.Path` becomes `string?`; `bool WriteTab.IsUntitled => Path is null`; `string? WriteTab.SuggestedSavePath { get; set; }`; `WriteTab(OpenedDocument opened, string untitledTitle)` second constructor; `DocumentOpener.OpenBlank(byte[] bytes, OpenWork work, CancellationToken token) : OpenAttempt` (a whole file, not a prefix, page 1 laid out); `OpenFlow.NewDocumentAsync(PaperSize paper, CancellationToken token) : Task<WriteTab?>`; `UntitledNames.Next(IEnumerable<WriteTab> tabs) : string`; `SaveCommandState` gains `Untitled` and `Refusal(saveAs: false)` skips `NothingToSave` for an untitled tab.

- [ ] **Step 1: Write the failing tests** in `UntitledTabTests`: `TitlesAreTheSmallestFreeNumber` (open three, close the second, the next is "Untitled 2"; German culture gives "Unbenannt 2"); `AnUntitledTabIsEditableAtOnce` (`Runner.FullModelReady`, typing marks it dirty); `SaveOnAnUntitledTabIsSaveAs` (the flow asks `PickSavePathAsync`, never `SaveOverOriginalAsync`; after success `Path` is the picked path, `IsUntitled` false); `SaveIsEnabledForAnUntitledCleanTab`; `AnUntitledTabIsNotInTheRecentList`; `ClosingAnUnchangedUntitledTabAsksNothing` and `ClosingAnEditedOneAsksAndSaveRunsSaveAs` (in `CloseGuardTests`); `WorkspaceFindIgnoresUntitledTabs`.
- [ ] **Step 2: Run to verify they fail:** `dotnet test tests/Raffica.Write.App.Tests -c Release --filter-class "*UntitledTabTests"`.
- [ ] **Step 3: Implement.** Make every consumer of `WriteTab.Path` null-safe (`WriteWorkspace.Find`, `WriteSaveFlow` lines using `tab.Path`, `_recent.Touch(tab.Path)` skips null, `ShowOpenError` only runs for opened files); `WriteSaveFlow.RunAsync` takes the Save as branch when `tab.IsUntitled`; `PickSavePathAsync` uses `tab.SuggestedSavePath` for the suggested name and folder when set. `Rebind` clears `SuggestedSavePath`.
- [ ] **Step 4: Run tests of the class, then** `dotnet test tests/Raffica.Write.App.Tests -c Release --filter-class "*SaveFlowTests" --filter-class "*CloseGuardTests"`; all pass.
- [ ] **Step 5: Commit** `feat(write): untitled tabs (M4.3c)`.

---

### Task 3: The New document command

**Files:**
- Modify: `src/Raffica.Write/Commands/WriteCommandIds.cs` (`New = "file.new"`), `WriteCommandCatalog.cs`, `WriteCommandRules.cs`, `WriteRibbon.cs`, `Platform/MacOS/WriteMacMenu.cs`, `WriteWindow.axaml.cs` (`NewDocumentAsync`), `src/Raffica.Shell/Styles/Icons.axaml` only if the icon is missing there; `Strings.en.resx`, `Strings.de.resx` (`CmdNew`, `DescNew`: "New document", "Neues Dokument")
- Test: `tests/Raffica.Write.App.Tests/Commands/NewDocumentCommandTests.cs`, existing `CommandIconTests`-style guards, `RibbonStartupTests`

**Interfaces:**
- Consumes: `OpenFlow.NewDocumentAsync`, `BlankPaper.For(RegionInfo.CurrentRegion)` (the window takes a `Func<PaperSize>` with that default so tests pass one).
- Produces: command `file.new`, Ctrl+N (⌘N on macOS through the catalog's existing key helper), its own Lucide icon `FilePlus` (a key no other command uses), a Home key tip that no other command in the group uses (`KeyTipTests` decides).

- [ ] **Step 1: Write the failing tests:** `NewDocumentOpensAnUntitledTabAndSelectsIt`; `CtrlNRunsIt` (through the page view and the window key path); `ItIsDisabledWhileTheWindowCloses` with a reason (the rules table test walks every id in every state); `TheCommandHasItsOwnIcon`; `HomeBandWidthHintStillHolds` (`RibbonStartupTests` unchanged and green); `StartupCreatesNoBlankDocumentWork` (a window made with no file has built no blank document: a counter on `BlankDocument` use through the flow).
- [ ] **Step 2: Run to verify they fail:** `dotnet test tests/Raffica.Write.App.Tests -c Release --filter-class "*NewDocumentCommandTests"`.
- [ ] **Step 3: Implement** the catalog entry (`Cmd(Key.N, "N")`), the rule (enabled whenever the window is not closing), the ribbon placement next to Open, the macOS menu line, the strings in both languages.
- [ ] **Step 4: Run** the class and `RibbonStartupTests`, `TranslationTests`, `WriteCommandTests`; all pass.
- [ ] **Step 5: Commit** `feat(write): New document command (M4.3c)`.

---

### Task 4: The recovery store

**Files:**
- Create: `src/Raffica.Write/Recovery/RecoveryEntry.cs`, `RecoveryStore.cs`, `RecoveryLiveness.cs`
- Test: `tests/Raffica.Write.App.Tests/Recovery/RecoveryStoreTests.cs`

**Interfaces:**
- Produces:
  - `internal sealed record RecoveryEntry(string Id, string Title, string? OriginalPath, DateTimeOffset Written, long Bytes, int OwnerProcessId, DateTimeOffset OwnerStarted)`.
  - `internal sealed class RecoveryStore(string folder)`: `static string FolderFor(string? settingsFile)`; `static string NewId()` (GUID, `"N"` format); `string DocxPathOf(string id)`; `Task WriteSidecarAsync(RecoveryEntry entry)` (atomic, through `AtomicFile`, after the DOCX exists: the sidecar is the commit marker); `RecoveryListing List()` with `IReadOnlyList<RecoveryEntry> Entries` and `IReadOnlyList<string> Problems` (ids whose sidecar is unreadable or whose DOCX is missing); `void Delete(string id)` (the pair only); no document text anywhere in a sidecar.
  - `internal static class RecoveryLiveness { static bool IsLive(RecoveryEntry entry, int selfProcessId, IProcessProbe probe); }` with `IProcessProbe { DateTimeOffset? StartTimeOf(int pid); }` and a real `SystemProcessProbe`; live means the process exists, is not self, and its start time is within 2 s of `OwnerStarted`.
  - Sidecar JSON through a `JsonSerializerContext` (Native AOT).

- [ ] **Step 1: Write the failing tests:** `FolderFollowsTheSettingsFile`; `SidecarRoundTrips`; `ListSeesOnlyCompletePairs`; `GarbageInTheFolderIsIgnoredAndNeverDeleted` (unrelated file, truncated JSON, non-JSON, sidecar without DOCX: `Entries` excludes them, `Problems` names the sidecar ones, `Delete` of a good id touches nothing else, listing deletes nothing); `DeleteRemovesThePairOnly`; `LivenessSkipsAnAliveForeignProcess` (fake probe: alive and other pid is live; dead pid is not; same pid with a different start time is not; self is not live); `NoDocumentTextInTheSidecar` (the sidecar of a typed document contains no typed character sequence).
- [ ] **Step 2: Run to verify they fail:** `dotnet test tests/Raffica.Write.App.Tests -c Release --filter-class "*RecoveryStoreTests"`.
- [ ] **Step 3: Implement** the three types; nothing reads or creates the folder before the first call (`RecoveryStore` is not constructed at start, Task 6 guards it).
- [ ] **Step 4: Run; all pass.**
- [ ] **Step 5: Commit** `feat(write): recovery store and owner check (M4.3c)`.

---

### Task 5: The snapshot writer and the shared lease

**Files:**
- Create: `src/Raffica.Write/Documents/ModelReadLease.cs`, `src/Raffica.Write/Recovery/IRecoverySubject.cs`, `RecoverySnapshot.cs`
- Modify: `src/Raffica.Write/Documents/WriteSaveFlow.cs` (use `ModelReadLease`)
- Test: `tests/Raffica.Write.App.Tests/Recovery/RecoverySnapshotTests.cs`; existing `SaveFlowTests` stay green

**Interfaces:**
- Produces:
  - `internal static class ModelReadLease { static IDisposable Attach(DocxFile docx, Func<IDisposable> takeLease, Action? afterLeaseTaken = null); }` sets `BeforeModelRead` and `AfterModelRead` as `WriteSaveFlow.WriteAsync` does today (lease taken and released on the saving thread, released in `finally` on every failure) and restores the previous hooks on dispose.
  - `internal interface IRecoverySubject { string Id {get;} string Title {get;} string? OriginalPath {get;} DocxFile Docx {get;} int Revision {get;} bool IsDirty {get;} SnapshotSkip? CannotSnapshotNow {get;} IDisposable TakeModelLease(); }`; `enum SnapshotSkip { Disposed, Prefix, Faulted, Saving }`.
  - `internal static class RecoverySnapshot { static Task<SnapshotResult> WriteAsync(IRecoverySubject subject, RecoveryStore store, CancellationToken token); }`; `abstract record SnapshotResult` with `Written(int Revision, TimeSpan LeaseHeld, TimeSpan Total)`, `Skipped(SnapshotSkip Reason)`, `Failed(Exception Error)` (the exception is kept for the caller, never shown).

- [ ] **Step 1: Write the failing tests:** `ASnapshotWritesADocxAndASidecarThatOpen` (open the copy, same text); `SavingACopyLeavesOriginStampAndSnapshotUntouched` (afterwards `docx.SaveAsync(origin)` succeeds with no `OriginalChanged`, `IsDirty` unchanged, no `MarkSaved`); `TheRevisionIsReadInsideTheLease` (an edit queued from another thread between "start" and "lease taken" is either in the copy and the recorded revision, or after both); `AllowedSnapshotsOnlyWhenSkipReasonIsNull`; `FailedSnapshotLeavesThePreviousCopyAndIsSilent` (the internal `BeforeReplace` seam throws `IOException`: result is `Failed`, the earlier copy opens and has the earlier text, no exception escapes); `ModelReadLeaseReleasesOnEveryFailure`.
- [ ] **Step 2: Run to verify they fail:** `dotnet test tests/Raffica.Write.App.Tests -c Release --filter-class "*RecoverySnapshotTests"`.
- [ ] **Step 3: Extract `ModelReadLease`** from `WriteSaveFlow.WriteAsync` unchanged in behavior; run `SaveFlowTests` first (green) before using it in the snapshot. **Implement `RecoverySnapshot.WriteAsync`:** check `CannotSnapshotNow`; write the DOCX with `subject.Docx.SaveAsync(store.DocxPathOf(id), overwriteChangedOriginal: false, token)` (the other-path save of 4.1) under `ModelReadLease`, record revision and lease-held time inside the hooks, then `WriteSidecarAsync` with the writer's pid and start time.
- [ ] **Step 4: Run both classes; all pass.**
- [ ] **Step 5: Commit** `feat(write): recovery snapshot over the shared model-read lease (M4.3c)`.

---

### Task 6: The scheduler and the window wiring

**Files:**
- Create: `src/Raffica.Write/Recovery/IRecoveryClock.cs`, `AutosaveScheduler.cs`, `WriteTabRecoverySubject.cs`
- Modify: `src/Raffica.Write/Documents/WriteTab.cs` (`DirtyChanged` event raised by `UpdateDirty` when the value changes), `WriteWindow.axaml.cs` and `WriteWindow.Save.cs` (create the scheduler on the first dirty tab, `Deactivated`, release on close and after close questions)
- Test: `tests/Raffica.Write.App.Tests/Recovery/AutosaveSchedulerTests.cs`, `StartupCreatesNoRecoveryWorkTests.cs`

**Interfaces:**
- Consumes: `RecoverySnapshot`, `RecoveryStore`, `IRecoverySubject`.
- Produces:
  - `internal interface IRecoveryClock { IDisposable Every(TimeSpan interval, Action tick); }` with `DispatcherRecoveryClock` (a `DispatcherTimer`, dispatcher captured at construction on the UI thread) and a test `ManualRecoveryClock`.
  - `internal sealed class AutosaveScheduler(RecoveryStore store, IRecoveryClock clock, Func<bool> enabled)`: `TimeSpan Interval = 30 s`; `void Track(IRecoverySubject s)` (starts the clock on the first call, never before); `void Adopt(IRecoverySubject s, string existingId)` (a restored copy); `void TickNow()` (deactivation); `void Release(IRecoverySubject s)` (deletes the copy: called after Save or Don't save, never on a bare dispose); `int FailureCount`; `IDisposable` stops the clock.
  - `WriteTabRecoverySubject(WriteTab tab)` implementing `IRecoverySubject` (`TakeModelLease` is `tab.Runner.PauseForEdit()`; `CannotSnapshotNow` reads prefix, faulted session, `Editor.IsSaving` and the save flow).

- [ ] **Step 1: Write the failing tests** with the manual clock and a fake subject: `NothingExistsBeforeTheFirstEdit` (scheduler not created, store folder not created, no timer, after opening a file); `FirstDirtyTabStartsTheClock`; `TickWritesOneCopyPerChangedTab`; `TickSkipsWhatCannotBeSnapshotted` (prefix, faulted, saving, disposed: no copy, no exception, no failure count); `TickSkipsAnUnchangedRevision`; `OneSnapshotAtATime`; `DeactivationWritesNow`; `DisabledSettingWritesNothing` (and turning it on writes at the next tick); `FailedSnapshotIsSilentAndRetriedNextTick` (`FailureCount` 1, then succeeds); `ACleanTabsCopyIsDeleted` (saved or undone to the saved revision); `ReleaseDeletesTheCopyButADisposeDoesNot`; `TheFolderFollowsSettingsFile`; and with a real `WriteTab` in `AutosaveSchedulerWindowTests`: `SnapshotDuringAPreeditDoesNotDeadlock` (a composition holds the UI-thread preedit lease request while a snapshot runs on the pool: both finish within the lease timeout), `ClosingWithDontSaveDeletesTheCopy`, `CloseWithCancelKeepsIt`.
- [ ] **Step 2: Run to verify they fail:** `dotnet test tests/Raffica.Write.App.Tests -c Release --filter-class "*AutosaveScheduler*"`.
- [ ] **Step 3: Implement** the scheduler (UI-thread state only; the snapshot's pool work is awaited and its continuation returns to the thread that started it through the captured context), the adapter and the window wiring (`tab.ModelEdited` handler that tracks when `tab.IsDirty`; `Deactivated` calls `TickNow`; the close paths call `Release` only after `CloseGuard` returned Saved or Discarded). The window's `enabled` reads the live `ShellSettings.Autosave` of the settings controller.
- [ ] **Step 4: Run both classes and `StartupTests`; all pass.**
- [ ] **Step 5: Commit** `feat(write): autosave scheduler and window wiring (M4.3c)`.

---

### Task 7: The autosave setting

**Files:**
- Modify: `src/Raffica.Shell/Frame/SettingsDialogOptions.cs` (`AutosaveLabel` optional, default null), `SettingsDialog.cs` (use it for the check box text), `src/Raffica.Write/Settings/WriteSettingsController.cs` (`ShowAutosave: true`, label from Write's strings, live `Autosave` for the scheduler), Write `Strings.en.resx` and `Strings.de.resx` (`SettingsAutosaveLabel`)
- Test: `tests/Raffica.Shell.Tests` (dialog), `tests/Raffica.Write.App.Tests/Settings/WriteSettingsTests.cs`

**Interfaces:**
- Produces: `SettingsDialogOptions(bool ShowAutosave = true, IReadOnlyList<SettingsExtraRow>? ExtraRows = null, string? AutosaveLabel = null)`.

- [ ] **Step 1: Write the failing tests:** shell `AutosaveLabelOverridesTheShellText` and `NullLabelKeepsTheShellText`; Write `TheDialogShowsTheAutosaveRowWithWritesLabel` (English and German), `ConfirmingStoresTheValue`, `TurningItOffStopsTheSchedulerWithoutDeletingCopies`; the PDF settings test that hides the row stays green.
- [ ] **Step 2: Run to verify they fail:** `dotnet test tests/Raffica.Shell.Tests -c Release --filter-class "*SettingsDialog*"` and `dotnet test tests/Raffica.Write.App.Tests -c Release --filter-class "*WriteSettingsTests"`.
- [ ] **Step 3: Implement** the option, the controller change and both strings.
- [ ] **Step 4: Run both plus `dotnet test tests/Raffica.Pdf.Tests -c Release --filter-class "*Settings*"`; all pass.**
- [ ] **Step 5: Commit** `feat(write): the recovery-copy setting in Write's dialog (M4.3c)`.

---

### Task 8: The offer and Restore

**Files:**
- Create: `src/Raffica.Write/Recovery/RecoveryFlow.cs`, `RecoveryCoordinator.cs`, `src/Raffica.Write/Dialogs/RecoveryCard.cs`; strings `RecoveryTitle`, `RecoveryBody`, `RecoveryRestore`, `RecoveryDiscard`, `RecoveryLater`, `RecoveryMoreFormat` ("... and {0} more"), `RecoveryEntryFormat` in both resx files
- Modify: `src/Raffica.Write/App.axaml.cs` (one coordinator per process), `WriteWindow.axaml.cs` (ask the coordinator after the first sharp page, or after `Opened` when the window has no file; never in a measurement run), the card-footer guard of the Write tests
- Test: `tests/Raffica.Write.App.Tests/Recovery/RecoveryFlowTests.cs`, `RecoveryCardTests.cs`

**Interfaces:**
- Consumes: `RecoveryStore.List()`, `RecoveryLiveness`, `AutosaveScheduler.Adopt`, `DocumentOpener.Open`.
- Produces: `enum RecoveryChoice { Restore, Discard, Later }`; `interface IRecoveryUi { Task<RecoveryChoice> AskAsync(IReadOnlyList<RecoveryEntry> entries); Task<WriteTab?> OpenRecoveredAsync(RecoveryEntry entry); void ShowOpenError(RecoveryEntry entry); }`; `RecoveryFlow(RecoveryStore, IProcessProbe, IRecoveryUi, int selfProcessId).RunAsync(CancellationToken) : Task`; `RecoveryCoordinator.OfferOnce(...)` (a flag per process: a second call does nothing).
- A restored tab: no path, title from the original file name or the next "Untitled N", `SuggestedSavePath = entry.OriginalPath`, made dirty (the model's `Edits.Raise()`), `Adopt`ed by its window's scheduler under the entry's id.

- [ ] **Step 1: Write the failing tests:** `NothingIsReadBeforeTheFirstSharpPage`; `OneCardForSeveralEntries`; `EscKeepsTheCopies` (Later); `RestoreOpensDirtyTabsTitledAsBefore` (original name, or Untitled N when none); `SaveOnARestoredTabIsSaveAsWithTheOldNameSuggested`; `ARestoredCopyIsDeletedOnlyAfterSaveOrClose` (a second simulated crash right after Restore still finds it); `DiscardDeletesAllListedEntries`; `ACorruptCopyIsReportedAndKept` (normal open error wording, not deleted); `LiveInstancesCopiesAreNeverOfferedOrDeleted`; `OfferedOncePerProcess` (two windows); `AMeasurementRunNeverOffers`; `ManyEntriesAreListedBounded` (50 entries: the card lists the first 20 and "and 30 more", Restore opens all 50, the card builds under a time bound); the card is in the card-footer guard (primary Restore, secondary Discard, cancel "Decide later", `IsDefault` on Restore, `IsCancel` on the cancel).
- [ ] **Step 2: Run to verify they fail:** `dotnet test tests/Raffica.Write.App.Tests -c Release --filter-class "*Recovery*"`.
- [ ] **Step 3: Implement** the flow, the coordinator, the card (through `CardFooter.Build`), the tab restore path and the wiring; German wording in the "Sie" form.
- [ ] **Step 4: Run `*Recovery*`, `TranslationTests` and the card-footer guard; all pass.**
- [ ] **Step 5: Commit** `feat(write): offer and restore recovery copies (M4.3c)`.

---

### Task 9: The killed-process acceptance test

**Files:**
- Create: `tests/Raffica.Write.RecoveryHarness/` (exe project, added to `Raffica.slnx`), `tests/Raffica.Write.App.Tests/Recovery/KilledProcessRecoveryTests.cs`
- Modify: `src/Raffica.Write/Raffica.Write.csproj` (`InternalsVisibleTo` for `Raffica.Write.RecoveryHarness`), `tests/Raffica.Write.App.Tests/RepoPaths.cs` (harness location)

**Interfaces:**
- Harness command line: `--folder <dir> --interval-ms <n> --paragraphs <n>`. It builds `BlankDocument.Create(PaperSize.A4)`, opens it with `DocxFile.Open`, types paragraphs "line 1" ... "line n" through the UI-free `TextEditor` (one per 5 ms), runs the real `AutosaveScheduler` over a fake subject with a thread-timer clock and the real `RecoveryStore`, and prints one stdout line per finished copy: `COPY <revision> <paragraphCount>`.

- [ ] **Step 1: Write the failing test** `RecoveryAfterAKilledProcessRestoresTheText`: start the harness, wait for a `COPY` line with the final paragraph count, `Process.Kill`, then `RecoveryStore.List()` returns one entry that `RecoveryLiveness` calls not live, and reading the copy (`DocxFile.Open`) gives the paragraphs "line 1" to "line n" in order, compared paragraph by paragraph. Second test `AKillBetweenTwoCopiesLeavesAWholeCopy`: keep typing, kill after the third `COPY` line without waiting; the listed copy opens and its text is exactly "line 1" to "line k" for some k at least the paragraph count of the last printed `COPY`, never a partial paragraph and never a corrupt file. If the harness binary is not built the test fails with a message naming `dotnet build Raffica.slnx -c Release` (it never skips).
- [ ] **Step 2: Run to verify it fails:** `dotnet build Raffica.slnx -c Release` then `dotnet test tests/Raffica.Write.App.Tests -c Release --filter-class "*KilledProcessRecoveryTests"`. Expected: fails (no harness yet).
- [ ] **Step 3: Implement the harness** (`Program.cs`, no Avalonia types reached at run time) and the test's process handling (read stdout lines, timeouts of 30 s that fail with the captured output).
- [ ] **Step 4: Run again, three times in a row; all pass each time** (a flake here is a defect in the write order, not a reason to retry).
- [ ] **Step 5: Commit** `test(write): recovery after a killed process (M4.3c)`.

---

### Task 10: ImageCache budget, cap, LRU, TryGet, Warm and Prefetch (library)

**Files:**
- Modify: `src/Raffica.Write.Layout/Floats/ImageCache.cs`
- Create: `src/Raffica.Write.Layout/Floats/ImageHeader.cs` (the header read now inside `ImageProbe`, shared)
- Test: `tests/Raffica.Write.Tests/Layout/ImageCacheBudgetTests.cs`; existing picture and `DrawGate`-related tests stay green

**Interfaces:**
- Produces: `public readonly record struct ImageCacheLimits(long ByteBudget, long MaxPixels)` with `static ImageCacheLimits Default` (256 MiB, 32,000,000) and an invariant `MaxPixels * 4 * 2 <= ByteBudget` (an invalid pair throws `ArgumentException`); `ImageCache(Func<string, ReadOnlyMemory<byte>> bytes, ImageCacheLimits? limits = null, bool decodeOnGet = true)`; `SKImage? Get(ImageRef)` (decodes when `decodeOnGet`, else same as `TryGet`); `SKImage? TryGet(ImageRef)` (never decodes); `void Warm(IEnumerable<ImageRef>)` (synchronous decode on the calling thread); `void Prefetch(IEnumerable<ImageRef>)` (pool thread, runs `Warm`, raises `Decoded` once per call that decoded anything); `event Action? Decoded`; `void ReleaseRetired()` (disposes images evicted since the last call); `long DecodedBytes`; `int RefusedCount`.
- Eviction moves an image to a retired list; it is disposed only by `ReleaseRetired`, which the owner calls after a draw returns, so an image handed to a running draw is never disposed under it. A picture whose header says more than `MaxPixels` is not decoded, is cached as unreadable (the renderer draws its marked box, as for a metafile) and counts in `RefusedCount`.

- [ ] **Step 1: Write the failing tests:** `DefaultLimitsAreThePinnedOnes`; `ACapAboveHalfTheBudgetIsRefused`; `LeastRecentlyUsedIsEvictedFirst` (budget for three small images, touch order decides); `EvictionDoesNotDisposeUntilReleaseRetired` (a held `SKImage` is still valid after eviction, invalid after `ReleaseRetired`); `AnImageAboveThePixelCapIsRefusedWithoutDecoding` (`DecodeCount` 0, `RefusedCount` 1, `Get` null); `TryGetNeverDecodes`; `GetWithDecodeOnGetFalseNeverDecodes`; `PrefetchRaisesDecodedOnce` and `PrefetchOfADecodedImageRaisesNothing`; `ConcurrentGetAndPrefetchAreSafe` (many threads, no exception, `DecodedBytes` never above the budget after a quiescent `ReleaseRetired`); `DisposeStopsPrefetchResults`.
- [ ] **Step 2: Run to verify they fail:** `dotnet test tests/Raffica.Write.Tests -c Release --filter-class "*ImageCacheBudgetTests"`.
- [ ] **Step 3: Implement** with one lock, an LRU list and a retired list; the default `decodeOnGet: true` keeps the bench, tests and oracles behaving as today.
- [ ] **Step 4: Run the class and `dotnet test tests/Raffica.Write.Tests -c Release --filter-namespace "*Layout*"`; all pass.**
- [ ] **Step 5: Commit** `feat(write): image cache budget, pixel cap and off-thread prefetch hooks (M4.3c)`.

---

### Task 11: The app never decodes on the render thread

**Files:**
- Modify: `src/Raffica.Write/Documents/DocumentOpener.cs` and `OpenedDocument` (the cache is made with the open and carried by it), `WriteTab.cs` (owns the cache, disposes it after the draw gate closes), `Viewing/PageView.cs` (uses `tab`'s cache with `decodeOnGet: false`, prefetch of the visible pages and one page each side when the range changes, invalidate on `Decoded`), `Viewing/PageDrawOperation.cs` (`ReleaseRetired()` after each draw, inside the gate)
- Test: `tests/Raffica.Write.App.Tests/Viewing/PageViewPicturesTests.cs`, `Documents/DocumentOpenerTests.cs`

**Interfaces:**
- Consumes: Task 10's `ImageCache`.
- Produces: `OpenedDocument.Pictures : ImageCache`; `WriteTab.Pictures`.

- [ ] **Step 1: Write the failing tests:** `PageOneHasItsPicturesDecodedBeforeTheFirstSharpPage` (`DecodeCount` equals the page-1 picture count when `FirstSharpPageDrawn` fires, decoded on a pool thread); `ADrawNeverDecodes` (headless render of a picture page with the cache at `decodeOnGet: false`: `DecodeCount` unchanged by the draw, the marked box is drawn); `ScrollingPrefetchesThePagesInView` (after the visible range moves, the pictures of those pages and one page each side are decoded off the UI thread and the view invalidates once on `Decoded`); `ACacheDisposeWaitsForTheDrawGate` (the existing rule, still true); `PictureHeavyFilesStayUnderTheBudget` (a generated document with pictures above the budget keeps `DecodedBytes` at most the budget after `ReleaseRetired`); the oracle pixel tests of the library are unchanged and green.
- [ ] **Step 2: Run to verify they fail:** `dotnet test tests/Raffica.Write.App.Tests -c Release --filter-class "*PageViewPicturesTests"`.
- [ ] **Step 3: Implement.** The prefetch runs from the same place that already reacts to a changed visible range; nothing is prefetched before the first sharp page except page 1's pictures in the opener; the window disposes the cache in the order of the 4.2 rule (gate first).
- [ ] **Step 4: Run `*PageView*`, `*DocumentOpenerTests*`, `*OpenCancellationTests*`, `StartupTests`; all pass.**
- [ ] **Step 5: Commit** `feat(write): pictures are decoded off the render thread (M4.3c)`.

---

### Task 12: Measuring typing during a snapshot

**Files:**
- Modify: `src/Raffica.Write/WriteOptions.cs`, `Measurement.cs`, `Measurement.Typing.cs` (switch `--measure-typing-snapshot`), `bench/write-layout/Edit.cs`, `EditCases.cs`, `Program.cs` (`edit --snapshot`), `bench/shell-probe/measure.cs` (case `typing-snapshot`, rows `typing-snapshot/<case>-<view>-median|p95` count into the judged `typing` metric, rows `snapshot/lease-ms` and `snapshot/total-ms` recorded and never judged), the `measure.cs` selftest
- Test: `tests/Raffica.Write.App.Tests/MeasurementTests.cs`, `dotnet run bench/shell-probe/measure.cs -- selftest`

**Interfaces:**
- The run is the `typing` run with a `RecoverySnapshot` loop writing back to back into a temp recovery folder (`--settings-file` temp). The bench `edit --snapshot` does the same through the UI-free editor and reports the lease-held and total time of a snapshot of the thesis.

- [ ] **Step 1: Write the failing tests:** `OptionsAcceptTheNewSwitch`; `TheSnapshotRunWritesTheSameRowsAsTyping` (headless, tiny document); `TypingSnapshotRowsCountIntoTheTypingMetric` and `LeaseRowsAreNeverJudged` (selftest cases in `measure.cs`).
- [ ] **Step 2: Run to verify they fail:** `dotnet test tests/Raffica.Write.App.Tests -c Release --filter-class "*MeasurementTests"` and `dotnet run bench/shell-probe/measure.cs -- selftest`.
- [ ] **Step 3: Implement** the switch, the loop, the bench option and the case registration; keep `EditCases` and `TypingPlaces` changed together.
- [ ] **Step 4: Run the two commands, then measure on a Native AOT win-x64 publish** (publish Write and the bench with `Microsoft Visual Studio\Installer` on `PATH`, search the log for `IL[23][0-9]{3}`): `RafficaWrite --measure-typing-snapshot` three launches on the thesis, `measure.cs check --target write` over the rows (worst p95 at most 16.0 ms), `write-layout-bench edit --snapshot --file testdata/write-corpus/thesis/thesis.docx`, then `--measure-typing`, `--measure-format` and `--measure-startup` (cold and warm) against the previous numbers in `Docs/DECISIONS.md`. If `typing-snapshot` exceeds 16.0 ms, stop and bound the model read further (changed parts only, or slices) before going on; do not loosen the limit.
- [ ] **Step 5: Commit** `feat(write): measure typing during a recovery snapshot (M4.3c)`.

---

### Task 13: Documents, roadmap and verification

**Files:**
- Modify: `Docs/roadmap.json` (and regenerate `Docs/ROADMAP.md`), `Docs/DECISIONS.md` (entry "Raffica Write: new documents and recovery (M4.3c)": the style set's look and why, the three numbering definitions, the calls of the spec's "Calls settled after the builder check", measured numbers, the chosen budget and cap, known limits), `CLAUDE.md` (section "Write recovery (M4.3c)" in the style of the earlier ones), `CHANGELOG.md` (`[Unreleased]`), `Docs/write-word-check.md` (section "New document and recovery (milestone 4.3c)" with an empty result table), `packaging/README.md` only if a check there changes
- Test: `dotnet run tools/roadmap.cs -- check`

- [ ] **Step 1: Roadmap.** Tick the three tasks of 4.3.3; tick the acceptance criterion with its proof (`KilledProcessRecoveryTests`, run count and commit); set the status `in-progress` while owner gates stay open or `done` with `completedOn` only when nothing is open; list the open owner gates (Word opens a blank document and a restored copy; kill-and-restart on Windows, the Linux laptop and the Intel Mac; the autosave box on a real display; `typing` and start on the Linux laptop and the Intel Mac) in a `blocks` entry. `dotnet run tools/roadmap.cs -- generate`, then `-- check` exits 0.
- [ ] **Step 2: Full verification, each result named in the PR:** `dotnet build Raffica.slnx -c Release` (0 warnings); `RAFFICA_REQUIRE_OCR=1 dotnet test Raffica.slnx -c Release --no-build --max-parallel-test-modules 1` (after `native-fetch` as in `CLAUDE.md`; no failure, skips explained); `dotnet format Raffica.slnx --verify-no-changes` (exit 0); every touched `.resx`, `.cs`, `.md` has LF endings; `dotnet run tools/roadmap.cs -- check` (exit 0).
- [ ] **Step 3: Commit** `docs: Write 4.3c decision, roadmap and notes`; then the PR to `dev` per `CLAUDE.md` (merged by Claude once CI is green).

---

## Self-review

- **Spec coverage:** blank document and style set, region paper, fonts (Task 1); untitled tabs, naming, Save as, close, no recent entry (Task 2); New document command, key, icon, German, menu, start cost (Task 3); recovery store, folder, sidecar without text, owner process (Task 4); snapshot through the existing writer, lease, no `MarkSaved`, silent failure (Task 5); 30 s timer from the first edit, deactivation, skip rules, one at a time, delete on clean and on explicit release (Task 6); the autosave setting and its label (Task 7); one offer per process, Restore as dirty unbound tabs, Discard, Later, live-process rule, bounded list, corrupt copy kept (Task 8); the killed-process acceptance test with an in-between kill (Task 9); the cache budget, cap, LRU, `TryGet`, prefetch, deferred disposal (Task 10); page 1 decode before the first sharp page, prefetch, no render-thread decode (Task 11); `typing-snapshot`, lease time, start and format re-runs (Task 12); roadmap, decision, `CLAUDE.md`, changelog, Word check rows, scope change for the crash guard (Task 13 and the roadmap entry made with this plan).
- **Types:** `IRecoverySubject`, `RecoveryStore`, `RecoveryEntry`, `RecoverySnapshot`, `SnapshotSkip`, `AutosaveScheduler`, `IRecoveryClock`, `ModelReadLease`, `ImageCacheLimits` and `ImageCache` members are named the same wherever they appear (Tasks 4, 5, 6, 8, 9, 10, 11, 12).
- **Gaps closed:** the harness uses the same scheduler and store as the app (Task 9), so the acceptance test covers the real recovery code; the window glue is covered by `AutosaveSchedulerWindowTests` and the owner gates.
