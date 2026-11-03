# Raffica Office – Design System and Shell

**Status:** draft for owner approval, 2026-11-03 · **Subproject:** 2 – Design system and shell · **Milestone:** 1 (Design directions & spec)

This spec is the source for subproject 2. It fixes the design system (tokens, typography, components), the app frame that all four apps share, the localization infrastructure, and how the speed targets are measured and set. It also enters the milestones that build them.

Calls marked *(author's call)* were made while writing this spec, not by the owner; the owner can overturn them on review.

## 1. Intent

Raffica Office should feel like one calm, professional desktop office suite: functional, quiet, precise and consistent, built for long working sessions. The owner wants frequent commands in sight and one click away, a screen that is easy to take in, and nothing that feels dated, cluttered, playful, trendy or technical. Speed is the core promise and is felt, not advertised.

The look was chosen from canvases built with Impeccable (`Docs/DECISIONS.md`, "Design direction: D "Formular Neutral""). The approved canvas is the visual reference: https://claude.ai/artifact/RGyDq2FZQYBttjkJp6MQbQ (version 9, 17 boards: four apps light, Sheets dark, the system and components sheets, and ten states: empty documents, PDF without marks, settings dialog, context menu, Alt codes, F6 focus in dark, hover and selection, start view).

**Sources in the repo:** the board sources of version 9 are in `Docs/design/direction-d/` (`*.dc.html`, `canvas.json`; open a board in a browser to see it). The contrast script that produced appendix A is `Docs/design/direction-d/contrast.py`. **Precedence:** this spec wins over the boards. For visual details the spec does not state, such as icon shapes, group contents shown as examples, proportions of the start view and dialogs, the boards are the reference.

## 2. Goals

1. One shared design system for Write, Sheets, Slides and PDF: tokens for colour, type, spacing, radius and sizes, light and dark, implemented by hand in AXAML in one shared shell project.
2. One app frame (title bar, document tabs, search, ribbon, side panels, status bar, start view, settings) that every app uses unchanged and fills with its own commands and document view.
3. English and German UI from the first release, with a test that no German string is missing.
4. Every text and UI-part colour pairing meets WCAG 2.2 AA (text 4.5:1, UI parts 3:1) in light and dark, proven by a test, not by a table alone.
5. Speed targets for the frame (cold start, warm start, input latency, idle memory, size on disk) set from measurements on the owner's three machines and guarded in CI.
6. Everything reachable from the keyboard: Alt field codes, F6 pane focus, visible focus everywhere.

## 3. Non-goals and anti-goals

**Non-goals (not in this subproject):**
- Any app's document view or commands beyond what the frame needs to show (the PDF view is built in subproject 3, Write, Sheets and Slides later).
- Live language switching. A language change in settings applies after a restart (owner's decision).
- A custom file browser. Browsing uses the operating system's dialog; Raffica only draws the recent-files list on the start view.
- Templates beyond blank documents, accounts, cloud locations, telemetry, tips or onboarding tours.
- Speed targets for opening, scrolling, typing, recalculating, saving and exporting documents. This spec only lists them (section 9.4); each app spec sets them.

**Anti-goals (a failure even if it works):**
- A frame that feels technical: uppercase condensed labels, keycap badges, codes, monospace or numbering in the chrome at rest.
- Colour in the chrome outside the defined active and focus states; tinted panels; a second accent.
- Red used for anything but destructive, irreversible actions.
- A frequent command reachable only through search, a "More" menu or a side panel.
- Gradients, glass, shadows for decoration, rounded cards, mascots, emoji.
- A UI that differs between the four apps where it does not have to.

## 4. Design language (decided)

The tokens with their values and measured contrast are in appendix A. The rules:

### 4.1 Colour
- **Neutral chrome.** Chrome text is neutral (`text.primary` #1A1D21, `text.secondary` #4A5059 in light). Desk, band, panels and page are neutral greys and white. No colour-tinted surfaces.
- **App ink only for active and focus states:** the active ribbon tab and the rule under the tab row, pressed and checked buttons, the active navigation item's 3 px edge, the focus ring, the app mark, field codes while Alt is held, the active document tab's 3 px top edge, the F6 pane border.
- **App inks:** Write blue #1D4F91, Sheets green #1E6B47, Slides orange #A3420F, PDF plum #5E1A3E, with lightened dark-theme variants of similar lightness (appendix A).
- **Review violet** #7B2FBE (dark #B48CF0) for insertions, comment anchors and comment borders, the same in every app.
- **Destructive red** (#B21F2D light) only for destructive, irreversible actions: a red outline button, set apart from its group by a rule or gap, with a red focus ring. A destructive menu item is red, last, after a separator.
- **Warnings and errors never use red.** A warning has an amber triangle icon (#8A6A00 light) in a neutral bar; an error has an octagon icon and a bold opening phrase in a neutral bar.
- **Selection** in lists and navigation: a neutral tint plus a 3 px ink edge, text stays `text.primary` semibold.
- The dark theme is its own neutral palette, not an inversion, with lightened inks used in the same places.

### 4.2 Type
- **Public Sans** for the UI. The base size is 14 px. Status bar, counts and menu shortcuts are 13 px. Group labels and panel titles are 12 px, sentence case, in `text.secondary`.
- **Barlow Semi Condensed** (semibold) only for field-code badges.
- **Carlito** (metric-compatible with Calibri) is the default document face. It belongs to the documents, not the chrome.
- Tabular figures wherever numbers align (status bar, counts, sheet headers, tables).
- No uppercase anywhere in the chrome. No monospace in the chrome.

### 4.3 Geometry
- The spacing scale is 2, 4, 6, 8, 10, 12, 16 and 24 px. Rules are 1 px.
- **Radius:**
  - 0 for surfaces: title bar, tab row, ribbon band, panels, status bar, document page, slide, grid and cells, document tabs, navigation selection.
  - 2 px for checkboxes.
  - 3 px for controls and overlays: buttons, ribbon tiles, inputs, selects, dropdowns, segmented controls, menus, tooltips, dialogs, notification bars, field-code badges and start tiles.
- **No decorative shadows.** A modal dialog sits over a dim scrim with a 1 px edge. Menus and tooltips have a 1 px edge.
- **Hit targets:**
  - At least 24 px everywhere. Small ribbon buttons are 26 px, large ribbon tiles 54 px.
  - List and menu rows are 32 px, file rows 40 px.
- **Icons:**
  - Stroke icons on one grid: 16 px for small commands, 20 px on large tiles.
  - 1.5 px stroke, round caps and joins.
  - Icon-only buttons only for universal glyphs (bold, italic, underline, undo, redo, alignment), each with an accessible name.

### 4.4 Behaviour
- **Field codes:**
  - While field-code mode is on, every ribbon tab shows a letter and every command a code: a 12 px badge in ink with white text, always at the bottom-right corner of its control. The input sequence is in section 4.5.
  - Group numbers also appear only in field-code mode.
  - The codes are language-independent: the same keys in English and German.
  - *(author's call)* Each command declares its code in its definition; codes are never derived from position, so they stay stable when a group gains a command. A test fails on duplicate codes within a tab.
- **F6:**
  - F6 and Shift+F6 move keyboard focus between panes: navigation, document, and comments or format.
  - The focused pane gets a 1 px ink border and its title turns `text.primary`.
  - The status bar shows "Focus: <pane>" ("Fokus: …") only while pane navigation is active. Pointer use or Esc ends it.
- **Compact mode:**
  - A setting ("Kompakte Befehlsleiste") collapses the ribbon to one 40 px row.
  - Group labels are hidden and become tooltips and accessible names, large tiles become small buttons, and labelled secondary buttons become icon-only.
  - Each group ends in a "▾" button that opens the rest of the group. No frequent command disappears.
- **Search:**
  - A search field in the title bar with a magnifier and the placeholder "Search" / "Suchen", without a keycap.
  - Ctrl+K (Cmd+K on macOS) focuses it, and the tooltip says "Search (Ctrl+K)".
  - It finds commands; it is an accelerator, never the only path.
- **Status bar:**
  - Fixed cells, from the left: save state, position, then the word count or the selection summary.
  - Zoom is on the right.
  - The focus cell appears only during F6 navigation. The language appears only when the selection's language differs from the document's.
  - Cells change in place and never reflow.

### 4.5 Field-code input sequence

The owner chose the Office-style sequence (no key is held):

1. Pressing and releasing Alt alone (no other key in between) turns field-code mode on. The tab letters appear on the tab row. F10 does the same.
2. Typing a tab letter switches to that tab and shows the command codes of its groups.
3. Typing a command code runs the command. Two-character codes complete on the second key. A command that opens a dropdown or menu shows the codes of its items next.
4. Esc goes back one step (command codes → tab letters → mode off). Alt again, a click, or moving the focus away ends the mode.
5. Holding Alt together with another key is an ordinary shortcut and never enters the mode. AltGr (Ctrl+Alt on Windows) never enters it, so German keyboards type @, € and the brackets as usual.
6. On Windows, the bare Alt press must not reach the system menu.

While the mode is on, the status bar shows a short hint ("Type a field code · Esc goes back"). Tests cover each step, the AltGr case and duplicate codes.

## 5. App frame (decided)

Shared by all four apps, top to bottom:

1. **Title bar, 36 px:**
   - Content from the left: the app mark, then the document tabs (each with a close button, plus "+" for a new document), then the search field, then the window buttons.
   - The active document tab has a 3 px ink top edge.
   - The window buttons are drawn by Raffica on Windows and Linux. macOS keeps the system buttons on the left (author's call, see section 8).
2. **Ribbon tab row, 32 px:**
   - Tabs per app ("File" opens the file view).
   - The active tab is shown in ink, with a 1 px ink rule under the row.
3. **Ribbon band, 82 px (compact: 40 px):**
   - Named command groups, grouped by task.
   - The large primary command comes first, then rows of small commands of equal height.
   - Space separates the groups. A rule appears only in front of a destructive group.
   - Group labels sit under each group.
4. **Work area:**
   - The navigation panel is on the left. What it holds depends on the app: outline, thumbnails, pages and marks. Sheets uses sheet tabs instead.
   - The document is centred on the desk.
   - Comments or format are on the right when open.
   - Panel headers are 38 px with a 12 px title, and panel padding is 12 px.
5. **Notification bar** (when needed):
   - Inset under the ribbon, neutral, 3 px radius, with an icon, one sentence and at most two actions. Example: after crash recovery, "Compare" and "Close".
   - No toasts, no stacks, no upsell.
6. **Status bar, 30 px**, as in section 4.4.

**Start view:**
- Every app shows it when no file is open, under that app's title bar, without a status bar.
- **"New":** blank tiles with a small preview and plain labels ("Blank document", "Blank spreadsheet", "Blank presentation", "Open PDF"). The app mark sits in the tile corner.
- **"Open":** a "Browse…" button that opens the system file dialog.
- **"Recent":** name, folder, and the date in the UI locale.
- *(author's call)* A tile for another app starts that app's executable with a "new blank document" argument; a tile for the own app opens the blank document in place. Handing a document to an already running instance (single instance, Pagewing's pipe) is settled in each app's spec. In the gallery app of milestone 1.2 the tiles only record the request.

**Settings:**
- A modal dialog with these settings only:
  - theme (Light, Dark, Same as system);
  - language (Deutsch, English), which applies after a restart, and the dialog says so;
  - compact command bar;
  - autosave (on by default).
- Buttons: "Cancel" and the primary "OK". The primary button is a dark neutral fill, not ink.

**Shared components** (the Komponenten board):
- title bar;
- context menu (shortcut column, submenu arrow, separators, disabled items, destructive item last);
- tooltip (the name once, then the shortcut and a one-line description);
- dialog;
- notification bar (info, warning, error);
- the recent-files list;
- the state matrix (rest, hover, pressed, selected, focus, disabled) for button, toggle, ribbon tile, navigation item, tab, document tab, menu item, cell and destructive button, in light and dark.

## 6. Implementation constraints

- **Stack:** .NET 10 Native AOT and Avalonia 12, as decided. No reflection-based XAML loading at runtime; compiled bindings only. Styles are AXAML in one shared project. Its name is left to the plan; `Raffica.Shell` is suggested.
- **Tokens:**
  - The tokens live in one resource dictionary per theme.
  - Components use semantic tokens only. No hex values inside component styles.
  - Pagewing's `Tokens.axaml`, `Density.axaml` and `HighContrast.axaml` (tag `v1.0.0`) are reference for the mechanism, not for values.
- **Contrast test:** a unit test reads the token dictionaries and checks every pairing listed in appendix A against its threshold, in both themes. A new pairing without a threshold fails the test.
- **High contrast** *(author's call)*: when Windows high contrast is on, the frame switches to a token set built from the system colours, as Pagewing did. On Linux and macOS the normal themes apply.
- **Fonts:**
  - Public Sans (regular, semibold), Barlow Semi Condensed (semibold) and Carlito (regular, bold, italic, bold italic) are embedded. All three are under the SIL Open Font License 1.1 and listed in the third-party licences.
  - The size each one adds is reported in the size measurement.
  - Slide theme fonts (the boards use Libre Franklin for the sample deck) are not part of the shell; the Slides spec decides them.
- **Icons** *(author's call)*: one icon set drawn on the 16/20 px grid with 1.5 px strokes, stored as path data in AXAML. Where an existing open icon set fits, it may be the source (for example Lucide, ISC licence), with its licence recorded. No icon fonts.
- **Accessibility:**
  - Every control has an automation name.
  - Icon-only buttons take their name from the command.
  - Focus is always visible, and the focus ring is the ink at 3:1 or more.
  - Field codes and F6 are covered by tests.

## 7. Localization (decided)

- English is the source language and German ships from the first release.
- **Mechanism:** .NET resource files, as in Pagewing (`Strings.cs`, `Strings.en.resx` and `Strings.de.resx`, tag `v1.0.0`), which already shipped under Native AOT there. The first implementation milestone confirms this in the published shell. *(author's call)* If it fails, the fallback is a build-time generator that turns the same `.resx` files into C# string tables, so translators and the missing-translation test keep working on `.resx`.
- **Layout:**
  - Strings used by the frame and by more than one app live in the shell project.
  - Each app keeps its own strings.
- **Language choice:**
  - The first start follows the operating system's UI language (German if it is German, English otherwise). After that, the setting decides.
  - A change applies after a restart.
  - Pagewing's `--lang <code>` switch is kept for tests and measurements.
- **Formats:** numbers, dates and currency follow the UI language (de-DE: decimal comma, period as thousands separator, "03.11.2026").
- **Tests:**
  - A test fails when any English key has no German value, or when a German value is empty.
  - A layout test renders the frame in German at 1440 px and at the compact width and fails on clipped ribbon labels. German runs about 30 % longer than English.
- Field codes are the same in both languages.

## 8. Platform notes

- **Title bar** *(author's call)*:
  - Windows and Linux: Raffica draws the whole title bar, including the window buttons, using Avalonia's extended client area.
  - macOS: the system "traffic light" buttons stay on the left, and Raffica's content starts after them.
  - **Risk:** some Linux setups do not allow client-side title bars, especially Wayland compositors without the decoration protocol. The fallback is the system title bar with the document tabs moved into the tab row. The first implementation milestone checks this on the ProBook (CachyOS).
- **Shortcuts:** Ctrl maps to Cmd on macOS. F6 and the Alt codes use Option on macOS. Whether macOS keeps Alt codes at all is left to the macOS spec (subproject 4).

## 9. Speed: measurement and targets

### 9.1 What milestone 1 measures
- **Subject:** the frame, not documents, because no app exists yet.
- **Probe** *(author's call)*: a throwaway Native AOT Avalonia 12 app under `bench/shell-probe/`. It is not product code and no app depends on it. It has two modes:
  - `empty`: one window with nothing in it, the baseline;
  - `frame`: a static mock of the D frame with the title bar, ribbon (about 60 controls with icons), both panels and the status bar. It embeds the three fonts and loads the German resources. Nothing in it works.
- **Measurements:**
  1. **Cold start:** from immediately before the process starts to the first presented frame after the window is drawn ("first-frame", the same method as Pagewing's `first-sharp` in `bench/startup`, tag `v1.0.0`). One case after a reboot is done by hand; the scripted case starts without a warm-up run and with a 5 s pause.
  2. **Warm start:** the same, after three warm-up runs.
  3. **Input latency:** from a synthetic key press to the presented frame that shows it, measured inside the app, 100 presses.
  4. **Idle memory:** the private working set (Windows) or RSS (Linux, macOS), 2 s after the first frame.
  5. **Size:** the published folder in bytes, the compressed archive, and the share of fonts and native libraries.
- **Statistics:** 10 runs per case, reporting the median, minimum and maximum, with output as CSV.
- **Validity:** valid numbers need a GPU and an active display. Pagewing found that machines without a GPU show the compositor's frame rate, not the app's. The script warns as Pagewing's does.

### 9.2 Machines
| Machine | OS | Role |
|---|---|---|
| Owner's main PC | Windows 11 | binding for Windows |
| HP ProBook | CachyOS (Linux, x64) | binding for Linux |
| Mac (2016, Intel) | macOS 15 via OpenCore Legacy Patcher | indicative only: unsupported hardware, Intel. Apple-silicon numbers are taken later, in the macOS subproject. |

Each machine records CPU, RAM, GPU, display resolution and scaling, OS version and power mode next to the numbers.

### 9.3 Runners and workflow
- **Runners:**
  - All three machines run a self-hosted GitHub Actions runner. The owner starts it by hand (`run.sh` or `run.cmd`) when a measurement should run, and stops it afterwards. No runner runs as a service.
  - Each runner has its own labels (`raffica-measure` plus `windows`, `linux` or `macos`). On the main PC this is the same kind of hand-started runner as Pagewing's `dotnet-windows-gpu` (`hardware-measure.yml`, tag `v1.0.0`), registered for this repository; the CI jobs can never land on these labels.
  - The owner registers each runner with a one-time token from the repository settings. A guide in `bench/shell-probe/RUNNERS.md` describes the steps. No token is written into the repository.
- **Workflow `measure.yml`:**
  - It is triggered only by `workflow_dispatch`, with inputs for the platform and the cases. It has no `push`, `pull_request` or schedule trigger, so code from a fork can never reach these machines.
  - It publishes the probe for the runner's runtime identifier, runs the cases and uploads the CSVs and machine notes as artefacts.
  - Claude starts it with `gh workflow run`, and the run waits until the owner's runner is online.
- **Security:** the repository is public, so the setting "Require approval for all outside collaborators" for fork pull requests is recommended. Changing it is a GitHub settings change and happens only on the owner's explicit instruction.

### 9.4 Setting the targets
- **Budgets:** for each binding machine and case, the budget is the median of the `frame` mode rounded up to the next 10 ms (memory to the next 5 MiB, size to the next MiB). The limit is the budget × 1.25, as in Pagewing. *(author's call)*
- **Recording:** the measured values, the machines and the resulting budgets are recorded in `Docs/DECISIONS.md`. This closes the first acceptance criterion of milestone 1.
- **Enforcement** *(author's call)*: the measurement runners are started by hand, so a PR check must never wait for them.
  - **Size** is deterministic and is checked in the normal CI (`ci.yml`, hosted runners) on every PR: the published shell must stay within its size limit.
  - **Time and memory** are checked with `measure.yml` on the binding machines as an acceptance criterion of every milestone that changes the shell or the startup path (first in 1.2), and before each release.
- **From probe to real frame:** milestone 1.2 measures the gallery app's published frame the same way. It must stay within the probe's limits (budget × 1.25). If it cannot, that is a finding for the owner, not a new budget; whatever the owner decides is a decisions entry. The gallery's own medians become the regression baseline for later milestones.
- **Changes:** budgets are never loosened to make a check pass. A change is a new decisions entry.
- **App specs:** each app spec must set targets, measured the same way, for:
  - opening a large file to the first usable view;
  - scrolling (frame interval p99 and maximum, as Pagewing's `--measure-scroll`);
  - typing latency in a large document, including one with many comments and tracked changes (Write);
  - recalculation after an edit in a large workbook (Sheets);
  - saving and exporting to PDF and to the other formats;
  - a second document opened in a running app.

  The research behind this list: Word typing lag with many tracked changes, Excel recalculation and scrolling, LibreOffice laying out a whole DOCX on load and saving slowly, and slow PowerPoint export with many images.

## 10. Milestones of this subproject

| Id | Title | Goal |
|---|---|---|
| 1 | Design directions & spec | (this milestone) one direction is chosen, the speed targets are measured and set, and this spec is written |
| 1.1 | Tokens and base controls | The shared shell project has the light, dark and high-contrast tokens, the fonts, the icon set and the base controls (button, toggle, input, select, checkbox, radio, segmented control, menu, context menu, tooltip, dialog, notification bar). The contrast test, the localization mechanism and the missing-translation test are in place, and a gallery app shows every control in both themes and languages. |
| 1.2 | App frame | The frame works in the gallery app: title bar with document tabs and search, ribbon with compact mode, side panels, status bar, Alt field codes, F6 pane focus, start view and settings dialog (theme, language after restart, compact, autosave). The German layout test and the size check in CI pass; a `measure.yml` run on the binding machines stays within the limits. |

*(author's call)* Milestone 2 (Raffica PDF spec) can be written after milestone 1. The PDF app's implementation milestones take milestone 1.2 as their prerequisite, so the PDF UI is built on the finished frame (owner's choice: frame before the PDF app).

## 11. Milestone 1 scope (what its plan covers)

The plan for milestone 1 covers only the items below. Milestones 1.1 and 1.2 get their own plans when they start; this spec is their source.

1. Measurement:
   - the probe;
   - `measure.yml`;
   - `RUNNERS.md`;
   - runs on the three machines;
   - the decisions entry with values and budgets.
2. The direction is chosen and recorded. This is done: decisions entry of 2026-11-03.
3. This spec is approved and linked in the roadmap as the spec of subproject 2, and milestones 1.1 and 1.2 are entered.

**Open until hardware runs:** the Linux and macOS numbers need the owner to start the runners. Until then the criterion "The measured values are recorded" stays open, with the command `gh workflow run measure.yml -f platform=<windows|linux|macos>`.

## 12. Left to the builder

- Project and namespace names, folder layout of the shell project and the gallery app.
- How the probe takes its timestamps, within the definition in section 9.1.
- The exact list of ribbon commands per app. That belongs to each app spec; the frame provides the slots.
- Mapping the token names in appendix A to AXAML resource keys.

## Appendix A – Tokens and measured contrast

(From the approved canvas, version 9. Values were measured with WCAG 2.x relative luminance.)

Source: the approved Claude Design canvas https://claude.ai/artifact/RGyDq2FZQYBttjkJp6MQbQ, version 9 (version id 1793713663-c255), 17 boards. Values are taken from the published board sources (the local build that was published as v9). Contrast values are WCAG 2.x ratios, computed with `d_colors.py` (relative luminance), not copied from the boards. Board names in parentheses are where a value appears.

### 1. Colours

#### 1.1 Neutral surfaces and text

| Token | Light | Dark | Used for |
|---|---|---|---|
| `surface.page` | #FFFFFF | #1B1D21 | Document page, spreadsheet cells, title bar, inputs, menus, dialogs, tooltips, notification bars |
| `surface.band` | #F5F6F8 | #222429 | Ribbon band, side panels, status bar, sheet-tab row, row/column headers, dialog footer, active command tab, active document tab |
| `surface.desk` | #E9EBEE | #131417 | Work area around the page or slide, ruler strip, preview area of Start tiles |
| `state.hover` | #E6E8EC | #2C2F35 | Hover fill for buttons, list items, tabs, menu items, rows |
| `selection.tint` | #DCDFE4 | #3A3E45 | Selected nav item, thumbnail, mark, file row, segmented choice; selected cell range; selected row/column headers |
| `text.selection` | #D6DAE0 | #3A3E45 | Browser text selection (`::selection`) |
| `text.primary` | #1A1D21 | #E8EAED | All UI text, document text, caret |
| `text.secondary` | #4A5059 | #A9AFB8 | Group labels, panel titles, status-bar cells, counts, shortcuts in menus, placeholders, hints |
| `text.disabled` | #9AA0A8 | #5E646D | Disabled text and the disabled control edge |
| `text.onInk` | #FFFFFF | #1B1D21 | Text and glyphs on ink fills (pressed buttons, active field codes, app marks, checked checkbox) |
| `border.control` | #7A818B | #7D848E | 1 px edge of inputs, dropdowns, boxed buttons, style tiles, tooltips, menus, dialogs, Start tiles, slide placeholders |
| `rule` | #D3D7DD | #373B42 | 1 px separators (panel edges, under title bar, status bar top, menu separators, inactive field-code edge); dark page edge |
| `grid.line` | #DADDE2 | #34373D | Spreadsheet gridlines |
| `button.primary.fill` | #1A1D21 | #E8EAED | Primary dialog button (same value as `text.primary`) |
| `button.primary.hover` | #3A3F46 | #C9CED6 | Primary button hover (light on Dialog board, dark on System dark matrix) |
| `scrim` | rgba(26,29,33,0.32) | not shown | Dim layer behind modal dialogs (Dialog board, light only) |
| `page.shadow` | 0 1px 2px rgba(26,29,33,.14), 0 8px 24px rgba(26,29,33,.10) | none; 1 px `rule` edge instead | Document page and slide on the desk |

#### 1.2 App inks, review, danger, warning

| Token | Light | Dark | Used for |
|---|---|---|---|
| `ink.write` | #1D4F91 | #8DB4EE | Write: app mark, active command tab (text and 1 px border), 1 px rule under the tab row, pressed/on buttons, active style tile, 3 px selection edge, focus ring, field codes while Alt is held, F6 pane border |
| `ink.sheets` | #1E6B47 | #7CCBA2 | Same roles in Sheets; also the 2 px active-cell and range outline |
| `ink.slides` | #A3420F | #F0A374 | Same roles in Slides; also the dashed outline and handles of the selected slide object |
| `ink.pdf` | #5E1A3E | #E5A4C5 | Same roles in PDF |
| `review.violet` | #7B2FBE | #B48CF0 | Tracked insertions (text), comment anchor underline, comment card border, change bar; same in every app |
| `review.tint` | #F1EAF9 | #2E2840 | Background of a comment anchor |
| `danger` | #B21F2D | #FF6F66 | Only irreversible actions: destructive button text, outline, focus ring and pressed fill; destructive menu item text |
| `warning.icon` | #8A6A00 | #D9A93A | Warning triangle icon in notification bars only, never a fill or text |
| `focus.ring` | = active app ink | = active app ink | 2 px keyboard focus ring; `danger` on destructive controls |

The dark inks appear on app boards for Write (F6 board) and Sheets (dark board). The dark Slides and PDF inks appear only in the System colour table and the Komponenten title bars.

#### 1.3 Document and chart content (not UI tokens, listed for completeness)

Sheets chart neutrals #2E3440 / #7A8494 / #C9CED6 (dark chart: #C9CED6 / #7A8494 / #3E444D); Write table rows #C9CDD3; highlighter swatch #F7D84A; PDF highlight #FBE7A1; slide theme colours #16213A, #13626B, #D3E6E8, #4A5568.

### 2. Measured contrast

Thresholds: **4.5** for text, **3** for UI parts (edges, rings, icons, indicators), **exempt** for disabled, **decorative** for rules and gridlines that carry no meaning. ✓ = passes its threshold, ✗ = below it.

#### 2.1 Inks, review, danger, warning on surfaces

**Light**

| Token | Hex | on `surface.page` #FFFFFF | on `surface.band` #F5F6F8 | on `surface.desk` #E9EBEE | on `state.hover` #E6E8EC | on `selection.tint` #DCDFE4 | Threshold |
|---|---|---|---|---|---|---|---|
| `ink.write` | #1D4F91 | 8.14 ✓ | 7.53 ✓ | 6.82 ✓ | 6.64 ✓ | 6.09 ✓ | 4.5 text (active tab, field code) / 3 UI (edge, ring) |
| `ink.sheets` | #1E6B47 | 6.46 ✓ | 5.98 ✓ | 5.41 ✓ | 5.27 ✓ | 4.84 ✓ | 4.5 / 3 |
| `ink.slides` | #A3420F | 6.28 ✓ | 5.81 ✓ | 5.26 ✓ | 5.12 ✓ | 4.70 ✓ | 4.5 / 3 |
| `ink.pdf` | #5E1A3E | 12.40 ✓ | 11.47 ✓ | 10.38 ✓ | 10.11 ✓ | 9.28 ✓ | 4.5 / 3 |
| `review.violet` | #7B2FBE | 7.02 ✓ | 6.49 ✓ | 5.88 ✓ | 5.72 ✓ | 5.25 ✓ | 4.5 text (insertions) |
| `danger` | #B21F2D | 6.72 ✓ | 6.21 ✓ | 5.62 ✓ | 5.47 ✓ | 5.03 ✓ | 4.5 text + outline + ring |
| `warning.icon` | #8A6A00 | 5.07 ✓ | 4.69 ✓ | 4.24 ✓ | 4.13 ✓ | 3.79 ✓ | 3 UI (icon only) |

**Dark**

| Token | Hex | on `surface.page` #1B1D21 | on `surface.band` #222429 | on `surface.desk` #131417 | on `state.hover` #2C2F35 | on `selection.tint` #3A3E45 | Threshold |
|---|---|---|---|---|---|---|---|
| `ink.write` | #8DB4EE | 7.95 ✓ | 7.32 ✓ | 8.68 ✓ | 6.32 ✓ | 5.06 ✓ | 4.5 text (active tab, field code) / 3 UI (edge, ring) |
| `ink.sheets` | #7CCBA2 | 8.78 ✓ | 8.08 ✓ | 9.58 ✓ | 6.98 ✓ | 5.59 ✓ | 4.5 / 3 |
| `ink.slides` | #F0A374 | 8.19 ✓ | 7.54 ✓ | 8.94 ✓ | 6.51 ✓ | 5.22 ✓ | 4.5 / 3 |
| `ink.pdf` | #E5A4C5 | 8.40 ✓ | 7.73 ✓ | 9.16 ✓ | 6.68 ✓ | 5.34 ✓ | 4.5 / 3 |
| `review.violet` | #B48CF0 | 6.39 ✓ | 5.88 ✓ | 6.97 ✓ | 5.08 ✓ | 4.07 ✗ | 4.5 text (insertions) |
| `danger` | #FF6F66 | 6.20 ✓ | 5.71 ✓ | 6.77 ✓ | 4.93 ✓ | 3.95 ✗ | 4.5 text + outline + ring |
| `warning.icon` | #D9A93A | 7.78 ✓ | 7.16 ✓ | 8.50 ✓ | 6.19 ✓ | 4.96 ✓ | 3 UI (icon only) |


Not used on the canvas: `review.violet`, `danger` and `text.disabled` on `selection.tint`. The ✗ cells for `review.violet` and `danger` on dark `selection.tint` are such unused pairings. The `border.control` ✗ on `selection.tint` is the selected segment's own edge; its adjacent surface is `surface.band` (3.64 light, 4.11 dark), and the selection is carried by the 3 px ink edge.

#### 2.2 Neutral text, control edge, rules on surfaces

**Light**

| Token | Hex | on `surface.page` #FFFFFF | on `surface.band` #F5F6F8 | on `surface.desk` #E9EBEE | on `state.hover` #E6E8EC | on `selection.tint` #DCDFE4 | Threshold |
|---|---|---|---|---|---|---|---|
| `text.primary` | #1A1D21 | 16.91 ✓ | 15.64 ✓ | 14.16 ✓ | 13.79 ✓ | 12.66 ✓ | 4.5 text |
| `text.secondary` | #4A5059 | 8.13 ✓ | 7.52 ✓ | 6.81 ✓ | 6.63 ✓ | 6.09 ✓ | 4.5 text |
| `text.disabled` | #9AA0A8 | 2.64 | 2.44 | 2.21 | 2.15 | 1.97 | exempt (disabled) |
| `border.control` | #7A818B | 3.93 ✓ | 3.64 ✓ | 3.29 ✓ | 3.21 ✓ | 2.94 ✗ | 3 UI |
| `rule` | #D3D7DD | 1.44 | 1.34 | 1.21 | 1.18 | 1.08 | decorative |
| `grid.line` | #DADDE2 | 1.36 | 1.26 | 1.14 | 1.11 | 1.02 | decorative (quiet grid) |

**Dark**

| Token | Hex | on `surface.page` #1B1D21 | on `surface.band` #222429 | on `surface.desk` #131417 | on `state.hover` #2C2F35 | on `selection.tint` #3A3E45 | Threshold |
|---|---|---|---|---|---|---|---|
| `text.primary` | #E8EAED | 14.00 ✓ | 12.88 ✓ | 15.28 ✓ | 11.13 ✓ | 8.91 ✓ | 4.5 text |
| `text.secondary` | #A9AFB8 | 7.64 ✓ | 7.03 ✓ | 8.34 ✓ | 6.08 ✓ | 4.87 ✓ | 4.5 text |
| `text.disabled` | #5E646D | 2.83 | 2.60 | 3.09 | 2.25 | 1.80 | exempt (disabled) |
| `border.control` | #7D848E | 4.47 ✓ | 4.11 ✓ | 4.88 ✓ | 3.55 ✓ | 2.85 ✗ | 3 UI |
| `rule` | #373B42 | 1.50 | 1.38 | 1.64 | 1.19 | 1.05 | decorative |
| `grid.line` | #34373D | 1.41 | 1.30 | 1.54 | 1.12 | 1.11 | decorative (quiet grid) |

#### 2.3 Text on filled colour (pressed buttons, active badges, app marks)

| Pairing | Light | Dark | Threshold |
|---|---|---|---|
| `text.onInk` on `ink.write` | #FFFFFF on #1D4F91: 8.14 ✓ | #1B1D21 on #8DB4EE: 7.95 ✓ | 4.5 text |
| `text.onInk` on `ink.sheets` | #FFFFFF on #1E6B47: 6.46 ✓ | #1B1D21 on #7CCBA2: 8.78 ✓ | 4.5 text |
| `text.onInk` on `ink.slides` | #FFFFFF on #A3420F: 6.28 ✓ | #1B1D21 on #F0A374: 8.19 ✓ | 4.5 text |
| `text.onInk` on `ink.pdf` | #FFFFFF on #5E1A3E: 12.40 ✓ | #1B1D21 on #E5A4C5: 8.40 ✓ | 4.5 text |
| `text.onInk` on `review.violet` | #FFFFFF on #7B2FBE: 7.02 ✓ | #1B1D21 on #B48CF0: 6.39 ✓ | 4.5 text |
| `text.onInk` on `danger` | #FFFFFF on #B21F2D: 6.72 ✓ | #1B1D21 on #FF6F66: 6.20 ✓ | 4.5 text |
| `text.onInk` on `button.primary.fill` | #FFFFFF on #1A1D21: 16.91 ✓ | #1B1D21 on #E8EAED: 14.00 ✓ | 4.5 text |
| `text.onInk` on `button.primary.hover` | #FFFFFF on #3A3F46: 10.61 ✓ | #1B1D21 on #C9CED6: 10.68 ✓ | 4.5 text |
| `text.primary` on `review.tint` (comment anchor) | #1A1D21 on #F1EAF9: 14.40 ✓ | #E8EAED on #2E2840: 11.66 ✓ | 4.5 text |
| `review.violet` on `review.tint` (anchor underline) | #7B2FBE on #F1EAF9: 5.97 ✓ | #B48CF0 on #2E2840: 5.32 ✓ | 3 UI |

#### 2.4 Other measured pairings

| Pairing | Value | Threshold |
|---|---|---|
| Light page edge: none, page vs desk | 1.19 | decorative / surface separation |
| Dark page edge `rule` #373B42 vs dark page | 1.50 | decorative / surface separation |
| Dark page edge `rule` #373B42 vs dark desk | 1.64 | decorative / surface separation |
| Dark page vs dark desk | 1.09 | decorative / surface separation |
| Dark band vs dark page | 1.09 | decorative / surface separation |
| Dark band vs dark desk | 1.19 | decorative / surface separation |
| Light band vs light page | 1.08 | decorative / surface separation |
| Light band vs light desk | 1.10 | decorative / surface separation |
| Dark hover vs dark band | 1.16 | decorative / surface separation |
| Dark selection.tint vs dark hover | 1.25 | decorative / surface separation |
| Light hover vs light band | 1.13 | decorative / surface separation |
| Light selection.tint vs light hover | 1.09 | decorative / surface separation |
| Dialog page #FFFFFF vs desk under scrim (#A7A9AC) | 2.36 | decorative / surface separation |
| `border.control` (dialog edge) vs desk under scrim (#A7A9AC) | 1.67 | decorative / surface separation |
| Placeholder (`text.secondary`) on light page | 8.13 ✓ | 4.5 text |
| Placeholder (`text.secondary`) on dark page | 7.64 ✓ | 4.5 text |
| Slide placeholder dashed edge `border.control` on slide #FFFFFF | 3.93 ✓ | 3.0 UI |

### 3. Lightness and colour distance

| Token | Light hex | L* | Dark hex | L* |
|---|---|---|---|---|
| `ink.write` | #1D4F91 | 33.8 | #8DB4EE | 72.5 |
| `ink.sheets` | #1E6B47 | 40.0 | #7CCBA2 | 75.8 |
| `ink.slides` | #A3420F | 40.8 | #F0A374 | 73.5 |
| `ink.pdf` | #5E1A3E | 21.8 | #E5A4C5 | 74.3 |
| `review.violet` | #7B2FBE | 37.8 | #B48CF0 | 65.6 |
| `danger` | #B21F2D | 39.0 | #FF6F66 | 64.6 |
| `warning.icon` | #8A6A00 | 46.6 | #D9A93A | 71.9 |

Dark ink spread (four app inks): 72.5 to 75.8.

| ΔE2000 | Light | Dark |
|---|---|---|
| `ink.pdf` vs `review.violet` | 24.1 | 19.0 |
| `ink.pdf` vs `danger` | 23.4 | 22.5 |

### 4. Typography, spacing, radius, rules, sizes

#### 4.1 Type

All UI text uses tabular figures (`font-variant-numeric: tabular-nums`).

| Token | Family | Size / line height | Weight | Used for |
|---|---|---|---|---|
| `type.ui` | Public Sans | 14 px / 1.3 | 400 | Base UI text: buttons, menus, list rows, inputs, dialogs |
| `type.ui.emphasis` | Public Sans | 14 px / 1.3 | 600 | Active command tab, selected nav item, selected segment, sheet tab, primary button, destructive button |
| `type.ui.medium` | Public Sans | 14 px / 1.3 | 500 | Command tabs at rest, current document tab |
| `type.secondary` | Public Sans | 13 px | 400 | Status-bar cells, panel counts, menu shortcuts, file folder and date, tooltip title (600) |
| `type.label` | Public Sans | 12 px / 1.2 | 400 | Group labels, panel titles, section legends; sentence case, `text.secondary`, normal letter-spacing |
| `type.small` | Public Sans | 12 px | 400 | Large-tile labels (line height 1.15), style-tile names, nav segmented control, tooltip description (1.35), hints, empty-state lines |
| `type.tile` | Public Sans | 13 px | 500 | Start "Neu" tile labels |
| `type.heading.dialog` | Public Sans | 15 px | 600 | Dialog titles |
| `type.heading.view` | Public Sans | 18 px | 600 | Start view heading "Zuletzt verwendet" |
| `type.mark` | Public Sans | 13 px (title bar), 12 px (lists), 11 px (Start tiles) | 600 | App marks W / S / SL / PDF |
| `type.fieldCode` | Barlow Semi Condensed | 12 px / 1 | 600 | Field-code badges only (letter-spacing .02em) |
| `type.document` | Carlito (Calibri-metric) | 15 px / 1.5 body; h1 22/700; h2 17/700; table 14 | 400/700 | Write page; Sheets cells 14 px; PDF form content |
| `type.slide` | Libre Franklin | slide content | 400–700 | Slide content only (document font) |

Sheets row and column headers use Public Sans 12 px / 600; Write outline numbers Public Sans 600 in `text.secondary`.

#### 4.2 Spacing scale

2 · 4 · 6 · 8 · 10 · 12 · 16 · 24 px (System board). Ribbon groups: 8 px inner padding each side plus 8 px column gap, so 24 px between group contents. Panel padding 12 px. Status-bar cell gap 20 px (outside the scale). Dialog padding 16 px.

#### 4.3 Radius

| Token | Value | Applies to |
|---|---|---|
| `radius.surface` | 0 | Title bar, command tab row, ribbon band, panels, status bar, document page, slide, spreadsheet grid and cells, document tabs, sheet tabs, nav selection items (the 3 px ink edge stays square), app marks |
| `radius.checkbox` | 2 px | Checkboxes; Start tile preview area |
| `radius.control` | 3 px | Buttons, large and style tiles, dropdowns, inputs, selects, search field, segmented controls (outer ends only), switches, field-code badges, Start tiles, document-tab close button |
| `radius.overlay` | 3 px | Menus and context menus, tooltips, dialogs, notification bars |

#### 4.4 Rules and indicators

| Token | Value | Where |
|---|---|---|
| `rule.width` | 1 px | All separators and control edges |
| `indicator.tabRow` | 1 px `ink.*` | Rule under the command tab row (ribbon top edge); active command tab has a 1 px ink border on three sides |
| `indicator.docTab` | 3 px `ink.*`, top, inset | Current document tab in the title bar |
| `indicator.selection` | 3 px `ink.*`, left, inset, on `selection.tint` | Nav items, thumbnails, PDF pages and marks, file rows |
| `indicator.tabUnderline` | 3 px `ink.*`, bottom, inset | Selected segment (nav view switch, theme choice), active sheet tab |
| `focus.ring` | 2 px, offset 2 px (−2 px inset in list rows, −3 px on tabs) | Keyboard focus |
| `focus.pane` | 1 px `ink.*`, inset | F6 pane focus |
| `cell.selection` | 2 px `ink.*`, inset | Active cell and selected range |
| `document.rules` | 1.5 px `text.primary` | Table header and total rules in documents and Sheets |
| `object.selection` | 1.5 px dashed `ink.*`, 8 px square handles with 1.5 px ink border | Selected slide object |

The ribbon has one group separator left: the 1 px `rule` before the destructive group in PDF. All other groups are separated by space only.

#### 4.5 Sizes

| Token | Value |
|---|---|
| `size.titleBar` | 36 px (caption buttons 46 × 36; document tab 28 px high; close button 24 × 24) |
| `size.tabRow` | 32 px (active tab 33 px, overlapping the ribbon's top rule) |
| `size.ribbon` | 82 px = 1 px top ink rule + 80 px + 1 px bottom rule; inside: 6 px top padding, 54 px command area (two 26 px rows with a 2 px gap, or one 54 px tile), 2 px gap, 14 px label row, 4 px bottom padding |
| `size.ribbon.compact` | 40 px, one row, small buttons only |
| `size.statusBar` | 30 px (cells 20 px high; zoom buttons 24 × 24; zoom slider 96 px) |
| `size.panelHeader` | 38 px |
| `size.row.list` | 32 px (nav items, menu items, list items) |
| `size.row.file` | 40 px (Start and open lists) |
| `size.button` | 26 px high, min width 26 px; dialog buttons 28 px high, min width 96 px |
| `size.dropdown` | 26 px |
| `size.searchField` | 28 px high, flex 0 1 300 px, min 200 px |
| `size.tile.large` | 54 px high, min width 48 px (PDF: fixed 78 px, two-line labels) |
| `size.tile.style` | 90 × 54 px |
| `size.button.destructive` | 40 px high in the PDF ribbon; 26 px elsewhere |
| `size.tile.start` | 150 px high, two per row in a 336 px column |
| `size.icon` | 16 px small, 20 px large tiles; stroke 1.5 px, round caps and joins, non-scaling stroke |
| `size.fieldCode` | 18 px high, min width 18 px, 0 4 px padding; anchored at the control's bottom right (right −4 px, bottom −6 px) |
| `size.hitTarget.min` | 24 px |
| `size.dialog` | 520 px wide; header 44 px; footer 52 px |
| `size.notificationBar` | 40 px in apps (inset 6 px / 8 px); 42 px on Komponenten |
| Other | Formula bar 34 px; sheet-tab row 34 px (tabs 26 px); spreadsheet rows 22 px, row header 44 px; Slides notes header 28 px; Write nav 240 px, Slides rail 228 px, PDF pages 204 px, PDF marks 256 px, comments pane 300 px |

### 5. Component states

Each cell names the tokens for that state. "ink" = the active app ink (`ink.write`, `ink.sheets`, `ink.slides` or `ink.pdf`). The names resolve to the light and dark values in section 1, so one table serves both themes; differences between themes are noted. "—" = the canvas does not show this state for the component.

| Component | Rest | Hover | Pressed / on | Selected | Focus (keyboard) | Disabled |
|---|---|---|---|---|---|---|
| Button (ribbon, icon or icon + label) | transparent, `text.primary` | `state.hover` | fill ink, `text.onInk` | — | 2 px `focus.ring` ink | `text.disabled`, transparent |
| Boxed button | `surface.page`, 1 px `border.control` | `state.hover` | fill ink, `text.onInk` | — | `focus.ring` | `text.disabled` text and edge |
| Primary button | `button.primary.fill`, `text.onInk`, 600 | `button.primary.hover` | fill ink | — | `focus.ring` | same as boxed button |
| Toggle (e.g. bold, align, track changes) | as button | `state.hover` | on = fill ink, `text.onInk`, `aria-pressed` | — | `focus.ring` | — |
| Ribbon large tile | as button, 54 px | `state.hover` | fill ink, `text.onInk` | — | `focus.ring` | — |
| Style tile | `surface.page`, `border.control` | `state.hover` | on = fill ink, border ink, `text.onInk` | — | `focus.ring` | — |
| Nav item / thumbnail / page / mark / file row | transparent, `text.primary` | `state.hover` | — | `selection.tint` + 3 px ink left edge, 600 | 2 px ink ring, inset | `text.disabled` (System) |
| Command tab | transparent, 500 | `state.hover` | — | `surface.band`, 1 px ink border, ink text, 600 | 2 px ink ring, inset −3 px | `text.disabled` (System) |
| Document tab | transparent, `text.secondary` | `state.hover` | — | `surface.band`, 1 px `rule` border, 3 px ink top edge, `text.primary`, 500 | 2 px ink ring, inset | — |
| Segmented choice (nav view, theme) | `surface.page`, `border.control`, 12 px (theme choice: 14 px) | `state.hover` | — | `selection.tint`, 3 px ink bottom edge, 600 | `focus.ring` (shown on the Dialog board) | — |
| Sheet tab | `surface.band`, 1 px `rule` | `state.hover` | — | `surface.page`, `text.primary` 600, 3 px ink bottom edge | 2 px ink ring, inset | — |
| Menu item | transparent | `state.hover` | System matrix: fill ink | — | 2 px ink ring, inset | `text.disabled` incl. shortcut |
| Destructive menu item | `danger` text and icon, 600 | `state.hover` | — | — | 2 px `danger` ring | — |
| Cell | `surface.page`, 1 px `grid.line` | none | — | Active cell: 2 px ink inset; range: `selection.tint` + 2 px ink outline; headers `selection.tint` | same as selected | — |
| Destructive button | `surface.page`, 1 px `danger` edge, `danger` text 600 | `state.hover` | fill `danger`, `text.onInk` | — | 2 px `danger` ring | `text.disabled` text and edge |
| Checkbox | 16 px, `surface.page`, `border.control`, radius 2 | — | checked: fill ink, `text.onInk` tick | — | `focus.ring` | — |
| Text field / search | `surface.page`, `border.control`, placeholder `text.secondary` | edge `text.secondary` (System) | typing: edge `text.primary` (System) | — | `focus.ring` | `surface.band`, `text.disabled` text and edge |
| Field-code badge | hidden | — | Alt held: fill ink, `text.onInk` | — | — | — |
| Pane (F6) | no frame | — | — | — | 1 px ink inset border, pane title `text.primary` | — |

Dark differences: `button.primary.hover` is #C9CED6. The dark page carries a 1 px `rule` edge instead of `page.shadow`. Measured dark state separations: hover vs band 1.16, selection vs hover 1.25, ink edge on selection 5.06 (Write), focus ring on band 7.32 (Write).

### 6. Behaviour shown on the boards

- **Field codes (Alt):** hidden at rest. While Alt is held, every command shows its code in an ink badge at its bottom-right corner (two digits: group digit + position, e.g. 13 = Bold). Command tabs show their letter. The group number appears next to the group label, and panes show a pane number (6, 7). The search field shows Q. The status bar shows "Alt · Feldcode eingeben · Esc bricht ab". Codes are the same in every language. Boards: Sheets – Feldcodes (Alt), the Alt prop on every app board, and the System demo.
- **F6 pane focus:** F6 / Shift+F6 cycles navigation → document → comments or format pane. The focused pane gets a 1 px ink inset border and its title turns `text.primary`. The status bar shows "Fokus: …" only while pane navigation is active. Boards: Write – F6-Fokus (dark), with "Fokus: Kommentare"; the interactive System demo. Static boards show no pane frame and no focus cell.
- **Compact mode** (`compact` prop on the app boards): the ribbon becomes one 40 px row in the same group order. Group labels are hidden (kept as `aria-label`). Large tiles become 26 px buttons. Icon + label commands become icon-only, with the text kept in the DOM. Style tiles collapse into a dropdown. Each group ends with a "▾" button for its further commands. The destructive button keeps its label.
- **Status bar:** cells left to right:
  - save state ("Gespeichert" / "Nicht gespeichert");
  - position (Seite / Blatt / Folie x von y);
  - count: Write word count, or in Sheets "Summe …" only when a range is selected;
  - focus cell, during pane navigation only;
  - zoom on the right (−, slider, +, value).

  The Alt hint appears only while Alt is held. The language appears only when the selection's language differs from the document's (no board shows this case). No icons apart from zoom. The Start view has no status bar.
- **Notification bars:** neutral `surface.page` bar under the ribbon, one action, closable.
  - **Info:** circle-i icon in `text.secondary`.
  - **Warning:** triangle in `warning.icon` with a bold opening phrase.
  - **Error:** octagon in `text.primary` with a bold opening phrase, no colour.

  Red is never used for warnings or errors. Examples: crash recovery "Vergleichen", missing font "Schriftarten anzeigen", save failure "Speichern unter …", Sheets import "In Datum umwandeln / Als Text behalten", PDF scan "Text erkennen".
- **Destructive actions:** isolated in their own group at the end of the ribbon, after space and the only group rule. The group is labelled "Endgültig anwenden" and has a short explanation beside it. When there is nothing to apply, the button is disabled with a reason in its tooltip. Destructive menu items come last, after a wider separator.
- **Search field:** a calm field in the title bar with a magnifier and the placeholder "Suchen", and no keycap. The shortcut lives in the tooltip "Suchen (Strg+K)" and in the System keyboard reference: "Strg+K Suchen · F6 und Umschalt+F6 Bereich wechseln · Alt Feldcodes · Esc Abbrechen".
- **Tooltips:** name (13 px / 600), shortcut as plain `text.secondary` text, one-line description. 1 px `border.control` edge, radius 3, no arrow, no shadow.
- **Dialogs:** modal over the dimmed app (`scrim`). Header with title and close button, body, footer on `surface.band` with Cancel and the primary action on the right.
- **Start view** (no file open, under the running app's title bar):
  - **"Neu":** blank tiles "Leeres Dokument", "Leere Tabelle", "Leere Präsentation" and "PDF öffnen", each with a small app mark in its corner.
  - **"Öffnen":** "Durchsuchen …" opens the system's file dialog; Raffica has no own file browser.
  - **"Zuletzt verwendet":** "Angeheftet" and "Weitere Dateien" rows with app mark, name, folder and date.

## Appendix B – Normalized where the boards disagree

The canvas has small inconsistencies between boards. Where they differ, this spec decides *(author's call)*:

- **Panel padding:** 12 px in every panel, including the Write navigation panel, empty-state lines and the PDF marks note (the Write board still shows 10 px).
- **Control height:** all buttons, segmented controls and view switches are 26 px with 14 px text, including Write's "Headings / Pages / Results" switch (24 px with 12 px text on the board). The destructive button is 26 px like its neighbours; the PDF board's 40 px version is not used.
- **Minimum text size:** 12 px in the chrome. Ruler numbers are 12 px (11 px on the board).
- **Tooltip:** at most 240 px wide.
- **Notification bar:** 40 px high.
- **Selected tab:** 32 px on the panel grey, as on the app boards; the 30 px white version on the Komponenten board is not used.
- **Button padding:** 8 px horizontal for buttons with text in dialogs and bars; 5 px for ribbon buttons.
- **Text selection:** text selection in inputs and documents uses `selection.tint`; the separate #D6DAE0 is dropped.
- **Document tabs:** at most 240 px wide in every app, with the name truncated in the middle.
- **Large ribbon tiles:** at least 48 px wide and sized to their label, in every app; the PDF board's fixed 78 px is not used.
- **Comments:** the docked comments pane is the F6 target. Margin comment cards are a Write document feature, settled in the Write spec.
- **Label line height:** 1.25 for single-line labels; multi-line text uses 1.4.
- **Spacing:** only values from the scale. The status-bar cell gap is 16 px (20 on the board) and the Start column gap 24 px (28 on the board).
- **Dark scrim:** the dialog scrim exists in dark too. Its value is set in milestone 1.1, and the frame stays visible behind it.
- **Pairings that fail but are not used:** `review.violet` and `danger` on dark `selection.tint` are not allowed pairings; the contrast test lists them as forbidden. The control edge on `selection.tint` is not a meaningful edge, because the 3 px ink edge carries the selection.
- **Not shown on any board:** the dark Slides and PDF inks in an app, and the status-bar language cell. Milestone 1.1's gallery shows them.
