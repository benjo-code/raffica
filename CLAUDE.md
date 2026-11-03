# Raffica

Raffica is a fast, open-source office suite for Windows, Linux and macOS, growing out of the PDF editor Pagewing: Raffica Office with Raffica Write, Sheets, Slides and PDF. Speed (startup, opening, scrolling) is the core promise; the suite should be as compatible as possible with DOCX/XLSX/PPTX. The suite is built step by step, starting with the PDF app (today's Pagewing core).

- **Stack:** .NET 10 (LTS) with Native AOT · Avalonia 12 for the shell · PDFium; the layout engine of Write is decided in the Write spec (see [Docs/DECISIONS.md](Docs/DECISIONS.md))
- **License:** GPL-3.0-or-later (as in Pagewing, see `LICENSE`)
- **Language:** everything in English: docs, README, commits, issues, PRs, code and comments · UI source language English, German localization from the first release
- **Build:** `dotnet build Raffica.slnx -c Release` · **Test:** once `dotnet run tools/native-fetch.cs -- --rid <win-x64|linux-x64> --out artifacts/native/<rid>` (OCR library), then `dotnet test Raffica.slnx -c Release --no-build --max-parallel-test-modules 1` with the environment variable `RAFFICA_REQUIRE_OCR=1` (a missing OCR library then fails the tests instead of skipping them) · **Format/Lint:** `dotnet format Raffica.slnx --verify-no-changes`
- **Roadmap:** [Docs/ROADMAP.md](Docs/ROADMAP.md) (generated, source [Docs/roadmap.json](Docs/roadmap.json), see "Maintaining the roadmap")
- **Decisions:** [Docs/DECISIONS.md](Docs/DECISIONS.md)
- **Specs:** `Docs/superpowers/specs/` · **Plans:** `Docs/superpowers/plans/`

## Project-specific

### Origin and migration from Pagewing

- Pagewing (`benjo-code/Pagewing`) is the predecessor and stays untouched. Its code is imported into this repo in M0 without git history; Pagewing is the read-only reference. Never modify the Pagewing checkout.
- Import only what is strictly needed: core/engine code, tests, build config, CI, LICENSE, README (rewritten). Do not import old plans, old docs or Pagewing's `DECISIONS.md` wholesale; write a new, short `Docs/DECISIONS.md` with only the decisions that still apply. Docs that are carried over are translated to English.
- Export the import from the `v1.0.0` tag of Pagewing (tracked files only, e.g. `git archive`), never copy the working folder (it contains build output).
- Order of the import: commit 1 = unchanged import, commit 2 = scripted rename (namespaces, AssemblyName, CLI, MSBuild properties, pipe name, desktop file, updater URLs, Inno Setup AppId). Run the rename script as a dry run first and read the list of hits. The Inno Setup AppId needs a new GUID and the updater needs its own signing key and URLs.
- The import branch must be merged with "Create a merge commit" so the two commits survive.

### Identity

- Commit only with the GitHub noreply address (`git config user.email`). The setting "Block command line pushes that expose my email" is enabled on the account.

### Design

- The UI is designed from scratch, one shared design system (tokens, typography, spacing, components) with one accent color per app. Check the contrast of every accent in the light and dark theme (WCAG).
- Use the Impeccable skill (`/impeccable init` creates `PRODUCT.md` and `DESIGN.md`) for direction and critique. Impeccable targets web frontends, so its tokens are implemented by hand in the shared shell project (AXAML).

### macOS

- No paid Apple account: no notarization, no Developer ID. Ad-hoc signing only (required on Apple Silicon; sign again after assembling the `.app` bundle). The README documents the first launch ("Open Anyway" in System Settings → Privacy & Security).

### Shell

- **Where:** `src/Raffica.Shell/Styles` holds `Tokens.axaml` (light and dark), `Tokens.HighContrast.axaml`, `Metrics.axaml`, `Typography.axaml`, `Fonts.axaml`, `Icons.axaml`, `Focus.axaml` and `Controls/*` (one file per control). `src/Raffica.Gallery` is the gallery app, `tests/Raffica.Shell.Tests` the shell tests.
- **Keys:** colours are `Raffica.Color.<Group>.<Name>`, brushes `Raffica.Brush.*`. Component styles use these semantic tokens only; hex literals in component styles fail `NoHexInComponentStylesTests`. A new colour pairing must be classified in the contrast tests (`EveryTokenIsClassified`).
- **Theme:** `ShellTheme.Apply` sets theme mode, app ink and high contrast. Fluent stays underneath for controls that are not restyled.
- **Strings:** English and German live in `Strings.en.resx` and `Strings.de.resx`, both embedded in the assembly; a missing key throws. `TranslationTests` finds every `Strings.en.resx` under `src/` and requires a German value for each key, so every new string needs both files.
- **Gallery switches:** `--lang en|de`, `--theme`, `--high-contrast`, `--ink`, `--section`, `--screenshot`, `--open-menu`, `--open-dialog` and `--print-string <key>` (prints one string and exits, used by the CI smoke test). The gallery tests run headless and need no display; screenshots need a real display.

---

<!-- From here on: general rules from benjo-code/project-template, translated to English for this repo. Keep them the same across projects; improvements are maintained in the template and carried over to the projects. -->

## Maintaining the roadmap

The source of truth is `Docs/roadmap.json` (schema: `Docs/roadmap.schema.json`). `Docs/ROADMAP.md` is generated from it and never edited by hand. The .NET 10 SDK is required, even if the project otherwise uses a different stack.

1. Edit only `Docs/roadmap.json`: status, plan link, tick tasks and criteria (`"done": true`, proof in `"evidence"`), scope changes, backlog. Leave optional fields out, never set them to `null`.
2. Then run `dotnet run tools/roadmap.cs -- generate` and commit both files. Errors name the path in the JSON (for example `subprojects[0].milestones[2].completedOn`).
3. `dotnet run tools/roadmap.cs -- check` validates the schema and references and checks that `ROADMAP.md` is up to date; the CI (job `roadmap`) does the same.
4. Rules: `status` is `open`, `planned`, `in-progress` or `done`; `done` requires `completedOn` (YYYY-MM-DD), from `planned` on `plan` is required (path relative to `Docs/`); milestone ids are unique strings (`"5.1"` is allowed); prerequisites and `afterMilestone` must exist; title, goal and name are a single line; texts are Markdown; whatever fits nowhere goes into `blocks` (`title`, `text`).
5. A new milestone is an entry in `subprojects[].milestones`:

```json
{
  "id": "1", "title": "<title>", "status": "open",
  "prerequisites": ["0"],
  "goal": "<One sentence: what is possible after this milestone?>",
  "tasks": [ { "text": "…", "done": false } ],
  "acceptance": [ { "text": "… (verifiable: test, command or a concrete manual check)", "done": false } ]
}
```

   A new subproject is an entry in `subprojects` (`number`, `name`, `spec` with `text` and/or `link`, `milestones`), a release is an entry in `releases` (`version`, `title`, `status`, `afterMilestone`, optional `covers`, `date`, `intro`, `acceptance`, `blocks`).
6. Ideas belong in `backlog` (`title`, `text`, optional `idea`). The skill `/ideen` (from `benjo-code/Labjo`) enters adopted ideas there and regenerates `Docs/ROADMAP.md`.

## Milestone workflow

Every change "in the roadmap" (link a plan, set a status, tick tasks, scope changes) is a change to `Docs/roadmap.json` together with the regenerated `Docs/ROADMAP.md`, see "Maintaining the roadmap".

**"Plan milestone N":**
1. Create the branch `feature/mNN-<topic>` from the current `dev`. Plan, roadmap maintenance and implementation of the milestone all go through this branch.
2. Read `Docs/ROADMAP.md`, the spec of the subproject and the current state of the code.
3. Review the project's open ideas with `/ideen` (skill from `benjo-code/Labjo`, installation: `bash tools/install-ideen-skill.sh` there): adopt, implement or discard. Whatever belongs in the milestone flows into the plan. The roadmap change lives on the milestone branch (when asked, choose "current branch").
4. If the milestone starts with "Create spec" or the subproject has no spec yet: first work out the spec with the user by brainstorming and put it under `Docs/superpowers/specs/`.
5. Write the plan to `Docs/superpowers/plans/YYYY-MM-DD-milestone-NN-<topic>.md`. The plan covers exactly the tasks and acceptance criteria of the milestone, nothing more.
6. In the roadmap, link the plan and set the status to `🟡 planned` (overview table and milestone).

**Implementation:**
- Status `🔵 in progress`. Tick every finished task in the roadmap right away.
- Tick acceptance criteria only once they have actually been checked, and note the proof behind them (test name, command + result or manual check).
- At the end, status `✅ done` with the date in the table and the milestone, then a PR to `dev` (see "Git & branches").
- Record scope changes in the roadmap under "Scope changes", not silently in the code.
- Work only within the scope of the active milestone. Do not anticipate features, packages or architecture decisions of later milestones.

## Git & branches

- `main` contains only published states (releases). `dev` is the integration branch.
- Always work on `feature/<topic>` or `fix/<topic>`, branched from the current `dev` (`git switch dev && git pull && git switch -c feature/<topic>`).
- Before any work, check `git status` and the current branch. If on `main` or `dev`, create a feature branch first. Never commit directly to `main` or `dev`.
- Feature and fix branches go to `dev` via PR. Claude merges these PRs itself once the work is finished and all CI checks are green (squash merge allowed).
- PRs to `main` exist only for releases and hotfixes and only on the explicit instruction of the user.
- PRs between `main` and `dev` (release, hotfix, returning `main` → `dev`) are always merged with **"Create a merge commit"**, never by squash or rebase. Otherwise `main` and `dev` lose their common history and every release causes conflicts.
- Hotfix: branch `hotfix/<topic>` from `main` → PR to `main` → release (see below) → then merge `main` into `dev` via PR.
- Stage selectively (`git add <file>`). No `git add .` or `git add -A` if foreign changes that do not belong to the task are in the working tree. For files that were already modified before, read `git diff <file>` first and use `git add -p` if needed.
- After every commit, check `git show --stat HEAD` against the expected file list.
- Commit messages follow Conventional Commits: `feat:`, `fix:`, `docs:`, `refactor:`, `test:`, `chore:`, `ci:`, optionally with a scope (`feat(api): …`).
- Forbidden without explicit instruction: force push, `git reset --hard`, deleting branches, rewriting history, bypassing hooks with `--no-verify`.
- Always create new repos (including companion ones like `-Releases` or `-Privacy`) in the org `benjo-code`, always with the owner spelled out (`gh repo create benjo-code/<name> …`), never under the personal account. Then check with `gh repo view benjo-code/<name>`.

## Releases

- Versioning follows SemVer (`vMAJOR.MINOR.PATCH`).
- `CHANGELOG.md` → `[Unreleased]` is extended in every PR with a change worth mentioning for users (not for pure planning, roadmap or internal cleanup PRs).
- Procedure (only on instruction):
  1. Branch `release/vX.Y.Z` from `dev`, turn `[Unreleased]` in `CHANGELOG.md` into `[X.Y.Z] – YYYY-MM-DD` (name the included milestones) and add a new, empty `[Unreleased]` above it.
  2. PR `release/vX.Y.Z` → `main` (merge commit).
  3. Tag and GitHub release in one step: `gh release create vX.Y.Z --target main --title "vX.Y.Z" --notes "<CHANGELOG section>"`.
  4. Return `main` to `dev` via PR (merge commit) so the two do not drift apart.

## Project memory

- Document anything important for the future in the repo without being asked:
  - Architecture and technology decisions, fixed versions, rejected approaches → `Docs/DECISIONS.md`
  - Scope changes → `Docs/ROADMAP.md`, section "Scope changes"
  - Lasting project knowledge (architecture, conventions, workarounds, pitfalls) → this file, section "Project-specific"
- Do not record guesses, intermediate states or temporary diagnoses as facts.
- References name the file and the type, member or section, never line numbers (they go stale).

## Quality & proof

- "Done" means: build, test and format/lint (commands above) are demonstrably green. Name the command and the result.
- `dotnet run tools/roadmap.cs -- check` is part of "done": the roadmap is valid and `Docs/ROADMAP.md` is up to date.
- Whatever could not be verified (for example because real hardware or credentials are missing) is explicitly marked as open, together with the command that checks it.
- Do not suppress warnings, skip tests or weaken checks just to make something green.

## Security

- No secrets (tokens, passwords, keys, real credentials) in files, commits, logs, PRs or replies.
- Never quote or print values from `.env` or similar, only variable names or placeholders.
- Secrets come from env vars or a secret store. Keep a placeholder in `.env.example` for every required variable. `.env` is in `.gitignore`.

## Collaboration

- Ask when requirements are unclear instead of guessing.
- Destructive actions or actions with external effect (merge to `main`, release, deleting branches, tags, releases or repos, changes to GitHub settings) only on explicit instruction. Feature branches that are deleted automatically after the merge are excluded.
