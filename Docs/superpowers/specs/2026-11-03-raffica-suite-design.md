# Raffica Office – Suite Design

Status: draft for approval · Date: 2026-11-03 · Author: brainstorming session (M0)

Items marked **(call)** or "author's call" are decisions made by the author of this document, not by the project owner. The owner should check them.

## 1. Intent

Raffica is a fast, open-source office suite for Windows, Linux and macOS: **Raffica Office** with **Write** (word processor), **Sheets** (spreadsheet), **Slides** (presentations) and **PDF** (viewer/editor). It grows out of the finished PDF editor Pagewing (`v1.0.0`).

The owner uses spreadsheets most but wants to build Write first, as the Word alternative. The target user is "me and people like me": a person who works with documents daily and wants a fast, tidy tool, not an enterprise administrator.

Why it should exist, from the owner and from research (patterns from forums and reviews, not measurements):
- LibreOffice is feature-rich, but users report slow start, a cluttered UI with overlapping modes (seven under View > User Interface), hard-to-find commands and uneven DOCX/XLSX/PPTX rendering.
- Microsoft Office is well organised but heavy, pushes AI and subscriptions, and its styles are hard to use.
- WPS has good fidelity but ads, nagging and cancellation complaints.
- New Rust suites exist but are early and unproven on real files.

## 2. Goals

1. **Speed is the core promise:** cold start, opening a file and scrolling are fast. Targets are set from a measurement of a Native AOT Avalonia app on all three operating systems (see section 6), not invented.
2. **Feature depth:** over the long run, about as many features as LibreOffice and Microsoft Office. This is a direction, not a promise of the first release.
3. **Structured UI:** commands are as easy to find as in Microsoft's UI (grouped by task), with none of LibreOffice's clutter. One shared design system (tokens, typography, spacing, components) with one accent color per app. Every accent is checked for WCAG contrast in the light and dark theme.
4. **Compatibility:** DOCX, XLSX and PPTX open and look right. "As compatible as possible" is made measurable with a corpus of real files compared against Word, Excel and PowerPoint output, kept in the repo (small files) or described in the docs (large ones).
5. **Languages:** English and German from the first release. English is the source language of the UI.
6. **Platforms:** Windows, Linux, macOS. macOS is its own milestone after the design system.

## 3. Non-goals and anti-goals

Non-goals for now (backlog, "someday"):
- ODF (.odt/.ods/.odp) and the legacy .doc/.xls/.ppt formats
- Macros and extensions (a VBA replacement)
- Real-time collaboration and any cloud service
- Mobile and web versions

Anti-goals, i.e. what would make Raffica a failure even if it technically works:
1. No ads, nag screens or upsell prompts, no subscription-only features.
2. No telemetry without opt-in, and no account needed to open, save or edit.
3. Any AI feature is optional and off until the user turns it on.
4. No silent changes to user data: no automatic date or number conversion on import, and import options are shown up front.
5. One UI structure, not several competing UI modes.
6. Autosave and crash recovery are on by default and local.
7. Compatibility claims are measured, not asserted.

## 4. Decisions

| Topic | Decision | Source |
|---|---|---|
| Name and structure | Brand Raffica, suite Raffica Office, apps Write, Sheets, Slides, PDF. One monorepo, one installer for the suite, apps start individually. | owner (see `Docs/DECISIONS.md`) |
| License | GPL-3.0-or-later | owner |
| Language of docs and code | English. UI source language English, German localization from the first release. | owner |
| Order of work | Foundation (M0: import of the Pagewing core) → shared design system and shell → Raffica PDF on the new UI → macOS → Write → Sheets and Slides | owner |
| UI | Built completely fresh. No Pagewing UI code is imported. | owner |
| Design process | Impeccable creates at least three complete design directions as Claude artifacts that show all apps (Write, Sheets, Slides, PDF), not only PDF. The owner picks one before the design system is implemented. | owner |
| Shell stack | .NET 10 (LTS), Native AOT, Avalonia 12, PDFium | owner, after the research in section 7 |
| Write's layout engine | Document model and layout live in a library with no UI dependency. Whether it is C# or a Rust core behind a C interface is decided in the Write spec, after the spikes in section 6. | owner |
| Sheets formula engine | Compare IronCalc (Rust, MIT/Apache) with an own engine in the Sheets spec. | author (call) |
| Design | Impeccable skill for direction and critique; tokens implemented by hand in the shared shell project (AXAML). | owner (`CLAUDE.md`) |
| macOS | Ad-hoc signing only, no notarization, distribution as `.dmg`, first launch documented in the README. | owner (`CLAUDE.md`) |

### License guardrails for dependencies (author's call, based on the research)
Not allowed: EPPlus 5 and later (Polyform Noncommercial), DocX/Xceed (non-commercial), NPOI binaries from 2.8.0 on (EULA with a maintenance fee; building from source needs a separate legal check), Avalonia Pro controls such as the RichTextEditor, and any proprietary document library (Aspose, Syncfusion, Spire, GemBox, FlexCel). MuPDF is AGPL and is avoided. Preferred: MIT, Apache-2.0, BSD. Every new dependency gets a license check before it is added.

## 5. Subprojects and order (author's call, to be refined in each subproject's spec)

1. **Foundation** – repo skeleton, import of the Pagewing core, CI. This spec belongs here.
2. **Design system and shell** – at least three design directions made with Impeccable and shown as artifacts for all four apps; the owner chooses one; then shared tokens, components, app frame, localization infrastructure (English/German).
3. **Raffica PDF** – a new UI on the imported core, the first app on the new shell.
4. **macOS** – build, ad-hoc signing, `.dmg`, documented first launch.
5. **Raffica Write** – document model, layout engine, DOCX import/export, the compatibility corpus.
6. **Raffica Sheets**
7. **Raffica Slides** – reuses Write's text layout.

Each subproject starts with its own spec (brainstorming), then plans per milestone.

## 6. Milestone 0 – scope and open verification

M0 sets up the repo so that later milestones build on it. Per `CLAUDE.md` it includes the Pagewing import (author's call: the roadmap's M0 task list does not mention the import yet, so it is added as a scope change). The owner decided that M0 imports **only the core and tests**; the UI is built fresh.

- Import from the `v1.0.0` tag of Pagewing with `git archive` (tracked files only), only what is needed. Based on a read-only look at the tag, that is: `src/Pagewing.Core`, `src/Pagewing.Pdfium`, `src/Pagewing.Ocr`, `src/Pagewing.Cli` (none depends on Avalonia; the core tests reference the CLI), `tests/Pagewing.Core.Tests`, `tests/Pagewing.Render.Tests`, `tests/Shared`, `testdata`, the licence files under `third-party` for what the imported projects ship (`pdfium`, `tesseract`, `leptonica`, `tessdata-fast`, `geist`; git holds no binaries: PDFium comes from NuGet and the OCR library is fetched before the tests run), build config (`Directory.Build.props`, `Directory.Packages.props`, `global.json`, `NuGet.config`, `.editorconfig`), `LICENSE`, the CI workflows (adapted) and a rewritten README; commit 1 = unchanged import. The builder checks this list against the tag and `CLAUDE.md` and records what it leaves out (author's call).
- Not imported: `src/Pagewing.App` and `tests/Pagewing.App.Tests`, the Avalonia, ANGLE, SkiaSharp, font and icon bundles under `third-party`, the installer and AppImage packaging, old plans and docs. The benchmark folder `bench` is optional; the builder decides.
- Commit 2 = scripted rename (namespaces, AssemblyName, CLI, MSBuild properties, pipe name, desktop entry, updater URLs). The Inno Setup AppId is renamed only if the installer files are imported; it is not part of M0. Dry run first, read the list of hits. The core still contains the update checker, single-instance handling and the desktop entry, so the rename applies there.
- Define and record the build, test and format/lint commands in `CLAUDE.md`; stack-specific `.gitignore`/`.gitattributes`; `.env.example` if secrets are needed.
- CI for build, test and format/lint on PRs to `dev` and `main`, added next to the existing `roadmap` job; required status checks in the branch protection; Dependabot for NuGet and GitHub Actions.
- Merge the import branch with "Create a merge commit" so both commits survive.

Not in M0: any feature work, the design system, macOS.

### Verification that is still open
These are not answered by the research and are measured before the decisions that depend on them:
1. Cold start, memory and size of Native AOT Avalonia apps on Windows, Linux, macOS. Sets the speed targets. Pagewing's `bench/startup` and `bench/compare` folders already contain a Windows result from 2026-10-16 that has not been read yet; since the Pagewing UI is not imported, the measurement is repeated on the new shell. (Needed for the design system and shell subproject.)
2. Open XML SDK 3.x under Native AOT: open, edit and save a DOCX and an XLSX. (Needed before Write's spec.)
3. Text layout speed: shaping and laying out a long document with Avalonia/Skia/HarfBuzz versus a Rust candidate (Parley). (Needed before Write's engine decision.)
4. Printing: Avalonia core has no printing API; find out what is needed per OS. (Needed before Write.)
5. IME behaviour on Windows and Linux (open Avalonia IME issues). (Needed before Write.)

### M0 details (author's calls, set after a review of this document)

- **The import is the skeleton.** No hand-built empty skeleton. The imported solution, its test project and its build files are what CI, `.gitignore` and the build/test/lint commands are set up around. The roadmap gets the new tasks (import, rename) and a "Scope changes" entry on the milestone branch.
- **Branches and PRs.** (1) `feature/m00-repo-skeleton` (this branch): spec, plan, `Docs/DECISIONS.md` and roadmap changes, squash merge to `dev`. (2) A separate import branch, merged to `dev` with "Create a merge commit". Commit 1 is the unchanged import and commit 2 the scripted rename, exactly as `CLAUDE.md` demands; further commits follow on the same branch: 3 = make the subset self-contained and green (below), 4 = translate German text to English, and, only if `dotnet format` fails, a style-only commit. (3) A CI branch: workflows, Dependabot, then the required status checks in the branch protection (a GitHub setting: done only on the owner's explicit instruction).
- **The subset is not self-contained (found by looking at the tag).** Three test files in `Pagewing.Core.Tests` read files of `Pagewing.App` or its packaging (`TextBoxFontTests`, `PackageFormTests`, a doc reference in `AnnotationMeasurementTests.Drawings`), `Pagewing.Core.csproj` grants `InternalsVisibleTo` to the App tests, and `Pagewing.Core` embeds the Geist font. Commit 3 removes or adapts what points at the missing App and keeps `third-party/geist`. A test is removed only if it checks the App; the commit message lists each one.
- **Updater trust.** The core contains `Updates/UpdateTrust.cs` with the real Pagewing public keys and GitHub URL. The rename must not leave Pagewing's key paired with a Raffica URL: keys and URLs become documented placeholders, and the updater test build keeps working.
- **Tools.** `tools/roadmap.cs` already exists here. Of Pagewing's scripts, M0 imports only what the retained tests and CI need: `tools/make-testpdfs.cs` is not imported (the fixtures in `testdata/pdfs` stay as committed files), and the OCR library fetch (`native-fetch` inside the 5,600-line packaging script `tools/package.cs`, SHA-256 pins included) is extracted into its own small script `tools/native-fetch.cs` with the same arguments. `tools/package.cs`, `tools/licenses.cs`, `tools/smoke-cli.cs`, `tools/update-testserver.cs` and `tools/measure-open.cs` are not imported; whatever is left out is recorded in the PR (author's call).
- **German text.** The subset has about 240 lines with umlauts (21 in source, 222 in tests) and more German written without umlauts. Commit 4 translates comments, strings and test names in the imported files. Behavior-visible strings are checked against the tests that assert on them.
- **CI.** Jobs build, test and format check on Windows and Linux in M0. macOS joins in the macOS milestone. One aggregate job named `ci` is the required status check, so adding or renaming matrix jobs later never breaks branch protection; `roadmap` stays required as well. A Native AOT publish smoke job is added to CI if the imported build already has a publish step; it is not a gate otherwise.
- **Lint.** `dotnet format --verify-no-changes` with the imported `.editorconfig`, warnings reported, not suppressed. If the imported code does not pass, a separate third commit on the import branch fixes style only. Commits 1 and 2 stay as specified.
- **Verification (list above) is not part of M0.** Measurement 1 belongs to the design system and shell subproject; measurements 2 to 5 belong to the Write spec. M0 only records them as open.
- **Updater, signing key and release repo.** M0 renames the updater URLs in the core to documented placeholders (confirmed by the owner). Creating the key and a releases repo under `benjo-code` is an external action and is done with the owner's explicit instruction at the first release milestone. Acceptance for M0 does not check the updater.

## 7. Research summary (late 2026; mostly vendor claims and project READMEs, few benchmarks)

- A Word-grade layout engine (pagination, floats, tables across pages, footnotes, Word-compatible metrics) is the largest cost in any language and no free, GPL-compatible one exists in .NET or Rust. The Rust word processor WordCraft rates its own pagination at 55% and says DOCX is not tested on real files.
- Rust has stronger low-level text libraries (Parley, HarfRust, ICU4X) and IronCalc, but its GUI toolkits (GPUI, egui, iced, Xilem) are experimental. Slint is the most mature and is GPLv3-compatible, but it has no table/grid control and treats rich text as out of scope. AccessKit has no rich-text accessibility yet.
- Avalonia 12 has no built-in ribbon (third-party Dock and RibbonControl exist), no printing API, an unclear free status of its large-data grid (TreeDataGrid) and open IME bugs. Open XML SDK (MIT) has had Native AOT regressions that were fixed.
- A Rust core called from .NET is feasible but costly for layout, because hit-testing and caret queries are fine-grained. Coarse interfaces such as a formula engine fit much better.

## 8. Left to the builder

- Exact structure of the solution (project names, folders), as long as the shared shell, per-app projects and engine libraries stay separate.
- Choice of test framework and formatter/linter for .NET, recorded in `CLAUDE.md`.
- CI matrix details (runners, caching), noting that Native AOT cannot cross-compile between operating systems, so Windows, Linux and macOS jobs need native runners.
- Which Pagewing files are "strictly needed", using the rule in `CLAUDE.md`. This includes the Pagewing `tools/` scripts, `third-party/licenses.json`, `CONTRIBUTING.md`, `SECURITY.md` and the workflows other than `ci.yml` (`codeql`, `release`, `hardware-measure`, `native-tesseract`): none of them is decided here, and anything left out is recorded.
