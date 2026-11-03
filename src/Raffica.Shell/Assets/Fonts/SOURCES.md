# Font sources

All fonts: SIL Open Font License 1.1 (texts in `third-party/public-sans`, `third-party/barlow-semi-condensed` and `third-party/carlito`), from https://github.com/google/fonts at commit `2fe1e8c6b137fb13041a7cfca6fdcd8a910ea4dc`. The files below are the pinned upstream files, except the three Public Sans files, which are static instances made from the upstream variable font.

| File | Upstream URL | SHA-256 |
|---|---|---|
| `BarlowSemiCondensed-SemiBold.ttf` | https://raw.githubusercontent.com/google/fonts/2fe1e8c6b137fb13041a7cfca6fdcd8a910ea4dc/ofl/barlowsemicondensed/BarlowSemiCondensed-SemiBold.ttf | bd299f4bc5b44d30be8d42e9bad3a5df7d66af1cd55d0ed72a8b8916360a1424 |
| `Carlito-Regular.ttf` | https://raw.githubusercontent.com/google/fonts/2fe1e8c6b137fb13041a7cfca6fdcd8a910ea4dc/ofl/carlito/Carlito-Regular.ttf | f6418f708baede9789daef5d458c0f53d2a888af9820e8062934e504fedc6595 |
| `Carlito-Bold.ttf` | https://raw.githubusercontent.com/google/fonts/2fe1e8c6b137fb13041a7cfca6fdcd8a910ea4dc/ofl/carlito/Carlito-Bold.ttf | bb5d20f79b82599ec72983597437373a80f2d2085fa91fc144fd74e876a594db |
| `Carlito-Italic.ttf` | https://raw.githubusercontent.com/google/fonts/2fe1e8c6b137fb13041a7cfca6fdcd8a910ea4dc/ofl/carlito/Carlito-Italic.ttf | 0b019225e58d702bfedcbd35c21696769f8ee115cb6343f84c2f240312450d1c |
| `Carlito-BoldItalic.ttf` | https://raw.githubusercontent.com/google/fonts/2fe1e8c6b137fb13041a7cfca6fdcd8a910ea4dc/ofl/carlito/Carlito-BoldItalic.ttf | b32928186c119599e03ca6a1ffc680fdcb7fac95772f4b95d989cf6cd3861517 |

## Public Sans: static instances

The upstream file `PublicSans[wght].ttf` (https://raw.githubusercontent.com/google/fonts/2fe1e8c6b137fb13041a7cfca6fdcd8a910ea4dc/ofl/publicsans/PublicSans%5Bwght%5D.ttf, SHA-256 `d75a7dc1a27eb9e336d5b33f55489d2ecb5621bf694d5c43b2415bce2ca830a8`) is a variable font. Avalonia 12.1.4 on Skia does not apply the weight axis: every weight rendered as the default instance (Thin), and `PublicSansWeightsTests` failed with one distinct width for 400, 500 and 600. So the shell embeds static instances, made with fontTools 4.68.0 (`pip install fonttools`):

```
fonttools varLib.instancer "PublicSans[wght].ttf" wght=<400|500|600> --update-name-table -o PublicSans-<Regular|Medium|SemiBold>.ttf
```

| File | SHA-256 |
|---|---|
| `PublicSans-Regular.ttf` (wght=400) | 55b0c1a25c87ffbe1e94b31f6a2cb817f7c79154cc410b26c6b44325c1ea5ca4 |
| `PublicSans-Medium.ttf` (wght=500) | d4238b5496ebbe2897881e1f22a03930cf1b8eff44fe4ca09c94c728f1561f06 |
| `PublicSans-SemiBold.ttf` (wght=600) | a592d9304e68514dda7ce9b972ef945373bfa16884562e758af209fec834368e |

The glyph outlines are unchanged; the instancer only pins the axis and updates the name table. The hashes of the instances can differ with another fontTools version; the pinned input is the upstream file above.

## Size

The embedded font files add 3,027,784 bytes (about 2.9 MiB) to `Raffica.Shell`: Carlito, four styles, 2,734,244; Barlow Semi Condensed 109,772; Public Sans, three weights, 183,768. Carlito is the bulk; a subset is possible later if the published size matters.
