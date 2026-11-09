# Shell probe

A throwaway [Native AOT](https://learn.microsoft.com/dotnet/core/deploying/native-aot/) Avalonia 12 app and the script
that measures it. It answers one question before the shell is built: **how fast can the frame of Raffica Office be on the
machines that count?** The numbers become the speed targets of the design system and shell (spec section 9 of
[`Docs/superpowers/specs/2026-11-03-design-system-shell-design.md`](../../Docs/superpowers/specs/2026-11-03-design-system-shell-design.md)).

The probe is not product code. No project under `src/` references it, and milestone 1.1 does not reuse it.

## What is in it

- [`ShellProbe/`](ShellProbe) has two modes:
  - `empty`: one window of 1440 × 900 with nothing in it, the baseline;
  - `frame`: a static mock of the Write frame of design direction D (title bar with three document tabs and search, tab
    row, ribbon with about 60 controls and icons, navigation panel, a page with a Carlito paragraph and a text box,
    comments, status bar), with three embedded font families and German resources. Nothing in it works.
- [`measure.cs`](measure.cs) starts the published probe, takes the times, and writes CSVs and a budget table.
- [`RUNNERS.md`](RUNNERS.md) and [`../../.github/workflows/measure.yml`](../../.github/workflows/measure.yml) run both on
  the owner's machines.

## What is measured

| Case | What | Runs |
|---|---|---|
| `cold` | From just before `Process.Start` to the probe's `first-frame` line; no warm-up run, 5 s pause. It does not empty the file cache of the operating system: a real cold start is the `cold-reboot` run right after a reboot. | 10 |
| `warm` | The same after three warm-up runs, 500 ms pause. | 10 |
| `input` | 100 synthetic key presses in the frame mode, measured inside the app: from raising the key events to the second animation frame after them. One row each for the median, the p99 (nearest rank) and the maximum. | 1 (100 presses) |
| `memory` | 2 s after `first-frame`: private bytes on Windows, working set (RSS) on Linux and macOS. | 10 |
| `size` | The published folder, its zip (`CompressionLevel.Optimal`), and the shares of fonts and native libraries. Debug symbols (`*.pdb`, `*.dbg`) are not shipped and count nowhere. | 1 |

**`first-frame`** is written in the second animation frame requested after the window's `Opened` event
(`TopLevel.RequestAnimationFrame`, the same method as the predecessor's `first-sharp`).

### Output

CSV columns: `machine,mode,case,run,value,unit` (`unit` is `ms` or `bytes`; numbers use the invariant culture). For
`input` the column `run` holds the statistic (`median`, `p99`, `max`), for `size` it holds `total`, `zip`, `fonts` or
`native`. The script prints every run and the median, minimum and maximum.

### Validity

Valid numbers need a GPU and an active display. On a machine without a usable compositor rate (a VM, a display that is
off, a remote session) the numbers show the compositor's rate, not the probe's. The script times the desktop
composition (`DwmFlush` on Windows) before the runs and warns when it is missing or below 25 Hz: before the runs, next
to the result, and as `::warning::` on GitHub Actions. It cannot do this check on Linux and macOS, where the machine
notes and the rule "logged-in desktop session, display on" apply.

When the check finds an invalid machine, `run` also writes `<csv>.warning.txt` next to the CSV, and `budgets` prints the warning at the top of its table: invalid numbers cannot silently become budgets. `--runs <n>` sets the number of measured runs (the cold run after a reboot uses `--runs 1`).

Every run ends: the probe exits with code 3 after 30 s without a result, and the script kills it after 45 s.

## Run it locally

```
dotnet publish bench/shell-probe/ShellProbe -c Release -r win-x64 -o artifacts/shell-probe/win-x64
dotnet run bench/shell-probe/measure.cs -- run --exe artifacts/shell-probe/win-x64/ShellProbe.exe --mode frame --case warm --out artifacts/shell-probe/warm.csv
dotnet run bench/shell-probe/measure.cs -- size --publish-dir artifacts/shell-probe/win-x64 --fonts-dir bench/shell-probe/ShellProbe/Assets/Fonts --out artifacts/shell-probe/size.csv
dotnet run bench/shell-probe/measure.cs -- selftest
```

Use `-r linux-x64` or `-r osx-x64` on the other systems. The probe's own switches: `--mode empty|frame`,
`--lang en|de`, `--measure-startup`, `--measure-input`, `--measure-memory`, `--print-string <key>`. On Windows the
Native AOT publish needs the Visual Studio C++ build tools, and `vswhere.exe` must be on `PATH`
(`%ProgramFiles(x86)%\Microsoft Visual Studio\Installer`); without it the linker step fails with a `vswhere` message.

`--print-string RibbonHome --lang de` prints `Start` (English: `Home`): the check that the German resource is found
under Native AOT.

## How the budgets are derived

Spec section 9.4. For each binding machine and case, from the **frame** mode only:

- the **budget** is the median rounded up to the next 10 ms (memory: next 5 MiB, size: next MiB);
- the **limit** is the budget × 1.25.

```
gh run download <run id> -n measure-<platform>-<run id> -D artifacts/measure/<platform>
dotnet run bench/shell-probe/measure.cs -- budgets --in "artifacts/measure/*/*.csv" --out artifacts/measure/budgets.md
```

(with `-n`, the CSVs land directly in the target folder, which the pattern needs; the artifacts of the repository are
kept for 7 days only). The first measurement is kept in
[`Docs/measurements/shell-probe-2026-11-03/`](../../Docs/measurements/shell-probe-2026-11-03).

writes one Markdown table per machine: the medians of both modes, the cost of the frame (frame minus empty), the budget
and the limit. The results, the machines and the budgets are recorded in [`Docs/DECISIONS.md`](../../Docs/DECISIONS.md).
Budgets are never loosened to make a check pass; a change is a new decisions entry.

## Measuring the gallery app (milestone 1.2)

The same script measures the frame of the gallery app (`src/Raffica.Gallery`, `--view frame`) against the limits of the
decisions entry "Speed targets of the frame (M1)". With `--target gallery`, `run` starts the exe with `--view frame`, the
case switch and a `--settings-file` in a temporary folder (a measurement never reads or writes the settings of the user);
there is no `--mode` and no `empty` baseline, and the CSV column `mode` is `gallery`. `size --target gallery` names the
gallery exe as the main file.

```
dotnet publish src/Raffica.Gallery -c Release -r win-x64 -o artifacts/gallery/win-x64
dotnet run bench/shell-probe/measure.cs -- run --target gallery --exe artifacts/gallery/win-x64/RafficaGallery.exe --case warm --out artifacts/gallery/warm.csv
dotnet run bench/shell-probe/measure.cs -- size --target gallery --publish-dir artifacts/gallery/win-x64 --fonts-dir src/Raffica.Shell/Assets/Fonts --out artifacts/gallery/size.csv
dotnet run bench/shell-probe/measure.cs -- check --platform windows --in "artifacts/gallery/*.csv" --summary artifacts/gallery/summary.md
```

`check` compares the medians of the gallery CSVs with the limits (the budget x 1.25; the table is in the script, its source
is the decisions entry) and prints case, measured, budget, limit and verdict. It exits with 1 when a value is above its
limit or a required CSV is missing. macOS has no limits: the verdict is `indicative` and the exit code 0.

| | Windows | Linux |
|---|---|---|
| cold / warm (ms) | 537.5 / 525 | 250 / 262.5 |
| input median (ms) | 12.5 | 25 |
| memory (MiB) | 156.25 | 156.25 |
| published size / zip (MiB) | 48.75 / 20 | 43.75 / 20 |

`size --check <win-x64|linux-x64|osx-x64> --publish-dir <dir> --fonts-dir <dir>` checks only size and zip, writes no CSV
and exits with 1 above the limit; CI runs it after publishing the gallery (job step "Size of the published gallery").
The workflow `measure.yml` takes the input `target` (`gallery` by default, or `probe`) and ends with `check`, whose table
goes to the job summary. A gallery that is over a limit is a finding for the owner, not a new budget.

## Measuring the PDF app (milestone 2.1)

The method (what each case measures, the invariant-culture lines, the 30 s exit-3 timeout, the settings isolation, the rule for
the limits) is the entry "Raffica PDF measurement method (M2.1)" in `Docs/DECISIONS.md`. The app has the switches
`--measure-startup`, `--measure-second`, `--measure-scroll` and `--measure-memory` (each needs `--settings-file` and a file); the
script starts it with a settings folder of its own per run. `--target pdf` has the cases `cold`, `warm`, `thousand` (the
1000-page file to its first page), `second` (a further file handed to the running instance), `scroll` (600 wheel steps through
the 1000-page file, p99 and max of the frame gaps) and `memory`; the CSV column `mode` is `pdf`; the test files come from
`testdata/pdfs` (`--testdata <folder>` if the script is not run from the repository root).

Software rendering is the default of the PDF app on Windows (entry "Raffica PDF: software rendering is the Windows default (M2.1
follow-up)" in `Docs/DECISIONS.md`); the environment variable `RAFFICA_RENDERING=default` opts out to Avalonia's own default path (ANGLE
over Direct3D 11). The `measure.yml` input `rendering` (only used for `--target pdf` on Windows) has three choices: `app` (the default;
nothing is set, the app does what a user gets), `software` (sets `RAFFICA_RENDERING=software`, which is the same as `app` today but
stays explicit) and `default` (sets `RAFFICA_RENDERING=default`, Avalonia's own path, for comparison on the same machine with the same
method). The scripts' child processes inherit the variable; the effective value is written to `machine.txt` and to the job summary as
"Rendering override: app", "... software" or "... default" for every `target=pdf` run (`app` means nothing is set). The Windows limits of
`check --target pdf` are the budgets of the software path; the CSVs of the first run (`Docs/measurements/pdf-viewer-2026-11-04/windows`)
were measured on Avalonia's default path and are kept as history, the Windows check runs on `rendering/software`. It is a developer
override, not a setting of the app. The runner group allows `measure.yml` only on `dev`.

```
dotnet publish src/Raffica.Pdf -c Release -r win-x64 -o artifacts/pdf/win-x64
dotnet run bench/shell-probe/measure.cs -- run --target pdf --exe artifacts/pdf/win-x64/RafficaPdf.exe --case warm --out artifacts/pdf/warm.csv
dotnet run bench/shell-probe/measure.cs -- size --target pdf --publish-dir artifacts/pdf/win-x64 --fonts-dir src/Raffica.Shell/Assets/Fonts --out artifacts/pdf/size.csv
dotnet run bench/shell-probe/measure.cs -- check --target pdf --platform windows --in "artifacts/pdf/*.csv" --summary artifacts/pdf/summary.md
```

**The limit table of the PDF app is recorded.** The budgets are the medians of the owner's runs of 2026-11-04 (commit `db90246`, 10 runs per
case, Windows and Linux, CSVs and machine notes in `Docs/measurements/pdf-viewer-2026-11-04/`), rounded up as in M1 (time to 10 ms, memory to
5 MiB, size to 1 MiB); the limit is the budget x 1.25 applied once. The single copy is `s_pdfBudgets` in `CheckCommand` of `measure.cs`, the
reasoning is the entry "Raffica PDF speed budgets (M2.1)" in `Docs/DECISIONS.md`. `scroll` is the median of the p99 of the frame gaps; `zip`
has a budget like the gallery's, because `size --check` judges size and zip together. macOS has none and stays indicative. A binding platform
with a table that lacks a limit still prints the medians and exits 2 ("no limits recorded for target pdf"); the selftest pins the recorded
values, runs `check` on the committed CSVs (both platforms must exit 0) and keeps the verdict logic under a test-only table. CI runs
`size --check <rid> --target pdf --publish-dir out/pdf --fonts-dir src/Raffica.Shell/Assets/Fonts` (step "Size of the published PDF app").

| | Windows budget / limit | Linux budget / limit |
|---|---|---|
| cold / warm (ms) | 400 / 500 and 390 / 487.5 | 230 / 287.5 and 230 / 287.5 |
| 1000 pages (ms) | 400 / 500 | 220 / 275 |
| second file (ms) | 30 / 37.5 | 40 / 50 |
| scroll p99 (ms) | 20 / 25 | 40 / 50 |
| memory (MiB) | 130 / 162.5 | 155 / 193.75 |
| published size / zip (MiB) | 51 / 63.75 and 22 / 27.5 | 48 / 60 and 22 / 27.5 |

Owner steps (what is still open):

1. Re-run the workflow with the recorded limits on both machines, the check step must pass: `gh workflow run measure.yml --ref <branch> -f platform=windows -f target=pdf -f note="<GPU, resolution, scaling, power>"` and the same with `platform=linux` (for the cold start after a reboot add `-f cases=cold-reboot`). Compare locally with `dotnet run bench/shell-probe/measure.cs -- check --target pdf --platform <windows|linux> --in "<CSVs>"` (exit 0 inside the limits, 1 above one; a miss is a finding, never a changed limit).
2. Measure the references on the same machines: the predecessor with `-f target=reference` (section "Measuring the reference app" below: startup to the first sharp page, the 1000-page file and a file handed to the running instance; the predecessor's script has no scroll or memory case) and Chrome by the method of the predecessor's comparison entry, and add the numbers to the entry "Raffica PDF speed budgets (M2.1)" with a finding for every case that is slower than the predecessor.
3. A new budget or a loosened limit is a new decisions entry and a change of `s_pdfBudgets` together with the selftest lines that pin it, never an edit to make a check pass.

## Measuring another commit (the `ref` input)

The workflow file runs from the dispatch ref (`--ref dev`; the runner group allows only `dev`), but the optional input `ref` names the commit
whose **app** is measured, for the merge commits of earlier milestones or a branch (for example to find where a speed case got slower):

```
gh workflow run measure.yml --ref dev -f platform=linux -f target=pdf -f note="<machine note>" -f ref=<40-hex commit or branch name>
```

- **What comes from `ref`:** only the app under test. The workflow checks `ref` out into `measured/` (without credentials) and runs
  `dotnet publish` from there into `out/app`; the size step reads the fonts of that checkout.
- **What stays on the dispatch ref (`dev`):** the workflow file, `bench/shell-probe/measure.cs` with its limit tables (`s_pdfBudgets` and the
  others) and the test files in `testdata/pdfs`. So the verdict of the check step is always against the current limits, and every commit is
  measured with the same files. An older commit that does not know a measurement switch of the current tool fails its case; that is a result, not a
  reason to change the tool.
- **Allowed values:** empty (the app of the dispatch ref, as before), a full 40-character lowercase commit id, or a plain branch name
  (`refs/heads/<name>` is accepted); never `pull/N/head`, `refs/pull/...`, `refs/tags/...` or a name with `..`. The check is the one of
  `linux-update-check.yml` and `windows-update-check.yml` and runs before anything is fetched; the value travels in an environment variable.
- **The target `reference` rejects `ref`:** it takes its own commit from `reference_ref` (pinned by SHA), so a non-empty `ref` with
  `-f target=reference` fails the first step.
- **What shows which commit was measured:** the run title (`Measure pdf on linux at <ref>`), the line `Measured app: commit <40-hex> (from ...)` in
  `machine.txt` of the artifact and in the job summary (the full commit id of the checkout, which matters for a branch name; with a 40-hex
  `ref` the step fails if the checkout is not exactly that commit). The line `Commit:` of `machine.txt` stays the dispatch ref, that is the
  commit of the tool and the limits.

## Measuring the reference app (the predecessor)

`measure.yml` with `-f target=reference` measures the predecessor of this suite on the same self-hosted machines, so that the numbers of
the PDF app (`Docs/DECISIONS.md`, "Raffica PDF speed budgets (M2.1)") can be compared with it by the same method on the same hardware.
The reference app is measured with **its own startup protocol** (its script `bench/startup/startup.cs`, run from the root of its checkout), not with
`measure.cs`, because the two apps have different measurement switches. Dispatch (the repository, the commit and the names are inputs, never
written into this repository):

```
gh workflow run measure.yml --ref dev -f platform=windows -f target=reference -f reference_repo=<owner/repo> -f reference_ref=<40-hex commit> -f reference_project=<project folder> -f reference_exe=<exe name> -f note="<machine note>"
```

The same with `-f platform=linux` (macOS is indicative only). `reference_ref` is a full 40-character commit SHA on purpose (a tag can move; find
it with `git rev-parse <tag>^{commit}` in a checkout of the reference), `reference_project` the project folder of its app inside its repository,
`reference_exe` the published executable without extension. The workflow validates the four inputs before anything is fetched, checks out
the reference at that commit into `reference/` without credentials, verifies that the checkout is exactly the pinned commit, and publishes
its app with `dotnet publish <project> -c Release -r <rid> -o out/reference` (Native AOT, nothing else is needed: the native OCR library
that the reference's CI fetches is for its tests only). Then it runs, with a real session and no display wrapper, from `reference/`:

| Key | Reference script case | Files (`testdata/pdfs`) | Runs |
|---|---|---|---|
| `cold` | `--case cold` | `text-basic.pdf` | 10, no warm-up, 5 s pause |
| `warm` | `--case warm --phases` | `text-basic.pdf` | 10 after 3 warm-up runs |
| `thousand` | `--case thousand` | `thousand-pages.pdf` | 10 after 3 warm-up runs |
| `second` | `--case second` | `text-basic.pdf`, then `hello_world.pdf` | 10 after 1 warm-up run |
| `samefiles-cold` | `--case cold` | `hello_world.pdf` | as `cold` |
| `samefiles-warm` | `--case warm` | `hello_world.pdf` | as `warm` |
| `samefiles-second` | `--case second` | `hello_world.pdf`, then `outline.pdf` | as `second` |

**What it produces:** the artifact `reference-<platform>-<run id>` with one CSV per key (columns `case,run,ms`, written by the reference's script),
`exit-codes.csv` (the script's exit code per key), `summary.md` and `machine.txt` (the machine note plus the repository, the commit and the
SHA-256 of the executable that was measured); the job summary shows the median, minimum, maximum and the number of runs per key and the published
size of `out/reference` without debug symbols. The reference script's own budget verdict (exit code 1 above its limit) is printed, kept in
`exit-codes.csv` and **never fails the job**; only exit code 2 (wrong invocation) or a missing CSV does. TEMP, TMP and TMPDIR point to the temp folder of the job (the
runner empties it), so a cached build of the file-based script from an earlier job can never run.

**How it compares with the PDF target.** The same measurement point (the moment the first sharp page of page 1 has been drawn and the frame after it, measured from
just before the process starts; for `second` measured inside the running instance from the arrival of the hand-over to the frame after
the first sharp tile of the new file) and the same isolation of settings and recent files. Compare per row:

| PDF target (`measure.cs --target pdf`) | Reference |
|---|---|
| `cold` (`hello_world.pdf`) | `samefiles-cold` (identical file); `cold` (`text-basic.pdf`) is the reference's own protocol |
| `warm` (`hello_world.pdf`) | `samefiles-warm`; `warm` is its own protocol |
| `thousand` (`thousand-pages.pdf`) | `thousand` (identical file) |
| `second` (`hello_world.pdf`, then `outline.pdf`) | `samefiles-second`; `second` is its own protocol |

The test files of both repositories with the same name are byte-identical (checked by SHA-256 for `text-basic.pdf`, `hello_world.pdf`, `outline.pdf` and
`thousand-pages.pdf`); the PDF target's `cold`, `warm` and `second` use `hello_world.pdf` and `outline.pdf`, not the reference's `text-basic.pdf`, which is why the
`samefiles-*` rows exist. Two differences remain and belong next to every comparison: (1) on Windows the reference renders in software by default, and the
measurement takes it as shipped, while the PDF app uses Avalonia's default (the GPU path); (2) the reference's script has no scroll and no memory case, so `scroll`
and `memory` have no reference row here. As for the PDF target, valid numbers need a GPU and an active display (the script warns when Windows
has no usable compositor rate; the annotation is on the run).

**Security.** The target runs the code of a second repository (its build and its published binary) on the owner's machine with the owner's rights. It is reachable only by a
manual dispatch, the commit is pinned by SHA, the dispatcher names the repository and no secret is used (see the header of `measure.yml`).
