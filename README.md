# AudioSwitcher

Gives your keyboard's **Mute** key a second function on Windows:

- **Short tap** → mute/unmute as usual.
- **Hold** (~0.5 s) → switch the default audio output to the next device and show an on-screen popup with its name.

No window and no tray icon: it runs silently in the background.

## Download

Grab the zip from the [latest release](https://github.com/Mechanix97/AudioSwitcher/releases/latest), extract it and run `AudioSwitcher.exe`.

## Requirements

Windows 10/11. Nothing to install: it builds with the .NET Framework 4 `csc.exe` that already ships with Windows.

## Build

```
build.cmd
```

Produces `AudioSwitcher.exe` in the same folder.

## Usage

| Command | What it does |
|---|---|
| `AudioSwitcher.exe` | Starts it in the background (does nothing if already running) |
| `AudioSwitcher.exe --exit` | Stops the running instance |
| `AudioSwitcher.exe --list` | Lists audio outputs (`*` = current) |

### Run at startup

```powershell
$s = (New-Object -ComObject WScript.Shell).CreateShortcut("$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Startup\AudioSwitcher.lnk")
$s.TargetPath = "<path>\AudioSwitcher.exe"; $s.Save()
```

## Configuration

`config.ini`, next to the `.exe` (changes apply after restarting the program):

```ini
# How long (ms) the Mute key must be held to switch output
holdMs=500

# Outputs skipped when cycling (partial name match). One per line.
exclude=Steam Streaming

# 1 = log special-key codes (not letters/digits) to audioswitcher.log.
# Useful if the key doesn't respond.
debug=0
```

## How it works

- A low-level keyboard hook (`WH_KEYBOARD_LL`) intercepts `VK_VOLUME_MUTE`.
- If the key is released before `holdMs`, it re-injects the key so Windows performs the normal mute (with its OSD). Its own key presses are tagged via `dwExtraInfo` instead of relying on the `LLKHF_INJECTED` flag, because some keyboards (e.g. Lenovo) deliver the physical Mute key already flagged as injected.
- To switch the output it uses the undocumented `IPolicyConfig` COM interface (the same one every audio switcher uses), for all three roles: console, multimedia and communications.

## Limitations

- If a game runs as administrator, the hook won't receive the key while that game has focus (run AudioSwitcher as admin too).
- The popup isn't visible over exclusive-fullscreen games (it is in borderless windowed).
