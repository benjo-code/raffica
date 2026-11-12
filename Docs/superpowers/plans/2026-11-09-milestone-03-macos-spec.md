# Milestone 3 – Create spec for macOS – Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The approved macOS spec is linked in the roadmap, its three implementation milestones (3.1 to 3.3) are entered, and its lasting decisions are in `Docs/DECISIONS.md`.

**Architecture:** Documents only, no product code, no workflow change. `Docs/roadmap.json` is edited and `Docs/ROADMAP.md` regenerated (CLAUDE.md, "Maintaining the roadmap"); `Docs/DECISIONS.md` gets new entries; `CLAUDE.md` gets one pointer line. The work goes to `dev` as one PR from `feature/m03-macos-spec`.

**Tech Stack:** `dotnet run tools/roadmap.cs -- generate|check` (.NET 10 SDK), `gh` for the PR.

**Spec:** `Docs/superpowers/specs/2026-11-09-raffica-macos-design.md` (approved by the owner on 2026-11-09; section 8 is the milestone table this plan enters).

## Global Constraints

- Everything in English (docs, commits, roadmap texts).
- Edit only `Docs/roadmap.json`; never `Docs/ROADMAP.md` by hand. Leave optional fields out, never set them to `null`.
- `status` is one of `open`, `planned`, `in-progress`, `done`; `done` needs `completedOn` (YYYY-MM-DD); from `planned` on `plan` is required (path relative to `Docs/`). Milestone ids are unique strings; prerequisites must exist; title, goal and name are single lines.
- Acceptance criteria are ticked only when checked, with the proof in `evidence`.
- Work only inside this milestone: no product code, no packages, no workflow edits. The CodeQL trigger trim the owner agreed to is a separate `fix/` branch, not this milestone.
- Commit with the GitHub noreply address only. Conventional Commits. Stage files by name, never `git add .` or `-A`. The two files already modified in the working tree before this branch (`src/Raffica.Shell/Frame/SettingsStore.cs`, `tests/Raffica.Pdf.Tests/Panes/NavListSelectionTests.cs`) are foreign and are never staged. After every commit check `git show --stat HEAD` against the expected file list.
- No force push, no `reset --hard`, no `--no-verify`. Do not touch `main`. The PR goes to `dev` and is merged by Claude once all CI checks are green (squash allowed).
- "Done" = `dotnet build Raffica.slnx -c Release`, the test command from `CLAUDE.md`, `dotnet format Raffica.slnx --verify-no-changes` and `dotnet run tools/roadmap.cs -- check` demonstrably green, with the commands and results named.

## Review Focus

- Roadmap validation: a `null` optional field, a duplicate id, a prerequisite that names a missing id, or a milestone with status `planned` and no `plan` fails the check. Pinned by `dotnet run tools/roadmap.cs -- check` after every edit.
- A stale `Docs/ROADMAP.md` (JSON edited, generator not run) fails the same check; the check is run before and after `generate` (Task 2 step 2).
- Write's milestone `4` depends on `["3"]`, which after this milestone means only "the macOS spec exists". Spec section 8 moves it to `["3.3"]` so that "macOS before Write" stays true. Pinned by Task 2 step 4.
- The backlog entry "Option key handling on macOS" is decided by spec 5.4 and becomes work of 3.2; leaving it would plan the same work twice. Pinned by Task 2 step 5.
- An acceptance criterion that no command or manual check can verify would break the roadmap rule. Pinned by Task 2 step 3: every criterion names a test, a command or a concrete manual check on the owner's Mac.

---

### Task 1: Link the spec and the plan

**Files:**
- Add: `Docs/superpowers/specs/2026-11-09-raffica-macos-design.md`, `Docs/superpowers/plans/2026-11-09-milestone-03-macos-spec.md`
- Modify: `Docs/roadmap.json` (subproject 4, milestone `3`)
- Regenerate: `Docs/ROADMAP.md`

- [ ] **Step 1:** In subproject 4 replace `spec` with `{ "text": "Raffica on macOS design, approved 2026-11-09", "link": "superpowers/specs/2026-11-09-raffica-macos-design.md" }`. Milestone `3`: add `"plan": "superpowers/plans/2026-11-09-milestone-03-macos-spec.md"` and `"spec": "superpowers/specs/2026-11-09-raffica-macos-design.md"`, set `"status": "planned"`. Change the goal to: `The macOS port of Raffica PDF (both architectures, Mac conventions, .dmg, updates, CI) is specified and split into milestones.`

- [ ] **Step 2:** `dotnet run tools/roadmap.cs -- generate`, then `-- check`. Expected: check passes.

- [ ] **Step 3: Commit** (after the owner has reviewed this plan)

```bash
git add Docs/superpowers/specs/2026-11-09-raffica-macos-design.md Docs/superpowers/plans/2026-11-09-milestone-03-macos-spec.md Docs/roadmap.json Docs/ROADMAP.md
git commit -m "docs: plan milestone 3 (macOS spec)"
git show --stat HEAD
```
Expected file list: the four files only.

### Task 2: Enter the macOS milestones in the roadmap

**Files:**
- Modify: `Docs/roadmap.json` (subproject 4, milestone `4`, `scopeChanges`, `backlog`)
- Regenerate: `Docs/ROADMAP.md`

**Interfaces:**
- Consumes: spec section 8 (table), sections 5 to 7.
- Produces: milestone ids `3.1`, `3.2`, `3.3`, which Task 3's entries and every later macOS plan cite.

- [ ] **Step 1: Status `in-progress`** for milestone `3`. Generate, check, pass.

- [ ] **Step 2: Failing check first.** Add the milestones of step 3 without regenerating. Run `dotnet run tools/roadmap.cs -- check`. Expected: FAIL (`ROADMAP.md` is not up to date).

- [ ] **Step 3: Add milestones `3.1`, `3.2`, `3.3` to subproject 4** (after `3`), each with `status: "open"`, `spec` set to the spec path, `goal`, `tasks` (the "Contents" cell of spec section 8 split into 4 to 7 tasks, `done: false`) and `acceptance` (the "Acceptance" cell split into criteria, `done: false`, each naming a test, a command or a concrete manual check on the owner's Mac):

| id | title | prerequisites | goal (one sentence) |
|---|---|---|---|
| `3.1` | Runs on the Mac | `["3"]` | Raffica PDF builds, passes its tests and opens PDFs from the Finder on Intel and Apple-silicon Macs, checked by a sparse macOS CI job. |
| `3.2` | Feels like a Mac app | `["3.1"]` | Raffica PDF behaves like a Mac app: menu bar, ⌘ shortcuts without key tips, life cycle without windows, trackpad, Retina and printing. |
| `3.3` | Package and release | `["3.2"]` | Signed `.app` bundles ship as two `.dmg` files with default-app offer, self-update, release workflow, README and recorded speed budgets. |

Milestone `3.1`'s tasks include the `build-macos` job with its path filter and the label `ci:macos` (spec section 7). Milestone `3.2`'s tasks include moving the single-instance listener, the update flow and the dialog queue off the window (spec 5.3) and the reserved-shortcut audit (spec 5.2). Milestone `3.3`'s tasks include the owner checks that only a real Mac answers (first launch through "Open Anyway", default app, update without a Gatekeeper prompt, quarantine attribute), the amendment of the M2.6 entry "x86_64 only" through a new `Docs/DECISIONS.md` entry, and the Apple-silicon measurement record.

- [ ] **Step 4: Re-point Write** — milestone `4`: `"prerequisites": ["3.3"]`.

- [ ] **Step 5: Backlog.** Remove "Option key handling on macOS" (decided in spec 5.4, built in 3.2). Add `{ "title": "macOS print panel", "text": "Print through the native macOS print panel (Preview, PDF export, presets) instead of the app's own CUPS print card. Left out of subproject 4 because it needs Objective-C interop for NSPrintOperation; the own card over CUPS is the decided path (spec 5.6)." }`.

- [ ] **Step 6: Scope changes** dated `2026-11-09`, context `Milestone 3`: (a) milestones 3.1 to 3.3 added under the macOS spec, with their names, both architectures, Apple silicon released with automatic checks only and marked "not tested by hand" in the README; (b) milestone 4 now depends on 3.3; (c) the backlog entry "Option key handling on macOS" is decided (no key tips and no ⌥ capture on macOS) and moves into 3.2, and "macOS print panel" is added. Texts are one or two sentences and name the Task 3 entry titles.

- [ ] **Step 7:** Generate, then check. Expected: check passes, `ROADMAP.md` lists 3.1 to 3.3 and milestone 4 shows prerequisite M3.3.

- [ ] **Step 8: Commit**

```bash
git add Docs/roadmap.json Docs/ROADMAP.md
git commit -m "docs: enter milestones 3.1 to 3.3 for macOS in the roadmap"
git show --stat HEAD
```
Expected file list: the two files only.

### Task 3: Record the decisions

**Files:**
- Modify: `Docs/DECISIONS.md` (new entries on top, template format), `CLAUDE.md` (section "Project-specific", subsection "macOS")

**Interfaces:**
- Consumes: spec sections 2 to 7 and 10.
- Produces: the entry titles the scope changes of Task 2 cite (match them exactly).

- [ ] **Step 1: Add two entries dated 2026-11-09** (Context, Decision, Rationale, Rejected alternatives; short, no copy of the spec):
  - "Raffica on macOS: scope and Mac conventions (M3)": parity with `v0.1.0` on `osx-x64` and `osx-arm64`; Apple silicon released with automatic checks only and marked in the README; two `.dmg`, bundle id `io.github.benjo-code.raffica.pdf`, CLI inside the bundle; native menu bar bound to the command catalog; no key tips and no ⌥ capture on macOS; the app outlives its last window; files from the Finder through activation events; data under `~/Library/Application Support/Raffica`; path identity on case-insensitive volumes; printing over CUPS with the own card; default app through LaunchServices; self-update by swapping the `.app` from the `.dmg` with the download page as fallback. Rejected: universal binary (double download), native print panel (Objective-C interop, backlog), ⌥ key tips (break typing on the German Mac layout), quitting with the last window (not Mac-like).
  - "Raffica on macOS: speed checks and CI (M3)": size budgets binding from hosted-runner measurements; time and memory budgets binding only for the owner's Intel MacBook through `measure.yml`; the Mac rendering default decided from those runs; Apple silicon recorded without limit plus a coarse start ceiling in the smokes; the `build-macos` job only on PRs into `main`, on platform-near paths and with the label `ci:macos`; the release built on a hosted macOS runner; standard hosted runners are free in the public repository, larger runners never used. Rejected: macOS on every PR (slow queue, little gain); binding time budgets from hosted VMs (they vary too much).

- [ ] **Step 2: `CLAUDE.md`**, subsection "macOS": add one line after the existing paragraph: `Spec: Docs/superpowers/specs/2026-11-09-raffica-macos-design.md (milestones 3.1 to 3.3); decisions "Raffica on macOS: scope and Mac conventions (M3)" and "... speed checks and CI (M3)".`

- [ ] **Step 3:** `dotnet run tools/roadmap.cs -- check` still passes.

- [ ] **Step 4: Commit**

```bash
git add Docs/DECISIONS.md CLAUDE.md
git commit -m "docs: record the decisions of the macOS spec"
git show --stat HEAD
```

### Task 4: Close milestone 3 and merge

**Files:**
- Modify: `Docs/roadmap.json`, regenerate `Docs/ROADMAP.md`

- [ ] **Step 1: Tick the task and the criterion with proof.** Milestone `3`: task done (`evidence` names the spec and milestones 3.1 to 3.3); criterion "The spec is approved and linked here" done with `evidence` naming the owner's approval in the planning session on 2026-11-09 ("ja passt"), the link `superpowers/specs/2026-11-09-raffica-macos-design.md` and the verification results of step 2. Status `done`, `completedOn: "2026-11-09"`. Generate and check.

- [ ] **Step 2: Verification** — run and record: `dotnet build Raffica.slnx -c Release` (expected 0 warnings, 0 errors), `RAFFICA_REQUIRE_OCR=1 dotnet test Raffica.slnx -c Release --no-build --max-parallel-test-modules 1` (after `dotnet run tools/native-fetch.cs -- --rid win-x64 --out artifacts/native/win-x64` if the library is missing; expected 0 failed), `dotnet format Raffica.slnx --verify-no-changes` (expected exit 0), `dotnet run tools/roadmap.cs -- check` (expected pass). If a command cannot run, say so in the PR and the roadmap and name the command.

- [ ] **Step 3: Commit and PR**

```bash
git add Docs/roadmap.json Docs/ROADMAP.md
git commit -m "docs: close milestone 3 (macOS spec approved)"
git show --stat HEAD
git push -u origin feature/m03-macos-spec
gh pr create --base dev --title "docs: milestone 3 – spec and milestones for macOS" --body "<what changed, the verification results, the (call) items the owner should check>"
```
`CHANGELOG.md` is not extended (planning PR, nothing for users).

- [ ] **Step 4: Merge when green** — `gh pr checks --watch`; when `ci` and `roadmap` are green (`ci` skips the build for a docs-only PR), `gh pr merge --squash`. Report the PR number and the check results.
