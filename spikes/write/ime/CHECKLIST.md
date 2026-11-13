# IME checklist (spike 4)

Run `dotnet run --project spikes/write/ime -c Release` on each system. The window has `ComposeBox` (our custom control) and Avalonia's `TextBox` (the control group), and an event log (preedit text with the preedit cursor, commits, focus). A failure in both controls is Avalonia's, a failure only in `ComposeBox` is ours.

For each row and each of the two controls, do these six checks in this order:

1. **Compose:** type a word in the input method (Japanese: `nihongo`, Chinese Pinyin: `zhongwen`); the composition shows underlined in the control.
2. **Candidates:** move the candidate selection (Space or arrow keys).
3. **Commit:** commit with Enter; the text stays in the control and the log shows a commit.
4. **Cancel:** compose again and cancel with Esc; the composition disappears, nothing is committed.
5. **Position:** while composing, the candidate window sits at the caret (note it if it appears at the window corner or on another screen).
6. **Backspace:** inside the composition Backspace removes one composed character and the composition stays.

Result per cell: `works`, `flaw: <what>`, or `fails`. Paste the event log of any failing cell into the notes. Also note: the system, the desktop session (Wayland or X11), the input method and its version, and the date.

## Windows (Avalonia uses IMM32 here, not TSF)

| Input method | Control | 1 | 2 | 3 | 4 | 5 | 6 | Notes |
|---|---|---|---|---|---|---|---|---|
| Microsoft Japanese IME | ComposeBox | works | works | works | works | works | works | log: `にほんご` → `日本語` (candidate), commit `日本語`, Esc left nothing, Backspace `にほ` → `に`; position from the owner's report |
| Microsoft Japanese IME | TextBox | | | | | | | |
| Microsoft Pinyin | ComposeBox | works | works | works | works | works | works | log: preedit `y'hong'wen` (the owner typed `zhongwen` on a German keyboard and got `y`, probably the physical key position, not the control), Enter committed the letters, Esc left nothing, Backspace removed one letter at a time; the log shows no candidate pick, the candidates and the position are from the owner's report |
| Microsoft Pinyin | TextBox | | | | | | | |

## Linux, Wayland session (HP ProBook, CachyOS)

| Input method | Control | 1 | 2 | 3 | 4 | 5 | 6 | Notes |
|---|---|---|---|---|---|---|---|---|
| Fcitx5 + Mozc | ComposeBox | | | | | | | |
| Fcitx5 + Mozc | TextBox | | | | | | | |
| Fcitx5 + Pinyin | ComposeBox | | | | | | | |
| Fcitx5 + Pinyin | TextBox | | | | | | | |
| IBus + Mozc (or Anthy) | ComposeBox | | | | | | | |
| IBus + Mozc (or Anthy) | TextBox | | | | | | | |
| IBus + Pinyin | ComposeBox | | | | | | | |
| IBus + Pinyin | TextBox | | | | | | | |

## Linux, X11 session (same machine; set `GTK_IM_MODULE`, `QT_IM_MODULE`, `XMODIFIERS` for the framework in use)

The same eight rows as under Wayland.

## macOS (Intel Mac)

| Input source | Control | 1 | 2 | 3 | 4 | 5 | 6 | Notes |
|---|---|---|---|---|---|---|---|---|
| Japanese - Romaji | ComposeBox | | | | | | | |
| Japanese - Romaji | TextBox | | | | | | | |

_Filled in so far (2026-11-13): the two `ComposeBox` rows on Windows 11 (build 26200, German keyboard), from the owner's event log and report. The `TextBox` rows, Linux and macOS are not filled in yet._
