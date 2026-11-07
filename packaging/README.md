# Packaging and release: owner checklists

This file lists the steps that only the owner can do (a key, a secret, a repository setting, a release). The tools and
the workflow are described in their own headers: `tools/package.cs`, `tools/update-testserver.cs`, `tools/licenses.cs`,
`.github/workflows/release.yml`. Nothing here is done by a pull request.

## Signing key for updates

The update manifest of a release is signed with an ECDSA P-256 key. The app trusts only the public keys compiled into
`src/Raffica.Core/Updates/UpdateTrust.cs`; until the first release that file holds a placeholder key whose private half
was discarded, so no manifest verifies and no update is accepted.

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
(`tools/native-fetch.cs`), which needs a token as long as the repository is private.

1. Make the signing key and the environment ready (the two sections above) and enter the public key in `UpdateTrust.cs`.
2. Change the visibility in the repository settings (Settings, General, Danger Zone). This is a decision of the owner.
3. Check: `gh repo view benjo-code/raffica --json visibility` prints `PUBLIC`, and
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
3. Windows: start the older version (installed from its setup) and check that it offers, downloads and verifies the newer
   one, then "Install" restarts into the new version with the tabs back. The helper mode can also be driven without the
   interface: `dotnet run tools/package.cs -- update-smoke <RafficaPdf-0.0.1-win-x64-setup.exe> <RafficaPdf-0.0.2-win-x64-setup.exe> --key <test key file>`
   (per-user, in isolated folders; it passed once on the development machine). Limits: it does not exercise a real UAC
   prompt (a per-machine install), and the helper does not hand `--settings-file` on, so a run started with that switch
   cannot complete an update.
4. Linux: the AppImage updater has only been unit-tested. On a Linux desktop copy the older AppImage somewhere writable,
   run it, and check that the title bar shows "Update available", that "Install" replaces the file (`ls -l` shows the new
   timestamp, the mode stays executable) and that the new version starts with the tabs back. The `.deb` and the portable
   ZIP only point to the download page; check that "Install" is absent there.
5. Delete the test key file afterwards.

## Dry run of the release workflow

Not run yet (open). It builds the Windows and Linux files, runs every smoke test (installer, `.deb`, AppImage, CLI,
licenses) and uploads nothing; with `ref` set the key check is skipped and the job `manifest` does not run.

1. Push the branch you want to build.
2. Start it:

   ```
   gh workflow run release.yml --ref <branch> -f tag=v0.1.0 -f ref=<branch> -f upload=false
   ```

   If the runner group only accepts `main` and `dev` (the workflow header says it names branch refs only), merge the
   branch into `dev` first and use `--ref dev -f ref=<branch>` instead.
3. Watch it: `gh run watch`. Check that the jobs `preflight`, `windows` and `linux` are green. The `linux` job runs the
   container build (`packaging/linux/container-build.sh`, Ubuntu 22.04) with `deb-smoke` and `appimage-smoke`; this is the
   first time those run, because they could not be run on the development machine.

## Open checks on real machines

Each item below was not verified during Milestone 2.6; the command or step says how to check it.

| Item | Check |
| --- | --- |
| Linux packages build and pass their smokes | the dry run above, or `docker run --rm -v "$PWD:/w" -w /w -e VERSION=0.1.0 -e DEBVERSION=0.1.0 -e HOST_UID -e HOST_GID ubuntu:22.04 bash packaging/linux/container-build.sh` |
| `StartupWMClass=RafficaPdf` matches the window | on a Linux desktop run `RafficaPdf`, run `xprop WM_CLASS` and click its window; the second string must be `RafficaPdf`, otherwise fix `packaging/linux/raffica-pdf.desktop` and `src/Raffica.Core/Distribution/DesktopEntry.cs` |
| Desktop file is valid | `desktop-file-validate packaging/linux/raffica-pdf.desktop` |
| AppImage update | step 4 of "Manual update check" |
| Per-machine install through the helper (real UAC prompt, declined prompt) | install the older setup for all users, run the update, accept and then decline the prompt |
| Default app on real desktops | Windows: the first-start card, "Set as default", the system page opens; Linux: `xdg-mime query default application/pdf` prints `raffica-pdf.desktop` |
| Printing on real printers | Windows print dialog and a CUPS printer (`RAFFICA_TEST_CUPS_PRINTER=<name> dotnet test tests/Raffica.Pdf.Tests -c Release --filter-namespace "*Platform*"`) |
| Speed and size on the binding machines | `gh workflow run measure.yml --ref <branch> -f platform=windows (or linux) -f target=pdf -f note="<machine>"`, and `dotnet run bench/shell-probe/measure.cs -- size --check <rid> --target pdf --publish-dir <dir> --fonts-dir src/Raffica.Shell/Assets/Fonts` (the Windows size was 68.32 MiB against the budget of 66 MiB, see `Docs/DECISIONS.md`, "Raffica PDF: packaging and release measurements (M2.6)") |

## Replacing the placeholder icon

The icons are placeholders generated by `tools/make-placeholder-icon.cs`; there is no brand mark yet.

1. Follow `Docs/design/icons/README.md`: export the mark as PNG in the sizes listed there and as one multi-size `.ico`,
   and overwrite `src/Raffica.Pdf/Assets/raffica-pdf.ico` and the files under
   `Docs/design/icons/linux/hicolor/<n>x<n>/apps/raffica-pdf.png`.
2. The `.ico` is used by the installer only. To give `RafficaPdf.exe` itself the icon (the file icon in Explorer and the
   taskbar of the portable form), add `<ApplicationIcon>Assets/raffica-pdf.ico</ApplicationIcon>` to
   `src/Raffica.Pdf/Raffica.Pdf.csproj`; this is not done yet.
3. Check: `dotnet run tools/package.cs -- selftest`, then build the packages and look at the installer, the start menu entry
   and the Linux menu entry.

## First release

Before the first release: the signing key, the environment and the repository visibility are done, `CHANGELOG.md` has
the version, and `dotnet run tools/package.cs -- selftest` passes on the owner's runners.

1. Dry run (see the section below), then continue only when it is green.
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
