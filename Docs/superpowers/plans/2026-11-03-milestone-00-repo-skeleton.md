# Milestone 0 – Repo Skeleton & CI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Import the Pagewing core (no UI) into this repo as a renamed, English-only, green-building base with CI that gates PRs to `dev` and `main`.

**Architecture:** Four Pagewing projects (`Core`, `Pdfium`, `Ocr`, `Cli`) plus two test projects and test data are exported from the `v1.0.0` tag, renamed by script, made self-contained, and put under one solution. CI builds, tests, format-checks and AOT-publishes the CLI on Windows and Linux, with one aggregate required check `ci`.

**Tech Stack:** .NET 10 (SDK `10.0.100`, `rollForward: latestFeature`), Native AOT, xunit.v3 on Microsoft.Testing.Platform, PDFium via NuGet (`bblanchon.PDFium.*`), GitHub Actions.

**Spec:** [Docs/superpowers/specs/2026-11-03-raffica-suite-design.md](../specs/2026-11-03-raffica-suite-design.md), section 6 ("Milestone 0"). Pagewing checkout (read-only): `C:\VS-Code\Pagewing`, tag `v1.0.0`.

## Global Constraints

- Everything in English: docs, code, comments, commits, PRs. German localization of the UI is not part of M0.
- License GPL-3.0-or-later; every new dependency gets a license check first; forbidden: EPPlus 5+, DocX/Xceed, NPOI binaries, Avalonia Pro, Aspose/Syncfusion/Spire/GemBox/FlexCel, MuPDF.
- Never modify the Pagewing checkout; export with `git archive` only, never copy the working folder.
- Commit with the GitHub noreply address only; Conventional Commits; stage files by name (no `git add .`/`-A`); after each commit run `git show --stat HEAD` and compare with the expected files; no force push, no `--no-verify`, no history rewrite, no branch deletion.
- Import branch merges with "Create a merge commit"; feature branches merge to `dev` by PR (squash allowed) once CI is green. Never commit to `main` or `dev`.
- No secrets in files, commits, logs or PRs. Changing GitHub settings (branch protection) only on the owner's explicit instruction.
- Do not suppress warnings, skip tests or weaken checks to get green (`TreatWarningsAsErrors` stays on).
- Commit trailer: `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`; PR bodies end with `🤖 Generated with [Claude Code](https://claude.com/claude-code)`.

## Review Focus

- **Updater trust:** the core's `Updates/UpdateTrust.cs` holds Pagewing's real public keys and GitHub URL; after the rename a Raffica build must not trust Pagewing's key or point at Pagewing's releases.
- **Rename vs. binary and hashed files:** PDF fixtures, fonts and SHA-pinned data must stay byte-identical to the tag; only text files are rewritten.
- **Line endings:** `.gitattributes` forces LF; text fixtures (`.tsv`, `.txt`) must give the same test results on a Windows and a Linux checkout.
- **OCR library absent:** tests that need it must fail in CI (`RAFFICA_REQUIRE_OCR=1`), not report themselves as skipped.
- **Translation changing behavior:** strings that tests or the CLI output assert on must still match after translation.

---

### Task 1: Import commit 1 (unchanged subset)

**Files:** Create (from the tag, byte-identical): `.gitignore`, `Directory.Build.props`, `Directory.Packages.props`, `global.json`, `NuGet.config`, `LICENSE`, `src/Pagewing.Core`, `src/Pagewing.Pdfium`, `src/Pagewing.Ocr`, `src/Pagewing.Cli`, `tests/Pagewing.Core.Tests`, `tests/Pagewing.Render.Tests`, `tests/Shared`, `testdata`, `third-party/{pdfium,tesseract,leptonica,tessdata-fast,geist}`. Never overwritten: `README.md`, `CHANGELOG.md`, `CLAUDE.md`, `.editorconfig`, `.gitattributes`, `tools/roadmap.cs`, `.github/workflows/roadmap.yml`. Not imported (task 5 or never): `*.slnx`, `.github/workflows/ci.yml`, `bench`, `packaging`, `src/Pagewing.App`, `tests/Pagewing.App.Tests`, other `tools/*`, `Docs`.

**Interfaces:** Produces the import branch `feature/m00-import`, cut from `dev` after the planning PR (this branch) is merged.

- [ ] **Step 1:** `git switch dev; git pull; git switch -c feature/m00-import`.
- [ ] **Step 2:** Export the paths above into the repo root: `git -C C:\VS-Code\Pagewing archive --format=tar v1.0.0 <paths> | tar -x -C C:\VS-Code\raffica`.
- [ ] **Step 3:** Verify byte-identity: archive the same paths into an empty temp directory and run `diff -r <temp> <path in raffica>` for each imported path. Expected: no output.
- [ ] **Step 4:** Verify the subset builds on its own: `dotnet build src/Pagewing.Cli -c Release` (no solution exists yet). Expected: `Build succeeded`, 0 warnings.
- [ ] **Step 5:** Stage the imported paths by name; commit `chore: import the Pagewing core from v1.0.0 (unchanged)`; check `git show --stat HEAD`.

### Task 2: Scripted rename (commit 2)

**Files:** a one-off script `rename.cs` in the scratchpad directory (not committed); modifies the imported files; renames files and folders containing `Pagewing`.

**Interfaces:** `dotnet run rename.cs -- <repo-root> [--apply]` prints a dry-run list (path, rule, count) and changes nothing without `--apply`. Rules, applied in this order to text files only (skip files with a NUL byte and the extensions in `.gitattributes` binary list): `PAGEWING_` → `RAFFICA_`, `Pagewing` → `Raffica`, `pagewing` → `raffica`; the same rules for file and folder names. Skip `.git/`.

- [ ] **Step 1:** Write the script. Behavior to pin: a dry run lists every hit and writes nothing; `--apply` is idempotent (a second run reports zero hits); a binary file is never rewritten.
- [ ] **Step 2:** Run the dry run, read the whole hit list and keep it for the PR description (not a committed file). Check by eye the hits in `Updates/UpdateTrust.cs`, `Hosting/SingleInstance.cs`, `Distribution/DesktopEntry.cs`, `Directory.Build.props` (update test-build properties) and `Core.csproj`.
- [ ] **Step 3:** Run with `--apply`.
- [ ] **Step 4:** In `src/Raffica.Core/Updates/UpdateTrust.cs`: replace Pagewing's public keys and release URL with documented placeholders (an obviously invalid key value and a URL under the reserved `.invalid` domain), with a comment that the real key and releases repo are created at the first release milestone on the owner's instruction. Update tests that assert the old values.
- [ ] **Step 5:** Verify: `git grep -il pagewing` (excluding `.git`) lists nothing outside an explicit allowlist (provenance notes only, e.g. one line in `third-party` READMEs); verify binary fixtures unchanged by repeating the byte-identity check from Task 1 for `testdata/**/*.pdf`, `*.ttf` and `third-party` binaries. `dotnet build src/Raffica.Cli -c Release` succeeds.
- [ ] **Step 6:** Commit `chore: rename Pagewing to Raffica (scripted)`; check `git show --stat HEAD`.

### Task 3: Make the subset self-contained and green (commit 3)

**Files:** Create `Raffica.slnx`, `tools/native-fetch.cs`; Modify `src/Raffica.Core/Raffica.Core.csproj`, the three App-coupled test files, `.editorconfig`, `tests/Raffica.Core.Tests/` (new `NoPagewingLeftoverTests.cs`).

**Interfaces:** `dotnet run tools/native-fetch.cs -- --rid <win-x64|linux-x64> --out <dir>` downloads the OCR library and language data with the same SHA-256 pins as `tools/package.cs native-fetch` in the Pagewing tag (copied verbatim); exits 2 for an unknown RID. Tests find the library in `artifacts/native/<rid>` or `RAFFICA_TESSERACT_DIR`.

- [ ] **Step 1:** Create `Raffica.slnx` with folders `/src/` (Cli, Core, Ocr, Pdfium) and `/tests/` (Core.Tests, Render.Tests). Run `dotnet build Raffica.slnx -c Release`; fix what fails.
- [ ] **Step 2:** Remove `InternalsVisibleTo Include="Raffica.App.Tests"` and the stale comment from `Raffica.Core.csproj`. Remove or adapt the tests that read `Raffica.App` files (`Annotations/TextBoxFontTests`, `Updates/PackageFormTests`; the doc comment in `Annotations/AnnotationMeasurementTests.Drawings`). Remove a test only if it checks the missing App; list each removed test and the reason in the commit message.
- [ ] **Step 3:** Write `tools/native-fetch.cs` from the Pagewing tag's `tools/package.cs` (`NativeFetch` part only, no other packaging code). Run it for the local RID, then confirm the OCR tests run and are not skipped: `RAFFICA_REQUIRE_OCR=1 dotnet test Raffica.slnx -c Release --max-parallel-test-modules 1`.
- [ ] **Step 4:** Write the failing test `NoPagewingLeftoverTests.Text_files_do_not_mention_Pagewing` in `tests/Raffica.Core.Tests`: scans text files under the repo root (skipping `.git`, `bin`, `obj`, `artifacts`) and asserts the list of files containing "pagewing" (case-insensitive) equals the allowlist from Task 2. Run it; it must fail first if any leftover remains, then pass.
- [ ] **Step 5:** Merge Pagewing's analyzer settings into the existing `.editorconfig`: take the rules from `git show v1.0.0:.editorconfig` that the retained code needs, keep this repo's header and indentation sections. `.gitattributes` needs no additions (check with `diff` that its rules equal `git show v1.0.0:.gitattributes` apart from comments). Run `dotnet format Raffica.slnx --verify-no-changes`. If it fails, fix style only in a separate commit `style: apply dotnet format`.
- [ ] **Step 6:** Run `dotnet build Raffica.slnx -c Release`, `RAFFICA_REQUIRE_OCR=1 dotnet test Raffica.slnx -c Release --no-build --max-parallel-test-modules 1`, `dotnet format Raffica.slnx --verify-no-changes`. Expected: all green, 0 skipped OCR tests.
- [ ] **Step 7:** Commit `chore: make the imported core self-contained`; check `git show --stat HEAD`.

### Task 4: Translate German text (commit 4)

**Files:** Modify imported source, test and config files containing German; no behavior changes.

- [ ] **Step 1:** List candidates: `git grep -lP "[äöüßÄÖÜ]"` plus a word heuristic for German written without umlauts (`git grep -liwE "und|nicht|oder|für|prüfen|pruefen|wird|werden|ein|eine"` filtered by eye to non-English hits). Save the list in the PR description.
- [ ] **Step 2:** Translate comments, doc comments, string literals and test names/display names to English. For every string literal, search the tests and `Raffica.Cli` output for assertions on it and keep both sides equal.
- [ ] **Step 3:** Verify: the first grep returns no hits except fixture data that must stay byte-identical (list them); build, test and format commands from Task 3 Step 6 are green.
- [ ] **Step 4:** Commit `docs: translate imported German comments and strings to English`; check `git show --stat HEAD`. Push `feature/m00-import` and open the PR to `dev`. The new CI does not exist yet, so put the local results of Task 3 Step 6 and the hit lists of Tasks 2 and 4 into the PR description; the `roadmap` check must be green. Merge with **"Create a merge commit"** so all commits survive.

### Task 5: CI, Dependabot, documented commands (CI branch)

**Files:** Create `.github/workflows/ci.yml`, `.github/dependabot.yml`; Modify `CLAUDE.md` (Build/Test/Format lines), `README.md` (Setup section), `Docs/DECISIONS.md` (CI trust model entry if it differs from Pagewing's).

**Interfaces:** Required status checks after this task: `ci` and `roadmap`. Job `build` is a matrix (`linux` on `ubuntu-24.04`, `windows` on `windows-2025`); job `ci` has `needs: [build]`, `if: always()` and fails unless `needs.build.result == 'success'`.

- [ ] **Step 1:** Cut `feature/m00-ci` from `dev` (after Task 4 is merged). Write `ci.yml` from `git show v1.0.0:.github/workflows/ci.yml`, keeping the pinned action SHAs, `permissions: contents: read`, concurrency, DOTNET_INSTALL_DIR steps and the Native AOT prerequisite checks; keep: restore, `dotnet run tools/native-fetch.cs`, build, test (`RAFFICA_REQUIRE_OCR: '1'`, `--max-parallel-test-modules 1`), render diff upload, format check, CLI Native AOT publish plus a smoke step (run the published binary with `--help` and `info testdata/pdfs/text-basic.pdf`, expect exit code 0). Drop: App publish, startup checks, packaging, licence tool, update test server, glibc check, test-PDF generator check, Avalonia telemetry variable. PR runs go to GitHub-hosted runners (Pagewing's trust model, author's call).
- [ ] **Step 2:** Write `.github/dependabot.yml` for `nuget` and `github-actions`, weekly, directory `/`.
- [ ] **Step 3:** Put build, test and format commands into `CLAUDE.md` (replace the three placeholders), write the README Setup section (SDK 10, `dotnet run tools/native-fetch.cs -- ...`, build, test); update the README status line.
- [ ] **Step 4:** Open the PR to `dev`; expected: jobs `build-linux`, `build-windows`, `ci` and `roadmap` green. Fix failures at the cause; no skipping.
- [ ] **Step 5:** Ask the owner for explicit permission, then set required checks `ci` and `roadmap` on `main` and `dev`: `gh api` call on `repos/benjo-code/raffica/branches/<branch>/protection`. Verify with `gh api repos/benjo-code/raffica/branches/dev/protection` and `.../main/protection` (PR required, checks required, no force push, no deletion).
- [ ] **Step 6:** Squash-merge the PR once green.

### Task 6: Roadmap close-out

**Files:** Modify `Docs/roadmap.json`, regenerate `Docs/ROADMAP.md`.

- [ ] **Step 1:** Tick every finished task right away; tick each acceptance criterion only with its evidence (command and result): local build/test/format output, the CI run URL, the `gh api` protection output. Mark `.env.example` as not needed (the imported code uses no secrets) in the milestone notes instead of ticking it.
- [ ] **Step 2:** Set status `done` with `completedOn`; run `dotnet run tools/roadmap.cs -- generate` and `dotnet run tools/roadmap.cs -- check`; expected: valid and up to date.
- [ ] **Step 3:** PR to `dev`, merge when CI is green.
