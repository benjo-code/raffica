# Milestone 3.3 – Package and release – Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Raffica PDF ships on macOS as two ad-hoc-signed `Raffica PDF.app` bundles in two `.dmg` files, offers itself as the default PDF app through LaunchServices, updates itself from the `.dmg`, and has a macOS job in `release.yml`, a README section and recorded speed budgets.

**Architecture:** Platform-neutral logic that Windows can test (bundle and path rules, the swap script's arguments, plist text, file names, manifest kinds, the default-app status rules) plus thin macOS code that calls `hdiutil`, `ditto`, `codesign`, `plutil`, `xattr` and CoreServices through the existing `IProcessRunner`/`IUpdateProcessStarter` seams and one `LibraryImport` seam. The packaging steps are new subcommands of `tools/package.cs` (`macos-app`, `dmg`, `dmg-smoke`) in the style of `appimage`/`appimage-smoke`. The app swaps its own bundle with a detached `/bin/sh` script after it quits.

**Tech Stack:** .NET 10 Native AOT, Avalonia 12.1.5, `LibraryImport` (CoreServices, CoreFoundation), `tools/package.cs` (file-based app), GitHub Actions on hosted `macos-15`, xUnit v3.

**Spec:** `Docs/superpowers/specs/2026-11-09-raffica-macos-design.md` (sections 5.1, 5.7, 5.8, 6, 7 and the 3.3 row of section 8). Roadmap: `Docs/roadmap.json`, milestone `3.3`.

## Global Constraints

- Everything in English; every UI string in `Strings.en.resx` **and** `Strings.de.resx` (German in "Sie"); update strings must pass `NoUiTextOverpromisesUpdates` (no "safe", "secure", "guaranteed", "trusted", "sicher", "garantiert", ...).
- Bundle id `io.github.benjo-code.raffica.pdf`; executable `RafficaPdf`, CLI `raffica` next to it in `Contents/MacOS`; `LSMinimumSystemVersion` is read from `MACOS_DEPLOYMENT_TARGET` in `packaging/native/versions.txt` (today `15.0`), never typed twice.
- Release names: `RafficaPdf-<v>-macos-arm64.dmg`, `RafficaPdf-<v>-macos-x64.dmg`, `SHA256SUMS-macos.txt`; manifest kinds `macos-arm64-dmg`, `macos-x64-dmg`; the manifest `format` stays 1 (older clients skip unknown kinds).
- Ad-hoc signing only (`codesign -s -`), after the bundle is assembled; no notarization, no Developer ID. No universal binary.
- Windows and Linux behavior, file names and the signed test vector `testdata/updates/update-manifest.json` do not change.
- Platform code lives in `src/Raffica.Pdf/Platform/MacOS`, never in UI code; macOS interop is `LibraryImport`, Native AOT safe, **no Objective-C runtime calls**; the app references only Core and Shell.
- Nothing of the new code runs before first use (`StartupCreatesNoPackagingWorkTests` stays green); measurement runs keep the update flow and default-app service off.
- Pool-thread rule and "every `BeginPending()` disposed" apply as in `CLAUDE.md`; no handler reads `Dispatcher.UIThread`.
- A macOS-only skip in the tests has a reason that starts with `macOS:`; a test that cannot run on Windows says why (`Windows: ...`).
- Commits: noreply address, Conventional Commits, no attribution lines, stage files by name, `git show --stat HEAD` after each. Branch `feature/m33-package-and-release` (exists, from `dev`); PR to `dev` with the label `ci:macos`; merged by Claude when every check is green.
- "Done" = `dotnet build Raffica.slnx -c Release`, `RAFFICA_REQUIRE_OCR=1 dotnet test Raffica.slnx -c Release --no-build --max-parallel-test-modules 1`, `dotnet format Raffica.slnx --verify-no-changes`, `dotnet run tools/roadmap.cs -- check`, `dotnet run tools/package.cs -- selftest`, `dotnet run bench/shell-probe/measure.cs -- selftest`, plus `build-macos` green. Whatever only a real Mac answers stays open with its checklist (Task 11).

## Review Focus

Inputs the spec implies but no happy path would catch. Each has its test in the named task.

1. **Spaces and quotes in the bundle path** (`/Applications/Raffica PDF.app`, a user folder with `'`): the swap script and every `hdiutil`/`ditto`/`open` call get arguments, never a composed command line. Task 2 (script test with `a b'c` folders) and Task 3.
2. **An Intel build on an Apple-silicon Mac (Rosetta)** must update with the `x64` image, not the `arm64` one: the form follows the process architecture, not the hardware. Task 1.
3. **Running from the mounted image or a translocated copy:** the app must not try to replace itself, and must say so in both languages instead of failing at a read-only volume. Task 2 and 3.
4. **A mounted image left behind** when the copy, the check or a cancel ends the update early, and a stale mount from an earlier failed try: detach always runs (with `-force` as a second try). Task 3.
5. **`LSCopyDefaultRoleHandlerForContentType` hands the bundle id back in lower case** (and `nil` when no default is set): the status compares ignoring case and treats `nil` as "not default". Task 4.

## File Structure

| File | Responsibility |
|---|---|
| `src/Raffica.Core/Updates/PackageForm.cs`, `UpdateManifest.cs`, `UpdateChecker.cs` | `MacAppArm64`/`MacAppX64`, the two kinds, `Detect` for a bundle, `Pick` |
| `src/Raffica.Pdf/Platform/MacOS/MacBundle.cs` (new) | bundle path and location rules (installed, translocated, mounted image) |
| `src/Raffica.Pdf/Platform/MacOS/MacSwapScript.cs` (new) | the swap script text and its argument list |
| `src/Raffica.Pdf/Platform/MacOS/MacAppUpdater.cs` (new) | `IUpdateApplier` for the `.dmg` |
| `src/Raffica.Pdf/Platform/MacOS/ILaunchServices.cs`, `LaunchServicesNative.cs`, `MacDefaultAppService.cs` (new) | default PDF app |
| `src/Raffica.Pdf/Platform/PlatformUpdateApplier.cs`, `PlatformDefaultAppServices.cs`, `Updating/UpdateServices.cs`, `Updating/UpdateFlow.cs`, `Updating/UpdateWording.cs`, `Resources/Strings.*.resx`, `PdfStrings.cs` | wiring and one new sentence |
| `tools/package.cs` | `macos-app`, `dmg`, `dmg-smoke`, osx native names, optional macOS kinds in `update-manifest`, selftest parts, workflow pins |
| `tools/update-testserver.cs`, `tools/smoke-cli.cs` | the macOS kinds; `libtesseract.dylib` |
| `.github/workflows/release.yml`, `ci.yml`, `measure.yml` | `macos` job and manifest changes; bundle smokes in `build-macos`; `macos-hosted` measure platform |
| `bench/shell-probe/measure.cs` | macOS limits, `--app` and `--dmg` size metrics |
| `src/Raffica.Pdf/Program.cs` (rendering choice) | `RAFFICA_RENDERING` on macOS |
| `packaging/macos/dev-app.sh` | deleted (replaced by `macos-app`) |
| `README.md`, `packaging/README.md`, `Docs/design/icons/README.md`, `CHANGELOG.md`, `CLAUDE.md`, `Docs/DECISIONS.md`, `Docs/roadmap.json` | docs |

---

### Task 1: Core: macOS package forms, manifest kinds and `Pick`

**Files:**
- Modify: `src/Raffica.Core/Updates/PackageForm.cs`, `UpdateManifest.cs`, `UpdateChecker.cs`
- Test: `tests/Raffica.Core.Tests/Updates/PackageFormTests.cs`, `ManifestVerifierTests.cs`

**Interfaces:**
- Produces:
  - `PackageForm` gets two members **appended**: `MacAppArm64`, `MacAppX64` (persisted by name in `UpdateRequest`, so no format bump).
  - `UpdateFileKinds.MacOsArm64Dmg = "macos-arm64-dmg"`, `MacOsX64Dmg = "macos-x64-dmg"`, both added to `All`.
  - `PackageFormDetector.Detect(bool isWindows, string? processPath, Func<string,bool> fileExists, Func<string,string?> env, bool isMacOS = false, bool isArm64 = false)` (the two new parameters are optional, so existing callers and tests compile). On macOS a `processPath` containing `.app/Contents/MacOS/` gives `MacAppArm64` or `MacAppX64` by `isArm64`; any other macOS path (a `dotnet run` build) gives `Unknown`.
  - `PackageFormDetector.BundlePathOf(string? processPath) -> string?` returns the path up to and including the first `.app` segment of a `.app/Contents/MacOS/` path, else null.
  - `UpdateChecker.Pick(manifest, form)` maps `MacAppArm64` to `macos-arm64-dmg` and `MacAppX64` to `macos-x64-dmg`.
- Decision: two forms instead of an architecture argument, so the three callers of `Pick` and `UpdateRequest` do not change; `isArm64` is the **process** architecture (`RuntimeInformation.ProcessArchitecture`), so an Intel build under Rosetta picks the Intel image (Review Focus 2).

- [ ] **Step 1: Write the failing tests.** In `PackageFormTests`: `MacFormFollowsThePathAndTheProcessArchitecture` (Theory: `/Applications/Raffica PDF.app/Contents/MacOS/RafficaPdf` with `isArm64` true gives `MacAppArm64`, false gives `MacAppX64`; a path under `/Users/x/Apps/Raffica PDF.app/...` too); `AMacProcessOutsideABundleIsUnknown` (`/Users/x/src/out/RafficaPdf` gives `Unknown`); `BundlePathOfCutsAtTheFirstAppSegment` (also `null` for null and for a plain path); `PickChoosesTheDmgOfTheArchitecture`; `PickGivesNothingWhenOnlyTheOtherArchitectureIsInTheManifest`; `AnOldManifestWithoutMacKindsGivesNothingForMac`. In `ManifestVerifierTests`: `AManifestWithMacKindsKeepsBothAndOneWithoutStillVerifies` (build with the existing `FileJson` helper; the four-kind manifest must still verify).
- [ ] **Step 2: Run to verify they fail:** `dotnet test tests/Raffica.Core.Tests -c Release --filter-class "*PackageFormTests"`. Expected: compile errors (`MacAppArm64`).
- [ ] **Step 3: Implement** the members above in `PackageForm.cs`, `UpdateManifest.cs` and `UpdateChecker.Pick`. `All` order is Windows, Linux, then macOS arm64, x64.
- [ ] **Step 4: Run** `"*PackageFormTests"`, `"*ManifestVerifierTests"`, `"*ReleaseToolTests"`, `"*UpdateCheckerTests"`, `"*UpdateRequestTests"`. Expected: pass; the vector test `TheManifestOfTheReleaseToolVerifiesInTheApp` is unchanged.
- [ ] **Step 5: Commit** `feat(macos): package forms and manifest kinds for the dmg`.

### Task 2: Bundle location rules and the swap script

**Files:**
- Create: `src/Raffica.Pdf/Platform/MacOS/MacBundle.cs`, `src/Raffica.Pdf/Platform/MacOS/MacSwapScript.cs`
- Test: `tests/Raffica.Pdf.Tests/Platform/MacBundleTests.cs`, `tests/Raffica.Pdf.Tests/Platform/MacSwapScriptTests.cs`

**Interfaces:**
- Produces (no platform attribute; compiled and tested everywhere):
  - `internal enum MacBundleLocation { Installed, Translocated, MountedImage }` and `internal static class MacBundle { public const string Identifier = "io.github.benjo-code.raffica.pdf"; public static string? PathOf(string? processPath) /* PackageFormDetector.BundlePathOf */; public static MacBundleLocation LocationOf(string bundlePath); }`. `Translocated` when the path contains `/AppTranslocation/`; `MountedImage` when it is `/Volumes/<name>/<App>.app` (the bundle directly at a volume root; a deeper path on an external drive is `Installed`); else `Installed`.
  - `internal static class MacSwapScript { public const string Text; public static IReadOnlyList<string> Arguments(int processId, string oldBundle, string newBundle, string backupBundle, string opener, string verifier, bool restoreSession); }`. `Arguments` returns `["-c", Text, "sh", pid, old, new, backup, opener, verifier, restore("1"/"0")]`; every value is a positional parameter (`"$1"`..`"$7"`), never part of the script text.
- Script behavior (`Text`, POSIX sh): wait up to 30 s while `kill -0 "$1"` succeeds; `"$6" --verify --deep --strict "$3"` (`$6` is `/usr/bin/codesign` in production); on a failed verify remove `$3`, reopen the old bundle and exit 1; `mv "$2" "$4"`, failure reopens the old bundle; `mv "$3" "$2"`, failure moves `$4` back and reopens; on success `rm -rf "$4"` and `"$5" "$2"` (plus `--args --restore-session` when `$7` is `1`). `$5` is `/usr/bin/open` in production.

- [ ] **Step 1: Write the failing tests.** `MacBundleTests`: `LocationOfTellsInstalledTranslocatedAndMounted` (Theory: `/Applications/Raffica PDF.app` Installed; `/private/var/folders/xx/T/AppTranslocation/ABC/d/Raffica PDF.app` Translocated; `/Volumes/Raffica PDF/Raffica PDF.app` MountedImage; `/Volumes/Data/Apps/Raffica PDF.app` Installed). `MacSwapScriptTests` (skip on Windows with `Windows: needs /bin/sh`; use real temp folders named `a b'c`, a recording opener script and `/usr/bin/true` or `/usr/bin/false` as verifier): `TheNewBundleReplacesTheOldAndIsOpenedWithTheRestoreFlag` (old content gone, new content in place, backup removed, opener got the old path and `--args --restore-session`), `NoRestoreFlagWhenNoSessionWasWritten`, `AFailedVerifyKeepsTheOldBundleRemovesTheNewAndReopensTheOld`, `AFailedMoveRestoresTheBackup`, `TheScriptWaitsForTheProcessToEnd` (start `sleep 1`, assert the swap happens after it ended), `PathsWithSpacesAndQuotesAreArgumentsNotCode` (all of the above run with the `a b'c` names).
- [ ] **Step 2: Run to verify they fail:** `dotnet test tests/Raffica.Pdf.Tests -c Release --filter-class "*MacBundleTests"`. Expected: compile errors.
- [ ] **Step 3: Implement** both types as above.
- [ ] **Step 4: Run** both classes (the script tests skip on Windows with their reason; they run on `build-macos` and on Linux).
- [ ] **Step 5: Commit** `feat(macos): bundle location rules and the swap script`.

### Task 3: `MacAppUpdater` and the update wiring

**Files:**
- Create: `src/Raffica.Pdf/Platform/MacOS/MacAppUpdater.cs`
- Modify: `src/Raffica.Pdf/Platform/PlatformUpdateApplier.cs`, `src/Raffica.Pdf/Updating/UpdateServices.cs`, `src/Raffica.Pdf/Updating/UpdateFlow.cs` (`CanInstall`), `src/Raffica.Pdf/Updating/UpdateWording.cs`, `src/Raffica.Pdf/Resources/Strings.en.resx`, `Strings.de.resx`, `src/Raffica.Pdf/PdfStrings.cs`
- Test: `tests/Raffica.Pdf.Tests/Platform/MacAppUpdaterTests.cs` (template: `AppImageUpdaterTests`, `ApplierKit`), `PlatformUpdateApplierTests.cs`, `Updating/UpdateFlowTests.cs`, `Updating/UpdateWordingTests.cs`, `Platform/UpdateApplierSupportTests.cs`

**Interfaces:**
- Consumes: `MacBundle`, `MacSwapScript` (Task 2); `PackageForm.MacApp*` (Task 1); `UpdateApplierSupport.VerifyAsync/WriteFailure/DeleteSession/TryDeleteDirectory/HasSession`, `IProcessRunner.RunAsync`, `IUpdateProcessStarter.Start`.
- Produces:
  - `internal sealed record MacAppUpdaterSetup(string BundlePath, PackageForm Form, string ConfigDirectory, IReadOnlyList<UpdateKey> Keys, UpdateVersion Running, int ProcessId, IProcessRunner Runner, IUpdateProcessStarter Starter, Action Shutdown)` and `internal sealed class MacAppUpdater(MacAppUpdaterSetup setup) : IUpdateApplier`.
  - `UpdateApplyProblem.RunsFromDownload` and the resx key `UpdateFailureRunsFromDownload` (en and de, mapped in `UpdateWording.ForApplier`). English: "Raffica is running from the downloaded disk image or from a copy macOS moved aside, so it cannot replace itself. Move Raffica PDF to the Applications folder, start it from there and check for updates again, or get the new version from the download page." German in the "Sie" form.
  - `PlatformUpdateApplier.Create` internal overload gets two **trailing optional** parameters `bool isMacOS = false, IProcessRunner? runner = null`; the public `Create` passes `OperatingSystem.IsMacOS()`. Both mac forms on macOS with a bundle path give a `MacAppUpdater`; anything else stays null.
  - `UpdateServices.Create` detects with `isMacOS: OperatingSystem.IsMacOS()` and `isArm64: RuntimeInformation.ProcessArchitecture == Architecture.Arm64`; `TargetDirectory` is the parent of the bundle on macOS. `UpdateFlow.CanInstall` adds the two forms.
- `ApplyAsync` sequence (all external programs through `Runner` with arguments, never a shell string; timeouts attach 60 s, ditto 120 s, codesign 60 s):
  1. Wrong form or `LocationOf` is `Translocated` or `MountedImage`: `Fail(RunsFromDownload, offersDownloadPage: true)` before anything is touched.
  2. `UpdateApplierSupport.VerifyAsync(staged.Directory, ConfigDirectory, Keys, Running, Form, staged.Version, holdFile: false, ct)`; a problem deletes the session and fails with it.
  3. Create the staged bundle's folder entry next to the installed bundle (`<parent>/.Raffica PDF update.app`; backup name `<parent>/.Raffica PDF previous.app`); an `IOException` or `UnauthorizedAccessException` gives `UpdateApplierSupport.WriteFailure(exception, parent)` (`FolderWriteProtected`, download page).
  4. Detach a stale mount (`hdiutil detach <staged.Directory>/mnt -force`, result ignored) and delete a stale staged or backup bundle.
  5. `/usr/bin/hdiutil attach -readonly -nobrowse -noautoopen -noverify -mountpoint <staged.Directory>/mnt <file>`; non-zero gives `InstallFailed`. The mount point is explicit, so no output is parsed.
  6. Exactly one `*.app` at the root of the mount, else `InstallFailed`. `/usr/bin/ditto <mount>/<App>.app <staged bundle>`.
  7. **Always**, on every path after step 5: `hdiutil detach <mount>`, and `-force` once if that failed (Review Focus 4).
  8. `/usr/bin/codesign --verify --deep --strict <staged>` (failure: `Rejected`); `plutil -extract CFBundleShortVersionString raw -o - <staged>/Contents/Info.plist` equals `staged.Version` and `CFBundleIdentifier` equals `MacBundle.Identifier` (failure: `VersionMismatch`).
  9. Only now: `xattr -p com.apple.quarantine <staged>`; exit 0 means present, then `xattr -dr com.apple.quarantine <staged>` (spec 5.8).
  10. `Starter.Start("/bin/sh", MacSwapScript.Arguments(pid, BundlePath, staged, backup, "/usr/bin/open", "/usr/bin/codesign", UpdateApplierSupport.HasSession(config)))`; `false` gives `NotStarted`.
  11. `TryDeleteDirectory(staged.Directory)`, return `ApplyResult.Restarting`, then `Shutdown()` in try/catch. Cancellation is honored only before step 10 and cleans up the staged and mounted state; every failure path deletes the session and the staged bundle.

- [ ] **Step 1: Write the failing tests.** `MacAppUpdaterTests` (a `FakeRunner` whose `ditto` outcome creates the staged folder, real temp folders): `TheHappyPathMountsCopiesVerifiesSwapsAndShutsDown` (assert the exact call order and the arguments, including the explicit mount point, and that the script got `restore=1` when a session exists); `ATranslocatedOrMountedBundleIsRefusedBeforeAnyCall` (no runner call, `OffersDownloadPage`, message equals `RunsFromDownload` wording); `AWriteProtectedParentFailsWithTheFolderAndOffersTheDownloadPage`; `AFailureAfterTheMountAlwaysDetaches` (ditto fails, codesign fails, version mismatch; detach called each time; a failing detach is retried with `-force`); `AStaleMountIsDetachedFirst`; `AStagedFileThatNoLongerMatchesTheSignedManifestTouchesNothing`; `ANonZeroAttachIsInstallFailedAndDeletesTheSession`; `AMountWithoutExactlyOneAppIsRefused`; `TheQuarantineAttributeIsRemovedOnlyAfterTheChecksAndOnlyWhenPresent`; `ACodesignOrVersionMismatchNeverReachesTheSwap`; `AScriptThatCannotBeStartedIsWordedAndKeepsTheApp`; `PathsWithSpacesAreOneArgumentEach` (bundle `/Applications/My Raffica PDF.app`). `PlatformUpdateApplierTests`: `TheMacFormsGetTheMacUpdaterOnlyOnMacOsInsideABundle`. `UpdateFlowTests`: `TheMacFormsCanInstall`. `UpdateWordingTests`/`UpdateApplierSupportTests`: the existing enum-walking guards must pass for `RunsFromDownload` in both languages (no `{`, en differs from de, no banned words).
- [ ] **Step 2: Run to verify they fail:** `dotnet test tests/Raffica.Pdf.Tests -c Release --filter-class "*MacAppUpdaterTests"`. Expected: compile errors.
- [ ] **Step 3: Implement** the sequence above, the wording and the wiring.
- [ ] **Step 4: Run** `"*MacAppUpdaterTests"`, `"*PlatformUpdateApplierTests"`, `"*UpdateFlowTests"`, `"*UpdateWordingTests"`, `"*UpdateApplierSupportTests"`, `"*UpdateServicesTests"`, `"*StartupCreatesNoPackagingWorkTests"`, `"*TranslationTests"`, `"*GermanRegisterTests"`. Expected: pass.
- [ ] **Step 5: Commit** `feat(macos): self-update from the dmg`.

### Task 4: Default app through LaunchServices

**Files:**
- Create: `src/Raffica.Pdf/Platform/MacOS/ILaunchServices.cs`, `LaunchServicesNative.cs`, `MacDefaultAppService.cs`
- Modify: `src/Raffica.Pdf/Platform/PlatformDefaultAppServices.cs`
- Test: `tests/Raffica.Pdf.Tests/Platform/MacDefaultAppServiceTests.cs`, `DefaultAppServiceTests.cs` (the last test)

**Interfaces:**
- Produces:
  - `internal interface ILaunchServices { string? DefaultHandlerOf(string contentType); int SetDefaultHandler(string contentType, string bundleId); }`.
  - `[SupportedOSPlatform("macos")] internal sealed class LaunchServicesNative : ILaunchServices` over `LibraryImport` of CoreServices `LSCopyDefaultRoleHandlerForContentType(nint, uint) -> nint` and `LSSetDefaultRoleHandlerForContentType(nint, uint, nint) -> int` (roles `kLSRolesAll`, `0xFFFFFFFF`) and CoreFoundation `CFStringCreateWithCString`, `CFStringGetCString`, `CFRelease`; every created `CFString` is released.
  - `internal sealed class MacDefaultAppService : IDefaultAppService` with ctor `(ILaunchServices services, string? bundlePath)`. `IsSupported` is `bundlePath is not null && MacBundle.LocationOf(bundlePath) == Installed`. Content type `com.adobe.pdf`, bundle id `MacBundle.Identifier`. `GetStatusAsync`: `null` handler gives `NotDefault`; equal ignoring case gives `IsDefault`; other gives `NotDefault`; an exception gives `Unknown`. `MakeDefaultAsync`: a non-zero status gives `Failed`; after a zero status the default is read again and equal gives `Done`, else `Failed`. Both run in `Task.Run`.
  - `PlatformDefaultAppServices.Create` gets `if (OperatingSystem.IsMacOS()) return new MacDefaultAppService(new LaunchServicesNative(), MacBundle.PathOf(Environment.ProcessPath));` before the stub.
- Decision (spec 5.7, "the plan names which"): the **C API** (`LSSetDefaultRoleHandlerForContentType`). It is deprecated since macOS 12; that it still works on macOS 15 is an assumption recalled from Apple's headers and not verified in this session. NSWorkspace would need the Objective-C runtime, which this milestone forbids. If the owner's check (Task 11) shows the call fails or the Finder ignores it, the fallback is a new backlog entry for an NSWorkspace-based service (an Objective-C use that its own plan must name); nothing else changes because the service sits behind `IDefaultAppService`. No new strings: the card and notes are generic and `Done`, `Failed` exist.

- [ ] **Step 1: Write the failing tests** in `MacDefaultAppServiceTests` with a fake `ILaunchServices`: `NoHandlerIsNotDefault`; `TheHandlerComparesIgnoringCase` (fake returns `IO.GITHUB.BENJO-CODE.RAFFICA.PDF` lower- and mixed-case); `AnotherHandlerIsNotDefault`; `AnExceptionIsUnknown`; `MakeDefaultIsDoneOnlyWhenTheReadBackMatches`; `ANonZeroStatusIsFailedAndAReadBackThatDiffersIsFailed`; `NotSupportedOutsideAnInstalledBundle` (null path, translocated path, mounted-image path); `TheContentTypeAndBundleIdArePassedExactly`; `LaunchServicesAnswersOnMacOs` (real `LaunchServicesNative.DefaultHandlerOf("com.adobe.pdf")` does not throw and returns a non-empty id on a stock Mac; `Assert.Skip` elsewhere with a reason). Change the last test of `DefaultAppServiceTests`, `TheFactoryNeverReturnsNullAndTheStubIsNotSupportedWhereThereIsNoService`: on macOS a dev build (no bundle) is `IsSupported == false`, so the expectation becomes `OperatingSystem.IsWindows() || OperatingSystem.IsLinux()` for non-bundle runs; assert the new rule in `MacDefaultAppServiceTests`.
- [ ] **Step 2: Run to verify they fail:** `dotnet test tests/Raffica.Pdf.Tests -c Release --filter-class "*MacDefaultAppServiceTests"`. Expected: compile errors.
- [ ] **Step 3: Implement** as above.
- [ ] **Step 4: Run** `"*MacDefaultAppServiceTests"`, `"*DefaultAppServiceTests"`, `"*DefaultAppFlowTests"`, `"*StartupCreatesNoPackagingWorkTests"`. Expected: pass.
- [ ] **Step 5: Commit** `feat(macos): default PDF app through LaunchServices`.

### Task 5: `package.cs macos-app`: the bundle, the icon and the signature

**Files:**
- Modify: `tools/package.cs` (`Cli.Commands`, `Usage`, header comment, `Layout.NativeLibrary`, `NativeFiles.For`, new `MacApp` class, new selftest part `MacAppParts` listed in `SelfTest.Run`)
- Delete: `packaging/macos/dev-app.sh`
- Modify: every reference to `dev-app.sh` (`rg dev-app.sh`: `packaging/README.md`, `CLAUDE.md`, `Docs/`)

**Interfaces:**
- Produces: `package.cs macos-app --rid <osx-arm64|osx-x64> --app <publish dir> --cli <publish dir> --out <dir> --version <x.y.z> [--native <dir>] [--repo <dir>]` writing `<out>/Raffica PDF.app`. macOS only (anywhere else it prints why and returns 2); an unknown rid returns 2.
  - `MacApp.BundleName = "Raffica PDF.app"`; `MacApp.InfoPlist(string version, string minSystemVersion) -> string` (pure); `MacApp.MinSystemVersion(string versionsFileText) -> string` (reads `MACOS_DEPLOYMENT_TARGET`, throws on a missing key); `MacApp.Arch(string rid)` returns `arm64` or `x64`.
  - `Layout.NativeLibrary("osx-*")` is `libpdfium.dylib`; `NativeFiles.For("osx-*")` is `libtesseract.dylib` plus tessdata. `layout` itself stays Windows and Linux only.
- `Info.plist` content: `CFBundleIdentifier`, `CFBundleExecutable` `RafficaPdf`, `CFBundleName` and `CFBundleDisplayName` `Raffica PDF`, `CFBundlePackageType` `APPL`, `CFBundleShortVersionString` and `CFBundleVersion` the version, `CFBundleIconFile` `RafficaPdf`, `NSHighResolutionCapable` true, `LSApplicationCategoryType` `public.app-category.productivity`, `LSMinimumSystemVersion` from `versions.txt`, `CFBundleDocumentTypes` for `com.adobe.pdf` (role Editor, `LSHandlerRank` Alternate). Only stable `X.Y.Z` versions are accepted (a prerelease tag has no numeric plist form; the command returns 2 for it).
- Layout inside the bundle: the app publish folder (without `.pdb`, `.dbg`, `.dSYM`) and the CLI `raffica` in `Contents/MacOS`; the CLI's native files must be byte-identical to the app's (else exit 1, no silent overwrite); `--native` adds `NativeFiles.For(rid)` and refuses to overwrite OCR files already present (as `layout` does); `LICENSE` and `THIRD-PARTY-NOTICES.txt` in `Contents/Resources`; `Contents/Resources/RafficaPdf.icns` made from `Docs/design/icons/brand/pdf.png` with `sips -z` into an `.iconset` (16, 32, 128, 256, 512 at 1x and 2x) and `iconutil -c icns` (no binary in the repo). Modes 0755 for `RafficaPdf`, `raffica`, 0644 for the rest. `plutil -lint` on the plist.
- Signing (CLAUDE.md, macOS): every Mach-O (`RafficaPdf`, `raffica`, each `*.dylib`) gets `codesign --force -s -`, then the bundle `codesign --force -s -`, then `codesign --verify --deep --strict` must pass, else exit 1.
- Risk, stated so it is checked rather than assumed: tessdata and the dylibs stay in `Contents/MacOS` (the app finds them next to its executable; the spec leaves the place to the builder). If `codesign --verify --deep --strict` rejects the data files there, move tessdata to `Contents/Resources` behind a seam in `ShippedTessdataDirectory` and record it; the verify step in this task is what decides. **Amended after the first CI run (42843259224):** the risk materialized (the bundle signature failed on `Contents/MacOS/tessdata`); tessdata is now in `Contents/Resources/tessdata` and `TessdataStore.ShippedDirectoryFor` has a bundle rule, see the decision "Raffica on macOS: packaging, default app and updates (M3.3)".

- [ ] **Step 1: Write the failing selftest checks** in `MacAppParts` (everywhere): `InfoPlistHasTheKeysAndValues` (parse as XML; assert bundle id, executable, category, `NSHighResolutionCapable` true, `LSMinimumSystemVersion` `15.0`, `com.adobe.pdf` with Editor and Alternate, both version keys `0.1.0`, icon file); `MinSystemVersionReadsTheRealVersionsFile` (`packaging/native/versions.txt` gives `15.0`; a text without the key throws); `PrereleaseVersionIsRefused` (`macos-app ... --version 0.2.0-rc.1` returns 2); `UnknownRidReturns2`; `OffMacOsReturns2` (Windows/Linux only); `OsxRidsHaveNativeNames` (`NativeFiles.For`, `Layout.NativeLibrary`); `MacOsRealRun` (macOS only: fake publish folders where `RafficaPdf`, `raffica` and `libpdfium.dylib` are copies of `/usr/bin/true`, a tessdata folder, the real brand PNG; assert plist, icns, executable bits, `codesign --verify --deep --strict` passes, and a second CLI folder whose `libpdfium.dylib` differs makes it exit 1).
- [ ] **Step 2: Run to verify they fail:** `dotnet run tools/package.cs -- selftest`. Expected: failing or missing `macos-app`.
- [ ] **Step 3: Implement** `MacApp`, the osx names and the command; delete `dev-app.sh` and fix its references.
- [ ] **Step 4: Run** `dotnet run tools/package.cs -- selftest` on Windows (pure parts pass, the macOS run is skipped with its reason) and look at the count; the real run is proven by Task 8's `build-macos` step.
- [ ] **Step 5: Commit** `feat(macos): the app bundle step of the packaging tool`.

### Task 6: `package.cs dmg` and `dmg-smoke`

**Files:**
- Modify: `tools/package.cs` (commands, `Dmg`, `DmgSmoke`, selftest part `DmgParts`)

**Interfaces:**
- Produces:
  - `Dmg.FileName(string version, string rid) -> string`: `RafficaPdf-<v>-macos-arm64.dmg` or `...-macos-x64.dmg`.
  - `package.cs dmg --app <Raffica PDF.app> --rid <rid> --version <x.y.z> --out <file>`: stages the app (via `ditto`) and a symlink `Applications` to `/Applications` in a temp folder, then `hdiutil create -volname "Raffica PDF" -srcfolder <stage> -fs HFS+ -format UDZO -ov <out>` and `hdiutil verify`. The `--out` file name must equal `Dmg.FileName` (as `appimage` requires), else exit 2; macOS only; no background picture.
  - `package.cs dmg-smoke --dmg <file> --rid <rid> [--ceiling-seconds 5]` in the `Check`/`Finish` style of `AppImageSmoke`: attach read-only and hidden at an explicit temp mount point; the image holds exactly `Raffica PDF.app` and `Applications` linking to `/Applications`; copy the app with `ditto`; detach (also on failure); `codesign --verify --deep --strict` on the copy; `lipo -archs` of `RafficaPdf`, `raffica` and every `*.dylib` that is not universal equals `arm64` or `x86_64` for the rid; start `RafficaPdf --print-string TabView --lang de` printing `Ansicht` and `--lang en` printing `View` (an `osx-x64` image on an arm64 host runs under `arch -x86_64`), each within the ceiling. The ceiling of **5 seconds** is the coarse "starts within a few seconds" value of spec section 6; it catches gross faults such as an Intel build that cannot run, not speed.
- The Mach-O dependency check is **not** inside `dmg-smoke`; it is its own workflow step on the unpacked bundle (`tools/check-binary.cs`, Mach-O mode with `--bundle`, `--arch`, `--max-macos`; read the file header for the exact switches), so a failing dependency names its file.

- [ ] **Step 1: Write the failing selftest checks** in `DmgParts`: `DmgFileNames` (both rids, other rid returns 2); `AWrongOutNameReturns2`; macOS only: `ADmgOfAFakeBundleHoldsTheAppAndTheLink` (bundle from Task 5's fake publish; create the image, attach, list, detach) and `TheSmokeFailsForABinaryThatDoesNotPrintTheString` (the fake `RafficaPdf` is `/usr/bin/true`, so the smoke must exit 1 and still detach).
- [ ] **Step 2: Run to verify they fail:** `dotnet run tools/package.cs -- selftest`.
- [ ] **Step 3: Implement** both commands after `AppImageSmoke` (before `TagRules`), reusing `Options.Parse`, `Tool.Run`, `Sums.Line`.
- [ ] **Step 4: Run** `dotnet run tools/package.cs -- selftest` (Windows: pure parts pass; macOS parts are proven by Task 8).
- [ ] **Step 5: Commit** `feat(macos): disk image and disk image smoke`.

### Task 7: Manifest tool, test server and CLI smoke know macOS

**Files:**
- Modify: `tools/package.cs` (`UpdateManifestRules.Kinds`, `KnownEndings`, `Problem`, `update-manifest`, selftest `UpdateReleaseParts`), `tools/update-testserver.cs`, `tools/smoke-cli.cs`

**Interfaces:**
- Produces:
  - `UpdateManifestRules.Kinds` entries get a `Required` flag: the four existing ones are required, `macos-arm64-dmg` (`RafficaPdf-<v>-macos-arm64.dmg`) and `macos-x64-dmg` (`RafficaPdf-<v>-macos-x64.dmg`) are optional **as a pair** (both or neither, otherwise `update-manifest` fails with a message naming the missing one). `KnownEndings` gains `-macos-arm64.dmg` and `-macos-x64.dmg`. The output keeps `Kinds` order, so a four-kind run is byte-identical to today and `testdata/updates/update-manifest.json` and its `.sig` stay as they are.
  - `update-manifest --sums` takes a third file (`SHA256SUMS-macos.txt`, both `.dmg` lines in one file).
  - `update-testserver.cs` kind table gains both kinds; `smoke-cli.cs` chooses `libtesseract.dylib` on macOS (today an `.so` check would silently skip the real OCR run).

- [ ] **Step 1: Write the failing selftest checks:** `ManifestWithThreeSumsHasSixKindsInOrder`; `ManifestWithTwoSumsIsByteIdenticalToTheVector` (the existing byte compare stays); `OnlyOneMacArchitectureIsRefused`; `AMacDmgOfAnotherVersionIsListedAsNotBelongingToTheVersion`; `TheTestServerPreparesMacDmgs` (in `update-testserver.cs selftest`).
- [ ] **Step 2: Run to verify they fail:** `dotnet run tools/package.cs -- selftest` and `dotnet run tools/update-testserver.cs -- selftest`.
- [ ] **Step 3: Implement** the three edits.
- [ ] **Step 4: Run** both selftests, `dotnet run tools/smoke-cli.cs` is exercised by the workflows (state that in the commit body, it needs a binary), and `dotnet test tests/Raffica.Core.Tests -c Release --filter-class "*ReleaseToolTests"` (expect pass).
- [ ] **Step 5: Commit** `feat(macos): update manifest, test server and CLI smoke for the dmg`.

### Task 8: Workflows: `release-macos`, the manifest job and the PR smokes

**Files:**
- Modify: `.github/workflows/release.yml`, `.github/workflows/ci.yml`, `tools/package.cs` (workflow pins in `ManifestJobParts`, `LinuxContainerParts`, `WorkflowYamlParts`, `PublicRepoWorkflowParts`)

**Interfaces:**
- Produces in `release.yml`: job id `macos`, name `release-macos`, `needs: preflight`, `runs-on: macos-15` (hosted, arm64; the x64 build is published for `osx-x64` on the same runner and smoked under Rosetta), `permissions: contents: write`, `timeout-minutes: 60`, env `TAG`, `REF_INPUT`, `UPLOAD`, the same preamble as the Linux job (tag and ref rules, "Require the verified commit", checkout, fresh SDK and package folders, setup-dotnet), and `outputs.sums_sha256` from a step `id: sums`. Steps after the preamble: check Rosetta (as `build-macos`); `package.cs selftest` (step name `Self-test of the packaging tool`); `licenses.cs selftest`; `native-fetch.cs` for `osx-arm64` and `osx-x64` (with `GITHUB_TOKEN` like the Windows job); then a loop over both rids: publish CLI and app (`-r <rid> -p:Version=$VERSION`), `licenses.cs check --publish-dir`, `package.cs macos-app`, the Mach-O check on the bundle, `smoke-cli.cs` against the bundle's `raffica` (real OCR run), `package.cs dmg`, `package.cs dmg-smoke`; `package.cs sums <both dmgs> --out out/release/SHA256SUMS-macos.txt`; upload (`if: env.UPLOAD == 'true'`, `gh release upload ... --clobber`); the report step.
- `manifest` job: `needs: [preflight, windows, linux, macos]`, its `if` gains `needs.macos.result == 'success'`, the sums download gets `--pattern SHA256SUMS-macos.txt`, `sums-verify` runs a third time with `MACOS_SUMS_SHA256`, `update-manifest --sums` gets the third file.
- `ci.yml` `build-macos`: after "Smoke test of the CLI binary" add "Self-test of the packaging tool" (`package.cs selftest`) and "Bundle, image and smoke (both architectures)" (`macos-app`, Mach-O check, `dmg`, `dmg-smoke` for each rid from the already published `out/pdf-<rid>` and `out/cli-<rid>`, version `0.0.0`); replace the packed publish folder artifact by the Intel image `raffica-pdf-macos-x64-dmg` (3 days) for the owner's check.
- Selftest pins to change: `^      TAG:` count 4 to 5, `^      UPLOAD:` count 3 to 4, `name: Self-test of the packaging tool` count 3 to 4, the manifest `needs` string and `if` text, the report-step shape loop (add `macos`), the `ref` first-step check list (add `macos`); new pins: the `macos` job contains `macos-app`, `dmg`, `dmg-smoke` and `SHA256SUMS-macos.txt`, and the manifest passes three sums files.
- `.github/scripts/macos-paths.sh` already matches `tools/package.cs`, `packaging/*` and every workflow; no change (state this in the commit body).

- [ ] **Step 1: Write the failing pins** in `tools/package.cs` selftest (the new and changed counts above) so that the selftest fails against today's workflow files.
- [ ] **Step 2: Run to verify they fail:** `dotnet run tools/package.cs -- selftest`. Expected: the workflow pins fail.
- [ ] **Step 3: Edit** `release.yml` and `ci.yml` as above (one-line `name`, `if` and `run` values contain no unquoted `: `).
- [ ] **Step 4: Run** `dotnet run tools/package.cs -- selftest` (expect pass) and a YAML parse of both files (`python -c "import yaml,sys; yaml.safe_load(open(sys.argv[1]))" <file>` or the repo's `actionlint` if installed). The jobs themselves are proven in Task 12.
- [ ] **Step 5: Commit** `ci(macos): macOS job in release.yml and bundle smokes in build-macos`.

### Task 9: Measuring: macOS limits, `.app` and `.dmg` sizes

**Files:**
- Modify: `bench/shell-probe/measure.cs` (`CheckCommand.s_pdfBudgets`, `PdfHasNoLimits`, `SizeCommand`, `SelfTest`)

**Interfaces:**
- Produces:
  - `size` accepts `--app <bundle dir>` and `--dmg <file>`; they add the metrics `app` (bundle bytes, MiB) and `dmg` (file bytes, MiB) to the size table and to the CSV rows; with `--check` they are judged only when given. The metrics exist in the PDF table for macOS only.
  - The PDF `macos` entry is **no longer exempt**: `PdfHasNoLimits("macos")` is true when the table is empty (exit 2), and `Judge` gives `indicative` only for the **gallery** target on macOS. Time and memory limits of `s_pdfBudgets["macos"]` bind only the owner's Intel Mac (`measure.yml`, platform `macos`); size limits bind both architectures through the one `macos` entry (budget from the larger of the two measured medians, Task 12).
  - Until Task 12 fills the numbers, the real entry stays empty; the selftest pins the mechanism through `PdfTableForTests`.
- [ ] **Step 1: Write the failing selftest checks:** `size --check osx-arm64 ... --app ... --dmg ...` with a test table judges `app` and `dmg`; a missing `--dmg` skips only that row; an empty macOS PDF table exits 2; the gallery on macOS is still `indicative`; update the pinned lines that assumed `PdfTable["macos"].Count == 0` and the macOS `indicative` PDF verdicts.
- [ ] **Step 2: Run to verify they fail:** `dotnet run bench/shell-probe/measure.cs -- selftest`.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** `dotnet run bench/shell-probe/measure.cs -- selftest` (expect pass) and `dotnet build bench/shell-probe` if it has a project.
- [ ] **Step 5: Commit** `feat(bench): macOS limits and bundle and image sizes in the measure tool`.

### Task 10: Rendering choice on macOS and the hosted Apple-silicon record

**Files:**
- Modify: `src/Raffica.Pdf/Program.cs` (`BuildAvaloniaApp`), the `RenderingChoiceParser` file, `.github/workflows/measure.yml`
- Test: `tests/Raffica.Pdf.Tests/Hosting/RenderingChoiceParserTests.cs` (or the existing parser test class)

**Interfaces:**
- Produces:
  - On macOS `RAFFICA_RENDERING=software` selects Avalonia's software rendering for the app (the macOS platform options type and its software mode are looked up in Avalonia 12.1.5; if the mode does not exist there, record that in the decision entry, keep macOS on Avalonia's default and skip the comparison). Unset, empty, `default` or anything else keeps Avalonia's own path, **unchanged from today**; Windows and Linux behavior is untouched. The default is switched only in Task 12, from the measurement. A pure `RenderingChoice` helper, not the Avalonia options, is what the tests pin.
  - `measure.yml`: the input `rendering` also applies on macOS (the steps pass `RAFFICA_RENDERING` to the app on macOS as on Windows); `platform` gets a fourth choice `macos-hosted` that runs on `macos-15` (hosted, arm64) with RID `osx-arm64`, needs `setup-dotnet` instead of the self-hosted `DOTNET_INSTALL_DIR`, produces the CSVs and the summary, uploads `measure-macos-hosted-<run_id>`, and has **no check step** (recorded, not judged, spec section 6). The implementer reads every use of `inputs.platform` in the file and treats `macos-hosted` like `macos` except for the runner, the RID, the dotnet setup and the missing check; the concurrency group keeps platforms apart.
- [ ] **Step 1: Write the failing tests:** `SoftwareIsChosenOnMacOnlyWhenAskedFor` (`software` in any case gives the software choice for macOS; `default`, empty, unset and `nonsense` give the default choice; Windows keeps its existing mapping); `LinuxIsUntouchedByTheVariable`.
- [ ] **Step 2: Run to verify they fail:** `dotnet test tests/Raffica.Pdf.Tests -c Release --filter-class "*RenderingChoiceParser*"`.
- [ ] **Step 3: Implement** the helper and the call in `BuildAvaloniaApp`, then the `measure.yml` edits.
- [ ] **Step 4: Run** the parser tests and the `Program`/`App` startup tests that exist (`--filter-class "*StartupCreates*"`); parse `measure.yml` as YAML.
- [ ] **Step 5: Commit** `feat(macos): RAFFICA_RENDERING on macOS and a hosted macOS measure platform`.

### Task 11: Docs: README, checklists, icons, changelog, project memory

**Files:**
- Modify: `README.md`, `packaging/README.md`, `Docs/design/icons/README.md`, `CHANGELOG.md`, `CLAUDE.md`, `Docs/DECISIONS.md`

- [ ] **Step 1: README.** Install table gets two macOS rows (arm64 "not tested by hand", x64), the checksum file names add `SHA256SUMS-macos.txt`, and a macOS section: which `.dmg` to pick, drag into Applications, first launch through System Settings → Privacy & Security → "Open Anyway" (on macOS 15 a right-click → Open no longer skips the check), the terminal alternative `xattr -dr com.apple.quarantine "/Applications/Raffica PDF.app"`, the CLI symlink `ln -s "/Applications/Raffica PDF.app/Contents/MacOS/raffica" /usr/local/bin/raffica`, and the Apple-silicon note. "Updates" and "Build from source" gain macOS (Xcode command-line tools, Native AOT builds on macOS only).
- [ ] **Step 2: `packaging/README.md`.** New section "Manual check on a Mac (M3.3)" with unticked checkboxes: (a) open the `.dmg`, drag the app, first launch through "Open Anyway" as the README says; (b) the icon in Finder and Dock, "Open With" lists Raffica for a PDF; (c) the default-app card sets Raffica and a Finder double-click opens it; (d) update from one test build to the next (steps: two test builds `-p:RafficaUpdateTestBuild=true` with `RafficaUpdateTestPort` and `RafficaUpdateTestKey`, `update-testserver.cs prepare` and `serve`, then in Terminal `xattr -l "/Applications/Raffica PDF.app"` before and after) with no Gatekeeper prompt and the quarantine result written down; (e) running from the mounted image shows the "Move Raffica PDF to the Applications folder" sentence in both languages; (f) a read-only Applications folder shows the write-protected sentence; (g) the CLI symlink works. Update the M3.1 steps (they used `dev-app.sh`), the "Dry run" and "First release" sections (jobs `release-macos`, the new files) and the open-checks table (`measure.yml ... platform=macos`).
- [ ] **Step 3: `Docs/design/icons/README.md`:** how `macos-app` derives `RafficaPdf.icns` (`sips`, `iconutil`).
- [ ] **Step 4: `CHANGELOG.md` `[Unreleased]`:** macOS packages (two `.dmg`), default-app offer, self-update on macOS. **`CLAUDE.md`:** a "M3.3 (package and release)" paragraph under macOS (bundle step, `MacBundle`, `MacAppUpdater`, `MacSwapScript`, `MacDefaultAppService`, the `package.cs` subcommands, the one `macos` budget entry, the swap-script deviation, the workflow pins) and remove the `dev-app.sh` sentence.
- [ ] **Step 5: `Docs/DECISIONS.md` entry** "Raffica on macOS: packaging, default app and updates (M3.3)": names, plist, signing order, the C API choice and its assumption, optional manifest kinds as a pair, two package forms by process architecture, **the swap script instead of `--finish-update`** (the helper mode is Windows-only and a copy of a running bundle cannot be the helper; the script re-checks the staged bundle with `codesign` before the move and the app verified the image, signature, size and SHA-256 right before, so the window between check and move is the same-user write window the Windows helper also has), the translocation and mounted-image refusal at apply time (the image is downloaded before the refusal; a known limit), the amendment of "x86_64 only" (M2.6): macOS ships `arm64` and `x64`, Windows and Linux stay x86_64. Measured numbers go into a second entry in Task 12.
- [ ] **Step 6: Run** `dotnet run tools/roadmap.cs -- check` (the roadmap is edited in Task 12) and `dotnet format Raffica.slnx --verify-no-changes`.
- [ ] **Step 7: Commit** `docs(macos): README, owner checklist and decisions for packaging`.

### Task 12: Runs, records and the gates

No new code except the recorded numbers. Everything here needs a pushed branch, hosted runners or the owner's Mac; nothing is ticked without its proof.

- [ ] **Step 1: PR CI.** Push, open the PR to `dev` with the label `ci:macos`. Expected: `build-macos` green including "Bundle, image and smoke (both architectures)" and the macOS-only selftest parts (this is the first run of `macos-app`, `dmg`, `dmg-smoke` and `codesign --verify --deep --strict`; fix findings here, never loosen a check; if the strict verify rejects data files in `Contents/MacOS`, apply the fallback of Task 5).
- [ ] **Step 2: Sizes.** Add a non-failing step to `build-macos` that runs `measure.cs size --target pdf --publish-dir out/pdf-<rid> --fonts-dir src/Raffica.Shell/Assets/Fonts --app <bundle> --dmg <dmg>` without `--check`. Re-run the job twice (three runs), take the median per metric and per architecture, round up (M1 rule), use the larger architecture per metric as the `macos` budget in `s_pdfBudgets["macos"]` (size, zip, app, dmg), switch the step to `--check`, and record both architectures' numbers in `Docs/DECISIONS.md`.
- [ ] **Step 3: Apple-silicon record.** `gh workflow run measure.yml --ref feature/m33-package-and-release -f platform=macos-hosted -f target=pdf -f note="hosted macos-15 arm64" -f ref=<branch head>`; record the artifact name and the numbers in `Docs/DECISIONS.md` **without a limit**.
- [ ] **Step 4: Intel budgets and the rendering default (owner's Mac).** The owner's Mac runner must be online: `gh workflow run measure.yml --ref feature/m33-package-and-release -f platform=macos -f target=pdf -f rendering=default -f note="MacBook Pro 2016 ..."` and the same with `rendering=software`. Apply the decision rule of the entry "Raffica PDF: software rendering is the Windows default (M2.1 follow-up)" to choose the default, fill the time and memory budgets of `s_pdfBudgets["macos"]` by the M1 rule from the chosen variant, change the macOS default in `Program.BuildAvaloniaApp` if software wins, and record both variants. If the Mac is offline, leave these two items open in the roadmap with the commands above and do not guess numbers.
- [ ] **Step 5: Roadmap.** Edit `Docs/roadmap.json`: tick each finished task, tick an acceptance criterion only with its proof (run id, test name, command), add the scope changes ("`measure.yml` gets the platform `macos-hosted`", "`build-macos` also bundles and smokes", "`RAFFICA_RENDERING` on macOS"), run `dotnet run tools/roadmap.cs -- generate` and `-- check`, commit both files.
- [ ] **Step 6: Merge and dry run.** When every check is green, Claude merges the PR to `dev`. The release dry run can only start after that, because the `ref` must be a commit on `dev` or `main` and `release.yml` is read from the dispatch ref: `gh workflow run release.yml --ref dev -f tag=v0.1.0 -f ref=<40-hex head of dev> -f upload=false`. Expected: `release-preflight`, `release-windows`, `release-linux` and `release-macos` green, `release-manifest` skipped. Record the run id, tick the dry-run and release-smoke criteria in a small follow-up docs PR (this is the one place the roadmap closes after the merge).
- [ ] **Step 7: Open owner gates, with their checks, stay unticked:** first launch through "Open Anyway", default app, update without a Gatekeeper prompt and the quarantine result, the Intel budgets if the Mac was offline, real printers (3.2), and the first release (`upload=true` has never run).

---

## Self-review

- **Spec coverage:** 5.1 bundle and icon (T5), CLI inside the bundle (T5), CLI symlink in README (T11); 5.7 default app (T4); 5.8 update, fallbacks and quarantine rule (T3, wording T3); 6 speed (T9, T10, T12); 7 CI and release (T8, T7); README (T11); `DECISIONS` amendment (T11); owner checks (T11 checklist, T12 gates). Roadmap tasks 1 to 7 map to T5, T6, T4, T3, T7 and T8, T9 and T10 and T12, T11 and T12.
- **Type consistency:** `MacBundle.PathOf/LocationOf/Identifier` (T2) are used in T3 and T4; `PackageFormDetector.BundlePathOf` (T1) is what `PathOf` calls; `MacSwapScript.Arguments` (T2) is called in T3 with seven values; `Dmg.FileName` (T6) matches `UpdateManifestRules.Kinds` names (T7) and `UpdateFileKinds` (T1).
- **Deviations from the spec, named:** a swap script instead of `--finish-update` for macOS (the helper mode is Windows-only); the refusal for a translocated or mounted bundle comes at apply time and not at offer time (no new card API); one `macos` size entry for both architectures.
- **Not verifiable on Windows, so unticked until Task 12:** `codesign`, `hdiutil`, `ditto`, `plutil`, `xattr`, `sips`, `iconutil`, the CoreServices calls, the strict signature of the bundle (tessdata in `Contents/Resources`, the dylibs in `Contents/MacOS`), the hosted-runner Rosetta start, and the Avalonia macOS software-rendering option.
