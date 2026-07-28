# NN Switch

<p align="center">
  <img src="NN.ico" width="160" alt="NN Switch logo">
</p>

**NN Switch** is a small Windows utility for correcting text typed with the
wrong keyboard layout. It works on demand: choose an action, assign a hotkey,
and press it when text needs to be fixed.

NN Switch does not continuously analyze what you type and does not install a
global keyboard hook. It only acts when one of its configured hotkeys is
pressed.

## No installation. No Yandex. No bundled extras.

Download `NN Switch.exe` from the
[latest release](https://github.com/Philippsapegin/NNSwitch/releases/latest) and
run it.

- One portable, self-contained x64 executable.
- No installer and no separate .NET runtime.
- No offers to install Yandex, browsers, toolbars, or any other software.
- No advertising, telemetry, or network requests.
- Typed text is never logged or written to files.

The application runs entirely from the system tray, with no main window and no
taskbar button.

## Key features

- **Switch selected text** — corrects the currently selected text.
- **Switch last written word** — corrects the word immediately before the caret.
- **Switch active text field** — corrects all text in the active field.
- **Per-layout targets** — selects a separate destination layout for every
  layout installed in Windows.
- **Direct language hotkeys** — sends selected text, the last word, or the
  active field directly to a specific installed layout.
- **Any hotkey** — accepts a key combination or a single key such as `Pause`,
  `F8`, or even a letter.
- **Clipboard restoration** — restores the previous clipboard contents after
  replacing text.
- **Native Windows conversion** — preserves capitalization and punctuation and
  supports US, UK, Russian, and other installed layouts.
- **Compact dark UI** — provides a tray menu and dense settings tables without
  a permanent application window.

## Getting started

1. Open **Switch to...** and select a target for each source layout.
2. Open **Hotkeys...** and assign the shortcuts you want to use.
3. Focus any editable text field and press a configured hotkey.

The three default actions use the mapping from **Switch to...**. For less common
workflows, the Hotkeys table also provides three empty direct-target actions for
every installed layout.

## Tray menu

### Hotkeys...

Opens the table containing every available action.

To change a hotkey:

1. Click its cell. The previous value disappears immediately.
2. Press a new key or key combination.
3. Click **Save**.

Empty cells are not registered and do not intercept any input. NN Switch rejects
duplicate shortcuts inside its own configuration; Windows may also reject a
shortcut already reserved by another application.

### Switch to...

Selects the target layout used by the three default switching actions for each
possible current layout.

### Exit

Releases all global hotkeys and exits the process.

## Default hotkeys

| Action | Hotkey |
| --- | --- |
| Switch selected text | `Ctrl+Alt+S` |
| Switch last written word | `Ctrl+Alt+W` |
| Switch active text field | `Ctrl+Alt+A` |

Direct language hotkeys are empty by default.

## Upgrading from v1.0

Automatic switching has been removed. NN Switch is now exclusively
hotkey-driven, which eliminates continuous keyboard monitoring and the language
detection heuristics it required.

Existing hotkeys and target mappings are preserved. Obsolete settings are
discarded automatically when the configuration is upgraded.

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
- Password fields and controls that block Ctrl+A, Ctrl+C, Ctrl+X, or Ctrl+V
  cannot be corrected this way.
- A regular Windows process cannot send input to an application running as
  administrator. Start NN Switch with the same privileges to work with such a
  window.
- Some terminals and editors redefine the standard clipboard shortcuts.

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
