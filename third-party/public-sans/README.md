# Public Sans – third-party license

Raffica embeds the font **Public Sans** (weights 400, 500, 600) as the font of the interface (chrome) of all apps. It lies in `src/Raffica.Shell/Assets/Fonts/` and is embedded in `Raffica.Shell`.

## Source and license

- Project: https://github.com/uswds/public-sans, taken from https://github.com/google/fonts at commit `2fe1e8c6b137fb13041a7cfca6fdcd8a910ea4dc` (`ofl/publicsans/PublicSans[wght].ttf`)
- License: SIL Open Font License 1.1, text in [OFL.txt](OFL.txt) (Copyright 2015 The Public Sans Project Authors). The font has no Reserved Font Name.
- The shell embeds three static instances (wght 400, 500, 600) cut from the variable font with the fontTools instancer, because Avalonia on Skia does not apply the weight axis. The glyphs are unchanged. Command, tool version and file hashes are in `src/Raffica.Shell/Assets/Fonts/SOURCES.md`.
