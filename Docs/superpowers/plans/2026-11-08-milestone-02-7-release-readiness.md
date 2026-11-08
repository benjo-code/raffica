# Milestone 2.7 – Release readiness: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Raffica PDF and its repository are ready to be made public and released as `v0.1.0`: the self-hosted runners cannot be reached by fork pull requests, redaction removes what it reports as removed, no visible control is dead or unreachable at the default window size, the German UI reads as one product, a crash never ends the app without a word, and the start-time regression found on 2026-11-08 is explained and fixed.

**Architecture:** No new subsystem. The work fixes the findings of the release-readiness review of 2026-11-08 (eight independent reviewers: updater and release chain, public repository, hostile documents, Core bugs, app bugs in two halves, UI from source, UI live on Windows) and the Linux measurement of that day. Each task owns one area and ends in its own tests. Shell changes are additive (the gallery keeps working); app changes follow the conventions of `CLAUDE.md` ("PDF app" and the milestone sections).

**Tech Stack:** .NET 10 Native AOT, Avalonia 12.1.5, xunit.v3 with `Avalonia.Headless.XUnit`, PDFium (P/Invoke), Lucide icons (already licensed, `third-party/lucide`), GitHub Actions. No new NuGet package.

**Spec:** [Docs/superpowers/specs/2026-11-04-raffica-pdf-design.md](../specs/2026-11-04-raffica-pdf-design.md) (goals, section 3 no telemetry, section 6 unsaved changes, section 9 speed) and [Docs/superpowers/specs/2026-11-03-design-system-shell-design.md](../specs/2026-11-03-design-system-shell-design.md) (frame, tokens, contrast, localization). Product rules: `PRODUCT.md` ("frequent commands are visible and one click away", every lock shows its reason). The findings with their file and member references are quoted in each task; the review notes themselves were working files and are not part of the repository.

## Global Constraints

- **Layering:** `Raffica.Pdf` references `Raffica.Core` and `Raffica.Shell` only (`AppProjectTests.TheAppReferencesOnlyCoreAndShell`). Platform code stays in `src/Raffica.Pdf/Platform/<OS>`.
- **Shell API is additive:** new members and options only; the gallery app and `tests/Raffica.Shell.Tests` keep passing unchanged except where a test pins a value this plan changes on purpose (name it in the commit).
- **Strings:** every user-visible string lives in `Strings.en.resx` and `Strings.de.resx` of its project (`TranslationTests` in both test projects). German uses "Sie" throughout. English uses US spelling ("color", "canceling").
- **Tokens:** component styles use semantic tokens only (`NoHexInComponentStylesTests`); a new or changed token is classified in `ContrastPolicy`, pinned in `TokenValuesTests` and present in Light, Dark and HighContrast and in the gallery's `FoundationsSection`.
- **Wording of safety:** no "safe", "guaranteed", "valid" or "legally binding" in redaction, update or signing texts (`NoUiTextOverpromisesRemoval`, `NoUiTextOverpromisesUpdates`).
- **Speed:** nothing new runs before the first sharp page (`StartupCreatesNoPackagingWorkTests`, `StartupCreatesNoRedactOcrOrEditWork` and their siblings stay green); the budgets in `s_pdfBudgets` (`bench/shell-probe/measure.cs`) are never loosened.
- **GitHub:** this milestone changes no repository or organisation setting and makes nothing public. Settings are listed for the owner in `packaging/README.md`; the owner (or Claude on an explicit instruction) applies them.
- **Git:** all work on `feature/m27-release-readiness`; a PR to `dev` after each group, merged when CI is green: PR 1 = plan, roadmap and Task 1 (so the owner can prepare the flip early), PR 2 = Tasks 2 to 7, PR 3 = Tasks 8 to 14, PR 4 = Tasks 15 and 16; the branch is re-created from `dev` after each merge. Conventional Commits; no attribution lines; selective `git add`; `CHANGELOG.md` `[Unreleased]` gets a line for every user-visible change.
- **Done means:** `dotnet build Raffica.slnx -c Release`; `dotnet test Raffica.slnx -c Release --no-build --max-parallel-test-modules 1` with `RAFFICA_REQUIRE_OCR=1`; `dotnet format Raffica.slnx --verify-no-changes`; `dotnet run tools/roadmap.cs -- check`; for workflow changes the run named in the task.

## Calls made while planning

1. **CI after the flip.** While the repository is private, CI keeps using the self-hosted runners (minutes cost money). Once it is public, every CI job runs on GitHub-hosted runners (free for public repositories). The routing reads `github.event.repository.private`, so no edit is needed at the flip. This routing is a convenience, not the security boundary: the boundary is the runner-group setting (group `raffica-ci` restricted to `release.yml@refs/heads/main` and `@refs/heads/dev`, which the owner sets before the flip) because a fork controls the workflow file of its own pull request.
2. **Extract and Split keep the document level.** Parts stay working PDFs (bookmarks, form, document information stay, as today). Rebuilding parts in an empty document would break their forms. Instead the Extract and Split cards say concretely what stays when the document has it (attached files, scripts, form values of other pages, bookmarks of other pages), and point to Redact for sending sensitive content. A cleaned part is a backlog item.
3. **Crash handling.** An unexpected exception on the UI thread is logged, marked handled and answered with one card ("Something went wrong; save your work under a new name and restart"); if more than three arrive within ten seconds the app logs and ends (no loop). Exceptions of other threads are logged; the process may end as before. The log holds type, message and stack only, never a document path or text.
4. **Start view of the PDF app.** It shows one tile, "Open PDF", and the recent files. The suite tiles return when Write, Sheets and Slides exist. The "+" of the tab strip opens the file picker.
5. **Ribbon overflow.** Below the width the full ribbon needs, the band switches to the compact layout by itself (the backlog item "Automatic compact command bar below a window width" is adopted); if even the compact band does not fit, it scrolls with chevron buttons at both ends. The manual compact setting stays and forces compact at any width.
6. **Icons.** Every command gets a Lucide glyph of its own meaning; one glyph may serve several commands only when they are the same action in different places (for example Undo in two tabs). A guard test pins this.
7. **Default-app card.** "Not now" is remembered for 30 days (today it returns on every start). This changes the 2.6 behaviour and is a scope change.
8. **Button order.** All card footers: primary action first, then secondary actions, Cancel last, right-aligned (the Windows convention), with `IsDefault` on the primary and `IsCancel` on Cancel.
9. **Out of scope, to the backlog:** page text for screen readers (a text pattern on the page view), system high-contrast colours, a lower `MinWidth`, cleaned Extract and Split parts, a Narrator/NVDA pass, a simplified 16 px icon.

## Review Focus

1. **A fork pull request after the flip** that edits `ci.yml` to `runs-on: [self-hosted, dotnet-linux]`: the job must never start on the owner's machines. Pinned by the runner-group setting in the owner checklist and by `release.yml` refusing any `ref` that is not on `main` or `dev` (Task 1).
2. **A PDF/A-3 invoice with an embedded XML attachment on the page (`/AF`)** redacted with "Save as": the attachment is gone or the page is refused, and the report never lists something as cleaned that was not (Task 2).
3. **German UI at the default window size (1280 x 860) and at 1366 x 768 with 125 % scaling:** no command group, status cell, dialog row or pane label is cut or unreachable (Tasks 9, 10, 11).
4. **Two tabs and a write onto the other tab's file** (Save as, Apply, Redact Apply, Extract): refused with a note, never two tabs with one path (Task 4).
5. **An exception from a ribbon command handler** (simulated in a test): the app stays open, the other tabs keep their unsaved changes, a card appears once and `crash.log` holds the stack without a path (Task 6).

---

### Task 1: Repository and CI ready to be public

**Files:**
- Modify: `.github/workflows/ci.yml`, `.github/workflows/roadmap.yml` (routing), `.github/workflows/release.yml` (ref guard, fresh package and SDK folders, digest-pinned image), `packaging/linux/container-build.sh`, `packaging/native/container-build.sh` (image digest if they name the image), `.gitignore`, `README.md`, `CLAUDE.md`, `tools/native-fetch.cs` (header comment), `bench/shell-probe/RUNNERS.md`, `Docs/DECISIONS.md`, `packaging/README.md`, `CHANGELOG.md`, `.github/pull_request_template.md`
- Create: `SECURITY.md`, `CONTRIBUTING.md`, `.github/ISSUE_TEMPLATE/config.yml`

**Interfaces:** none (no code consumed by other tasks).

- [ ] **Step 1: Routing.** In `ci.yml` (jobs `changes`, `build`, `ci`) and `roadmap.yml`, route to the hosted image when the repository is public or the pull request comes from a fork: the condition becomes `github.event.repository.private == false || (github.event_name == 'pull_request' && github.event.pull_request.head.repo.full_name != github.repository)`. Keep the hosted-runner preparation steps working for every hosted run (they are written for the fork case today). Update the header comments: the boundary is the runner group, not this expression.
- [ ] **Step 2: `release.yml` ref guard (finding 01-S3).** For a non-empty `ref`, resolve it to a commit id and require that it is reachable from `main` or `dev` (`gh api repos/$GITHUB_REPOSITORY/compare/<branch>...<sha>` with status `identical` or `behind` for one of the two), the same way the tag check works; a branch name must be `main` or `dev`. Applies to the jobs `preflight`, `windows`, `linux`.
- [ ] **Step 3: Fresh build state for releases (01-S1, fix 4).** In the Windows and Linux release jobs set `NUGET_PACKAGES` to a folder under `$RUNNER_TEMP` and let `actions/setup-dotnet` install into a folder under `$RUNNER_TEMP` (not the shared `~/.dotnet`). Pin `ubuntu:22.04` by digest (`docker buildx imagetools inspect ubuntu:22.04` gives the current one) in `release.yml` and the container scripts, and record the digest and how to update it in `Docs/DECISIONS.md`.
- [ ] **Step 4: Dry run.** Push the branch, dispatch `gh workflow run release.yml --ref dev -f tag=v0.1.0 -f ref=<40-hex sha of the branch head> -f upload=false` and expect the preflight to refuse it (not on dev yet); after the merge dispatch it again with the dev head and expect green. Also dispatch once with a SHA that is on no branch and expect the refusal. Record both run ids in the PR.
- [ ] **Step 5: Public-facing files.** `SECURITY.md`: report through GitHub's "Report a vulnerability" (private vulnerability reporting), supported version = the latest release, in scope = update signature and download checks, PDF parsing, redaction completeness, the installers. `CONTRIBUTING.md`: build and test commands from `CLAUDE.md`, GPL-3.0-or-later for contributions, branch from `dev`, Conventional Commits. `.github/ISSUE_TEMPLATE/config.yml` with `blank_issues_enabled: false` and a link to the security policy; keep `idee.md` but word it for any contributor; trim `pull_request_template.md` to what an outside contributor can answer. `.gitignore`: `*.pem`, `*.key`, `*.pfx`, `*.p12`, `*.snk`.
- [ ] **Step 6: Docs that say "private".** `README.md` ("Build from source": no token needed once public, the unauthenticated rate limit is 60 API calls per hour), the matching sentence in `CLAUDE.md` (M2.5) and the header of `tools/native-fetch.cs`, `bench/shell-probe/RUNNERS.md` (the approval setting only gates; the group restriction is the boundary), `Docs/DECISIONS.md` (new entry "CI on hosted runners once public (M2.7)" superseding the rejected alternative of "CI on self-hosted runners, as few runs as possible"). Fix `CLAUDE.md` "The limit table `s_pdfBudgets` … is empty": it holds the M2.1 budgets with the M2.5 size amendment. Remove "The icon is a placeholder until the real mark exists" from `CHANGELOG.md`.
- [ ] **Step 7: Owner checklist.** In `packaging/README.md`, section "Making the repository public", list in order: runner group `raffica-ci` restricted to `benjo-code/raffica/.github/workflows/release.yml@refs/heads/main` and `@refs/heads/dev`; group `Default` stays private-only; fork PR approval "Require approval for all external contributors"; wiki and projects off; after the flip: private vulnerability reporting, Dependabot alerts and security updates, secret scanning and push protection on; tag ruleset `v*` (no update or delete, creation by admins); Actions policy "require full-length commit SHA"; required reviewer on `release-signing`; then a test PR from a fork-like branch to confirm CI runs hosted.
- [ ] **Step 8: Verify and commit.** `dotnet run tools/roadmap.cs -- check`; `actionlint` if available, otherwise a push and the CI run of the PR. Commit `ci: prepare the repository and workflows for public visibility`.

---

### Task 2: Redaction removes page attachments and article threads, and reports only what it removed

**Files:**
- Modify: `src/Raffica.Core/Redaction/PageKeyScanner.cs` (`Keys`), `src/Raffica.Core/Redaction/RedactionExecutor.cs` (`ReadInventoryAsync`, the `CleanupKind.Attachments` count), `src/Raffica.Core/Redaction/RawScanParts.cs` (`NameTally`), `src/Raffica.Core/Redaction/CleanupReadBack.cs`, `src/Raffica.Pdf/Redacting/RedactReportLines.cs` and the wording tables if a new kind appears
- Test: `tests/Raffica.Core.Tests/Redaction/*` (new fixtures generated in the test, as the existing redaction tests do), `tests/Raffica.Pdf.Tests` report-line tests

**Interfaces:**
- Produces: nothing other tasks consume.

The finding (03-S03-01): the cleaning rebuild copies pages with `FPDF_ImportPagesByIndex`, which keeps every page key except `/Type` and `/Parent`. A page `/AF` (associated files with `/EF` embedded file streams) and `/B` (article beads leading to the thread dictionary with `/I` Title and Author) survive. The inventory counts every `/Type /EmbeddedFile` minus one per file-attachment annotation as "Attached files" removed, and the report lists them under "Cleaned (read back from the written file)" although the read-back checks only catalog keys, the Info dictionary, outlines and page labels.

- [ ] **Step 1: Failing tests.** (a) A one-page PDF with page `/AF [<< /Type /Filespec /F (a.txt) /EF << /F <stream "ATTACHEDSECRETDATA"> >> >>]` and `/B [bead → thread with /I << /Title (THREADTITLESECRET) /Author (THREADAUTHORSECRET) >>]`, one text mark elsewhere on the page: after Apply, the written bytes contain none of the three secrets and no `/EmbeddedFile`, or the page is refused with the page-keys cause (whichever path Step 2 takes, the test asserts the secrets are absent from the output). (b) The report of a document whose only embedded file hangs off a page `/AF` never lists "Attached files" under the cleaned lines unless the read-back confirmed none is left. (c) An annotation-borne embedded file on an unmarked page (`/AF` on an annotation, a `/FileAttachment` paperclip) is listed under "Not cleaned".
- [ ] **Step 2: Implement.** Add `/AF` and `/B` to `PageKeyScanner.Keys` so such pages take the same path as `/Thumb` and `/PieceInfo` today (key removed in the rebuild, or the page rasterized/refused where the existing rule says so). Count as "Attached files" only the files of the names tree. Extend `CleanupReadBack` to scan the written bytes for `/Type /EmbeddedFile` and `/Filespec` with `/EF` beyond the paperclip annotations; anything found becomes a finding that keeps the item out of the cleaned list (and a refusal if the existing rules make it one). List annotation-borne attachments and media under "Not cleaned".
- [ ] **Step 3: Run** `dotnet test tests/Raffica.Core.Tests -c Release --filter-namespace "*Redaction*"` and the Pdf report tests; then the full suite.
- [ ] **Step 4: Commit** `fix(redact): remove page attachments and article threads and report only what was read back`.

---

### Task 3: Extract and Split say what stays in every part

**Files:**
- Modify: `src/Raffica.Core/Pages/PageEditExecutor.Analyze.cs` (or the analysis the split and extract flows call), `src/Raffica.Pdf/Organizing/SplitFlow.cs`, `src/Raffica.Pdf/Dialogs/ExtractCard.cs`, `src/Raffica.Pdf/Dialogs/SplitCard.cs`, `src/Raffica.Pdf/Resources/Strings.*.resx`, `Docs/roadmap.json` (backlog item)
- Test: `tests/Raffica.Core.Tests/Pages/*`, `tests/Raffica.Pdf.Tests/Organizing/*` or `Dialogs/*`

**Interfaces:**
- Produces: `record DocumentLevelContent(int AttachedFiles, bool HasScripts, int FieldsOnOtherPages, int BookmarksToOtherPages)` (Core, next to the split analysis), read once per card from the open document.

The finding (03-S03-02): a part is the working copy minus the unwanted pages, so the Info dictionary, XMP, the whole outline, the names tree (attached files, document JavaScript) and the AcroForm with the values of fields on dropped pages stay. The cards say only "Pages {0} of the document are written into a new file."

- [ ] **Step 1: Failing tests.** For a two-page PDF with an attached file in the names tree, document JavaScript, a filled text field on page 2 and a bookmark to page 2, extracting page 1 gives `DocumentLevelContent(1, true, 1, 1)`; the Extract card then shows one line per non-zero item in both languages, plus the sentence that points to Redact; a document without these shows no such lines.
- [ ] **Step 2: Implement** the count with the existing raw scanners (`RawContentScanner` / `NameTally` and the form and outline readers); wording in both languages without "removed" or "safe"; the split card names the same items for "every part".
- [ ] **Step 3: Backlog.** Add the item "Extract and Split write cleaned parts" (import the chosen pages into a new document and carry over only a filtered outline and the fields of the kept pages) to `Docs/roadmap.json` `backlog`.
- [ ] **Step 4: Run** the filtered tests, then the suite. **Commit** `fix(pages): Extract and Split name the document-level content every part keeps`.

---

### Task 4: No write onto a file that is open in another tab

**Files:**
- Create: `src/Raffica.Pdf/Documents/OpenTargetGuard.cs`
- Modify: `src/Raffica.Pdf/Documents/SaveController.cs` (`SaveCoreAsync`, `WriteAsync`, `RunAsync`), `src/Raffica.Pdf/Organizing/ApplyFlow.cs`, `src/Raffica.Pdf/Redacting/RedactApplyFlow.cs`, `src/Raffica.Pdf/Organizing/SplitFlow.cs` (`ExtractCoreAsync`), strings
- Test: `tests/Raffica.Pdf.Tests/Documents/OpenTargetGuardTests.cs` and one test per flow

**Interfaces:**
- Produces: `static class OpenTargetGuard { static Task<PdfTab?> FindOtherTabAsync(Workspace workspace, PdfTab self, string target, CancellationToken token); }`: the other open tab whose path is `target` or an alias of it (`JobRunner.SamePath`, then `FileIdentity.AreSameFile` off the UI thread; a null answer from `AreSameFile` counts as the same file, as for the original).

The findings (06-B-1, 05-A-02, 05-A-07): after the picker none of Save as, Apply, Redact Apply or Extract checks the other tabs; PDFium opens files with share-delete, so the replace succeeds and two tabs end with one path; a stale tab can later write the unredacted content back. Also, Save in the close question returns false silently while a save runs.

- [ ] **Step 1: Failing tests.** With `a.pdf` and `b.pdf` open: Save as from A onto `b.pdf` shows the note "b.pdf is open in another tab. Close it first." / "b.pdf ist in einem anderen Tab geöffnet. Schließen Sie ihn zuerst." and writes nothing; the same for Apply with Save as, Redact Apply with Save as, and Extract onto `b.pdf`. Saving a tab while a save of the same tab runs shows `SaveBusy` instead of returning silently.
- [ ] **Step 2: Implement** the guard and call it after each picker and after the plan-version re-check, before any write. In `RunAsync` tell `SaveBusy` when `IsSaving`.
- [ ] **Step 3: Run** `dotnet test tests/Raffica.Pdf.Tests -c Release --filter-class "*OpenTargetGuardTests"` and the flow tests, then the suite. **Commit** `fix(pdf): refuse writing onto a file that another tab has open`.

---

### Task 5: Core fixes: save check, dates, link targets, links and leftovers, CLI console

**Files:**
- Modify: `src/Raffica.Core/Annotations/AnnotationEditor.cs` (`SaveAsync` counts), `src/Raffica.Core/Saving/DocumentSaver.cs` (`CheckAsync`, `Replace`), `src/Raffica.Core/Pdf/PdfDocument.Annotations.cs` (an uncapped count), `src/Raffica.Core/Pdf/PdfDate.cs` (`TryParseOffsetDigits`), `src/Raffica.Pdfium/PdfiumPageText.cs` (`ReadTargetTop`), `src/Raffica.Pdfium/PdfiumTextEdit.cs` (`CheckPixels`, `Render`), `src/Raffica.Cli/Program.cs`, `src/Raffica.Cli/CliApp.cs`
- Test: the matching classes in `tests/Raffica.Core.Tests` and `tests/Raffica.Render.Tests` if the link test needs a render

**Interfaces:**
- Produces: `Task<int> PdfDocument.CountAnnotationsForCheckAsync(int page, CancellationToken token)`: the count with the filter of `PdfiumAnnotations.Read`, without touching the document-wide listing cap. `SaveResult` (or the existing result type) gains `string? LeftoverPath` for a `.pw-*.old` file that could not be deleted.

- [ ] **Step 1: Save check (04-B1).** Failing test: a document with more than 10,000 annotation entries (generated: many link annotations over several pages), all pages listed through `GetAnnotationsAsync` first, then a change on the last page and Save: today `VerificationFailed`, after the fix it saves. Implement the uncapped count and use it on both sides of the check.
- [ ] **Step 2: Dates (04-B4).** Failing test: `PdfDate.TryParse("D:20240101120000+01'")` gives 2024-01-01 12:00 +01:00. Allow the end of text after the apostrophe.
- [ ] **Step 3: Link targets (04-B3).** Failing test: an `/XYZ` destination on a page with CropBox `[0 36 612 828]` and on a page with `/Rotate 90` lands on the target point (compare with `PageTransform.TryToPage`). Convert with the target page's transform (load the page briefly on the PDFium thread).
- [ ] **Step 4: File-level links (04-B5).** Failing test (Linux and Windows where symlinks can be made; skip otherwise): saving through a file symlink writes the target and keeps the link. Resolve with `FileInfo.ResolveLinkTarget(returnFinalTarget: true)` before the save and write the temporary file next to the final target. Hard links: document in `Docs/DECISIONS.md` that a save gives that name a new file.
- [ ] **Step 5: Leftover after the fallback replace (03-S03-03).** Retry the delete of the `.pw-<hex>.old` file three times over 300 ms; if it still exists, return its path in the result. The app shows it as a warning card naming the path (for a redaction: "The unredacted original is still at …; delete it."). Test with an injected file operation that fails the delete.
- [ ] **Step 6: Text-edit check render (03-S03-04).** Compute `width * height * 4` and the stride in `long`, cap each side at the `PdfiumRenderWork.MaxPixels` rule, and refuse the check (the edit is not possible) beyond the cap. Test with a page of `MediaBox [0 0 1073741888 0.005]`.
- [ ] **Step 7: CLI console (04-B2).** On Windows, when stdout is not redirected, write through `Console.Out` after `Console.OutputEncoding = new UTF8Encoding(false)`; redirected output stays the raw UTF-8 stream. Test the selection logic with an injected "is redirected" flag.
- [ ] **Step 8: Run** the Core and Render suites, then all. **Commit** one commit per finding (`fix(core): …`).

---

### Task 6: A crash never ends the app without a word

**Files:**
- Create: `src/Raffica.Pdf/Hosting/CrashGuard.cs`
- Modify: `src/Raffica.Pdf/App.axaml.cs` (`OnFrameworkInitializationCompleted`), `src/Raffica.Pdf/Program.cs`, strings
- Test: `tests/Raffica.Pdf.Tests/Hosting/CrashGuardTests.cs`

**Interfaces:**
- Produces: `sealed class CrashGuard { CrashGuard(string logDirectory, TimeProvider clock, Action<Exception> showCard, Action exit); void OnUiException(Exception ex, out bool handled); void OnBackgroundException(Exception ex); }`. The log file is `crash.log` in `PdfSettingsFiles.ConfigDirectoryFor(settingsFile)`, appended, capped at 1 MiB (older half dropped).

- [ ] **Step 1: Failing tests.** (a) A UI exception is written to `crash.log` with time, type, message and stack, and the text contains no path given in the message of a wrapping `IOException` (strip absolute paths from messages with a regex for drive letters, `/home/`, `/Users/`, UNC); `handled` is true and `showCard` ran once. (b) Four UI exceptions within ten seconds: the fourth calls `exit` after logging. (c) A background exception is logged and does not show a card. (d) In a headless window, a ribbon command that throws leaves the window open and the other tab dirty.
- [ ] **Step 2: Implement** and subscribe `Dispatcher.UIThread.UnhandledException`, `AppDomain.CurrentDomain.UnhandledException` and `TaskScheduler.UnobservedTaskException` (`SetObserved` after logging). The card goes through `DialogQueue`: "Something went wrong" / "Etwas ist schiefgelaufen", text "Save your work under a new name and restart Raffica PDF. Details are in crash.log." with a button that opens the folder. `OutOfMemoryException` is logged and not handled.
- [ ] **Step 3: Run** the class, then the suite. **Commit** `feat(pdf): log unexpected errors and keep the window open`.

---

### Task 7: Window, single instance and hand-over behaviour

**Files:**
- Modify: `src/Raffica.Shell/Frame/ShellSettings.cs` (window placement), `src/Raffica.Pdf/PdfWindow.axaml`, `src/Raffica.Pdf/PdfWindow.axaml.cs` (`OnOpened`, `OnFirstSharpPageAsync`, `ShowHandover`, `AskAsync`, `OpenRecentAsync`, `OnDrop`/`OpenDroppedAsync`, `ShowActiveView`), `src/Raffica.Pdf/Program.cs` (`RestoreSession`, `HandOverTimeout`), `src/Raffica.Pdf/Viewing/PageView.cs` (current page at the end)
- Test: `tests/Raffica.Shell.Tests/Frame/*`, `tests/Raffica.Pdf.Tests/Hosting/*`, `tests/Raffica.Pdf.Tests/Documents/*`, `tests/Raffica.Pdf.Tests/Viewing/*`

**Interfaces:**
- Produces: `ShellSettings.WindowPlacement` (`record WindowPlacement(double X, double Y, double Width, double Height, bool Maximized)`, null when never stored) and the static helper `WindowPlacement.FitTo(PixelRect workArea, double scaling, WindowPlacement? stored, Size defaultSize)`.

- [ ] **Step 1: Placement (08-UI-08).** Tests for `FitTo`: no stored placement on a 1920 x 1032 work area gives 1280 x 860 centred; on 1366 x 728 it shrinks to the work area minus a 24 px margin; a stored placement outside every screen is moved inside. The window applies it before it is shown and stores it on close (maximized flag plus the restore bounds).
- [ ] **Step 2: Single instance (05-A-05).** Start listening right after the window has opened (accepting is cheap; showing the hand-over waits for the UI as now) and keep the 1 s hand-over timeout. Test: a hand-over sent before the first sharp page is accepted.
- [ ] **Step 3: Hand-over and close while a card is open (05-A-08, 05-A-04).** While `Host.IsOpen` or the close guard runs, accepted hand-over paths are queued and opened when the host is free; `ShowActiveView` does not move focus while a dialog is open. The close question is queued behind an open card instead of answering Cancel, and the window is activated. Tests for both.
- [ ] **Step 4: Recent entry on a dead share (05-A-06).** Decide removal from the reported `PdfOpenError` (`FileNotFound`) instead of `File.Exists` on the UI thread. Test with a fake opener.
- [ ] **Step 5: Many bad files (05-A-03).** A drop or open of several files collects the failures into one summary card (up to five names, then "and N more"). Test with 20 non-PDF paths: one card.
- [ ] **Step 6: Active tab after an update (06-B-4).** `RestoreSession` selects the tab at `ActiveIndex`. Test.
- [ ] **Step 7: End key (08-UI-15).** When the view is scrolled to its end, the last page is current. Test on a document of 1,000 short landscape pages: End gives "Page 1000 of 1000".
- [ ] **Step 8: Run** the suites. **Commit** one commit per step group (`fix(pdf): …`, `feat(shell): remember the window placement`).

---

### Task 8: Start view and tab strip

**Files:**
- Modify: `src/Raffica.Shell/Frame/StartTile.cs` (a `PdfOnly` set), `src/Raffica.Shell/Frame/DocumentTabs.cs` (configurable "+" label), `src/Raffica.Shell/Frame/SearchField.cs` (placeholder), `src/Raffica.Pdf/PdfWindow.axaml.cs`, strings of Shell and Pdf
- Test: `tests/Raffica.Pdf.Tests/StartViewTests.cs`, `tests/Raffica.Shell.Tests/Frame/StartViewTests.cs`

**Interfaces:**
- Produces: `StartTile.OpenPdfOnly()`; `DocumentTabs.NewButtonLabel` (string, default the existing "New document").

- [ ] **Step 1: Failing tests.** The PDF app's start view has exactly one tile, "Open PDF" / "PDF öffnen", and invoking it opens the picker (fake picker in the test); the "+" of the tab strip in the PDF app is named "Open file" / "Datei öffnen" and opens the picker; the gallery still shows the four suite tiles.
- [ ] **Step 2: Implement** and wire `NewRequested` and `Tabs.NewRequested` to `OpenFromDialogAsync`.
- [ ] **Step 3: Search field (07-UI-15).** The title-bar placeholder becomes "Search commands" / "Befehle suchen" (shell string, the gallery follows), and the palette offers "Search the document for '…'" / "Im Dokument nach „…“ suchen" as the last result when a document is open, which opens the find bar with the text.
- [ ] **Step 4: Run** and **commit** `fix(pdf): working start tile and open button; name the command search`.

---

### Task 9: One icon per meaning

**Files:**
- Modify: `src/Raffica.Shell/Styles/Icons.axaml` (new Lucide geometries), `src/Raffica.Pdf/Commands/PdfCommandCatalog.cs` (icon column), `src/Raffica.Pdf/Panes/RedactFindPanel.cs` and the find bar if they draw their own chevrons, `src/Raffica.Gallery` icon list (`FoundationsSection`)
- Test: `tests/Raffica.Pdf.Tests/Commands/CommandIconTests.cs`, `tests/Raffica.Shell.Tests` icon resource test (every key resolves)

**Interfaces:**
- Consumes: Lucide at the tag recorded in `Icons.axaml` (1.63.0); fetch the SVGs of that tag (`lucide-static` package of the same version on jsDelivr) and convert them as the header comment of `Icons.axaml` describes.
- Produces: icon keys `Raffica.Icon.<LucideNameInPascalCase>` used by Tasks 10 and 11.

- [ ] **Step 1: Failing test** `CommandIconTests.NoGlyphServesTwoMeanings`: group the catalog by icon; a group with more than one command must be on an allow list of same-action pairs (for example `home.undo` and `annotate.undo` if both exist). Today it fails (Undo serves eight commands).
- [ ] **Step 2: Choose the glyphs.** A table in the commit message and as a comment block at the top of `PdfCommandCatalog`: command id → Lucide name. Use the obvious Lucide names (for example printer, settings, save, save-all for Save as, folder-open, x for close tab, chevron-left/right and chevrons-left/right for page navigation, arrow-up/down for find matches, panel-left for panes, zoom-in/zoom-out, scan-text for OCR, file-output for Extract, scissors for Split, image-down for Export images, rotate-ccw/rotate-cw, copy, trash-2, highlighter, pen-line, square, type, eraser, stamp or signature, eye-off for redaction marks). No glyph may suggest a state (no check mark for Save).
- [ ] **Step 3: Implement**, add each geometry to the gallery list, run the gallery headless tests and the new test.
- [ ] **Step 4: Look.** Screenshot the PDF app (Home, Annotate, Pages, Redact, Edit, View; compact bar) with the gallery's screenshot switch or the live-test procedure of Task 14 and check that no two neighbours look alike. **Commit** `feat(pdf): distinct icons for every command`.

---

### Task 10: Ribbon that never hides a command

**Files:**
- Modify: `src/Raffica.Shell/Frame/RibbonView.cs`, `src/Raffica.Shell/Frame/RibbonButton.cs` (checked state, label breaking), `src/Raffica.Shell/Frame/FieldCodeInput.cs` / badge placement, `src/Raffica.Shell/Frame/StatusBar.cs` (zoom tooltips), the ribbon styles, `src/Raffica.Pdf/Commands/PdfCommandCatalog.cs` (checked-state source, Help group additions), `src/Raffica.Pdf/Resources/Strings.de.resx` (shorter labels)
- Test: `tests/Raffica.Shell.Tests/Frame/RibbonViewTests.cs`, `tests/Raffica.Shell.Tests/Frame/GermanLayoutTests.cs`, a new `tests/Raffica.Pdf.Tests/PdfRibbonLayoutTests.cs`

**Interfaces:**
- Consumes: the icons of Task 9.
- Produces: `RibbonCommand.IsChecked` (a `Func<bool>?` re-read with `IsEnabled`), exposed as a pressed style and the UIA Toggle pattern; ribbon tabs expose SelectionItem.

- [ ] **Step 1: Failing tests** `PdfRibbonLayoutTests`: for every tab, in English and German, at 1280 and 1024 DIP: every command button lies fully inside the band's visible bounds or is reachable through the chevrons (no button clipped without a way to it); at 1280 the band is compact when the full band does not fit; with the manual compact setting it is compact at 1920.
- [ ] **Step 2: Implement** auto-compact (measure the full band once per tab and language; switch when the available width is smaller) and scroll chevrons at both ends when even compact overflows. Remove the backlog item "Automatic compact command bar below a window width" in the same commit (adopted).
- [ ] **Step 3: Labels (08-UI-05, 07-UI-12).** Tile labels break only at spaces or hyphens (no character-wise break); shorten the German labels: `CmdAnnotateMode` "Anmerken", `CmdApplyPages` "Änderungen anwenden", `CmdFormsHighlight` "Felder hervorheben" (also the badge), `CmdPagesRedo` "Wiederholen", `CmdPagesUndo` "Rückgängig", `CmdDiscardPages` "Verwerfen", `CmdExportImages` "Als Bilder exportieren …", `CmdInsertFile` "Aus Datei einfügen …". Extend `GermanLayoutTests` to every PDF ribbon tab.
- [ ] **Step 4: State (08-UI-10).** Dark page, the layout choices, the panes and the zoom fit modes show a pressed state when active; tests read the Toggle state through the automation peer.
- [ ] **Step 5: Small fixes.** Group labels sit on one bottom row in every group (08-UI-12); key-tip badges sit below the button, not over its text (08-UI-13); the status-bar zoom buttons get tooltips (07-UI-23); "Document info" and "Check for updates" join the Home Help group (07-UI-25); the form notice's dismiss button is focusable and Esc on the badge dismisses it.
- [ ] **Step 6: Run** the Shell and Pdf suites. **Commit** per step (`feat(shell): ribbon switches to compact and scrolls instead of clipping`, …).

---

### Task 11: Status bar, dialogs, About card, panes, screen-reader basics

**Files:**
- Modify: `src/Raffica.Shell/Styles/Frame/StatusBar.axaml`, `src/Raffica.Shell/Frame/StatusBar.cs`, `src/Raffica.Shell/Styles/Controls/Dialog.axaml`, `src/Raffica.Shell/Frame/DialogHost.cs` (if the max height is set there), `src/Raffica.Pdf/Dialogs/AboutCard.cs`, `tools/licenses.cs` and the generated notices (section title line, "enthält"), `src/Raffica.Pdf/Panes/RedactPane.Look.cs`, `src/Raffica.Pdf/Panes/RedactFindPanel.cs`, `src/Raffica.Shell/Frame/DocumentTabs.cs` (overflow), `src/Raffica.Pdf/Viewing/PageView.cs` (automation peer), strings
- Test: `GermanLayoutTests`, `tests/Raffica.Pdf.Tests/Dialogs/AboutCardTests.cs`, `tests/Raffica.Shell.Tests/Frame/DialogHostTests.cs`, a new `PageViewAutomationTests`

- [ ] **Step 1: Status bar (08-UI-04, 07-UI-03).** The save-state cell says "Unsaved" / "Nicht gespeichert"; the count and position cells size to their content with a minimum (Auto with `MinWidth`), taking room from the `*` column. Extend `GermanLayoutTests.NoDocumentTabOrStatusCellIsClippedInGerman` with SaveState, "128 Seiten · 210 × 297 mm" and "Seite 1000 von 1000".
- [ ] **Step 2: Dialogs scroll (08-UI-07, 07-UI-04).** Wrap the dialog body in a `ScrollViewer` (vertical Auto, horizontal Disabled) and cap the card at the host height minus 48 DIP; the focused control is brought into view. Test: Settings in a 1024 x 480 host reaches "Updates" by Tab and the row is in view.
- [ ] **Step 3: About card (08-UI-09, 08-UI-18).** The notices box fits the content width, the intro wraps, section toggles are disclosure rows without the Fluent checked fill (a shell style for a disclosure `ToggleButton`, tokens only), section titles show name and licence (generated by `licenses.cs`), the project address is a link button through the external-address confirmation, "enthält" with the umlaut (UTF-8 notices). Run `dotnet run tools/licenses.cs -- check`.
- [ ] **Step 4: Redact pane German (07-UI-11).** Checkboxes and buttons in the 256 px panes wrap their text (`Content = new TextBlock { TextWrapping = Wrap }`). Test at 256 px in German: no text wider than its control.
- [ ] **Step 5: Many tabs (08-UI-14).** Trim the file name before ".pdf" (keep the extension visible only when there is room); when tabs overflow, show scroll arrows and a list button with all tab names; off-strip tabs report offscreen.
- [ ] **Step 6: Screen-reader basics (08-UI-11).** `PageView` gets an automation peer with control type Document, `IsControlElement` true, name "<file>, page <n> of <m>"; the status cells are control elements with their text as name. Page text itself is a backlog item.
- [ ] **Step 7: Run** and **commit** per step.

---

### Task 12: Forms and contrast

**Files:**
- Modify: `src/Raffica.Shell/Styles/Controls/ComboBox.axaml` (editable variant), `src/Raffica.Pdf/Forms/FormFieldEditor.cs`, `src/Raffica.Pdf/Styles/PageView.axaml`, `src/Raffica.Pdf/Annotating/TextBoxEditor.cs`, `src/Raffica.Pdf/Styles/Panes.axaml` (`nav-list` selection), `src/Raffica.Shell/Styles/Controls/TextBox.axaml`, tokens if a new selection token is needed, `src/Raffica.Pdf/Forms/FormMarkers.cs` (lock glyph)
- Test: `tests/Raffica.Pdf.Tests/Forms/FieldEditTests.cs`, `tests/Raffica.Shell.Tests/Tokens/*`, `tests/Raffica.Shell.Tests/Controls/ComboBox*`

- [ ] **Step 1: Editable combo (07-UI-02).** Failing test: an editable combo field gets keyboard text input through key events (not by setting `Text` in code) and commits "Other value"; an existing custom value is shown when the editor opens. Implement `PART_EditableTextBox` in the shell's ComboBox theme, styled like the text field and shown only when `IsEditable`.
- [ ] **Step 2: Editors on paper (07-UI-09).** The text-box annotation editor and the form field editor are always paper white, so their caret is black and their edge uses the light-theme ink in every theme (a fixed token pair classified in `ContrastPolicy` as "on paper"). Test: contrast of caret and edge against white ≥ 3:1 in Light, Dark and HighContrast.
- [ ] **Step 3: Selection (07-UI-08, 07-UI-10).** Selected items of `nav-list` (organize pages, outline) get a 2 px edge bar in `Text.Primary` like `mark-edge`; the text selection brush in text fields reaches ≥ 3:1 against the field background in all three themes (new or changed token, full token procedure). Tests in `ContrastPolicy`.
- [ ] **Step 4: Lock marker (08-UI-20).** The lock glyph of a locked field sits outside the widget's left edge (or at the right edge when there is no room), never over the value. Test with `forms-basic.pdf` geometry.
- [ ] **Step 5: Run** the Shell and Pdf suites. **Commit** per step.

---

### Task 13: Words, order and reasons

**Files:**
- Modify: `src/Raffica.Pdf/Resources/Strings.*.resx`, `src/Raffica.Shell/Resources/Strings.*.resx`, `src/Raffica.Core/Pdf/PageRange.cs`, the card footers in `src/Raffica.Pdf/Dialogs/*`, `src/Raffica.Pdf/Printing/PrintCard.cs`, `src/Raffica.Pdf/Signing/*Card.cs`, `src/Raffica.Shell/Frame/SettingsDialog.cs`, `src/Raffica.Pdf/Commands/PdfCommandRules.cs` (`ReasonFor`), `src/Raffica.Pdf/Commands/PdfCommandCatalog.cs` (`HelpEntries`), `src/Raffica.Pdf/Dialogs/HelpCard.cs`, the Windows print path (job title)
- Test: `TranslationTests` (both), new `GermanRegisterTests`, `PageRangeTests`, `PdfCommandRulesTests`, `CardFooterTests`, `HelpCardTests`

- [ ] **Step 1: Sie (07-UI-05).** Failing test `GermanRegisterTests.NoDuForms`: no German value matches `\b(du|dich|dir|dein\w*)\b` or a du-imperative from a short list (wähle, gib, öffne, beende, verwirf, wende, lass); rewrite `ApplyWarning`, `ApplyOpenEdit`, `ApplyFailAdoptFormat`, `ApplyPlanChanged`, `ExportDpiInvalid`, `ExportTooLargeFormat`, `ExportTooLargeHugeFormat` in the Sie form.
- [ ] **Step 2: "alle" (07-UI-06).** `PageRange.Parse` accepts "all" and "alle" (case-insensitive); the range fields start with the keyword of the UI language; the labels name that keyword in quotes. Tests.
- [ ] **Step 3: Wording fixes.** "herunter" in `UpdateManualMessage`, `UpdateFailureNoInstaller`, `UpdateFailureFolderWriteProtectedFormat`, `UpdateFailureHelperInstallFailed` (07-UI-16); "Wie die Darstellung" for `SettingsPageFollowTheme`; "annotations"/"Anmerkungen" for annotation marks on the redact cards; one name for the field highlight (Task 10 owns the ribbon label, use the same here); button names quoted the same way everywhere (07-UI-17); US spelling in English (07-UI-18); the commas in `UnsavedLockedTitle` and `OcrReportNotUndoable` and one ellipsis style, "…" directly after the word, in both languages (07-UI-19, 08-UI-17). A test pins the ellipsis style.
- [ ] **Step 4: Footers (Call 8; 07-UI-13, 08-UI-17).** A shared footer helper (`CardFooter.Build(primary, secondary[], cancel)`) used by every card; `CardFooterTests` walks every card type and checks order, `IsDefault` and `IsCancel`.
- [ ] **Step 5: Reasons (07-UI-07).** `ReasonFor` returns "Turn on Redact first" / "Schalten Sie zuerst „Schwärzen“ ein" for the inside-mode commands, the same for Organize pages, Edit text and the OCR run, and `SaveBusy` for Print, Save and Save as while a save runs. `PdfCommandRulesTests` asserts that every command that can be disabled has a reason in every disabled state the tests reach.
- [ ] **Step 6: Help card (07-UI-20).** A fixed "Navigation" section with Ctrl+K, F6, Alt, Ctrl+Y, Enter and Shift+Enter in search, the Organize keys, the form keys, Ctrl+Enter in text boxes and comments, Ctrl+Shift+G (through `KeyNames`).
- [ ] **Step 7: Hints and print title (07-UI-21, 08-UI-19).** The validation hints of the cards get `AutomationProperties.LiveSetting = Assertive`; the Windows print job and dialog use "Raffica PDF" as the application name and the document name as job title.
- [ ] **Step 8: Run** and **commit** per step.

---

### Task 14: Platform: AppImage variable, all-users default app, OCR language lock, default-app card

**Files:**
- Modify: `src/Raffica.Pdf/Platform/Linux/LinuxDefaultAppService.cs`, `src/Raffica.Pdf/Platform/Windows/WindowsDefaultAppService.cs`, `src/Raffica.Pdf/Commands/PdfCommandRules.cs` (`OcrLanguages`), `src/Raffica.Pdf/PdfWindow.Ocr.cs` (`LanguagesAsync`), `src/Raffica.Pdf/Packaging/DefaultAppFlow.cs`, `src/Raffica.Core/Settings/AppSettings.cs` (a `DefaultAppPromptSnoozedUntil`), `Docs/roadmap.json` (scope change)
- Test: `DefaultAppServiceTests`, `DefaultAppFlowTests`, `PdfCommandRulesTests`

- [ ] **Step 1: Inherited `APPIMAGE` (01-S2).** Treat `APPIMAGE` as this program only when `PackageFormDetector.IsInsideAppDir(Environment.ProcessPath, APPDIR)`, in `SetDefaultAsync` and `QueryStatusAsync`. Test with a foreign `APPIMAGE` and `APPDIR`: the non-AppImage path is used and no desktop file names the foreign file.
- [ ] **Step 2: All-users installation (06-B-2).** Read the open command through HKCU and then HKLM (`Software\Classes\Raffica.PDF\shell\open\command`); skip the HKCU write in `SetDefaultAsync` when a matching HKLM registration exists. Test with a fake registry root.
- [ ] **Step 3: OCR languages (06-B-3).** `OcrLanguages` is disabled with its reason while any OCR run goes (`OcrBusy`), and `LanguagesAsync` returns early then. Test with a run in another tab.
- [ ] **Step 4: Snooze (Call 7; 08-UI-16).** "Not now" stores today + 30 days; the card is offered only after that date; "Don't ask again" stays permanent. Tests with a fake clock. Add the scope change to `Docs/roadmap.json`.
- [ ] **Step 5: Run** and **commit** per step.

---

### Task 15: The start-time regression

**Files:**
- Modify: `.github/workflows/measure.yml` (an optional `ref` input, validated like the update-check workflows), `bench/shell-probe/README.md`, whatever the bisection finds, `Docs/DECISIONS.md` (a measurement entry "Raffica PDF: release readiness measurements (M2.7)")

**Context:** run 42502138965 (Linux, `target=pdf`, 2026-11-08) went over its limits: cold 333.94 ms (limit 287.5), warm 342.47 (287.5), 1000 pages 340.34 (275), second file 57.98 (50); scroll, memory, size and zip passed. The control run 42503638604 of the predecessor `v1.0.0` on the same laptop the same day gave cold 276.3, warm 285.8, 1000 pages 281.8, second file 45.8 ms, against 210.8, 208.0, 208.6 and 46.4 ms on 2026-11-04. So the machine is about 31 to 37 % slower on the start cases than on 2026-11-04, but the PDF app also lost ground against the predecessor: start ratio 1.04 to 1.10 then, about 1.20 now; second file 0.73 then, 1.27 now. The Windows run of the same commit is open.

- [ ] **Step 1: `ref` input.** Add `ref` to `measure.yml` (empty = the dispatch ref; a commit id or a branch name, never `pull/*`, the same check as `linux-update-check.yml`), checked out for the measured app only. The runner group keeps working because the workflow file still runs from `dev`.
- [ ] **Step 2: Windows run.** When the owner has started the Windows runner, dispatch `target=pdf` on `dev` and record the verdict.
- [ ] **Step 3: Bisect.** Measure the merge commits of M2.2, M2.3, M2.4, M2.5 and M2.6 on `dev` (from `git log --first-parent dev`) with `target=pdf` on the machine that shows the regression, plus the predecessor control in the same session; find the first commit whose ratio to the predecessor jumps. Inside that range, bisect further. Look first at work that may run before the first sharp page or on every open (the second-file case): settings files read at start, the update and default-app services, the scan-hint probe, the notices, the form or text pre-reads.
- [ ] **Step 4: Fix** the cause, with a test that pins it (for example a `StartupCreatesNo…` test for the work that moved before the first sharp page).
- [ ] **Step 5: Measure** both machines on the fixed `dev`, each with the predecessor control. The check step passes on both, or the result is a finding for the owner with numbers (budgets are never loosened). Record runs and medians in `Docs/DECISIONS.md` and in the roadmap evidence.
- [ ] **Step 6: Commit** `perf(pdf): …` (the fix) and `docs: record the M2.7 measurements`.

---

### Task 16: Final check

**Files:** `Docs/roadmap.json`, `Docs/ROADMAP.md`, `CHANGELOG.md`, `CLAUDE.md` (a short "Release readiness (M2.7)" paragraph with the new conventions: crash log, open-target guard, auto-compact, icon rule, footer helper, Sie)

- [ ] **Step 1: Live UI pass.** Publish the app (Native AOT, `win-x64`) and repeat the live check of 2026-11-08 with a scratch `--settings-file`: start view, every ribbon tab at 1280 x 860 and 1024 x 600 in German and English, dark and high contrast, Settings at 1024 x 480, About, the status bar with a 1,000-page file, the open errors, a form with an editable combo. Every item of Tasks 8 to 13 is either seen fixed (screenshot path in the PR) or reopened.
- [ ] **Step 2: Full suite** (build, test with `RAFFICA_REQUIRE_OCR=1`, format, roadmap check) and the release dry run on the `dev` head.
- [ ] **Step 3: Roadmap.** Tick tasks and acceptance with evidence, status done, `completedOn`, backlog items of Call 9. **Commit** `docs: close milestone 2.7`.
