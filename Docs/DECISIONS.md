# Raffica – Decisions

Log of architecture and technology decisions. Newest entries on top.

**When an entry is due:** choice of framework, library or version, architecture and interface decisions, deliberately rejected approaches, workarounds with consequences – anything you would otherwise discuss again in half a year.

**Rules:** Do not rewrite entries afterwards. If a decision is revised, add a new entry and note "Superseded by: <title>" in the old one.

<!-- Template:

## YYYY-MM-DD – <Title>
**Context:** <Which problem, which constraints?>
**Decision:** <What was decided?>
**Rationale:** <Why?>
**Rejected alternatives:** <What else was considered and why not>
-->

## 2026-11-04 – Raffica PDF: viewer conventions (M2.1)
**Context:** Milestone 2.1 builds the PDF viewer on the shared frame of M1.2. The imported core already has its own settings store, the frame has a settings dialog, tabs and a search field, and Pagewing 1.0 behaved differently from the frame in a few places. The calls below are the author's calls while planning (`Docs/superpowers/plans/2026-11-04-milestone-02-1-viewer.md`); the owner can overturn them.
**Decision:**
1. **Names:** project `src/Raffica.Pdf` (`AssemblyName` `RafficaPdf`, root namespace `Raffica.Pdf`), tests `tests/Raffica.Pdf.Tests`. This matches `RafficaGallery` and the CLI `raffica`.
2. **Two settings files, no merge:** the shell's `SettingsStore` (`Raffica/Pdf/settings.json`, `ShellSettings`: theme, language, compact bar) and Core's `SettingsStore`/`AppSettings` (`<UserDataPaths.ConfigDirectory()>/pdf-settings.json`: page appearance, default layout and zoom mode, last printer, print scaling). The overlapping `Theme`, `Language` and `Density` fields of `AppSettings` are not read by the app; removing the overlap is a backlog entry. Recent files and positions use Core's `RecentFilesStore` (`recent.json` in the same folder).
3. **Settings dialog extension:** `SettingsDialogOptions(bool ShowAutosave = true, IReadOnlyList<SettingsExtraRow>? ExtraRows = null)`. The app builds the editors of its extra rows itself and reads them in its `onOk`; when the autosave row is hidden the dialog returns `current.Autosave` unchanged.
4. **Palette entries:** the frame's search gets an optional `ExtraResults` provider for entries that are not ribbon commands (open tabs, recent files, outline entries, "Go to page N"). The ribbon commands stay the only thing the frame itself searches.
5. **Tab semantics:** a file that is already open is selected, not opened twice. Dragging tabs between windows (Pagewing 4.1) and "open in a new window" (Pagewing 4) are not built: the frame's `DocumentTabs` has no drag-out. They are in the backlog for after 2.6 unless the owner asks earlier.
6. **Test PDF generator:** `tools/make-testpdfs.cs` is not imported in 2.1. The fixtures under `testdata/pdfs` (`text-rich`, `text-dense`, `thousand-pages`, `outline`, `rotated-page`, `wide-page`, `cropbox`, `dark-sample`, `encrypted_hello_world_r3`, `print-mixed`, `not-a-pdf`, `empty`) cover every viewer test; a task that needs a new fixture says so.
**Rationale:** (1) one naming scheme for the suite. (2) The shell settings are the same for every app, while page appearance, layout and print scaling are PDF-only and already live in tested Core code; merging them would mean a migration for no user-visible gain, and reading the overlapping fields would let two files disagree. (3) Autosave contradicts "never damages the original" (see "Raffica PDF: platform code, autosave, OCR build and releases (M2)"), and an additive option leaves the other apps unchanged. (4) The spec asks for a palette with tabs and contents, and Pagewing's palette had these groups; a provider keeps the frame free of document knowledge. (5) In 2.2 each open tab can hold unsaved changes, so two tabs of one file would hold diverging dirty state; Pagewing opened a second tab. Drag-out needs a workspace across windows, which no other app needs yet. (6) The fixtures cover the viewer, and a generator is only worth its maintenance once a milestone needs a document that does not exist yet.
**Rejected alternatives:** One merged settings file (migration and a second source of truth for theme and language); forking the settings dialog for PDF (the frame would diverge); making the search a PDF-specific control (the title bar is shared); opening a second tab for a file that is already open as Pagewing did; importing the generator "just in case"; building tab drag-out in 2.1 (scope beyond the viewer; the spec says the frame's behaviour does not change).

## 2026-11-04 – Raffica PDF: platform code, autosave, OCR build and releases (M2)
**Context:** Core holds the PDF features, but the platform parts (printing, default-app registration, single instance) were not imported from Pagewing, the shell's settings dialog has an autosave row, the OCR library is still fetched from a prerelease of Pagewing's repository, and the updater and release addresses are placeholders. Spec: `Docs/superpowers/specs/2026-11-04-raffica-pdf-design.md`, sections 4, 6 and 9.
**Decision:** Platform code may be ported from Pagewing in an import commit followed by an adaptation commit; UI code is never ported. The PDF app hides the autosave row through a small additive shell option, so the other apps keep today's dialog. The Tesseract library is built in this repository as the first task of milestone 2.5 and the fetch from Pagewing's prerelease is dropped; the CodeQL and hardware-measure workflows return in 2.5 and the release workflow in 2.6. Releases live in this repository. The owner generates the update signing key and keeps it in a CI secret; the repository must be public by the first release, and nothing on GitHub changes without the owner's instruction.
**Rationale:** Platform code is hard to get right and has no look to be redesigned; views are the new UI's job. Autosave contradicts "never damages the original" and Pagewing decided against it. Installers must not depend on another repository. A key the owner holds cannot leak through the plan.
**Rejected alternatives:** Fetching Pagewing's prerelease for the shipped build (a dependency on a repository this project does not control). Porting Pagewing's views (they would bring back the old look). A recovery file or autosave (no autosave in Pagewing 1.0, so no parity reason).

## 2026-11-04 – Raffica PDF: speed budgets are measured, not inherited (M2)
**Context:** Pagewing 1.0 had absolute budgets (cold 400 ms, warm 200 ms, next file in a running instance 100 ms, 1000-page file 300 ms to the first sharp page). The frame alone measures 375 ms cold on the owner's Windows machine ("Gallery frame baseline (M1.2)"), so the old cold budget does not fit the new frame.
**Decision:** As in milestone 1, milestone 2.1 measures the PDF app, sets each budget from the median, and sets the limit at budget × 1.25 once; budgets and limits are recorded here and checked in CI with the `measure.yml` machinery on Windows and Linux (macOS stays indicative). Pagewing 1.0 and Chrome are measured as references. Being slower than Pagewing 1.0 on the same machine is a recorded finding for the owner, like a miss of a budget; the plan of 2.1 looks for the cause first. Budgets are never loosened to make a check pass.
**Rationale:** Speed is the core promise, and a limit that comes from a measurement can be defended; a number inherited from another frame cannot.
**Rejected alternatives:** Keeping Pagewing's absolute numbers (they fail before the app exists). Setting budgets before measuring (guesses).

## 2026-11-04 – Raffica PDF: scope and order (M2)
**Context:** Milestone 2 creates the spec of the PDF app. Pagewing 1.0 is already feature-rich, and the suite needs a first app that people can use.
**Decision:** The first Raffica PDF release has everything Pagewing 1.0 had, defined by its roadmap and README (an inventory is the first task of 2.1), and nothing new; the CLI `raffica` ships with the app. Windows and Linux only (macOS is subproject 4); no migration of Pagewing settings or files. Order: 2.1 Viewer, 2.2 Annotate, 2.3 Forms and signing, 2.4 Organize pages, 2.5 Redact, OCR and line editing, 2.6 Packaging and release. Non-goals are those of Pagewing: certificate signatures, XFA, JavaScript in PDFs, PDF-to-Word, cloud, accounts, an AI assistant, preflight, 3D and multimedia.
**Rationale:** Parity makes the first release a replacement, not a rebuild; features come in the order that lets each milestone be used and tested alone, and packaging last avoids packaging a moving target.
**Rejected alternatives:** A slimmer first release with the viewer only (it would not replace Pagewing). New features in the first release (they would mix a rebuild with design work).

## 2026-11-04 – Gallery frame baseline (M1.2)
**Context:** Milestone 1.2 ships the shared app frame in the gallery app (`--view frame`). The speed limits of "Speed targets of the frame (M1)" apply to it: the gallery's published frame must stay within them, and a value above a limit is a finding for the owner, not a new budget (spec 9.4).
**Decision:** The limit of each binding machine and case is the budget × 1.25, exactly the table of "Speed targets of the frame (M1)". The single copy in code is `CheckCommand` in `bench/shell-probe/measure.cs` (its selftest pins every value).

| Case | Windows limit | Linux limit |
|---|---|---|
| Cold start | 537.5 ms | 250 ms |
| Warm start | 525 ms | 262.5 ms |
| Input latency (median) | 12.5 ms | 25 ms |
| Idle memory | 156.25 MiB | 156.25 MiB |
| Published size | 48.75 MiB | 43.75 MiB |
| Zip | 20 MiB | 20 MiB |

- **Checks:** `dotnet run bench/shell-probe/measure.cs -- check --platform <windows|linux> --in "<gallery CSVs>" [--summary <file>]` compares the medians of a `measure.yml` run (`target=gallery`) with the limits and exits 1 above a limit. `dotnet run bench/shell-probe/measure.cs -- size --check <win-x64|linux-x64> --publish-dir <dir> --fonts-dir src/Raffica.Shell/Assets/Fonts` checks size and zip only and writes no CSV; CI runs it as the step "Size of the published gallery". macOS is indicative and sets no limit. A run flagged "Invalid measurement" (compositor warning) must not count.
- **Measured so far:** the win-x64 Native AOT publish of `src/Raffica.Gallery` is 40.65 MiB, zip 16.80 MiB at the head of the milestone branch (command `dotnet run bench/shell-probe/measure.cs -- size --check win-x64 --publish-dir artifacts/gallery/win-x64 --fonts-dir src/Raffica.Shell/Assets/Fonts`), under the limits of 48.75 and 20 MiB. The probe's own frame was 38.1 / 15.7 MiB. The Linux size was not measured locally; the CI step does it.
- **Baseline (measure.yml, target `gallery`, commit ae4d07c on `dev`, run 2026-11-04, `check` passed on both; CSVs and machine notes in `Docs/measurements/gallery-frame-2026-11-04/`):**

| Case | Windows (run 41788263177) | Linux (run 41788266869) |
|---|---|---|
| Cold start (median) | 375.23 ms | 185.68 ms |
| Warm start (median) | 361.92 ms | 182.15 ms |
| Input latency (median / p99 / max) | 4.53 / 9.59 / 15.98 ms | 16.59 / 36.11 / 80.15 ms |
| Idle memory | 111.34 MiB | 132.3 MiB |
| Published size | 40.65 MiB | 37.25 MiB |
| Zip | 16.8 MiB | 16.38 MiB |

  Windows: AMD Ryzen 7 7800X3D, RTX 4070 SUPER, 1920 x 1080 at 60 Hz, power plan balanced. Linux: HP ProBook (Intel i5-8265U, 15.4 GiB), CachyOS, Wayland session. Every median is under its limit; the Linux input median (16.59 ms against a 20 ms budget) and memory (132.3 MiB against a 125 MiB budget) are the closest to their budgets, memory is above the budget but inside the limit. These medians are the regression baseline for later milestones; compare with `check`, never with a changed limit. `--measure-input` ran to the end on both machines (the exit 3 seen in the development session came from its slow compositor).
**Rationale:** One table in one place in code, so a loosened limit is a visible change. The timings come from the two binding machines through the workflow, not from the development VM.
**Rejected alternatives:** A separate check script (a second copy of the limits); timings from the development VM (3.8 Hz compositor, invalid).

## 2026-11-04 – App frame conventions (M1.2)
**Context:** Milestone 1.2 builds the shared frame (title bar, ribbon, panes, status bar, field codes, start view, settings). The plan listed author's calls; these held, and the details that came up while building are recorded here.
**Decision:**
- **Where:** `src/Raffica.Shell/Frame` (one control per file) and `src/Raffica.Shell/Styles/Frame/*.axaml`. An app supplies data (`RibbonDefinition`, pane content); commands carry their own field code.
- **Compact width:** 1024 px, the frame's minimum window width (`MinWidth` of the gallery's `FrameDemoWindow`), with the compact command bar on. The normal case is 1440 px with the full ribbon. The full ribbon at 1024 px without the compact bar clips groups of the Home tab; that is a recorded limitation, not a tested case (there is no automatic switch to compact by width).
- **Title bar:** drawn by Raffica on Windows and Linux, on Avalonia 12 `Window.WindowDecorations` (`WindowDecorations.BorderOnly`, resize grips kept) together with `Window.ExtendClientAreaToDecorationsHint = true`. Hit testing goes through `Avalonia.Controls.Chrome.WindowDecorationProperties.SetElementRole` with `Avalonia.Input.WindowDecorationsElementRole` (`TitleBar` for the drag area, `MinimizeButton`, `MaximizeButton` and `CloseButton` for the caption buttons, `User` for tabs, "+" and the search field). macOS keeps `Full` decorations and the system buttons (`TitleBar.LeadingInset` 80 px). No system-title-bar fallback is built; the check on the ProBook (CachyOS, Wayland) is open.
- **Search:** the frame's own `CommandMatcher` (word prefixes, case-insensitive, menu items included) over the `RibbonDefinition`, with no dependency on the PDF core. Ctrl+K (the platform's command modifier from `PlatformSettings.HotkeyConfiguration.CommandModifiers`) is handled by `FrameKeys` and ignored while a dialog is open.
- **Settings:** one JSON file through `SettingsStore`, at `SettingsStore.DefaultPath(appName)` (`Raffica/<appName>/settings.json` in the application-data folder). A missing, empty, oversized, unreadable or corrupt file gives the defaults, never an error at start. The language is stored and applies only after a restart.
- **Field codes** (spec 4.5): tab letters, then command codes, then menu item codes (`FieldCodeSession`, `FieldCodeInput`). Bare Alt: both Alt events are consumed so the Windows system menu stays shut. AltGr: Alt with Ctrl held never enters the mode (AltGr is Ctrl+Alt on Windows); on Linux AltGr sends no Ctrl, so text input between Alt down and Alt up counts as another key. F10 enters the mode like Alt. A key that another handler already took ends the mode (`FieldCodeInput` listens with `handledEventsToo`). The mode does not start while the ribbon is not on screen or a dialog is open.
- **Codes:** `RibbonDefinition.Validate` rejects duplicate codes and prefix codes (such as `1` and `12`) per scope (a tab, a menu), because a prefix code makes the longer one unreachable. The demo's codes are the group digit plus the position in the group. Q (search), 6 and 7 (panes) act in the tab-letter stage through app-supplied `FieldCodeTarget`s; the group digits are only a legend.
- **Panes:** F6 moves through a ring of the open panes (`PaneRing`, `PaneFocus`), closed panes are skipped, and with a dialog open F6 stays inside it. In the status bar the field-code hint wins over the focus text.
- **Collapsing:** `AppFrame` collapses an empty tab row and band (the start view sits directly under the title bar); a closed pane collapses its slot and its rule; `DocumentTabs` share the room down to 96 px, then scroll, and keep "+" visible.
- **Key names:** shortcuts are written through `KeyNames` (`Ctrl+K`, `Strg+K`, `Umschalt`, `Entf`). On macOS they are symbols without a separator in both languages (`⌃⌥⇧⌘K`: Ctrl, Option, Shift, Command), a call beyond the plan.
- **Tests:** shell test classes run one at a time (`DisableTestParallelization` in `tests/Raffica.Shell.Tests/TestCulture.cs`) because `ShellStrings.Culture` is shared static state and a parallel run was flaky; the test assembly pins English, and an assembly-level `BeforeAfterTestAttribute` resets `ShellStrings.Culture` and `GalleryStrings.Culture` to English around every test, because a test that sets the process language (the `--print-string` test did, without restoring it) would otherwise leak it into whichever test runs next; the order of the tests is not fixed and differs between Windows and Linux.
**Rationale:** Each call keeps the frame data-driven and testable headless, and avoids a second copy of anything the shell already has.
**Rejected alternatives:** The predecessor's palette matcher in the shell (ties the shell to the PDF core).
**Not verified:** The real behaviour of bare Alt, AltGr, F10 and the Transient menu flyout on Windows, Linux and macOS; the caption buttons on Windows (the roles map to the system hit codes and the buttons also set `WindowState`, so a double maximize is possible); the macOS drag area, inset and Option key; the title bar on Wayland. The list of manual checks is in the evidence of milestone 1.2 in `Docs/roadmap.json`.

## 2026-11-03 – Shell project and token conventions (M1.1)
**Context:** Milestone 1.1 creates the shared shell with tokens, fonts, icons and the base controls.
**Decision:**
- Projects: `Raffica.Shell` (tokens, styles, controls, strings), `Raffica.Gallery` (the gallery app) and `Raffica.Shell.Tests`.
- Resource keys are `Raffica.Color.<Group>.<Name>` and `Raffica.Brush.*`. `ShellTheme.Apply` sets the theme mode, the app ink and high contrast, and re-resolves the brushes when the theme variant changes.
- The shell is layered over `FluentTheme`, which stays for what is not restyled (scroll bars and similar). Component styles use semantic tokens only, no hex literals (`NoHexInComponentStylesTests`).
- Focus ring: 2 px thick with a 2 px offset, as literals in AXAML (a `Thickness` cannot be derived from `Metrics` doubles). `AdornerLayer.IsClipEnabled=false` is needed so the ring is not clipped.
- `StrokeIcon` takes the inherited text colour when `Stroke` is unset. Milestone 1.2: the shell `Window` style must set `TextElement.Foreground` to `Text.Primary`.
- Tabular figures are an opt-in `tnum` class. Spec 4.1 says all UI text, the plan said where numbers align; revisit with the status bar in 1.2.
**Rationale:** One place for every value, so contrast and the no-literal rule can be tested.
**Rejected alternatives:** Replacing `FluentTheme` (would mean restyling every framework control at once).

## 2026-11-03 – Avalonia.Headless.XUnit with xunit.v3 3.2.2 in the shell tests only
**Context:** The shell tests need a headless Avalonia host. `Avalonia.Headless.XUnit` 12.1.4 fails test discovery with xunit.v3 4.0.x, the version the other test projects use.
**Decision:** `Raffica.Shell.Tests` pins xunit.v3 3.2.2 through a `VersionOverride`. The tests run real Skia headless (`UseSkia`), so the font and weight tests measure real text.
**Rationale:** The headless host is the only way to test controls without a display.
**Removal condition:** When `Avalonia.Headless.XUnit` supports xunit.v3 4.x, drop the `VersionOverride`.
**Rejected alternatives:** Downgrading xunit.v3 in every test project (no reason to hold the others back).

## 2026-11-03 – Dark scrim value
**Context:** The dialog scrim in the dark theme needs a value; the spec gives only the light one.
**Decision:** Light is the spec value `#521A1D21`. Dark is `#80000000` (black at 50 %), so the frame behind the dialog stays visible in the gallery screenshot.
**Rationale:** A darker scrim on a dark frame would hide it.

## 2026-11-03 – Public Sans as static instances
**Context:** The variable font did not apply weights under Skia: the weights test showed a single advance width for all weights.
**Decision:** Static instances 400, 500 and 600, generated with the fontTools instancer (command in `src/Raffica.Shell/Assets/Fonts/SOURCES.md`). It costs 3.03 MB.
**Rejected alternatives:** The variable font (weights do not apply).

## 2026-11-03 – High-contrast palette
**Context:** The spec has no high-contrast values; it only says (section 6) that the frame switches to "a token set built from the system colours, as the predecessor did".
**Decision:** A fixed palette in every app: black ground, white text and edges, yellow ink (the same mechanism as the predecessor's fixed palette). The contrast tests verify it. Fluent controls use the Dark palette under high contrast.
**Deviation from the spec:** The spec asks for a token set built from the system colours; the implementation is a fixed palette. The task report records the values as the shell's own and gives no reason for not using system colours. Owner to confirm, or to ask for a system-colour mapping.
**Not verified:** Behaviour with a real Windows high-contrast setting. Check it on a Windows machine: turn on a Windows contrast theme, start `RafficaGallery` without `--high-contrast`, and toggle the contrast theme while the app runs. `--high-contrast` forces the palette (`ShellTheme.Apply` with `forceHighContrast: true`) and skips the OS detection and the `ColorValuesChanged` subscription, so it only shows the palette; use it for that on any OS.

## 2026-11-03 – SelectBox instead of plain ComboBox for the select
**Context:** The framework `ComboBox` changes the selection on Down and Up instead of opening the list.
**Decision:** `SelectBox : ComboBox` (same theme through `StyleKeyOverride`) opens on Enter and Down. The gallery uses `SelectBox`.
**Rationale:** Keyboard behaviour matches the menus and what users expect from a select.

## 2026-11-03 – Menu and board calls
**Context:** Details the spec and board left open or that differ between them.
**Decision:** Field height 26 (the board's 28 is the title-bar search field). Menu labels 14 px, shortcuts 13 px, tooltip shortcuts 12 px. The shortcut of a destructive menu item is Danger (board), a pressed destructive item has a Danger fill. Menu minimum width 240. The keyboard ring on menu items is `:focus:not(:pointerover)`, because menus never raise `:focus-visible`.

## 2026-11-03 – Localization mechanism and its limits
**Context:** English and German must work under Native AOT without reflection-based lookups.
**Decision:** Two `ResourceManager`s, one per language with the invariant culture; both `.resx` files are embedded in the main assembly through `LogicalName`. A missing key throws; there is no fallback across languages. Proven under Native AOT on Windows and Linux in CI. The gallery sets `InvariantGlobalization=false` (as the M1 probe does) because German formats are needed; on Linux that requires libicu on the machine.
**Limits (milestone 1.2):** Key names in menu shortcuts come from Avalonia's converter (`Ctrl+X` in German, not `Strg+X`), and the TextBox context menu keeps Fluent's English Cut, Copy and Paste.

## 2026-11-03 – Speed targets of the frame (M1)
**Context:** Spec section 9 asks milestone 1 to measure a Native AOT Avalonia 12 app on the owner's machines and to set speed targets from it. The probe (`bench/shell-probe`, commit of this entry) has an `empty` mode (one window) and a `frame` mode (static mock of the Write frame of direction D: about 60 controls, three embedded font families, German resources). `measure.yml` ran it on three hand-started self-hosted runners; the raw CSVs, the machine notes and the budget table are in `Docs/measurements/shell-probe-2026-11-03/` (machine names replaced by `windows-main-pc`, `linux-probook`, `macos-macbook`). Cases: cold (no warm-up, 5 s pause), warm (3 warm-ups, 500 ms pause), 10 runs each; input: 100 synthetic key presses in one run; memory: 2 s after first frame; size: the published folder without debug symbols. All figures are medians. Runs (all `dev` at 0480960): Linux 41682565866, Windows 41683058597, macOS 41687457617, cold after reboot Windows 41689113140 and Linux 41689118537.

**Machines:**
- Windows (binding): Ryzen 7 7800X3D, 30.9 GiB, NVIDIA RTX 4070 SUPER, 1920 × 1080 at 60 Hz, scaling 100 %, Windows 11 build 26300, balanced power plan, mains power.
- Linux (binding): HP ProBook, Core i5-8265U, 15.4 GiB, Intel UHD Graphics 620, 1920 × 1080, Wayland session with XWayland, CachyOS (kernel 7.3.0-4-cachyos), balanced profile, mains power.
- macOS (indicative only): MacBook Pro 2016, Core i5-6360U, 8 GiB, Intel Iris Graphics 540, 2560 × 1600 Retina, macOS 15.8.2 via OpenCore Legacy Patcher, mains power. Unsupported hardware; Apple-silicon numbers come later in the macOS subproject.

**Measured (empty / frame):**

| Case | Windows | Linux | macOS (indicative) |
|---|---|---|---|
| Cold start (ms) | 303 / 426 | 134 / 198 | 506 / 623 |
| Warm start (ms) | 310 / 416 | 131 / 200 | 493 / 685 |
| Cold start after a reboot, frame (ms, one run) | 558 | 159 | not measured |
| Input latency, frame, median (ms) | 4.3 | 16.6 | 16.7 |
| Input latency, frame, p99 / max (ms) | 9.7 / 13.4 | 33.5 / 47.3 | 128 / 213 |
| Idle memory (MiB) | 87 / 124 | 100 / 125 | 57 / 83 |
| Published size, frame (MiB) / zip | 38.1 / 15.7 | 34.9 / 15.3 | 36.8 / 16.5 |

**Cost of the frame (frame minus empty):** cold 124 ms (Windows), 65 ms (Linux), 117 ms (macOS); warm 105, 70 and 192 ms; memory 36, 25 and 26 MiB.

**Size shares (frame):** fonts 2.81 MiB (7 % Windows, 8 % Linux, 8 % macOS); native libraries 18.0 MiB (47 %), 13.3 MiB (38 %) and 18.8 MiB (51 %). The probe's own code and the framework make up the rest.

**Decision:** The budget of each binding machine and case is its frame median rounded up (time to 10 ms, memory to 5 MiB, size to 1 MiB); the limit is the budget × 1.25 (spec 9.4).

| Case | Windows budget / limit | Linux budget / limit |
|---|---|---|
| Cold start | 430 / 537.5 ms | 200 / 250 ms |
| Warm start | 420 / 525 ms | 210 / 262.5 ms |
| Input latency (median) | 10 / 12.5 ms | 20 / 25 ms |
| Idle memory | 125 / 156.25 MiB | 125 / 156.25 MiB |
| Published size | 39 / 48.75 MiB | 35 / 43.75 MiB |
| Zip | 16 / 20 MiB | 16 / 20 MiB |

The macOS values (cold 630, warm 690 ms, memory 85 MiB, size 37 MiB as rounded medians) are indicative and set no limit. Milestone 1.2 measures the gallery app's published frame the same way and must stay within these limits; if it cannot, that is a finding for the owner, not a new budget (spec 9.4). Budgets are never loosened to make a check pass.

**Rationale:** The numbers come from the real machines with a real GPU and an active display; no compositor warning was raised (the check exists on Windows only, see below).

**Caveats (read before comparing):**
- The input latency is measured inside the app, from the key events to the second animation frame. On Windows it is far below one 60 Hz frame (4.3 ms): the frame callbacks there are not paced by the display, so the value is the time to the app's frame, not to the pixel. On Linux and macOS the value is one to two display frames. Compare it across systems with care; against its own baseline it is valid.
- The first scripted Windows run had a 12 s outlier at the first launch of the empty window; the median is unaffected, the repeat run had none.
- The cold start is not a cold disk: the probe is published minutes before the run. Only the single run after a reboot comes closer, and on Windows it is about 130 ms slower than the scripted cold case.
- The frame-rate check of the script runs on Windows only; on Linux and macOS the rule "logged-in desktop session, display on" applies. The input latency of the ProBook (one display frame at 60 Hz) shows an active display.
- Self-hosted runners and the Actions retention limit of the repository (7 days): the CSVs are kept in `Docs/measurements/` for that reason.

**Not measured:** a start after a reboot on macOS (command: `gh workflow run measure.yml --ref dev -f platform=macos -f note="<note>" -f cases=cold-reboot`); the macOS Apple-silicon figures (later); a compositor-rate check on Linux and macOS; scrolling, typing in a large document and opening a large file (these belong to the app specs, spec 9.4).
**Rejected alternatives:** Budgets from the median of all machines (the machines differ by a factor of two, and the limit must hold on the slower binding one); taking the probe's budgets from its `empty` mode (it is only the baseline).

## 2026-11-03 – CI on self-hosted runners, as few runs as possible
**Context:** The CI ran on GitHub-hosted runners (Windows minutes count double) on every pull request and again on every push to `dev`. The owner has self-hosted org runners (group `raffica-ci`: one Windows, one Linux; the `Default` group has more).
**Decision:**
- `ci.yml` and `roadmap.yml` run on the self-hosted runners (`[self-hosted, dotnet-linux]`, `[self-hosted, dotnet-windows]`), except for a pull request from a fork, which goes to GitHub-hosted runners so that no foreign code reaches the owner's machines.
- A run happens for pull requests that are not drafts, and for pushes to `main`. There is no run for the squash merge on `dev`: the pull request run checked the same tree.
- A pull request into `dev` that changes only `Docs/`, Markdown in the root or in `bench/`, or `LICENSE` skips the build; the roadmap check runs only when a roadmap file changes. Pull requests into `main` and pushes to `main` always run everything. The required checks `ci` and `roadmap` always report (success when there was nothing to do).
- The runner group `raffica-ci` has no workflow restriction (a pull request run has the ref `refs/pull/N/merge`, which the setting cannot name) but is limited to this repository.
**Rationale:** No hosted minutes, and no check is lost: every change that can break the build or tests still runs the full matrix.
**Rejected alternatives:** Keeping pull requests on hosted runners as in Pagewing (costs minutes, and the repository is private for now); a `paths` filter on the workflow (the required checks would stay pending).

## 2026-11-03 – Design direction: D "Formular Neutral"
**Context:** Milestone 1 asks the owner to choose one design direction for Write, Sheets, Slides and PDF. Several directions were built with Impeccable as Claude Design canvases, each showing all four apps in light and dark with a measured contrast table: A "Formular" (https://claude.ai/artifact/VxVWMN5vGJcDvY8UfB14Nm), B "Shelf" (https://claude.ai/artifact/8dGi9yQJYdESDKXnwFkikH), B "Wayfinding" (https://claude.ai/artifact/BfUsTE8SwkahWxGXHzNLnr), C "Graphite" (https://claude.ai/artifact/Lx5wjMHpbfcsdYThkcX4ua) and D "Formular Neutral" (https://claude.ai/artifact/RGyDq2FZQYBttjkJp6MQbQ).
**Decision:** Direction D "Formular Neutral", in the refined version on its canvas, is the design direction for all four apps:
- Ribbon with tabs and named command groups (82 px, compact one-row mode 40 px). Frequent commands stay visible and one click away, and search (Ctrl+K) is only an accelerator.
- Neutral chrome. The app ink appears only for active and focused states: active tab, pressed buttons, active navigation item (neutral tint with a 3 px ink edge), focus ring, app mark and field codes.
- App inks: Write blue, Sheets green, Slides orange, and PDF plum instead of red. Red is reserved for destructive, irreversible actions (red outline, set apart, red focus ring); warnings and errors never use it.
- One review violet for insertions and comments in all apps.
- Public Sans for the UI (14 px base, sentence-case labels); Barlow Semi Condensed only for field codes; Carlito for document content; tabular figures.
- Radius 0 for surfaces (bars, panels, page, grid) and 2 to 3 px for controls and overlays; 1 px rules.
- Keyboard: Alt shows field codes, F6 moves between panes with a 1 px ink border.
- German UI ships from the first release.

Exact tokens and measured contrast values go into the design system spec.
**Rationale:** The owner wanted frequent commands in sight and one click away, a screen that is easy to take in, and nothing dated, cluttered, playful or trendy. D keeps A's familiar ribbon structure but moves colour out of the chrome, so the four apps read as one calm suite. Two refinement rounds reduced the ribbon height, simplified the status bar and softened the technical look: sentence case instead of uppercase labels, slightly rounded controls, more air, and a plain search field.
**Rejected alternatives:**
- A "Formular": colour-tinted chrome and desk; the base D was refined from.
- B "Shelf": hid frequent commands behind a slim toolbar and panels, and was too slow to click.
- B "Wayfinding": the saturated full-width sign band risked feeling loud over a workday.
- C "Graphite": a darker, more technical world with a command palette and inspector; even after its revision the owner preferred D's ribbon.
- Red as the PDF accent: it would collide with the destructive red, so PDF gets plum.

## 2026-11-03 – PDF markers: no compatibility with files written by Pagewing
**Context:** The scripted rename also changed three markers that the code writes into saved PDFs and reads back to recognise its own objects: `RafficaSignature` (image signatures), `RafficaOcr` (OCR text layer) and `%RafficaTextBox` (text boxes). Pagewing wrote `PagewingSignature`, `PagewingOcr` and `%PagewingTextBox`.
**Decision:** Raffica writes and reads only its own markers. A PDF edited with Pagewing is treated as foreign: its text boxes and signatures cannot be edited, and redoing OCR adds a second text layer. No code to read the old markers.
**Rationale:** Matches the decision of 2026-11-02 (no user migration): only the owner used Pagewing. Changing the markers after Raffica has written documents would be a migration, so it is settled now.
**Rejected alternatives:** Also reading the Pagewing markers (extra code and tests for files nobody else has); keeping the Pagewing names (the files would carry the old name for good).

## 2026-11-03 – Updater: key and addresses until the first release
**Context:** The imported updater trusted Pagewing's release key and released from the Pagewing repository.
**Decision:** The trusted key is the design-time placeholder whose private half was discarded, so no manifest verifies and no update is accepted. The three addresses point at `benjo-code/raffica` releases, which do not exist yet. The real key and the choice of a releases repository are made on the owner's instruction at the first release milestone.
**Rationale:** Nothing can be installed through the updater before a real key exists, and no address leads to Pagewing's releases. The addresses are not documented placeholders (an `.invalid` host) because the repository that will hold the releases is likely this one.
**Rejected alternatives:** `.invalid` placeholder addresses (the update tests expect GitHub hosts); keeping Pagewing's key (would trust a key whose owner is another product).

## 2026-11-03 – CI: one required check, GitHub-hosted runners
**Context:** M0 adds the first CI for build, test and format on Windows and Linux. Matrix jobs get renamed when platforms are added (macOS comes later), and a renamed required check silently stops protecting a branch.
**Decision:** The workflow `ci.yml` builds, tests (with `RAFFICA_REQUIRE_OCR=1`), format-checks and publishes the CLI with Native AOT on a GitHub-hosted Windows and Linux runner, and smoke-tests the published binary. The aggregate job `ci` (`needs: [build]`, `if: always()`, fails unless the matrix succeeded) is the required status check together with `roadmap`. Native AOT cannot cross-compile between operating systems, so each OS builds on its own runner.
**Rationale:** The required check keeps its name when the matrix changes. Hosted runners need no assumptions about the owner's self-hosted runners.
**Rejected alternatives:** Requiring each matrix job by name; self-hosted runners for Windows (not known to exist for this repo).

## 2026-11-03 – OCR library: fetched from Pagewing's pinned release
**Context:** The OCR tests need the Tesseract library. Pagewing builds it in its own workflow and publishes it as the prerelease `native-tesseract-5.5.4-1`; Raffica has no build for it yet.
**Decision:** `tools/native-fetch.cs` downloads the pinned files (size and SHA-256 are checked, nothing is laid down if one fails) from that release and from `tessdata_fast` 4.1.0. Raffica builds the library itself later, when the suite ships OCR.
**Rationale:** Reuses a built and verified binary; the pinned hash is trusted, not the place it comes from.
**Rejected alternatives:** Skipping the OCR tests (weakens the checks); importing the Tesseract build workflow in M0 (out of scope).

## 2026-11-03 – Dependency and license guardrails
**Context:** Raffica is GPL-3.0-or-later. Research on document libraries showed several popular .NET libraries with licenses that do not fit.
**Decision:** Not allowed: EPPlus 5 and later (Polyform Noncommercial), DocX/Xceed (non-commercial), NPOI binaries from 2.8.0 on (EULA with a maintenance fee; building from source needs a separate legal check), Avalonia Pro controls such as the RichTextEditor, and proprietary document libraries (Aspose, Syncfusion, Spire, GemBox, FlexCel). MuPDF (AGPL) is avoided. Preferred licenses: MIT, Apache-2.0, BSD. Every new dependency gets a license check before it is added.
**Rationale:** Keeps the suite redistributable under the GPL without per-user fees or revenue limits.
**Rejected alternatives:** Commercial libraries with free tiers (revenue caps and page limits conflict with an open-source suite).

## 2026-11-03 – Core-only import, fresh UI, new order of work
**Context:** The decision of 2026-11-02 planned Raffica PDF as a pure move of Pagewing. The owner wants a completely fresh UI. A read-only look at the Pagewing `v1.0.0` tag shows that `Pagewing.Core`, `Pagewing.Pdfium`, `Pagewing.Ocr` and `Pagewing.Cli` do not depend on Avalonia.
**Decision:** M0 imports only the core, the CLI, their tests and test data, the bundled PDFium and OCR binaries and the build config. `Pagewing.App`, the Avalonia-related third-party bundles and the installer packaging are not imported. Order: Foundation (M0) → design system and shell → Raffica PDF on the new UI → macOS → Write → Sheets and Slides. The design system starts with at least three design directions made with Impeccable and shown as Claude artifacts for all four apps; the owner chooses one.
**Rationale:** Reuses the proven PDF engine and tests without carrying over a UI that would be replaced. The design is decided on all apps together, so PDF does not set the look alone.
**Rejected alternatives:** Importing the whole app and restyling it (keeps a UI the owner does not want); designing the PDF app first and extending the look to the other apps later.
Supersedes the order of work in "Suite structure and order of work".

## 2026-11-03 – Stack: .NET and Avalonia for the shell, Write's layout engine decided later
**Context:** Research compared .NET 10 with Native AOT and Avalonia 12 against Rust (Slint, GPUI, egui, iced, Xilem, Parley, IronCalc) and others. Mostly vendor claims and project READMEs, few independent benchmarks.
**Decision:** The suite shell uses .NET 10 (LTS), Native AOT and Avalonia 12. The document model and layout of Write live in a library with no UI dependency; whether that engine is C# or a Rust core behind a C interface is decided in the Write spec, after spikes (Open XML SDK under Native AOT, text layout speed, printing, IME). For Sheets, IronCalc is compared with an own formula engine in the Sheets spec.
**Rationale:** The core already runs on .NET. No Rust GUI toolkit is ready for a ribbon, grids and accessibility. A Word-grade layout engine is the largest cost in any language, so the choice for it should rest on measurements.
**Rejected alternatives:** Rust for the whole suite (immature GUI toolkits, rich-text accessibility missing in AccessKit); C++ and Qt (heavy for a solo developer, no reuse of the core); Tauri or web UI (undermines the speed promise).

## 2026-11-02 – Migration from Pagewing: new repo, no history, only what is needed
**Context:** Pagewing (PDF editor) becomes the first app of an office suite. Nobody except the owner uses Pagewing, and its public history contains commits with a private email address.
**Decision:** A new repo `benjo-code/raffica` (created from `project-template`) without Pagewing's git history. Import only what is strictly needed (core/engine code, tests, build config, CI, LICENSE, a rewritten README) from the `v1.0.0` tag; commit 1 = unchanged import, commit 2 = scripted rename. Old plans and docs stay in the old repo, and `Docs/DECISIONS.md` here starts fresh with the decisions that still apply. No bridge release and no user migration. Pagewing is finished first (v1.0.0), then migrated.
**Rationale:** A clean start with a small surface; no translation of Pagewing's large German decision log; no need to support existing installations.
**Rejected alternatives:** Renaming Pagewing in place (keeps the history and the old identity); carrying over all docs (they are German and mostly obsolete).

## 2026-11-02 – English everywhere
**Context:** `project-template` and Pagewing use German for docs and commits.
**Decision:** Docs, README, commits, issues, PRs, code and comments are English. The UI source language is English, and German localization stays on the plan (to be confirmed in the M0 spec). The template block in `CLAUDE.md`, the roadmap tool output and the repo templates are translated to English in this repo.
**Rationale:** An international open-source project with a single language for contributors.
**Rejected alternatives:** Keeping the template German; mixing languages.

## 2026-11-02 – Suite structure and order of work
**Context:** Raffica Office is meant to contain Write, Sheets, Slides and PDF.
**Decision:** Brand Raffica, suite Raffica Office, apps Raffica Write, Sheets, Slides and PDF. One repo (monorepo) with one installer for the suite and individually started apps; platforms Windows, Linux and macOS. Order: first Raffica PDF as a pure move of Pagewing, then the shared design system and shell project, then Write (its text layout engine also serves Slides), later Sheets and Slides. macOS is its own milestone after the design system. Details are worked out in the M0 spec.
**Rationale:** Do not start all apps in parallel; extract shared infrastructure before a second app exists.
**Rejected alternatives:** Developing all apps in parallel.
Order of work superseded by: Core-only import, fresh UI, new order of work (2026-11-03).

## 2026-11-02 – Design system and Impeccable
**Context:** The UI is designed from scratch and should look consistent across the apps.
**Decision:** One shared design system (tokens, typography, spacing, components), one accent color per app, designed with the Impeccable skill. Impeccable targets web frontends, so it provides direction and critique and the tokens are implemented by hand (Avalonia/AXAML).
**Rationale:** Consistent look, different colors per app.
**Rejected alternatives:** Designing each app on its own.

## 2026-11-02 – macOS without paid Apple account
**Context:** No Apple Developer account is paid for.
**Decision:** Ad-hoc signing only, no Developer ID and no notarization; distribution as `.dmg`. First launch needs "Open Anyway" in System Settings → Privacy & Security, which the README documents.
**Rationale:** Ad-hoc signing is free and required to run native binaries on Apple Silicon.
**Rejected alternatives:** Unsigned builds (do not start on Apple Silicon).
