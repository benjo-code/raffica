# Geist and Geist Mono – third-party license

Raffica embeds the fonts **Geist** (weights 400, 500, 600) and **Geist Mono** (400, 500) as a subset. Only Geist Regular is part of this repository so far: it lies in `src/Raffica.Core/Assets/Fonts/` and is embedded in `Raffica.Core`. The other weights belonged to the Pagewing UI, which is not imported; they come back with the new UI.

## Source and license

- Project: https://github.com/vercel/geist-font, tag `v1.8.1` (commit `e1f671c727f6de7e97c1e1d26dbb44f349c19459`)
- License: SIL Open Font License 1.1, text in [OFL.txt](OFL.txt) (Copyright 2024 The Geist Project Authors). The font has no Reserved Font Name, so a subset may keep the name.
- Source files (static TTF): `fonts/Geist/ttf/Geist-{Regular,Medium,SemiBold}.ttf` and `fonts/GeistMono/ttf/GeistMono-{Regular,Medium}.ttf`
- The glyphs are unchanged; the subset only removes characters and layout features that Raffica does not need.

## Subset

Characters: Basic Latin (U+0020–007E), Latin-1 (U+00A0–00FF), Latin Extended-A (U+0100–017F), the typographic punctuation – — ‘ ’ ‚ “ ” „ … € (U+2013, 2014, 2018, 2019, 201A, 201C, 201D, 201E, 2026, 20AC) and ẞ (U+1E9E). Two characters were added to the list of the original design spec (section 5.2): the apostrophe ’ (U+2019), because it is common in German and English, and the closing quotation mark ” (U+201D), which the English UI texts use (for example “File name”).

Layout features: `kern,liga,tnum,ccmp,locl,mark,mkmk` (those that exist; Geist Mono has no `kern`, `tnum`, `ccmp`).

Command (fonttools 4.67.0 and brotli 1.2.0 in a Python virtual environment; `<in>` is the source file, `<out>` the target file):

```
pyftsubset <in>.ttf \
  --unicodes=U+0020-007E,U+00A0-00FF,U+0100-017F,U+2013,U+2014,U+2018,U+2019,U+201A,U+201C,U+201D,U+201E,U+2026,U+20AC,U+1E9E \
  --layout-features=kern,liga,tnum,ccmp,locl,mark,mkmk \
  --name-IDs='*' --notdef-outline --glyph-names \
  --output-file=<out>.ttf
```

`--name-IDs='*'` keeps all name entries, including the copyright and license notice of the font. The `meta` table (only metadata about scripts) is dropped; `pyftsubset` reports this as a warning.

## Sizes

| File | Original | Subset |
|---|---|---|
| Geist-Regular.ttf | 126048 | 52336 |
| Geist-Medium.ttf | 127660 | 53352 |
| Geist-SemiBold.ttf | 127872 | 53356 |
| GeistMono-Regular.ttf | 149284 | 53712 |
| GeistMono-Medium.ttf | 150096 | 54180 |
