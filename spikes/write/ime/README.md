# Spike 4: IME in a custom text control

Question: does composition (preedit) work in a custom text control, which is what Write's editor will be (not Avalonia's `TextBox`)?

- `ComposeBox` (`ComposeBox.cs`): a `Control` that draws its own text and the preedit (underlined, with Avalonia's `TextLayout`), commits from `TextInput`, and answers `TextInputMethodClientRequested` with a `TextInputMethodClient` subclass: `SetPreeditText(text, cursor)`, `CursorRectangle` (the caret rectangle in the control's coordinates, following the preedit cursor), `TextViewVisual`, `SurroundingText`, `Selection`, `SupportsPreedit` and `SupportsSurroundingText`. Every call is written to the event log.
- `MainWindow.cs`: the window with `ComposeBox`, a `TextBox` as the control group, the log and a "Copy log" button.
- `CHECKLIST.md`: the tables the owner fills in on each system.
- Package: Avalonia 12.1.5 (MIT).

```
dotnet run --project spikes/write/ime -c Release
```

## What differs from the plan's text (member names checked against Avalonia 12.1.5)

- `TextInputMethodClient` has the members the plan lists; in 12.1.5 `SetPreeditText` also exists with a second parameter (the preedit cursor), which `ComposeBox` implements, and the event `ResetRequested`.
- `FormattedText` has no hit-testing in 12; the caret rectangle comes from `TextLayout.HitTestTextPosition`.
- `OnGotFocus` and `OnLostFocus` take `FocusChangedEventArgs`.

## Results (2026-11-13)

Works in `ComposeBox` and in Avalonia's `TextBox` on Windows (Microsoft Japanese IME and Pinyin), on Linux Wayland (Fcitx5 with Mozc and Pinyin) and on macOS 15.8.2 Intel (Japanese - Romaji): compose, candidates, commit, cancel, Backspace. Details, flaws and what was not tested are in `CHECKLIST.md`; the decision is the entry "Raffica Write: IME in a custom text control (M4)" in `Docs/DECISIONS.md`.
