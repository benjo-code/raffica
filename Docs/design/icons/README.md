# App icons

`brand/` holds the brand marks, one 1024x1024 PNG per app: `suite.png` (Raffica), `write.png`, `sheets.png`, `slides.png` and `pdf.png`. They are transparent outside the rounded square (a margin of 40 px on every side).

## The mark

- A rounded square in the app's accent colour (the light-theme value of `Raffica.Color.Ink.*` in `src/Raffica.Shell/Styles/Tokens.axaml`): Write `#1D4F91`, Sheets `#1E6B47`, Slides `#A3420F`, PDF `#5E1A3E`. The suite mark is `#1B1D21`.
- The two-letter monogram in off-white `#F7F7F5`, italic and bold: `Wr`, `Sh`, `Sl`, `Pd`. The suite mark has a single large `R`.
- Wind streaks run in from the left and meet the letters: three streaks in the dark-theme ink of the app (Write `#8DB4EE`, Sheets `#7CCBA2`, Slides `#F0A374`, PDF `#E5A4C5`), four streaks in the four app colours for the suite. They are the shared "Raffica" element: the gust that pulls the letters forward.
- All marks share one geometry (same letter height and placement in the four app marks, same streak thickness, positions and slant). No Pagewing artwork is used.

## Where the marks come from

The letters are taken from generated artwork that the owner supplied. The canvas, the colours, the streaks and the placement were rebuilt so the marks are consistent and the streaks meet the letter contour without a gap. The generated sources are not kept in the repository; `brand/*.png` are the masters.

## What the packaging reads

| File | Used by |
| --- | --- |
| `src/Raffica.Pdf/Assets/raffica-pdf.ico` | the Windows installer (`SetupIconFile` in `packaging/windows/raffica-pdf.iss`, passed as `/DIconFile` by `tools/package.cs installer`) and `RafficaPdf.exe` itself (`<ApplicationIcon>` in `src/Raffica.Pdf/Raffica.Pdf.csproj`: file icon, taskbar, Start menu, PDF file icon) |
| `Docs/design/icons/linux/hicolor/<n>x<n>/apps/raffica-pdf.png` | the Debian package (`/usr/share/icons/hicolor`) and the AppImage (the 256x256 file is also the root icon); sizes 16, 24, 32, 48, 64, 128, 256 and 512 |
| `brand/pdf.png` itself | `dotnet run tools/package.cs -- macos-app` (macOS only) makes `Contents/Resources/RafficaPdf.icns` of the bundle from it on every run; no binary is kept in the repository |

The `.ico` and the PNGs are derived from `brand/pdf.png`; the macOS `.icns` is built from it at packaging time. The `.ico` holds the sizes 16, 24, 32, 48, 64, 128 and 256 (PNG entries, 256 stored as 0 in the directory). Only the PDF mark is packaged until the other apps exist.

## How to derive the files

1. Resize `brand/pdf.png` to each size with a Lanczos filter (`Pillow`: `Image.resize((s, s), Image.LANCZOS)`); below 48 px add a light unsharp mask (radius 0.6, 60 percent) so the letters keep their edge.
2. Save the PNGs of 16, 24, 32, 48, 64, 128, 256 and 512 px to `linux/hicolor/<n>x<n>/apps/raffica-pdf.png` (keep the file name; `tools/package.cs` and the `.desktop` file, `Icon=raffica-pdf`, depend on it).
3. Write the `.ico` from the first seven of them (the 256 px entry has width and height 0 in its directory entry).
4. The macOS icon needs no step of its own: `package.cs macos-app` resizes `brand/pdf.png` with `sips -z` into an `.iconset` (16, 32, 128, 256 and 512 px at 1x and at 2x, named `icon_<n>x<n>.png` and `icon_<n>x<n>@2x.png` as `iconutil` requires) and packs it with `iconutil -c icns` into `RafficaPdf.icns`, which `Info.plist` names as `CFBundleIconFile`. Both tools are part of macOS, so the step runs only there.
5. Run `dotnet run tools/package.cs -- selftest` and build the packages as described in `packaging/README.md`.

## Known limits

- Below 32 px only the silhouette and the colour read; the letters and the streaks blur. A simplified small-size variant without streaks and with larger letters is possible later.
- `Sl` can read as `S/` because the italic `l` is a plain stroke.
- The suite mark is close to the dark window background on a dark theme; a hairline border would help if it is used there.
