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
