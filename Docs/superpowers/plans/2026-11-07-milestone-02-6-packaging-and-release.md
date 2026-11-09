# Milestone 2.6 – Packaging and release: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Raffica PDF ships as a Windows installer and portable ZIP and as a Linux `.deb` and AppImage, registers as a PDF handler, asks once whether it should be the default app, checks for signed updates (switchable) and installs them for the installer and AppImage forms, and a release workflow builds all of it as a dry run. Publishing the first release is not part of this milestone.

**Architecture:** Core already holds the logic below the UI (imported in M0): `UpdateChecker`, `ManifestVerifier`, `ChecksumVerifier`, `UpdateTrust` (placeholder key, test-build swap `RafficaUpdateTestBuild`), `PackageFormDetector`, `UpdateRequest`/`UpdateStaging`, `SessionStore`/`SessionSnapshot`, `IUpdateApplier`, `IUpdateSource`, `IDefaultAppService`, `FileTypeRegistration`, `DesktopEntry`, and the settings `AppSettings.UpdateCheck` and `AppSettings.DefaultAppPrompt`. What was not imported is the platform and tooling side: `tools/package.cs`, `tools/update-testserver.cs`, `tools/licenses.cs`, `tools/smoke-cli.cs`, `packaging/windows`, `packaging/linux`, `.github/workflows/release.yml` and the app services (`GitHubUpdateSource`, `UpdateCoordinator`, the Windows helper mode and installer, `AppImageUpdater`, both default-app services, the registry writer). 2.6 ports them from the read-only Pagewing `v1.0.0` checkout in an unchanged-import commit followed by adaptation commits (the M0 rule, spec section 4), and builds the UI on the shell's frame: an update button in the title bar, cards through `DialogQueue`, an extra row in the settings dialog, an About card. Logic without a window is plain classes tested without Avalonia; cards are tested headless as in 2.1 to 2.5.

**Tech Stack:** .NET 10 Native AOT, Avalonia 12.1.5, xunit.v3 3.2.2 with `Avalonia.Headless.XUnit`, Inno Setup 6 (pinned in `tools/package.cs`, build tool only), `dpkg-deb`, the AppImage runtime built from the pinned sources; GitHub Actions; no new NuGet package.

**Spec:** [Docs/superpowers/specs/2026-11-04-raffica-pdf-design.md](../specs/2026-11-04-raffica-pdf-design.md): sections 2 (goals 4 and 5), 3 (no telemetry, the only network access is the update check, which can be switched off), 4 (platform code may be ported, UI code may not), 6 (unsaved changes asked before an update install), 7 (row 2.6), 8, 9. Decisions: "Raffica PDF: platform code, autosave, OCR build and releases (M2)", "Updater: key and addresses until the first release", "CI: one required check" in [Docs/DECISIONS.md](../../DECISIONS.md). Behaviour rules come from the predecessor's `2026-10-23-m81-automatische-updates-design.md` and `packaging/README.md` (read-only checkout `C:\VS-Code\Pagewing`, tag `v1.0.0`, read with `git show v1.0.0:<path>`; German); the rules a task uses are cited in English in that task.

## Global Constraints

- **Layering:** `Raffica.Pdf` references `Raffica.Core` and `Raffica.Shell` only and never `Raffica.Ocr`/`Raffica.Pdfium` (`AppProjectTests.TheAppReferencesOnlyCoreAndShell`). Platform code lives in `src/Raffica.Pdf/Platform/<OS>`, reached through a factory (`PlatformDefaultAppServices.Create`, `PlatformUpdateApplier.Create`), never from UI code.
- **No telemetry, one network use:** the only connection the app makes is the update check and download against the addresses compiled into `UpdateTrust`. There is no environment variable, switch or setting that changes the addresses or keys of a release build. With `UpdateCheck` off the app opens no connection at all.
- **Secrets:** the update signing key never appears in a file, commit, log, PR or reply. This plan generates no real key; it names the placeholder and the CI secret. The placeholder in `UpdateTrust.cs` stays until the owner instructs the key (open item).
- **Nothing on GitHub changes:** no visibility change, release, tag, environment or secret is created by this milestone; the dry run of `release.yml` uploads nothing (`upload=false`).
- **Speed:** nothing of the updater, the default-app card or the About card exists before first use or before the first sharp page (`StartupCreatesNoPackagingWork` pins it); the budgets of `Docs/DECISIONS.md` ("Raffica PDF speed budgets (M2.1)") stay and `measure.yml` stays within them. The published-size budget is re-checked (`dotnet run bench/shell-probe/measure.cs -- size --check ...`); a miss is a finding for the owner, never a loosened limit.
- **Strings and shell:** every UI string in `Strings.en.resx` and `Strings.de.resx` of the app (a missing German value fails `TranslationTests`); no hex literals in component styles; new shell API is additive so the gallery and other apps stay unchanged. The UI never shows Core's English sentences (`UpdateFailure` codes and `ApplyResult.Message` are worded by the app in both languages).
- **Wording:** the update UI never says an update is "safe" or "guaranteed"; it says what was checked (the signature and the size and SHA-256 of the file).
- **Git:** work on `feature/m26-packaging-and-release`, selective `git add`, Conventional Commits, no attribution lines, commit with the noreply address. Commit 1 of Task 1 is the unchanged import (it fails `NoPagewingLeftoverTests` on purpose, as in M0); the PR to `dev` is merged with a squash only if the import commit does not need to survive, otherwise with "Create a merge commit" (owner to confirm at merge time).
- **Done means:** `dotnet build Raffica.slnx -c Release`, `dotnet test Raffica.slnx -c Release --no-build --max-parallel-test-modules 1` with `RAFFICA_REQUIRE_OCR=1`, `dotnet format Raffica.slnx --verify-no-changes`, `dotnet run tools/roadmap.cs -- check` green, plus the workflow runs named in the acceptance evidence.

## Calls made while planning

1. **One plan, no split.** The spec allows splitting a milestone; the tasks below each end in an independently testable deliverable, so the roadmap keeps the single id 2.6.
2. **Names.** Product "Raffica PDF"; the app executable stays `RafficaPdf` (`.exe` on Windows); the CLI `raffica` ships in `cli/`. The Windows install folder is `{autopf}\Raffica PDF`; the Linux files are `raffica-pdf` (`/usr/lib/raffica-pdf/RafficaPdf`, `raffica-pdf.desktop`). Core's `FileTypeRegistration` names `Applications\Raffica.exe` and `DesktopEntry` sets `StartupWMClass=Raffica`; both are corrected to the real executable (Task 3). Release file names: `RafficaPdf-<v>-win-x64-setup.exe`, `RafficaPdf-<v>-win-x64-portable.zip`, `RafficaPdf-<v>-x86_64.AppImage`, `raffica-pdf_<v>_amd64.deb`, `update-manifest.json(.sig)`, `SHA256SUMS-windows.txt`, `SHA256SUMS-linux.txt`.
3. **Only the parts needed.** Not imported: `check-binary` and `native-fetch` of Pagewing's `package.cs` (this repo has `tools/check-binary.cs` and `tools/native-fetch.cs`), the license of Pagewing's own packages, the old German README sections for the M10 to M16 viewer checks. The owner-gate sections of `packaging/README.md` (key generation, making the repository public, the manual update check) are rewritten in English for Raffica.
4. **Test vector.** `testdata/updates/update-manifest.json(.sig)` carries `Pagewing-0.2.0-...` names and was signed with a discarded key. Task 2 regenerates it with the ported tool, a new throwaway key (private half discarded, never stored) and Raffica names, and removes the two `Pagewing` entries of the `NoPagewingLeftoverTests` allow list that exist only for it (`testdata/updates/update-manifest.json`, `tests/Raffica.Core.Tests/Updates/ReleaseToolTests.cs`, `UpdateTrustTests.cs` if it only names the vector).
5. **Update button.** `TitleBar` gets an additive `Trailing` content slot left of the caption buttons (Pagewing's choice, spec decision 1 of its M8.1); the frame behaviour for the gallery is unchanged. Recorded under "Scope changes" in the roadmap.
6. **Applier behaviour kept from the predecessor:** the installer form updates through a helper copy of the app (`--finish-update <staging>`) after download and verification and after the user chose "Install"; the AppImage replaces itself; the `.deb` and the portable ZIP only show "available" with the download page (opened through the confirmation card of external addresses). Tabs reopen after the restart through Core's `SessionStore`.
7. **x86_64 only,** as Pagewing; no arm64 build.

## Review Focus

Failure modes the spec implies but no acceptance criterion exercises, most likely first (each has its test in the owning task):

1. **A profile or install path with spaces and non-ASCII characters** (`C:\Users\Zoë Müller\AppData\Local\Programs\Raffica PDF\RafficaPdf.exe`): the registry command, the `Exec=` line and the helper's arguments must stay one argument each. (Task 3, Task 8)
2. **No network, a captive portal or a proxy answering HTML at `releases/latest`:** the start shows no error and no delay of the first page; a manual check says "could not check" once. (Task 7)
3. **Unsaved changes in several tabs when "Install" is chosen:** every tab is asked, Cancel anywhere keeps all tabs and the staged file, and nothing is written before the last answer. (Task 7)
4. **A read-only or full disk and a portable copy in a read-only folder:** staging fails with a worded message and offers the download page, the running version stays. (Task 8)
5. **A machine where the default app cannot be set** (policy-locked `HKCU`, no `xdg-mime`, a desktop without a settings page): the card ends in a result that says so, "Don't ask again" is remembered and the card never reappears in a loop. (Task 4)

---

### Task 1: Import the packaging and update code unchanged

**Files:**
- Create (unchanged copies from `git show v1.0.0:<path>` of the Pagewing checkout, tracked files only): `tools/package.cs`, `tools/update-testserver.cs`, `tools/licenses.cs`, `tools/smoke-cli.cs`, `packaging/windows/pagewing.iss`, `packaging/linux/{AppRun,container-build.sh,control.template,copyright.template,pagewing.desktop}`, `.github/workflows/release.yml`, and the app code `src/Pagewing.App/Services/{GitHubUpdateSource,UpdateCoordinator,UpdateApplierSupport,UpdateInstallSetup,UpdateCheckOutcome,IUpdateFlow,PlatformUpdate,PlatformDefaultAppServices}.cs`, `Platform/Windows/{WindowsUpdateHelper,WindowsUpdateInstaller,WindowsDefaultAppService,WindowsFileTypeRegistry,ShellNative}.cs`, `Platform/Linux/{AppImageUpdater,LinuxDefaultAppService}.cs` into a scratch folder `artifacts/pagewing-import/` that is **not committed**; only the files of this list that are not C# app code are committed in place (tools, packaging, workflow).
- Test: none (a reference import).

**Interfaces:**
- Produces: the unchanged tools, scripts and workflow in their final paths, so Task 2 is a reviewable rename diff; the app code stays in the scratch folder as the read-only reference for Tasks 3, 4, 7 and 8 (UI and view-model code is never imported, spec section 4).

- [ ] **Step 1:** In the Pagewing checkout run `git status --short` (must be clean, never modify it) and export each listed file with `git show v1.0.0:<path> > <target>`. Do not copy the working folder.
- [ ] **Step 2:** Confirm `git status` here shows exactly the tool, packaging and workflow files; `artifacts/` is ignored.
- [ ] **Step 3: Commit** `chore: import packaging tools and release workflow from Pagewing v1.0.0 unchanged` and check `git show --stat HEAD` against the list.

---

### Task 2: Adapt the tools, scripts and workflow to Raffica

**Files:**
- Modify: `tools/package.cs`, `tools/update-testserver.cs`, `tools/licenses.cs`, `tools/smoke-cli.cs`, `packaging/windows/pagewing.iss` (rename to `raffica-pdf.iss`), `packaging/linux/*` (rename files and contents), `.github/workflows/release.yml`
- Create: `packaging/README.md` (English, only the sections of call 3), `third-party/licenses.json` if `licenses.cs` needs its manifest (read the tool's header; it reads `third-party/`, `Directory.Packages.props` and the publish folders)
- Replace: `testdata/updates/update-manifest.json`, `testdata/updates/update-manifest.json.sig`
- Modify: `tests/Raffica.Core.Tests/NoPagewingLeftoverTests.cs` (shrink the allow list), `tests/Raffica.Core.Tests/Updates/ReleaseToolTests.cs`, `UpdateTrustTests.cs` (names and vector key)
- Test: `tools/package.cs selftest`, `tools/licenses.cs selftest`, `tools/update-testserver.cs selftest`, `ReleaseToolTests`, `NoPagewingLeftoverTests`

**Interfaces:**
- Consumes: the imported files of Task 1; `tools/check-binary.cs` and `tools/native-fetch.cs` of this repo (the imported `check-binary` and `native-fetch` commands are deleted, the workflow calls the repo's tools).
- Produces: `package.cs` commands `layout`, `zip`, `sums`, `sums-verify`, `inno-install`, `installer`, `installer-smoke`, `update-smoke`, `update-keygen`, `update-manifest`, `update-sign`, `update-verify`, `deb`, `deb-smoke`, `runtime-fetch`, `runtime-sources`, `appimage`, `appimage-smoke`, `selftest` with English messages; constants `AppName="Raffica PDF"`, `ExeName="RafficaPdf"`, `CliName="raffica"`, the release file names of call 2; a new Inno `AppId` GUID (generated once with `dotnet run` or `[guid]::NewGuid()`, written into `raffica-pdf.iss` with the comment "never change it"; `IssRules` in the selftest pins that it differs from Pagewing's `472398B4-29BA-4DDB-8475-7928DA355D74`).

- [ ] **Step 1: Write the failing tests:** `NoPagewingLeftoverTests` (existing, now with the two vector entries removed from `Allowed`) fails on the imported tools; `ReleaseToolTests.TheManifestOfTheReleaseToolVerifiesInTheApp` expects the Raffica names of call 2 and version `0.2.0` for the regenerated vector.
- [ ] **Step 2: Run** `dotnet test tests/Raffica.Core.Tests -c Release --filter-class "*NoPagewingLeftoverTests"`. Expected: FAIL naming the tool files.
- [ ] **Step 3: Rename and translate** with a scripted dry run first (list every hit, read it, then apply): `Pagewing`→`Raffica PDF`/`RafficaPdf`/`raffica-pdf` by context, `pagewing`→`raffica`, the update-key variable `PAGEWING_UPDATE_SIGNING_KEY`→`RAFFICA_UPDATE_SIGNING_KEY`, repository `benjo-code/Pagewing`→`benjo-code/raffica`, `src/Pagewing.*`→`src/Raffica.*` (App→`src/Raffica.Pdf`, the CLI project `src/Raffica.Cli`), German messages and comments to English. Delete the imported `check-binary` and `native-fetch` code and the Tesseract-prerelease handling (the workflow runs `dotnet run tools/native-fetch.cs -- --rid <rid> --out out/native`). Keep the ELF/glibc rule of the Linux build and the tag and ref-input rules of the workflow verbatim (they are security rules; translate only the text). In `release.yml` rename the signing secret and environment `release-signing` as before, keep `workflow_dispatch` only, keep the key preflight that refuses an upload while `UpdateTrust.cs` holds the placeholder (`update-verify --keys-from-source`).
- [ ] **Step 4: Regenerate the vector:** run `package.cs update-keygen` to a path outside the working tree, build the manifest and signature for fixed inputs with the Raffica names (the selftest documents the exact command), write the two files into `testdata/updates/`, then delete the private key file. Put the new public key in `ReleaseToolTests`, update the `selftest` constants.
- [ ] **Step 5: Run** `dotnet run tools/package.cs -- selftest`, `dotnet run tools/licenses.cs -- selftest`, `dotnet run tools/update-testserver.cs -- selftest` and the two test classes. Expected: PASS. `dotnet run tools/roadmap.cs -- check` stays green.
- [ ] **Step 6: Commit** `refactor: rename and translate the packaging tools for Raffica PDF` (and `test:` for the vector in a separate commit).

---

### Task 3: File type registration and the command line of registration

**Files:**
- Create: `src/Raffica.Pdf/Platform/Windows/WindowsFileTypeRegistry.cs`, `src/Raffica.Pdf/Platform/RegistrationCommand.cs`
- Modify: `src/Raffica.Core/Distribution/FileTypeRegistration.cs` (key `Applications\RafficaPdf.exe`, display name "Raffica PDF Document" kept), `src/Raffica.Core/Distribution/DesktopEntry.cs` (`Name=Raffica PDF`, `StartupWMClass` of the real window class, checked in Task 6), `src/Raffica.Pdf/PdfOptions.cs`, `src/Raffica.Pdf/Program.cs`
- Test: `tests/Raffica.Core.Tests/Distribution/FileTypeRegistrationTests.cs`, `DesktopEntryTests.cs`, `tests/Raffica.Pdf.Tests/Platform/RegistrationCommandTests.cs`

**Interfaces:**
- Consumes: `FileTypeRegistration.Entries(string exePath) : IReadOnlyList<RegistryEntry>`, `Removals()`, `RegistrationScope` (Core).
- Produces: `PdfOptions.RegisterFileTypes : RegistrationScope?` and `PdfOptions.UnregisterFileTypes : RegistrationScope?` from `--register-file-types=user|machine` and `--unregister-file-types=user|machine` (any other value is a usage error, exit code 1); `RegistrationCommand.Run(PdfOptions options, IRegistryWriter writer, string exePath) : int` (0 on success, 2 on a failed write, never opens a window, never touches `UserChoice`/`FileExts`); `interface IRegistryWriter { void Write(RegistrationScope scope, IReadOnlyList<RegistryEntry> entries); void Remove(RegistrationScope scope, IReadOnlyList<RegistryRemoval> removals); }` with `WindowsFileTypeRegistry : IRegistryWriter`. `Program.Main` runs the command before the single-instance check and before any Avalonia start.

- [ ] **Step 1: Write the failing tests:** `FileTypeRegistrationTests.CommandQuotesAPathWithSpacesAndNonAscii` (`C:\Users\Zoë Müller\AppData\Local\Programs\Raffica PDF\RafficaPdf.exe` gives a command value that starts and ends the path with `"` and ends with `"%1"`), `FileTypeRegistrationTests.ApplicationKeyNamesTheRealExecutable`, `DesktopEntryTests.NameIsRafficaPdf`, `RegistrationCommandTests.RegisterWritesEntriesAndExitsZeroWithoutAWindow` (fake `IRegistryWriter`), `RegistrationCommandTests.AFailingWriterExitsTwo`, `RegistrationCommandTests.AnUnknownScopeIsAUsageError`, `PdfOptionsTests.RegistrationSwitchesParse`.
- [ ] **Step 2: Run** `dotnet test tests/Raffica.Core.Tests -c Release --filter-class "*FileTypeRegistrationTests"` and `dotnet test tests/Raffica.Pdf.Tests -c Release --filter-namespace "*Platform*"`. Expected: FAIL.
- [ ] **Step 3: Implement** the types above. `WindowsFileTypeRegistry` follows `Pagewing.App/Platform/Windows/WindowsFileTypeRegistry.cs` in the scratch folder: `Registry.CurrentUser` or `Registry.LocalMachine` by scope, only the keys of `FileTypeRegistration`, then `SHChangeNotify` (`ShellNative`, `[SupportedOSPlatform("windows")]`, `LibraryImport` source-generated so Native AOT stays warning-free).
- [ ] **Step 4: Run** the same filters plus the whole `Raffica.Core.Tests` classes under `*Distribution*`. Expected: PASS.
- [ ] **Step 5: Commit** `feat(pdf): file type registration and its command line`.

---

### Task 4: Default-app services and the first-start card

**Files:**
- Create: `src/Raffica.Pdf/Platform/Windows/WindowsDefaultAppService.cs`, `src/Raffica.Pdf/Platform/Linux/LinuxDefaultAppService.cs`, `src/Raffica.Pdf/Platform/PlatformDefaultAppServices.cs`, `src/Raffica.Pdf/Packaging/DefaultAppFlow.cs`, `src/Raffica.Pdf/Dialogs/DefaultAppCard.cs`
- Modify: `src/Raffica.Pdf/PdfWindow.Commands.cs` (one command `app.default`, "Make Raffica the default app for PDFs", in the Home tab's File group or the command palette only: the plan chooses the palette entry plus a row in the settings dialog, no new ribbon button), `src/Raffica.Pdf/Settings/PdfSettingsController.cs`, `src/Raffica.Pdf/Resources/Strings.{en,de}.resx`
- Test: `tests/Raffica.Pdf.Tests/Platform/DefaultAppServiceTests.cs`, `tests/Raffica.Pdf.Tests/Packaging/DefaultAppFlowTests.cs`, `tests/Raffica.Pdf.Tests/Dialogs/DefaultAppCardTests.cs`

**Interfaces:**
- Consumes: `IDefaultAppService`, `DefaultAppStatus`, `DefaultAppResult`, `DefaultAppPromptSetting`, `AppSettings.DefaultAppPrompt`, `DialogQueue`.
- Produces: `PlatformDefaultAppServices.Create() : IDefaultAppService` (Windows and Linux implementations, a `NotSupported` stub otherwise); both services take their process runner and registry through constructor seams (`IProcessRunner.RunAsync(string file, IReadOnlyList<string> args, TimeSpan timeout, CancellationToken)` returning exit code and output) so tests use fakes; `DefaultAppFlow(IDefaultAppService service, Func<AppSettings> read, Action<DefaultAppPromptSetting> write, DialogQueue cards)` with `Task OfferAtStartAsync(CancellationToken ct)` (asks once per run, only when `DefaultAppPrompt == Ask`, only after the first sharp page and only when the status is `NotDefault`) and `Task MakeDefaultFromCommandAsync(CancellationToken ct)`; `DefaultAppCard` with buttons Set as default, Not now, Don't ask again (arming lock as the other destructive-free cards use for key repeat).

- [ ] **Step 1: Write the failing tests:** `DefaultAppFlowTests.AsksOnlyWhenNotDefaultAndSettingIsAsk`, `DefaultAppFlowTests.NeverAsksWhenTheStatusIsUnknown`, `DefaultAppFlowTests.DontAskAgainIsStoredAndNeverShownAgain` (second `OfferAtStartAsync` shows no card), `DefaultAppFlowTests.NotAvailableAndFailedEndInAWordedNoteWithoutLooping` (Review Focus 5: a service returning `Failed` then `NotAvailable` yields one note each and the setting is unchanged), `DefaultAppFlowTests.OpenedSystemSettingsNamesTheSettingsPage`, `DefaultAppServiceTests.LinuxUsesXdgMimeQueryAndDefault` (fake runner: `xdg-mime query default application/pdf` output `raffica-pdf.desktop` is `IsDefault`; absent `xdg-mime` is `Unknown`/`NotAvailable`), `DefaultAppServiceTests.WindowsReadsUserChoiceAndOpensSettingsNotWritesIt` (Windows 10 and 11 forbid setting `UserChoice`: the service opens `ms-settings:defaultapps`, result `OpenedSystemSettings`), `StartupCreatesNoPackagingWork` (building the window and opening the first page constructs no `DefaultAppFlow`, no service and no process).
- [ ] **Step 2: Run** `dotnet test tests/Raffica.Pdf.Tests -c Release --filter-namespace "*Packaging*"` and `"*Platform*"`. Expected: FAIL.
- [ ] **Step 3: Implement** the types above following the scratch copies of `WindowsDefaultAppService`, `LinuxDefaultAppService` (including the `.desktop` file and MIME database writes in `~/.local/share` for an AppImage, with `DesktopEntry.Render`) and `PlatformDefaultAppServices`; the flow runs on the UI synchronization context only for the card, the service work on the pool; every wait has a timeout and a `CancellationToken`. Strings for card, buttons and the four result notes in both languages.
- [ ] **Step 4: Run** the filters, then `dotnet test tests/Raffica.Pdf.Tests -c Release` as a whole. Expected: PASS with `TranslationTests` green.
- [ ] **Step 5: Commit** `feat(pdf): default app services and the first-start card`.

---

### Task 5: Windows packages (layout, ZIP, installer) and the About notices

**Files:**
- Create: `src/Raffica.Pdf/Resources/ThirdPartyNotices.txt` (generated), `src/Raffica.Pdf/Dialogs/AboutCard.cs`
- Modify: `src/Raffica.Pdf/Raffica.Pdf.csproj` (embed `ThirdPartyNotices.txt` as a resource, nothing else), `src/Raffica.Pdf/PdfWindow.Commands.cs` (command `app.about`, in the palette and the help card), `packaging/windows/raffica-pdf.iss` (final), `tools/package.cs` (layout rules)
- Test: `tests/Raffica.Pdf.Tests/Dialogs/AboutCardTests.cs`, `package.cs selftest` (`IssRules`), `licenses.cs check`

**Interfaces:**
- Consumes: `AppInfo.Version`, `UpdateTrust.TestBuildNote(CultureInfo)`, `DialogQueue`, the embedded notices text.
- Produces: `AboutCard` showing name, version (with the test-build note when `TestBuildNote` is not null), license "GPL-3.0-or-later", the notices text in a scrollable box and the project address; the Windows layout of `package.cs layout`: `RafficaPdf.exe` with its libraries (no `*.pdb`), `pdfium.dll`, `tesseract.dll` and `tessdata/`, `LICENSE`, `THIRD-PARTY-NOTICES.txt`, `cli/raffica.exe` with its own `pdfium.dll`; Inno script rules (user install by default with `PrivilegesRequiredOverridesAllowed=commandline dialog`, `CloseApplications=yes`, `ChangesAssociations=yes`, `[Run]` `RafficaPdf.exe --register-file-types={code:FileTypeScope}` hidden, `[UninstallRun]` `--unregister-file-types=...`, English and German setup languages, `AppId` of Task 2, user data stays on uninstall).

- [ ] **Step 1: Write the failing tests:** `AboutCardTests.ShowsVersionLicenseAndNotices`, `AboutCardTests.TestBuildNoteShownOnlyWhenPresent`, `AboutCardTests.NoticesNameEveryShippedComponent` (the text contains PDFium, Tesseract, Leptonica, Avalonia, SkiaSharp-or-HarfBuzz as `licenses.cs check` lists them, plus Geist, Public Sans, Barlow Semi Condensed, Lucide, Carlito and the tessdata_fast language data), and the selftest `IssRules` pins the rules above.
- [ ] **Step 2: Run** `dotnet test tests/Raffica.Pdf.Tests -c Release --filter-class "*AboutCardTests"` and `dotnet run tools/package.cs -- selftest`. Expected: FAIL.
- [ ] **Step 3: Implement** `AboutCard` (strings in both languages), generate the notices with `dotnet run tools/licenses.cs -- generate`, and finish the layout and Inno rules in the tool. `licenses.cs check --publish-dir out/app --publish-dir out/cli` must pass on a real publish (`dotnet publish src/Raffica.Pdf -c Release -r win-x64 -o out/app`, add `C:\Program Files (x86)\Microsoft Visual Studio\Installer` to `PATH` if `vswhere.exe` is missing).
- [ ] **Step 4: Build the files locally on Windows:** `package.cs layout`, `zip`, `inno-install`, `installer`, `installer-smoke` (installs silently to a temp folder as the current user, checks the registry keys of Task 3 and the uninstall, removes everything). Expected: all exit 0; record the file sizes in the evidence.
- [ ] **Step 5: Run** the unit test class, the selftests and the whole `Raffica.Pdf.Tests`. Expected: PASS.
- [ ] **Step 6: Commit** `feat(pdf): Windows layout, installer script, About card and license notices`.

---

### Task 6: Linux packages (`.deb` and AppImage)

**Files:**
- Modify: `packaging/linux/{AppRun,container-build.sh,control.template,copyright.template,raffica-pdf.desktop}`, `tools/package.cs` (`deb`, `deb-smoke`, `appimage`, `appimage-smoke`, `runtime-fetch`, `runtime-sources`)
- Create: `packaging/linux/raffica-pdf.png` or `.svg` icon only if the shell assets already hold the mark (use `src/Raffica.Shell/Assets`; no new artwork)
- Test: `package.cs selftest` (Linux rules), `deb-smoke` and `appimage-smoke` inside the `ubuntu:22.04` container

**Interfaces:**
- Consumes: the layout of Task 5 for Linux (`RafficaPdf` executable, `libpdfium.so`, `libtesseract.so`, `tessdata/`, `cli/raffica`).
- Produces: `raffica-pdf_<v>_amd64.deb` installing to `/usr/lib/raffica-pdf/` (so `PackageFormDetector.DebDirectory` must read `/usr/lib/raffica-pdf/`: change that constant and `PackageFormTests` in Core in this task), symlinks `/usr/bin/raffica-pdf` and `/usr/bin/raffica`, the desktop file and MIME association; `RafficaPdf-<v>-x86_64.AppImage` without `libfuse2` (runtime built from the pinned sources `fuse-3.15.0`, `type2-runtime-20251108`, source archives uploaded next to the release as in the predecessor), `AppRun` execing `$APPDIR/usr/lib/raffica-pdf/RafficaPdf` (this path is what `PackageFormDetector.IsInsideAppDir` expects).

- [ ] **Step 1: Write the failing tests:** `PackageFormTests.DebDirectoryIsTheRafficaPdfFolder`, `PackageFormTests.AnAppImageChildStartedFromTheDebIsDeb` (existing rule kept, new path), and the Linux part of the selftest: control file fields, `Depends` and `Recommends` as in the predecessor with the new names, the glibc ceiling check of `container-build.sh`.
- [ ] **Step 2: Run** `dotnet test tests/Raffica.Core.Tests -c Release --filter-class "*PackageFormTests"`. Expected: FAIL.
- [ ] **Step 3: Implement** the renames in Core and the packaging files. Check the window class: the desktop file's `StartupWMClass` must equal the class Avalonia sets for this app (read it with `xprop WM_CLASS` in the container smoke or on a Linux machine; if it cannot be read here, mark it as an open item with that command and keep `RafficaPdf`).
- [ ] **Step 4: Build in the container** (needs Docker, on the owner's Linux runner or a Linux machine): `docker run --rm -v "$PWD:/w" -w /w -e VERSION=0.1.0 -e DEBVERSION=0.1.0 -e HOST_UID -e HOST_GID ubuntu:22.04 bash packaging/linux/container-build.sh`. Expected: `out/release` holds the `.deb` and AppImage and both smokes pass. If no Linux machine or Docker is available when this task runs, say so, leave the step open and let the workflow dry run of Task 10 be the proof.
- [ ] **Step 5: Commit** `feat(pdf): Linux deb and AppImage packaging`.

---

### Task 7: The update flow in the app

**Files:**
- Create: `src/Raffica.Pdf/Updating/GitHubUpdateSource.cs`, `UpdateFlow.cs`, `UpdateWording.cs`, `UpdateInstallGuard.cs`, `src/Raffica.Pdf/Platform/PlatformUpdateApplier.cs`, `src/Raffica.Pdf/Dialogs/UpdateCard.cs`, `UpdateProgressCard.cs`
- Modify: `src/Raffica.Shell/Frame/TitleBar.cs` (additive `Trailing` slot, styles in `Styles/Frame/TitleBar.axaml`), `src/Raffica.Pdf/PdfWindow.axaml.cs` and a new `PdfWindow.Update.cs` (button, command `app.update`, "Check for updates"), `src/Raffica.Pdf/Documents/CloseGuard.cs` (a way to ask every tab, see below), `Resources/Strings.{en,de}.resx`, `tests/Raffica.Shell.Tests` for the slot
- Test: `tests/Raffica.Pdf.Tests/Updating/*`, `tests/Raffica.Shell.Tests/Frame/TitleBarTrailingTests.cs`

**Interfaces:**
- Consumes: `UpdateChecker.CheckDetailedAsync(bool force, CancellationToken) : Task<UpdateCheckResult>`, `FetchVerifiedDocumentsAsync`, `UpdateChecker.Pick(manifest, PackageForm)`, `ChecksumVerifier`, `UpdateStaging.DirectoryFor/DeleteOld`, `UpdateRequest.TryWrite`, `StagedUpdate`, `IUpdateApplier`, `PackageFormDetector.Detect`, `UpdateState`/`UpdateStage`/`UpdateFailure`, `SessionStore`, `AppSettings.UpdateCheck`, `UpdateTrust`.
- Produces: `GitHubUpdateSource : IUpdateSource` (an `HttpClient` created on first use, `GET UpdateTrust.ReleasesLatestUrl` and the assets of `update-manifest.json` and `.sig` below `DownloadBaseUrl`, follows only redirects to `https` hosts of GitHub's storage, response size caps, `User-Agent: RafficaPdf/<version>`, no cookies, no proxy credentials); `UpdateFlow(IUpdateChecker checker, IUpdateSource source, IUpdateApplier? applier, PackageForm form, Func<bool> enabled, TimeProvider clock, string configDirectory)` with `UpdateState State`, `event Action<UpdateState>? Changed`, `Task CheckOnStartAsync(CancellationToken)` (nothing at all when `enabled()` is false), `Task CheckNowAsync(CancellationToken)` (works even when the switch is off, since the user asked; reports "up to date", "could not check" or an offer), `Task DownloadAndStageAsync(CancellationToken)`, `Task<ApplyResult> InstallAsync(CancellationToken)`, `Cancel()`; `UpdateInstallGuard.AskAllTabsAsync(IReadOnlyList<PdfTab> tabs, ...) : Task<bool>` (true only if every tab with unsaved work was answered Save, Don't save or already clean); `TitleBar.Trailing : object?`.

- [ ] **Step 1: Write the failing tests** (a `FakeUpdateSource` of Core's tests is reused; the clock is a `FakeTimeProvider`):
  - `UpdateFlowTests.NoConnectionWhenTheSwitchIsOff` (source throws if called; `CheckOnStartAsync` returns, the source count is 0),
  - `UpdateFlowTests.ACheckIsNotRepeatedWithin24Hours` and `UpdateFlowTests.ManualCheckIgnoresTheInterval`,
  - `UpdateFlowTests.HtmlAnswerFromACaptivePortalIsSilentAtStartAndWordedOnManualCheck` (Review Focus 2: source returns non-manifest bytes; start leaves `State` unchanged and raises no `Changed`; manual check ends with one "could not check" note),
  - `UpdateFlowTests.DownloadVerifiesChecksumBeforeAnythingIsStaged` and `UpdateFlowTests.AWrongChecksumDeletesTheFileAndFailsWithChecksum`,
  - `UpdateFlowTests.DebAndPortableOnlyOfferTheDownloadPage`,
  - `UpdateInstallGuardTests.EveryDirtyTabIsAskedAndCancelAnywhereKeepsAll` (Review Focus 3: three tabs, the second answers Cancel; no tab is saved or closed after that, the staged file is still there),
  - `UpdateInstallGuardTests.SavedAnswersAreWrittenBeforeTheInstallerStarts`,
  - `UpdateWordingTests.EveryUpdateFailureHasWordingInBothLanguages` (iterates the constants of `UpdateFailure`),
  - `NoUiTextOverpromisesUpdates` (no "safe", "guaranteed", "secure" in the update strings),
  - `TitleBarTrailingTests.TrailingContentSitsLeftOfTheCaptionButtonsAndTheDefaultIsEmpty`,
  - `StartupCreatesNoPackagingWork` extended: no `HttpClient`, no `UpdateFlow` before the first sharp page.
- [ ] **Step 2: Run** `dotnet test tests/Raffica.Pdf.Tests -c Release --filter-namespace "*Updating*"` and the shell test class. Expected: FAIL.
- [ ] **Step 3: Implement** the types above. `GitHubUpdateSource` and `UpdateFlow` follow the scratch `GitHubUpdateSource.cs` and `UpdateCoordinator.cs` (the check starts after the first sharp page on the pool; results marshal to the UI thread). `UpdateInstallGuard` reuses `CloseGuard`'s per-tab question (Save, Don't save, Cancel) through a new `CloseGuard.AskForUpdateAsync(IReadOnlyList<PdfTab>)` that never closes a tab. A pending organize plan or redact plan asks its own card first, as on close. The session is written with `SessionStore.TrySave` only after the last answer and removed by the next start that consumes it.
- [ ] **Step 4: Run** the filters, then `dotnet test tests/Raffica.Pdf.Tests -c Release` and `dotnet test tests/Raffica.Shell.Tests -c Release` as a whole. Expected: PASS.
- [ ] **Step 5: Measure:** run the PDF measurements as in 2.5 (`measure.yml` locally with `--target pdf`) and the published-size check; record the result in the DECISIONS entry of Task 11. A start that is slower than before 2.6 is a finding to chase (the check must not run before the first sharp page) before it is raised.
- [ ] **Step 6: Commit** `feat(pdf): update check, download, verification and install guard`.

---

### Task 8: The platform appliers (Windows helper, AppImage)

**Files:**
- Create: `src/Raffica.Pdf/Platform/Windows/WindowsUpdateInstaller.cs`, `WindowsUpdateHelper.cs`, `src/Raffica.Pdf/Platform/Linux/AppImageUpdater.cs`, `src/Raffica.Pdf/Platform/UpdateApplierSupport.cs`
- Modify: `src/Raffica.Pdf/PdfOptions.cs` (`--finish-update <staging>` helper mode), `src/Raffica.Pdf/Program.cs` (helper mode runs before the single-instance check; its window uses the shell's theme)
- Test: `tests/Raffica.Pdf.Tests/Platform/WindowsUpdateInstallerTests.cs`, `AppImageUpdaterTests.cs`, `UpdateApplierSupportTests.cs`; `package.cs update-smoke` (Windows)

**Interfaces:**
- Consumes: `IUpdateApplier`, `StagedUpdate`, `ApplyResult`, `UpdateRequest` (`Form`, `ExePath`, `TargetDirectory`, window position), `UpdateStaging`, `ManifestVerifier` and `ChecksumVerifier` (the helper verifies manifest, signature and file again from the staging folder before it starts Inno).
- Produces: `WindowsUpdateInstaller : IUpdateApplier` (copies the running app with its libraries to a temp folder, starts the copy with `--finish-update <staging>`, returns `ApplyResult.Restarting` once the helper runs, ends the app); the helper mode (closes the app, runs `RafficaPdf-<v>-win-x64-setup.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS` with the mode of the existing installation (Inno's fixed `AppId` carries it), waits with a timeout, then starts the new version; on UAC refusal or Inno failure it restarts the old version and shows a worded error with "Try again"); `AppImageUpdater : IUpdateApplier` (writes the new file next to the running AppImage as `.part`, checks, `chmod 0755`, renames over the old with `rename(2)` so a failure leaves the old file, then execs the new one); `UpdateApplierSupport` (path quoting, argument arrays only, never a shell string).

- [ ] **Step 1: Write the failing tests:** `WindowsUpdateInstallerTests.ArgumentsAreAnArrayAndSurviveSpacesAndNonAscii` (Review Focus 1: exe path `C:\Users\Zoë Müller\...`, the fake process starter receives each path as one argument), `WindowsUpdateInstallerTests.AVerificationFailureInTheHelperLeavesTheOldVersionRunning`, `WindowsUpdateInstallerTests.UacRefusalRestartsTheOldVersionAndOffersRetry`, `UpdateApplierSupportTests.AReadOnlyTargetFailsWithStorageAndOffersTheDownloadPage` (Review Focus 4), `AppImageUpdaterTests.ARenameFailureKeepsTheOldFile`, `AppImageUpdaterTests.TheNewFileIsExecutableBeforeItReplacesTheOld`, `AppImageUpdaterTests.DiskFullDeletesThePartFile`.
- [ ] **Step 2: Run** `dotnet test tests/Raffica.Pdf.Tests -c Release --filter-namespace "*Platform*"`. Expected: FAIL.
- [ ] **Step 3: Implement** following the scratch copies of the three Pagewing files; keep their rules (nothing is changed and no session is written before the file matched the signed manifest; once the installer took over the call never throws), replace German text by `UpdateWording` keys.
- [ ] **Step 4: End-to-end on Windows with the test build:** build two versions with `-p:RafficaUpdateTestBuild=true -p:RafficaUpdateTestPort=<port> -p:RafficaUpdateTestKey=<public key>` (the properties come from the command line only, the guard in `Directory.Build.props` refuses an environment variable), make the two installers (`-p:Version=0.1.0` and `0.2.0`), serve 0.2.0 with `dotnet run tools/update-testserver.cs -- prepare ...` and `serve`, then `dotnet run tools/package.cs -- update-smoke --old <0.1.0 setup> --new <0.2.0 setup> --key <test key file>` (the helper mode, unattended). Expected: `update-smoke: 0.1.0 -> 0.2.0` passed; the temp test key file is deleted afterwards. Paste the output in the roadmap evidence.
- [ ] **Step 5: Run** the filter and the whole `Raffica.Pdf.Tests`. Expected: PASS.
- [ ] **Step 6: Commit** `feat(pdf): Windows helper and AppImage update appliers`.

---

### Task 9: Update UI, settings row and strings

**Files:**
- Create: `src/Raffica.Pdf/Updating/UpdateButton.cs`
- Modify: `PdfWindow.Update.cs`, `src/Raffica.Pdf/Settings/PdfSettingsController.cs` (the `SettingsDialogOptions.ExtraRows` row "Check for updates automatically", writing `AppSettings.UpdateCheck`), `PdfCommandCatalog.cs` (`app.update`), `Resources/Strings.{en,de}.resx`, `Dialogs/HelpCard.cs` if it lists commands from the catalog (it builds from the catalog; verify)
- Test: `tests/Raffica.Pdf.Tests/Updating/UpdateButtonTests.cs`, `UpdateCardTests.cs`, `SettingsUpdateRowTests.cs`

**Interfaces:**
- Consumes: `UpdateFlow` (Task 7), `SettingsDialogOptions`, `SettingsExtraRow`, `AppSettings.UpdateCheck`.
- Produces: `UpdateButton` (visible only while `UpdateStage` is not `None`; accessible name "Update available: Raffica PDF <version>"; opens `UpdateCard`: Install, Later, and for Deb and portable "Open download page" through the existing `LinkCard` confirmation); `UpdateProgressCard` with the four steps Download, Check, Install, Restart named in text next to the bar (progress is never colour alone), Cancel up to the install step, retry on failure; the settings row.

- [ ] **Step 1: Write the failing tests:** `UpdateButtonTests.HiddenWhenNothingIsNewer`, `UpdateButtonTests.ShowsTheVersionInItsAutomationName`, `UpdateCardTests.LaterHidesTheCardAndKeepsTheButton`, `UpdateCardTests.DebOffersOnlyTheDownloadPage`, `UpdateProgressCardTests.StepsAreNamedInTextAndCancelStopsBeforeInstall`, `SettingsUpdateRowTests.TheRowWritesUpdateCheckAndTheDialogStillHidesAutosave`, `SettingsUpdateRowTests.TurningTheSwitchOffStopsLaterChecksInTheRunningApp`.
- [ ] **Step 2: Run** `dotnet test tests/Raffica.Pdf.Tests -c Release --filter-namespace "*Updating*"`. Expected: FAIL.
- [ ] **Step 3: Implement** the controls with shell tokens only (no hex literals; `NoHexInComponentStylesTests` stays green) and the strings in both languages. Layout check at 1024 px and in German (the frame's clipped-label test renders the title bar; extend it by the button).
- [ ] **Step 4: Run** the filter, `TranslationTests` and the whole Pdf and Shell test projects. Expected: PASS.
- [ ] **Step 5: Look at it on a real display** (run the app with `-p:RafficaUpdateTestBuild=true` against the test server of Task 8): button, card, progress, German, dark and high contrast; take screenshots into the evidence. Without a display, mark this as open with that command.
- [ ] **Step 6: Commit** `feat(pdf): update button, cards and the update-check setting`.

---

### Task 10: The release workflow, dry run

**Files:**
- Modify: `.github/workflows/release.yml` (finish: jobs `preflight`, `windows`, `linux`, `manifest`), `.github/workflows/ci.yml` only if a packaging smoke belongs in every PR (it does not: the dry run is dispatch-only, as in the predecessor)
- Test: workflow dry runs

**Interfaces:**
- Consumes: the `package.cs` commands of Tasks 2, 5 and 6; `tools/native-fetch.cs`; `tools/licenses.cs check`.
- Produces: `release.yml` as `workflow_dispatch` with inputs `tag` (SemVer, leading `v`), `ref` (branch or 40-hex commit for dry runs, `pull/*` and `refs/pull/*` refused), `upload` (default false); jobs: `preflight` (tag shape, tag on `main` or `dev`, key check refuses the placeholder when `upload=true`), `windows` (publish app and CLI with Native AOT, smoke the CLI, license check, native fetch, layout, ZIP, installer, installer smoke, checksums), `linux` (container build, `.deb`, AppImage, smokes, checksums, runtime source archives), `manifest` (GitHub-hosted runner behind the environment `release-signing`, builds and signs the manifest with the secret `RAFFICA_UPDATE_SIGNING_KEY`, verifies it with the source keys, uploads; never runs with `ref` set or `upload=false`).

- [ ] **Step 1:** Run `dotnet run tools/package.cs -- selftest` (it covers the tag and ref-input rules, `TagRules` and `RefInputRule`) and a YAML parse of the workflow (`actionlint` if installed, else `python -c "import yaml,sys;yaml.safe_load(open(sys.argv[1]))" .github/workflows/release.yml`). Expected: PASS.
- [ ] **Step 2:** Push the branch and dispatch the dry run (needs the owner's go because it uses their runners; `gh workflow run release.yml --ref feature/m26-packaging-and-release -f tag=v0.1.0 -f ref=feature/m26-packaging-and-release -f upload=false`). Expected: `windows` and `linux` jobs green, no upload, `manifest` skipped. Record the run URL and the file list with sizes in the evidence. A workflow file must exist on the default branch to be dispatchable by name: if `gh` answers 404, dispatch with `--ref dev` after the file reached `dev`, or run the jobs' commands by hand and say so.
- [ ] **Step 3:** If a job fails, fix the cause (never loosen a check, never skip a smoke) and run again; each failed run is named in the notes.
- [ ] **Step 4: Commit** `ci: release workflow for Raffica PDF, dry run`.

---

### Task 11: README, CHANGELOG, DECISIONS and CLAUDE.md

**Files:**
- Modify: `README.md` (users: what Raffica PDF is, install per platform, first start on Linux and Windows SmartScreen notice, the update switch, the command line, build from source, license), `CHANGELOG.md` (`[Unreleased]` → Added: installers, default app, updater; no release section, a release needs the owner's instruction), `Docs/DECISIONS.md` (entries "Raffica PDF: packaging and release conventions (M2.6)" and "... measurements (M2.6)" with call list, rejected alternatives, the numbers of Task 7 step 5, and the key-loss and runner-compromise risks of the predecessor's decision), `CLAUDE.md` (section "Project-specific", new paragraph "Packaging and release (M2.6)": where the code is, the helper mode, the test build, the dry-run command, the owner gates)
- Modify: `packaging/README.md` (owner checklists: generate the key with `package.cs update-keygen` outside the repo and store it as the secret `RAFFICA_UPDATE_SIGNING_KEY`, enter the public key in `UpdateTrust.cs`, create the environment `release-signing`, make the repository public, the manual update check on Windows and Linux, the first-release command)
- Test: `dotnet run tools/roadmap.cs -- check`, `NoPagewingLeftoverTests`

**Interfaces:**
- Produces: the user-facing and owner-facing documents; no code.

- [ ] **Step 1:** Write the documents. Keep README short (what, install, update, build); every owner action is a numbered checklist with the exact command.
- [ ] **Step 2:** Compare the Pagewing 1.0 inventory of 2.1 (roadmap, marks `(P<n>)`, scope changes of 2026-11-04) with the app by hand and write the result as the evidence of the fourth acceptance criterion: every mark is done, or open with its reason (tabs between windows, open in a new window, "reduce animations" are in the backlog; density and the separate high-contrast switch were not rebuilt on purpose; P8 and P8.1 are this milestone).
- [ ] **Step 3:** Run `dotnet run tools/roadmap.cs -- check`. Expected: valid.
- [ ] **Step 4: Commit** `docs: README, changelog, decisions and project notes for packaging and release (M2.6)`.

---

### Task 12: Open items, full verification, roadmap

**Files:**
- Modify: `Docs/roadmap.json` (tick tasks and criteria with evidence, "Open items" block as in 2.5, status `done` with `completedOn`), `Docs/ROADMAP.md` (generated)
- Test: the full suite and the commands of "Done means"

**Interfaces:**
- Produces: the final state of the milestone.

- [ ] **Step 1:** Run, in this order, and keep the output: `dotnet build Raffica.slnx -c Release`; `dotnet run tools/native-fetch.cs -- --rid win-x64 --out artifacts/native/win-x64` (needs `GH_TOKEN` while the repository is private; skip when the folder already exists); `RAFFICA_REQUIRE_OCR=1` with `dotnet test Raffica.slnx -c Release --no-build --max-parallel-test-modules 1`; `dotnet format Raffica.slnx --verify-no-changes`; `dotnet run tools/roadmap.cs -- check`; `dotnet publish src/Raffica.Pdf/Raffica.Pdf.csproj -c Release -r win-x64 -o <empty folder>` (0 warnings, size noted), then `RafficaPdf.exe --print-string TabView --lang de` printing `Ansicht`. Expected: all green.
- [ ] **Step 2:** List the open items that need the owner's machines or decisions, each with the command that checks it, in the roadmap block "Open items" of 2.6: (a) the real update key, its secret and the entry in `UpdateTrust.cs` (`dotnet run tools/package.cs -- update-keygen --out <path outside the repo>`, then the checklist in `packaging/README.md`); (b) the environment `release-signing` and the repository visibility (`gh repo edit benjo-code/raffica --visibility public` is the owner's action, listed, never run here); (c) the update chain `v0.1.0` → next test release with the **test** key on Windows (`package.cs update-smoke`) and the manual run on a Linux desktop with the AppImage (steps in `packaging/README.md`), unless Task 8 and the Linux machine covered them; (d) the install and uninstall of the `.deb` on Debian or Ubuntu (`sudo apt install ./raffica-pdf_<v>_amd64.deb`, open a PDF, `sudo apt remove raffica-pdf`); (e) default app on a real Windows 11 and a real Linux desktop; (f) printers (carried over from 2.1); (g) SmartScreen and the missing Authenticode signature (the installer is unsigned, the README says so); (h) the first release number and the command `gh release create vX.Y.Z --target main ...` followed by `gh workflow run release.yml ...` from the predecessor's procedure; (i) `StartupWMClass` against `xprop WM_CLASS` on Linux if not confirmed in Task 6.
- [ ] **Step 3:** In `Docs/roadmap.json` tick each task as it was finished and each acceptance criterion only with its proof: criterion 1 names the dry-run workflow run (Task 10), criterion 2 records the Windows test-chain output (Task 8) and what stays open on Linux, criterion 3 names the dry-run run and keeps "first release" as an open item, criterion 4 cites Task 11 step 2. Record scope changes (the `TitleBar.Trailing` slot, the regenerated test vector, the imported-tools list of call 3). Then `dotnet run tools/roadmap.cs -- generate` and `check`.
- [ ] **Step 4: Commit** `docs(roadmap): milestone 2.6 done` and open the PR to `dev` (squash or merge commit as decided in Global Constraints; the owner confirms before merging because the PR changes `.github/workflows`).

---

## Self-review

- **Spec coverage:** installers and ZIP, `.deb`, AppImage (Tasks 5, 6, 10); default-app registration and first-start card (3, 4); updater with key, address, switch and unsaved prompt (7, 8, 9); license notices and About card (5); release workflow (10); README and CHANGELOG (11); open items with commands (12); Pagewing inventory (11). Not covered by design: the real key, the public repository, the first release (owner gates, listed in Task 12).
- **Types:** `UpdateFlow`, `UpdateInstallGuard`, `IUpdateApplier`, `PackageForm`, `RegistrationScope`, `IRegistryWriter`, `IDefaultAppService` are used with the same names in every task that mentions them. `PackageFormDetector.DebDirectory` changes once, in Task 6, together with its tests.
- **Dependencies between tasks:** 1 → 2 → {3, 4} → 5 → 6; 7 needs 4's strings only; 8 needs 5 (installer) and 7; 9 needs 7; 10 needs 5, 6; 11 and 12 last. Tasks 3, 4 and 7 may run in parallel after Task 2 if two executors are used.
