# Milestone 3.2 – Feels like a Mac app – Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Raffica PDF behaves like a Mac app on `osx-x64` and `osx-arm64`: a native menu bar bound to the command catalog, ⌘ shortcuts without key tips, an app that outlives its last window, pinch zoom, a title bar that fits full screen, and printing over CUPS.

**Architecture:** Platform-neutral logic that can be tested on Windows (a menu model, a shortcut table per system, a `WindowHost` that owns "which window exists and who gets the files") plus thin macOS wiring in `App`. The menu is one Avalonia `NativeMenu` on the `Application`; every item delegates to the `CommandDefinition` of the current window, so enabled and checked state come from the same place as the ribbon. The single-instance listener, the Finder bridge and the Dock reopen move from `PdfWindow` to `WindowHost` on macOS; the update flow and dialog queue stay per window and come back with the new one. Windows and Linux keep their code path.

**Tech Stack:** .NET 10 Native AOT, Avalonia 12.1.5 (`NativeMenu`, `NativeMenuItem`, `IActivatableLifetime` with `ActivationKind.Reopen`, `WindowClosingEventArgs.CloseReason`, `InputElement.PointerTouchPadGestureMagnify`), `LibraryImport` against `libcups.2.dylib`, xUnit v3 with Avalonia headless.

**Spec:** `Docs/superpowers/specs/2026-11-09-raffica-macos-design.md` (sections 5.2 to 5.6 and the 3.2 row of section 8). Roadmap: `Docs/roadmap.json`, milestone `3.2`.

## Global Constraints

- Everything in English: code, comments, docs, commits. UI strings exist in `Strings.en.resx` **and** `Strings.de.resx` (German in the "Sie" form; `TranslationTests`, `GermanRegisterTests`, `WordingStyleTests` apply).
- Platform code lives in `src/Raffica.Pdf/Platform/MacOS` (neutral types that must compile and be tested everywhere carry no `[SupportedOSPlatform]`) or in `src/Raffica.Pdf/Hosting`, never in UI code. macOS interop is `LibraryImport`, Native AOT safe, no reflection, **no Objective-C runtime calls** in this milestone.
- Windows and Linux behaviour does not change: key tips, the Ctrl shortcuts, the shutdown on the last window and the print path stay as they are. Every task keeps the Windows tests green locally.
- A menu item that runs an app command shows that command's existing catalog label; only items that macOS defines use Apple's own terms (spec section 4).
- Pool-thread rule (`CLAUDE.md`): `WindowHost` captures the dispatcher on the UI thread at construction; no handler of the pipe, the bridge or a timer reads `Dispatcher.UIThread`.
- Every `AnnotationSession.BeginPending()` handle stays disposed on all paths; the close questions never answer Cancel behind a card (`CLAUDE.md`, window and hand-over).
- No new ribbon command, so no new icon key. The menu bar adds no PDF feature.
- Commit only with the noreply address, Conventional Commits, no attribution lines, stage files by name, run `git show --stat HEAD` after each commit. Branch `feature/m32-feels-like-a-mac-app` from `dev`; PR to `dev` with the label `ci:macos`; Claude merges when all checks are green.
- "Done" = `dotnet build Raffica.slnx -c Release`, `RAFFICA_REQUIRE_OCR=1 dotnet test Raffica.slnx -c Release --no-build --max-parallel-test-modules 1`, `dotnet format Raffica.slnx --verify-no-changes`, `dotnet run tools/roadmap.cs -- check` green, plus `build-macos` green on the PR. Everything that needs the owner's Mac stays open with its checklist (Task 9), never ticked from CI.

## Review Focus

Inputs the spec implies but no task's happy path would catch. Each line has its test in the named task.

1. **⌘Q, a close question, Cancel:** the quit must not stay armed; a later ordinary window close must not end the app. Task 4.
2. **A Finder file arriving while the last window is closing (confirmed, not yet closed):** the file opens in the next window, it is not lost. Task 4.
3. **⌘Z, ⌘C, ⌘A, ⌘X, ⌘V with a text box focused** (comment, text-box editor, search field): the menu acts on the text box and never on the document or the annotation history. Task 6.
4. **The second window of a process:** it must read the settings the first one wrote at close (placement, compact bar), not the ones read at process start. Task 4.
5. **⇧⌘Q is macOS "Log Out"** and today is Recognize text: the shortcut moves on macOS only. Task 1. (A bare ⌥ tap typing `@` and `€` on the German layout is Task 3.)

## File Structure

| File | Responsibility |
|---|---|
| `src/Raffica.Pdf/Commands/ReservedShortcuts.cs` (new) | the macOS reserved list and the audit over the catalog |
| `src/Raffica.Pdf/Commands/PdfCommandCatalog.cs` | `Add(..., macShortcut:)`, `CatalogCommand.ShortcutFor(bool macOs)`, `Find(..., macOs)` |
| `src/Raffica.Pdf/Commands/NavigationHelp.cs` | `Entries(bool macOs)`: no key-tip line on macOS, ⌘ where the handler takes it |
| `src/Raffica.Pdf/Organizing/OrganizeList.cs` | the command modifier of the platform for D and A |
| `src/Raffica.Pdf/Commands/KeyTips.cs` (new), `PdfWindow.axaml.cs` | one rule: key tips exist unless macOS |
| `src/Raffica.Pdf/Hosting/WindowHost.cs` (new) | the one window, files and reopen routing, quit |
| `src/Raffica.Pdf/Hosting/AvaloniaFileActivationSource.cs` | also raises `Reopened` (new `IReopenSource`) |
| `src/Raffica.Pdf/App.axaml.cs`, `Program.cs`, `PdfWindow.axaml.cs` | window factory, `ShutdownMode`, crash guard through the host, `QuitRequested` |
| `src/Raffica.Pdf/Platform/MacOS/MacMenuModel.cs`, `MacMenuBinder.cs` (new) | the menu as data, and its `NativeMenu` with state |
| `src/Raffica.Pdf/PdfWindow.Menu.cs` (new) | `IMenuWindow` of the window |
| `src/Raffica.Pdf/Viewing/PageView.cs`, `ViewCommands.cs` | pinch zoom |
| `src/Raffica.Shell/Frame/TitleBar.cs` | inset 0 in full screen |
| `src/Raffica.Pdf/Platform/Linux/CupsNative.cs`, `CupsPrintService.cs`, `Platform/PlatformPrintServices.cs` | CUPS on macOS |
| `.github/scripts/macos-paths.sh` and its test | `src/Raffica.Pdf/Hosting/*` counts as platform-near |

---

### Task 1: Shortcut table per system and the reserved-shortcut audit

**Files:**
- Create: `src/Raffica.Pdf/Commands/ReservedShortcuts.cs`
- Modify: `src/Raffica.Pdf/Commands/PdfCommandCatalog.cs`, `src/Raffica.Pdf/PdfRibbon.cs` (only `command.Shortcut` stays valid; no change expected)
- Test: `tests/Raffica.Pdf.Tests/Commands/ReservedShortcutTests.cs`

**Interfaces:**
- Produces:
  - `CatalogGroup.Add(..., PdfShortcut? macShortcut = null)` (after `describe`); `CatalogCommand.ShortcutFor(bool macOs)` returns `macOs && macShortcut is not null ? macShortcut : shortcut`; the existing `CatalogCommand.Shortcut` stays and means `ShortcutFor(OperatingSystem.IsMacOS())`, so ribbon, help card and tooltips follow without edits.
  - `PdfCommandCatalog.Find(Key key, KeyModifiers modifiers, KeyModifiers command, bool? macOs = null)`; `null` takes `OperatingSystem.IsMacOS()`.
  - `internal static class ReservedShortcuts` with `public static IReadOnlyList<ReservedShortcut> MacOs { get; }` and `public static IReadOnlyList<CatalogCommand> ClashesIn(bool macOs)`; `internal sealed record ReservedShortcut(Key Key, KeyModifiers Modifiers, string Meaning)`.
- Decisions: the list is the spec's (⌘H, ⌥⌘H, ⌘M, ⌘Q, ⌘Tab, ⌘Space, ⌘\`, ⌃⌘F, ⇧⌘3, ⇧⌘4, ⇧⌘5) plus ⇧⌘Q (Log Out). The audit of the catalog found one clash: `ocr.run` is ⌘⇧Q. On macOS it becomes ⌘⇧R (free; `Recognize`); Windows and Linux keep Ctrl+Shift+Q. Nothing else clashes (⇧⌘H highlight, ⇧⌘M, ⇧⌘P, ⇧⌘W, ⇧⌘D, ⇧⌘K and ⇧⌘U are not on the list).

- [ ] **Step 1: Write the failing tests** in `ReservedShortcutTests`: `NoCatalogShortcutClashesWithAReservedMacShortcutOnMacOs` (`ReservedShortcuts.ClashesIn(true)` is empty); `TheUnmovedCatalogHasExactlyOneClash` (build the list by matching `ReservedShortcuts.MacOs` against each command's **default** `shortcut`, assert the single id is `PdfCommandIds.OcrRun`, so the pinned audit result cannot drift silently); `OcrRunIsCommandShiftRonMacAndCtrlShiftQElsewhere` (`ShortcutFor(true).Text(true) == "Meta+Shift+R"`, `ShortcutFor(false).Text(false) == "Ctrl+Shift+Q"`); `FindUsesTheMacTableOnMac` (`Find(Key.R, Meta|Shift, Meta, macOs: true)` is `ocr.run`; `Find(Key.Q, Meta|Shift, Meta, macOs: true)` is null; `Find(Key.Q, Control|Shift, Control, macOs: false)` is `ocr.run`); `NoTwoCommandsShareAShortcutOnEitherSystem` for `macOs` true and false.
- [ ] **Step 2: Run to verify they fail:** `dotnet test tests/Raffica.Pdf.Tests -c Release --filter-class "*ReservedShortcutTests"`. Expected: compile errors (`ReservedShortcuts`, `ShortcutFor`).
- [ ] **Step 3: Implement** the catalog members and `ReservedShortcuts` as above (`ClashesIn` compares `Key` and the `Relevant` modifiers of each command's `ShortcutFor(macOs)` with `Matches`). Give `PdfCommandIds.OcrRun` the `macShortcut: Cmd(Key.R, "R", shift: true)`.
- [ ] **Step 4: Run** the class (expect pass), then `--filter-class "*ShortcutMatchTests"`, `"*CommandCatalogTests"` and `"*HelpNavigationTests"` (expect unchanged results).
- [ ] **Step 5: Commit** `feat(macos): shortcut table per system and the reserved-shortcut audit`.

### Task 2: ⌘ in the page list and the navigation help

**Files:**
- Modify: `src/Raffica.Pdf/Organizing/OrganizeList.cs` (`HandleKey`, the `Control` case), `src/Raffica.Pdf/Commands/NavigationHelp.cs`
- Test: `tests/Raffica.Pdf.Tests/Organizing/OrganizeListTests.cs`, `tests/Raffica.Pdf.Tests/Dialogs/HelpNavigationTests.cs`

**Interfaces:**
- Produces: `OrganizeList.CommandModifier` (`internal KeyModifiers`, default `KeyNames.CommandModifier`, settable for tests); `NavigationHelp.Entries(bool macOs)` and `EntriesIn(CultureInfo, bool macOs = false)`; the parameterless `Entries()` calls `Entries(OperatingSystem.IsMacOS())`.
- Finding: the list handles only `KeyModifiers.Control` for D (duplicate) and A (select all), so on macOS ⌘D and ⌘A do nothing in the page list, and the help card prints `Ctrl+D`/`Ctrl+A` there. Fix both; the key-tip line (`HelpNavFieldCodes`) is Task 3.

- [ ] **Step 1: Write the failing tests:** in `OrganizeListTests` `CommandDDuplicatesWhenTheCommandModifierIsMeta` and `CommandADoesSelectAllWhenTheCommandModifierIsMeta` (set `CommandModifier = KeyModifiers.Meta`, press `Meta+D` / `Meta+A` in the list, assert the plan changed / all pages selected; `Control+D` then does nothing), and `ControlStaysTheModifierWhereItIsTheCommandModifier` (default value on this machine keeps the existing Ctrl+D test green). In `HelpNavigationTests` `OnMacTheOrganizerLinesUseTheCommandKey`: `Entries(macOs: true)` has the `HelpNavOrganizeDuplicate` and `HelpNavOrganizeSelectAll` lines ending in `⌘D` and `⌘A` in English.
- [ ] **Step 2: Run to verify they fail:** `--filter-class "*OrganizeListTests"` and `"*HelpNavigationTests"`.
- [ ] **Step 3: Implement.** `HandleKey` compares `e.KeyModifiers == CommandModifier` for the D/A case instead of the `Control` case label (turn the `switch` into `if` for that branch; the `Alt` and `None` cases stay). `NavigationHelp` uses its local `command` ("Meta"/"Ctrl") for the two lines.
- [ ] **Step 4: Run** both classes plus `"*OrganizeWindowKeysTests"` (expect pass).
- [ ] **Step 5: Commit** `fix(macos): command key in the page list and its help lines`.

### Task 3: No key tips and no ⌥ capture on macOS

**Files:**
- Create: `src/Raffica.Pdf/Commands/KeyTips.cs`
- Modify: `src/Raffica.Pdf/PdfWindow.axaml.cs` (constructor, `_fieldCodes`, `OnWindowClosed`), `src/Raffica.Pdf/Commands/NavigationHelp.cs`
- Test: `tests/Raffica.Pdf.Tests/Commands/KeyTipPolicyTests.cs`, `tests/Raffica.Pdf.Tests/Viewing/WindowFixture.cs` (new optional `keyTips` argument)

**Interfaces:**
- Produces: `internal static class KeyTips { public static bool Offered(bool macOs) => !macOs; }`; `PdfWindow` constructor gets a last optional parameter `bool? keyTips = null` (`null` means `KeyTips.Offered(OperatingSystem.IsMacOS())`); `internal bool HasKeyTips { get; }`; `_fieldCodes` is `IDisposable?` and is attached only when `HasKeyTips`. `NavigationHelp.Entries(true)` omits the `HelpNavFieldCodes` line.
- Decision (spec 5.4): no Alt capture at all on macOS, so ⌥ types `@`, `€` and friends and ⌥-letter shortcuts stay free. The title search stays ⌘K, panes F6, and the menu bar replaces the field codes. The pane codes Q, 6 and 7 are part of the field-code mode and go with it.

- [ ] **Step 1: Write the failing tests:** `KeyTipsAreOfferedExceptOnMacOs` (`Offered(false)` true, `Offered(true)` false); `AWindowWithoutKeyTipsDoesNotEnterTheFieldCodeMode` (`WindowFixture.Create(keyTips: false)`, press Alt down and up as `FieldCodeIntegrationTests.Alt` does; assert `window.Ribbon.ShowFieldCodes` is false, `window.HasKeyTips` is false and the Alt key-down event is not marked handled: add a tunnel `KeyDown` handler on the window in the test that records `e.Handled` after the window's own handlers); `AWindowWithKeyTipsStillEntersIt` (default; Alt shows the codes: the existing `AltHVRunsZoomInAndOpensTheViewTab` must stay green); `NoKeyTipLineInTheHelpOnMac` (`NavigationHelp.Entries(true)` has no entry named `PdfStrings.HelpNavFieldCodes`, `Entries(false)` has).
- [ ] **Step 2: Run to verify they fail:** `--filter-class "*KeyTipPolicyTests"`.
- [ ] **Step 3: Implement** as in Interfaces; `OnWindowClosed` disposes `_fieldCodes?.`. Do not touch `FieldCodeInput` or the shell.
- [ ] **Step 4: Run** `"*KeyTipPolicyTests"`, `"*FieldCodeIntegrationTests"`, `"*PdfRibbonKeyTipTests"`, `"*HelpNavigationTests"` (expect pass).
- [ ] **Step 5: Commit** `feat(macos): no key tips and no Option capture on macOS`.

### Task 4: `WindowHost`: the app outlives its window

**Files:**
- Create: `src/Raffica.Pdf/Hosting/WindowHost.cs`
- Modify: `src/Raffica.Pdf/Hosting/AvaloniaFileActivationSource.cs`, `src/Raffica.Pdf/App.axaml.cs`, `src/Raffica.Pdf/PdfWindow.axaml.cs` (`OnWindowClosing`, `FileActivation`, `Listen`, `OnOpened`), `src/Raffica.Pdf/Program.cs` (nothing expected, check)
- Test: `tests/Raffica.Pdf.Tests/Hosting/WindowHostTests.cs` (fakes, runs everywhere), `tests/Raffica.Pdf.Tests/Hosting/WindowHostWindowTests.cs` (real `PdfWindow`), `tests/Raffica.Pdf.Tests/Packaging/StartupCreatesNoPackagingWorkTests.cs` (adjust)

**Interfaces:**
- Produces (`namespace Raffica.Pdf.Hosting`):
  - `internal interface IHostedWindow { bool IsClosing { get; } bool QuitRequested { get; } void Handover(IReadOnlyList<string> paths); void BringToFront(); event EventHandler? Closed; }`, implemented by `PdfWindow` (`IsClosing` is `_closeConfirmed`; `Handover` is `OnHandover` run through `Task.Run` as the bridge does today; `QuitRequested` below; `BringToFront` is the existing private method made internal).
  - `internal interface IReopenSource { event Action? Reopened; }`, implemented by `AvaloniaFileActivationSource` (raises on `ActivatedEventArgs { Kind: ActivationKind.Reopen }`; its `OnActivated` keeps ignoring other kinds). `IFileActivationSource` and the existing bridge tests do not change.
  - `internal sealed class WindowHost` with ctor `WindowHost(Func<IReadOnlyList<string>, IHostedWindow> create, Action shutdown, Dispatcher dispatcher)`, `IHostedWindow? Current { get; }`, `void Adopt(IHostedWindow window)` (the window App made at start), `void OpenFiles(IReadOnlyList<string> paths)` and `void Reopen()` (both callable from any thread: they post to the dispatcher captured in the constructor), `void Quit()`.
  - Rules (each its own test): no window → `OpenFiles` creates one with those paths; a window → `Handover`; **a window that `IsClosing` → the paths wait and open in the next window made when it closes (Review Focus 2)**; `Reopen` with no window creates an empty one, with a window brings it forward and makes no second one; when the window closes and `QuitRequested` was set, `shutdown` is called and nothing is created; a window that closes without `QuitRequested` leaves `Current` null and the host alive; after `Quit` nothing is created.
- `PdfWindow.OnWindowClosing` keeps its shape but hands the work to `internal Task RunCloseAsync(bool quit)` (the seam for tests, since `Window.Close()` cannot carry a reason headless). The handler calls it with `quit = e.CloseReason is WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown`. `QuitRequested` is set to `quit` at the start of every closing attempt and cleared when the guard answers Cancel or throws (Review Focus 1). The close questions are unchanged.
- App wiring (macOS only, not in a measurement run; Windows and Linux keep today's path): `desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown`; `App.CreateWindow(IReadOnlyList<string> files)` holds what `OnFrameworkInitializationCompleted` does today for a window (options with `_options.WithFiles(files)`, `DefaultAppServices`, `UpdateFlows` with its `EndForUpdate` closure over **that** window, the crash guard card) and **re-reads `ShellSettings` and the viewer `AppSettings` from their stores for every window after the first** (Review Focus 4); the host replaces `window.Listen` (it calls `instance.StartListening(host.OpenFiles)` once at the first window's `Opened`, ignoring a failure as the window does) and `window.FileActivation` (it attaches the bridge once at the first window's `Opened` with `host.OpenFiles`; subscribe `IReopenSource.Reopened` to `host.Reopen`); `AttachCrashGuard` posts to `host.Current` and calls `guard.CardClosed()` at once when there is no window; the lifetime's `ShutdownRequested` is not needed (⌘Q arrives as a close with the reason above). Remove `PdfWindow.FileActivation` and its `OnOpened` use; `StartupCreatesNoPackagingWorkTests.NoFileActivationBridgeOutsideMacOS` becomes `TheWindowOwnsNoFileActivationBridge` (no skip).
- The update flow, the default-app flow and the dialog queue stay per window and are made again with the new window (spec 5.3 allows "come back with the new one"). An update that is downloaded and ready when its window closes is dropped and found again by the next check; record this in the decision entry (Task 9).

- [ ] **Step 1: Write the failing tests** in `WindowHostTests` with a `FakeWindow : IHostedWindow` and a fake dispatcher: the seven rules above, plus `OpenFilesFromAPoolThreadRunsOnTheDispatcher` and `QuitRequestedIsClearedByACancelledAttempt` (this one in `WindowHostWindowTests` with a real window and a dirty tab: call `RunCloseAsync(quit: true)`, answer Cancel on the card, assert `QuitRequested` is false and the host's `shutdown` was not called; then close the window normally with "Don't save" and assert `shutdown` is still not called), and `AConfirmedQuitCallsShutdownOnceTheWindowIsClosed` (`RunCloseAsync(quit: true)` on a clean window: the window closes, then `shutdown` is called once). In `WindowHostWindowTests`: `ASecondWindowReadsTheSettingsTheFirstWrote` (factory re-reads: close window one with a changed placement, create window two, assert it applies it), `ClosingTheLastWindowKeepsTheHostAndReopenMakesAWindow`.
- [ ] **Step 2: Run to verify they fail:** `--filter-class "*WindowHostTests"` (compile error, then red).
- [ ] **Step 3: Implement `WindowHost`, `IReopenSource`, the `PdfWindow` changes and the App wiring** as specified. `WindowHost` is the only owner of "which window is current"; it subscribes `Closed` of each window it adopts or makes.
- [ ] **Step 4: Run** `"*WindowHostTests"`, `"*WindowHostWindowTests"`, `"*FileActivationBridgeTests"`, `"*HandoverWindowTests"`, `"*HandoverBehindCardTests"`, `"*StartupCreatesNoPackagingWorkTests"`, `"*CrashGuardTests"`, `"*WindowPlacementWindowTests"` (expect pass).
- [ ] **Step 5: Commit** `feat(macos): the app outlives its last window, reopen and quit`.

### Task 5: The menu as data, with its strings

**Files:**
- Create: `src/Raffica.Pdf/Platform/MacOS/MacMenuModel.cs`
- Modify: `src/Raffica.Pdf/Resources/Strings.en.resx`, `Strings.de.resx`, `src/Raffica.Pdf/PdfStrings.cs`
- Test: `tests/Raffica.Pdf.Tests/Platform/MacMenuModelTests.cs`

**Interfaces:**
- Produces (`namespace Raffica.Pdf.Platform.MacOS`):
  - `internal abstract record MenuNode`; `CommandNode(string CommandId)` (header and shortcut come from the catalog), `ActionNode(MenuAction Action, string HeaderKey, string? Gesture = null)`, `SubmenuNode(string HeaderKey, IReadOnlyList<MenuNode> Items, MenuAction? Fill = null)`, `SeparatorNode`.
  - `internal enum MenuAction { About, Cut, Paste, OpenRecent, Minimize, ZoomWindow, BringAllToFront, ToggleFullScreen }`.
  - `internal static class MacMenuModel { public static IReadOnlyList<SubmenuNode> Build(); }` with the top-level menus in this order, items in this order (spec 5.2, decisions below):
    - **Raffica PDF:** `ActionNode(About)`, `CommandNode(app.update)`, `CommandNode(file.settings)`. Services, Hide, Hide others, Show all and Quit are not in the model: Avalonia's native side adds them to the first menu (recalled from Avalonia's macOS menu code, **not verified in this session**; the owner's check in Task 9 confirms it, and if one is missing the fallback is an `ActionNode` per missing item that calls `Application.ApplicationLifetime`'s `TryShutdown`/`NSApp` equivalents).
    - **File:** `file.open`, `ActionNode(OpenRecent)` as a submenu filled from the recent list, separator, `file.close`, `file.save`, `file.saveAs`, separator, `file.print`.
    - **Edit:** `edit.undo`, `edit.redo`, separator, `ActionNode(Cut)`, `text.copy`, `ActionNode(Paste)`, `text.selectall`, separator, `find.search`.
    - **View:** `zoom.in`, `zoom.out`, `zoom.fitwidth`, `zoom.fitpage`, `zoom.actual`, separator, the four `layout.*`, `page.dark`, separator, `panes.pages`, `panes.outline`, separator, `ActionNode(ToggleFullScreen)` with gesture `Ctrl+Meta+F`.
    - **Window:** `ActionNode(Minimize, gesture Meta+M)`, `ActionNode(ZoomWindow)`, separator, `ActionNode(BringAllToFront)`.
    - **Help:** `help.shortcuts` only. The spec lists "Raffica PDF help (the F1 card)" and "Keyboard shortcuts"; they are the same card in this app (the F1 list is the only help), so one item (call, owner may veto). The system search field of the Help menu is the system's and is checked on the Mac.
  - New strings (key = English → German, the German being Apple's term as macOS shows it in German; the owner reads them on the Mac): `MenuFile` File → Ablage, `MenuEdit` Edit → Bearbeiten, `MenuView` View → Darstellung, `MenuWindow` Window → Fenster, `MenuHelp` Help → Hilfe, `MenuAbout` About Raffica PDF → Über Raffica PDF, `MenuCut` Cut → Ausschneiden, `MenuPaste` Paste → Einsetzen, `MenuOpenRecent` Open Recent → Benutzte Dokumente, `MenuNoRecent` No recent files → Keine Dokumente, `MenuMinimize` Minimize → Im Dock ablegen, `MenuZoomWindow` Zoom → Zoomen, `MenuBringAllToFront` Bring All to Front → Alle nach vorne bringen, `MenuEnterFullScreen` Enter Full Screen → Vollbildmodus aktivieren, `MenuExitFullScreen` Exit Full Screen → Vollbildmodus beenden. The app-menu title is the product name and has no key.

- [ ] **Step 1: Write the failing tests:** `EveryCommandNodeNamesACatalogCommand` (each `CommandId` is in `PdfCommandCatalog.Commands`); `NoCommandAppearsTwice`; `TheMenusAreTheSpecsInOrder` (headers of the top-level menus: app, File, Edit, View, Window, Help, and the item ids of File and Edit exactly as listed); `EveryHeaderKeyExistsInBothLanguages` (resolve each `HeaderKey` with `PdfStrings.Get` under `en` and `de`, assert non-empty and, for German, different from English except `MenuAbout`-style proper names is not needed: assert only that both resolve); `AMacShortcutOfAnItemIsOnlyDefinedByTheCatalog` (no `ActionNode` gesture duplicates a catalog `ShortcutFor(true)`).
- [ ] **Step 2: Run to verify they fail:** `--filter-class "*MacMenuModelTests"`.
- [ ] **Step 3: Implement** the model and add the 15 string pairs to both resx files and `PdfStrings` (one property each, `Table.Get("<key>")` as the file does). Keep the resx entries next to each other under a comment `<!-- macOS menu bar (M3.2) -->`.
- [ ] **Step 4: Run** `"*MacMenuModelTests"`, `"*TranslationTests"`, `"*GermanRegisterTests"`, `"*WordingStyleTests"` (expect pass).
- [ ] **Step 5: Commit** `feat(macos): the menu bar model and its English and German strings`.

### Task 6: The native menu, bound to the commands

**Files:**
- Create: `src/Raffica.Pdf/Platform/MacOS/MacMenuBinder.cs`, `src/Raffica.Pdf/PdfWindow.Menu.cs`
- Modify: `src/Raffica.Pdf/App.axaml.cs` (macOS only: `NativeMenu.SetMenu(this, binder.Menu)`), `src/Raffica.Pdf/PdfWindow.Commands.cs` (`RefreshCommandStates` raises `MenuStatesChanged`), `src/Raffica.Pdf/PdfWindow.axaml.cs` (`Activated` tells the host)
- Test: `tests/Raffica.Pdf.Tests/Platform/MacMenuBindingTests.cs`

**Interfaces:**
- Consumes: `MacMenuModel.Build()` (Task 5), `WindowHost.Current` (Task 4).
- Produces:
  - `internal interface IMenuWindow { CommandDefinition? CommandOf(string id); TextBox? FocusedTextBox { get; } IReadOnlyList<RecentFile> RecentFiles { get; } void OpenRecent(RecentFile file); void ShowAbout(); void Minimize(); void ToggleWindowZoom(); void BringAllToFront(); bool IsFullScreen { get; set; } event Action? StatesChanged; }`, implemented by `PdfWindow` in `PdfWindow.Menu.cs` (`CommandOf` reads the `CommandDefinition` built for the ribbon, `_commandsById`).
  - `internal sealed class MacMenuBinder` with ctor `MacMenuBinder(Func<IMenuWindow?> current, Func<Task> openWithoutWindow)`, `NativeMenu Menu { get; }`, `void Refresh()`. Each `CommandNode` becomes a `NativeMenuItem` whose `Header` is the catalog `Name`, `Gesture` the catalog `ShortcutFor(true)` as a `KeyGesture`, `Command` an `ICommand` whose `CanExecute` is `current()?.CommandOf(id)?.IsEnabled == true` and whose `Execute` runs `Run`; `ToggleType = CheckBox` and `IsChecked` follow `IsChecked` for toggle commands (`IsToggle`). `Refresh` re-reads all states, raises `CanExecuteChanged`, rebuilds the Open Recent submenu, and is called on `NativeMenu.NeedsUpdate`, on `StatesChanged` of the current window and when the current window changes.
  - Routing rule (Review Focus 3): for `edit.undo`, `edit.redo`, `text.copy`, `text.selectall` and the `Cut` and `Paste` actions, when `FocusedTextBox` is not null the item is enabled and acts on that text box (`Undo`, `Redo`, `Copy`, `SelectAll`, `Cut`, `Paste` of the control), mirroring the exemption in `PdfWindow.OnShortcutKeyDown`; without a focused text box `Cut` and `Paste` are disabled and the others run the catalog command.
  - No window rule: every item is disabled except `file.open`, which calls `openWithoutWindow` (the host makes a window, then the picker runs). Open Recent and About need a window and are disabled without one (call, owner may veto).
  - Full screen: `ToggleFullScreen` flips `IsFullScreen` (`WindowState.FullScreen` and back to the previous state); its header switches between `MenuEnterFullScreen` and `MenuExitFullScreen` on `Refresh`.
- Facts to check in the Avalonia 12.1.5 API while implementing (the XML docs in `~/.nuget/packages/avalonia/12.1.5/lib/net10.0` are the reference): `NativeMenuItem.Gesture` takes a `KeyGesture`; whether `NativeMenuItem` can be created and read in a headless test (it is an `AvaloniaObject`; the tests below do not need the native side); the exact signature of `TextBox.Cut/Copy/Paste/Undo/Redo`.

- [ ] **Step 1: Write the failing tests** in `MacMenuBindingTests` (headless, any OS): with a `FakeMenuWindow` whose `CommandOf` returns counting `CommandDefinition`s: `EveryCommandItemRunsItsCommand` (for each `CommandNode`, `Execute` runs the definition's `Run` exactly once); `EnabledAndCheckedStateFollowTheCommand` (flip `EnabledWhen` and `CheckedWhen` of the fakes, call `Refresh`, assert `CanExecute` and `IsChecked` of every item, for all ids); `HeadersAreTheCatalogLabelsInBothLanguages` (for `en` and `de`: each command item's `Header` equals `CatalogCommand.Name`); `WithoutAWindowOnlyOpenIsEnabled`; `OpenWithoutAWindowCallsTheHostHook`; `ATextBoxInFocusTakesUndoCopySelectAllCutPaste` (fake `FocusedTextBox` with text; `Execute` of Copy/SelectAll acts on it and the catalog command is not run); `OpenRecentListsTheRecentFilesAndOpensOne` (two `RecentFile`s become two items, `Execute` calls `OpenRecent` with that entry; no recent files gives one disabled `MenuNoRecent` item). Then with a real `WindowFixture`: `TheRealWindowAnswersEveryCommandNodeWithTheRibbonsDefinition` (for each `CommandNode`, `window.CommandOf(id)` is not null, with no tab open and again with a tab open its `IsEnabled` matches `PdfCommandRules` through the ribbon: compare to `window.Ribbon` state by running `RefreshCommandStates`).
- [ ] **Step 2: Run to verify they fail:** `--filter-class "*MacMenuBindingTests"`.
- [ ] **Step 3: Implement** `MacMenuBinder`, `PdfWindow.Menu.cs` (the `IMenuWindow` members: `ShowAbout` calls `ShowAboutAsync` under `GuardAsync`; `OpenRecent` calls `OpenRecentAsync` under `GuardAsync`; `RecentFiles` is the start view's recent list) and the `App` wiring: on macOS and outside a measurement run `NativeMenu.SetMenu(this, new MacMenuBinder(() => _host.Current as IMenuWindow, OpenWindowThenPicker).Menu)`.
- [ ] **Step 4: Run** `"*MacMenuBindingTests"`, `"*MacMenuModelTests"`, `"*CommandCatalogTests"`, `"*PdfRibbonStateTests"` (expect pass).
- [ ] **Step 5: Commit** `feat(macos): native menu bar bound to the command catalog`.

### Task 7: Pinch zoom, full-screen title bar, Retina check

**Files:**
- Modify: `src/Raffica.Pdf/Viewing/PageView.cs`, `src/Raffica.Pdf/Viewing/ViewCommands.cs`, `src/Raffica.Shell/Frame/TitleBar.cs`
- Test: `tests/Raffica.Pdf.Tests/Viewing/PinchZoomTests.cs`, `tests/Raffica.Shell.Tests/Frame/TitleBarInsetTests.cs`, `tests/Raffica.Core.Tests/Viewing/ViewportPlannerTests.cs`

**Interfaces:**
- Produces: `ViewCommands.ZoomByMagnify(double zoom, double magnification) -> double` (`ZoomSteps.Clamp(zoom * (1 + magnification))`, non-finite magnification returns `zoom`); `PageView.MagnifyAround(double magnification, Point point)` (`internal`; calls `ZoomAround(ZoomByMagnify(_zoom, magnification), point)`, so a fit mode ends and the spot under the pointer stays); an `InputElement.PointerTouchPadGestureMagnifyEvent` handler on the view that calls it with the event's `Delta.X` and the pointer position and marks the event handled; `TitleBar.EffectiveInset(double inset, WindowState state, bool macOs) -> double` (`0` for `FullScreen` on macOS, else `inset`) applied to the inset column when `WindowState` changes.
- Decision (call, owner may veto): the pinch is continuous between `ZoomSteps.Min` (10 %) and `ZoomSteps.Max` (800 %), the limits of the zoom commands; it does not snap to the step table (a pinch that jumps from 100 % to 125 % feels broken). The zoom commands keep stepping from wherever the pinch left the zoom (`ZoomSteps.Next/Previous` accept any value). Two-finger scrolling and the natural-scrolling setting come from Avalonia and are checked on the Mac.
- Retina (spec 5.5, "the builder checks this and fixes it"): the view already plans tiles with `RenderScaling()`, snaps tile rectangles to device pixels and replans on `ScalingChanged`; the 96-dpi `Vector` of `TileBitmaps` does not matter because tiles are drawn into an explicit destination rectangle. So no code change is expected; this task pins it and the owner confirms sharpness at 2× on the Mac.

- [ ] **Step 1: Write the failing tests:** `ZoomByMagnifyClampsAtTheZoomLimits` (`ZoomByMagnify(7.9, 0.5) == ZoomSteps.Max`, `ZoomByMagnify(0.11, -0.9) == ZoomSteps.Min`, `ZoomByMagnify(1, double.NaN) == 1`); `MagnifyKeepsThePointUnderThePointer` (open `pages-marked.pdf` in a `ViewFixture`, scroll to a known place, call `MagnifyAround(0.5, point)`, assert zoom 1.5 and that the layout position under `point` before and after is the same within one DIP, and that a fit mode ended); `MagnifyIsRaisedByTheTouchPadEvent` (raise `InputElement.PointerTouchPadGestureMagnifyEvent` on the view if `PointerDeltaEventArgs` can be built in the headless test, otherwise call the private handler through `MagnifyAround` and say so in the test comment). `TitleBarInsetTests`: `FullScreenOnMacRemovesTheInset` (`EffectiveInset(80, FullScreen, true) == 0`), `NormalOnMacKeepsIt`, `FullScreenElsewhereChangesNothing` (`EffectiveInset(0, FullScreen, false) == 0`, and `EffectiveInset(80, FullScreen, false) == 80`). In `ViewportPlannerTests` `AtScalingTwoTilesAskForTwiceThePixels` if no existing test already asserts a scaling-2 request size (read the file first; add only the missing case).
- [ ] **Step 2: Run to verify they fail:** `--filter-class "*PinchZoomTests"`, `dotnet test tests/Raffica.Shell.Tests -c Release --filter-class "*TitleBarInsetTests"`.
- [ ] **Step 3: Implement** as specified. Register the magnify handler in the `PageView` constructor next to the wheel handling.
- [ ] **Step 4: Run** the new classes plus `"*ZoomTests"`, `"*PageViewScrollTests"`, `"*PageViewRenderTests"`, and the shell `TitleBar` tests (expect pass).
- [ ] **Step 5: Commit** `feat(macos): pinch zoom and the full-screen title bar`.

### Task 8: Printing over CUPS on macOS

**Files:**
- Modify: `src/Raffica.Pdf/Platform/Linux/CupsNative.cs`, `src/Raffica.Pdf/Platform/Linux/CupsPrintService.cs`, `src/Raffica.Pdf/Platform/PlatformPrintServices.cs`
- Test: `tests/Raffica.Pdf.Tests/Platform/PlatformPrintServicesTests.cs`, `tests/Raffica.Pdf.Tests/Platform/CupsPrintServiceTests.cs`

**Interfaces:**
- Produces: `PlatformPrintServices.Create()` returns `CupsPrintService` on Linux **and macOS**; `internal static IPrintService Choose(bool windows, bool linux, bool macOs)` is the seam (`NoPrintService` for none). `CupsNative` resolves its library by a logical name through `NativeLibrary.SetDllImportResolver(typeof(CupsNative).Assembly, ...)` set in its static constructor: `libcups.so.2` on Linux, `libcups.2.dylib` on macOS (a leaf name, found in `/usr/lib` by `dlopen`; the Mach-O rules of `tools/check-binary.cs` already allow `/usr/lib`). The `LibraryImport` constant becomes the logical name `"cups"`; the resolver is Native AOT safe. `[SupportedOSPlatform]` of both classes lists `linux` and `macos`. The Linux print rules (always light, annotations printed, a document that forbids printing locks the command, background job with progress and Cancel) are untouched: no other change in `CupsPrintService`.
- Folder: the CUPS files stay in `Platform/Linux`'s folder to keep the history of the Linux code; moving them is cosmetic and left out (call). The decision entry says so.

- [ ] **Step 1: Write the failing tests:** `ChooseGivesCupsOnLinuxAndMacOs` (type of `Choose(false, true, false)` and `Choose(false, false, true)` is `CupsPrintService`; `Choose(true, false, false)` is `WindowsPrintService`; `Choose(false, false, false)` is `NoPrintService`; do not construct on a platform that forbids it: the test is guarded with `Assert.SkipUnless(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() || OperatingSystem.IsWindows(), ...)` only where needed, otherwise compare `Func` results by `GetType().Name`); `CupsLibraryLoadsOnMacOs` (`Assert.SkipUnless(OperatingSystem.IsMacOS(), "macOS only: libcups.2.dylib")`, then `CupsNative.IsAvailable` is true and `new CupsPrintService().GetPrintersAsync(...)` returns without throwing); `CupsLibraryNameIsPerSystem` (the resolver's name function, factored as `CupsNative.LibraryNameFor(bool linux, bool macOs)`, returns the two names and `null` for neither).
- [ ] **Step 2: Run to verify they fail:** `--filter-class "*PlatformPrintServicesTests"`.
- [ ] **Step 3: Implement** the resolver, the attribute change and `Choose`; `Create()` passes the three `OperatingSystem` checks.
- [ ] **Step 4: Run** `"*PlatformPrintServicesTests"`, `"*CupsPrintServiceTests"`, `"*CupsOptionsTests"`, `"*PrintCommandTests"` on Windows (expect pass or the existing named skips), then `dotnet build Raffica.slnx -c Release` (0 warnings: the platform-compatibility analyzer is on).
- [ ] **Step 5: Commit** `feat(macos): printing over CUPS on macOS`.

### Task 9: Green on macOS, owner checks, documentation, wrap-up

**Files:**
- Modify: `.github/scripts/macos-paths.sh` and `.github/scripts/macos-paths.test.sh` (add `src/Raffica.Pdf/Hosting/*` and `src/Raffica.Pdf/App.axaml.cs`, with a positive and a negative case in the test); `Docs/DECISIONS.md`; `CLAUDE.md` (section "macOS", one line per new type: `WindowHost`, `MacMenuModel`/`MacMenuBinder`, `ReservedShortcuts`, `KeyTips`); `packaging/README.md` (a section "Manual check on a Mac (M3.2)"); `Docs/roadmap.json` and the generated `Docs/ROADMAP.md`

- [ ] **Step 1: Run the local gate** (Windows): `dotnet build Raffica.slnx -c Release`, `RAFFICA_REQUIRE_OCR=1 dotnet test Raffica.slnx -c Release --no-build --max-parallel-test-modules 1`, `dotnet format Raffica.slnx --verify-no-changes`, `bash .github/scripts/macos-paths.test.sh`, `dotnet run tools/roadmap.cs -- check` (expect 0 failed, 0 warnings, exit 0, valid).
- [ ] **Step 2: Open the PR** to `dev` (not a draft), `gh pr create --base dev`, add the label (`gh pr edit --add-label ci:macos`). Triage every `build-macos` failure by cause: code that still treats macOS like Linux (the CUPS tests, `WindowHost` tests under the real lifetime), a test that needs a real display (skip it with `Assert.SkipWhen(OperatingSystem.IsMacOS(), "macOS: <reason>")`, a reason is mandatory, the job's skip list fails on an empty one), anything else is a bug with a test. Do not loosen a check. Record the green run id, the result line and the names of: `ReservedShortcutTests`, `WindowHostTests`, `WindowHostWindowTests`, `MacMenuBindingTests`, `KeyTipPolicyTests`, `PlatformPrintServicesTests.CupsLibraryLoadsOnMacOs`.
- [ ] **Step 3: Owner's Mac checklist** in `packaging/README.md`, each with its command or action, recorded in the owner's words in the roadmap evidence, left unticked until done (use the artifact `raffica-pdf-osx-x64` of the PR's `build-macos` run and `packaging/macos/dev-app.sh` as in M3.1): (a) the menu bar shows File, Edit, View, Window and Help, the app menu has About, Check for updates, Settings and (from Avalonia) Services, Hide, Hide others, Show all, Quit; no item is duplicated (full screen, Window items); the German wording reads right with `--lang de`; (b) File, Edit and View items act on the document, and with a comment box focused ⌘Z/⌘C/⌘A/⌘X/⌘V act on the box; (c) ⌘Q with unsaved marks asks the close questions, Cancel keeps the app, the answer quits; (d) close the last window: the app stays in the Dock, a Dock click shows a new window, a PDF opened from the Finder with no window creates one, a second Finder file while one is open becomes a tab; (e) pinch zoom around the pointer, and the zoom commands step on from there; pages sharp at 2× on a Retina display (zoom in and compare with Preview); (f) title bar: traffic lights, the 80 px inset, drag area, ⌃⌘F and the View menu item, no empty gap in full screen; (g) ⌥ types `@`, `€` and friends in a text box and nothing flashes in the ribbon; ⇧⌘R recognizes text, ⇧⌘Q is the system's Log Out dialog and not ours; (h) a page prints through the own print card to a real printer and to a PDF printer; (i) the title-bar search ⌘K and F6 still work without the Alt field codes.
- [ ] **Step 4: Write one `Docs/DECISIONS.md` entry** "Raffica on macOS: menu bar, keys and life cycle (M3.2)": the menu model and what is native versus wired; the one-item Help menu and why; the no-window rule; the update flow and dialog queue come back with each window (a ready update is dropped with its window); the continuous pinch; the shortcut audit result (⇧⌘Q moved to ⇧⌘R on macOS, the unmoved table pinned by `TheUnmovedCatalogHasExactlyOneClash`); the page list's command modifier; key tips off (spec 5.4, closes the backlog entry "Option key handling on macOS"); CUPS on macOS and the folder left in `Platform/Linux`; the German wording is Apple's as recalled, read by the owner; the Avalonia assumption about the native system items and its fallback. Add the `CLAUDE.md` pointers (one line each) and the macOS-paths addition.
- [ ] **Step 5: Roadmap.** In `Docs/roadmap.json` set 3.2 to `planned` with `"plan": "superpowers/plans/2026-11-10-milestone-03-2-feels-like-a-mac-app.md"` when this plan is committed (done at planning time); during implementation tick each task, then tick only the acceptance criteria that were checked, with evidence (the test names and run id above; the three "On the owner's Mac" criteria stay unticked until the owner reports). Remove the backlog entry "Option key handling on macOS" if it is still there when Task 3 is ticked. Status `done` with `completedOn` only when every criterion is ticked, otherwise `in-progress` and the open criteria named. Run `dotnet run tools/roadmap.cs -- generate` then `-- check` (expect valid) and commit both files.
- [ ] **Step 6: `CHANGELOG.md`:** no entry (there is no macOS release yet; Windows and Linux users see nothing); say so in the PR body.
- [ ] **Step 7: Final verification and merge:** Step 1 again on the final head, all PR checks green (`ci`, `roadmap`, CodeQL, Windows, Linux and `build-macos`), then merge to `dev` (squash) as `CLAUDE.md` allows. Name every command and result in the PR body.

## Self-Review

- **Spec coverage (3.2 tasks and criteria):** menu bar with all section-5.2 items bound to the catalog with equal state (Tasks 5 and 6; test names `MacMenuBindingTests.EveryCommandItemRunsItsCommand`, `EnabledAndCheckedStateFollowTheCommand`); German and English wording (Task 5 `EveryHeaderKeyExistsInBothLanguages`, Task 6 `HeadersAreTheCatalogLabelsInBothLanguages`); reserved-shortcut audit with a pinning test (Task 1); no key tips, no ⌥ capture, no key-tip line in the help card, Windows and Linux unchanged (Task 3); life cycle without windows, Dock reopen, ⌘Q and close questions (Task 4); pinch, Retina, title bar inset and full screen (Task 7); CUPS printing on macOS (Task 8); the four "On the owner's Mac" criteria are the checklist of Task 9 step 3 (menu and text box, ⌘Q, Dock and Finder, pinch/2×/title bar/full screen, printing).
- **Found while reading the code, not in the spec:** the page list ignored ⌘ for ⌘D and ⌘A and printed `Ctrl+D` in the macOS help (Task 2); `⇧⌘Q` Recognize clashes with Log Out (Task 1); the title bar keeps its 80 px inset in full screen (Task 7).
- **Open points called by this plan, for the owner to veto:** one Help item (the F1 card is the only help); pinch is continuous within the zoom limits; with no window only Open works in the menu; ⇧⌘R for Recognize on macOS; the update flow and the dialog queue are per window and come back with the new one; the CUPS files stay in `Platform/Linux`; the German menu wording is Apple's as recalled and unverified.
- **Unverified assumption:** Avalonia's native side adds Services, Hide, Hide others, Show all and Quit to the first menu. CI cannot check it; Task 9 step 3(a) does, with a named fallback.
- **Needs the owner:** the checklist of Task 9 step 3 on the Intel Mac; Apple silicon stays automatic-only and "not tested by hand".
