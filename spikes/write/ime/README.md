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

## Results

Not yet: see `CHECKLIST.md`. The spike builds and opens on Windows; no input method has been tried (needs a person at the keyboard).
