# SoundFlip

Switch your Windows audio output between headphones and speakers with one key.

SoundFlip sits in the system tray and flips the default output device when you press **Ctrl+Alt+S** or click the tray icon. A small dark on-screen popup shows which device is now active.

## Features

- **One-key switching:** global hotkey (default `Ctrl+Alt+S`, changeable from the menu)
- **Tray icon:** shows the active device; left-click to switch, right-click for the menu
- **On-screen popup:** Windows 11 style, fades out by itself
- **Device list:** add or remove devices from the menu, e.g. when you buy a new headset
- **Start with Windows:** one click in the menu
- **Languages:** Türkçe / English
- **No dependencies:** a single small exe, nothing to install

## Download

Grab `SoundFlip.exe` from the [latest release](https://github.com/erdemkosk/sound-flip/releases/latest). Every push to `main` builds a new one automatically.

## Build

Windows ships with a C# compiler, so no Visual Studio is needed:

```
build.bat
```

This produces `SoundFlip.exe` next to the source.

## Usage

1. Run `SoundFlip.exe`; an icon appears near the clock.
2. Right-click → **Add device** → pick your headset → **As headphones**, then your speakers → **As speakers**.
3. Press `Ctrl+Alt+S` to flip.

Settings are stored next to the exe:

| File | Contents |
| --- | --- |
| `devices.txt` | devices to switch between (`name part=headphones` / `speakers`) — see `devices.example.txt` |
| `settings.txt` | `hotkey=Ctrl+Alt+S`, `language=tr` or `en` |

Requires Windows 10/11.
