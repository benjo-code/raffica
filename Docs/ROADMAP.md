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
| 1.2 | App frame | 2 – Design system and shell | ⬜ open | – | – |
| 2 | Create spec for Raffica PDF | 3 – Raffica PDF | ⬜ open | – | – |
| 3 | Create spec for macOS | 4 – macOS | ⬜ open | – | – |
| 4 | Spikes & spec for Raffica Write | 5 – Raffica Write | ⬜ open | – | – |
| 5 | Create spec for Raffica Sheets | 6 – Raffica Sheets | ⬜ open | – | – |
| 6 | Create spec for Raffica Slides | 7 – Raffica Slides | ⬜ open | – | – |

## Scope changes

- **2026-11-03 (Milestone 0):** M0 includes the import of the Pagewing core (no UI) from the `v1.0.0` tag as the project skeleton: unchanged import, scripted rename, a commit that makes the subset self-contained, and the translation of German text. The listed tasks for the skeleton, `.gitignore` and CI are carried out on top of the imported solution. `.env.example` is not needed because the imported code uses no secrets. Decided in the suite spec and in `Docs/DECISIONS.md` (entry "Core-only import, fresh UI, new order of work").
- **2026-11-03 (Milestone 1.1):** Localized key names in menu shortcuts (`Strg+X` instead of `Ctrl+X`) and the TextBox context menu strings (Fluent's English Cut, Copy and Paste) move to milestone 1.2 with the German layout work. The gallery window is 1520 x 900. Decisions in `Docs/DECISIONS.md`, entries "Shell project and token conventions (M1.1)" and "Localization mechanism and its limits". The high-contrast palette is a fixed palette, not a token set built from the system colours as the spec asks (entry "High-contrast palette"); owner to confirm.

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
**Status:** ⬜ open · **Plan:** – · **Prerequisites:** M1.1

Goal: The shared app frame works in the gallery app with every behaviour of the spec, within the measured speed limits.

**Tasks**
- [ ] Title bar with document tabs and search (Ctrl+K), ribbon with compact mode, side panels, status bar
- [ ] Field codes (Office-style sequence) and F6 pane focus
- [ ] Start view and settings dialog (theme, language after restart, compact command bar, autosave)
- [ ] Size check in CI and a German layout test at 1440 px and at the compact width

**Acceptance criteria**
- [ ] Tests cover every field-code step, the AltGr case, duplicate codes and F6 navigation
- [ ] The German layout test finds no clipped ribbon label
- [ ] A `measure.yml` run on the Windows and Linux machines stays within the limits recorded in milestone 1

---

# Subproject 3 – Raffica PDF

**Spec:** not written yet (created in its first milestone)

## Milestone 2 – Create spec for Raffica PDF
**Status:** ⬜ open · **Plan:** – · **Prerequisites:** M1

Goal: The scope and the milestones of the PDF app on the new UI and the imported core are written down.

**Tasks**
- [ ] Create the spec (brainstorming) and enter its milestones

**Acceptance criteria**
- [ ] The spec is approved and linked here

---

# Subproject 4 – macOS

**Spec:** not written yet (created in its first milestone)

## Milestone 3 – Create spec for macOS
**Status:** ⬜ open · **Plan:** – · **Prerequisites:** M2

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
- **Build the OCR library in this repository:** `tools/native-fetch.cs` fetches the Tesseract library from a pinned prerelease of the Pagewing repository. Build it here (workflow and build scripts) before the suite ships OCR, and put the CodeQL, release and hardware-measure workflows back where they are needed.
- **Macros and extensions:** A macro and extension system (a VBA replacement), someday, not for the beginning.
- **Real-time collaboration:** Several people editing one document at the same time, someday, not for the beginning.
