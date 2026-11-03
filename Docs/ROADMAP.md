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
| 0 | Repo skeleton & CI | 1 – Foundation | 🟡 planned | [Plan](superpowers/plans/2026-11-03-milestone-00-repo-skeleton.md) | – |
| 1 | Design directions & spec | 2 – Design system and shell | ⬜ open | – | – |
| 2 | Create spec for Raffica PDF | 3 – Raffica PDF | ⬜ open | – | – |
| 3 | Create spec for macOS | 4 – macOS | ⬜ open | – | – |
| 4 | Spikes & spec for Raffica Write | 5 – Raffica Write | ⬜ open | – | – |
| 5 | Create spec for Raffica Sheets | 6 – Raffica Sheets | ⬜ open | – | – |
| 6 | Create spec for Raffica Slides | 7 – Raffica Slides | ⬜ open | – | – |

## Scope changes

- **2026-11-03 (Milestone 0):** M0 includes the import of the Pagewing core (no UI) from the `v1.0.0` tag as the project skeleton: unchanged import, scripted rename, a commit that makes the subset self-contained, and the translation of German text. The listed tasks for the skeleton, `.gitignore` and CI are carried out on top of the imported solution. `.env.example` is not needed because the imported code uses no secrets. Decided in the suite spec and in `Docs/DECISIONS.md` (entry "Core-only import, fresh UI, new order of work").

---

# Subproject 1 – Foundation

**Spec:** [2026-11-03-raffica-suite-design.md](superpowers/specs/2026-11-03-raffica-suite-design.md) Suite design, approved 2026-11-03

## Milestone 0 – Repo skeleton & CI
**Status:** 🟡 planned · **Plan:** [2026-11-03-milestone-00-repo-skeleton.md](superpowers/plans/2026-11-03-milestone-00-repo-skeleton.md) · **Spec:** [2026-11-03-raffica-suite-design.md](superpowers/specs/2026-11-03-raffica-suite-design.md) · **Prerequisites:** none

Goal: A set-up repo with the renamed Pagewing core, tests and CI on which all further milestones build.

**Tasks**
- [x] Create the spec (brainstorming): goal, scope, stack, subprojects; link it here and enter the first milestones
- [x] Repo setup done: `dev` as default branch, merge options, branch protection for `main` and `dev`
- [x] Fill in the project header of `CLAUDE.md` and `README.md`, remove the template section from the README
- [ ] Import commit 1: the Pagewing core (`Core`, `Pdfium`, `Ocr`, `Cli`), its tests, test data and licence files from the `v1.0.0` tag, unchanged, with a stack-specific `.gitignore`
- [ ] Import commit 2: scripted rename (dry run first), updater keys and URLs replaced by documented placeholders
- [ ] Make the subset self-contained and green: solution file, removal of App-coupled tests, OCR library fetch script, `.editorconfig`; project skeleton incl. test projects
- [ ] Translate the imported German comments, strings and test names to English
- [ ] `.env.example` with placeholders, if secrets are needed (not needed, see scope change)
- [ ] CI (GitHub Actions): build, test and format/lint on PRs to `dev` and `main`; the job `roadmap` from `.github/workflows/roadmap.yml` stays; aggregate check `ci`
- [ ] Record the build, test and format/lint commands in `CLAUDE.md` and write the README setup section
- [ ] Enter the CI jobs (`roadmap` and `ci`) as required status checks in the branch protection of `main` and `dev` (only on the owner's explicit instruction)
- [ ] Dependabot for the package ecosystem and GitHub Actions

**Acceptance criteria**
- [ ] Before the rename, every imported file is byte-identical to the `v1.0.0` tag (`diff -r` against a `git archive` of the same paths)
- [ ] No reference to Pagewing remains outside the allowlist (test `NoPagewingLeftoverTests`), and the updater trusts no Pagewing key or URL
- [ ] Build, test and format/lint run green locally (commands in `CLAUDE.md`), with no skipped OCR tests
- [ ] CI is green on a PR to `dev`
- [ ] Branch protection for `main` and `dev` is active: PR required, CI check required, no force push, no deletion

---

# Subproject 2 – Design system and shell

**Spec:** not written yet (created in its first milestone)

## Milestone 1 – Design directions & spec
**Status:** ⬜ open · **Plan:** – · **Prerequisites:** M0

Goal: One design direction for all four apps is chosen and the spec for the design system and shell is written.

**Tasks**
- [ ] Measure cold start, memory and size of a Native AOT Avalonia app on Windows, Linux and macOS (sets the speed targets)
- [ ] Create at least three design directions with Impeccable as Claude artifacts that show Write, Sheets, Slides and PDF; the owner chooses one
- [ ] Create the spec (brainstorming): tokens, components, app frame, localization infrastructure (English/German), WCAG contrast of every accent

**Acceptance criteria**
- [ ] The measured values are recorded and the speed targets are set from them
- [ ] A design direction is chosen by the owner and recorded in `Docs/DECISIONS.md`

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
- **Macros and extensions:** A macro and extension system (a VBA replacement), someday, not for the beginning.
- **Real-time collaboration:** Several people editing one document at the same time, someday, not for the beginning.
