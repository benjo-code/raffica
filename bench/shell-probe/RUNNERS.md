# Measurement runners

The workflow [`measure.yml`](../../.github/workflows/measure.yml) runs on self-hosted GitHub Actions runners that the
owner starts by hand on the machines to be measured (spec section 9.3). This guide describes the setup for each
operating system. A runner is never a service and never runs unattended.

| Machine | Platform label | Role |
|---|---|---|
| Owner's main PC (Windows 11) | `windows` | binding |
| HP ProBook (CachyOS, x64) | `linux` | binding |
| Mac (2016 Intel, macOS 15 via OpenCore Legacy Patcher) | `macos` | indicative only |

Every runner has two labels: `raffica-measure` and its platform label. The CI jobs of `ci.yml` use other labels, so they
can never land on these machines.

## Prerequisites

- **Windows:** Visual Studio Build Tools with the workload "Desktop development with C++". The Native AOT linker finds
  the tools through `vswhere.exe`; the workflow puts its folder on `PATH` for you. Git for Windows is needed by the
  checkout.
- **CachyOS:** `clang`, `zlib` (headers and library), `git`, and an X11 or XWayland session. Avalonia starts through
  XWayland or its default backend; under Wayland without XWayland the probe ends with an `error:` line instead of
  hanging.
- **macOS:** the Xcode command line tools (`xcode-select --install`; without them the Native AOT publish fails with "No developer tools were found").

The .NET SDK itself is installed by the workflow (`actions/setup-dotnet`) into the user's folder; no root rights are
needed.

## Register the runner

The runners of the project owner are organisation runners in the runner group `main-pc`, which is limited to this
repository and to `measure.yml` on `dev`. A runner of the repository itself works the same way with the repository URL.

1. In the organisation, open Settings → Actions → Runners → New runner and choose the operating system.
2. Follow the download steps shown there, into a folder of your own (not inside a checkout of this repository).
3. Configure it with the label `raffica-measure` (GitHub adds `Windows`, `Linux` or `macOS` itself), the runner group
   and the one-time token that the page shows:

   ```
   ./config.sh --url https://github.com/benjo-code --token <one-time token> --runnergroup main-pc --labels raffica-measure
   ```

   On Windows use `config.cmd` with the same arguments. The token is valid for about an hour and is used once. Never put
   it in a file of the repository, a script or a chat.

## Start it by hand

Start the runner in the **logged-in desktop session**, on **mains power**, with the **display on**:

- Windows: `run.cmd` in a terminal of the desktop session.
- Linux and macOS: `./run.sh` in a terminal of the desktop session.

Not as a service, and not over SSH or a remote session. A session without an active display gives the compositor's
frame rate instead of the probe's: the numbers are then invalid. The script warns about this before the runs and next
to the result (and as an annotation on the workflow run), but it cannot fix it. Close other heavy programs, and let the
machine sit idle while the workflow runs (a measurement takes about ten minutes).

Then start the measurement, for example:

```
gh workflow run measure.yml -f platform=windows -f note="<GPU, resolution, scaling, power mode>"
```

By default the workflow measures the gallery app's frame (`-f target=gallery`) and ends with a check against the limits
of the decisions entry "Speed targets of the frame (M1)", shown in the job summary; `-f target=probe` measures the
throwaway shell probe as before. `-f target=pdf` measures the PDF app (cases cold, warm, 1000 pages, second file, scroll, memory, size; see the section "Measuring the PDF app" in `README.md`); its check step fails with "no limits recorded for target pdf" until the owner has recorded the budgets. `-f target=reference` builds and measures the predecessor (a second repository, pinned by commit) with its own startup protocol, for the comparison with the PDF app on the same machine; it needs the extra inputs `reference_repo`, `reference_ref`, `reference_project` and `reference_exe` and runs that repository's code with the owner's rights (see "Measuring the reference app" in `README.md`). It has no check step and never fails on the reference's own budget.

The optional input `-f ref=<40-hex commit or branch name>` measures the app of another commit (for example an earlier merge commit) while the
workflow, the measuring tool, its limits and the test files stay those of the dispatch ref `dev`; it is not for `target=reference` (see "Measuring another commit"
in `README.md`).

The run waits until the runner of that platform is online. For the cold start after a reboot (spec 9.1) reboot the
machine, start the runner, and run `-f platform=<platform> -f note="…" -f cases=cold-reboot`.

## Stop it

Press Ctrl+C in the terminal of the runner after the run. Do not leave it online. To remove it entirely, use
Settings → Actions → Runners and `./config.sh remove` with a new token.

## Recommended repository setting

Once the repository is public: in Settings → Actions → General → "Fork pull request workflows from all external contributors",
choose "Require approval for all external contributors". `measure.yml` cannot be started from a pull request in any case
(it has `workflow_dispatch` only), but the setting keeps fork workflows of other files off any self-hosted runner. This
is a GitHub settings change that only the owner makes.

This approval setting only gates a run (somebody must click); it is not the boundary. The boundary is the runner group:
a group that is restricted to named workflows (`release.yml` on `main` and `dev` for `raffica-ci`) cannot be reached by a
fork pull request at all, because a fork controls the workflow file of its own pull request and can name any
`runs-on`. See `packaging/README.md`, "Making the repository public", for the settings in order.
