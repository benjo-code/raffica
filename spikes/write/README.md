# Raffica Write spikes (milestone 4)

Throwaway spikes that answer the open technical questions before the Write spec (design: `Docs/superpowers/specs/2026-11-12-raffica-write-spikes-design.md`, plan: `Docs/superpowers/plans/2026-11-12-milestone-04-write-spikes-spec.md`).

Spike code never becomes product code. This folder is outside `Raffica.slnx`; `spikes/Directory.Build.props` and `spikes/Directory.Packages.props` stop the repository's build settings and central package management from applying. Every spike project pins its package versions itself. `global.json` still applies.

| Folder | Spike | Result (2026-11-12) |
|---|---|---|
| `testdoc/` | the generated 311-page thesis every spike reads | deterministic, `verify` ok; Word's page count and the "1 on page 3" check are open (owner), see `testdoc/README.md` |
| `openxml-aot/` | spike 1: Open XML SDK under Native AOT | works on win-x64, linux-x64, osx-arm64, osx-x64 with 0 IL warnings; untouched parts stay byte-identical when never loaded; LibreOffice opens the files; Word and Excel are open (owner). Decision entry "Raffica Write: Open XML SDK under Native AOT (M4)" |
| `layout/` | spike 2: layout engine, C# against Rust (Parley) | both candidates pass all checks and meet every limit except one hosted-VM first-page value; the binding Linux laptop numbers are open, so the engine decision is not recorded yet; see `layout/README.md` |
| `print/` | spike 3: Write prints itself | Windows draws directly onto the printer (11 s, 1.66 MB, real text), PDF through PDFium is 42 times slower and 60 times larger; Linux and macOS hand a PDF in memory to CUPS (works on hosted runners); real printers are open. Decision entry "Raffica Write: printing per operating system (M4)" |
| `ime/` | spike 4: IME in a custom text control | the spike is built and starts; nothing is tested yet (needs a person with the input methods), see `ime/CHECKLIST.md` |
| `fonts/` | Caladea (the fonts for the test document; Carlito comes from `src/Raffica.Shell/Assets/Fonts`) | n/a |

## Licence checks (suite spec, "License guardrails")

- **Caladea** (`fonts/Caladea-*.ttf`, `fonts/OFL-Caladea.txt`): SIL Open Font License 1.1, copyright 2012 The Caladea Project Authors. Source: `https://github.com/google/fonts/tree/main/ofl/caladea` (folder commit `13c010f766052c13fa9cc8c656008cee55fb4775`, fetched 2026-11-12). OFL fonts are allowed.
- **Carlito:** already in the repository (`third-party/carlito/OFL.txt`, files in `src/Raffica.Shell/Assets/Fonts`); the spikes read the files from there (the plan named `third-party/carlito`, which holds only the licence text and README).
- **DocumentFormat.OpenXml 3.5.1:** MIT.
- **SkiaSharp 3.119.4** (with `SkiaSharp.NativeAssets.Linux.NoDependencies`): MIT.
