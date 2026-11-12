# Milestone 3.1 – Runs on the Mac – Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Raffica PDF builds, passes its tests and opens PDFs from the Finder on Intel (`osx-x64`) and Apple-silicon (`osx-arm64`) Macs, checked by a sparse `build-macos` CI job.

**Architecture:** Mostly platform plumbing, no new PDF feature. One shared path rule (`PathRules`) replaces the 16 places that compare paths by `OperatingSystem.IsWindows()`. `FileIdentity`, `UserDataPaths`, `SingleInstance` and `TesseractLibrary` learn macOS. A small `FileActivationBridge` turns Avalonia's file activation (Apple events) into the existing hand-over path of `PdfWindow`. PDFium comes from a NuGet package, Tesseract from a new macOS job of `native-tesseract.yml` pinned in `tools/native-fetch.cs`. CI gets one `build-macos` job on `macos-15` that runs on a few paths and a label. Packaging (`.app`, `.dmg`, signing), menu bar, life cycle, printing, default app and updates are 3.2 and 3.3 and stay out.

**Tech Stack:** .NET 10 Native AOT, Avalonia 12.1.5 (`IActivatableLifetime`, `FileActivatedEventArgs`), `LibraryImport` against `libSystem`, `bblanchon.PDFium.macOS`, CMake and Apple clang for Tesseract, GitHub Actions (`macos-15`), `gh`.

**Spec:** `Docs/superpowers/specs/2026-11-09-raffica-macos-design.md` (approved 2026-11-09; sections 4, 5.3, 5.9, 5.10, 7 and the 3.1 row of section 8 are this milestone). Roadmap: `Docs/roadmap.json`, milestone `3.1`.

## Global Constraints

- Everything in English: code, comments, docs, commits, workflow text.
- Platform code only through a seam, never UI code; macOS interop uses `LibraryImport` and stays Native AOT safe (no reflection). No Objective-C runtime calls in this milestone.
- Windows and Linux behaviour does not change unless a task says so. Every task keeps Windows tests green locally.
- The repository is public: GitHub-hosted standard runners only, never larger runners. macOS jobs run only on pull requests into `main`, on pull requests that touch platform-near paths, and on pull requests with the label `ci:macos` (spec section 7).
- Apple silicon is checked automatically only; nothing here may be marked "tested by hand" for arm64.
- Budgets and limits are never loosened to make a check pass. Size budgets, signing, `.app`, `.dmg` are 3.3.
- Commit only with the GitHub noreply address. Conventional Commits, no attribution lines. Stage files by name, never `git add .` or `-A`. After every commit run `git show --stat HEAD` and compare with the task's file list.
- Two files in the working tree are foreign and were modified before this branch: `src/Raffica.Shell/Frame/SettingsStore.cs` and `tests/Raffica.Pdf.Tests/Panes/NavListSelectionTests.cs`. Task 3 must edit the first one: run `git diff src/Raffica.Shell/Frame/SettingsStore.cs` before editing. If it shows foreign hunks, stage only the own hunks with `git add -p`; never stage the second file.
- No force push, no `reset --hard`, no `--no-verify`, no work on `main` or `dev`. The PR goes to `dev` and is merged by Claude once all CI checks are green (squash allowed). Dispatching a workflow that creates a release (Task 7, step 8) needs the owner's explicit go first.
- New or changed strings: none in this milestone (no UI text). If one appears, both `Strings.en.resx` and `Strings.de.resx` get it.
- "Done" = `dotnet build Raffica.slnx -c Release`, `RAFFICA_REQUIRE_OCR=1 dotnet test Raffica.slnx -c Release --no-build --max-parallel-test-modules 1`, `dotnet format Raffica.slnx --verify-no-changes` and `dotnet run tools/roadmap.cs -- check` green on Windows, plus `build-macos` green on the PR, with commands and results named in the roadmap evidence.
- Whatever needs the owner's Mac (Finder double-click, `open -a`, OCR by hand) is marked open with its command, never ticked from CI.

## Review Focus

The inputs that the spec implies but no task's own happy path would catch. Each line has its test in the named task.

1. **Same file, different spelling (`Bericht.pdf` / `bericht.pdf`) on a case-insensitive APFS volume:** opens one tab, a write onto the other spelling is refused, the CLI refuses output = input. Tasks 1 and 2.
2. **A German file name with an umlaut in the other normalization (`Übersicht.pdf`, NFC vs NFD):** one tab, one recent entry, same identity. Tasks 1 and 2.
3. **A long macOS temp folder (`/var/folders/xx/…/T/`, about 49 characters) plus a long user name:** the single-instance socket path would pass 104 bytes, the listener would fail silently and every Finder open would start a second window. Task 4.
4. **A PDF handed over by the Finder before the window listens (first start, no `argv`):** the file must not be lost. Task 5.
5. **`-psn_0_12345` in `argv` (older macOS launch through LaunchServices):** must not become a file that "cannot be found". Task 5.

## File Structure

| File | Responsibility |
|---|---|
| `src/Raffica.Core/Saving/PathRules.cs` (new) | The one rule for "are these two paths the same name" per system |
| `src/Raffica.Core/Saving/FileIdentity.cs` | gains the macOS identity from `stat` |
| `src/Raffica.Core/Settings/UserDataPaths.cs` | macOS config and cache folders |
| `src/Raffica.Shell/Frame/SettingsStore.cs` | macOS default path of the shell settings |
| `src/Raffica.Core/Hosting/SingleInstance.cs` | socket path that fits `sun_path` |
| `src/Raffica.Pdf/Hosting/FileActivationBridge.cs` (new) | buffers and forwards Finder and Dock files |
| `src/Raffica.Pdf/Hosting/AvaloniaFileActivationSource.cs` (new) | the only place that touches `IActivatableLifetime` |
| `src/Raffica.Ocr/TesseractLibrary.cs` | `.dylib` name |
| `tools/check-binary.cs` | Mach-O rules next to the ELF rules, plus `selftest` |
| `packaging/native/build-macos.sh` (new), `versions.txt`, `.github/workflows/native-tesseract.yml` | macOS Tesseract build |
| `tools/native-fetch.cs` | pins for `osx-x64` and `osx-arm64` |
| `.github/workflows/ci.yml`, `.github/scripts/macos-paths.sh` (new) and its test | the `build-macos` job and its path filter |
| `packaging/macos/dev-app.sh` (new) | throwaway `.app` wrapper for the owner's manual check |

---

### Task 1: One path rule for every comparison

**Files:**
- Create: `src/Raffica.Core/Saving/PathRules.cs`
- Modify (replace the `OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal` ternary and the two `StringComparer` variants): `src/Raffica.Pdf/Documents/Workspace.cs`, `Documents/SaveController.cs`, `Documents/OpenTargetGuard.cs` (only its doc comment), `Organizing/JobRunner.cs`, `Organizing/InsertFileFlow.cs`, `Organizing/OrganizeSession.cs`, `Organizing/ApplyFlow.cs`, `Redacting/RedactApplyFlow.cs`, `Platform/UpdateApplierSupport.cs`, `Platform/Windows/WindowsUpdateHelper.cs` (all under `src/Raffica.Pdf/`); `src/Raffica.Core/Saving/DocumentSaver.cs`, `Pages/DocumentSplitter.cs`, `Pages/PageImageExporter.cs`, `Settings/RecentFiles.cs`, `Updates/SessionSnapshot.cs`; `src/Raffica.Cli/Commands/OutputGuard.cs`, `Commands/SplitCommand.cs`
- Test: `tests/Raffica.Core.Tests/Saving/PathRulesTests.cs`, `tests/Raffica.Core.Tests/Saving/PathComparisonGuardTests.cs`, `tests/Raffica.Pdf.Tests/Documents/WorkspaceTests.cs`

**Interfaces:**
- Produces (Core, namespace `Raffica.Core.Saving`):
  - `public static class PathRules` with
    - `public static bool IsCaseInsensitive { get; }` (Windows or macOS),
    - `public static bool AreEqual(string? a, string? b)`,
    - `public static StringComparer Comparer { get; }` (same rule for dictionaries and sets),
    - `internal static bool AreEqual(string? a, string? b, PathSystem system)` (the test seam),
    - `internal enum PathSystem { Windows, Linux, MacOS }`.
  - Rules: Windows `OrdinalIgnoreCase`; Linux `Ordinal`; macOS: an `Ordinal` match is equal at once, otherwise both strings are normalized to NFC and compared `OrdinalIgnoreCase`. Both `null` is equal, one `null` is not. `Comparer.GetHashCode` follows the same normalization so equal paths hash equal.
- Consumes: nothing.

- [ ] **Step 1: Write the failing tests** in `PathRulesTests`: `WindowsIgnoresCase` (`C:\A\Bericht.pdf` equals `c:\a\bericht.pdf`); `LinuxKeepsCase` (`/a/Bericht.pdf` not equal `/a/bericht.pdf`); `MacOsIgnoresCase` (`/Users/x/Bericht.pdf` equals `/Users/x/bericht.pdf`); `MacOsTreatsNfcAndNfdAsOneName` (`"/x/\u00dcbersicht.pdf"` equals `"/x/U\u0308bersicht.pdf"`, and not equal `"/x/Ubersicht.pdf"`); `MacOsDifferentNamesStayDifferent` (`a.pdf` vs `b.pdf`); `NullHandling` (both null equal, one null not); `ComparerDeduplicatesLikeAreEqual` (a `HashSet<string>(PathRules.Comparer)` on this machine holds one entry for `Bericht.pdf` and `bericht.pdf` exactly when `PathRules.IsCaseInsensitive`, and `PathRules.IsCaseInsensitive == (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())`). In `PathComparisonGuardTests` write `NoSourceFileChoosesPathCaseByWindowsCheck`: read every `*.cs` under `src/` (root from `TestData.Root`) and assert no line matches the regex `IsWindows\(\)\s*\?\s*(StringComparison|StringComparer)\.OrdinalIgnoreCase`; the failure message lists file and line. In `WorkspaceTests` add `OpeningTheSameFileUnderAnotherSpellingSelectsTheExistingTab`: it needs a case-insensitive file system, so it creates `Bericht.pdf` in a temp folder, asks `File.Exists` for `bericht.pdf`, and uses `Assert.SkipUnless(ignoresCase, "macOS/Windows only: the volume must ignore case")`; then `workspace.Find(upperCaseSpelling)` returns the tab added under the other spelling.
- [ ] **Step 2: Run to verify they fail:** `dotnet test tests/Raffica.Core.Tests -c Release --filter-class "*PathRulesTests"` and `--filter-class "*PathComparisonGuardTests"`. Expected: compile error (`PathRules` missing), after a stub the guard test fails listing the 16 sites.
- [ ] **Step 3: Implement `PathRules`** as specified in Interfaces. A private nested `PathComparer : StringComparer` serves `Comparer`; normalization uses `string.Normalize(NormalizationForm.FormC)` guarded so that a string that is already ASCII skips it.
- [ ] **Step 4: Replace every site** listed under Files with `PathRules.AreEqual(a, b)` or `PathRules.Comparer`. Where a local `comparison` variable was only passed to `string.Equals`, delete the variable. `Workspace.PathComparison` becomes a call to `PathRules.AreEqual`. Do not change anything else in those methods.
- [ ] **Step 5: Run** the three test classes (expect pass), then `dotnet build Raffica.slnx -c Release` (0 warnings) and `dotnet test tests/Raffica.Core.Tests -c Release --no-build` plus `dotnet test tests/Raffica.Pdf.Tests -c Release --no-build` (expect 0 failed; on Windows the macOS-only skip shows its reason).
- [ ] **Step 6: Commit**
```bash
git add src/Raffica.Core/Saving/PathRules.cs <every modified source file by name> tests/Raffica.Core.Tests/Saving/PathRulesTests.cs tests/Raffica.Core.Tests/Saving/PathComparisonGuardTests.cs tests/Raffica.Pdf.Tests/Documents/WorkspaceTests.cs
git commit -m "feat(core): one path rule for case and Unicode normalization, used by every comparison"
```

### Task 2: `FileIdentity` from device and inode on macOS

**Files:**
- Modify: `src/Raffica.Core/Saving/FileIdentity.cs`
- Create: `tests/Raffica.Core.Tests/Saving/FileIdentityTests.cs`, `tests/Shared/TestVolume.cs`
- Modify: `tests/Raffica.Core.Tests/Cli/RotateCommandTests.cs` (the two `OperatingSystem.IsWindows()` upper-case branches)

**Interfaces:**
- Consumes: `PathRules` (Task 1) for the doc and the tests.
- Produces:
  - `FileIdentity.AreSameFile(string, string) : bool?` unchanged signature; on macOS it answers `true` or `false` from the file system and `null` only when the system cannot tell.
  - `internal static byte[]? IdentityFromDarwinStat(ReadOnlySpan<byte> stat)`: 16 bytes, `st_ino` (8 bytes at offset 8) then `st_dev` (4 bytes at offset 0) zero-padded; `null` for a buffer shorter than 16. Pure, so it is testable on every system.
  - `TestVolume.IgnoresCase(string folder) : bool` and `TestVolume.TreatsNfcAndNfdAsOneName(string folder) : bool` in `Raffica.TestSupport`: create a probe file in the folder, ask `File.Exists` with the other spelling, delete the probe.

Notes the implementer must know: on `x86_64` macOS the plain `stat` symbol of `libSystem` is the legacy 32-bit-inode variant; the 64-bit one is `stat$INODE64`. On `arm64` only `stat` exists and is 64-bit. So two `LibraryImport`s (`EntryPoint = "stat$INODE64"` and `"stat"`) and `RuntimeInformation.ProcessArchitecture` picks one. `struct stat` (64-bit inode): `st_dev` int32 at 0, `st_mode` uint16 at 4, `st_nlink` uint16 at 6, `st_ino` uint64 at 8; use a 256-byte buffer.

- [ ] **Step 1: Write the failing tests** in `FileIdentityTests`: `DarwinStatBuffersWithSameInodeAndDeviceAreEqual` and `...DifferentInodeAreDifferent` (hand-built 144-byte buffers, no OS needed); `DarwinStatBufferTooShortGivesNull`; `TwoNamesOfOneFileAreTheSameFile` (a hard link made with `File.CreateHardLink`-equivalent: use `ln` on Unix, skip on Windows where the existing RotateCommand tests cover it, reason "Unix only"); `ACopyIsAnotherFile`; `AMissingFileIsFalse`; `MacOsAnswersAndDoesNotGiveUp` (`Assert.SkipUnless(OperatingSystem.IsMacOS(), "macOS only")`; two existing files give a non-null answer); `OtherSpellingOfTheNameIsTheSameFile` (probe `TestVolume.IgnoresCase`; file `Bericht.pdf` vs `bericht.pdf`; skip with reason when the volume is case-sensitive); `OtherNormalizationOfTheNameIsTheSameFile` (`Übersicht.pdf` NFC vs NFD, probe `TestVolume.TreatsNfcAndNfdAsOneName`, skip with reason otherwise). In `RotateCommandTests` replace the two `OperatingSystem.IsWindows()` upper-case branches (`spellings.Add(input.ToUpperInvariant())` and the `FileIdentity.AreSameFile(input, input.ToUpperInvariant())` assertion) by `TestVolume.IgnoresCase(workDirectory.FullName)`.
- [ ] **Step 2: Run to verify they fail:** `dotnet test tests/Raffica.Core.Tests -c Release --filter-class "*FileIdentityTests"`. Expected: compile error (`IdentityFromDarwinStat` missing).
- [ ] **Step 3: Implement** `IdentityFromDarwinStat`, the two imports in a private `MacOS` class, and a `MacId(string fullPath)` that returns `null` when the call fails; add the `OperatingSystem.IsMacOS()` branch next to the Linux one in `AreSameFile`; keep the `DllNotFoundException`/`EntryPointNotFoundException` catch so a missing symbol gives `null`, not a crash. Update the class summary (it names Windows and Linux).
- [ ] **Step 4: Implement `TestVolume`** in `tests/Shared` (it is linked into the test projects the same way `TestData` is; check how `tests/Shared/*.cs` is included and follow it).
- [ ] **Step 5: Run** `dotnet test tests/Raffica.Core.Tests -c Release --filter-class "*FileIdentityTests"` and `--filter-class "*RotateCommandTests"` (expect pass on Windows; the Unix-only and macOS-only tests skip with a reason). The macOS result is proven in Task 10.
- [ ] **Step 6: Commit** `feat(core): FileIdentity answers from device and inode on macOS` with the files above.

### Task 3: Mac folders for settings and caches

**Files:**
- Modify: `src/Raffica.Core/Settings/UserDataPaths.cs`, `src/Raffica.Shell/Frame/SettingsStore.cs` (foreign changes in the working tree: see Global Constraints)
- Test: `tests/Raffica.Core.Tests/Settings/UserDataPathsTests.cs`, `tests/Raffica.Shell.Tests/Frame/SettingsStoreTests.cs`

**Interfaces:**
- Produces (`Raffica.Core.Settings`):
  - `public enum UserDataSystem { Windows, Linux, MacOS }` and `public static UserDataSystem Current { get; }` inside `UserDataPaths`.
  - `public static string ConfigDirectory(Func<string, string?> getEnvironmentVariable, UserDataSystem system)` replaces the `bool isWindows` overload; the parameterless `ConfigDirectory()` keeps its signature. Rules: Windows and Linux exactly as today; macOS `$HOME/Library/Application Support/Raffica`; `XDG_CONFIG_HOME` is not read on macOS; an empty or blank `HOME` gives `Path.Combine(Path.GetTempPath(), "Raffica")`.
  - `public static string CacheDirectory()` and `CacheDirectory(Func<string, string?>, UserDataSystem)`: Windows `%LOCALAPPDATA%\Raffica\Cache` (fallback `%USERPROFILE%\AppData\Local\Raffica\Cache`, then temp), Linux `$XDG_CACHE_HOME/raffica` (fallback `$HOME/.cache/raffica`, then temp), macOS `$HOME/Library/Caches/Raffica` (fallback temp). Nothing consumes it yet; it exists so later apps do not invent their own rule (the roadmap task names it).
- Produces (`Raffica.Shell.Frame.SettingsStore`): `public static string DefaultPath(string appName)` unchanged signature; `internal static string DefaultPath(string appName, bool isMacOS, Func<Environment.SpecialFolder, string> folder)` is the seam. macOS: `<UserProfile>/Library/Application Support/Raffica/<appName>/settings.json`; otherwise as today (`SpecialFolder.ApplicationData`). Shell has no reference to Core, so the rule is written there once more by design; the test in step 1 pins that both resolve to the same parent on macOS.
- `--settings-file` still redirects everything: `PdfSettingsFiles.*For(settingsFile)` is untouched and its tests stay green.

- [ ] **Step 1: Write the failing tests.** `UserDataPathsTests`: change the existing calls to `UserDataSystem.Windows` / `UserDataSystem.Linux`; add `MacOsUsesApplicationSupport` (`HOME=/Users/x` gives `/Users/x/Library/Application Support/Raffica`), `MacOsIgnoresXdgConfigHome`, `MacOsWithoutHomeFallsBackToTemp`, `MacOsCacheIsUnderLibraryCaches`, `LinuxCacheFollowsXdgCacheHome`, `WindowsCacheIsUnderLocalAppData`, `EveryResultIsNonEmptyWithoutEnvironment` (all three systems, empty environment). `SettingsStoreTests`: `MacOsPutsTheShellSettingsUnderApplicationSupport` using the seam (`UserProfile` returns `/Users/x`; expected `/Users/x/Library/Application Support/Raffica/Pdf/settings.json`, compare with `Path.Combine` to stay OS-neutral) and `OtherSystemsKeepApplicationData`; `ShellAndCoreAgreeOnTheMacParent` (the parent of the shell result's `Raffica` folder equals the parent of `UserDataPaths.ConfigDirectory(env, UserDataSystem.MacOS)`).
- [ ] **Step 2: Run to verify they fail:** `dotnet test tests/Raffica.Core.Tests -c Release --filter-class "*UserDataPathsTests"`. Expected: compile error.
- [ ] **Step 3: Implement** both files as specified. Update the XML summaries (they name `$XDG_CONFIG_HOME`).
- [ ] **Step 4: Run** `dotnet test tests/Raffica.Core.Tests -c Release --filter-class "*UserDataPathsTests"`, `--filter-class "*TessdataStoreTests"` and `dotnet test tests/Raffica.Shell.Tests -c Release --filter-class "*SettingsStoreTests"` (expect pass), then `dotnet test tests/Raffica.Pdf.Tests -c Release --filter-class "*PdfSettingsFilesTests"`.
- [ ] **Step 5: Commit** `feat: settings and cache folders under ~/Library on macOS` (`git diff` rule of the Global Constraints for `SettingsStore.cs`).

### Task 4: A single-instance socket path that fits on macOS

**Files:**
- Modify: `src/Raffica.Core/Hosting/SingleInstance.cs`
- Test: `tests/Raffica.Core.Tests/Hosting/SingleInstanceTests.cs`

**Interfaces:**
- Produces: `internal static string PipeNameFor(string name, string? runtimeDirectory, string tempPath)`: the seam of the existing `PipeNameFor(string name)` (which now passes `RuntimeDirectory()` and `Path.GetTempPath()`). Rules: unchanged for a usable runtime directory; otherwise the plain safe name when `SocketPathFor` of it stays under 100 UTF-8 bytes (`sun_path` is 104 on macOS and 108 on Linux, including the terminator, and .NET builds `<temp>/CoreFxPipe_<name>`), else a short name `"r-" + <first 8 bytes of SHA-256 of the original name as hex>` (18 characters). Two different original names never share a short name in the test set.
- Consumes: the existing `FileSafeName`, `SocketPathFor`.

- [ ] **Step 1: Write the failing tests:** `PlainNameIsKeptWhenTheSocketPathFits` (short temp path, `raffica-ben`); `LongTempFolderGivesAShortHashedName` (a 49-character macOS style temp path `/var/folders/zz/zyxvpxvq6csfxvn_n0000000000000/T/` and the longest realistic name `raffica-<20 character user>-<12 hex>`; assert the returned name differs, the resulting `SocketPathFor` is under 100 bytes); `ShortNamesAreStableAndDistinct` (same input twice equal, two inputs differ); `RuntimeDirectoryStillWinsWhenItFits`. Add a real round trip `LongTempFolderStillHandsOverFiles` that sets `TMPDIR` to a created folder whose path is about 80 characters, restores it in `finally`, becomes primary, starts listening, sends one path with `TrySend`, and asserts the callback received it; `Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix only: Windows pipes are not socket files")`; it runs in the serial collection the other tests of that class use (read the class and follow it).
- [ ] **Step 2: Run to verify they fail:** `dotnet test tests/Raffica.Core.Tests -c Release --filter-class "*SingleInstanceTests"`. Expected: compile error (seam missing).
- [ ] **Step 3: Implement** the seam and make `PipeNameFor(string)` call it. The lock file keeps `Path.Combine(RuntimeDirectory() ?? Path.GetTempPath(), FileSafeName(name) + ".lock")` (a file name has no length limit that matters here).
- [ ] **Step 4: Run** the class (expect pass; the round trip skips on Windows with its reason).
- [ ] **Step 5: Commit** `fix(core): keep the single-instance socket path under the sun_path limit`.

### Task 5: Files from the Finder and the Dock

**Files:**
- Create: `src/Raffica.Pdf/Hosting/FileActivationBridge.cs`, `src/Raffica.Pdf/Hosting/AvaloniaFileActivationSource.cs`
- Modify: `src/Raffica.Pdf/App.axaml.cs`, `src/Raffica.Pdf/PdfWindow.axaml.cs` (property and one call in `OnOpened`), `src/Raffica.Pdf/PdfOptions.cs`
- Test: `tests/Raffica.Pdf.Tests/Hosting/FileActivationBridgeTests.cs`, `tests/Raffica.Pdf.Tests/PdfOptionsTests.cs`, `tests/Raffica.Pdf.Tests/Packaging/StartupCreatesNoPackagingWorkTests.cs` (one added case)

**Interfaces:**
- Produces:
  - `internal interface IFileActivationSource { event Action<IReadOnlyList<string>>? FilesActivated; }`: local file paths only, in the order the system gave them.
  - `internal sealed class FileActivationBridge : IDisposable`: `FileActivationBridge(IFileActivationSource source)` subscribes at once and queues what arrives; `void Attach(Action<IReadOnlyList<string>> sink)` hands the queued batches to the sink in their order, then forwards later batches directly; a second `Attach` throws `InvalidOperationException`; `Dispose` unsubscribes. Batches arriving on any thread are queued under a lock, and the sink is never called while the lock is held.
  - `internal sealed class AvaloniaFileActivationSource(IActivatableLifetime lifetime) : IFileActivationSource`: subscribes to `lifetime.Activated`; for `ActivationKind.File` takes `FileActivatedEventArgs.Files`, keeps those whose `TryGetLocalPath()` is not null; other kinds are ignored (Reopen is 3.2). The exact member names of Avalonia 12.1.5 (`Files`, `TryGetLocalPath`) are checked by the compiler; if they differ, use the equivalent and keep the contract above.
  - `PdfWindow`: `internal FileActivationBridge? FileActivation { get; set; }`; in `OnOpened`, after `StartListening()`, `FileActivation?.Attach(paths => _ = Task.Run(() => OnHandover(paths)))` (the existing `OnHandover` posts to the UI itself; the file check runs off the UI thread as for the pipe).
  - `App.OnFrameworkInitializationCompleted`: on macOS and not in a measurement run, `TryGetFeature<IActivatableLifetime>()` (Avalonia `Application.TryGetFeature`) builds the bridge and sets `window.FileActivation`.
  - `PdfOptions.Parse` ignores any argument that starts with `-psn_` (the process serial number older macOS puts into `argv`).

- [ ] **Step 1: Write the failing tests.** `FileActivationBridgeTests` with a fake source: `BatchBeforeAttachIsDeliveredOnAttach` (raise one batch, attach, the sink gets it once); `OrderIsKept` (two batches before attach arrive in order); `BatchAfterAttachIsForwardedDirectly`; `NothingIsDeliveredAfterDispose`; `SecondAttachThrows`; `ConcurrentBatchesAreNeitherLostNorDuplicated` (raise 200 batches from 4 tasks while attaching; the sink sees all 200 exactly once); `SinkIsNotCalledUnderTheLock` (the sink raises a new batch from inside itself; no deadlock). `PdfOptionsTests`: `ProcessSerialNumberArgumentIsIgnored` (`["-psn_0_12345", "a.pdf"]` gives `Files == ["a.pdf"]`) and `ADashedFileNameThatIsNotPsnStaysAFile`. `StartupCreatesNoPackagingWorkTests`: assert that on a non-macOS start `PdfWindow.FileActivation` is null (the bridge exists only on macOS and costs nothing elsewhere).
- [ ] **Step 2: Run to verify they fail:** `dotnet test tests/Raffica.Pdf.Tests -c Release --filter-class "*FileActivationBridgeTests"`. Expected: compile error.
- [ ] **Step 3: Implement** the bridge, the Avalonia source, the `PdfOptions` rule, the window property and the `App` wiring. Keep the single-instance pipe path unchanged (it still serves starts from the command line).
- [ ] **Step 4: Run** `dotnet test tests/Raffica.Pdf.Tests -c Release --filter-class "*FileActivationBridgeTests"`, `--filter-class "*PdfOptionsTests"`, `--filter-class "*StartupCreates*"` (expect pass; check the counts are not 0 per `CLAUDE.md`).
- [ ] **Step 5: Commit** `feat(pdf): open files the Finder and the Dock hand to the app`. The live behaviour (first start and while running) is a manual check in Task 11.

### Task 6: PDFium for macOS

**Files:**
- Modify: `Directory.Packages.props`, `src/Raffica.Pdfium/Raffica.Pdfium.csproj`, `third-party/licenses.json`, `src/Raffica.Pdf/Resources/ThirdPartyNotices.txt` (generated), `tools/licenses.cs` (only the fixtures of its `selftest` if they list the PDFium packages by name and the new package breaks them)
- Test: `dotnet run tools/licenses.cs -- selftest`, `-- check`

**Interfaces:** none new. Consumes the version of the Windows and Linux packages (`157.0.8100`; `tools/licenses.cs` requires one version for every `bblanchon.PDFium.*`).

- [ ] **Step 1: Check the package exists** in that version: `dotnet package search bblanchon.PDFium.macOS --exact-match --format json` (or the NuGet API). Expected: version `157.0.8100` listed. If it is not, stop and report; do not pick another version silently (the three packages must match).
- [ ] **Step 2: Add** `<PackageVersion Include="bblanchon.PDFium.macOS" Version="157.0.8100" />` and the `PackageReference` in `Raffica.Pdfium.csproj`. Run `dotnet restore Raffica.slnx`; check the restored package carries `runtimes/osx-x64/native/libpdfium.dylib` and `runtimes/osx-arm64/native/libpdfium.dylib` (look in `~/.nuget/packages/bblanchon.pdfium.macos/157.0.8100/runtimes`). If the layout differs, record it in the commit message and adjust nothing else.
- [ ] **Step 3: Run `dotnet run tools/licenses.cs -- check`** (expect failure: the package is missing from the manifest), then add the entry to `third-party/licenses.json` copying the shape of `bblanchon.PDFium.Linux` (`id` `bblanchon.PDFium.macOS`, same version, `Apache-2.0`, the same `licenseFile` and `licenseFiles`, `files: ["libpdfium.dylib"]`). Run `dotnet run tools/licenses.cs -- generate`, then `-- check` and `-- selftest` (expect both green; if the selftest fixtures name the two packages, add the third there the way the other two are written).
- [ ] **Step 4: Run** `dotnet build Raffica.slnx -c Release` (0 warnings) and `dotnet test tests/Raffica.Core.Tests -c Release --no-build --filter-class "*Licen*"` if such a test class exists (check the count).
- [ ] **Step 5: Commit** `feat: PDFium for macOS (osx-x64 and osx-arm64)` with the files above.

### Task 7: Tesseract for macOS

**Files:**
- Modify: `src/Raffica.Ocr/TesseractLibrary.cs`, `tests/Raffica.Core.Tests/Ocr/OcrTestLibrary.cs`, `tests/Raffica.Pdf.Tests/Ocr/OcrTestLibrary.cs`, `tests/Raffica.Core.Tests/Ocr/TesseractBindingsTests.cs` (only if it names `libtesseract.so`), `tools/check-binary.cs`, `tools/native-fetch.cs`, `packaging/native/versions.txt`, `.github/workflows/native-tesseract.yml`, `third-party/licenses.json` (only if the Tesseract entry lists files per system)
- Create: `packaging/native/build-macos.sh`
- Test: `tests/Raffica.Core.Tests/Ocr/TesseractLibraryNameTests.cs`, `dotnet run tools/check-binary.cs -- selftest`, `dotnet run tools/native-fetch.cs -- selftest`

**Interfaces:**
- Produces:
  - `TesseractLibrary.FileName` returns `tesseract.dll` on Windows, `libtesseract.dylib` on macOS, `libtesseract.so` otherwise; implemented through `internal static string FileNameFor(OperatingSystemKind kind)` with `internal enum OperatingSystemKind { Windows, MacOS, Linux }` so a test can pin all three.
  - `OcrTestLibrary.Find()` (both copies): rid and file name for all three systems (`win-x64`/`tesseract.dll`, `osx-x64` or `osx-arm64` chosen by `RuntimeInformation.ProcessArchitecture`/`libtesseract.dylib`, `linux-x64`/`libtesseract.so`).
  - `check-binary.cs`: the format is detected from the first four bytes (`7F 45 4C 46` ELF; `CF FA ED FE` thin 64-bit Mach-O). New options for Mach-O: `--arch <x86_64|arm64>` (must equal what `lipo -archs` prints for the file), `--max-macos <x.y>` (highest allowed deployment target, read with `vtool -show-build`), `--bundle <folder>` (the folder whose files a `@rpath/`, `@loader_path/` or `@executable_path/` dependency may name). A dependency passes when it starts with `/usr/lib/` or `/System/Library/`, or it is one of the three `@` forms and `<bundle>/<file name>` exists; everything else is a violation (a Homebrew path such as `/opt/homebrew/...` or `/usr/local/...` always fails). Pure parser `MachORules.Violations(string otoolOutput, string lipoArchs, string vtoolOutput, string expectedArch, int[] maxMacos, Func<string, bool> bundleHasFile)` so `selftest` can run it on sample text. `selftest` is a new subcommand that also keeps the ELF rules' behaviour on one sample.
  - `Pins.Libraries` gets two entries: `new("osx-x64", "libtesseract-osx-x64.dylib", "libtesseract.dylib", …)` and `new("osx-arm64", "libtesseract-osx-arm64.dylib", "libtesseract.dylib", …)` (the release asset names must differ, the laid-down name is the same). Until the build exists they carry the placeholder pin (`Size 0`, `Pins.Placeholder`), which `Fetcher` already refuses with a clear message. The macOS entries use their own release constant (`TesseractReleaseMacOS`, tag `native-tesseract-5.5.4-2`), not the `-1` one. `Cli.Run` accepts `osx-x64` and `osx-arm64`; the usage text lists them; the selftest's unknown-RID case stays on `mac`.

- [ ] **Step 1: Look up the minimum macOS version** that .NET 10 and Avalonia 12 support (the supported-OS page of .NET 10, the Avalonia 12 requirements) and write `MACOS_DEPLOYMENT_TARGET=<x.y>` (the higher of the two) into `packaging/native/versions.txt` with a comment naming both sources. Record the two numbers in the commit message; Task 11 puts them into `Docs/DECISIONS.md`. If a source cannot be reached, stop and ask the owner; do not guess.
- [ ] **Step 2: Write the failing tests.** `TesseractLibraryNameTests`: `WindowsName`, `MacOsName`, `LinuxName` through `FileNameFor`, plus `CurrentSystemNameIsOneOfThem`. In `check-binary.cs` write the `selftest` first with these cases and run it (`dotnet run tools/check-binary.cs -- selftest`, expect failure because `MachORules` is missing): `otool` text with only `/usr/lib/libSystem.B.dylib` and `/usr/lib/libc++.1.dylib` passes; a line `/opt/homebrew/opt/libpng/lib/libpng16.16.dylib` fails; `@rpath/libleptonica.dylib` fails when the bundle lacks it and passes when `bundleHasFile` says yes; `lipo` output `arm64` against expected `x86_64` fails; a `minos 15.0` against max `14.0` fails and `minos 13.0` passes. In `native-fetch.cs` extend `SelfTest` with: both macOS entries have distinct `Asset` names and the same `Target`; `Pins.For("osx-arm64")` returns the dylib plus the two tessdata files; a placeholder macOS pin is refused with exit 1 and nothing laid down.
- [ ] **Step 3: Implement** `FileNameFor`, `OcrTestLibrary.Find`, `MachORules` and the format detection in `check-binary.cs` (call `otool -L`, `lipo -archs`, `vtool -show-build` as processes, as `Objdump` does now, with a clear message when a tool is missing), and the `native-fetch.cs` entries and RID list. Run both selftests and the name tests: expect pass.
- [ ] **Step 4: Write `packaging/native/build-macos.sh <repo-root> <out-dir> <x86_64|arm64>`** after `build-linux.sh` (read it first and keep its structure: `versions.txt`, the commit check of both sources, `RAFFICA_NATIVE_SOURCES`). Differences: no apt; `cmake` and `ninja` come from the runner image (`brew install cmake ninja` only if missing); both builds pass `-DCMAKE_OSX_ARCHITECTURES=<arch>` and `-DCMAKE_OSX_DEPLOYMENT_TARGET=$MACOS_DEPLOYMENT_TARGET`; `-DCMAKE_IGNORE_PREFIX_PATH="/opt/homebrew;/usr/local"` so no Homebrew library is picked up; the same switches as Linux (Leptonica static with every image decoder off, `OPENMP_BUILD=OFF`, `DISABLE_CURL=ON`, `DISABLE_ARCHIVE=ON`, `GRAPHICS_DISABLED=ON`, no training tools, no tests); the shared library is linked with `-Wl,-install_name,@loader_path/libtesseract.dylib` and `-Wl,-dead_strip`, then `strip -x`; the result is copied to `<out>/libtesseract.dylib`.
- [ ] **Step 5: Extend `native-tesseract.yml`:** a new dispatch input `systems` (choice `all` or `macos`, default `all`); the `windows` and `linux` jobs run only for `all`; a new `macos` job on `macos-15` with a matrix `arch: [x86_64, arm64]` (asset name `libtesseract-osx-<x64|arm64>.dylib`) that runs `build-macos.sh`, then `dotnet run tools/check-binary.cs -- <file> --arch <arch> --max-macos $MACOS_DEPLOYMENT_TARGET --bundle <out dir>`, then checks the five C API exports with `nm -gU` (`TessBaseAPICreate`, `TessBaseAPIInit3`, `TessBaseAPISetImage`, `TessBaseAPIRecognize`, `TessResultIteratorGetUTF8Text`) and that `nm -gU` shows no `curl_`, `archive_`, `omp_`, `GOMP_` symbols, then uploads the asset and prints size and SHA-256 (`shasum -a 256`, `stat -f %z`) into the job summary in the same table shape. Same pinned action SHAs as the other jobs. Update the file header comment.
- [ ] **Step 6: Run** `dotnet build Raffica.slnx -c Release` (0 warnings), `dotnet test tests/Raffica.Core.Tests -c Release --no-build --filter-class "*TesseractLibraryNameTests"`, `dotnet run tools/native-fetch.cs -- selftest`, `dotnet run tools/check-binary.cs -- selftest`, `dotnet format Raffica.slnx --verify-no-changes`. Expect green.
- [ ] **Step 7: Commit** `feat(ocr): Tesseract for macOS (library name, Mach-O check, build script and job, pins)` and push the branch (`git push -u origin feature/m31-runs-on-the-mac`).
- [ ] **Step 8 (needs the owner's go): build and pin the library.** Ask the owner to confirm creating the prerelease `native-tesseract-5.5.4-2`. After the go: `gh workflow run native-tesseract.yml --ref feature/m31-runs-on-the-mac -f build=2 -f systems=macos` (if GitHub refuses a branch ref, report it and ask; do not merge to `dev` just for this). Wait for the run (`gh run watch`), read both rows of the job summary, write size and SHA-256 into the two `Pins.Libraries` entries, run `dotnet run tools/native-fetch.cs -- --rid osx-arm64 --out artifacts/native/osx-arm64` on any machine to prove the pin (expect "3 files (SHA-256 checked)"), run the selftest again, commit `feat(ocr): pin the macOS Tesseract build native-tesseract-5.5.4-2` with the run id in the message body.

### Task 8: The `build-macos` job

**Files:**
- Modify: `.github/workflows/ci.yml`
- Create: `.github/scripts/macos-paths.sh`, `.github/scripts/macos-paths.test.sh`

**Interfaces:**
- Produces:
  - `macos-paths.sh`: reads file names (one per line) on stdin, exits `0` and prints the first match when any file is platform-near, otherwise exits `1`. Platform-near (spec section 7): `src/*/Platform/*`, `src/Raffica.Pdf/Updating/*`, `src/Raffica.Pdf/Packaging/*`, `src/Raffica.Core/Hosting/*`, `src/Raffica.Pdfium/*`, `src/Raffica.Ocr/*`, `packaging/*`, `tools/package.cs`, `tools/native-fetch.cs`, `tools/check-binary.cs`, `Directory.*.props`, `*.csproj`, `.github/workflows/*`, `.github/scripts/*`. (In a shell `case`, `*` also matches `/`.)
  - `ci.yml` `changes` job output `macos` (`true` when the event is a pull request into `main`, a push to `main`, a pull request whose label list contains `ci:macos`, or `macos-paths.sh` matches the changed files; never for a draft or a documentation-only change) and `labeled` added to the `pull_request` types.
  - Job `build-macos` (`needs: changes`, `if: needs.changes.outputs.code == 'true' && needs.changes.outputs.macos == 'true'`, `runs-on: macos-15`, `timeout-minutes: 45`).
  - The aggregate `ci` job also depends on `build-macos`: skipped counts as passed, any other non-success fails when `macos == 'true'`.

- [ ] **Step 1: Write `macos-paths.test.sh` first** (plain bash, exit 1 on the first wrong answer, prints one line per case): matches `src/Raffica.Pdf/Platform/Windows/X.cs`, `src/Raffica.Core/Hosting/SingleInstance.cs`, `src/Raffica.Pdfium/Native/Fpdf.cs`, `tools/package.cs`, `Directory.Packages.props`, `src/Raffica.Pdf/Raffica.Pdf.csproj`, `.github/workflows/ci.yml`; does not match `src/Raffica.Pdf/Panes/MarksPane.cs`, `src/Raffica.Shell/Styles/Tokens.axaml`, `tests/Raffica.Pdf.Tests/Panes/X.cs`, `Docs/ROADMAP.md`, `README.md`. Run `bash .github/scripts/macos-paths.test.sh` (expect failure: script missing), implement `macos-paths.sh`, run again (expect all lines ok).
- [ ] **Step 2: Edit `changes`** to compute `macos` as above (keep the existing `code` logic untouched), run `macos-paths.test.sh` as a step of `changes`, and add `labeled` to the types.
- [ ] **Step 3: Write the `build-macos` steps** after the Linux and Windows jobs' pattern (same pinned actions, `AVALONIA_TELEMETRY_OPTOUT` comes from the file-level `env`): checkout; `actions/setup-dotnet` with `10.0.x`; Rosetta check (`arch -x86_64 /usr/bin/true || sudo softwareupdate --install-rosetta --agree-to-license`); `dotnet restore`; `dotnet build -c Release --no-restore`; `dotnet run tools/native-fetch.cs -- --rid osx-arm64 --out artifacts/native/osx-arm64` (`GITHUB_TOKEN` from `github.token`); test with `RAFFICA_REQUIRE_OCR=1` and the same command as the other jobs (`dotnet test Raffica.slnx -c Release --no-build --max-parallel-test-modules 1`) with the render-diff upload on failure (`render-diff-macos`); `dotnet run tools/native-fetch.cs -- selftest`; `dotnet run tools/check-binary.cs -- selftest`; publish `src/Raffica.Pdf` and `src/Raffica.Cli` for `osx-arm64` and for `osx-x64` into `out/pdf-<rid>` and `out/cli-<rid>` (fetch the x64 OCR files with `native-fetch --rid osx-x64` first so the x64 publish folder carries them); run `lipo -archs` on each published executable and fail unless it prints `arm64` or `x86_64` as expected; smoke the PDF app (`--print-string TabView --lang de` must print `Ansicht`, `--lang en` must print `View`) natively for arm64 and through `arch -x86_64` for x64; smoke the CLI the same way (`text testdata/pdfs/text-basic.pdf` output contains `Hello,`); the same-file smoke for both architectures: copy `testdata/pdfs/text-basic.pdf` to `$RUNNER_TEMP/Bericht.pdf`, run `raffica rotate "$RUNNER_TEMP/Bericht.pdf" --pages all --angle 90 --output "$RUNNER_TEMP/bericht.pdf"` and fail unless it exits non-zero and prints `same file` (this exercises `FileIdentity` and `stat$INODE64` under Rosetta); `tar czf` the `out/pdf-osx-x64` folder (keeps the executable bit) and upload it as artifact `raffica-pdf-osx-x64` with 3 days retention (the owner's manual check, Task 9 and 11).
- [ ] **Step 4: Update the `ci` aggregate** as described; keep its output line format.
- [ ] **Step 5: Validate offline:** `bash .github/scripts/macos-paths.test.sh`; YAML check with `dotnet run` is not available, so use `python -c "import yaml,sys; yaml.safe_load(open('.github/workflows/ci.yml'))"` if Python exists, otherwise `gh workflow view ci.yml` after the push (a syntax error shows there). Expected: parses.
- [ ] **Step 6: Commit** `ci: build-macos job on macos-15 with its path filter and the ci:macos label` (the three files).

### Task 9: A throwaway `.app` for the owner's manual check

**Files:**
- Create: `packaging/macos/dev-app.sh`
- Modify: `packaging/README.md` (one short section)

**Interfaces:**
- Produces: `packaging/macos/dev-app.sh <publish-dir> <out-dir>` builds `<out-dir>/Raffica PDF.app` with `Contents/MacOS/` holding the whole publish folder (executable `RafficaPdf` first), a minimal `Contents/Info.plist` (`CFBundleIdentifier io.github.benjo-code.raffica.pdf`, `CFBundleExecutable RafficaPdf`, `CFBundleName Raffica PDF`, `CFBundleDocumentTypes` with `com.adobe.pdf` role Editor rank Alternate, `NSHighResolutionCapable`, `LSMinimumSystemVersion` from `packaging/native/versions.txt`), then `codesign --force --deep -s -`. It says in its header that it is only a manual-check aid and that milestone 3.3 replaces it by the real bundle step of `tools/package.cs` (no icon, no version, no `.dmg`). It refuses a publish folder without `RafficaPdf`.

- [ ] **Step 1: Write the script** and run `bash -n packaging/macos/dev-app.sh` plus `shellcheck` if installed (expect clean). Add a section "Manual check on a Mac (M3.1)" to `packaging/README.md`: download the artifact, `tar xzf`, run the script, then (a) double-click a PDF with Open With → the app, (b) `open -a "Raffica PDF.app" file.pdf`, (c) `open -a "Raffica PDF.app" a.pdf b.pdf` while it runs, (d) OCR a scanned page. State that the first launch of an ad-hoc signed app needs "Open Anyway" (`xattr -dr com.apple.quarantine "Raffica PDF.app"` for the download).
- [ ] **Step 2: Commit** `chore(macos): a throwaway app wrapper for the manual Finder check`.

### Task 10: Green on macOS

**Files:** whatever the failures name; every change keeps Windows and Linux behaviour.

**Interfaces:** none. This task is the loop that turns "runs in CI" into "every test passes or is skipped with a named macOS reason".

- [ ] **Step 1: Open the PR** to `dev` (not a draft: CI skips drafts), `gh pr create --base dev`; the diff already touches `.github/workflows/**` so `build-macos` runs; add the label too (`gh pr edit --add-label ci:macos`, create it with `gh label create ci:macos --description "Run the macOS build on this pull request"` if it is missing).
- [ ] **Step 2: Triage each failure by cause, not by test:** (a) code that treats every non-Windows system as Linux (grep the failing area for `OperatingSystem.IsLinux()` and `IsWindows()` branches, for example `DefaultAppServiceTests`, `CupsPrintServiceTests`, `PrintSupportTests`, `AppImageUpdaterTests`, `SignatureStoreTests`, `DocumentSaverTests`) is fixed in the code or the test's guard; (b) a feature that is 3.2 or 3.3 (printing, default app, updates, key tips) is skipped on macOS with `Assert.SkipWhen(OperatingSystem.IsMacOS(), "macOS: <what and which milestone>")`; (c) the render regression corpus (`tests/Raffica.Render.Tests`) compares against one reference set made on Windows: if macOS renders differ beyond the existing tolerance, do not touch the references or the tolerance; skip the affected cases on macOS with the reason `macOS: the shared reference set is made on Windows (PDFium renders differ), see DECISIONS` and record the differences in `Docs/DECISIONS.md` for the owner to judge; (d) anything else is a bug and gets a test and a fix.
- [ ] **Step 3: Every skip has a reason that starts with `macOS:`.** To prove it, list the skipped tests with their reasons from the test output of the macOS job (run the same command once locally on Windows with the detailed output option of the test runner to learn how skip reasons are printed, then use that in a final step of the job that greps skipped lines). A skip without a reason is fixed.
- [ ] **Step 4: Repeat** until `build-macos` is green; each fix is its own commit with `fix:` or `test:`. Record: the green run id, the result line (`total`, `failed`, `skipped` counts), the names of the identity tests (`PathRulesTests`, `FileIdentityTests.OtherSpellingOfTheNameIsTheSameFile`, `RotateCommandTests`), and the artifact name.
- [ ] **Step 5: Also run once on Windows:** `dotnet build Raffica.slnx -c Release`, `RAFFICA_REQUIRE_OCR=1 dotnet test Raffica.slnx -c Release --no-build --max-parallel-test-modules 1`, `dotnet format Raffica.slnx --verify-no-changes` (expect 0 failed, 0 warnings, exit 0) so the macOS fixes did not break the home platform, and wait for the Linux and Windows jobs of the PR.

### Task 11: Owner checks, documentation and wrap-up

**Files:**
- Modify: `Docs/DECISIONS.md`, `CLAUDE.md` (section "macOS"), `packaging/README.md` (the native build and the pin procedure for macOS), `Docs/roadmap.json` and the generated `Docs/ROADMAP.md`

- [ ] **Step 1: Owner's Mac, manual checks (recorded, never ticked from CI).** Give the owner the artifact name and the commands of Task 9. The checks: double-click and `open -a` open the PDF at first start and while the app runs (Task 5 live), and OCR recognizes text in a scanned page. Record the result in the roadmap evidence in the owner's words; if the owner cannot do it before the merge, leave those two criteria unticked and name them as open.
- [ ] **Step 2: Write one `Docs/DECISIONS.md` entry** "Raffica on macOS: native libraries, paths and activation (M3.1)" with: the minimum macOS version and its two sources (Task 7 step 1); `PathRules` and its limits (a case-sensitive macOS volume is treated as case-insensitive by name, the identity checks still decide where a file system answer is asked; Unicode normalization is NFC, nothing else is folded); `stat$INODE64` versus `stat` and why; the pipe-name rule and the 100-byte limit; the activation bridge and that Reopen belongs to 3.2; the macOS Tesseract build (flags, `-2` release, asset names); the Mach-O rules; the `build-macos` triggers and that `labeled` re-runs CI; any render-regression skips of Task 10. Add the pointers to `CLAUDE.md` (one line each: `PathRules`, `FileActivationBridge`, `build-macos`, `packaging/macos/dev-app.sh`, `check-binary` Mach-O mode).
- [ ] **Step 3: Roadmap.** In `Docs/roadmap.json` tick each task of 3.1 as it is finished (keep optional fields out, never `null`), tick the acceptance criteria that were actually checked with their evidence (run id, test names, result line), leave the two Mac criteria unticked unless step 1 was done, set `status` `done` with `completedOn` only when all are ticked, otherwise `in-progress`. Run `dotnet run tools/roadmap.cs -- generate` and `-- check` (expect valid). Scope changes go into the roadmap's "Scope changes": the throwaway `dev-app.sh` (the spec puts `Info.plist` into 3.3, but 3.1's manual check needs a bundle), the `systems` input of `native-tesseract.yml`, and `CacheDirectory`.
- [ ] **Step 4: `CHANGELOG.md`:** no entry (nothing in this milestone changes what a Windows or Linux user sees and there is no macOS release yet); say so in the PR body.
- [ ] **Step 5: Final verification** (commands and results named in the PR): `dotnet build Raffica.slnx -c Release`, `RAFFICA_REQUIRE_OCR=1 dotnet test Raffica.slnx -c Release --no-build --max-parallel-test-modules 1`, `dotnet format Raffica.slnx --verify-no-changes`, `dotnet run tools/roadmap.cs -- check`, `dotnet run tools/licenses.cs -- check`, `build-macos` run id.
- [ ] **Step 6: Merge** the PR to `dev` once every check is green (squash), as `CLAUDE.md` allows; do not delete branches by hand.

## Self-Review

- **Spec coverage:** PDFium both RIDs and licence (Task 6); Tesseract job, pins, `.dylib` names, Mach-O check (Task 7); `~/Library` folders incl. caches (Task 3); case-insensitive identity in workspace, save and job paths, `RecentFiles`, CLI `OutputGuard`, and `FileIdentity` from device and inode (Tasks 1 and 2); single-instance socket length (Task 4); Finder and Dock activation at first start and while running (Task 5, live check Task 11); `build-macos` with path filter, label, aggregate (Task 8); all tests pass or skip with a named macOS reason (Task 10); the two manual acceptance checks (Task 11). The Dock reopen and the life cycle without a window are 3.2 by the spec and are not here.
- **Open points called by this plan, for the owner to veto:** `PathRules` folds Unicode normalization (NFC) on macOS; the macOS Tesseract assets get their own release `native-tesseract-5.5.4-2` through a `systems` input so the Windows and Linux pins stay as they are; a throwaway `dev-app.sh` for the manual check; `labeled` re-runs the whole CI on any label.
- **Needs the owner:** the go for the workflow dispatch (Task 7 step 8); the two checks on the Mac (Task 11 step 1). Neither is verified by CI.
