# Milestone 1.2 – App frame: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The shared app frame (title bar with document tabs and search, ribbon with compact mode, side panels, status bar, Alt field codes, F6 pane focus, start view, settings dialog) works in the gallery app in English and German, and the published gallery stays within the speed and size limits recorded in milestone 1.

**Architecture:** `Raffica.Shell` gets a `Frame` namespace. Pure logic (command and ribbon definitions, `FieldCodeSession`, `PaneRing`, `SettingsStore`) has no Avalonia dependency beyond key enums and is tested without a window. The controls (`AppFrame`, `TitleBar`, `DocumentTabs`, `RibbonView`, `Pane`, `StatusBar`, `StartView`, `SettingsDialog`) are built from the 1.1 tokens, strings and `Dialog`/`DialogHost`; an app fills the frame with a `RibbonDefinition`, panes and content. The gallery gets a second view, `--view frame`, that shows the frame with a sample Write ribbon and carries the measurement switches of the probe.

**Tech Stack:** .NET 10 Native AOT, Avalonia 12.1.4, xunit.v3 with `Avalonia.Headless.XUnit` (as in 1.1), `System.Text.Json` source generation for the settings file.

**Spec:** [Docs/superpowers/specs/2026-11-03-design-system-shell-design.md](../specs/2026-11-03-design-system-shell-design.md): sections 4.4, 4.5, 5, 6, 7, 8, 9.4 and 10 (milestone 1.2), appendix A section 4.5 (sizes) and section 5 (states), appendix B. The boards in `Docs/design/direction-d/` are the visual reference where the spec is silent (`AltCodes`, `F6Dunkel`, `Start`, `Dialog`, `Komponenten`, `System`); the spec wins where they differ. Limits: [Docs/DECISIONS.md](../../DECISIONS.md), entry "Speed targets of the frame (M1)".

## Global Constraints

- **Sizes (px):** title bar 36 (caption buttons 46 × 36, document tab 28 high, close button 24 × 24); tab row 32 (active tab 33); ribbon band 82, compact 40; status bar 30 (cells 20 high, zoom buttons 24 × 24, slider 96, cell gap 16); panel header 38 with a 12 px title, panel padding 12; search field 28 high, `flex 0 1 300`, min 200; large tile 54 high, min 48 wide; buttons 26; field-code badge 18 high, min 18 wide, padding `0 4`, 12 px Barlow Semi Condensed 600, anchored at the control's bottom right (right −4, bottom −6); document tab max 240 wide, name truncated in the middle; notification bar 40; hit targets ≥ 24.
- **Radius:** 0 for title bar, tab row, band, panels, status bar and document tabs; 3 for buttons, tiles, search field, badges, start tiles, dialogs. No decorative shadows, no uppercase, no monospace, no colour outside the active and focus states (app ink: active tab and rule under the tab row, pressed and checked buttons, 3 px edge of the active nav item and document tab, focus ring, F6 pane border, badges while Alt is held).
- **Field codes:** the sequence of spec 4.5, codes language independent, declared in each command, never derived from position; a duplicate code in a tab is an error. Esc goes back one step (command codes → tab letters → off).
- **F6 / Shift+F6:** navigation → document → comments or format; focused pane gets a 1 px ink inset border and its title turns `Text.Primary`; the status bar shows "Focus: <pane>" ("Fokus: …") only during pane navigation; a pointer press or Esc ends it.
- **Search:** placeholder "Search" / "Suchen", no keycap, tooltip "Search (Ctrl+K)" / "Suchen (Strg+K)"; Ctrl+K focuses it (Cmd+K on macOS); it finds commands and is never the only path.
- **Settings:** theme (Light, Dark, Same as system), language (Deutsch, English; applies after a restart and the dialog says so), compact command bar, autosave (on by default); buttons Cancel and the primary OK (dark neutral fill). First start follows the OS UI language; `--lang` stays for tests.
- **Localization:** every new string in `Strings.en.resx` and `Strings.de.resx` of the project that owns it (`TranslationTests` fails otherwise); a missing key throws; the predecessor's name appears nowhere under `src/`, `tests/`, `tools/`, `bench/` or `.github/` (write "the predecessor").
- **Tokens only:** no hex in component styles (`NoHexInComponentStylesTests`); this milestone adds sizes to `Metrics.axaml` and no colour token.
- **Stack:** Native AOT, compiled bindings, no reflection, no new package except what the tasks name. The published gallery builds without warnings.
- **Not in this milestone:** any app's real commands or document view, live language switching, a file browser of our own, handing a document to another app or instance (the tiles only record the request), templates, telemetry.
- **Done** means: `dotnet build Raffica.slnx -c Release`, the test command from `CLAUDE.md` (with `RAFFICA_REQUIRE_OCR=1`), `dotnet format Raffica.slnx --verify-no-changes` and `dotnet run tools/roadmap.cs -- check` are green, and each acceptance criterion carries its proof.
- Commits: Conventional Commits, trailer `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`, stage files by name, `git show --stat HEAD` after each. Branch `feature/m01-2-app-frame`; PR to `dev` at the end. Tick the roadmap tasks as they finish.

## Calls made while planning (author's calls, the owner can overturn them)

1. **Where the frame lives:** `src/Raffica.Shell/Frame/` and `Styles/Frame/*.axaml`, one control per file. An app supplies data (`RibbonDefinition`, pane content), the frame builds the controls; commands carry their own field code.
2. **Gallery views:** the gallery keeps its control view (default) and gets `--view frame`. The measurement switches work in the frame view only. The frame view is what `measure.yml` measures.
3. **"Compact width" of the German layout test** (spec 7 does not give a number): the frame's minimum window width, set to **1024 px** here, with the compact command bar on. The normal case is 1440 px with the full ribbon.
4. **Title bar:** built on Avalonia 12's `Window.WindowDecorations` and `WindowDecorationProperties.ElementRole` for dragging and caption hit-testing. The system-title-bar fallback of spec 8 is **not** built; the check on the ProBook (CachyOS, Wayland) is an open manual item. If it fails, the fallback becomes a scope change.
5. **Search results** are a popup list under the field filtering the frame's own commands (case-insensitive, by words in the name); the predecessor's palette matcher is not used because it would tie the shell to the PDF core.
6. **Settings persistence:** one JSON file (`ShellSettings`) in the user's application-data folder; missing, unreadable or corrupt means defaults, never an error at start.
7. **The size and time checks reuse `bench/shell-probe/measure.cs`** (new `--target gallery` and a `check` subcommand) instead of a second script; the probe stays as it is.
8. **Windows bare Alt:** both Alt key events are marked handled so the system menu does not open; whether that is enough on a real machine is an open manual check (Task 6).

## Review Focus

1. **A German keyboard types `@`, `€`, `[` or `]` (AltGr = Ctrl+Alt on Windows) and field-code mode switches on, or Alt+key shortcuts get swallowed.** Expected: the mode stays off and the character is typed. Pinned by `FieldCodeSessionTests.AltGrNeverEntersTheMode` (Task 1) and `FieldCodeInputTests.AltGrTypesTheCharacter` (Task 6).
2. **Field-code mode stays on after the window loses focus, a dialog opens or the user clicks.** Expected: it ends. Pinned by `FieldCodeInputTests.ClickDeactivationAndDialogEndTheMode` (Task 6).
3. **F6 lands on a closed or hidden pane, or on a pane behind an open dialog.** Expected: closed panes are skipped, a dialog keeps F6 inside. Pinned by `PaneRingTests.ClosedPanesAreSkipped` and `PaneFocusTests.F6DoesNothingWhileADialogIsOpen` (Task 5).
4. **Many or long document tab names push the "+" button, the search field or the caption buttons out of the window.** Expected: tabs shrink to a minimum and scroll, the rest keeps its place and size. Pinned by `DocumentTabsTests.ThirtyTabsKeepPlusSearchAndCaptionButtonsInPlace` (Task 4).
5. **A corrupt, empty or unwritable settings file stops the app from starting, or a language change applies live.** Expected: defaults at start, no exception, the language changes only after a restart. Pinned by `SettingsStoreTests.CorruptOrMissingFileGivesDefaults` and `SettingsDialogTests.LanguageChangeIsOnlyStoredNotApplied` (Task 7).

---

### Task 1: Command model, ribbon definition and the field-code engine

**Files:**
- Create: `src/Raffica.Shell/Frame/CommandDefinition.cs`, `RibbonDefinition.cs`, `FieldCodeSession.cs`
- Test: `tests/Raffica.Shell.Tests/Frame/RibbonDefinitionTests.cs`, `FieldCodeSessionTests.cs`

**Interfaces:**
- Produces: `enum CommandSize { Large, Small }`, `enum CompactPlacement { Inline, Overflow }`.
- Produces: `sealed record CommandDefinition(string Id, string Name, string IconKey, string FieldCode, Action? Run = null, CommandSize Size = CommandSize.Small, CompactPlacement Compact = CompactPlacement.Inline, bool IconOnly = false, string? Shortcut = null, string? Description = null, IReadOnlyList<CommandDefinition>? Items = null)`. `Items` makes it a dropdown whose items have their own codes. `FieldCode` is 1–2 characters from `A–Z0–9`.
- Produces: `sealed record RibbonGroupDefinition(string Label, string Digit, IReadOnlyList<CommandDefinition> Commands, bool Destructive = false)`, `sealed record RibbonTabDefinition(string Name, string Letter, IReadOnlyList<RibbonGroupDefinition> Groups)`, `sealed record RibbonDefinition(IReadOnlyList<RibbonTabDefinition> Tabs)` with `void Validate()` (throws `InvalidOperationException` naming the tab and the code on: a duplicate tab letter, a duplicate command code within a tab, a malformed code, an empty group, a command that is both `Large` and `IconOnly`).
- Produces: `enum FieldCodeStage { Off, TabLetters, CommandCodes, MenuItems }`; `sealed class FieldCodeSession(RibbonDefinition ribbon)` with `FieldCodeStage Stage`, `RibbonTabDefinition? ActiveTab`, `CommandDefinition? OpenMenu`, `string Typed`, `event Action Changed`, and:
  - `bool AltDown(bool ctrlHeld)`, `bool OtherKey()`, `bool AltUp()` (the bare-Alt rule; each returns whether the key event is consumed),
  - `bool F10()` (enters `TabLetters`),
  - `bool Type(char c)` (consumed when the mode is on; runs `Run` of a complete code and ends the mode; a menu command moves to `MenuItems`; an unknown character is consumed and changes nothing),
  - `void Escape()` (one step back), `void Cancel()` (click, focus loss, dialog: to `Off`),
  - `event Action<CommandDefinition> CommandRun`, `event Action<RibbonTabDefinition> TabSelected`.

- [ ] **Step 1: Write the failing tests.**
  - `RibbonDefinitionTests`: `DuplicateCodeInATabFails`, `TheSameCodeInTwoTabsIsAllowed`, `DuplicateTabLetterFails`, `MalformedCodeFails` (`"a"`, `"123"`, `""`), `ValidRibbonPasses`, `EmptyGroupFails`.
  - `FieldCodeSessionTests` (a small fixture ribbon: tab `H` with commands `11`, `12`, `13`, a menu command `21` whose items are `A` and `B`; tab `E`):
    `BareAltPressAndReleaseTurnsTheModeOn`, `F10TurnsTheModeOn`, `AltWithAnotherKeyInBetweenIsAnOrdinaryShortcut` (session stays `Off`, `OtherKey()` returned false for the combination key and nothing is consumed), `AltGrNeverEntersTheMode` (`AltDown(ctrlHeld: true)` is not consumed and `AltUp()` leaves `Off`), `TabLetterShowsTheCommandCodes` (`Type('H')` → `CommandCodes`, `TabSelected` raised), `TwoCharacterCodeCompletesOnTheSecondKey` (`Type('1')` stays, `Type('3')` raises `CommandRun` for `13` and ends the mode), `MenuCommandShowsItsItemCodesNext`, `UnknownCharacterChangesNothing`, `EscapeGoesBackOneStep` (menu items → command codes → tab letters → off), `AltAgainEndsTheMode`, `CancelEndsTheMode`, `CodesAreLanguageIndependent` (the session never reads `Name`).
- [ ] **Step 2: Run** `dotnet test tests/Raffica.Shell.Tests -c Release --filter-namespace "*Frame*"`. Expected: FAIL (types missing).
- [ ] **Step 3: Implement** the records, `RibbonDefinition.Validate` and `FieldCodeSession` as a small state machine; the session stores whether Alt went down alone, and drops it on `OtherKey`.
- [ ] **Step 4: Run** the same command. Expected: PASS.
- [ ] **Step 5: Commit** `feat(shell): add the command model and the field-code engine`.

### Task 2: Frame metrics, `AppFrame` skeleton and `StatusBar`

**Files:**
- Modify: `src/Raffica.Shell/Styles/Metrics.axaml` (sizes below), `tests/Raffica.Shell.Tests/Tokens/MetricsTests.cs`, `src/Raffica.Shell/RafficaStyles.axaml` (include `Styles/Frame/*.axaml`)
- Create: `src/Raffica.Shell/Frame/AppFrame.cs`, `StatusBar.cs`, `src/Raffica.Shell/Styles/Frame/AppFrame.axaml`, `StatusBar.axaml`, `Window.axaml`
- Test: `tests/Raffica.Shell.Tests/Frame/AppFrameLayoutTests.cs`, `StatusBarTests.cs`

**Interfaces:**
- Produces in `Metrics.axaml` (`x:Double`): `Raffica.Size.TitleBar` 36, `TabRow` 32, `Ribbon` 82, `RibbonCompact` 40, `StatusBar` 30, `PanelHeader` 38, `SearchField` 28, `CaptionButton` 46, `DocumentTab` 28, `DocumentTabMax` 240, `TileLarge` 54, `TileLargeMin` 48, `FieldCode` 18, `StatusCell` 20, `ZoomButton` 24, `ZoomSlider` 96, `StatusCellGap` 16.
- Produces: `AppFrame : TemplatedControl` with content slots `TitleBar` (Control), `TabRow` (Control), `Band` (Control), `LeftPane`, `RightPane`, `Document` (Control), `Notification` (Control, collapsed when null), `StatusBar` (Control, collapsed when null), and `bool IsCompact` (styled; sets the band row to 40). Rows: 36, 32, band, `*`, notification, 30.
- Produces: `StatusBar : TemplatedControl` with styled string properties `SaveState`, `Position`, `Count`, `FocusPane`, `Hint`, `Language`, `ZoomText`, `double Zoom` and `event EventHandler<double> ZoomChanged`. Cells sit left to right as spec 4.4 says; `FocusPane`, `Hint` and `Language` are empty and collapsed when null; the hint shares the focus cell's slot (they never coexist). Cells have a fixed minimum width, `TextTrimming=CharacterEllipsis` and the `tnum` class.
- Produces: `Window.axaml` style for `Window` that sets `TextElement.Foreground` to `Raffica.Brush.Text.Primary` and the UI font (the 1.1 decision note).

- [ ] **Step 1: Write the failing tests.**
  - `MetricsTests` extended: every new key above exists with exactly the value of the Global Constraints.
  - `AppFrameLayoutTests.RowsHaveTheSpecHeights` (slots filled with fixed content: title bar 36, tab row 32, band 82, status 30; `IsCompact` → band 40), `NotificationAndStatusCollapseWhenNull`.
  - `StatusBarTests.CellsChangeInPlace` (set `Position`, `Count`, `FocusPane` to texts of very different length; the `Bounds.X` of the zoom cluster and of the other cells stays equal), `FocusAndHintCellsAreCollapsedAtRest`, `ZoomChangedRaisesWhenTheSliderMoves`, `NumbersUseTabularFigures` (cell text has `tnum`; tests the 1.1 note), `CellHeightIs20`.
- [ ] **Step 2: Run** the `*Frame*` and `*Tokens*` namespaces. Expected: FAIL.
- [ ] **Step 3: Implement** metrics, `AppFrame`, `StatusBar` and their styles (tokens only; 1 px `Rule` on top of the status bar and under the title bar; `Surface.Band` for band, panels and status bar, `Surface.Page` for the title bar).
- [ ] **Step 4: Run** the namespaces again plus `dotnet test … --filter-namespace "*Tokens*"`. Expected: PASS (including `NoHexInComponentStylesTests`).
- [ ] **Step 5: Commit** `feat(shell): add the frame metrics, AppFrame and StatusBar`.

### Task 3: Ribbon: tab row, band, groups, tiles and compact mode

**Files:**
- Create: `src/Raffica.Shell/Frame/RibbonView.cs`, `RibbonButton.cs`, `src/Raffica.Shell/Styles/Frame/Ribbon.axaml`
- Modify: `src/Raffica.Shell/Resources/Strings.en.resx` and `Strings.de.resx` (`RibbonMore`, `RibbonGroupMore`)
- Test: `tests/Raffica.Shell.Tests/Frame/RibbonViewTests.cs`, `RibbonCompactTests.cs`; fixture `tests/Raffica.Shell.Tests/Frame/SampleRibbon.cs` (a Write-like ribbon of about 40 commands, large tiles, a menu command and a destructive group)

**Interfaces:**
- Consumes: Task 1 records; Task 2 sizes.
- Produces: `RibbonView : TemplatedControl` with `RibbonDefinition? Definition`, `bool IsCompact`, `RibbonTabDefinition? ActiveTab`, `string? FieldCodeTabLetter`/`ShowFieldCodes` (used in Task 6), `event Action<CommandDefinition> CommandInvoked`, and two parts for `AppFrame`: `TabRow` and `Band`. The tab row shows the tab names (active tab in ink with 1 px ink border on three sides, 33 px high, weight 600, 1 px ink rule under the row); the band shows the groups of the active tab.
- Produces: `RibbonButton : Button` created from a `CommandDefinition` (`Command` property), a large tile (54 px, 20 px icon above a label of up to two lines, min 48 wide) or a small button (26 px, 16 px icon, optional label); accessible name from `Name`; tooltip from `TooltipContent` with name, shortcut, description; a menu command opens a menu of its items.
- Normal band: groups side by side, the large command first, 8 px padding each side plus 8 px gap, group label (12 px, `Text.Secondary`, sentence case) under each group; a 1 px `Rule` only before a `Destructive` group.
- Compact band (40 px, one row): labels hidden (kept as automation names and tooltips), large tiles become small buttons, `IconOnly` or labelled secondary buttons become icon-only, every group ends in a "▾" button (`Raffica.Icon.ChevronDown`) opening the commands with `CompactPlacement.Overflow`; a group without overflow commands has no "▾".

- [ ] **Step 1: Write the failing tests** (`[AvaloniaFact]`, Light and Dark where colours matter):
  - `RibbonViewTests`: `ActiveTabIsShownInInkWithItsRule`, `SwitchingTabsShowsItsGroups`, `LargeTileIs54HighAndAtLeast48Wide`, `SmallButtonIs26High`, `OnlyTheDestructiveGroupHasARule`, `EveryCommandHasAnAutomationName`, `GroupLabelsAreSentenceCase12pxSecondary`, `MenuCommandOpensItsItems`, `CommandInvokedRaisesWithTheCommand`.
  - `RibbonCompactTests`: `BandIs40High`, `LabelsAreHiddenButKeptAsNames`, `LargeTilesBecomeSmallButtons`, `EveryCommandIsReachableInCompactMode` (for the sample ribbon: each command is either a visible button or an item in its group's overflow popup), `GroupWithoutOverflowHasNoDropdownButton`, `NoFrequentCommandIsOnlyInTheOverflow` (the sample marks its frequent commands `Inline`; the test asserts all `Inline` ones are visible buttons).
- [ ] **Step 2: Run** `--filter-namespace "*Frame*"`. Expected: FAIL.
- [ ] **Step 3: Implement** `RibbonView` and `RibbonButton` with the styles; build the controls in code from the definition (no reflection); when `IsCompact` changes, rebuild the band.
- [ ] **Step 4: Run** the namespace, then build and format. Expected: PASS, 0 warnings.
- [ ] **Step 5: Commit** `feat(shell): add the ribbon with compact mode`.

### Task 4: Title bar, document tabs and search

**Files:**
- Create: `src/Raffica.Shell/Frame/TitleBar.cs`, `DocumentTabs.cs`, `DocumentTab.cs`, `SearchField.cs`, `CommandMatcher.cs`, `src/Raffica.Shell/Styles/Frame/TitleBar.axaml`, `DocumentTabs.axaml`, `SearchField.axaml`
- Modify: `src/Raffica.Shell/Resources/Strings.en.resx`, `Strings.de.resx` (`SearchTooltip` with the shortcut as a separate key `SearchShortcut`, `NewDocument`, `CloseDocument`, `Minimize`, `Maximize`, `Restore`, `SearchNoResults`)
- Test: `tests/Raffica.Shell.Tests/Frame/CommandMatcherTests.cs`, `DocumentTabsTests.cs`, `TitleBarTests.cs`, `SearchFieldTests.cs`

**Interfaces:**
- Consumes: `CommandDefinition`, `RibbonDefinition` (Task 1), `TooltipContent` and `StrokeIcon` (1.1).
- Produces: `static class CommandMatcher { static IReadOnlyList<CommandDefinition> Find(RibbonDefinition ribbon, string query, CultureInfo culture) }`: every word of the query must start a word of the name (case-insensitive, culture-aware), commands in menus included, ordered by tab then position; an empty query gives an empty list.
- Produces: `DocumentTab(string Title, string? Path = null)` model; `DocumentTabs : TemplatedControl` with `ObservableCollection<DocumentTab> Items`, `DocumentTab? Selected`, `event Action<DocumentTab> CloseRequested`, `event Action NewRequested`. Tabs: 28 high, max 240 wide, title truncated in the middle (custom `MiddleTrimTextBlock` or a helper `MiddleEllipsis.Fit`), close button 24 × 24 (`Raffica.Icon.X`, automation name `CloseDocument`), the selected one with a 3 px ink top edge on `Surface.Band` and a 1 px `Rule` border; the "+" button always visible; the strip scrolls when the tabs do not fit (minimum tab width 96).
- Produces: `SearchField : TemplatedControl` (28 high, magnifier, placeholder, flex 0 1 300 / min 200, no keycap), `RibbonDefinition? Source`, `event Action<CommandDefinition> CommandChosen`, a results popup of rows 32 high (name left, shortcut right) with Up/Down/Enter/Esc, and `void FocusAndSelect()`.
- Produces: `TitleBar : TemplatedControl` with `object? Mark` (app mark), `DocumentTabs Tabs`, `SearchField Search`, caption buttons (46 × 36: minimize, maximize/restore, close; close hover is a neutral hover, never red) on Windows and Linux, and `bool ShowCaptionButtons`. Dragging and the caption hit-testing use `WindowDecorationProperties.ElementRole`; the title bar sets `Window.WindowDecorations` to the borderless-with-resize mode. macOS: no caption buttons, 80 px left inset for the system buttons. Confirm member names against `~/.nuget/packages/avalonia/12.1.4/lib/net10.0/Avalonia.Controls.xml` (`WindowDecorations`, `WindowDecorationProperties`, `WindowDecorationsElementRole`) before writing code; if the mode names differ, keep the behaviour and record the names used in the decisions entry.
- Produces: window-level key binding `Ctrl+K` (Meta+K on macOS, via the platform's hotkey configuration) that calls `SearchField.FocusAndSelect()`, installed by `AppFrame` through `static void FrameKeys.Attach(Window window, SearchField search)`.

- [ ] **Step 1: Write the failing tests.**
  - `CommandMatcherTests`: `FindsByWordPrefixIgnoringCase`, `FindsGermanNamesWithUmlauts` (`"ausr"` finds "Ausrichtung"), `FindsCommandsInsideMenus`, `EmptyQueryGivesNothing`, `NoMatchGivesNothing`.
  - `DocumentTabsTests`: `SelectedTabHasThe3pxInkTopEdge`, `CloseButtonIs24By24AndNamed`, `LongTitleIsTrimmedInTheMiddleAndTabIsAtMost240`, `PlusIsAlwaysVisible`, **`ThirtyTabsKeepPlusSearchAndCaptionButtonsInPlace`** (30 tabs with long names in a 1440 px title bar: the "+" button, the search field (≥ 200 wide) and the three caption buttons stay inside the window with their sizes), `CloseRaisesCloseRequested`, `TabsAreNotRound`.
  - `SearchFieldTests`: `Is28HighWithPlaceholder`, `TypingShowsMatchingCommandsAndEnterChoosesTheFirst`, `EscClosesTheResultsAndKeepsTheText`, `CtrlKFocusesTheField` (window key press in headless), `TooltipNamesTheShortcut`, `NoResultsShowsTheNoResultsLine`.
  - `TitleBarTests`: `CaptionButtonsAre46By36`, `CloseButtonHoverIsNotRed`, `ButtonsAreHiddenWhenShowCaptionButtonsIsFalse`, `TitleBarIs36High`.
- [ ] **Step 2: Run** `--filter-namespace "*Frame*"`. Expected: FAIL.
- [ ] **Step 3: Implement** the controls, strings (English and German; `SearchTooltip` renders "Search (Ctrl+K)" / "Suchen (Strg+K)" through the key-name helper of Task 8, so use a plain formatted string until then and switch in Task 8).
- [ ] **Step 4: Run** the namespace, build and format. Expected: PASS.
- [ ] **Step 5: Commit** `feat(shell): add the title bar, document tabs and search`.

### Task 5: Panes, F6 pane focus and the status bar's focus cell

**Files:**
- Create: `src/Raffica.Shell/Frame/Pane.cs`, `PaneRing.cs`, `PaneFocus.cs`, `src/Raffica.Shell/Styles/Frame/Pane.axaml`
- Modify: `src/Raffica.Shell/Resources/Strings.en.resx`, `Strings.de.resx` (`FocusPane` format "Focus: {0}", `PaneNavigation`, `PaneComments`, `PaneFormat`, `PaneDocument`)
- Test: `tests/Raffica.Shell.Tests/Frame/PaneRingTests.cs`, `PaneFocusTests.cs`, `PaneTests.cs`

**Interfaces:**
- Produces: `Pane : ContentControl` (`string Title`, `bool IsOpen` (collapsed when false), `bool IsPaneFocused`, `bool Focusable`), header 38 high with a 12 px title (`Text.Secondary`, `Text.Primary` when the pane is focused), 12 px padding, 1 px ink inset border when focused, `Surface.Band` unless it is the document pane.
- Produces: `interface IPaneItem { string Name { get; } bool IsAvailable { get; } }` (implemented by `Pane`) and `static class PaneRing` (no Avalonia) with `static int? Next(IReadOnlyList<IPaneItem> panes, int? current)` and `static int? Previous(IReadOnlyList<IPaneItem> panes, int? current)`; unavailable (closed or hidden) panes are skipped; with none available both return null; `current == null` starts at the first (Next) or last (Previous) available pane.
- Produces: `PaneFocus` (instance per window): `PaneFocus(Window window, IReadOnlyList<Pane> panes, StatusBar status)`, `bool IsNavigating`, `bool Move(bool backwards)` (focuses the first focusable descendant of the next pane, sets `IsPaneFocused`, sets `status.FocusPane` to the formatted name), `void End()`. Wired to F6 and Shift+F6 on the window (tunnel), ended by a pointer press anywhere or Esc. While a `DialogHost` of the window is open `Move` returns false and F6 is consumed.

- [ ] **Step 1: Write the failing tests.**
  - `PaneRingTests`: `NextCyclesThroughThePanes`, `PreviousCyclesBackwards`, **`ClosedPanesAreSkipped`**, `NoAvailablePaneGivesNull`, `StartsAtTheFirstAvailablePaneWhenNothingIsFocused`.
  - `PaneFocusTests` (`[AvaloniaFact]`): `F6MovesNavigationDocumentComments` (keyboard focus lands inside the right pane each time), `ShiftF6GoesBackwards`, `FocusedPaneHasTheInkBorderAndPrimaryTitle`, `StatusShowsFocusOnlyWhileNavigating` (German: "Fokus: Kommentare"), `PointerPressEndsNavigation`, `EscEndsNavigation`, `ClosingTheCommentsPaneWhileFocusedMovesToTheDocument`, **`F6DoesNothingWhileADialogIsOpen`**.
  - `PaneTests`: `HeaderIs38HighAndPaddingIs12`, `ClosedPaneTakesNoSpace`.
- [ ] **Step 2: Run** `--filter-namespace "*Frame*"`. Expected: FAIL.
- [ ] **Step 3: Implement** `Pane`, `PaneRing`, `PaneFocus` and the style.
- [ ] **Step 4: Run** the namespace, build and format. Expected: PASS.
- [ ] **Step 5: Commit** `feat(shell): add panes and F6 pane focus`.

### Task 6: Field codes in the UI

**Files:**
- Create: `src/Raffica.Shell/Frame/FieldCodeInput.cs`, `FieldCodeBadge.cs`, `src/Raffica.Shell/Styles/Frame/FieldCode.axaml`
- Modify: `RibbonView.cs` (badges), `StatusBar` hint wiring, `src/Raffica.Shell/Resources/Strings.en.resx`, `Strings.de.resx` (`FieldCodeHint` "Type a field code · Esc goes back" / German)
- Test: `tests/Raffica.Shell.Tests/Frame/FieldCodeInputTests.cs`, `FieldCodeBadgeTests.cs`

**Interfaces:**
- Consumes: `FieldCodeSession` (Task 1), `RibbonView` (Task 3), `StatusBar.Hint` (Task 2), `Window` key events.
- Produces: `static class FieldCodeInput { static IDisposable Attach(Window window, FieldCodeSession session, RibbonView ribbon, StatusBar status) }`. It feeds `KeyDown`/`KeyUp`/`TextInput` of `LeftAlt` and `RightAlt`, `F10`, `Esc` and typed characters into the session, marks consumed events `Handled` (both Alt events, so the Windows system menu does not open), counts a `TextInput` between Alt down and up as another key (Linux AltGr), and calls `session.Cancel()` on `PointerPressed`, window `Deactivated`, loss of keyboard focus out of the window, tab change by pointer, or a `DialogHost` opening. On each `Changed` it sets `ribbon.ShowFieldCodes` and the active tab letters, and `status.Hint`.
- Produces: `FieldCodeBadge : TemplatedControl` (`Text`, 18 high, min 18 wide, padding `0 4`, 3 px radius, ink fill, `Text.OnInk`, Barlow Semi Condensed 600 12 px) placed by `RibbonView` at each control's bottom right (right −4, bottom −6) while a code is shown; the tab letters appear on the tab row, group digits next to the group label, the search field shows `Q`, panes show their number (6, 7) only while the mode is on; at rest none exist (hidden, not in the layout).

- [ ] **Step 1: Write the failing tests** (`[AvaloniaFact]`, sample ribbon, headless key presses via `Avalonia.Headless.HeadlessWindowExtensions.KeyPress/KeyRelease/KeyTextInput`):
  - `FieldCodeInputTests`: `BareAltShowsTabLettersAndTheHint`, `TabLetterThenCodeRunsTheCommand`, `AltPlusAnotherKeyIsAnOrdinaryShortcut`, **`AltGrTypesTheCharacter`** (Ctrl+Alt with `@` text input: a focused `TextBox` receives `@`, the mode stays off), `EscStepsBackAndLeavesTheMode`, `F10Works`, **`ClickDeactivationAndDialogEndTheMode`** (each of pointer press, `window.Deactivated`, opening a dialog ends the mode and removes the badges), `AltKeyEventsAreHandled`, `DetachRestoresNormalKeys`.
  - `FieldCodeBadgeTests`: `BadgeIs18HighWithInkFillAndOnInkText` (Light and Dark, all four inks), `BadgesAreAtTheBottomRightOfTheirControl`, `NoBadgesAtRest`, `SameCodesInGermanAndEnglish` (the badge texts are identical for `--lang de` and `en`), `HintIsLocalized`.
- [ ] **Step 2: Run** `--filter-namespace "*Frame*"`. Expected: FAIL.
- [ ] **Step 3: Implement** the input adapter, the badge and the ribbon wiring.
- [ ] **Step 4: Run** the namespace, build and format. Expected: PASS.
- [ ] **Step 5: Manual check, recorded as open when not done:** on the Windows machine, `dotnet run --project src/Raffica.Gallery -- --view frame` (Task 9), press and release Alt: the field codes appear and no system menu opens; type `@` with AltGr in the search field on a German layout. Expected as stated.
- [ ] **Step 6: Commit** `feat(shell): add Alt field codes to the frame`.

### Task 7: Start view, settings and the settings dialog

**Files:**
- Create: `src/Raffica.Shell/Frame/StartView.cs`, `StartTile.cs`, `RecentFile.cs`, `ShellSettings.cs`, `SettingsStore.cs`, `SettingsDialog.cs`, `src/Raffica.Shell/Styles/Frame/StartView.axaml`, `SettingsDialog.axaml`
- Modify: `src/Raffica.Shell/Resources/Strings.en.resx`, `Strings.de.resx` (start view headings and buttons, settings labels and the restart note)
- Test: `tests/Raffica.Shell.Tests/Frame/StartViewTests.cs`, `SettingsStoreTests.cs`, `SettingsDialogTests.cs`

**Interfaces:**
- Consumes: `Dialog`, `DialogHost`, `SegmentedControl`, `SelectBox`, `CheckBox` (1.1), `Languages.Resolve` (existing), `ThemeMode`, `AppInk`.
- Produces: `enum StartApp { Write, Sheets, Slides, Pdf }`; `sealed record StartTile(StartApp App, string Label)`; `sealed record RecentFile(string Name, string Folder, DateTimeOffset Modified)` with `string DateText(CultureInfo ui)` (short date; de-DE gives `03.11.2026`).
- Produces: `StartView : TemplatedControl` with `IReadOnlyList<StartTile> Tiles`, `IReadOnlyList<RecentFile> Recent`, `StartApp OwnApp`, and events `NewRequested(StartApp)`, `OpenRequested(RecentFile)`, `BrowseRequested`. Headings "New", "Open", "Recent"; tiles 150 high, two per row in a 336 px column, 24 px column gap, the app mark (`type.mark` 11 px) in the tile's corner; file rows 40 high with name, folder and date; an empty recent list shows one calm line. No status bar under it (the host collapses `AppFrame.StatusBar`).
- Produces: `sealed record ShellSettings(ThemeMode Theme = ThemeMode.System, string? Language = null, bool CompactCommandBar = false, bool Autosave = true)`; `sealed class SettingsStore(string path)` with `ShellSettings Load()` (missing, empty, unreadable or corrupt file: defaults, no exception), `void Save(ShellSettings)` (writes a temp file and moves it over; an IO error is swallowed and returned as `false`: `bool TrySave(ShellSettings)`), `static string DefaultPath(string appName)` (application-data folder, `Raffica/<appName>/settings.json`); JSON by a source-generated `ShellSettingsJsonContext`.
- Produces: `static class SettingsDialog { static Dialog Create(ShellSettings current, Action<ShellSettings> onOk, Action onCancel) }`: 520 px dialog, theme as `SegmentedControl` (Light, Dark, Same as system), language as `SelectBox` (Deutsch, English) with the note "Takes effect after a restart" shown whenever the choice differs from the running language, "Compact command bar" and "Autosave" checkboxes, footer Cancel and the primary OK. The dialog changes nothing live; OK calls `onOk` with the new record.

- [ ] **Step 1: Write the failing tests.**
  - `StartViewTests`: `ShowsFourTilesAndTheOwnAppOpensInPlace` (clicking the own tile raises `NewRequested(OwnApp)`; another raises it with that app), `BrowseRaisesBrowseRequested`, `RecentRowsAre40HighWithLocalizedDates` (German `03.11.2026`, English per culture), `EmptyRecentShowsOneLine`, `TilesAreTwoPerRowIn336Px`, `EveryTileAndRowHasAnAutomationName`.
  - `SettingsStoreTests`: `RoundTripKeepsEverySetting`, **`CorruptOrMissingFileGivesDefaults`** (missing, empty, `{`, wrong types, a 1 MB file of garbage), `SaveWritesAtomically` (no `.tmp` left, the old file survives a failing move), `UnwritablePathReturnsFalseFromTrySave`, `AutosaveIsOnByDefault`, `FirstStartHasNoLanguage`.
  - `SettingsDialogTests`: `ShowsThemeLanguageCompactAndAutosave`, `OkPassesTheNewRecord`, `CancelAndEscKeepTheOldSettings`, **`LanguageChangeIsOnlyStoredNotApplied`** (after OK the `ShellStrings.Culture` is unchanged and the restart note was shown), `PrimaryButtonIsTheDarkNeutralFillNotInk`, `RestoresFocusToTheOpener`.
- [ ] **Step 2: Run** `--filter-namespace "*Frame*"`. Expected: FAIL.
- [ ] **Step 3: Implement** the types, the JSON context (`[JsonSerializable(typeof(ShellSettings))]`, enum as string) and the dialog.
- [ ] **Step 4: Run** the namespace, `TranslationTests`, build (AOT warnings from the JSON context count) and format. Expected: PASS, 0 warnings.
- [ ] **Step 5: Commit** `feat(shell): add the start view, settings and the settings dialog`.

### Task 8: German details left over from 1.1

**Files:**
- Create: `src/Raffica.Shell/Localization/KeyNames.cs`
- Modify: `src/Raffica.Shell/Styles/Controls/Menu.axaml`, `ContextMenu.axaml`, `TextBox.axaml`, `ToolTip.axaml` content; `TooltipContent.cs`; `src/Raffica.Shell/Resources/Strings.en.resx`, `Strings.de.resx` (`KeyCtrl`, `KeyShift`, `KeyAlt`, `KeyCmd`, `KeyOption`, `KeyEsc`, `KeyDelete`, `KeyEnter`, `ContextCut`, `ContextCopy`, `ContextPaste`, `ContextSelectAll`, `ContextUndo`, `ContextRedo`)
- Test: `tests/Raffica.Shell.Tests/Localization/KeyNamesTests.cs`, `Controls/TextBoxContextMenuTests.cs`; extend `MenuTests`, `TooltipTests`

**Interfaces:**
- Produces: `static class KeyNames { static string Format(KeyGesture gesture, CultureInfo ui); static string Format(string shortcut, CultureInfo ui) }`. German: `Ctrl` → `Strg`, `Shift` → `Umschalt`, `Delete` → `Entf`, `Esc` → `Esc`, `Enter` → `Eingabe`; on macOS `Meta` → `⌘`, `Alt` → `⌥` in both languages. English keeps `Ctrl`, `Shift`.
- Produces: the shortcut text of menu items, context menus and tooltips goes through `KeyNames.Format` with the UI language; the TextBox gets a `ContextFlyout` of the shell menu theme with Cut, Copy, Paste and Select all (and Undo, Redo), texts from `ShellStrings`, each with its shortcut.

- [ ] **Step 1: Write the failing tests.**
  - `KeyNamesTests`: `GermanNamesCtrlAsStrg` (`Ctrl+K` → `Strg+K`), `GermanShift` (`Umschalt+F6`), `EnglishStaysCtrl`, `MacUsesSymbols`, `UnknownKeyIsPassedThrough`.
  - `TextBoxContextMenuTests`: `GermanMenuReadsAusschneidenKopierenEinfuegen` (items "Ausschneiden", "Kopieren", "Einfügen", "Alles auswählen"), `EnglishMenuReadsCutCopyPaste`, `ShortcutsAreLocalized` ("Strg+X"), `DisabledWhenThereIsNothingToCopy`.
  - Extended: `MenuTests.ShortcutColumnIsLocalized` (German `Strg+S`), `TooltipTests.ShortcutIsLocalized`.
- [ ] **Step 2: Run** `--filter-namespace "*Localization*"` and the control tests. Expected: FAIL.
- [ ] **Step 3: Implement** `KeyNames`, route the existing menu and tooltip shortcut text through it, add the TextBox flyout. Switch Task 4's search tooltip to `KeyNames`.
- [ ] **Step 4: Run** the whole shell test project, build and format. Expected: PASS; `TranslationTests` green with the new keys.
- [ ] **Step 5: Commit** `feat(shell): localize key names and the text box context menu`.

### Task 9: Frame view in the gallery and the measurement switches

**Files:**
- Create: `src/Raffica.Gallery/FrameDemo/FrameDemoWindow.cs`, `DemoRibbon.cs`, `DemoDocument.cs`, `src/Raffica.Gallery/Measurement.cs`
- Modify: `src/Raffica.Gallery/GalleryOptions.cs`, `App.axaml.cs`, `Program.cs`, `Resources/Strings.en.resx`, `Strings.de.resx`, `Raffica.Gallery.csproj` (none expected), `tests/Raffica.Shell.Tests/GalleryOptionsTests.cs`
- Test: `tests/Raffica.Shell.Tests/Gallery/FrameDemoTests.cs`, `MeasurementTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 1–8.
- Produces: new switches on `GalleryOptions`: `--view gallery|frame` (default `gallery`, property `GalleryView View`), `--measure-startup`, `--measure-input`, `--measure-memory` (bool properties; only valid with `--view frame`, else `ArgumentException`; `--measure-input` needs the frame view), `--settings-file <path>` (property `string? SettingsFile`; measurement runs never read the user's settings), `--compact` (starts with the compact command bar, for screenshots and the layout test).
- Produces: `FrameDemoWindow : Window` — an `AppFrame` with the drawn title bar (mark, three document tabs, search over `DemoRibbon`), a `RibbonView` with the sample Write ribbon (tabs Home, Insert, Review, View; the File tab shows the start view), a navigation pane (outline list), a document pane (a page with a Carlito paragraph and a `TextBox` as `InputTarget`), a comments pane, a notification bar, the status bar, `PaneFocus`, `FieldCodeInput`, the settings dialog from a title-bar menu entry, and the start view when no document tab is open. Start tiles, "Browse…" and recent rows show their request in the notification bar ("Requested: new Sheets document").
- Produces: `Measurement` (copied in behaviour from the probe's `Measurement`; same line formats): `first-frame` in the second animation frame after `Opened`, `memory-bytes <n>` 2 s later (private bytes on Windows, working set elsewhere), `input median-ms=… p99-ms=… max-ms=…` for 100 synthetic key presses into `InputTarget`, exit code 3 after 30 s; invariant culture.

- [ ] **Step 1: Write the failing tests.**
  - `GalleryOptionsTests` extended: `--view frame`, `--view nonsense` throws, `--measure-startup` without the frame view throws, `--compact`, `--settings-file x.json`.
  - `FrameDemoTests` (`[AvaloniaFact]`): `OpensWithEveryFramePartPresent` (title bar 36, tab row 32, band 82, status bar 30), `CompactOptionGivesA40pxBand`, `ClosingTheLastTabShowsTheStartView`, `StartTileRecordsItsRequest`, `SettingsOkAppliesThemeNowAndStoresTheLanguageForTheNextStart` (temp settings file), `NoUserSettingsAreReadWithSettingsFile`, `StartUsesTheStoredLanguageUnlessLangIsGiven` (language order: `--lang`, then the stored setting, then the OS UI language via `Languages.Resolve`), `EveryDemoCommandHasANameAndAUniqueCodePerTab` (`Validate`), `AutomationNamesExistOnEveryControlOfTheFrame` (extends the 1.1 check to the frame view).
  - `MeasurementTests`: `InputLineFormatMatchesTheScript` (`input median-ms=1.00 p99-ms=2.00 max-ms=3.00`), `OptionsRejectInputWithoutFrameView`.
- [ ] **Step 2: Run** `--filter-namespace "*Gallery*"`. Expected: FAIL.
- [ ] **Step 3: Implement** options, the demo window and the measurement; `App.OnFrameworkInitializationCompleted` opens `FrameDemoWindow` for `--view frame` and keeps the `--screenshot` path for it.
- [ ] **Step 4: Run** the shell tests; then look at the frame: `dotnet run --project src/Raffica.Gallery -- --view frame --theme light --lang de --screenshot artifacts/frame/light-de.png`, repeat for `--theme dark --lang en`, `--compact`, `--ink pdf`, `--high-contrast`, and open the PNGs against the boards (`Docs/design/direction-d/Main.dc.html`, `Start.dc.html`). Expected: proportions and states as the boards; note differences as findings.
- [ ] **Step 5: Run** build and format. Expected: green.
- [ ] **Step 6: Commit** `feat(gallery): show the app frame and add the measurement switches`.

### Task 10: German layout test

**Files:**
- Create: `tests/Raffica.Shell.Tests/Frame/LabelFit.cs`
- Test: `tests/Raffica.Shell.Tests/Frame/GermanLayoutTests.cs`, `LabelFitTests.cs`

**Interfaces:**
- Consumes: `FrameDemoWindow` (Task 9), `ShellStrings.Culture`/`GalleryStrings.Culture`.
- Produces: `static class LabelFit { static IReadOnlyList<string> FindClipped(Window window) }` — for every `TextBlock` below the title bar, tab row and band (and the status bar), with a non-empty `Text`: the text laid out at its font and size (a `TextLayout` with the block's available width and `TextWrapping`) must fit in the block's `Bounds` (width, and height when it wraps), and the block's rectangle must lie inside the rectangle of the window; the result lists each offender as `"<Text> (<where>)"`.

- [ ] **Step 1: Write the failing tests.**
  - `LabelFitTests.TheCheckFindsALabelThatDoesNotFit` (a 40 px wide `TextBlock` with a long German word → reported), `TheCheckAcceptsALabelThatFits`, `TheCheckFindsALabelOutsideTheWindow`.
  - `GermanLayoutTests` (`[AvaloniaTheory]`): `NoRibbonLabelIsClippedInGerman` with `(width 1440, compact false)` and `(width 1024, compact true)`, for every tab of the sample ribbon, in Light and Dark; the demo's German names are real German and at least as long as the longest the spec shows ("Endgültig anwenden", "Kompakte Befehlsleiste"); `NoDocumentTabOrStatusCellIsClippedInGerman` (same widths); `EnglishFitsToo`.
- [ ] **Step 2: Run** `--filter-class "*GermanLayoutTests*"`. Expected: FAIL (helper missing), then after Step 3 either PASS or a list of clipped labels.
- [ ] **Step 3: Implement** `LabelFit`; fix every reported label by shortening the German text, widening the tile or wrapping to two lines, never by turning the check off; a label that truly cannot fit at 1024 px compact is a finding for the owner.
- [ ] **Step 4: Prove the test can fail:** temporarily lengthen one German demo label, run, see the test fail with that label named, revert. Record this in the roadmap evidence.
- [ ] **Step 5: Run** the whole shell test project, build and format. Expected: green.
- [ ] **Step 6: Commit** `test(shell): add the German layout test for the frame`.

### Task 11: Size check in CI and gallery measurement

**Files:**
- Modify: `bench/shell-probe/measure.cs` (`--target probe|gallery` on `run` and `size`; new `check` subcommand; `selftest` cases), `.github/workflows/ci.yml` (size step), `.github/workflows/measure.yml` (input `target`, gallery publish and cases, `check` step and job summary), `bench/shell-probe/README.md`, `bench/shell-probe/RUNNERS.md` if it names the probe only
- Test: `dotnet run bench/shell-probe/measure.cs -- selftest`

**Interfaces:**
- Produces: `measure.cs run … --target gallery` starts the exe with `--view frame` plus the case switch (no `--mode`; CSV column `mode` is `gallery`); `measure.cs size --publish-dir out/gallery --fonts-dir src/Raffica.Shell/Assets/Fonts --target gallery --out <csv>`.
- Produces: `measure.cs check --platform windows|linux|macos --in "<csv glob>" [--summary <md>]`. It compares the medians of the gallery CSVs with the limits of the decisions entry "Speed targets of the frame (M1)" and prints a table (case, measured, budget, limit, verdict); exit 1 when any value is above its limit or a required CSV is missing. Limits (budget × 1.25): Windows cold 537.5 ms, warm 525 ms, input median 12.5 ms, memory 156.25 MiB, size 48.75 MiB, zip 20 MiB; Linux cold 250 ms, warm 262.5 ms, input median 25 ms, memory 156.25 MiB, size 43.75 MiB, zip 20 MiB; macOS: no limits, verdict `indicative`.
- Produces: `measure.cs size --check <win-x64|linux-x64>` for CI: the same size and zip limits, exit 1 when exceeded, without writing a CSV.

- [ ] **Step 1: Write the failing selftest cases** in `SelfTest`: `check` passes at exactly the limit, fails 0.1 ms above it, fails on a missing CSV, reports macOS as indicative; `size --check` passes below and fails above (a generated folder of the right size).
- [ ] **Step 2: Run** `dotnet run bench/shell-probe/measure.cs -- selftest`. Expected: FAIL on the new cases.
- [ ] **Step 3: Implement** `--target`, `check`, `--check` with the limits as one table in the script (comment: source is the decisions entry; budgets are never loosened here).
- [ ] **Step 4: CI:** in `ci.yml` after "Publish the gallery (Native AOT)" add the step "Size of the published gallery" running `dotnet run bench/shell-probe/measure.cs -- size --check ${{ matrix.rid }} --publish-dir out/gallery --fonts-dir src/Raffica.Shell/Assets/Fonts`. Locally: `dotnet publish src/Raffica.Gallery -c Release -r win-x64 -o artifacts/gallery/win-x64` then the same command. Expected: a verdict with the real size. **If the gallery is over its limit this is a finding for the owner, not a new budget:** report the numbers, leave the step in, and do not mark the size part of the third acceptance criterion done.
- [ ] **Step 5: `measure.yml`:** add the input `target` (`gallery` default, `probe`); publish `src/Raffica.Gallery` or the probe accordingly (`RafficaGallery[.exe]`); cases for the gallery are cold, warm, input, memory and size (no `empty` baseline); a last step `check` writes `$GITHUB_STEP_SUMMARY`. The workflow stays `workflow_dispatch` only.
- [ ] **Step 6: Run** `dotnet run bench/shell-probe/measure.cs -- selftest`, and `dotnet format Raffica.slnx --verify-no-changes`; after the push, `gh workflow view measure.yml --ref feature/m01-2-app-frame` shows the new `target` input. Expected: green.
- [ ] **Step 7: Commit** `ci: check the size of the published gallery and measure the frame`.

### Task 12: Verification, measurement run, decisions and roadmap

**Files:**
- Modify: `Docs/DECISIONS.md`, `Docs/roadmap.json`, `Docs/ROADMAP.md` (generated), `CLAUDE.md` (gallery switches), `Docs/measurements/gallery-frame-<date>/` (CSVs and machine notes of the run)

- [ ] **Step 1: Full local verification:** `dotnet build Raffica.slnx -c Release`; `dotnet run tools/native-fetch.cs -- --rid win-x64 --out artifacts/native/win-x64`; `RAFFICA_REQUIRE_OCR=1 dotnet test Raffica.slnx -c Release --no-build --max-parallel-test-modules 1`; `dotnet format Raffica.slnx --verify-no-changes`; `dotnet run tools/roadmap.cs -- check`. Expected: all green, no skipped OCR test; record counts.
- [ ] **Step 2: Native AOT publish of the frame:** `dotnet publish src/Raffica.Gallery -c Release -r win-x64 -o artifacts/gallery/win-x64`, then `artifacts/gallery/win-x64/RafficaGallery.exe --view frame --lang de --screenshot artifacts/frame/aot-de.png` exits 0 and the PNG shows the German frame. Expected: no publish warnings.
- [ ] **Step 3: PR to `dev`** (draft off), wait for CI (`ci`, `roadmap`), including the size step.
- [ ] **Step 4: Measurement runs** (needs the owner to start the runners, `bench/shell-probe/RUNNERS.md`): `gh workflow run measure.yml --ref feature/m01-2-app-frame -f platform=windows -f target=gallery -f note="<GPU, resolution, scaling, power>"`, then `-f platform=linux`. Download with `gh run download <id> -n measure-<platform>-<id> -D Docs/measurements/gallery-frame-<date>/<platform>`. Expected: `check` passes on both. **If a value is above its limit, stop and report it to the owner; do not change a limit.** Until the owner starts the runners the third acceptance criterion stays open, with these commands.
- [ ] **Step 5: `Docs/DECISIONS.md`:** new entries (never rewrite old ones): "App frame conventions (M1.2)" (names, the author's calls above that held, the Avalonia member names used for the title bar, the `Ctrl`-held rule for AltGr, where the settings file lives); "Gallery frame baseline (M1.2)" (the medians of both machines as the regression baseline, the limit table, the check command). Add the new gallery switches (`--view`, `--measure-*`, `--settings-file`, `--compact`) to the "Shell" section of `CLAUDE.md`.
- [ ] **Step 6: Roadmap:** in `Docs/roadmap.json` set milestone 1.2 to `planned` with `"plan": "superpowers/plans/2026-11-03-milestone-01-2-app-frame.md"` when this plan is committed; `in-progress` when Task 1 starts; tick each task as it finishes; tick an acceptance criterion only with its proof in `evidence` (test names; the output of the commands; the run ids; the failing-then-passing German label check of Task 10); `done` with `completedOn` only when every criterion is ticked. Open items go in the evidence text with their command: the title bar on the ProBook (`dotnet publish src/Raffica.Gallery -c Release -r linux-x64 -o out/gallery`, then `out/gallery/RafficaGallery --view frame`), bare Alt on a real Windows machine (Task 6 step 5), the macOS title bar. Record any scope change under `scopeChanges`. Run `dotnet run tools/roadmap.cs -- generate` and commit both files.
- [ ] **Step 7: Commit** `docs: record the app frame decisions and close milestone 1.2` (only after the owner has the measurement result; otherwise commit as `docs: record the app frame decisions` and leave the milestone `in-progress`).
