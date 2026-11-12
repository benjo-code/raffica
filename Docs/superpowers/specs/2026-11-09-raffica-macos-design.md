# Raffica on macOS – Design

Status: approved by the owner on 2026-11-09 · Date: 2026-11-09 · Author: brainstorming session (M3)

Items marked **(call)** are decisions made by the author of this document, not by the project owner. Some the owner delegated explicitly ("your recommendation"); those are marked **(call, delegated)**. Everything else was decided by the owner in the session or is settled in an existing document that this one names.

## 1. Intent

Raffica PDF `v0.1.0` ships for Windows and Linux. This subproject brings the same app to macOS, on Intel and Apple-silicon Macs, as a real Mac app: menu bar, Finder and Dock integration, trackpad, Retina, a `.dmg` with a documented first launch, and the same updater and default-app offer as the other platforms.

macOS comes now, before Write, because every later app inherits the platform work done here (native libraries, bundle, menu bar, file activation, packaging). Building it once for one finished app is cheaper than retrofitting it into four.

Speed stays the core promise. The Mac build must not be the slow one, and the way that is checked is spelled out in section 6.

## 2. Goals

1. **Feature parity with Windows and Linux (owner).** Everything Raffica PDF `v0.1.0` can do works on macOS: viewing, search, annotate, forms and signing, organize pages, redact, OCR, text-line editing, printing, the default-app offer and self-updates. The CLI `raffica` ships too.
2. **Both architectures (owner).** `osx-x64` (Intel) and `osx-arm64` (Apple silicon). The owner tests Intel by hand on a MacBook Pro 2016 running macOS 15 through OpenCore Legacy Patcher. Apple silicon is checked only automatically on GitHub-hosted runners and may be released that way. The README marks it "not tested by hand" (owner).
3. **Feels like a Mac app (owner).** Section 5 lists what that means.
4. **Distribution as decided (existing decision, `Docs/DECISIONS.md`, "macOS without paid Apple account").** Ad-hoc signing only, no Developer ID, no notarization, a `.dmg`, and the first launch documented in the README.
5. **Little CI cost (owner).** macOS jobs run as rarely as possible without losing a check (section 7).

## 3. Non-goals and anti-goals

- No new PDF features. Everything new goes to the backlog, as in subproject 3.
- No paid Apple account, no notarization, no Mac App Store, no sandboxing.
- No universal binary **(call)**. Two `.dmg` files, one per architecture. A universal build would double the download for every user.
- No macOS-native print panel **(call, delegated)**. Printing uses the app's own print card over CUPS, as on Linux (section 5.6). The native panel goes to the backlog.
- No ribbon key tips on macOS **(call, delegated)** (section 5.4).
- No migration. There are no Mac users yet, so the Mac config folder starts clean.
- No measurement-based limits for Apple silicon (section 6).
- Anti-goal: a Mac build that is a Windows app in a Mac window. Examples are an empty menu bar, Finder double-clicks that do nothing, ⌥ that swallows typing, a quit on closing the window, and blurry pages on Retina.
- Anti-goal: macOS jobs on every pull request.

## 4. Constraints

- The stack is unchanged: .NET 10 Native AOT, Avalonia 12, PDFium, and Tesseract through the C API. Each Mac build is built on macOS; Native AOT cannot cross-compile from Windows or Linux. On macOS one machine can build both architectures; the build machine needs Xcode or its command-line tools.
- The minimum macOS version is the higher of the minimums of .NET 10 and Avalonia 12. The builder looks both up, sets `LSMinimumSystemVersion` from them and records the result in `Docs/DECISIONS.md`. The owner's Mac runs macOS 15.
- Platform code lives in `src/Raffica.Pdf/Platform/MacOS` (and the matching Core or Shell spot for shared hosting code), never in UI code (`CLAUDE.md`, PDF app, "Platform code"). The app keeps referencing only `Raffica.Core` and `Raffica.Shell`.
- Interop with macOS frameworks (CoreServices, CUPS, `libSystem`) uses `LibraryImport` and must stay Native AOT safe, with no reflection. Objective-C runtime calls are allowed only where no C API exists; each such use is named in the milestone's plan.
- Every user-facing string exists in English and German (`Strings.en.resx`, `Strings.de.resx`, the existing guard tests apply). Menu items that macOS defines (Services, Hide, Quit and similar) use Apple's own terms in each language. Items that run an app command use the command's existing label, so the menu and the ribbon say the same thing.
- The repository is public, so GitHub-hosted standard runners, macOS included, cost nothing. Larger runners are never used.
- Ad-hoc signing: every Mach-O file and the bundle are signed with `codesign -s -` after the `.app` is assembled (`CLAUDE.md`, macOS). This is required on Apple silicon.
- Windows and Linux behaviour does not change unless this spec says so.

## 5. What "feels like a Mac app" means

### 5.1 Bundle and identity

- **(call)** Bundle `Raffica PDF.app` with the executable `RafficaPdf`, the CLI `raffica` next to it in `Contents/MacOS`, and native libraries in `Contents/MacOS` or `Contents/Frameworks` (the builder's choice, loaded through `@rpath` or `@executable_path`). The bundle identifier is `io.github.benjo-code.raffica.pdf`.
- `Info.plist` contains:
  - `CFBundleDocumentTypes` for PDF (`com.adobe.pdf`, role Editor, rank Alternate);
  - the version from the build;
  - `NSHighResolutionCapable`;
  - `LSApplicationCategoryType` productivity;
  - `LSMinimumSystemVersion` (section 4).
- The icon is an `.icns` derived from the PDF brand master in `Docs/design/icons/brand/`; `Docs/design/icons/README.md` says how.
- **(call)** The CLI is not put on `PATH`. The README shows the one-line symlink.

### 5.2 Menu bar

The app has a full native menu bar (Avalonia `NativeMenu`) next to the ribbon, which stays as it is. Every app item runs the same command as the ribbon through the command catalog, with the same enabled and checked state; the existing disabled reasons stay the ribbon's business. **(call)** The menus are:

| Menu | Items |
|---|---|
| Raffica PDF | About Raffica PDF · Check for updates… · Settings… ⌘, · Services · Hide ⌘H · Hide others ⌥⌘H · Show all · Quit ⌘Q |
| File | Open… ⌘O · Open recent ▸ · Close tab ⌘W · Save ⌘S · Save as… ⇧⌘S · Print… ⌘P |
| Edit | Undo ⌘Z · Redo ⇧⌘Z · Cut · Copy · Paste · Select all · Find ⌘F |
| View | Zoom in, out, fit · page layouts · show and hide panes · Enter full screen ⌃⌘F |
| Window | macOS's standard items (Minimize ⌘M, Zoom, Bring all to front) |
| Help | macOS's menu search field · Raffica PDF help (the F1 card) · Keyboard shortcuts |

Edit items act on the focused text box when one has focus, and on the document otherwise. Items without a catalog command (Cut, Paste, Open recent and the system items) are wired directly. They do not get new ribbon commands. The builder audits every ⌘ shortcut of the PDF app against the shortcuts macOS reserves (at least ⌘H, ⌥⌘H, ⌘M, ⌘Q, ⌘Tab, ⌘Space, ⌘\`, ⌃⌘F, ⇧⌘3/4/5). A clash is moved on macOS only, and a test pins the result.

### 5.3 Finder, Dock and the app life cycle

- A PDF opened from the Finder (double-click, "Open With", dropped on the Dock icon) opens as a tab. macOS delivers these files as an Apple event, not as `argv`. The app handles them through Avalonia's activation API (`IActivatableLifetime`, file activation), at first start and while it runs. The single-instance pipe stays for starts from the command line.
- **(call)** Closing the last window does not quit the app, as in Preview. Quit is ⌘Q or the menu. Clicking the Dock icon with no window open (reopen) shows a new window with the start view. Opening a file with no window open creates the window. The close questions (unsaved marks, plans, running jobs) run on closing a window and on ⌘Q, the same way they run on closing the window today. Today the single-instance listener, the update flow and the dialog queue hang off `PdfWindow`. They must survive without a window, or come back with the new one. This is real work in 3.2, not a detail.
- The window keeps the system traffic-light buttons on the left with the 80 px inset of the title bar (`TitleBar.LeadingInset`, shell spec). The drag area, the inset, full screen and the Window menu are checked on the owner's Mac.
- Settings, recent files, signatures, the crash log and the update staging live in `~/Library/Application Support/Raffica/...` (`UserDataPaths`, `ShellSettingsStore`). Today these resolve to `~/.config` on macOS. Caches follow the same rule under `~/Library/Caches/Raffica`. `--settings-file` keeps redirecting everything.

### 5.4 Keyboard

- Every shortcut uses ⌘ where Windows and Linux use Ctrl (already prepared in `KeyNames`, `FrameKeys` and `PdfShortcut`). ⌃Tab keeps switching tabs.
- **(call, delegated)** There are no ribbon key tips on macOS. A bare ⌥ tap does nothing. On the German Mac layout ⌥ types characters such as @ (⌥L) and € (⌥E), so taking it would break typing. Every command stays reachable by keyboard:
  - the menu bar, which macOS reaches with ⌃F2;
  - the search field ⌘K, which finds every ribbon command;
  - Tab through the ribbon when the system's keyboard navigation is on.

  The F1 help card shows no key-tip table on macOS. This closes the backlog entry "Option key handling on macOS". Windows and Linux are unchanged.
- F6 pane focus stays (fn+F6 on laptop keyboards).

### 5.5 Pointer, trackpad, display

- Pinch to zoom on the trackpad zooms the page around the pointer, using the same zoom steps and limits as the zoom commands. Two-finger scrolling is smooth and follows the system's natural-scrolling setting (Avalonia passes it through).
- Pages render sharp on Retina: the page view renders at the window's scaling, 2× on the owner's display. The builder checks this and fixes it if the tile cache assumes 1×.
- Light and dark follow the system, as today. High contrast keeps the app's own palette; the system's "Increase contrast" is not read **(call)**.

### 5.6 Printing

**(call, delegated)** Printing uses the existing CUPS print service with the app's own print card, as on Linux. On macOS the service binds `libcups.2.dylib`, and `PlatformPrintServices.Create` returns it there. The Linux print rules apply unchanged: always light, annotations printed, a document that forbids printing locks the command, background job with progress and Cancel. Real printing is checked on the owner's Mac with a real or a PDF printer. The macOS print panel (Preview, PDF export, presets) is a backlog entry.

### 5.7 Default app

The first-start card "Set Raffica as default for PDFs?" (with its 30-day snooze) is offered on macOS as well. Setting and reading the default go through LaunchServices for `com.adobe.pdf` (C API, or `NSWorkspace` if the C API no longer works on the minimum version; the plan names which). After setting the default, a PDF double-clicked in the Finder opens in Raffica. The `--register-file-types` switches stay Windows-only, because the bundle's `Info.plist` does that job on macOS.

### 5.8 Updates

**(call)** The app updates itself on macOS, as the AppImage does on Linux:

1. It downloads the `.dmg` of its own architecture.
2. It checks the signature of the manifest, the size and SHA-256, the same checks as everywhere.
3. It mounts the image read-only and hidden.
4. It copies the new `.app` into staging next to the installed one.
5. After the app quits it swaps the bundles and restarts with the session (the existing `--finish-update` and `--restore-session` paths).

Expectation to verify: a file the app downloads itself gets no quarantine attribute, so no Gatekeeper prompt appears after an update. **(call)** If the check shows that the staged copy does carry one, the updater removes it from the staged bundle. It does this only after the signature, size and SHA-256 checks have passed.

The app falls back to the existing "download page" path when it cannot write next to its own bundle (an admin-owned `/Applications`) or when it runs translocated (started from a quarantined download or the mounted `.dmg`). The update card words that case in both languages.

### 5.9 Files and paths

macOS volumes are case-insensitive by default (APFS). Today the code treats every non-Windows system like Linux and compares paths with `StringComparison.Ordinal` in about 15 places, among them `Workspace.PathComparison`, the save and job paths, `RecentFiles`, the CLI `OutputGuard` and `FileIdentity.AreSameFile`, which answers null for every pair on macOS. Required behaviour on macOS:

- Two spellings that name the same file are the same file: "already open", "no write onto a file another tab has open", "save in the original file" and the CLI output guard.
- `FileIdentity.AreSameFile` answers from the device and inode (`stat` in `libSystem`) instead of null.

The builder chooses the mechanism, for example identity first and the volume's case rule as a fallback. Tests pin `Bericht.pdf` and `bericht.pdf` on a case-insensitive volume.

### 5.10 Native libraries

- **PDFium:** the `bblanchon.PDFium.macOS` package (same version as the Windows and Linux packages) for both RIDs. Its license is added to `third-party/licenses.json`.
- **Tesseract:** `native-tesseract.yml` gets a macOS job that builds `libtesseract.dylib` for `osx-x64` and `osx-arm64` as a new prerelease build. `tools/native-fetch.cs` pins size and SHA-256 per RID, as for Windows and Linux. `TesseractLibrary.FileName` and `OcrTestLibrary` learn the `.dylib`. A Mach-O check (the counterpart of `check-binary`'s ELF rules, for example `otool -L`) allows only system libraries and files inside the bundle.

## 6. Speed

**(call, delegated)**

- **Size: binding.** The size of the published app, of the `.app` and of each `.dmg` is measured on the hosted runner and does not depend on the machine. Budgets are set from the first measurement with the M1 rule (median rounded up, limit = budget × 1.25 once), in `s_pdfBudgets` of `measure.cs` and checked in CI like the Windows and Linux size steps.
- **Time and memory: binding for the owner's Intel Mac only.** `measure.yml` (`target=pdf`, `platform=macos`, the owner's self-hosted runner) measures the same cases as 2.1: cold, warm, second file, 1000 pages, scroll p99, memory. Budgets follow the M1 rule and are valid only for that machine. They guard against regressions and say nothing about other Macs. The runs happen only when the owner dispatches them. The same runs decide the Mac's rendering default: Avalonia's default path or software rendering, as decided for Windows in "Raffica PDF: software rendering is the Windows default". `RAFFICA_RENDERING` stays the opt-out.
- **Apple silicon: recorded, not judged.** A measurement on a hosted arm64 runner is recorded in `Docs/DECISIONS.md` without a limit, because hosted VMs vary too much. Release and CI smokes have a coarse ceiling: the app must start and print a string within a few seconds, the value set by the builder. This catches gross faults, such as the x64 build running under Rosetta.
- Budgets are never loosened to make a check pass. A miss is a finding for the owner.

## 7. CI and release

**(call, owner's direction "as few macOS runs as possible, still keep quality")**

- **`ci.yml` gets a `build-macos` job on `macos-15` (arm64).** It runs restore, build, the full test suite with `RAFFICA_REQUIRE_OCR=1`, a Native AOT publish of both RIDs, and the smokes (`--print-string` in English and German; the x64 build runs under Rosetta). The aggregate `ci` check stays the only required check. It treats a skipped macOS job as passed. The job runs only:
  - on pull requests into `main` (releases and hotfixes);
  - on pull requests that touch platform-near paths. The builder writes the list into `ci.yml`'s `changes` job; it holds at least `src/**/Platform/**`, `src/Raffica.Pdf/Updating/**`, `src/Raffica.Pdf/Packaging/**`, `src/Raffica.Core/Hosting/**`, `src/Raffica.Pdfium/**`, `src/Raffica.Ocr/**`, `packaging/**`, `tools/package.cs`, `tools/native-fetch.cs`, `Directory.*.props`, `*.csproj` and the workflow files;
  - on pull requests with the label `ci:macos`, set by hand when a change may affect the Mac.
- `release.yml` gets a `macos` job on a hosted macOS runner. It builds both architectures, assembles and signs the bundles, makes the two `.dmg` files and `SHA256SUMS-macos.txt`, and runs the release smokes:
  - `codesign --verify --deep --strict`;
  - the Mach-O check;
  - the architecture of each binary;
  - mount, copy, start, and the string check per architecture.

  The `manifest` job also waits for it, and `update-manifest` takes the macOS sums and two new file kinds (for example `macos-arm64-dmg`, `macos-x64-dmg`). Unknown kinds are already ignored by older clients, so the manifest `format` stays.
- No self-hosted runner is involved except the owner's Mac for `measure.yml`, which already exists.
- **Release names (call):** `RafficaPdf-<v>-macos-arm64.dmg` and `RafficaPdf-<v>-macos-x64.dmg`. The image holds the app and an `/Applications` link, with no background picture.
- The README gets a macOS section:
  - which `.dmg` to pick;
  - the drag into Applications;
  - the first launch through System Settings → Privacy & Security → "Open Anyway" (on macOS 15 a right-click → Open no longer skips the check);
  - the terminal alternative `xattr -dr com.apple.quarantine`;
  - the CLI symlink;
  - the note that the Apple-silicon build is not tested by hand.

## 8. Milestones

The roadmap gets three milestones under subproject 4 (ids 3.1 to 3.3). Each milestone gets its own plan.

| # | Milestone | Contents | Acceptance (verifiable) |
|---|---|---|---|
| 3.1 | Runs on the Mac | PDFium and Tesseract for `osx-x64` and `osx-arm64` (packages, native build job, pins, `.dylib` names, Mach-O check); paths under `~/Library`; case-insensitive path identity and `FileIdentity` on macOS; single-instance pipe checked on macOS (socket path length); Finder and Dock file activation; the `build-macos` CI job with its path filter and label; every test passes on macOS or is skipped there with a named reason. | `build-macos` is green on a PR with the label; the identity tests for two spellings pass on macOS; on the owner's Mac a published `osx-x64` build opens a PDF by double-click in the Finder and by `open -a`, and recognizes text with OCR (manual check, recorded). |
| 3.2 | Feels like a Mac app | Menu bar with all items of 5.2 bound to the command catalog; reserved-shortcut audit; no key tips and no ⌥ capture on macOS; app life cycle without windows (reopen, ⌘Q with the close questions); pinch zoom; Retina rendering; title bar, inset, full screen; printing over CUPS on macOS; German and English menu wording. | Tests for the menu-to-command binding, the shortcut audit and the absent ⌥ capture; on the owner's Mac: menu bar, ⌘Q with unsaved marks, Dock reopen, pinch zoom, sharp pages at 2×, a printed page (manual checks, recorded). |
| 3.3 | Package and release | `.app` with `Info.plist`, `.icns` and ad-hoc signature; two `.dmg`; default-app offer through LaunchServices; self-update with the `.dmg` and its fallbacks; macOS in `release.yml` and the update manifest; README section; size budgets; Intel time and memory budgets and the rendering default; the Apple-silicon record. | `release.yml` dry run green with the macOS job; the release smokes pass for both architectures; on the owner's Mac: first launch through "Open Anyway", default app set, an update from one test build to the next without a Gatekeeper prompt (manual checks, recorded); `measure.yml` within the recorded limits. |

Prerequisites: 3.1 needs 3, 3.2 needs 3.1, 3.3 needs 3.2. **(call)** Write's milestone 4 moves its prerequisite from 3 to 3.3, so the order "macOS before Write" of the suite spec stays true.

## 9. Left to the builder

- Where the native libraries sit inside the bundle, and how the `.icns` is generated.
- The exact path list of the CI filter (the minimum is in section 7) and the label name if `ci:macos` clashes with something.
- How the Tesseract dependencies are linked on macOS (static where possible, system libraries only).
- The mechanism of the path identity (section 5.9) and the LaunchServices API (section 5.7), within the constraints of section 4.
- The coarse start ceiling of the smokes.
- Splitting a milestone further if its plan is too large; the roadmap then gets the extra ids.

## 10. Decided with the owner's delegation, and open

- Decided with the owner's delegation: no key tips on macOS (5.4), printing over CUPS with the own card (5.6), speed checks (section 6). The owner also approved the CI direction (section 7) and the three-milestone split (section 8) as proposed.
- Open, with an owner check that only a real machine can answer:
  - whether a self-downloaded update really carries no quarantine attribute (5.8);
  - the behaviour on Apple silicon beyond the hosted smokes;
  - real printers on macOS.
- `Docs/DECISIONS.md` "x86_64 only" (M2.6) is amended in 3.3: macOS ships `arm64` as well; Windows and Linux stay x86_64.
- The version number of the first release that includes macOS is not decided here.
