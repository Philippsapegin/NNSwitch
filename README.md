# NN Switch

<p align="center">
  <img src="NN.ico" width="160" alt="NN Switch logo">
</p>

**NN Switch** is a small Windows utility for correcting text typed with the
wrong keyboard layout. It works on demand: choose an action, assign a hotkey,
and press it when text needs to be fixed.

NN Switch never corrects text automatically. Its lightweight keyboard hook keeps
a short-lived, in-memory history of recent physical keystrokes so the last word
can usually be replaced without selecting it or touching the system clipboard.
The history is limited to 4,096 keystrokes and is discarded after mouse input,
caret/navigation commands, a window or layout change, or 30 seconds of inactivity.
Configured shortcuts are consumed before the
active application can react; Windows global hotkey registration remains a
fallback if the hook is unavailable. Text changes only when a configured hotkey
is pressed.

## No installation. No Yandex. No bundled extras.

Download `NN Switch.exe` from the
[latest release](https://github.com/Philippsapegin/NNSwitch/releases/latest) and
run it.

- One portable, self-contained x64 executable.
- No installer and no separate .NET runtime.
- No offers to install Yandex, browsers, toolbars, or any other software.
- No advertising, telemetry, or network requests.
- Typed text is never logged, written to files, sent over the network, or kept
  after NN Switch exits.

The application runs entirely from the system tray, with no main window and no
taskbar button.

## What's new in v1.3.1

- A narrower, cleaner single-column Hotkeys window with layout-dependent settings
  grouped together.
- Clearer hotkey capture and dropdown focus behavior, with shortcuts remaining
  active while the settings window is open.
- An optional cyclic input-language shortcut, empty by default.

## What's new in v1.3

Compared with v1.2, this release adds:

- **Reliable invisible replacement** — a private in-memory typing buffer handles
  recent words without selection or clipboard access, backed by verified native
  and clipboard fallbacks for existing text and custom controls.
- **Priority hotkeys** — configured shortcuts are intercepted before conflicting
  application shortcuts, and rapid commands are queued instead of discarded.
- **One unified hotkey window** — universal actions, correction targets, direct
  input-language switching, and per-layout conversion shortcuts are configured
  together without extra tray menus or tabs.
- **Selected-text case tools** — optional shortcuts for `UPPERCASE`, `lowercase`,
  and sentence case.

## Key features

- **Switch selected text** — corrects the currently selected text.
- **Switch last written word** — corrects the word immediately before the caret.
- **Switch active text field** — corrects all text in the active field.
- **Selected-text case** — changes a selection to `UPPERCASE`, `lowercase`, or
  sentence case without changing the keyboard layout.
- **Per-layout targets** — selects a separate destination layout for every
  layout installed in Windows.
- **Direct layout hotkeys** — cycles through installed input languages or switches
  directly to a specific layout without changing text or touching the clipboard.
- **Direct text-conversion hotkeys** — sends selected text, the last word, or the
  active field directly to a specific installed layout.
- **Any hotkey** — accepts a key combination or a single key such as `Pause`,
  `F8`, or even a letter.
- **Private last-word buffer** — recently typed words are replaced atomically
  with backspaces and Unicode input, with no selection and no clipboard access.
- **Invisible native edit path** — standard Windows text controls are read and
  replaced directly, with repainting suspended during the atomic range update.
- **Verified clipboard fallback** — existing or ambiguous text is read only on
  demand in browser/custom controls, and the previous clipboard contents are
  restored immediately afterward.
- **Fail-safe last-token replacement** — rejects copy-line results and verifies the
  final selection before replacing anything outside the intended token.
- **Exclusive hotkeys** — configured shortcuts take precedence over
  conflicting application shortcuts whenever Windows permits input interception.
- **Queued commands** — a second deliberate hotkey press waits for the active
  transaction instead of being silently discarded.
- **Native Windows conversion** — preserves capitalization and punctuation and
  supports US, UK, Russian, and other installed layouts.
- **Compact dark UI** — provides a tray menu and dense settings tables without
  a permanent application window.

## Getting started

1. Open **Hotkeys...**, verify the default correction targets, and assign the
   shortcuts you want to use.
2. Focus any editable text field and press a configured hotkey.

The three universal layout-switching actions use the default correction targets
shown in the same window. The case actions operate only on the current selection.
The layout-specific section starts with an empty cyclic input-language hotkey,
then provides an empty direct switch and three empty direct-target text actions
for every installed layout.

## Tray menu

### Hotkeys...

Opens one window with two simultaneously visible sections:

- **Universal** contains mapped-target text switching and selected-text case tools.
- **By installed layout** contains the default correction-target mapping, followed
  by cyclic/direct input-language switching and separated text-conversion groups
  for each installed layout.

To change a hotkey:

1. Click its cell. The previous value disappears immediately.
2. Press a new key or key combination.
3. Click **Save**.

Empty cells are not registered and do not intercept any input. NN Switch rejects
duplicate shortcuts inside its own configuration; Windows may also reject a
shortcut already reserved by another application.

### Exit

Releases all global hotkeys and exits the process.

## Default hotkeys

| Action | Hotkey |
| --- | --- |
| Switch selected text | `Ctrl+Alt+S` |
| Switch last written word | `Ctrl+Alt+W` |
| Switch active text field | `Ctrl+Alt+A` |
| Change selected text to UPPERCASE | Empty |
| Change selected text to lowercase | Empty |
| Change selected text to sentence case | Empty |

Case, cyclic/direct layout, and direct text-conversion hotkeys are empty by default.

## Upgrading

Settings, existing hotkeys, and target mappings from v1.2 are preserved when
upgrading to v1.3. New case and direct layout-switching hotkeys are empty by
default.

Automatic switching has been removed. NN Switch is now exclusively hotkey-driven.
It retains only the bounded, short-lived keystroke history needed for a manual
last-word command; it performs no continuous language detection or automatic
correction.

Obsolete settings from earlier versions are discarded automatically when the
configuration is upgraded.

## Settings and diagnostics

- Settings: `%APPDATA%\NN Switch\settings.json`
- Error log: `%LOCALAPPDATA%\NN Switch\error.log`
- Previous rotated log: `%LOCALAPPDATA%\NN Switch\error.previous.log`

Settings from early builds stored in `%APPDATA%\ИN Switch\settings.json` are
imported automatically. If the current settings file contains invalid JSON,
NN Switch preserves it as `settings.json.corrupt-<timestamp>` before creating
clean defaults.

## Requirements and limitations

- Windows 10 or newer.
- x64 processor.
- Recently typed words use the private in-memory path. Standard Windows edit
  controls also support direct verified replacement for older text. Older text in
  browser/custom controls requires standard selection/copy support from the control.
- Password fields and controls that block selection, copying, or Unicode keyboard
  input may reject the corresponding correction path.
- A regular Windows process cannot send input to an application running as
  administrator. Start NN Switch with the same privileges to work with such a
  window.
- Some terminals and editors redefine the standard copy shortcut.

The executable is currently unsigned, so Windows SmartScreen may display a
warning on first launch.

## Build

.NET 10 SDK x64 is required.

```powershell
.\build.ps1
```

The script runs the fast test suite, publishes a self-contained single-file
executable using the ready-to-use `NN.ico`, and creates a local
`NN Switch.lnk` shortcut in the project root.

Output:

```text
bin\publish\win-x64\NN Switch.exe
```

Run the slower Windows integration checks locally with:

```powershell
.\verify.ps1 -SkipShortcut
```
