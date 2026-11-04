# Milestone 2 – Create spec for Raffica PDF – Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The approved spec of Raffica PDF is linked in the roadmap, its six implementation milestones (2.1 to 2.6) are entered, and its lasting decisions are in `Docs/DECISIONS.md`.

**Architecture:** Documents only, no product code. `Docs/roadmap.json` is edited and `Docs/ROADMAP.md` regenerated (CLAUDE.md, "Maintaining the roadmap"); `Docs/DECISIONS.md` gets new entries; the work goes to `dev` as one PR from `feature/m02-pdf-spec`.

**Tech Stack:** `dotnet run tools/roadmap.cs -- generate|check` (.NET 10 SDK), `gh` for the PR.

**Spec:** `Docs/superpowers/specs/2026-11-04-raffica-pdf-design.md` (approved by the owner on 2026-11-04; section 7 is the milestone table this plan enters).

## Global Constraints

- Everything in English (docs, commits, roadmap texts).
- Edit only `Docs/roadmap.json`; never `Docs/ROADMAP.md` by hand. Leave optional fields out, never set them to `null`.
- `status` is one of `open`, `planned`, `in-progress`, `done`; `done` needs `completedOn` (YYYY-MM-DD); from `planned` on `plan` is required (path relative to `Docs/`). Milestone ids are unique strings; prerequisites must exist; title, goal and name are single lines.
- Acceptance criteria are ticked only when checked, with the proof in `evidence`.
- Work only inside this milestone: no product code, no packages, no later-milestone decisions.
- Commit with the GitHub noreply address only. Conventional Commits. Stage files by name, never `git add .` or `-A`. After every commit check `git show --stat HEAD` against the expected file list.
- No force push, no `reset --hard`, no `--no-verify`. Do not touch `main`. The PR goes to `dev` and is merged by Claude once all CI checks are green (squash allowed).
- "Done" = `dotnet build Raffica.slnx -c Release`, the test command from `CLAUDE.md`, `dotnet format Raffica.slnx --verify-no-changes` and `dotnet run tools/roadmap.cs -- check` demonstrably green, with the commands and results named.

## Review Focus

- Roadmap validation: a `null` optional field, a duplicate id, a prerequisite that names a missing id, or a milestone with status `planned` and no `plan` fails the check. Pinned by `dotnet run tools/roadmap.cs -- check` after every edit.
- A stale `Docs/ROADMAP.md` (JSON edited, generator not run) fails the same check; the check is run before and after `generate`.
- The macOS spec milestone `3` depends on `["2"]`, which after this milestone means only "the PDF spec exists". The suite order is PDF before macOS, so it must depend on `["2.6"]`. Pinned by Task 1 step 4.
- The backlog entry "Build the OCR library in this repository" is now task 1 of milestone 2.5; leaving it in the backlog would plan the same work twice. Pinned by Task 1 step 5.
- A milestone's acceptance criterion that no command or manual check can verify (for example "works well") would break the roadmap rule that criteria are verifiable. Pinned by Task 1 step 3: every criterion names a test, command or concrete manual check.

---

### Task 1: Enter the PDF milestones in the roadmap

**Files:**
- Modify: `Docs/roadmap.json` (subproject 3, milestone `2`, milestone `3`, `scopeChanges`, `backlog`)
- Regenerate: `Docs/ROADMAP.md`

**Interfaces:**
- Consumes: spec section 7 (the table) and section 5 (speed), section 9 (decisions with delegation).
- Produces: milestone ids `2.1` … `2.6` with the prerequisites below, which Task 2 and every later PDF plan refer to.

- [ ] **Step 1: Link the plan and set `planned` (CLAUDE.md "Plan milestone N", step 6)**

In `Docs/roadmap.json`, milestone `2`: add `"plan": "superpowers/plans/2026-11-04-milestone-02-pdf-spec.md"`, `"spec": "superpowers/specs/2026-11-04-raffica-pdf-design.md"`, set `"status": "planned"`. In subproject 3 replace the `spec` object with `{ "text": "Raffica PDF design, approved 2026-11-04", "link": "superpowers/specs/2026-11-04-raffica-pdf-design.md" }`. Run `dotnet run tools/roadmap.cs -- generate`, then `-- check`. Expected: check passes. Commit (`docs: plan milestone 2 (Raffica PDF spec)`) together with the spec and this plan, after the owner has reviewed the plan.

- [ ] **Step 2: Failing check first**

Add the six milestones of step 3 without regenerating. Run `dotnet run tools/roadmap.cs -- check`. Expected: FAIL (`ROADMAP.md` is not up to date).

- [ ] **Step 3: Add milestones `2.1` … `2.6` to subproject 3** (after milestone `2`), each with `status: "open"`, `spec` set to the spec path, `goal`, `tasks` (the "Contents" cell of spec section 7 split into 4 to 6 tasks, `done: false`) and `acceptance` (the "Acceptance" cell split into criteria, `done: false`, each naming a test, a command or a concrete manual check):

| id | title | prerequisites | goal (one sentence) |
|---|---|---|---|
| `2.1` | Viewer | `["2", "1.2"]` | Raffica PDF opens, shows, searches and prints PDFs on the shared frame within speed budgets measured for it. |
| `2.2` | Annotate | `["2.1"]` | Marks and comments can be added, changed, undone and saved through the existing save pipeline. |
| `2.3` | Forms and signing | `["2.2"]` | AcroForm fields can be filled and a visible signature placed and saved. |
| `2.4` | Organize pages | `["2.2"]` | Pages can be reordered, rotated, deleted, inserted, merged, split and exported from the pages pane. |
| `2.5` | Redact, OCR and line editing | `["2.4"]` | Content can be redacted for real, scans made searchable and text lines corrected, with the OCR library built in this repository. |
| `2.6` | Packaging and release | `["2.5"]` | Installers, default-app registration and the updater exist for Windows and Linux, and the first release can be published on the owner's instruction. |

Milestone `2.1`'s first task is the inventory of Pagewing 1.0's features as a checklist per milestone; its tasks also include the shell option that hides the autosave row (spec section 6) and the task to look for the cause if the app is slower than Pagewing 1.0 (spec section 5). Milestone `2.5`'s first task is building the Tesseract library in this repository. Milestone `2.6` lists the open items that need the owner's machines (installers, printers, the real update key) with the command that checks each.

- [ ] **Step 4: Re-point the macOS spec milestone** — milestone `3`: `"prerequisites": ["2.6"]`.

- [ ] **Step 5: Backlog and scope changes**

Remove the backlog entry "Build the OCR library in this repository" (its work is task 1 of `2.5`). Add `scopeChanges` entries dated `2026-11-04`, context `Milestone 2`: (a) milestones `2.1` to `2.6` added under the PDF spec, with the names above; (b) milestone `3` now depends on `2.6` so that macOS stays after the finished PDF app; (c) the OCR library build moved from the backlog into `2.5`. Texts are one or two sentences and name `Docs/DECISIONS.md` entries from Task 2.

- [ ] **Step 6: Generate and check**

Run `dotnet run tools/roadmap.cs -- generate`, then `dotnet run tools/roadmap.cs -- check`. Expected: check passes, `ROADMAP.md` lists 2.1 to 2.6.

- [ ] **Step 7: Commit**

```bash
git add Docs/roadmap.json Docs/ROADMAP.md
git commit -m "docs: enter milestones 2.1 to 2.6 for Raffica PDF in the roadmap"
git show --stat HEAD
```
Expected file list: the two files only.

### Task 2: Record the decisions

**Files:**
- Modify: `Docs/DECISIONS.md` (new entries on top, template format)

**Interfaces:**
- Consumes: spec sections 3, 4, 5, 6 and 9.
- Produces: entry titles that the roadmap's scope changes cite.

- [ ] **Step 1: Add three entries dated 2026-11-04** (Context, Decision, Rationale, Rejected alternatives; short, no copy of the spec):
  - "Raffica PDF: scope and order (M2)" — everything Pagewing 1.0 had, defined by its roadmap and README; nothing new; Windows and Linux only; order 2.1 viewer, 2.2 annotate, 2.3 forms and signing, 2.4 pages, 2.5 redact/OCR/line editing, 2.6 packaging; no migration. Rejected: a slimmer first release (viewer only); new features in the first release.
  - "Raffica PDF: speed budgets are measured, not inherited (M2)" — measure-then-set as in M1, limit ×1.25 once, Pagewing 1.0 and Chrome as references, slower than Pagewing is a finding for the owner. Rejected: keeping Pagewing's absolute numbers (the frame alone is 375 ms cold on the owner's Windows machine, Pagewing's cold budget was 400 ms).
  - "Raffica PDF: platform code, autosave, OCR build and releases (M2)" — platform code may be ported from Pagewing in an import commit plus an adaptation commit, UI code never; autosave hidden in the PDF app by an additive shell option; the Tesseract library is built in this repository (task 1 of 2.5); releases live in this repository, the owner generates the update key and keeps it in a CI secret. Rejected: fetching Pagewing's prerelease for the shipped build; porting Pagewing's views.

- [ ] **Step 2: Check** — `dotnet run tools/roadmap.cs -- check` still passes (the scope changes cite entry titles; they must match the headings exactly).

- [ ] **Step 3: Commit**

```bash
git add Docs/DECISIONS.md
git commit -m "docs: record the decisions of the Raffica PDF spec"
git show --stat HEAD
```

### Task 3: Close milestone 2 and merge

**Files:**
- Modify: `Docs/roadmap.json`, regenerate `Docs/ROADMAP.md`

**Interfaces:**
- Consumes: Tasks 1 and 2.

- [ ] **Step 1: Tick the task and the criterion with proof.** Milestone `2`: task done; criterion "The spec is approved and linked here" done with `evidence` naming the owner's approval in the planning session on 2026-11-04 and the link `superpowers/specs/2026-11-04-raffica-pdf-design.md`. Status `done`, `completedOn: "2026-11-04"`. Generate and check.

- [ ] **Step 2: Verification** — run and record the results: `dotnet build Raffica.slnx -c Release` (expected 0 warnings, 0 errors), `RAFFICA_REQUIRE_OCR=1 dotnet test Raffica.slnx -c Release --no-build --max-parallel-test-modules 1` (after `dotnet run tools/native-fetch.cs -- --rid win-x64 --out artifacts/native/win-x64` if the library is missing; expected 0 failed), `dotnet format Raffica.slnx --verify-no-changes` (expected exit 0), `dotnet run tools/roadmap.cs -- check` (expected pass). If a command cannot run, say so in the PR and the roadmap and name the command.

- [ ] **Step 3: Commit and PR**

```bash
git add Docs/roadmap.json Docs/ROADMAP.md
git commit -m "docs: close milestone 2 (Raffica PDF spec approved)"
git show --stat HEAD
git push -u origin feature/m02-pdf-spec
gh pr create --base dev --title "docs: milestone 2 – spec and milestones for Raffica PDF" --body "<what changed, the verification results, the calls the owner should check>"
```
`CHANGELOG.md` is not extended (planning PR, nothing for users).

- [ ] **Step 4: Merge when green** — `gh pr checks --watch`; when `ci` and `roadmap` are green, `gh pr merge --squash`. Expected: PR merged into `dev`. Report the PR number and check results.
