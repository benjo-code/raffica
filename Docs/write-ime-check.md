# Raffica Write: input method check

The owner's checklist for the input method client of Raffica Write (milestone 4.3a, task 18). CI cannot run a real input method; it drives the client with a test client that calls `SetPreeditText`, delivers the commit as text input and reads the cursor rectangle. What only a real system answers is listed here. **Nothing in this file has been checked yet**: every result table is empty on purpose, and the roadmap ticks the IME criterion only after the tables are filled in and the log lines are recorded.

## What the client does (so that you know what to expect)

- The platform asks the page view for its client when the view takes the focus. Nothing of it exists before that.
- While a composition runs, the composed text (the preedit) is drawn **inside the paragraph, underlined**, at the caret; the line reflows around it. It is never in the document: the document stays unchanged and clean, the title shows no dirty mark, Undo has nothing new.
- Write draws its own caret inside the preedit, at the cursor the input method reports (at the end of the preedit when the system reports none).
- The candidate window must stand at that caret, also after scrolling and after zooming.
- Keys belong to the input method while a composition runs. Esc cancels the composition and leaves no residue.
- The result of a composition arrives as text and is typed like any text: one Undo step (letters join the typing step, a comma or a full stop starts a step of its own).
- Clicking into the page, switching between the page view and the writing view, and leaving the window end the composition without writing it.
- A German keyboard layout is not an input method: `@` (AltGr+Q), `z` and `y` must arrive as text, never as a command.

## The log

Set the environment variable `RAFFICA_WRITE_IME_LOG` to a file path before you start Write, for example `RAFFICA_WRITE_IME_LOG=C:\temp\write-ime.log` (Windows), `export RAFFICA_WRITE_IME_LOG=/tmp/write-ime.log` (Linux, macOS). Without the variable nothing is written and nothing happens.

The file gets one line per event, in order, UTF-8, appended (delete it between runs):

| Line | Meaning |
| --- | --- |
| `preedit len=<n>` | The input method set or changed the preedit to a text of `n` UTF-16 code units. |
| `commit len=<n>` | Text of `n` code units arrived as the result of a composition and was typed. |
| `cancel` | A composition ended without text (Esc, a click, focus lost, or the input method cleared it with no result). |

The log holds **lengths only**, never the composed text. Record the lines for each check below; a `commit` line is the proof that the commit reached Write (the earlier spike's log had none on macOS). A cancel after a commit that you did not make, or a `commit` with the wrong length, is a finding.

## Before you start

1. Build or install the Write app (a Native AOT build; look at the AOT build, not a JIT one).
2. Open `testdata/write-corpus/thesis/thesis.docx` (or another document of several pages). Click into a paragraph in the middle of a page of text, so that the candidate window has text around it.
3. Note the system, its version, the input method and its version in the result table.

## Windows

### Japanese (Microsoft IME)

1. Switch to the Japanese input method (hiragana mode). Type `nihongo` and look: the text `にほんご` is underlined inside the paragraph, the line reflows, Write's caret is at the end of it.
2. Press Space: the candidate window opens **at the caret** (below the line, next to the preedit). Move the selection with the arrow keys: the preedit changes to the candidate.
3. Press Enter to commit. The text is in the paragraph without underline, the caret is after it, the document shows a dirty mark, one Undo step takes it back.
4. Type again, press Esc: the composition is gone, nothing was written, the document is as before (no dirty mark if it was clean).
5. Type `nihongo`, scroll the page with the mouse wheel until the caret is at another place of the window, press Space: the candidate window follows the caret. Zoom in with Ctrl and the wheel: the same.
6. Type `nihongo`, click somewhere else in the text: the composition ends, the caret is where you clicked, nothing was written (or the system's commit is at the old place, never lost silently).
7. Compose across a line end: type a long phrase so that the preedit wraps to the next line: the caret and the candidate window follow to the second line.
8. Select a word, then start a composition: the word stays selected until the commit, which replaces it.

### Chinese (Microsoft Pinyin)

1. Switch to the Pinyin input method. Type `zhongwen`: the pinyin or the converted text is underlined inside the paragraph.
2. Press Space or a number key to pick a candidate: the commit is typed once.
3. Repeat steps 4 to 8 of the Japanese check with Pinyin.
4. Type a mixed sequence (a Latin word, a Chinese phrase, punctuation): each commit is one `commit` line.

## Linux

### Wayland with Fcitx5

1. Start a Wayland session with Fcitx5 running (`fcitx5-remote` answers) and Mozc or a Pinyin engine installed. Start Write with the log variable set.
2. Run the Japanese or Chinese check of the Windows section (all eight steps).
3. Check that the candidate window of Fcitx5 stands at the caret of Write and not at the corner of the window or the screen; scroll and zoom as in step 5.
4. Record whether the preedit shown in Write is the one Fcitx5 shows (some versions show their own preedit as well: note it).

### Wayland with IBus

1. Start a Wayland session with IBus (`ibus-daemon` running) and an engine (Anthy, Mozc or Pinyin).
2. Run the eight steps. Note that IBus and Fcitx5 report the preedit cursor differently: Write draws its own caret, so the caret inside the preedit must stand where the engine's cursor is, or at the end if the engine reports none.

### X11 session

1. Start an X11 session (not Wayland) with Fcitx5 or IBus (the XIM or the GTK module path, whichever the system uses).
2. Run the eight steps. This path was **not tested in the spike**: record every difference from Wayland, above all where the candidate window appears.

## macOS

### Japanese (Kotoeri, Hiragana)

1. Switch to Japanese - Hiragana. Start Write from the terminal with the log variable set, so that the log can be read afterwards.
2. Type `nihongo`: the marked text is underlined inside the paragraph. Press Space for candidates: the candidate window stands at the caret. Press Enter (or Return): the text is typed.
3. **The commit must appear in the log** (`commit len=<n>`). The spike's log showed none on macOS: if the line is missing, the commit did not arrive as text input and the check fails, whatever the page shows.
4. Run steps 4 to 8 of the Windows check (Esc, scroll, zoom, click, wrap, selection). Use Cmd as the zoom key where the app maps it.

### Chinese (Pinyin - Simplified)

1. Switch to Pinyin - Simplified. Type `zhongwen`, pick a candidate with Space or a number: one `commit` line.
2. Run steps 4 to 8 of the Windows check.

## A German layout

1. Switch the layout to German (Germany) with no input method. Click into a paragraph.
2. Type `z` and `y`: they appear as typed, not swapped.
3. Type `@` (AltGr+Q on Windows and Linux, Option+L on macOS), `€`, `\`, `{`, `[`: each appears as a character. AltGr arrives as Ctrl+Alt and is text, never a command (Alt+Q is the command search, Ctrl+Alt+Q is not).
4. Type `ä`, `ö`, `ü`, `ß` and a dead key sequence (`^` then `e` gives `ê`): each arrives once.
5. Check that no command ran (no search opened, nothing selected, no dialog).

## Candidate window in a page of text

This is the check the spike could not make in a small field. For each system and input method above:

1. Place the caret in the middle of a long paragraph in the middle of a page, not at the top of the window.
2. Start a composition and open the candidate window: it stands next to the preedit at the caret.
3. Scroll so that the caret is near the bottom of the window, then near the top: the window follows. Zoom to 50 % and to 300 %: the window follows.
4. Start the composition on the last line of a page, on a line in a table cell, and in a paragraph with a hanging indent: the window stands at the caret.

## Result tables

Fill in one row per system and input method. Result is `pass` or `fail`; put the log lines (lengths) and anything unexpected into the notes. The date is the day of the check.

### Composition (preedit, candidates, commit, cancel)

| System | IME | Date | Result (pass/fail) | Notes (log lines, differences) |
| --- | --- | --- | --- | --- |
| Windows (version) | Japanese (Microsoft IME) | | | |
| Windows (version) | Chinese (Microsoft Pinyin) | | | |
| Linux Wayland | Fcitx5 (Mozc or Pinyin) | | | |
| Linux Wayland | IBus (Anthy, Mozc or Pinyin) | | | |
| Linux X11 | Fcitx5 or IBus | | | |
| macOS (version) | Japanese (Hiragana) | | | |
| macOS (version) | Chinese (Pinyin - Simplified) | | | |

### Candidate window at the caret (scroll, zoom, wrap, table cell)

| System | IME | Date | Result (pass/fail) | Notes |
| --- | --- | --- | --- | --- |
| Windows (version) | Japanese (Microsoft IME) | | | |
| Windows (version) | Chinese (Microsoft Pinyin) | | | |
| Linux Wayland | Fcitx5 | | | |
| Linux Wayland | IBus | | | |
| Linux X11 | Fcitx5 or IBus | | | |
| macOS (version) | Japanese (Hiragana) | | | |
| macOS (version) | Chinese (Pinyin - Simplified) | | | |

### The commit in Write's own log

| System | IME | Date | `commit len=<n>` present (yes/no) | Lines recorded |
| --- | --- | --- | --- | --- |
| Windows (version) | Japanese | | | |
| Windows (version) | Chinese | | | |
| Linux Wayland | Fcitx5 | | | |
| Linux Wayland | IBus | | | |
| Linux X11 | Fcitx5 or IBus | | | |
| macOS (version) | Japanese | | | |
| macOS (version) | Chinese | | | |

### German layout (`@`, `z`, `y`, AltGr is text)

| System | Layout | Date | Result (pass/fail) | Notes |
| --- | --- | --- | --- | --- |
| Windows (version) | German (Germany) | | | |
| Linux | German (de) | | | |
| macOS (version) | German | | | |
