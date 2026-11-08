# Raffica

Raffica ("gust" in Italian) is a fast, open-source office suite for Windows, Linux and macOS: Raffica Office with Write, Sheets, Slides and PDF. It grows out of the PDF editor [Pagewing](https://github.com/benjo-code/Pagewing); speed (startup, opening, scrolling) is the core promise.

**Status:** in development. The first app, **Raffica PDF**, is feature-complete in the code for Windows and Linux: view, search, print, annotate, fill forms, add a visible signature, organize pages, redact, recognize text in scans (OCR) and correct lines of text; English and German. Write, Sheets and Slides do not exist yet. See [Docs/ROADMAP.md](Docs/ROADMAP.md). **No release has been published yet**, so there is nothing to download today; the sections below describe what the first release will contain. The Windows installer and ZIP were built and smoke-tested on one development machine; the Linux packages (`.deb`, AppImage) were built and smoke-tested in a container (Ubuntu 22.04) and in the release workflow dry run, but have **not yet been installed or run on a real Linux desktop** (open items in [packaging/README.md](packaging/README.md)).

## Install (Raffica PDF)

| Platform | Package | Notes |
| --- | --- | --- |
| Windows (x64) | `RafficaPdf-<version>-win-x64-setup.exe` | Per-user installer (no administrator rights needed), English and German. Registers Raffica PDF for PDF files; your settings stay when you uninstall. |
| Windows (x64) | `RafficaPdf-<version>-win-x64-portable.zip` | Unpack anywhere and run `RafficaPdf.exe`. Nothing is registered until you accept the default-app card ("Set as default"); then entries under `HKCU` point at the unpacked folder, and deleting the folder does not remove them. Run `RafficaPdf.exe --unregister-file-types=user` before you delete it. |
| Linux (x86_64) | `raffica-pdf_<version>_amd64.deb` | Needs glibc 2.35 or newer (Ubuntu 22.04 or newer, current Debian) and `libfontconfig1`, `libx11-6`, `libice6`, `libsm6` and `libssl3`, which `apt` installs; `libcups2` (printing) and `xdg-utils` are recommended. Installs to `/usr/lib/raffica-pdf` and adds the commands `raffica-pdf` and `raffica`. |
| Linux (x86_64) | `RafficaPdf-<version>-x86_64.AppImage` | One file; `chmod +x` it and run it. Needs no `libfuse2`. |
| macOS | none yet | macOS comes after the PDF app; there is no package. |

The files come from the [releases page](https://github.com/benjo-code/raffica/releases). A `SHA256SUMS-windows.txt` and a `SHA256SUMS-linux.txt` list the checksums.

**Windows.** The files are not signed with a code-signing certificate (Authenticode), so Windows SmartScreen shows "Windows protected your PC" when you start the installer or the program for the first time. Choose "More info", then "Run anyway". Only do this for a file you downloaded from the releases page above.

**Linux.** The AppImage is not integrated into the desktop by itself; when it starts, Raffica PDF offers to make itself the default PDF viewer (until you choose "Don't ask again"), which also writes a menu entry and icons into your user folder. Other desktops may need `chmod +x` in the file properties first. Printing uses CUPS.

**Default PDF viewer.** When it starts and another program is the default, Raffica PDF asks whether it should be your default viewer for PDF files, at most once per start; "Not now" asks again at the next start, "Don't ask again" stops the question (also under Settings and in the command palette, "Make Raffica the default app for PDFs"). Windows does not allow a program to choose for you: the app registers itself and opens the "Default apps" page of the system settings where you confirm. On Linux it uses `xdg-mime`.

## Updates

Raffica PDF looks for a new version at most every 24 hours, after the first page is on screen. It talks only to GitHub: the program asks `api.github.com` for the latest release of this repository and fetches the release information and files from `github.com`, which hands them over from GitHub's release storage. The addresses are fixed in the program; there is no other network use and no telemetry. Switch it off under Settings, "Check for updates automatically"; with the switch off the program opens no connection at all (the command "Check for updates" in the command palette still checks on request).

- **Installer and AppImage:** a button "Update available" appears in the title bar. After you confirm, the program downloads the new version, checks it and installs it; it asks about unsaved changes in every tab first and restarts with your tabs.
- **`.deb` and portable ZIP:** these forms cannot update themselves. The program only points you to the download page.
- **What is checked:** the signature of the release information, and the size and SHA-256 of the downloaded file. Nothing is installed if one of them does not match.
- Until the first release is published, no update is offered.

## Command line

`RafficaPdf` (the app; on Linux `raffica-pdf`) takes the files to open as arguments, and these switches: `--lang en|de`, `--theme light|dark|system`, `--high-contrast`, `--settings-file <path>` (keeps all settings, recent files and signatures next to that file and gives the instance its own identity; for tests and measurements), `--print-string <key>`, `--register-file-types=user|machine` and `--unregister-file-types=user|machine` (Windows, used by the installer), and the measurement switches `--measure-startup`, `--measure-second`, `--measure-scroll` and `--measure-memory` (each needs `--settings-file`). A second start with a file opens it in the running window. The switches `--finish-update`, `--after-process` and `--restore-session` belong to the updater and are not meant to be typed.

`raffica` is a separate command-line tool that works on PDF files without a window: `raffica info | render | text | search | rotate | merge | split | export | redact | ocr`. `raffica --help` lists every option. It ships in the Windows packages (folder `cli`) and in the `.deb` (`/usr/bin/raffica`).

## Build from source

Prerequisite: the .NET 10 SDK (`global.json` pins the version). On Linux, Native AOT publishing also needs `clang` and `zlib1g-dev`; on Windows, the Visual Studio C++ build tools (and the folder `Microsoft Visual Studio\Installer` on the `PATH`, so that the AOT publish finds `vswhere`).

```
dotnet build Raffica.slnx -c Release
dotnet run tools/native-fetch.cs -- --rid win-x64 --out artifacts/native/win-x64     # linux-x64 on Linux; fetches the OCR library (no token needed)
dotnet test Raffica.slnx -c Release --no-build --max-parallel-test-modules 1         # set RAFFICA_REQUIRE_OCR=1 to fail instead of skip without the OCR library
dotnet format Raffica.slnx --verify-no-changes
dotnet publish src/Raffica.Pdf -c Release -r win-x64 -o out/pdf                      # the PDF app, Native AOT (linux-x64 on Linux)
dotnet publish src/Raffica.Cli -c Release -r win-x64 -o out/cli                      # the command line tool, Native AOT
```

The OCR library comes from a release of this repository and needs no token. Without a token GitHub allows 60 API calls per hour per address, which is plenty for this step; set `GH_TOKEN` only if you run it many times.

The packages are built by `tools/package.cs` and the workflow `.github/workflows/release.yml`; the owner's steps for a release are in [packaging/README.md](packaging/README.md). Decisions are in [Docs/DECISIONS.md](Docs/DECISIONS.md), the change history in [CHANGELOG.md](CHANGELOG.md).

## License

GPL-3.0-or-later, like Pagewing – see [LICENSE](LICENSE). The notices of the third-party components are in `third-party/` and shown in the About card of Raffica PDF.
