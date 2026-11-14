# hyph-utf8 hyphenation patterns – third-party license

Raffica Write embeds three TeX hyphenation pattern sets of the `hyph-utf8` project for automatic hyphenation (Liang's algorithm, `src/Raffica.Write.Layout/Text/Hyphenator.cs`). The files lie in `src/Raffica.Write.Layout/Text/Patterns/`, are the upstream `.pat.txt` files unchanged, and are embedded in `Raffica.Write.Layout`. Documents in any other language get no hyphenation.

## Source

- Project: https://github.com/hyphenation/tex-hyphen, folder `hyph-utf8/tex/generic/hyph-utf8/patterns/txt/`, repository commit `5684c0f51c0b81133db2efbe60a408b4155a3ff5` (master, fetched 2026-11-14).
- The `.pat.txt` files carry no header. The copyright and licence text of each set is the header of the matching `patterns/tex/hyph-<tag>.tex` file of the same commit; the `LICENSE-<tag>.txt` files here are those headers (the `%` comment marker removed, nothing else changed; for de-1996 the header up to its `source:` line).

| Set | File | Licence | Licence text | SHA-256 of the shipped `.pat.txt` |
|---|---|---|---|---|
| American English | `hyph-en-us.pat.txt` | FSFAP (GNU all-permissive: copying and distribution of the file, with or without modification, in any medium without royalty provided the copyright notice and this notice are preserved), (C) 1990, 2004, 2005 Gerard D.C. Kuiken | [LICENSE-en-us.txt](LICENSE-en-us.txt) | 0f57318b878b132547ae92db39a6e1d1cf2a05d9008874955d6ecb910007a463 |
| British English | `hyph-en-gb.pat.txt` | MIT, (C) 1992, 1996, 2005, 2016 Dominik Wujastyk, Graham Toal | [LICENSE-en-gb.txt](LICENSE-en-gb.txt) | 651b89b4a0d4051995db8586e81ab109a4c7395d7706c06483893ebab5af013b |
| German, reformed spelling | `hyph-de-1996.pat.txt` | MIT, (C) 2013-2024 Stephan Hennig, Werner Lemberg, Günter Milde, Sander van Geloven, Georg Pfeiffer, Gisbert W. Selke, Tobias Wendorf, Keno Wehr | [LICENSE-de-1996.txt](LICENSE-de-1996.txt) | 0570839683e4843a6210b188e2a586fe06f2b6b73952ec8106a634209d735adb |

## Compatibility with GPL-3.0-or-later

All three licences are permissive and compatible. The en-us licence has no rename clause (that clause belongs to Knuth's `hyphen.tex`, which is not used here). The en-gb header notes that the Oxford University Press word list the patterns were derived from is not redistributed; the patterns themselves "may be freely distributed" and are MIT licensed. The files are used unmodified. The hyphenation exceptions lists (`.hyp.txt`) are not shipped.
