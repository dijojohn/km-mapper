# InputMonitorMapper

A small Windows app that lets you **map your keyboard and mouse to a specific monitor** by locking the mouse cursor to that monitor. Useful for multi-monitor setups where you want the cursor (and thus focus) to stay on one display.

## Features

- **Enumerate monitors** – Lists all connected displays (primary is marked).
- **Single mouse: lock to one monitor** – Confines the cursor to the selected monitor’s bounds using Windows’ `ClipCursor` API.
- **Two mice: bind each to a different monitor** – With two physical mice connected, you can assign each mouse to a monitor. When **multi-mouse** is enabled, the Raw Input API is used so that:
  - Mouse A only moves the cursor within its assigned monitor (and clicks go to that monitor).
  - Mouse B only moves the cursor within its assigned monitor.
  So each mouse effectively “owns” one monitor. Requires two mice; enable from the “Two mice” section.
- **Two keyboards: bind each to a different monitor** – With two physical keyboards connected, you can assign each keyboard to a monitor. When **multi-keyboard** is enabled:
  - Keys from keyboard A are sent to the window that was last focused on monitor A.
  - Keys from keyboard B are sent to the window that was last focused on monitor B.
  The app tracks which window has focus on each monitor (updated as you click or switch windows). Focus is switched to that window before injecting keys. *Note:* Windows may block focus changes from background apps in some cases; if keys don’t go to the right window, click that window once with the mouse for that monitor, then try again.
- **Unlock** – Releases the single-mouse clip from the main window or tray.
- **System tray** – Tray icon; double-click to show the window; menu: Show window, Unlock mouse, Exit.

## Requirements

- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (for building)

## Build and run

### Option 1: Script (after installing .NET 8)

1. Install the **.NET 8 SDK** from PowerShell (no browser needed):
   ```powershell
   cd C:\Users\dmjohn\InputMonitorMapper
   .\install-dotnet-sdk.ps1
   ```
   The script uses **winget** if available, otherwise downloads the SDK installer and runs it. Then close and reopen PowerShell so `dotnet` is on your PATH.
   - Or install manually: https://dotnet.microsoft.com/download/dotnet/8.0 → Windows x64 SDK.

2. In PowerShell, from the project folder:

```powershell
cd C:\Users\dmjohn\InputMonitorMapper
.\build-and-run.ps1
```

The script builds in Release and then runs the app. To only build:

```powershell
dotnet build -c Release
dotnet run -c Release --no-build
```

### Option 2: Visual Studio 2022

Open `InputMonitorMapper.sln`, then press **F5** to run or **Ctrl+Shift+B** to build.

### Run the built executable directly

After a successful build:

- **Release:** `bin\Release\net8.0-windows\InputMonitorMapper.exe`
- **Debug:** `bin\Debug\net8.0-windows\InputMonitorMapper.exe`

## Usage

1. Start the app. Your monitors are listed.
2. Select the monitor you want to bind the mouse to.
3. Click **Lock to selected monitor**. The cursor is now confined to that monitor.
4. Click **Unlock mouse** to release, or use the tray icon → **Unlock mouse**.
5. You can minimize the window; the tray icon keeps the app running.

## Notes

- **Single mouse:** Lock uses `ClipCursor`; the cursor cannot leave the chosen monitor.
- **Two mice:** Multi-mouse mode uses Raw Input with `RIDEV_NOLEGACY`; this app moves the cursor and injects clicks. If you close the app while multi-mouse is on, **disable multi-mouse first** or the assigned mice may stop working until you reopen and disable, or reboot.
- **Two keyboards:** Multi-keyboard mode uses Raw Input with `RIDEV_NOLEGACY`; this app injects each key into the “target window” for that keyboard’s monitor (the last focused window on that monitor). Windows sometimes blocks `SetForegroundWindow` from background processes; if keys don’t appear in the right window, click that window once so it becomes foreground, then type again.
- **Keyboard:** Windows has a single keyboard focus; the active window gets key input. This app does not route a specific keyboard to a specific monitor.
- **Virtual desktops:** This app does not bind input to Windows virtual desktops; it only works with physical monitors.

## License

Use and modify as you like.
