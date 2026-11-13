# IME checklist (spike 4)

Run `dotnet run --project spikes/write/ime -c Release` on each system. The window has `ComposeBox` (our custom control) and Avalonia's `TextBox` (the control group), and an event log (preedit text with the preedit cursor, commits, focus). A failure in both controls is Avalonia's, a failure only in `ComposeBox` is ours.

For each row and each of the two controls, do these six checks in this order:

1. **Compose:** type a word in the input method (Japanese: `nihongo`, Chinese Pinyin: `zhongwen`); the composition shows underlined in the control.
2. **Candidates:** move the candidate selection (Space or arrow keys).
3. **Commit:** commit with Enter (Japanese) or Space or a number key (Chinese Pinyin: there Enter commits the typed letters, not a character); the text stays in the control and the log shows a commit of the converted text (Japanese `日本語`, Pinyin `你好` for `nihao`; avoid `z` and `y` on a German keyboard, Microsoft Pinyin reads the key positions of a US keyboard).
4. **Cancel:** compose again and cancel with Esc; the composition disappears, nothing is committed.
5. **Position:** while composing, the candidate window sits at the caret (note it if it appears at the window corner or on another screen).
6. **Backspace:** inside the composition Backspace removes one composed character and the composition stays.

Result per cell: `works`, `flaw: <what>`, or `fails`. Paste the event log of any failing cell into the notes. Also note: the system, the desktop session (Wayland or X11), the input method and its version, and the date.

## Windows (Avalonia uses IMM32 here, not TSF)

| Input method | Control | 1 | 2 | 3 | 4 | 5 | 6 | Notes |
|---|---|---|---|---|---|---|---|---|
| Microsoft Japanese IME | ComposeBox | works | works | works | works | below the field | works | log: `にほんご` → `日本語` (candidate), commit `日本語`, Esc left nothing, Backspace `にほ` → `に` |
| Microsoft Japanese IME | TextBox | works | works | works | works | below the field | works | owner's report (no TextBox log kept) |
| Microsoft Pinyin | ComposeBox | works | works | works | works | below the field | works | the owner typed `zhongwen` on a German keyboard and the log shows `y'hong'wen`: Microsoft Pinyin reads the key positions of a US keyboard, not the control; Enter commits the letters; `nihao` with Space or 1 committed characters (owner); Esc left nothing, Backspace removed letter by letter |
| Microsoft Pinyin | TextBox | works | works | works | works | below the field | works | owner's report; `nihao` with Space or 1 worked too |

## Linux, Wayland session (HP ProBook, CachyOS; the log's first line reads `desktop wayland`)

| Input method | Control | 1 | 2 | 3 | 4 | 5 | 6 | Notes |
|---|---|---|---|---|---|---|---|---|
| Fcitx5 + Mozc | ComposeBox | works | works | works | works | below the field | works | the hiragana stood underlined in the field (owner); the log has the commit `日本語` but, unlike the other runs, no preedit line |
| Fcitx5 + Mozc | TextBox | works | works | works | works | below the field | works | owner's report |
| Fcitx5 + Pinyin | ComposeBox | works | works | works | works | below the field | works | log: preedit `zhong wen` with the preedit cursor always 0 (never at the end); Esc and Backspace as expected; Enter commits the letters, `nihao` with Space or 1 committed characters (owner) |
| Fcitx5 + Pinyin | TextBox | works | works | works | works | below the field | works | owner's report |
| IBus + Mozc (or Anthy) | ComposeBox | | | | | | | |
| IBus + Mozc (or Anthy) | TextBox | | | | | | | |
| IBus + Pinyin | ComposeBox | | | | | | | |
| IBus + Pinyin | TextBox | | | | | | | |

## Linux, X11 session (same machine; set `GTK_IM_MODULE`, `QT_IM_MODULE`, `XMODIFIERS` for the framework in use)

The same eight rows as under Wayland.

## macOS (Intel Mac)

| Input source | Control | 1 | 2 | 3 | 4 | 5 | 6 | Notes |
|---|---|---|---|---|---|---|---|---|
| Japanese - Romaji | ComposeBox | works | works | works | works | below the field | works | log (macOS 15.8.2, Intel): no `commit` line at all, so step 3 stands on the owner's report; the preedit cursor is always none; Esc once removes the candidates (`日本語` back to `にほんご`), Esc twice removes the text, as macOS does it; Backspace goes back through `にほん`, `にほ`, `に` |
| Japanese - Romaji | TextBox | works | works | works | works | below the field | works | owner's report |

_Filled in (2026-11-13) by the owner: Windows 11 build 26200 (German keyboard), Linux Wayland with Fcitx5 (Mozc and Pinyin; the log's first line reads `desktop wayland`), macOS 15.8.2 on an Intel Mac. Column 5 reads "below the field": on every system the candidate window stood under the text field, not at the caret; the owner likes that, but the field is a small box, so the spike cannot tell whether the window follows the caret in a large text. Not tested: IBus, the X11 session, Apple silicon, Chinese on macOS._
