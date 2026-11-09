# Packaging and release: owner checklists

This file lists the steps that only the owner can do (a key, a secret, a repository setting, a release). The tools and
the workflow are described in their own headers: `tools/package.cs`, `tools/update-testserver.cs`, `tools/licenses.cs`,
`.github/workflows/release.yml`. Nothing here is done by a pull request.

## Signing key for updates

The update manifest of a release is signed with an ECDSA P-256 key. The app trusts only the public keys compiled into
`src/Raffica.Core/Updates/UpdateTrust.cs`; it holds the public half of the key `release-1` (the private half is the secret
of the environment `release-signing`). Before the key was entered it held a placeholder key whose private half was discarded.

1. Create the key outside every Git working tree (the tool refuses a path inside one and never overwrites a file):

   ```
   dotnet run tools/package.cs -- update-keygen --id release-1 --private-out <path outside the repository>
   ```

   The private half goes into the given file only (readable only by you); the tool prints the public half as a ready
   `new UpdateKey(...)` entry for `UpdateTrust.Keys`.
2. Store the content of the private file as the secret `RAFFICA_UPDATE_SIGNING_KEY` of the environment `release-signing`
   (see below), keep one copy in your password manager, then delete the file. Never paste the key into a file, an issue,
   a pull request or a log.
3. Enter the public half in `src/Raffica.Core/Updates/UpdateTrust.cs`: replace the placeholder entry and keep the id
   `release-1`, which is the `UPDATE_KEY_ID` of the job `manifest` in `release.yml`. For a later key rotation add the new
   key next to the old one first and only then change `UPDATE_KEY_ID`.
4. Check: `dotnet run tools/package.cs -- update-verify --keys-from-source` exits 0 (it fails while only the placeholder
   is present), and `RAFFICA_REQUIRE_RELEASE_KEY=1 dotnet test --project tests/Raffica.Core.Tests -c Release -- --filter-method "Raffica.Core.Tests.Updates.UpdateTrustTests.ReleaseKeysAreNotThePlaceholderWhenReleasing"` passes.

## Environment release-signing

The job `manifest` of `release.yml` runs on a GitHub-hosted runner behind the environment `release-signing`. Create it in
the repository settings (Settings, Environments):

- Deployment branches: only `main` and `dev`.
- Required reviewers: the owner.
- Environment secret: `RAFFICA_UPDATE_SIGNING_KEY` (not a repository or organisation secret). The secret appears in
  exactly one place of the workflow, the step "Sign the update manifest"; `package.cs selftest` keeps it so.
- Check: `gh api repos/benjo-code/raffica/environments/release-signing` shows the branch policy and the reviewer.

## Making the repository public

The updater and the download links use `https://github.com/benjo-code/raffica/releases`, which anonymous users can read
only when the repository is public. The OCR library is fetched from a prerelease of the same repository
(`tools/native-fetch.cs`), which needs a token as long as the repository is private (anonymous calls are limited to 60 API
calls per hour once it is public).

The boundary that keeps a fork pull request off the owner's machines is the runner group setting, not a workflow
expression: a fork controls the workflow file of its own pull request and can name any `runs-on`. The routing of `ci.yml`
and `roadmap.yml` to hosted runners (public repository or fork) is a convenience on top of it. `release.yml` refuses any
`ref` that is not `main`, `dev` or a commit on one of them; `measure.yml` and the two update-check workflows, which build the
`ref` input on the owner's desktop and laptop, accept a branch of this repository or a commit on `main` or `dev` and refuse the
head commit of a fork's pull request. None of the settings below is changed by a commit; the owner
applies them, in this order:

1. Make the signing key and the environment ready (the two sections above) and enter the public key in `UpdateTrust.cs`.
2. Runner group `raffica-ci` (organisation settings, Actions, Runner groups): restrict the workflows to
   `benjo-code/raffica/.github/workflows/release.yml@refs/heads/dev`. GitHub refuses an entry whose file does not exist at
   that ref, and `main` has no workflow file until the first release pull request (`release/vX.Y.Z` to `main`) has been
   merged: add `benjo-code/raffica/.github/workflows/release.yml@refs/heads/main` right after that merge and before the
   first `release.yml` run on `main` (without it that run finds no runner of the group).
   The group `Default` stays private-only (no access for public repositories). Until the visibility flip (step 5) `ci.yml`
   and `roadmap.yml` still run on the self-hosted runners of this group, so do steps 2 to 5 in one sitting: a pull request
   opened in between waits for a runner.
3. Repository settings, Actions, General, "Fork pull request workflows from outside collaborators": "Require approval for
   all external contributors".
4. Turn the wiki and projects off (Settings, General, Features).
5. Change the visibility (Settings, General, Danger Zone). This is a decision of the owner.
6. Right after the flip: turn on private vulnerability reporting (Settings, Code security), Dependabot alerts and
   Dependabot security updates, secret scanning and push protection.
7. Tag ruleset for `v*` (Settings, Rules, Rulesets): no update, no deletion, creation only by administrators.
8. Actions policy (Settings, Actions, General): "Require actions to be pinned to a full-length commit SHA".
9. Required reviewer on the environment `release-signing` (see "Environment release-signing"; protection rules need a
   public repository or a paid plan).
10. Open a test pull request from a fork (or a branch of another account) and check that `ci`, `changes` and `roadmap`
    ran on GitHub-hosted runners (`gh run view <id> --json jobs --jq '.jobs[].runnerName'` shows `GitHub Actions N`, not a
    machine of the owner).
11. Check: `gh repo view benjo-code/raffica --json visibility` prints `PUBLIC`, and
    `curl -sI https://api.github.com/repos/benjo-code/raffica/releases/latest` answers without a token.

## Manual update check

The app never connects anywhere except for the update check against the addresses of `UpdateTrust.cs`. To try the updater
before a real release, build two versions as a test build: `-p:RafficaUpdateTestBuild=true -p:RafficaUpdateTestPort=<port>
-p:RafficaUpdateTestKey=<public test key>` swaps `UpdateTrust.cs` for `UpdateTrust.TestBuild.cs`, which trusts only that
key and a server on `127.0.0.1`. No release build sets these properties (`package.cs selftest` checks it).

1. Create a throwaway test key (`package.cs update-keygen --id test-1 --private-out <path outside the repository>`) and
   publish two versions, for example 0.0.1 and 0.0.2, with the test-build properties above.
2. Build the release files of the newer version, then `dotnet run tools/update-testserver.cs -- prepare --version 0.0.2
   --key <test key file> --out <new folder> <release files...>` and `... -- serve --root <folder> --port <port>`.
   `--tamper signature` and `--tamper checksum` produce the error cases.
3. Windows: the workflow `windows-update-check.yml` does the setup for you (`packaging/windows/update-check.ps1`): it builds two test
   installers with a throw-away key, serves the new one on 127.0.0.1, installs the old one into a test folder (below HKCU, as
   installer-smoke does, and removed again at the end; it refuses to run when Raffica PDF is installed), starts it on the desktop
   with its own `APPDATA` and `LOCALAPPDATA` and waits for your click on "Install"; then it checks the version of the exe, that the
   staging folder is gone and that the new version runs. Start the runner in the logged-in desktop (`run.cmd`; it needs the .NET SDK
   and the Visual Studio C++ build tools for Native AOT), then `gh workflow run windows-update-check.yml --ref dev` (`-f ref=<branch>`
   for the code of a branch). The script was tried on the development machine with the install done by hand. By hand, without the
   workflow: start the older version (installed from its setup) and check that it offers, downloads and verifies the newer
   one, then "Install" restarts into the new version with the tabs back (Ctrl+K, "About" shows the version). The helper mode can also be driven without the
   interface: `dotnet run tools/package.cs -- update-smoke <RafficaPdf-0.0.1-win-x64-setup.exe> <RafficaPdf-0.0.2-win-x64-setup.exe> --key <test key file>`
   (per-user, in isolated folders; it passed once on the development machine). Limits: it does not exercise a real UAC
   prompt (a per-machine install), and the helper does not hand `--settings-file` on, so a run started with that switch
   cannot complete an update.
4. Linux: the AppImage updater has been unit-tested; the script below was tried in a container with a virtual display and FUSE, with
   the install done by hand, so the real click and the restart need a desktop. On the owner's laptop the workflow `linux-update-check.yml` does the setup for you (`packaging/linux/update-check.sh`): it builds
   two test AppImages with a throw-away key, serves the new one on 127.0.0.1, starts the old one through the real FUSE mount
   and waits for your click on "Install"; then it checks the file (SHA-256, mode, no `.part` leftover, a new process, FUSE start).
   Start the runner in the desktop session (`./run.sh` in `~/actions-runner`), install `squashfs-tools`, `clang` and the .NET SDK,
   then `gh workflow run linux-update-check.yml --ref dev` (`-f ref=<branch>` for the code of a branch). By hand, without the
   workflow: copy the older AppImage somewhere writable,
   run it without `--settings-file` (the restarted program does not get it and would not find the saved tabs; to keep your own
   settings out of it set `XDG_CONFIG_HOME` and `XDG_DATA_HOME` to empty folders instead), and check that the title bar shows "Update available", that "Install" replaces the file (`ls -l` shows the new
   timestamp, the mode stays executable) and that the new version starts with the tabs back (Ctrl+K opens the command search: "Check for updates" asks again, "About" shows the version). The `.deb` and the portable
   ZIP only point to the download page; check that "Install" is absent there.
5. Delete the test key file afterwards.

## Dry run of the release workflow

Run green on 2026-11-08 (run 42422270038, `dev` commit 4a31a74b81563f6015dc773ebe07a7b12ab16ca9; the first run, 42418844306,
had failed in the Linux part of the packaging selftest and was fixed by PR #37). It builds the Windows and Linux files,
runs every smoke test (installer, `.deb`, AppImage, CLI, licenses) and uploads nothing; with `upload=false` the key check
is skipped and the job `release-manifest` does not run.

1. Push the branch you want to build. The workflow file must exist on `dev` (a workflow can only be dispatched when the
   default branch has it).
2. Start it from `dev`, with the branch or the 40-character commit SHA as `ref` (with a PR branch use its commit SHA):

   ```
   gh workflow run release.yml --ref dev -f tag=v0.1.0 -f ref=<40-hex commit on dev or main> -f upload=false
   ```

   Dispatching with `--ref dev` and a commit SHA as `ref` is verified to work. Since M2.7 the preflight, Windows and
   Linux jobs refuse a `ref` that is neither `main`, `dev` nor a commit on one of them (a commit of a PR branch that is not
   merged yet is refused too), so merge to `dev` first and pass its head.
3. Watch it: `gh run watch`. Check that the jobs `release-preflight`, `release-windows` and `release-linux` are green and
   `release-manifest` is skipped. The Linux job runs the container build (`packaging/linux/container-build.sh`,
   Ubuntu 22.04) with `deb-smoke` and `appimage-smoke`.

## Open checks on real machines

Each item below was not verified during Milestone 2.6; the command or step says how to check it. Done and not listed any
more: the release dry run, the container build with `deb-smoke` and `appimage-smoke`, `desktop-file-validate` and
`StartupWMClass=RafficaPdf` on X11 (see `Docs/DECISIONS.md`, "Raffica PDF: packaging and release (M2.6)", "Dry run and Linux build").

| Item | Check |
| --- | --- |
| `StartupWMClass=RafficaPdf` matches the window on Wayland | on a Wayland desktop run `RafficaPdf` and look up the window's `app_id` with the compositor's tool; it must be `RafficaPdf` (X11 is confirmed: `xprop WM_CLASS` returned "RafficaPdf", "RafficaPdf"), otherwise fix `packaging/linux/raffica-pdf.desktop` and `src/Raffica.Core/Distribution/DesktopEntry.cs` |
| AppImage on a real desktop (FUSE mount) and its update | run the AppImage without `APPIMAGE_EXTRACT_AND_RUN`, then step 4 of "Manual update check" (the updater's unit tests passed 10 of 10 on real Linux) |
| `.deb` on a real Debian or Ubuntu machine | `sudo apt install ./raffica-pdf_<v>_amd64.deb`, open a PDF with `raffica-pdf`, `sudo apt remove raffica-pdf`; check the menu entry and icon |
| Per-machine install through the helper (real UAC prompt, declined prompt) | install the older setup for all users, run the update, accept and then decline the prompt |
| Default app on real desktops | Windows: the first-start card, "Set as default", the system page opens; Linux: `xdg-mime query default application/pdf` prints `raffica-pdf.desktop` |
| Printing on real printers | Windows print dialog and a CUPS printer (`RAFFICA_TEST_CUPS_PRINTER=<name> dotnet test tests/Raffica.Pdf.Tests -c Release --filter-namespace "*Platform*"`) |
| Speed and size on the binding machines | `gh workflow run measure.yml --ref <branch> -f platform=windows (or linux) -f target=pdf -f note="<machine>"`, and `dotnet run bench/shell-probe/measure.cs -- size --check <rid> --target pdf --publish-dir <dir> --fonts-dir src/Raffica.Shell/Assets/Fonts` (the Windows size was 68.32 MiB against the budget of 66 MiB, see `Docs/DECISIONS.md`, "Raffica PDF: packaging and release measurements (M2.6)") |

The container build by hand (the same call as the step "Build, package and smoke-test in ubuntu:22.04" of `release.yml`; run it
in the repository root on a Linux host with Docker, and export `GITHUB_TOKEN` first while the repository is private):

```
export DOTNET_INSTALL_DIR=$HOME/.dotnet      # the .NET SDK of global.json must be installed there
export UBUNTU_IMAGE=ubuntu:22.04@sha256:076e7bf82319619f28dd0aa9c02ab34b055ad37100395b4f49a2ffba04fb5e11   # the digest of release.yml
dotnet run tools/native-fetch.cs -- --rid linux-x64 --out out/native
docker run --rm --name raffica-release-build   -v "$PWD:/work" -v "$DOTNET_INSTALL_DIR:/dotnet:ro" -w /work   -e VERSION=0.1.0 -e DEBVERSION=0.1.0 -e "HOST_UID=$(id -u)" -e "HOST_GID=$(id -g)"   "$UBUNTU_IMAGE" bash packaging/linux/container-build.sh
```

The files end up in `out/release`.

## Replacing the icons

The icons are the brand marks in `Docs/design/icons/brand/` (see `Docs/design/icons/README.md`); only the PDF mark is packaged.

1. To change the mark, replace `Docs/design/icons/brand/pdf.png` (1024x1024, transparent outside the squircle), then derive the sizes as described in
   `Docs/design/icons/README.md` and overwrite `src/Raffica.Pdf/Assets/raffica-pdf.ico` and the files under
   `Docs/design/icons/linux/hicolor/<n>x<n>/apps/raffica-pdf.png`.
2. The same `.ico` is used twice: by the installer (`SetupIconFile`) and, through `<ApplicationIcon>` in
   `src/Raffica.Pdf/Raffica.Pdf.csproj`, as the icon of `RafficaPdf.exe` itself (file icon in Explorer, taskbar, Start menu
   and the PDF file icon). Nothing else needs changing; rebuild after overwriting the file.
3. Check: `dotnet run tools/package.cs -- selftest`, then build the packages and look at the installer, the start menu entry
   and the Linux menu entry.

## First release

Before the first release: the signing key, the environment and the repository visibility are done, `CHANGELOG.md` has
the version, and `dotnet run tools/package.cs -- selftest` passes on the owner's runners.

1. Dry run (section "Dry run of the release workflow" above), then continue only when it is green.
2. Create the release (this is the release procedure of `CLAUDE.md`), then attach the files and the signed manifest:

   ```
   gh release create vX.Y.Z --target main --title "vX.Y.Z" --notes "<CHANGELOG section>"
   gh workflow run release.yml --ref main -f tag=vX.Y.Z -f upload=true
   ```

   The workflow checks that the tag is on `main` or `dev` and that `UpdateTrust.cs` trusts a real key, builds the Windows
   and Linux files, and the job `manifest` (after your approval of `release-signing`) signs `update-manifest.json`.
3. Check: the release lists `RafficaPdf-X.Y.Z-win-x64-setup.exe`, `RafficaPdf-X.Y.Z-win-x64-portable.zip`,
   `RafficaPdf-X.Y.Z-x86_64.AppImage`, `raffica-pdf_X.Y.Z_amd64.deb`, `update-manifest.json`, `update-manifest.json.sig`,
   `SHA256SUMS-windows.txt` and `SHA256SUMS-linux.txt`, and
   `dotnet run tools/package.cs -- update-verify --manifest update-manifest.json --sig update-manifest.json.sig --keys-from-source`
   accepts the downloaded manifest.
