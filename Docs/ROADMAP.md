<!-- Generated from Docs/roadmap.json with `dotnet run tools/roadmap.cs -- generate` – do not edit by hand. -->
# Raffica – Roadmap

Overall roadmap of the project. Every milestone is sized so that it can be handled in **one** plan and one round of implementation.

> **This file is generated.** The source is [roadmap.json](roadmap.json) (schema: [roadmap.schema.json](roadmap.schema.json)). Change it only there, then run `dotnet run tools/roadmap.cs -- generate`.

## How we work

1. **"Plan milestone N":** Claude Code reads this roadmap, the linked spec and the current state of the code and writes a plan to `Docs/superpowers/plans/YYYY-MM-DD-milestone-NN-<topic>.md`. The plan is linked at the milestone, status → `🟡 planned`.
2. **Implementation:** Work through the plan. Every finished task is ticked right away (`done: true` in `Docs/roadmap.json`).
3. **Completion:** All acceptance criteria checked and ticked → status `✅ done`, enter the date.
4. Scope changes during implementation are recorded under "Scope changes", not silently in the code.

**Status legend:** `⬜ open` · `🟡 planned` · `🔵 in progress` · `✅ done`

**Rules:**
- Work through the milestones in order as a rule. The prerequisites of the respective milestone are what counts.
- A milestone starts with `Create spec` if its subproject has no spec yet. The spec is worked out by brainstorming and put under `Docs/superpowers/specs/`.
- Tasks of later subprojects are deliberately kept rough and are made precise when the respective spec is written.

## Overview

| # | Milestone | Subproject | Status | Plan | Done on |
|---|---|---|---|---|---|
| 0 | Repo skeleton & CI | 1 – Foundation | ✅ done | [Plan](superpowers/plans/2026-11-03-milestone-00-repo-skeleton.md) | 2026-11-03 |
| 1 | Design directions & spec | 2 – Design system and shell | ✅ done | [Plan](superpowers/plans/2026-11-03-milestone-01-design-directions.md) | 2026-11-03 |
| 1.1 | Tokens and base controls | 2 – Design system and shell | ✅ done | [Plan](superpowers/plans/2026-11-03-milestone-01-1-tokens-and-base-controls.md) | 2026-11-03 |
| 1.2 | App frame | 2 – Design system and shell | ✅ done | [Plan](superpowers/plans/2026-11-03-milestone-01-2-app-frame.md) | 2026-11-04 |
| 2 | Create spec for Raffica PDF | 3 – Raffica PDF | ✅ done | [Plan](superpowers/plans/2026-11-04-milestone-02-pdf-spec.md) | 2026-11-04 |
| 2.1 | Viewer | 3 – Raffica PDF | 🟡 planned | [Plan](superpowers/plans/2026-11-04-milestone-02-1-viewer.md) | – |
| 2.2 | Annotate | 3 – Raffica PDF | ⬜ open | – | – |
| 2.3 | Forms and signing | 3 – Raffica PDF | ⬜ open | – | – |
| 2.4 | Organize pages | 3 – Raffica PDF | ⬜ open | – | – |
| 2.5 | Redact, OCR and line editing | 3 – Raffica PDF | ⬜ open | – | – |
| 2.6 | Packaging and release | 3 – Raffica PDF | ⬜ open | – | – |
| 3 | Create spec for macOS | 4 – macOS | ⬜ open | – | – |
| 4 | Spikes & spec for Raffica Write | 5 – Raffica Write | ⬜ open | – | – |
| 5 | Create spec for Raffica Sheets | 6 – Raffica Sheets | ⬜ open | – | – |
| 6 | Create spec for Raffica Slides | 7 – Raffica Slides | ⬜ open | – | – |

## Scope changes

- **2026-11-03 (Milestone 0):** M0 includes the import of the Pagewing core (no UI) from the `v1.0.0` tag as the project skeleton: unchanged import, scripted rename, a commit that makes the subset self-contained, and the translation of German text. The listed tasks for the skeleton, `.gitignore` and CI are carried out on top of the imported solution. `.env.example` is not needed because the imported code uses no secrets. Decided in the suite spec and in `Docs/DECISIONS.md` (entry "Core-only import, fresh UI, new order of work").
- **2026-11-03 (Milestone 1.1):** Localized key names in menu shortcuts (`Strg+X` instead of `Ctrl+X`) and the TextBox context menu strings (Fluent's English Cut, Copy and Paste) move to milestone 1.2 with the German layout work. The gallery window is 1520 x 900. Decisions in `Docs/DECISIONS.md`, entries "Shell project and token conventions (M1.1)" and "Localization mechanism and its limits". The high-contrast palette is a fixed palette, not a token set built from the system colours as the spec asks (entry "High-contrast palette"); owner to confirm.
- **2026-11-04 (Milestone 1.2):** AppFrame collapses an empty tab row and band, so the start view sits directly under the title bar; the gallery's frame demo gets a fifth File tab.
- **2026-11-04 (Milestone 1.2):** `AutomationNameTests` ignores the RepeatButtons inside a RangeBase (the zoom slider), which carry no name of their own.
- **2026-11-04 (Milestone 1.2):** `RibbonView.NotifyInvoked` is new so that `CommandInvoked` also fires for commands run by a field code.
- **2026-11-04 (Milestone 1.2):** Additions beyond the plan, needed by the frame demo and the reviews: `Pane.IsDocument` and `Pane.PaneName`, `AppFrame.IsLeftPaneShown` and `IsRightPaneShown`, `TitleBar.LeadingInset`, zoom from 10 to 500 percent in steps of 10 with snapping, `FieldCodeTarget`, `PaneFocus.Focus(Pane)` and `StartView.FocusFirstTile`.
- **2026-11-04 (Milestone 1.2):** Q (search), 6 and 7 (panes) act in the field-code tab-letter stage through app-supplied `FieldCodeTarget`s; the spec lists the badges as display only and the plan said "shows". The group digits stay a display-only legend.
- **2026-11-04 (Milestone 1.2):** The frame demo window has a minimum width of 1024 px. The full ribbon at 1024 px without the compact bar clips groups of the Home tab; this is a recorded limitation and not a tested case, there is no automatic compact mode (backlog).
- **2026-11-04 (Milestone 1.2):** The shell tests run serially (`DisableTestParallelization` in `tests/Raffica.Shell.Tests/TestCulture.cs`) because parallel test classes switching the shared `ShellStrings.Culture` made the suite flaky (about one first run in three after a build).
- **2026-11-04 (Milestone 2):** Milestones 2.1 to 2.6 are added under the PDF spec: Viewer, Annotate, Forms and signing, Organize pages, Redact, OCR and line editing, Packaging and release. Decided in `Docs/DECISIONS.md`, entries "Raffica PDF: scope and order (M2)" and "Raffica PDF: speed budgets are measured, not inherited (M2)".
- **2026-11-04 (Milestone 2):** Milestone 3 (macOS spec) now depends on 2.6 instead of 2, so that macOS stays after the finished PDF app; milestone 2 only means that the PDF spec exists.
- **2026-11-04 (Milestone 2):** The OCR library build moves from the backlog into milestone 2.5, and the release workflow into 2.6; the CodeQL and hardware-measure workflows come back in 2.5. Decided in `Docs/DECISIONS.md`, entry "Raffica PDF: platform code, autosave, OCR build and releases (M2)".
- **2026-11-04 (Milestone 2):** Milestone 2.1 adds an additive option to the shell's settings dialog so that the PDF app hides the autosave row (autosave contradicts "never damages the original"); the frame's behaviour for the other apps stays as it is. Decided in `Docs/DECISIONS.md`, entry "Raffica PDF: platform code, autosave, OCR build and releases (M2)".
- **2026-11-04 (Milestone 2.1):** Inventory of Pagewing 1.0 (its roadmap and README at the `v1.0.0` tag), entered as marks `(P<n>)` in the tasks of 2.1 to 2.6. Done in 2.1 although not named in the spec's list: document info from the status bar (P4: file, pages, page size, PDF version, file size, metadata) and the F1 help card with the shortcut overview built from the command catalog (P6). Already done in the imported core: `raffica search` (P5). Open for after 2.6 unless the owner asks earlier, both in the backlog: tabs between windows (P4.1) and the "open in new window" command of P4, because the frame's `DocumentTabs` has no drag-out and a workspace across windows is a shell feature that no other app needs yet; the setting "reduce animations" (P6), because the shell's design spec has no such setting and its motion is limited to control transitions (owner to confirm). Not rebuilt on purpose: the density setting (replaced by the compact command bar of the shell) and a separate high-contrast switch (the shell follows the system). Pagewing's public-repository work (12.1) is the owner's decision and stays listed as open in 2.6.
- **2026-11-04 (Milestone 2.1):** Three additive extensions of the shell, needed by the PDF app and neutral for the other apps: (1) the settings dialog takes optional `SettingsDialogOptions` (hide the autosave row, extra rows the app builds and reads in its `onOk`), (2) `DocumentTab` gets a dirty marker and an exposed selected state (the backlog item on the selected state of document tabs for assistive technology is done with it), (3) the frame's `SearchField` takes an optional `ExtraResults` provider for entries that are not ribbon commands (open tabs, recent files, outline entries, "Go to page N"). Decided in `Docs/DECISIONS.md`, entry "Raffica PDF: viewer conventions (M2.1)".
- **2026-11-04 (Milestone 2.1):** A file that is already open is selected, not opened twice (Pagewing opened a second tab). Two tabs of one file would both hold dirty state once 2.2 adds editing.
- **2026-11-04 (Milestone 2.1):** The test PDF generator of Pagewing (`tools/make-testpdfs.cs`) is not imported in 2.1: the fixtures under `testdata/pdfs` cover every viewer test, and a task that needs a new fixture says so.
- **2026-11-04 (Milestone 2.1):** Milestone 2.1 has 17 tasks in its plan. If the owner asks, it can be split after Task 11 of the plan into 2.1 (viewing and searching) and a further milestone (printing and speed budgets); the extra id is chosen then.

---

# Subproject 1 – Foundation

**Spec:** [2026-11-03-raffica-suite-design.md](superpowers/specs/2026-11-03-raffica-suite-design.md) Suite design, approved 2026-11-03

## Milestone 0 – Repo skeleton & CI
**Status:** ✅ done (2026-11-03) · **Plan:** [2026-11-03-milestone-00-repo-skeleton.md](superpowers/plans/2026-11-03-milestone-00-repo-skeleton.md) · **Spec:** [2026-11-03-raffica-suite-design.md](superpowers/specs/2026-11-03-raffica-suite-design.md) · **Prerequisites:** none

Goal: A set-up repo with the renamed Pagewing core, tests and CI on which all further milestones build.

**Tasks**
- [x] Create the spec (brainstorming): goal, scope, stack, subprojects; link it here and enter the first milestones
- [x] Repo setup done: `dev` as default branch, merge options, branch protection for `main` and `dev`
- [x] Fill in the project header of `CLAUDE.md` and `README.md`, remove the template section from the README
- [x] Import commit 1: the Pagewing core (`Core`, `Pdfium`, `Ocr`, `Cli`), its tests, test data and licence files from the `v1.0.0` tag, unchanged, with a stack-specific `.gitignore`
- [x] Import commit 2: scripted rename (dry run first), updater keys and URLs replaced by documented placeholders
- [x] Make the subset self-contained and green: solution file, removal of App-coupled tests, OCR library fetch script, `.editorconfig`; project skeleton incl. test projects
- [x] Translate the imported German comments, strings and test names to English
- [x] `.env.example` with placeholders, if secrets are needed (not needed, see scope change)
- [x] CI (GitHub Actions): build, test and format/lint on PRs to `dev` and `main`; the job `roadmap` from `.github/workflows/roadmap.yml` stays; aggregate check `ci`
- [x] Record the build, test and format/lint commands in `CLAUDE.md` and write the README setup section
- [x] Enter the CI jobs (`roadmap` and `ci`) as required status checks in the branch protection of `main` and `dev` (only on the owner's explicit instruction)
- [x] Dependabot for the package ecosystem and GitHub Actions

**Acceptance criteria**
- [x] Before the rename, every imported file is byte-identical to the `v1.0.0` tag (`diff -r` against a `git archive` of the same paths) – `diff -r` of a fresh `git archive` against every imported path before commit 1: no differences; the final review compared all 628 blobs of commit b128acd with the tag: none differ; 80 binary fixtures compared after the rename: none changed
- [x] No reference to Pagewing remains outside the allowlist (test `NoPagewingLeftoverTests`), and the updater trusts no Pagewing key or URL – `NoPagewingLeftoverTests.TextFilesMentionPagewingOnlyWhereTheFixturesDo` fails with a planted mention in `NuGet.config` and passes without it; `UpdateTrustTests.TheReleaseBuildDoesNotTrustThePagewingReleaseKeyOrReleases` failed first (RED) and passes
- [x] Build, test and format/lint run green locally (commands in `CLAUDE.md`), with no skipped OCR tests – `dotnet build Raffica.slnx -c Release`: 0 warnings, 0 errors; `RAFFICA_REQUIRE_OCR=1 dotnet test Raffica.slnx -c Release --no-build --max-parallel-test-modules 1`: 3,409 run, 0 failed, 21 skipped (OS-specific or opt-in measurement tests, none for OCR); `dotnet format Raffica.slnx --verify-no-changes`: exit 0 (Windows)
- [x] CI is green on a PR to `dev` – PR 5, run 41604111774: `build-linux` (3,409 run, 0 failed), `build-windows` (3,409 run, 0 failed), `ci` and `roadmap` green
- [x] Branch protection for `main` and `dev` is active: PR required, CI check required, no force push, no deletion – `gh api repos/benjo-code/raffica/branches/dev/protection` and `.../main/protection` (2026-11-03): required checks `roadmap` and `ci` (strict), PR required, force pushes false, deletions false

---

# Subproject 2 – Design system and shell

**Spec:** [2026-11-03-design-system-shell-design.md](superpowers/specs/2026-11-03-design-system-shell-design.md) Design system and shell, approved 2026-11-03

## Milestone 1 – Design directions & spec
**Status:** ✅ done (2026-11-03) · **Plan:** [2026-11-03-milestone-01-design-directions.md](superpowers/plans/2026-11-03-milestone-01-design-directions.md) · **Prerequisites:** M0

Goal: One design direction for all four apps is chosen and the spec for the design system and shell is written.

**Tasks**
- [x] Measure cold start, memory and size of a Native AOT Avalonia app on Windows, Linux and macOS (sets the speed targets)
- [x] Create at least three design directions with Impeccable as Claude artifacts that show Write, Sheets, Slides and PDF; the owner chooses one
- [x] Create the spec (brainstorming): tokens, components, app frame, localization infrastructure (English/German), WCAG contrast of every accent

**Acceptance criteria**
- [x] The measured values are recorded and the speed targets are set from them – `measure.yml` runs on the three machines (`dev` at 0480960): Linux 41682565866, Windows 41683058597, macOS 41687457617 (indicative), cold after a reboot Windows 41689113140 and Linux 41689118537; raw CSVs and budget table in `Docs/measurements/shell-probe-2026-11-03/`; budgets and limits for Windows and Linux in `Docs/DECISIONS.md`, entry "Speed targets of the frame (M1)". Not yet measured, with the command: cold start after a reboot on macOS (`gh workflow run measure.yml --ref dev -f platform=macos -f note="<note>" -f cases=cold-reboot`)
- [x] A design direction is chosen by the owner and recorded in `Docs/DECISIONS.md` – Directions A, B (Shelf, then Wayfinding), C and D built as Claude Design canvases; the owner chose D "Formular Neutral" after two refinement rounds (canvas version 9, sources in `Docs/design/direction-d/`); `Docs/DECISIONS.md`, entry "Design direction: D "Formular Neutral"" (2026-11-03)

## Milestone 1.1 – Tokens and base controls
**Status:** ✅ done (2026-11-03) · **Plan:** [2026-11-03-milestone-01-1-tokens-and-base-controls.md](superpowers/plans/2026-11-03-milestone-01-1-tokens-and-base-controls.md) · **Prerequisites:** M1

Goal: The shared shell project has the tokens, fonts, icons and base controls in light, dark and high contrast, shown in a gallery app in English and German.

**Tasks**
- [x] Shell project with the token dictionaries (light, dark, Windows high contrast) from appendix A of the spec
- [x] Embedded fonts (Public Sans, Barlow Semi Condensed, Carlito) and the icon set on the 16/20 px grid
- [x] Base controls: button, toggle, input, select, checkbox, radio, segmented control, menu, context menu, tooltip, dialog, notification bar
- [x] Localization mechanism (resx under Native AOT) with the missing-translation test
- [x] Gallery app that shows every control in both themes and languages

**Acceptance criteria**
- [x] The contrast test checks every pairing of appendix A in light and dark and passes – `ContrastTests.EveryTokenIsClassified`, `PairingsMeetTheirThreshold`, `FilledPairingsMeetTheirThreshold`, `ForbiddenPairingsReallyFail` and `TokenValuesMatchAppendixA` pass (CI run 41717652867, `build-linux` and `build-windows`; also green in the local full run)
- [x] The missing-translation test fails on a removed German string and passes without it – `TranslationTests.TheCheckFailsOnARemovedGermanString` fails the check on a removed, empty or extra German key in an in-memory copy of the real file; `TranslationTests.EveryEnglishKeyHasAGermanValue` passes on the real files (CI run 41717652867)
- [x] The published gallery app runs under Native AOT on Windows and Linux and shows German strings with `--lang de` – CI run 41717652867: `build-windows` and `build-linux` publish the gallery with Native AOT and the smoke step passes (`--print-string Cancel --lang de` gives Abbrechen, `--lang en` gives Cancel); local win-x64 publish without warnings. Proven locally: the win-x64 Native AOT binary rendered all sections to screenshots with exit 0 (`--screenshot`); on Linux CI only the publish and `--print-string` run (no window). Still open (needs the owner's machines): the visual look on the ProBook (CachyOS, Wayland) with `dotnet publish src/Raffica.Gallery -c Release -r linux-x64 -o out/gallery` and then `out/gallery/RafficaGallery --lang de`, and the real Windows high-contrast setting: turn on a Windows contrast theme, start `RafficaGallery` without `--high-contrast` and toggle the contrast theme while the app runs (`--high-contrast` forces the palette and skips the OS detection; use it to look at the palette on any OS)

## Milestone 1.2 – App frame
**Status:** ✅ done (2026-11-04) · **Plan:** [2026-11-03-milestone-01-2-app-frame.md](superpowers/plans/2026-11-03-milestone-01-2-app-frame.md) · **Prerequisites:** M1.1

Goal: The shared app frame works in the gallery app with every behaviour of the spec, within the measured speed limits.

**Tasks**
- [x] Title bar with document tabs and search (Ctrl+K), ribbon with compact mode, side panels, status bar
- [x] Field codes (Office-style sequence) and F6 pane focus
- [x] Start view and settings dialog (theme, language after restart, compact command bar, autosave)
- [x] Size check in CI and a German layout test at 1440 px and at the compact width

**Acceptance criteria**
- [x] Tests cover every field-code step, the AltGr case, duplicate codes and F6 navigation – Field-code steps: `FieldCodeSessionTests` (`BareAltPressAndReleaseTurnsTheModeOn`, `F10TurnsTheModeOn`, `AltWithAnotherKeyInBetweenIsAnOrdinaryShortcut`, `TabLetterShowsTheCommandCodes`, `TwoCharacterCodeCompletesOnTheSecondKey`, `MenuCommandShowsItsItemCodesNext`, `EscapeGoesBackOneStep`, `AltAgainEndsTheMode`, `CancelEndsTheMode`) and `FieldCodeInputTests` (menu item codes, click, deactivation, dialog, F6 and Ctrl+K ending the mode). AltGr: `FieldCodeSessionTests.AltGrNeverEntersTheMode`, `FieldCodeInputTests.AltGrOnWindowsTypesTheCharacter`, `AltGrPressedAndReleasedAloneDoesNotEnterTheMode`, `AltGrOnLinuxSendsNoCtrlButTheCharacterStillCountsAsAnotherKey`. Duplicate and prefix codes: `RibbonDefinitionTests.DuplicateCodeInATabFails`, `DuplicateTabLetterFails`, `DuplicateCodeInAMenuFails`, `PrefixCodeInATabFails`, `PrefixCodeInAMenuFails`, `APrefixAcrossTwoGroupsOfATabFails`, `ThePrefixCheckIsPerTabAndPerMenu`; `FrameDemoTests.EveryDemoCommandIsReachableThroughItsFieldCode` walks every demo command. F6: `PaneRingTests` (`NextCyclesThroughThePanes`, `PreviousCyclesBackwards`, `ClosedPanesAreSkipped`) and `PaneFocusTests` (`F6MovesNavigationDocumentComments`, `ShiftF6GoesBackwards`, `F6DoesNothingWhileADialogIsOpen`). Run: `RAFFICA_REQUIRE_OCR=1 dotnet test Raffica.slnx -c Release --no-build --max-parallel-test-modules 1` gives 4071 total, 4050 passed, 0 failed, 21 skipped (Linux file modes and measurement-only tests); `dotnet build Raffica.slnx -c Release` gives 0 warnings; `dotnet format Raffica.slnx --verify-no-changes` is clean. Headless tests cannot show the real keyboard path of an operating system: the manual checks that stay open are listed under "Open items" below.
- [x] The German layout test finds no clipped ribbon label – `GermanLayoutTests.NoRibbonLabelIsClippedInGerman` (1440 px with the full ribbon and 1024 px with the compact bar, Light and Dark, every tab) passes, as do `EnglishFitsToo`, `NoDocumentTabOrStatusCellIsClippedInGerman` and `TabsAndStatusCellsFitInEnglishToo`. The test can fail: the first run with `LabelFit` in place listed 15 clipped German labels at 1440 px (for example Ausschneiden, Format übertragen, Rechtschreibung, Alle Änderungen annehmen, Persönliche Daten entfernen), which were fixed (label width 96 px for tiles and 160 px for small buttons, three demo labels shortened). Negative proof: with the German `CmdCopy` lengthened to "Kopieren in die Zwischenablage" the test failed at 1440 px, and with the position text lengthened to "Seite 1 von 3 im Abschnitt Einleitung" it failed at 1024 px compact, naming the labels (status bar); both strings were reverted. Recorded limitation, not a tested case: the full ribbon at 1024 px without the compact bar clips the Find and Clear groups of the Home tab (see the scope change and the backlog entry about an automatic compact mode).
- [x] A `measure.yml` run on the Windows and Linux machines stays within the limits recorded in milestone 1 – `measure.yml` with `target=gallery` on `dev` (commit ae4d07c, after PR #18): Windows run 41788263177 and Linux run 41788266869, both `check: ok`. Windows (Ryzen 7 7800X3D, RTX 4070 SUPER, 1920 x 1080 at 60 Hz): cold 375.23 ms (limit 537.5), warm 361.92 ms (525), input median 4.53 ms (12.5), memory 111.34 MiB (156.25), size 40.65 MiB (48.75), zip 16.8 MiB (20). Linux (HP ProBook i5-8265U, CachyOS, Wayland): cold 185.68 ms (250), warm 182.15 ms (262.5), input median 16.59 ms (25), memory 132.3 MiB (156.25), size 37.25 MiB (43.75), zip 16.38 MiB (20). The CSVs and machine notes are in `Docs/measurements/gallery-frame-2026-11-04/` and the medians are the baseline in `Docs/DECISIONS.md` ("Gallery frame baseline (M1.2)"). The CI step "Size of the published gallery" passes on both legs of PR #18; `dotnet run bench/shell-probe/measure.cs -- selftest` gives 122/122 checks. The runner group only accepts `measure.yml` from `dev`, so the runs were started after the merge.

**Open items**
**Measurement runs (criterion 3)** are done, see its evidence.

**Manual checks (headless tests cannot show them); the owner chose to skip them on 2026-11-04, so they stay open.**
- Bare Alt on a real Windows machine: `dotnet run --project src/Raffica.Gallery -- --view frame`, press and release Alt: the field codes appear and no system menu opens; also with a dialog open (Alt inside the settings dialog) and with F10.
- AltGr: type `@` with AltGr in the search field on a German layout; F10 then a letter.
- Alt, H, 2, 1 opens the paste menu without taking the keyboard focus (the flyout is opened `Transient`), on Windows, Linux and macOS.
- Title bar on the ProBook (CachyOS, Wayland): `dotnet publish src/Raffica.Gallery -c Release -r linux-x64 -o out/gallery`, then `out/gallery/RafficaGallery --view frame`; the system-title-bar fallback is not built and becomes a scope change if this fails.
- Windows caption buttons: the roles map to the system hit codes and the buttons also set `WindowState`; check that maximize does not toggle twice.
- macOS: drag area, the 80 px inset for the system buttons and the Option key (a bare Option tap enters the mode and takes the next letter; there is no platform gating).

---

# Subproject 3 – Raffica PDF

**Spec:** [2026-11-04-raffica-pdf-design.md](superpowers/specs/2026-11-04-raffica-pdf-design.md) Raffica PDF design, approved 2026-11-04

## Milestone 2 – Create spec for Raffica PDF
**Status:** ✅ done (2026-11-04) · **Plan:** [2026-11-04-milestone-02-pdf-spec.md](superpowers/plans/2026-11-04-milestone-02-pdf-spec.md) · **Spec:** [2026-11-04-raffica-pdf-design.md](superpowers/specs/2026-11-04-raffica-pdf-design.md) · **Prerequisites:** M1

Goal: The scope and the milestones of the PDF app on the new UI and the imported core are written down.

**Tasks**
- [x] Create the spec (brainstorming) and enter its milestones

**Acceptance criteria**
- [x] The spec is approved and linked here – The owner approved the spec in the planning session on 2026-11-04 ("approved"), as handed over with its marked (call) items; the spec status is set to approved; it is linked from subproject 3 and from this milestone: `superpowers/specs/2026-11-04-raffica-pdf-design.md`. `dotnet run tools/roadmap.cs -- check` passes with milestones 2.1 to 2.6 entered. Verification on Windows: `dotnet build Raffica.slnx -c Release` 0 warnings, 0 errors; `RAFFICA_REQUIRE_OCR=1 dotnet test Raffica.slnx -c Release --no-build --max-parallel-test-modules 1` 4071 run, 0 failed, 21 skipped; `dotnet format Raffica.slnx --verify-no-changes` exit 0; PR 20 `ci` and `roadmap` green.

## Milestone 2.1 – Viewer
**Status:** 🟡 planned · **Plan:** [2026-11-04-milestone-02-1-viewer.md](superpowers/plans/2026-11-04-milestone-02-1-viewer.md) · **Spec:** [2026-11-04-raffica-pdf-design.md](superpowers/specs/2026-11-04-raffica-pdf-design.md) · **Prerequisites:** M2, M1.2

Goal: Raffica PDF opens, shows, searches and prints PDFs on the shared frame within speed budgets measured for it.

**Tasks**
- [x] Inventory Pagewing 1.0's features (its roadmap and README) and enter the result in this roadmap as a checklist per milestone 2.1 to 2.6 (tasks of each milestone, recorded as a scope change) - done 2026-11-04: read from `git show v1.0.0:Docs/ROADMAP.md` and `README.md` of Pagewing (milestones 3 to 16 and the 8.1, 12.1 and 14.1 additions). Every predecessor feature is a mark `(P<n>)` in the tasks of 2.1 to 2.6 (n = Pagewing milestone); the decisions for features outside the spec list are in the scope changes of 2026-11-04 (context "Milestone 2.1"): done in 2.1 (document info, help card F1), already done in Core (`raffica search`), open for after 2.6 (tabs between windows, open in a new window, reduce-animations setting).
- [ ] App project and executable on the shared frame (depends on `Raffica.Core` and `Raffica.Shell` only); PDF ribbon, panes and start view (P3)
- [ ] Document and tab model with the dirty state, its marker on the tab and in the status bar, and the close prompt (Save, Don't save, Cancel)
- [ ] Open by file argument, dialog, drag and drop and password (P3); single instance, a second file opens in the running instance (P3); recent files, stored locally and read only after the first sharp page (P6); tabs: open, close, switch, reorder, and a file that is already open is selected instead of opened twice (P4); position per file (page, zoom, zoom mode) (P6)
- [ ] Continuous scroll with render scheduler and tile cache (P3); zoom with Ctrl+wheel and Ctrl+plus/minus (P3); zoom modes page width, whole page, actual size, free (P4); go to page (P4); layouts single, continuous, two-page with and without cover page (P4); dark page without inverted images, switchable per tab (P6); pages pane with thumbnails and outline pane with jump (P4); heavy work starts only after the first sharp page
- [ ] Full-text search with highlights, next and previous, in the background (P5); select and copy text (P5); internal and external links, external only after a click and a confirmation that shows the URL (P5); command palette (Ctrl+K) with open tabs, recent files, pages and outline entries besides the commands (P6)
- [ ] Settings that apply to PDF (page appearance, default layout and zoom, print scaling; theme, language, and the compact command bar in place of the density setting; high contrast follows the shell) (P6); the autosave row hidden through an additive shell option, recorded as a scope change
- [ ] Printing (P7) on Windows (Win32 print dialog, `FPDF_RenderPage`) and Linux (CUPS with an own Avalonia dialog): page range, copies, fit to paper, always light, annotations printed, locked for documents that forbid printing, background job with progress and cancel
- [ ] Set the PDF speed budgets (P3 startup check): measure the cases of spec section 5 with Pagewing 1.0 and Chrome as references, record budgets and limits (budget x 1.25 once) in `Docs/DECISIONS.md`, extend the `measure.yml` machinery; if the app is slower than Pagewing 1.0, look for the cause first (for example deferring PDFium initialisation or the ribbon) before raising the finding
- [ ] Document info from the status bar: file name, page count, page size, PDF version, file size and metadata (P4)
- [ ] Help card (F1) with the shortcut overview built from the command catalog, and keyboard shortcuts for every viewer action (P6)
- [x] `raffica search` in the CLI (P5): already part of the imported core, nothing to build (`tests/Raffica.Core.Tests/Cli/SearchCliTests.cs` came with the core in M0, the command is dispatched in `src/Raffica.Cli/CliApp.cs`)

**Acceptance criteria**
- [ ] The render-regression and viewer tests pass: `dotnet test Raffica.slnx -c Release --no-build --max-parallel-test-modules 1` with `RAFFICA_REQUIRE_OCR=1`
- [ ] The PDF speed budgets are recorded in `Docs/DECISIONS.md` and `measure.yml` is within them on Windows and Linux
- [ ] The app opens a file passed as argument and a second file in the running instance (manual check, recorded)
- [ ] Every Pagewing 1.0 feature of the viewer group is either done or listed as open in the inventory

## Milestone 2.2 – Annotate
**Status:** ⬜ open · **Plan:** – · **Spec:** [2026-11-04-raffica-pdf-design.md](superpowers/specs/2026-11-04-raffica-pdf-design.md) · **Prerequisites:** M2.1

Goal: Marks and comments can be added, changed, undone and saved through the existing save pipeline.

**Tasks**
- [ ] Markup (highlight, underline, strike-through) and comments with the comments pane and the marks pane (P9)
- [ ] Freehand with mouse and pen, rectangle, ellipse, line, arrow and text box (P10); select, move, resize, colour, delete, and undo and redo in one history for all annotation kinds (P9, P10)
- [ ] The save pipeline and its UI through the existing core (P9): save, save as, incremental by default, atomic and checked, the warning for signed files, failure cards, the unsaved-changes prompt on close
- [ ] Tests for every tool and for save, reopen and undo

**Acceptance criteria**
- [ ] Tests for every tool and for save, reopen and undo pass
- [ ] A saved file opens with the marks in an independent viewer (manual check, recorded with the viewer's name)

## Milestone 2.3 – Forms and signing
**Status:** ⬜ open · **Plan:** – · **Spec:** [2026-11-04-raffica-pdf-design.md](superpowers/specs/2026-11-04-raffica-pdf-design.md) · **Prerequisites:** M2.2

Goal: AcroForm fields can be filled and a visible signature placed and saved.

**Tasks**
- [ ] Fill AcroForm fields (text, checkbox, radio, list, combo) with entry in Avalonia elements over the field, field highlight, tab order and keyboard-only use, and a limited character set (P11); undo and redo of values in the shared history with the annotations (P11); the app says that calculations, formats and checks need JavaScript and are not run, that XFA and digital signature fields are not supported and that buttons never run an action (P11)
- [ ] The required-field card before saving, with the values saved together with the annotations in one operation (P11)
- [ ] The visible signature (P12): draw or import an image, up to ten saved signatures stored locally, placement; the dialog carries the Raffica PDF hint that a visible signature is not legally binding
- [ ] Tests for each field kind and for the signature store

**Acceptance criteria**
- [ ] Tests for each field kind and for the signature store pass
- [ ] A filled form opens with the values in an independent viewer (manual check, recorded with the viewer's name)

## Milestone 2.4 – Organize pages
**Status:** ⬜ open · **Plan:** – · **Spec:** [2026-11-04-raffica-pdf-design.md](superpowers/specs/2026-11-04-raffica-pdf-design.md) · **Prerequisites:** M2.2

Goal: Pages can be reordered, rotated, deleted, inserted, merged, split and exported from the pages pane.

**Tasks**
- [ ] Reorder, rotate, delete and insert pages in the pages pane and the ribbon (P13)
- [ ] Merge and split (split every n pages, by ranges, by top-level bookmarks) and export pages as image (P13); the CLI commands `rotate`, `merge`, `split` and `export` already exist in the imported core
- [ ] Full rewrite for page operations with the warning for signed files and the list of what a page operation loses, shown before it runs (P13)
- [ ] Tests on the page edit plan and its report

**Acceptance criteria**
- [ ] Tests on the page edit plan and its report pass
- [ ] Merge and split results open with the right page count (test and a manual check in an independent viewer)

## Milestone 2.5 – Redact, OCR and line editing
**Status:** ⬜ open · **Plan:** – · **Spec:** [2026-11-04-raffica-pdf-design.md](superpowers/specs/2026-11-04-raffica-pdf-design.md) · **Prerequisites:** M2.3, M2.4

Goal: Content can be redacted for real, scans made searchable and text lines corrected, with the OCR library built in this repository.

**Tasks**
- [ ] Build the Tesseract library in this repository (workflow and build scripts), replace the fetch from Pagewing's prerelease in `tools/native-fetch.cs`, and put the CodeQL and hardware-measure workflows back where needed
- [ ] The destructive group of the ribbon; redaction by selection, area and pattern (email, IBAN, phone, card), fill colour, blur and pixelate for image parts, overlay text, and the rescue of pages that cannot be taken apart by turning them into an image (P14, P14.1), ending in a full rewrite with the checks on extracted text and raw bytes
- [ ] OCR with language management (English and German bundled, more from the user folder); the heavy work starts after the first sharp page (P15)
- [ ] Text line editing: correct existing lines and typos, no paragraph editing; an empty text removes the run (P16)

**Acceptance criteria**
- [ ] The redaction removal tests (extracted text and raw bytes) pass through the UI path
- [ ] OCR makes a scanned test file searchable (test with `RAFFICA_REQUIRE_OCR=1`)
- [ ] Text editing changes a line and keeps the rest of the page (test)
- [ ] `tools/native-fetch.cs` no longer names the Pagewing repository and the CI builds the OCR library here

## Milestone 2.6 – Packaging and release
**Status:** ⬜ open · **Plan:** – · **Spec:** [2026-11-04-raffica-pdf-design.md](superpowers/specs/2026-11-04-raffica-pdf-design.md) · **Prerequisites:** M2.5

Goal: Installers, default-app registration and the updater exist for Windows and Linux, and the first release can be published on the owner's instruction.

**Tasks**
- [ ] Windows installer (Inno Setup, new AppId) and portable ZIP; Linux `.deb` and AppImage (P8)
- [ ] Default-PDF-app registration and the first-start card, with the platform implementations of IDefaultAppService (P8)
- [ ] The updater (P8.1) with its own signing key (kept by the owner in a CI secret) and the release address of this repository; the update-check switch in the settings; the unsaved-changes prompt before an install
- [ ] License notices in the packages and the About card, the release workflow, README for users and CHANGELOG (P8)
- [ ] List the open items that need the owner's machines (installers, printers, the real update key, the public repository) with the command that checks each

**Acceptance criteria**
- [ ] Installers build in CI (the workflow run is named in the evidence)
- [ ] An installed build runs, registers as the PDF handler and updates from one test release to the next with the test key (manual check on Windows and Linux, recorded); the path with the real key stays an open item
- [ ] The release workflow runs green as a dry run (workflow run named in the evidence); publishing the first release is not part of this milestone, it happens only on the owner's instruction and stays listed as an open item
- [ ] Every Pagewing 1.0 feature in the inventory of 2.1 is done or listed as open, with the reason (checked against the inventory by hand and recorded)

---

# Subproject 4 – macOS

**Spec:** not written yet (created in its first milestone)

## Milestone 3 – Create spec for macOS
**Status:** ⬜ open · **Plan:** – · **Prerequisites:** M2.6

Goal: The build, ad-hoc signing and `.dmg` packaging for macOS are specified.

**Tasks**
- [ ] Create the spec (brainstorming) and enter its milestones

**Acceptance criteria**
- [ ] The spec is approved and linked here

---

# Subproject 5 – Raffica Write

**Spec:** not written yet (created in its first milestone)

## Milestone 4 – Spikes & spec for Raffica Write
**Status:** ⬜ open · **Plan:** – · **Prerequisites:** M3

Goal: The layout engine decision for Write rests on measurements and the spec is written.

**Tasks**
- [ ] Spike: Open XML SDK 3.x under Native AOT (open, edit, save DOCX and XLSX)
- [ ] Spike: text layout speed on a long document (Avalonia/Skia/HarfBuzz versus a Rust candidate)
- [ ] Spike: printing per operating system and IME behaviour on Windows and Linux
- [ ] Create the spec (brainstorming) and enter its milestones

**Acceptance criteria**
- [ ] The spike results are recorded and the layout engine decision is in `Docs/DECISIONS.md`
- [ ] The spec is approved and linked here

---

# Subproject 6 – Raffica Sheets

**Spec:** not written yet (created in its first milestone)

## Milestone 5 – Create spec for Raffica Sheets
**Status:** ⬜ open · **Plan:** – · **Prerequisites:** M4

Goal: The scope of Sheets and the choice of the formula engine (IronCalc or an own one) are decided.

**Tasks**
- [ ] Create the spec (brainstorming) and enter its milestones

**Acceptance criteria**
- [ ] The spec is approved and linked here

---

# Subproject 7 – Raffica Slides

**Spec:** not written yet (created in its first milestone)

## Milestone 6 – Create spec for Raffica Slides
**Status:** ⬜ open · **Plan:** – · **Prerequisites:** M4

Goal: The scope of Slides, reusing the text layout of Write, is written down.

**Tasks**
- [ ] Create the spec (brainstorming) and enter its milestones

**Acceptance criteria**
- [ ] The spec is approved and linked here

## Backlog

Deliberately not planned. Ideas that `/ideen` puts here are listed; if needed, one becomes a milestone.

- **ODF and legacy Microsoft formats:** Support for ODF (.odt, .ods, .odp) and the old .doc, .xls and .ppt formats, after the main apps are built.
- **Clean up references to Pagewing documents and translate the fixture catalogue:** About 68 imported files cite Pagewing's German decision-log entries or `packaging/README.md` in comments; the references point at documents that are not in this repo. `testdata/pdfs/LICENSES.md` is still German and describes fixtures generated by `tools/make-testpdfs.cs`, which was not imported. Replace the references with Raffica entries or drop them, translate the catalogue, and decide whether to import or rebuild the test PDF generator.
- **Macros and extensions:** A macro and extension system (a VBA replacement), someday, not for the beginning.
- **Real-time collaboration:** Several people editing one document at the same time, someday, not for the beginning.
- **Automatic compact command bar below a window width:** The full ribbon at 1024 px without the compact bar clips groups of the Home tab; compact mode is a user toggle today. Decide whether the frame switches to compact automatically below a width (a scope change for the frame).
- **What the field-code group numbers should do:** In field-code mode the group digits are shown as a legend only; Q, 6 and 7 act. Decide whether typing a group digit should do something (for example jump to the group).
- **German wording for "System" in the settings dialog:** The German label "Wie System" (`SettingsThemeSystem`, the follow-the-system theme choice) needs the owner's decision on the wording.
- **Ctrl+K and hyperlinks in Write:** The frame binds Ctrl+K to the search field. Word and other editors use Ctrl+K for inserting a hyperlink; settle this in the Write spec (note for the Write spec).
- **Accessibility items deferred from the frame:** Not done in milestone 1.2: the selected state of ribbon and document tabs is not exposed to assistive technology (only a style class), the search results list has no selection semantics, and a document tab has two tab stops (tab and close button).
- **Option key handling on macOS:** A bare Option tap enters field-code mode and takes the next letter; there is no platform gating. Decide the macOS behaviour when the macOS subproject is specified, after checking it on a Mac.
- **Tabs between windows and open in a new window:** Pagewing 1.0 let a tab be moved to its own window (menu "Open in new window", M4) and dragged between windows (M4.1, Windows only). Raffica PDF 2.1 has one window with tabs; the frame's `DocumentTabs` has no drag-out. Revisit after 2.6 or when the owner asks.
- **Setting "reduce animations":** Pagewing 1.0 had it in the settings (M6, because Avalonia had no system setting for it). Raffica's shell has no such setting; add it as an extra row of the settings dialog or as a shell setting once the shell has animations that matter.
