# Milestone 1.1 – Tokens and base controls: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A shared shell project (`Raffica.Shell`) holds the Direction D tokens (light, dark, Windows high contrast), the embedded fonts, the icon set and twelve base controls, with English and German strings, and a gallery app shows every control in both themes and both languages.

**Architecture:** `src/Raffica.Shell` is an Avalonia 12 class library with AXAML only: one token dictionary per theme, metrics, type styles, icon geometries and one `ControlTheme` per base control, using semantic tokens only. A `ShellTheme` class applies theme mode and app ink. `StringTable` wraps the `.resx` files (both languages embedded in the main assembly). `src/Raffica.Gallery` is a Native AOT app that shows every control; `tests/Raffica.Shell.Tests` holds the contrast, token, localization, control and gallery tests.

**Tech Stack:** .NET 10 Native AOT, Avalonia 12.1.4 (central versions), xunit.v3 on Microsoft.Testing.Platform, `Avalonia.Headless.XUnit` for control tests.

**Spec:** [Docs/superpowers/specs/2026-11-03-design-system-shell-design.md](../specs/2026-11-03-design-system-shell-design.md): sections 4, 6, 7 and 10 (milestone 1.1), appendix A (tokens and measured contrast), appendix B (normalizations). The boards in `Docs/design/direction-d/` are the visual reference where the spec is silent; the spec wins where they differ.

## Global Constraints

- **Values come from the new design only.** Every colour, size, radius and type value is copied from appendix A of the spec (Direction D "Formular Neutral"). The predecessor's `Tokens.axaml`, `Density.axaml`, `HighContrast.axaml`, `Strings.cs` (tag `v1.0.0`, `git -C C:\VS-Code\Pagewing show v1.0.0:<path>`) are reference for the mechanism, never for values. Never modify the Pagewing checkout.
- **Naming in code:** the predecessor's name must not appear in any file under `src/`, `tests/`, `tools/`, `third-party/` or `.github/` (test `NoPagewingLeftoverTests` fails). Write "the predecessor" in comments.
- **Stack:** .NET 10 Native AOT, Avalonia 12.1.4, compiled bindings only (`AvaloniaUseCompiledBindingsByDefault`), no runtime-reflection XAML, no icon fonts. Do not change Avalonia versions.
- **Components use semantic tokens only.** No hex value, no `Color=`/`Background="#…"` literal inside a component style (test `NoHexInComponentStylesTests`, Task 2).
- **Tokens:** one resource dictionary per theme. Key naming: `Raffica.Color.<Group>.<Name>` (a `Color`) and `Raffica.Brush.<Group>.<Name>` (a `SolidColorBrush`, same value), where the appendix token `surface.page` becomes `Surface.Page`, `text.onInk` becomes `Text.OnInk`, `button.primary.fill` becomes `Button.Primary.Fill`. 8-digit colours are `#AARRGGBB` in Avalonia (spec writes CSS alpha): `scrim` light rgba(26,29,33,0.32) is `#521A1D21`.
- **Radius:** 0 surfaces, 2 px checkbox, 3 px controls and overlays. **No decorative shadows** (menus, tooltips, dialogs have a 1 px `border.control` edge; the dialog sits over the scrim). **Hit targets** ≥ 24 px; button, dropdown 26 px high; dialog buttons 28 px high and min 96 px wide; list and menu rows 32 px; notification bar 40 px; dialog 520 px wide, header 44, footer 52; tooltip ≤ 240 px wide.
- **Type:** Public Sans 14 px base (13 px secondary, 12 px labels), tabular figures wherever numbers align; no uppercase and no monospace in the chrome. **Focus ring:** 2 px, offset 2 px, in the active app ink (`danger` on destructive controls); visible on every control.
- **Localization:** English is the source, German ships with it. Both `.resx` files per string table are embedded in the main assembly (no satellite assemblies, which need runtime assembly loading under Native AOT). A missing key throws, it never falls back silently. Language changes apply after a restart (no live switching).
- **Accessibility:** every control has an automation name; icon-only buttons take the name of their command.
- **Not in this milestone** (spec section 3 and 10): title bar, tabs, ribbon, panels, status bar, field codes, F6, start view, settings dialog, size check and German layout test (all 1.2), scroll bars and list boxes beyond what the gallery needs from the base theme, any app's commands.
- **Done** means: `dotnet build Raffica.slnx -c Release`, the test command from `CLAUDE.md` (with `RAFFICA_REQUIRE_OCR=1`), `dotnet format Raffica.slnx --verify-no-changes` and `dotnet run tools/roadmap.cs -- check` are green.
- Commits: Conventional Commits, trailer `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`, stage files by name, `git show --stat HEAD` after each commit. PR to `dev` at the end (squash allowed once CI is green). No commit to `dev` or `main`.

## Calls made while planning (author's calls, the owner can overturn them)

1. **Base theme:** the shell layers its own `ControlTheme`s over `FluentTheme` (kept for the controls this milestone does not restyle, such as scroll bars). Every restyled control replaces Fluent's template wherever Fluent hard-codes a shadow, a radius or a colour.
2. **Toggle** means `ToggleButton` (pressed state fills with the ink). The spec's state table has no switch, so there is no `ToggleSwitch`. **Radio** has no row in the state table: it takes the checkbox states, a 16 px circle, checked = ink fill with a 6 px `text.onInk` dot.
3. **Dark scrim** (appendix B leaves its value to this milestone): `#80000000` (black at 50 %). Verified visually in the gallery (the frame stays visible); recorded in `Docs/DECISIONS.md`.
4. **Icons:** Lucide (ISC), pinned tag, 24-unit path data drawn by a `StrokeIcon` control at 16 or 20 px with a constant 1.5 px stroke. Only the icons the base controls and the gallery need; the ribbon icons belong to the app milestones.
5. **Dialog** is an in-window overlay (`DialogHost`) with the scrim, not an OS window, because the spec needs the dimmed app behind it.
6. **Language in the gallery** is chosen with `--lang` (a restart, as in the spec), not with a live switch. Theme and ink switch live.
7. **String resource names:** `Strings.en.resx` and `Strings.de.resx` per project (as in the spec section 7).
8. **Gallery tooling:** the gallery has a `--screenshot <png>` switch (renders the window once and exits), used to check every theme × language combination without a person at the screen.

## Review Focus

1. **Public Sans weights do not apply** (variable font under Skia and Native AOT falls back to synthetic bold or ignores 500/600). Expected: 400, 500 and 600 render with different advance widths. Pinned by `PublicSansWeightsTests` (Task 3); fallback is static instances.
2. **German resource lookup falls back to English under Native AOT without an error.** Expected: the published gallery with `--lang de` prints a German string. Pinned by the CI smoke step (Task 8) and `StringTableTests`.
3. **A new colour token or a changed value slips past the contrast test.** Expected: a token without a classification fails, and a pairing below its threshold fails. Pinned by `EveryTokenIsClassified`, `PairingsMeetTheirThreshold`, `TokenValuesMatchAppendixA` (Task 2).
4. **A hex literal in a control style bypasses the tokens** (works in light, breaks in dark and high contrast). Pinned by `NoHexInComponentStylesTests` (Task 2).
5. **A control that cannot be reached or understood by keyboard and screen reader** (icon-only button without name, dialog that lets Tab escape, menu item without name). Pinned by `AutomationNameTests` (Task 8) and the dialog focus-trap test (Task 7).
6. **A translation key exists in English but its German value is empty, or a German file has a key English lacks.** Pinned by `TranslationTests` (Task 4).
7. **Windows high contrast on:** controls must stay readable and focus visible. Checked in the gallery with the forced high-contrast switch (Task 8) since CI cannot toggle the OS setting; the key-parity test (Task 2) guarantees no missing token.

---

### Task 1: Projects, test infrastructure and gallery skeleton

**Files:**
- Create: `src/Raffica.Shell/Raffica.Shell.csproj`, `src/Raffica.Shell/RafficaStyles.axaml(.cs)` (empty style bundle that includes `FluentTheme`)
- Create: `src/Raffica.Gallery/Raffica.Gallery.csproj`, `Program.cs`, `App.axaml(.cs)`, `GalleryWindow.axaml(.cs)`, `GalleryOptions.cs`
- Create: `tests/Raffica.Shell.Tests/Raffica.Shell.Tests.csproj`, `HeadlessSmokeTests.cs`, `TestApp.cs`, `RepoPaths.cs`
- Modify: `Raffica.slnx` (add the three projects), `Docs/DECISIONS.md` (entry for the headless override, written in Task 8 once final)

**Interfaces:**
- Produces: `GalleryOptions.Parse(string[] args) : GalleryOptions` with `string Lang` (`en`|`de`, default from the OS UI language, German if it is German), `ThemeMode Theme` (`light|dark|system`, default `system`), `bool HighContrast`, `AppInk Ink` (default `Write`), `string? Screenshot`, `string? PrintString`. Invalid switch: throws `ArgumentException`, `Program` prints `error: …` to stderr and exits 1.
- Produces: `RepoPaths.Root` (walks up from `AppContext.BaseDirectory` to the folder with `Raffica.slnx`) and `RepoPaths.Combine(params string[] parts)` for tests that read source files.

- [ ] **Step 1: Create `Raffica.Shell.csproj`** (class library, `IsAotCompatible` true, compiled bindings by default, package references `Avalonia` and `Avalonia.Themes.Fluent`) and `Raffica.Gallery.csproj` (WinExe, `PublishAot`, `AssemblyName` `RafficaGallery`, references `Avalonia.Desktop`, the shell project, `TrimmerRootAssembly` for the shell and Fluent, `InvariantGlobalization` false, `SatelliteResourceLanguages` `en;de`). Add both to `Raffica.slnx`.
- [ ] **Step 2: Create `Raffica.Shell.Tests.csproj`** (Exe, references both projects, `Avalonia.Headless.XUnit`). `Avalonia.Headless.XUnit` 12.1.4 fails discovery with xunit.v3 4.0.x: set `<PackageReference Include="xunit.v3" VersionOverride="3.2.2" />` in this project only, with a comment explaining why. If discovery still fails, fall back to `Avalonia.Headless` plus a small session helper that runs the test body on the headless UI thread; keep the same test names.
- [ ] **Step 3: Write the failing test** `HeadlessSmokeTests.AWindowWithTheShellStylesOpensInTheHeadlessPlatform` (`[AvaloniaFact]`): `TestApp` loads `new RafficaStyles()`, a `Window` with a `Button` shows, and `window.IsVisible` is true with the button's bounds non-empty.
- [ ] **Step 4: Run** `dotnet test tests/Raffica.Shell.Tests -c Release`. Expected: FAIL (missing types), then after Step 5 PASS, proving discovery works with the override.
- [ ] **Step 5: Implement `RafficaStyles`** (a `Styles` subclass whose AXAML includes `<FluentTheme/>` only, for now) and `GalleryOptions.Parse` with its tests `GalleryOptionsTests` (`--lang de`, `--theme dark`, `--high-contrast`, `--ink pdf`, `--screenshot out.png`, `--print-string Close`; `--lang fr` and unknown switches throw).
- [ ] **Step 6: `Program.Main`:** `--print-string <key>` prints the value from the shell string table and exits 0 before any window exists (wired in Task 4); otherwise start the app with the options. `GalleryWindow` is an empty 1280 × 800 window for now.
- [ ] **Step 7: Run** build, test and format (see "Done"). Expected: 0 warnings, green.
- [ ] **Step 8: Commit** `feat(shell): add the shell, gallery and shell test projects`.

### Task 2: Token dictionaries, metrics, theme switch and the contrast test

**Files:**
- Create: `src/Raffica.Shell/Styles/Tokens.axaml` (the two theme dictionaries `Light` and `Dark`), `Tokens.HighContrast.axaml`, `Metrics.axaml`
- Create: `src/Raffica.Shell/Theming/ShellTheme.cs`, `ThemeMode.cs`, `AppInk.cs`
- Modify: `src/Raffica.Shell/RafficaStyles.axaml` (merge the dictionaries)
- Test: `tests/Raffica.Shell.Tests/Tokens/WcagContrastTests.cs`, `TokenValuesTests.cs`, `ContrastTests.cs`, `TokenParityTests.cs`, `MetricsTests.cs`, `NoHexInComponentStylesTests.cs`, `ShellThemeTests.cs`; helpers `TokenFile.cs`, `ContrastPolicy.cs`, `WcagContrast.cs`

**Interfaces:**
- Produces: `enum ThemeMode { Light, Dark, System }`, `enum AppInk { Write, Sheets, Slides, Pdf }`.
- Produces: `ShellTheme.Apply(Application app, ThemeMode mode, AppInk ink, bool? forceHighContrast = null)`. It sets `RequestedThemeVariant`, loads the high-contrast set when `forceHighContrast == true` or (null and the system asks for high contrast, Windows only), and sets the semantic keys `Raffica.Color.Ink` / `Raffica.Brush.Ink` (and the `Raffica.Brush.Focus.Ring`) from the chosen ink and the actual variant; it re-resolves them when `ActualThemeVariantChanged` fires.
- Produces: all appendix A section 1.1 and 1.2 tokens as `Raffica.Color.*` / `Raffica.Brush.*` in `Light` and `Dark`: `Surface.Page|Band|Desk`, `State.Hover`, `Selection.Tint`, `Text.Primary|Secondary|Disabled|OnInk`, `Border.Control`, `Rule`, `Grid.Line`, `Button.Primary.Fill|Hover`, `Scrim`, `Ink.Write|Sheets|Slides|Pdf`, `Review.Violet|Tint`, `Danger`, `Warning.Icon`, plus `Raffica.Shadow.Page` (light box shadows from the appendix; dark: none). `text.selection` is dropped (appendix B).
- Produces: `Metrics.axaml` with `Raffica.Space.2|4|6|8|10|12|16|24` (`x:Double`), `Raffica.Radius.Surface|Checkbox|Control|Overlay` (`CornerRadius`), `Raffica.Size.Button`, `Dropdown`, `HitTargetMin`, `RowList`, `RowFile`, `DialogWidth|DialogHeader|DialogFooter`, `NotificationBar`, `Icon.Small|Large`, `Rule.Width`, `Focus.RingWidth|RingOffset`.
- Produces for tests: `TokenFile.Load(string theme)` returning `IReadOnlyDictionary<string, Color>` read straight from `Tokens.axaml` with `System.Xml.Linq` (no Avalonia needed), `WcagContrast.Ratio(Color fg, Color bg) : double` (WCAG 2.x relative luminance), `ContrastPolicy` (below).

- [ ] **Step 1: Write `WcagContrastTests`** (pure, no Avalonia): `Ratio(white, black) == 21`, `Ratio(#1A1D21, #FFFFFF)` is 16.91 and `Ratio(#B48CF0, #3A3E45)` is 4.07 and `Ratio(#FF6F66, #3A3E45)` is 3.95 (to two decimals, the values of appendix A section 2). Run: FAIL, implement `WcagContrast`, PASS.
- [ ] **Step 2: Write `TokenValuesTests`** `TokenValuesMatchAppendixA` (a `[Theory]` over a table that copies every row of appendix A sections 1.1 and 1.2, light and dark, hex exactly as in the spec; `scrim` dark is the author's call `#80000000`). Run: FAIL (no tokens).
- [ ] **Step 3: Write `ContrastTests`** with `ContrastPolicy`, which classifies every foreground token once:
  - `Text` (4.5): `Text.Primary`, `Text.Secondary`, all four `Ink.*`, `Review.Violet`, `Danger`;
  - `Ui` (3): `Border.Control`, `Warning.Icon`;
  - `Exempt` (with a reason string): `Text.Disabled` (disabled controls);
  - `Decorative`: `Rule`, `Grid.Line`;
  - `Background`: the five surfaces `Surface.Page`, `Surface.Band`, `Surface.Desk`, `State.Hover`, `Selection.Tint`;
  - `Fill` (checked on their own pairing): `Review.Tint`, `Button.Primary.Fill`, `Button.Primary.Hover`, `Scrim`;
  - `Forbidden` pairings (appendix B, must not be used): `Review.Violet` and `Danger` on dark `Selection.Tint`; `Border.Control` on `Selection.Tint` is `Exempt` ("selection is carried by the 3 px ink edge").
  
  Tests: `EveryTokenIsClassified` (a token in `Tokens.axaml` that `ContrastPolicy` does not know fails, and so does a policy entry without a token), `PairingsMeetTheirThreshold` (every foreground × the five backgrounds, light and dark, except exempt/decorative/forbidden), `FilledPairingsMeetTheirThreshold` (appendix section 2.3: `Text.OnInk` on each ink, `Review.Violet`, `Danger`, `Button.Primary.Fill`, `Button.Primary.Hover` at 4.5; `Text.Primary` on `Review.Tint` at 4.5; `Review.Violet` on `Review.Tint` at 3), and `ForbiddenPairingsReallyFail` (the two forbidden dark pairings are below their threshold, so the list cannot go stale).
- [ ] **Step 4: Run** `dotnet test tests/Raffica.Shell.Tests -c Release --filter-namespace "*Tokens*"`. Expected: FAIL.
- [ ] **Step 5: Write `Tokens.axaml`:** `ResourceDictionary.ThemeDictionaries` with `Light` and `Dark`, each token as `Color` and `SolidColorBrush`, with the header comment pointing to the spec appendix. Read the predecessor's `Tokens.axaml` only for the structure. Run Steps 2 and 3 tests: PASS.
- [ ] **Step 6: Write `Tokens.HighContrast.axaml`** (same keys, built from system colours as the predecessor did: read its `HighContrast.axaml` and `Services/HighContrast.cs` for the mechanism) and `TokenParityTests.HighContrastHasTheSameKeysAsLightAndDark` (also `Light` and `Dark` have identical key sets). RED, then GREEN.
- [ ] **Step 7: Write `Metrics.axaml`** and `MetricsTests` with the pinned values from "Global Constraints" (button 26, dropdown 26, hit target 24, row 32, notification bar 40, dialog 520/44/52, icons 16/20, spacing scale, radii 0/2/3/3, focus ring 2/2). RED, GREEN.
- [ ] **Step 8: Write `NoHexInComponentStylesTests.NoComponentStyleContainsAColourLiteral`:** scans `src/Raffica.Shell/Styles/Controls/**/*.axaml` (and the gallery's AXAML) for `#[0-9A-Fa-f]{3,8}\b` and named-colour attribute values; plants a literal in a temp copy to prove the test fails, then passes on the real files (the folder is empty now and fills in Tasks 5 to 7).
- [ ] **Step 9: Implement `ShellTheme.Apply`** and write `ShellThemeTests` (`[AvaloniaFact]`): applying `(Dark, Sheets)` resolves `Raffica.Brush.Ink` to `#7CCBA2`; switching to `Light` re-resolves it to `#1E6B47`; `forceHighContrast: true` makes `Raffica.Brush.Surface.Page` come from the high-contrast set.
- [ ] **Step 10: Run** build, test and format. Expected: green.
- [ ] **Step 11: Commit** `feat(shell): add the Direction D tokens, metrics, theme switch and the contrast test`.

### Task 3: Fonts, type styles and icons

**Files:**
- Create: `src/Raffica.Shell/Assets/Fonts/` (Public Sans variable, Barlow Semi Condensed SemiBold, Carlito ×4, `SOURCES.md`; copy the files and their pinned hashes from `bench/shell-probe/ShellProbe/Assets/Fonts/`)
- Create: `src/Raffica.Shell/Styles/Fonts.axaml`, `Typography.axaml`, `Icons.axaml`, `src/Raffica.Shell/Controls/StrokeIcon.cs`
- Create: `third-party/public-sans/`, `third-party/barlow-semi-condensed/`, `third-party/carlito/`, `third-party/lucide/` (each `README.md` in the style of `third-party/geist/README.md` plus the licence text: OFL 1.1 for the fonts, ISC for Lucide)
- Test: `tests/Raffica.Shell.Tests/Typography/PublicSansWeightsTests.cs`, `TabularFiguresTests.cs`, `FontResourcesTests.cs`; `Icons/IconTests.cs`

**Interfaces:**
- Produces: font family resources `Raffica.Font.Ui` (Public Sans), `Raffica.Font.FieldCode` (Barlow Semi Condensed), `Raffica.Font.Document` (Carlito).
- Produces: text style classes on `TextBlock` and in control themes: `Classes="ui"` (14/400), `"ui-emphasis"` (14/600), `"ui-medium"` (14/500), `"secondary"` (13), `"label"` (12, `Text.Secondary`), `"small"` (12), `"heading-dialog"` (15/600); all with `FontFeatures="tnum"` where numbers align (status bar, counts); line heights from appendix A 4.1 and appendix B (labels 1.25, multi-line 1.4).
- Produces: `StrokeIcon : Control` with `StyledProperty<Geometry?> Data`, `StyledProperty<int> Size` (16 or 20), `StyledProperty<IBrush?> Stroke`; and `StrokeIcon.StrokeThicknessFor(int size) : double` = `1.5 * 24 / size` (the geometry is in 24-unit space). Round caps and joins, no fill.
- Produces: geometry keys `Raffica.Icon.<Name>` for Check, ChevronDown, ChevronRight, X, Search, Plus, Minus, Info, TriangleAlert, OctagonAlert, Bold, Italic, Underline, Undo, Redo, AlignLeft, AlignCenter, AlignRight (Lucide path data at a pinned tag, licence in `third-party/lucide`).

- [ ] **Step 1: Write the failing tests.**
  - `FontResourcesTests.AllThreeFamiliesResolveFromTheShellAssembly`: `FontManager` finds "Public Sans", "Barlow Semi Condensed" and "Carlito" via the `avares://Raffica.Shell/Assets/Fonts#…` URIs.
  - `PublicSansWeightsTests.Weights400500And600HaveDifferentAdvanceWidths`: a `TextLayout` of one sample string in each weight has three distinct widths.
  - `TabularFiguresTests.DigitsHaveTheSameWidthWithTabularFigures`: the widths of "1111" and "0000" are equal in `"ui"` text with `tnum`.
  - `IconTests`: every `Raffica.Icon.*` key parses to a non-empty geometry; `StrokeThicknessFor(16)` is 2.25 and `StrokeThicknessFor(20)` is 1.8; a `StrokeIcon` with `Size` other than 16 or 20 throws `ArgumentOutOfRangeException`.
- [ ] **Step 2: Run** the tests. Expected: FAIL.
- [ ] **Step 3: Copy the fonts and write the licence folders** (hash check: `sha256sum` equals `SOURCES.md`); `Fonts.axaml` and `Typography.axaml` as in the Interfaces; embed the files as `AvaloniaResource`.
- [ ] **Step 4: Implement `StrokeIcon`** (custom `Render`: push a scale of `Size / 24`, stroke with the computed thickness) and write `Icons.axaml` from Lucide.
- [ ] **Step 5: Run** the tests. Expected: PASS. If the weights test fails (variable axis not applied), generate static instances (400, 500, 600) with the fontTools instancer, record the command and source in `SOURCES.md`, and switch `Fonts.axaml` to them; the tests stay unchanged.
- [ ] **Step 6: Run** build, test and format. Expected: green; note the size added by the fonts (`SOURCES.md` or the commit body).
- [ ] **Step 7: Commit** `feat(shell): embed the fonts and add the type styles and the icon set`.

### Task 4: Localization mechanism and the missing-translation test

**Files:**
- Create: `src/Raffica.Shell/Localization/StringTable.cs`, `Languages.cs`, `ShellStrings.cs`; `src/Raffica.Shell/Resources/Strings.en.resx`, `Strings.de.resx`
- Modify: `src/Raffica.Shell/Raffica.Shell.csproj` (logical names, see Step 3); `src/Raffica.Gallery/Program.cs` (`--print-string`)
- Test: `tests/Raffica.Shell.Tests/Localization/StringTableTests.cs`, `LanguagesTests.cs`, `TranslationTests.cs`, `TranslationCheck.cs`

**Interfaces:**
- Produces: `sealed class StringTable(string baseName, Assembly assembly)` with `CultureInfo Culture { get; set; }` (German for two-letter language `de`, English otherwise), `string Get(string key)` (throws `InvalidOperationException("Missing string: <key>")` if absent) and `string Get(string key, params object[] args)` (formats with `Culture`). Resource names are `<baseName>.en` and `<baseName>.de`.
- Produces: `Languages.Resolve(string? requested, CultureInfo systemUi) : CultureInfo`: `"de"` or `"en"` if requested; null means follow the system (German if the system language is German, English otherwise); any other requested code throws `ArgumentException`. (The persisted setting is milestone 1.2.)
- Produces: `ShellStrings` (static, one property per key, backed by a `StringTable`): `Close`, `Cancel`, `Ok`, `NotificationInfo`, `NotificationWarning`, `NotificationError`, `Search`, `More`, plus the keys the dialog and notification bar need.
- Produces for tests: `TranslationCheck.FindProblems(XDocument english, XDocument german) : IReadOnlyList<string>` listing an English key missing in German, an empty German value, and a German key English lacks.

- [ ] **Step 1: Write the failing tests.**
  - `StringTableTests`: a German table returns the German value, an English one the English; an absent key throws with the key in the message; `Get("Key", args)` formats with the table's culture (`1234.5` as "1.234,5" in German).
  - `LanguagesTests`: `Resolve(null, de-AT)` is `de`, `Resolve(null, fr-FR)` is `en`, `Resolve("en", de-DE)` is `en`, `Resolve("fr", …)` throws.
  - `TranslationTests.EveryEnglishKeyHasAGermanValue`: finds every `Strings.en.resx` under `src/` (via `RepoPaths`), requires a sibling `Strings.de.resx`, and asserts `FindProblems` is empty for each pair. `TranslationTests.TheCheckFailsOnARemovedGermanString` removes one key from the in-memory German document of the real shell file and asserts `FindProblems` reports it (and an emptied value, and an extra German key).
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement** `StringTable`, `Languages`, `ShellStrings`; csproj `EmbeddedResource Update` entries with `WithCulture="false"` and `LogicalName="Raffica.Shell.Strings.en.resources"` / `….de.resources`; fill both `.resx` files (the German values are real German, not copies).
- [ ] **Step 4: Wire `--print-string <key>`** in the gallery `Program` (prints the shell string for `--lang`, exit 0; unknown key: `error: Missing string: <key>` on stderr, exit 1). Test `GalleryOptionsTests` extended.
- [ ] **Step 5: Run** build, test and format. Then publish for the check of Review Focus 2: `dotnet publish src/Raffica.Gallery -c Release -r win-x64 -o artifacts/gallery/win-x64` and `artifacts/gallery/win-x64/RafficaGallery.exe --print-string Cancel --lang de` prints `Abbrechen` (and `Cancel` with `--lang en`). Expected: both as stated.
- [ ] **Step 6: Commit** `feat(shell): add the string table with English and German and the missing-translation test`.

### Task 5: Button family, toggle, checkbox, radio, segmented control

**Files:**
- Create: `src/Raffica.Shell/Styles/Controls/Button.axaml`, `ToggleButton.axaml`, `CheckBox.axaml`, `RadioButton.axaml`, `SegmentedControl.axaml`; `src/Raffica.Shell/Controls/SegmentedControl.cs`; `src/Raffica.Shell/Styles/Focus.axaml`
- Create: `src/Raffica.Gallery/Sections/ButtonsSection.axaml(.cs)`, `StateMatrix.cs`
- Test: `tests/Raffica.Shell.Tests/Controls/ButtonTests.cs`, `SelectionControlTests.cs`, `SegmentedControlTests.cs`

**Interfaces:**
- Consumes: tokens and metrics (Task 2), type classes and `StrokeIcon` (Task 3).
- Produces: themes for `Button` (default transparent/ribbon style; `Classes="boxed"`, `"primary"`, `"destructive"`, `"icon"` for icon-only), `ToggleButton`, `CheckBox`, `RadioButton`; states exactly as the state table of appendix A section 5 (rest, hover, pressed/on, focus, disabled), 26 px high, 5 px horizontal padding on ribbon buttons and 8 px on text buttons (appendix B).
- Produces: `SegmentedControl : ListBox` (single selection, radio semantics for automation) with item container theme: rest `Surface.Page` + `Border.Control`, hover `State.Hover`, selected `Selection.Tint` + 3 px ink bottom edge + 600 weight, 3 px radius on the outer ends only.
- Produces: `StateMatrix.Force(Control control, params string[] pseudoClasses)` (gallery): sets `:pointerover`, `:pressed`, `:focus-visible` on a control through `((IPseudoClasses)control.Classes)` so the gallery can show states without a pointer.
- Produces: the shared focus ring in `Focus.axaml` (`FocusAdorner` template: 2 px, offset 2 px, ink; `danger` on controls with `Classes="destructive"`).

- [ ] **Step 1: Write the failing tests** (`[AvaloniaFact]`, window shown in headless):
  - `ButtonTests.DefaultButtonIsTransparentAt26PxAndHoverUsesTheHoverToken`, `PrimaryButtonUsesPrimaryFillAndOnInkText`, `PressedFillsWithTheInk`, `DestructiveButtonUsesDangerEdgeAndText`, `DisabledUsesDisabledText`, `ButtonHeightIs26AndHitTargetIsAtLeast24`.
  - `ButtonTests.FocusRingUsesTheInkAndDangerOnDestructive`: keyboard-focus the control, the adorner exists with thickness 2 and the ink (or danger) brush.
  - `SelectionControlTests`: toggle `IsChecked` fills with the ink and `Text.OnInk`; checkbox is 16 px with radius 2 and checked ink fill with a check icon; radio checked shows the 6 px dot; both react to Space.
  - `SegmentedControlTests`: arrow keys move the selection; exactly one item is selected; selected item has weight 600; the control exposes a selection automation pattern.
  - Each test sets `ShellTheme.Apply` for both Light and Dark (a `[Theory]`) and compares resolved brushes with the token values, not with literals.
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement** the control themes (replace Fluent's templates; tokens only), `Focus.axaml`, `SegmentedControl`. One line per control of approach is in the Interfaces; follow the structure of Fluent's templates for behaviour (pointer, keyboard, `:checked`).
- [ ] **Step 4: Gallery section `ButtonsSection`:** a row per control with its states (rest, hover, pressed/on, focus, disabled) using `StateMatrix.Force`, for all variants (default, boxed, primary, destructive, icon).
- [ ] **Step 5: Run** the tests, then `dotnet run --project src/Raffica.Gallery -- --theme dark --lang de --screenshot artifacts/gallery/buttons-dark-de.png` and open the PNG. Expected: PASS; the screenshot shows the states with the right tokens. Repeat for light English.
- [ ] **Step 6: Run** build, test (including `NoHexInComponentStylesTests`) and format. Expected: green.
- [ ] **Step 7: Commit** `feat(shell): add button, toggle, checkbox, radio and segmented control`.

### Task 6: Input, select, menu, context menu, tooltip

**Files:**
- Create: `src/Raffica.Shell/Styles/Controls/TextBox.axaml`, `ComboBox.axaml`, `Menu.axaml`, `ContextMenu.axaml`, `ToolTip.axaml`; `src/Raffica.Shell/Controls/TooltipContent.cs`
- Create: `src/Raffica.Gallery/Sections/InputsSection.axaml(.cs)`, `MenusSection.axaml(.cs)`
- Test: `tests/Raffica.Shell.Tests/Controls/TextBoxTests.cs`, `ComboBoxTests.cs`, `MenuTests.cs`, `TooltipTests.cs`

**Interfaces:**
- Consumes: Task 2, 3 and 5 (focus ring, type classes, icons).
- Produces: `TextBox` theme (rest `Surface.Page` + `Border.Control`, placeholder `Text.Secondary`, hover edge `Text.Secondary`, typing edge `Text.Primary`, disabled `Surface.Band` + `Text.Disabled`, 26 px high, radius 3, ink focus ring) and `ComboBox` theme (26 px, chevron icon, popup as menu-style list with 1 px `Border.Control` edge, 32 px rows, no shadow).
- Produces: `Menu`/`MenuItem`/`ContextMenu` themes: 32 px rows, hover `State.Hover`, shortcut column in 13 px `Text.Secondary` (from `InputGesture`), submenu arrow, separators (`Rule`), disabled incl. shortcut, `Classes="destructive"` item in `Danger`, 600 weight, set after a wider separator; 1 px `Border.Control` edge, radius 3, no shadow.
- Produces: `TooltipContent : ContentControl` with `Title` (13 px, 600), `Shortcut` (plain `Text.Secondary` text), `Description` (12 px), max width 240 px; the `ToolTip` theme: `Surface.Page`, 1 px `Border.Control`, radius 3, no arrow, no shadow.

- [ ] **Step 1: Write the failing tests** (`[AvaloniaFact]`, Light and Dark as in Task 5):
  - `TextBoxTests`: height 26; placeholder brush is `Text.Secondary`; focused edge/ring uses the ink; disabled uses `Surface.Band` and `Text.Disabled`; text selection uses `Selection.Tint` (appendix B).
  - `ComboBoxTests`: 26 px high; opens on Enter and Down; popup rows are 32 px; the selected item is announced by the automation peer.
  - `MenuTests`: item row height 32; shortcut text is shown for an item with an `InputGesture` (`Ctrl+S`) and right-aligned; a disabled item and its shortcut use `Text.Disabled`; a destructive item uses `Danger` and is last after a separator; popup has no shadow (`BoxShadow` empty) and a 1 px edge; keyboard: Down/Up/Enter/Esc work.
  - `TooltipTests`: `TooltipContent` shows title, shortcut and description; `MaxWidth` is 240; popup has no shadow.
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement** the themes and `TooltipContent`.
- [ ] **Step 4: Gallery sections** `InputsSection` (input states, select with items) and `MenusSection` (a `Button` with a flyout menu incl. submenu, disabled and destructive items, a `ContextMenu` area, a button with a rich tooltip).
- [ ] **Step 5: Run** the tests and the `--screenshot` check for dark/German and light/English as in Task 5. Expected: PASS and plausible screenshots (open popups: the gallery opens one menu on start with `--open-menu`, a gallery-only switch used for the screenshot; add it to `GalleryOptions` and its test).
- [ ] **Step 6: Run** build, test and format. Expected: green.
- [ ] **Step 7: Commit** `feat(shell): add input, select, menu, context menu and tooltip`.

### Task 7: Dialog and notification bar

**Files:**
- Create: `src/Raffica.Shell/Controls/Dialog.cs`, `DialogHost.cs`, `NotificationBar.cs`; `src/Raffica.Shell/Styles/Controls/Dialog.axaml`, `NotificationBar.axaml`
- Create: `src/Raffica.Gallery/Sections/DialogsSection.axaml(.cs)`
- Test: `tests/Raffica.Shell.Tests/Controls/DialogTests.cs`, `NotificationBarTests.cs`

**Interfaces:**
- Produces: `Dialog : ContentControl` with `string Title`, `object? Footer` (the action buttons), `event EventHandler? CancelRequested`; 520 px wide, header 44 px (title 15/600 and a close button named `ShellStrings.Close`), body padding 16, footer 52 px on `Surface.Band`, 1 px `Border.Control` edge, radius 3, no shadow.
- Produces: `DialogHost : ContentControl` (placed once at the root, wraps the app content) with `void Show(Dialog dialog)` and `void Close()`, `bool IsOpen`. While open: `Scrim` brush over the content, focus moved into the dialog, Tab/Shift+Tab cycle inside it, Esc raises `CancelRequested`, the content behind does not receive keys or pointer input, and focus returns to the previous control on close.
- Produces: `enum NotificationSeverity { Info, Warning, Error }` and `NotificationBar : ContentControl` with `Severity`, `LeadIn` (string?, the bold opening phrase), `Message`, `Actions` (`AvaloniaList<Button>`, adding a third throws `InvalidOperationException`), `IsClosable` (default true), `event EventHandler? Closed`. 40 px high, `Surface.Page`, 1 px `Border.Control`, radius 3; icon: `Info` in `Text.Secondary`, `TriangleAlert` in `Warning.Icon`, `OctagonAlert` in `Text.Primary`; never red. The close button's name is `ShellStrings.Close`; the icon has an accessible name from `ShellStrings.NotificationInfo|Warning|Error`.

- [ ] **Step 1: Write the failing tests.**
  - `DialogTests`: `DialogHostShowsTheScrimAndMovesFocusIntoTheDialog`; `TabKeyNeverLeavesTheDialog` (press Tab ten times with three buttons, focus stays inside; Shift+Tab too); `EscapeRaisesCancelRequested`; `ContentBehindTheDialogIsNotReachable` (a click and a key on the covered button do nothing); `FocusReturnsToTheOpenerOnClose`; `DialogMetricsMatchTheSpec` (width 520, header 44, footer 52, no shadow); the scrim brush is `Raffica.Brush.Scrim` in Light and Dark.
  - `NotificationBarTests`: height 40; severity icons and brushes as specified; the Error bar uses no `Danger` brush anywhere in its visual tree; a third action throws; closing raises `Closed` and removes it from the layout; the lead-in is bold; the close button and the icon have automation names.
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement** the controls and themes (focus trap with a `KeyDown` tunnel handler on the dialog; no reflection).
- [ ] **Step 4: Gallery section `DialogsSection`:** a button that opens a sample dialog (title, text, boxed "Cancel" and primary "OK"), and the three notification bars with real sample texts from the gallery's strings (the crash-recovery example from the spec section 5 for Info with the two actions "Compare" and "Close").
- [ ] **Step 5: Run** the tests and `--screenshot` for both themes. Check the dark scrim value of call 3: if the frame behind is not visible, adjust `Scrim` dark and the pinned value in `TokenValuesTests` together. Expected: PASS.
- [ ] **Step 6: Run** build, test and format. Expected: green.
- [ ] **Step 7: Commit** `feat(shell): add the dialog and the notification bar`.

### Task 8: Gallery completion, accessibility test and the Native AOT proof

**Files:**
- Modify: `src/Raffica.Gallery/GalleryWindow.axaml(.cs)` (navigation over all sections, toolbar with theme Light/Dark/System, high-contrast switch and the four inks), `src/Raffica.Gallery/Program.cs` (`--screenshot`)
- Create: `src/Raffica.Gallery/Sections/FoundationsSection.axaml(.cs)` (type specimen in 400/500/600 and sizes, all colour tokens as swatches with their names, the icon set at 16 and 20 px, the four inks), `src/Raffica.Gallery/Resources/Strings.en.resx`, `Strings.de.resx`, `GalleryStrings.cs`
- Modify: `.github/workflows/ci.yml` (publish and smoke-test the gallery on both runners)
- Test: `tests/Raffica.Shell.Tests/Gallery/AutomationNameTests.cs`, `GalleryTests.cs`

**Interfaces:**
- Consumes: all sections from Tasks 5 to 7; `StringTable`, `Languages` (Task 4).
- Produces: `GalleryStrings` (the gallery's own texts: section titles, sample labels, sample dialog and notification texts) backed by a `StringTable` over the gallery assembly; same `.resx` rules as the shell (picked up by `TranslationTests`).
- Produces: `--screenshot <png>`: renders the window once after the first frame with `RenderTargetBitmap`, writes the PNG, exits 0.

- [ ] **Step 1: Write the failing tests.**
  - `AutomationNameTests.EveryInteractiveControlInTheGalleryHasAName`: builds each section in headless, walks the visual tree and, for every `Button`, `ToggleButton`, `CheckBox`, `RadioButton`, `TextBox`, `ComboBox`, `MenuItem`, `ListBoxItem` and `TabItem`, asserts the automation peer's name is non-empty (`ControlAutomationPeer.CreatePeerForElement(c).GetName()`); a planted unnamed icon button in a test window proves the test can fail.
  - `GalleryTests`: `EverySectionBuildsInEveryThemeAndInkAndLanguage` (3 theme modes + forced high contrast × 4 inks × 2 languages, no exception, non-empty bounds); `TheGalleryShowsEveryControlTheMilestoneNames` (finds at least one instance of each of the twelve base controls: button, toggle, input, select, checkbox, radio, segmented control, menu, context menu, tooltip, dialog, notification bar).
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement** the remaining gallery parts; translate the gallery strings; make `--screenshot` work.
- [ ] **Step 4: Run** the tests. Expected: PASS.
- [ ] **Step 5: Extend `ci.yml`** in the `build` job after the CLI smoke tests: publish `src/Raffica.Gallery` for `${{ matrix.rid }}` to `out/gallery` (Native AOT), then run `RafficaGallery --print-string Cancel --lang de` and require `Abbrechen` (bash on Linux, PowerShell on Windows, as the CLI smoke steps do); also require that `--print-string Cancel --lang en` prints `Cancel`. The `changes` job already treats `src/` changes as code.
- [ ] **Step 6: Local proof on Windows:** `dotnet publish src/Raffica.Gallery -c Release -r win-x64 -o artifacts/gallery/win-x64`, run `RafficaGallery.exe --lang de --theme dark --screenshot artifacts/gallery/de-dark.png` and the same for `en` and `light`, `--high-contrast`, and all four inks; open the PNGs and check against the boards (`Docs/design/direction-d/Komponenten.dc.html`, `Dialog.dc.html`, `Kontextmenue.dc.html`). Record findings in the PR, fix deviations from the spec.
- [ ] **Step 7: Run** build, test and format. Expected: green. Push the branch and check that the CI run passes on Windows and Linux (this is the proof for "published gallery runs under Native AOT on Windows and Linux").
- [ ] **Step 8: Commit** `feat(gallery): show every base control in both themes and languages`; `ci: publish and smoke-test the gallery`.

### Task 9: Documentation and close the milestone

**Files:**
- Modify: `Docs/DECISIONS.md`, `Docs/roadmap.json`, regenerated `Docs/ROADMAP.md`, `CLAUDE.md` (section "Project-specific")

- [ ] **Step 1: Decisions entries** (new, on top): "Shell project and token conventions (M1.1)" (project names, key naming, `ShellTheme`, layering over Fluent, no hex in component styles), "Avalonia.Headless.XUnit with xunit.v3 3.2.2 in the shell tests only" (reason, removal condition), "Dark scrim value", and, if the weights test forced it, "Public Sans as static instances". Rules of the file: do not rewrite old entries.
- [ ] **Step 2: `CLAUDE.md`, "Project-specific":** a short "Shell" subsection: where tokens and styles live, the key naming, the no-literal rule and its test, the string-table rule (both `.resx` embedded, tests find every `Strings.en.resx` under `src/`), gallery switches. No line numbers.
- [ ] **Step 3: `Docs/roadmap.json`:** tick the five tasks right after each is done during the work (not only here); here tick the three acceptance criteria with evidence: the contrast test (`ContrastTests` names and result), the missing-translation test (`TranslationTests.TheCheckFailsOnARemovedGermanString` and the run without), the CI run id with the gallery publish and `--print-string Cancel --lang de` on Windows and Linux. A criterion that could not be checked stays open with the command that checks it (for example the visual check on the ProBook: `RafficaGallery --lang de` from the Linux CI artifact). Set the status to `done` with `completedOn`, then `dotnet run tools/roadmap.cs -- generate` and `-- check`.
- [ ] **Step 4: Run** build, test, format and the roadmap check. All green.
- [ ] **Step 5: Commit** `docs: record the shell conventions and close milestone 1.1`, push, `gh pr create --base dev` (body ends with the attribution line), merge once CI is green (squash allowed).

---

## Self-review (done while writing)

- **Spec coverage:** tokens light/dark/HC (Task 2), fonts and icons (Task 3), twelve controls (Tasks 5 to 7), localization with the missing-translation test (Task 4), gallery in both themes and languages (Tasks 5 to 8), contrast test of every appendix A pairing (Task 2), AOT proof on Windows and Linux (Task 8). Spec section 6 items covered: semantic tokens only (no-hex test), contrast test with the "new pairing fails" rule (`EveryTokenIsClassified`), high-contrast set, fonts, icons, accessibility names, localization fallback plan (the generator fallback of spec section 7 is not needed unless Task 4 Step 5 fails; if it does, stop and report to the owner).
- **Out of scope on purpose:** field codes, F6, compact mode, title bar, ribbon, status bar, start view, settings (1.2); size and German layout tests (1.2).
- **Types:** `ThemeMode`, `AppInk`, `ShellTheme.Apply`, `StringTable`, `Languages.Resolve`, `ShellStrings`, `StrokeIcon`, `SegmentedControl`, `TooltipContent`, `Dialog`, `DialogHost`, `NotificationBar`, `NotificationSeverity`, `GalleryOptions`, `StateMatrix` are defined once in the task that produces them and used under the same names later.
- **Open until checked by a person:** the look on the ProBook (CachyOS) under Wayland and Windows high contrast with the real OS setting; both need the owner's machine.
