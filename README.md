# NN Switch

<p align="center">
  <img src="NN.ico" width="160" alt="NN Switch logo">
</p>

**NN Switch** is a compact Windows utility that fixes text typed using the wrong
keyboard layout.

## No installation. No Yandex. No bundled extras.

Download the single `NN Switch.exe` file from the
[Releases](https://github.com/Philippsapegin/NNSwitch/releases/latest) page and
run it.

- No installer.
- No separate .NET runtime is required.
- No offers to install Yandex, browsers, toolbars, or any other software.
- No advertising, telemetry, or network requests.
- Typed text is never written to files.

NN Switch runs entirely from the system tray, with no main window and no taskbar
button.

## Key features

- **Switch selected text** — fixes the currently selected text.
- **Switch last written word** — fixes the word immediately before the caret.
- **Switch active text field** — fixes all text in the active field.
- **Switch to** — configures an independent target for every keyboard layout
  installed in Windows.
- **Direct language hotkeys** — every installed layout gets three additional
  actions. Selected text, the last word, or the entire field can be sent directly
  to that layout without using the regular target mapping.
- **Any hotkey** — assign a key combination or a single key such as `Pause`, `F8`,
  or even a letter.
- **Clipboard restoration** — the previous clipboard contents are restored after
  replacing text.
- **Native Windows layout conversion** — preserves capitalization and punctuation
  and supports US, UK, and other installed layouts.
- **Dark tray UI** — compact menus and settings tables with a consistent dark
  appearance.

## Tray menu

### Hotkeys...

Opens the table containing every available action. The first three rows use the
mapping configured under `Switch to...`. Three empty direct-target hotkeys are
then added dynamically for every installed keyboard layout.

To change a hotkey:

1. Click its cell. The previous value disappears immediately.
2. Press a new key or key combination.
3. Click **Save**.

Empty cells are not registered and do not intercept any input.

### Switch to...

Selects the target layout used by the three regular switching commands for each
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

## Settings

- `%APPDATA%\NN Switch\settings.json`
- `%LOCALAPPDATA%\NN Switch\error.log`

Settings from earlier builds stored in `%APPDATA%\ИN Switch\settings.json` are
imported automatically on first launch.

## Known limitations

- Password fields and controls that block Ctrl+A, Ctrl+C, Ctrl+X, or Ctrl+V cannot
  be corrected this way.
- A regular Windows process cannot send input to an application running as
  administrator. NN Switch must be started with the same privileges to work with
  such a window.
- Some terminals and editors redefine the standard clipboard shortcuts.

## Build

.NET 10 SDK x64 is required.

```powershell
.\build.ps1
```

The script:

1. Runs the fast test suite.
2. Publishes a self-contained single-file executable using the ready-to-use
   `NN.ico` stored in the repository.
3. Creates a local `NN Switch.lnk` shortcut in the project root.

Output:

```text
bin\publish\win-x64\NN Switch.exe
```

Run the slower Windows integration checks locally with:

```powershell
.\verify.ps1 -SkipShortcut
```
