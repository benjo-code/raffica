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
(`TopLevel.RequestAnimationFrame`, the same method as Pagewing's `first-sharp`).

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
