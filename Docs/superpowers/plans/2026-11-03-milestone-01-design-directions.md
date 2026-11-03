# Milestone 1 – Design directions & spec: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Measure cold start, warm start, input latency, idle memory and size of a Native AOT Avalonia app on the owner's Windows, Linux and macOS machines, and record the speed targets derived from them.

**Architecture:** A throwaway probe app (`bench/shell-probe/ShellProbe`) has two modes, `empty` and `frame`. The frame mode is a static mock of the Direction D frame with three embedded fonts and German resources. A single-file script (`bench/shell-probe/measure.cs`) starts the published probe, takes the times and writes CSVs. A manual-only workflow (`measure.yml`) runs both on hand-started self-hosted runners. The results and budgets go into `Docs/DECISIONS.md`.

**Tech Stack:** .NET 10 Native AOT, Avalonia 12.1.4 (central versions in `Directory.Packages.props`), GitHub Actions self-hosted runners.

**Spec:** `Docs/superpowers/specs/2026-11-03-design-system-shell-design.md`, sections 9 and 11. The design direction is already chosen and recorded (`Docs/DECISIONS.md`, entry "Design direction: D "Formular Neutral""), and the spec is approved. Both are done and are not tasks here.

## Global Constraints

- Prior art: Pagewing `bench/startup/startup.cs`, `bench/startup/README.md`, `src/Pagewing.App/Measurement/*` and `.github/workflows/hardware-measure.yml` at tag `v1.0.0`. Read them with `git -C C:\VS-Code\Pagewing show v1.0.0:<path>` and never modify the Pagewing checkout.
- The probe is not product code. No `src/` project references it, and it will not be reused in milestone 1.1.
- **Measurement point "first-frame":** in the window's `Opened` handler, call `TopLevel.RequestAnimationFrame`. In that callback, request a second frame. In the second callback, write the line `first-frame` to stdout. The script times from immediately before `Process.Start` to the moment it reads the line.
- **Cases** (spec 9.1, 10 runs each, median/min/max):
  - `cold`: 0 warm-up runs, 5 s pause between runs;
  - `warm`: 3 warm-up runs, 500 ms pause;
  - `input`: 100 key presses in one run, measured in the app;
  - `memory`: 2 s after first-frame; private bytes on Windows, RSS on Linux and macOS;
  - `size`: the published folder, the zip, and the shares of fonts and native libraries.
- **Budgets** (spec 9.4):
  - the budget is the median rounded up to the next 10 ms; memory to the next 5 MiB; size to the next MiB;
  - the limit is the budget × 1.25;
  - the budgets come from the `frame` mode only.
- **Machines:** Windows main PC and the HP ProBook (CachyOS) are binding. The 2016 Intel Mac on macOS 15 via OpenCore Legacy Patcher is indicative only.
- **Workflow security:** the only trigger is `workflow_dispatch`, with `permissions: contents: read`, no secrets, and runner labels `[self-hosted, raffica-measure, <windows|linux|macos>]`. CI labels are never used.
- **Telemetry:** `AVALONIA_TELEMETRY_OPTOUT: '1'` in the workflow env, as in Pagewing.
- **Docs, code and comments** are in English. The probe's UI strings are English (source) and German (resx), taken from the canvas `Docs/design/direction-d/Main.dc.html`.
- **Done** means:
  - `dotnet build Raffica.slnx -c Release`, the test command from `CLAUDE.md`, `dotnet format Raffica.slnx --verify-no-changes` and `dotnet run tools/roadmap.cs -- check` are green;
  - `dotnet run bench/shell-probe/measure.cs -- selftest` exits 0.

## Review Focus

1. **A machine without a usable compositor rate** (VM, display off, remote session). The script must warn before the runs and next to the result, never fail silently. Pagewing used the `DwmFlush` check, and Task 2 ports it.
2. **The probe hangs** (no display, a font fails to load). Every run must end. The probe exits with code 3 after 30 s and the script kills it after 45 s. Covered in Task 1 and the Task 2 selftest.
3. **Linux under Wayland only.** The probe must start through XWayland or Avalonia's default backend. If it cannot, the run fails with a clear `error:` line instead of hanging. Task 3 checks this on the ProBook.
4. **A German resource lookup falls back to English under Native AOT without an error.** Task 1 asserts that the published probe with `--lang de` prints a German string.
5. **CSV values written with a decimal comma on a German system.** Every number is formatted with `CultureInfo.InvariantCulture`, and the Task 2 selftest runs under `de-DE`.

---

### Task 1: The probe app

**Files:**
- Create: `bench/shell-probe/ShellProbe/ShellProbe.csproj`
- Create: `bench/shell-probe/ShellProbe/Program.cs`, `App.axaml(.cs)`, `ProbeWindow.axaml(.cs)` (frame mode), `EmptyWindow.cs` (empty mode)
- Create: `bench/shell-probe/ShellProbe/Measurement.cs` (switch parsing, first-frame, input, memory, timeout)
- Create: `bench/shell-probe/ShellProbe/Strings.resx`, `Strings.de.resx`, `Strings.cs`
- Create: `bench/shell-probe/ShellProbe/Assets/Fonts/` with `PublicSans[wght].ttf`, `BarlowSemiCondensed-SemiBold.ttf`, `Carlito-Regular.ttf`, `Carlito-Bold.ttf`, `Carlito-Italic.ttf`, `Carlito-BoldItalic.ttf` and `OFL-*.txt`
- Create: `bench/shell-probe/ShellProbe/Assets/Fonts/SOURCES.md` (upstream URL, commit and SHA-256 of each font)
- Modify: `Raffica.slnx` (add a `/bench/` folder with this project, so CI builds and formats it)

**Interfaces:**
- **Produces the command line:**
  - `ShellProbe [--mode empty|frame] [--lang en|de] --measure-startup`: writes `first-frame` and exits 0.
  - `ShellProbe --mode frame --measure-input`: after first-frame, 100 times: focus the probe's `TextBox`, record a timestamp, raise `KeyDown` (`Key.A`) and `TextInput` ("a") on it, wait two animation frames, take the elapsed time. Then writes `input median-ms=<x> p99-ms=<y> max-ms=<z>` and exits 0.
  - `ShellProbe --mode <m> --measure-memory`: writes `memory-bytes <n>` 2 s after first-frame and exits 0. On Windows `n` is `Process.PrivateMemorySize64`; elsewhere it is `Process.WorkingSet64` after `Refresh()`.
  - `ShellProbe --print-string RibbonHome --lang de`: writes the resource value and exits 0. This is the AOT resource check.
- **Rules for every switch:**
  - Numbers use the invariant culture.
  - Exit code 1 with `error: …` on stderr for invalid switches.
  - Exit code 3 after 30 s without a result.
  - No file is read or written outside the app folder.

- [ ] **Step 1: Fetch the fonts.** Download the files from `github.com/google/fonts` (folders `ofl/publicsans`, `ofl/barlowsemicondensed`, `ofl/carlito`) at one pinned commit. Record the URL, commit and SHA-256 per file in `SOURCES.md`, and copy each folder's `OFL.txt` as `OFL-<family>.txt`. Check: `sha256sum` matches `SOURCES.md`.
- [ ] **Step 2: Create `ShellProbe.csproj`.**
  - `OutputType` WinExe, `PublishAot` true, `IsAotCompatible` true, `AvaloniaUseCompiledBindingsByDefault` true, `InvariantGlobalization` false (German formats are needed), `SatelliteResourceLanguages` `en;de`.
  - Package references `Avalonia`, `Avalonia.Desktop` and `Avalonia.Themes.Fluent` (central versions).
  - Fonts as `AvaloniaResource`.
- [ ] **Step 3: Build the frame mode** as a static copy of the Write board in `Docs/design/direction-d/Main.dc.html` at 1440 × 900:
  - title bar 36 px with three document tabs and a search field;
  - tab row 32 px;
  - ribbon band 82 px with about 60 controls (16/20 px stroke icons as `PathIcon`, labels from the resx);
  - navigation panel with 8 items, a desk with a white page holding one Carlito paragraph and a `TextBox`;
  - comments panel and status bar 30 px.

  Use the light token values from appendix A of the spec. Nothing has to work.
- [ ] **Step 4: Build the empty mode:** one `Window` of 1440 × 900 with no content.
- [ ] **Step 5: Implement the switches in `Measurement.cs`** as specified in Interfaces.
- [ ] **Step 6: Publish and verify locally (Windows main PC):**

  Run: `dotnet publish bench/shell-probe/ShellProbe -c Release -r win-x64 -o artifacts/shell-probe/win-x64`

  Then:
  - `artifacts/shell-probe/win-x64/ShellProbe.exe --print-string RibbonHome --lang de` prints `Start` (the German value from the canvas; the English one is `Home`);
  - `artifacts/shell-probe/win-x64/ShellProbe.exe --mode frame --measure-startup | Out-String` contains `first-frame`;
  - `--measure-input` prints an `input …` line;
  - `--measure-memory` prints a `memory-bytes …` line.
- [ ] **Step 7: Build, test and format the solution** (the commands from "Done"). Expected: 0 warnings, all green.
- [ ] **Step 8: Commit.**

```bash
git add bench/shell-probe/ShellProbe Raffica.slnx
git commit -m "feat(bench): add the Native AOT shell probe for the speed measurement"
```

### Task 2: The measurement script

**Files:**
- Create: `bench/shell-probe/Directory.Build.props` (isolates the single-file script, like `tools/Directory.Build.props`). It must not isolate the `ShellProbe` project folder: place the probe's own `Directory.Build.props` there, importing the root one, or put the script in `bench/shell-probe/script/`. Choose one and say which in the commit.
- Create: `bench/shell-probe/measure.cs`

**Interfaces:**
- Consumes: the probe's switches from Task 1.
- **Produces `dotnet run bench/shell-probe/measure.cs -- run`** with the arguments `--exe <probe> --mode empty|frame --case cold|warm|input|memory --out <csv>`.
  - CSV columns: `machine,mode,case,run,value,unit`, with `unit` one of `ms` or `bytes`.
  - It prints every run plus the median, minimum and maximum. For `input` there is one row per statistic (`median`, `p99`, `max`).
- **Produces `… -- size`** with the arguments `--publish-dir <dir> --fonts-dir <dir> --out <csv>`. It writes rows `size,total`, `size,zip`, `size,fonts` and `size,native` in bytes. `native` covers every file in the folder except the main executable and its debug symbols. `zip` uses `CompressionLevel.Optimal` to a temp file.
- **Produces `… -- budgets`** with the arguments `--in <csv>... --out <md>`. It writes a Markdown table per machine and case: median, budget, limit (frame mode only, rules from Global Constraints).
- **Produces `… -- selftest`.** Exit codes: 0 ok, 1 a failed run, 2 usage.
- `machine` comes from `--machine <name>`; the default is `Environment.MachineName`.

- [ ] **Step 1: Write the selftest cases first** (inside `measure.cs`, as Pagewing's `SelfTest.Run`):
  - `Median([5,1,3]) == 3` and `Median([4,1,3,2]) == 2.5`;
  - `NearestRankP99` of 1..100 is 99;
  - `BudgetMs(201.3) == 210` and `BudgetMs(200.0) == 200`, `BudgetBytesMemory(52.1 MiB) == 55 MiB`, `BudgetBytesSize(30.2 MiB) == 31 MiB`, `Limit(210) == 262.5`;
  - CSV round-trip under `CultureInfo.CurrentCulture = de-DE` gives `"201.3"`, not `"201,3"`;
  - a fake executable that never prints `first-frame` is killed after the timeout (selftest timeout 2 s) and reported as a failed run with exit code 1;
  - parsing of the `input …` and `memory-bytes …` lines.
- [ ] **Step 2: Run** `dotnet run bench/shell-probe/measure.cs -- selftest`. Expected: FAIL (the functions do not exist yet).
- [ ] **Step 3: Implement** `run`, `size`, `budgets` and the helpers `Median`, `NearestRankP99`, `BudgetMs`, `BudgetBytesMemory`, `BudgetBytesSize` and `Limit`.
  - Port the `DwmFlush` warning from Pagewing's `startup.cs`: five intervals measured before the runs; warn above 40 ms median or when there is no composition.
  - The warning is printed before the runs, next to the result, and as `::warning::` when `GITHUB_ACTIONS` is set.
  - The run timeout is 45 s, as in Global Constraints.
- [ ] **Step 4: Run the selftest.** Expected: exit 0.
- [ ] **Step 5: Run it locally against the Task 1 publish:** `dotnet run bench/shell-probe/measure.cs -- run --exe artifacts/shell-probe/win-x64/ShellProbe.exe --mode frame --case warm --out artifacts/shell-probe/local-warm.csv`. Expected: 10 runs, a median line, exit 0.
- [ ] **Step 6: Commit.**

```bash
git add bench/shell-probe/Directory.Build.props bench/shell-probe/measure.cs
git commit -m "feat(bench): add the shell probe measurement script"
```

### Task 3: Workflow and runner guide

**Files:**
- Create: `.github/workflows/measure.yml`
- Create: `bench/shell-probe/RUNNERS.md`
- Create: `bench/shell-probe/README.md` (what is measured, how to run locally, how budgets are derived; link the spec section 9)

**Interfaces:**
- Consumes: Task 1 publish, Task 2 commands.
- **Produces:** `gh workflow run measure.yml -f platform=<windows|linux|macos> -f note="<machine note>"`. Artifact `measure-<platform>-<run id>` contains:
  - `cold.csv`, `warm.csv`, `input.csv`, `memory.csv` for both modes;
  - `size.csv` for the frame mode;
  - `machine.txt` with the note, OS description, processor name and count, and total RAM.

- [ ] **Step 1: Write `measure.yml`.**
  - Inputs: `platform` (choice: windows, linux, macos), `note` (string, required), `cases` (choice: `all` or `cold-reboot`, default `all`; `cold-reboot` runs only one `cold` run in frame mode and writes `cold-reboot.csv`).
  - Triggers and permissions as in Global Constraints. Header comment: the security block from Pagewing's `hardware-measure.yml`, adapted.
  - The job runs on `[self-hosted, raffica-measure, ${{ inputs.platform }}]`, with `concurrency: group: measure-${{ inputs.platform }}, cancel-in-progress: false` and a 60-minute timeout.
  - Steps: checkout (same pinned SHA as `ci.yml`, `persist-credentials: false`), setup-dotnet 10.0.x, then publish the probe for `win-x64`, `linux-x64` or `osx-x64` by platform.
  - Then the four timed cases in both modes, the size case for frame mode, `machine.txt`, and the upload with 30-day retention.
- [ ] **Step 2: Write `RUNNERS.md`.** Per OS:
  - **Prerequisites:**
    - Windows: Visual Studio Build Tools with the C++ workload.
    - CachyOS: `clang`, `zlib` and an X11 or XWayland session.
    - macOS: the Xcode command line tools.
  - **Register the runner:** Settings → Actions → Runners → New self-hosted runner, labels `raffica-measure,<os>`, then `./config.sh` with the one-time token. Never put the token in a file in the repo.
  - **Start it by hand** with `run.cmd` or `./run.sh` in the logged-in desktop session (not as a service, not over SSH), on mains power with the display on.
  - **Stop it** with Ctrl+C after the run.
  - **Recommended:** the fork-approval setting from spec 9.3, changed only by the owner.
- [ ] **Step 3: Check the workflow** with `gh workflow view measure.yml` after pushing the branch. Expected: it lists `workflow_dispatch` only.
- [ ] **Step 4: Commit.**

```bash
git add .github/workflows/measure.yml bench/shell-probe/RUNNERS.md bench/shell-probe/README.md
git commit -m "ci: add the manual shell probe measurement workflow and runner guide"
```

### Task 4: Measurement runs (needs the owner)

- [ ] **Step 1:** Ask the owner to register and start the Windows runner (main PC), with the display on.
- [ ] **Step 2:** Run `gh workflow run measure.yml --ref feature/m01-design-directions -f platform=windows -f note="<from owner: GPU, resolution, scaling>"`, wait with `gh run watch`, then download with `gh run download <id> -D artifacts/measure/windows`. Expected: all CSVs present and no compositor warning in the log.
- [ ] **Step 3:** Repeat for `linux` (ProBook) and `macos` (Mac). If the Wayland check (Review Focus 3) fails, record it and the fallback used.
- [ ] **Step 4: Cold start after a reboot** (spec 9.1). The owner reboots each binding machine, starts the runner and runs only `-f platform=<os>`. The first `cold` case after the reboot is kept as `cold-reboot`. The step input for this is `-f cases=cold-reboot`, which Task 3's workflow accepts as an optional input with the default `all`.

### Task 5: Record values and targets, close the milestone

**Files:**
- Modify: `Docs/DECISIONS.md` (new entry at the top)
- Modify: `Docs/roadmap.json`, regenerated `Docs/ROADMAP.md`

- [ ] **Step 1:** Run `dotnet run bench/shell-probe/measure.cs -- budgets --in artifacts/measure/*/*.csv --out artifacts/measure/budgets.md`.
- [ ] **Step 2: Add the decisions entry "Speed targets of the frame (M1)".**
  - Context: spec 9.
  - The machines with their notes.
  - A table of the medians per machine, mode and case (empty and frame).
  - The resulting budgets and limits for Windows and Linux.
  - The macOS values marked as indicative.
  - The cost of the frame (frame minus empty).
  - The size shares.
  - Anything not measured, with the command that measures it.
- [ ] **Step 3:** In `Docs/roadmap.json`, tick task 1 and acceptance 1 of milestone 1 with the evidence (workflow run ids, decisions entry). Set the status to `done` with `completedOn`. Then run `dotnet run tools/roadmap.cs -- generate` and `-- check`.
- [ ] **Step 4:** Run build, test, format and the roadmap check (see "Done"). All green.
- [ ] **Step 5: Commit, then open a PR to `dev`.**

```bash
git add Docs/DECISIONS.md Docs/roadmap.json Docs/ROADMAP.md
git commit -m "docs: record the measured speed targets and close milestone 1"
```

Open the PR with `gh pr create --base dev`. Merge it once CI is green (squash allowed).
