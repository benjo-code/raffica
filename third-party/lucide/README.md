# Lucide – third-party license

Raffica draws its icons from **Lucide**. The path data of 89 icons lies in `src/Raffica.Shell/Styles/Icons.axaml` (resource keys `Raffica.Icon.<Name>`); `StrokeIcon` draws them at 16 or 20 px with a 1.5 px stroke.

## Source and license

- Project: https://github.com/lucide-icons/lucide, tag `1.63.0` (commit `2ca268e74888d3506b3319fb8f94aaaaf70ceca9`), SVG files from `https://raw.githubusercontent.com/lucide-icons/lucide/1.63.0/icons/<name>.svg`
- License: ISC, with MIT-licensed parts derived from Feather (Cole Bemis). The full text of both, as in the Lucide repository at that tag, is in [LICENSE](LICENSE). The ISC notice (Copyright (c) 2026 Lucide Icons and Contributors) and the MIT notice must stay with the icons.
- Tag `1.63.0` is the last one that still has the `bold` icon; it is gone from `1.64.0` on. A later update must replace it (for example with `letter-b`) or keep the old path.
- The shapes are unchanged. Conversion into Avalonia path data: `path` is taken as is (a leading relative `m` becomes the absolute `M`), `line` becomes `M x1 y1 L x2 y2`, `circle` becomes two arcs.

| Raffica key | Lucide file |
|---|---|
| Check, ChevronDown, ChevronRight, X, Search, Plus, Minus, Info | `check`, `chevron-down`, `chevron-right`, `x`, `search`, `plus`, `minus`, `info` |
| TriangleAlert, OctagonAlert | `triangle-alert`, `octagon-alert` |
| Bold, Italic, Underline, Undo, Redo | `bold`, `italic`, `underline`, `undo`, `redo` |
| AlignLeft, AlignCenter, AlignRight | `text-align-start`, `text-align-center`, `text-align-end` |
| every other key | the Lucide file of the same name in kebab case (`FolderOpen` is `folder-open`, `CircleHelp` is `circle-help`); `rect` became a path (arcs for `rx`), compact arc flags were written out |
