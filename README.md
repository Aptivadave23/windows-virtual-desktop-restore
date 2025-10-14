[![Build](https://img.shields.io/github/actions/workflow/status/Aptivadave23/windows-virtual-desktop-restore/build.yml?branch=main)](https://github.com/Aptivadave23/windows-virtual-desktop-restore/actions)
[![Latest release](https://img.shields.io/github/v/release/Aptivadave23/windows-virtual-desktop-restore)](https://github.com/Aptivadave23/windows-virtual-desktop-restore/releases)
[![License: Unlicense](https://img.shields.io/badge/License-Unlicense-blue.svg)](LICENSE)

# Restore Virtual Desktop Workspaces for Windows 11

This is a tiny Windows helper that opens your daily apps on specific **Windows 11 virtual desktops** whenever you sign in.

- **Virtual-desktop aware** (e.g., “Thing 1”, “Thing 2”, or whatever you want to name your desktops)
- **Per-user autostart** via the built-in `--install` mode (no admin required)
- **Single-instance + debounce** to prevent duplicate runs at logon
- **Lightweight logging** to `%LOCALAPPDATA%\BootWorkspace\bootworkspace.log`

> The executable name depends on your project (e.g., `StartUp.exe`).  
> The app reads a `workspace.json` file that sits **next to the EXE**.

---

## Requirements

- Windows 11 (virtual desktops enabled)
- .NET 8 SDK to build
- NuGet dependency: [`Slions.VirtualDesktop`](https://www.nuget.org/packages/Slions.VirtualDesktop)

**Project TFM** (in your `.csproj`):
```xml
<TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>
```

**Entry point:** mark `Main` with `[STAThread]`.

---

## Releases

### Download & install (no build required)
1. Go to the **[Releases](https://github.com/Aptivadave23/windows-virtual-desktop-restore/releases)** page and download the latest `BootWorkspace.zip`.
2. Unzip it anywhere.
3. (Optional) Edit `workspace.json` to customize what launches and on which desktops.
4. From the unzipped folder, run:
   ```powershell
   .\StartUp.exe --install ".\workspace.json"
   ```
   This copies the app to `%LOCALAPPDATA%\BootWorkspace` and creates a Startup shortcut (`shell:startup → Boot Workspace.lnk`).

To uninstall later:
```powershell
%LOCALAPPDATA%\BootWorkspace\StartUp.exe --uninstall
```

> Windows SmartScreen may warn about running an unsigned app. Choose **More info → Run anyway** if you trust it.  
> If your policy blocks downloaded files, run `Unblock-File .\StartUp.exe` first.

---

## Quick Start (build from source)

1) **Build** (or Publish) the project:
```powershell
dotnet restore
dotnet build -c Release
# or publish a single-file build:
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:PublishTrimmed=false
```

2) **Create** a `workspace.json` next to the EXE (see example below).

3) **Install** (sets up per-user autostart and copies files to `%LOCALAPPDATA%\BootWorkspace`):
```powershell
.\StartUp.exe --install ".\workspace.json"
```

4) **Sign out/in** (or reboot) to see your apps open on the correct virtual desktops.

To **uninstall**:
```powershell
.\StartUp.exe --uninstall
```

---

## Configure (`workspace.json`)

Place this file **beside the EXE** before running `--install`. Example:

```json
{
  "desktops": [
    { "index": 0, "name": "Thing 1" },
    { "index": 1, "name": "Thing 2" }
  ],
  "apps": [
    { "name": "Outlook", "path": "C:\\Program Files\\Microsoft Office\\root\\Office16\\OUTLOOK.EXE", "desktop": "Thing 1" },
    { "name": "Teams (new)", "path": "%LOCALAPPDATA%\\Microsoft\\WindowsApps\\ms-teams.exe", "desktop": "Thing 1" },
    { "name": "Chrome", "path": "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe", "args": "--profile-directory=\"Default\"", "desktop": "Thing 2" },
    { "name": "GitHub Desktop", "path": "%LOCALAPPDATA%\\GitHubDesktop\\GitHubDesktop.exe", "desktop": "Thing 2" },
    { "name": "Obsidian", "path": "%LOCALAPPDATA%\\Obsidian\\Obsidian.exe", "desktop": "Thing 2" }
  ],
  "launchDelayMs": 1500
}
```

**Fields**
- `desktops[]` (optional): names and ensures count (`index` is 0-based).
- `apps[]`:
  - `name` — label for logging
  - `path` — full path or env-var path  
    (URIs like `microsoft-edge:https://...` and `shell:AppsFolder\...` are supported)
  - `args` — optional command-line args
  - `desktop` — target desktop (accepts `"Thing 2"`, `"Desktop 2"`, or `"1"`)
- `launchDelayMs` — pause between launches (ms)

You can edit `%LOCALAPPDATA%\BootWorkspace\workspace.json` anytime after installation.

---

## PowerShell Installation

You have two options:

### A) Use the provided script (recommended)
The repo includes **`release-publish.ps1`**, which builds, publishes, ensures `workspace.json`, and runs the self-installer for you:

```powershell
# PowerShell 7+
pwsh -File .\release-publish.ps1 -Install
```

### B) Manual publish & install
```powershell
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:PublishTrimmed=false

# then:
.\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\StartUp.exe --install ".\workspace.json"
```

---

## How it Works

- Waits for Explorer/taskbar (`Shell_TrayWnd`) to be ready, avoiding races with the shell.
- Ensures the requested number of virtual desktops exist; applies configured names if provided.
- Switches to the target desktop **before** launching each app, so the window lands on that desktop.
- Prevents duplicates via:
  - **Single-instance** named mutex (`Local\BootWorkspace_SingleInstance`)
  - **Debounce** file (`last_run.txt`) that suppresses a new run if one executed within the last 5 minutes
- Writes a simple log to `%LOCALAPPDATA%\BootWorkspace\bootworkspace.log`.

---

## Installation Details

Running `--install` will:

- Copy the EXE **and all files from the current folder** to `%LOCALAPPDATA%\BootWorkspace`
- Copy your `workspace.json` (or create a minimal default if none found)
- Create a **Startup** shortcut here: `shell:startup → Boot Workspace.lnk`  
  (Target = `%LOCALAPPDATA%\BootWorkspace\StartUp.exe`, Start in = same folder)

> **Tip:** Run `--install` from the **publish** folder so all required DLLs are copied with the EXE.

To remove, run `--uninstall` (deletes the Startup shortcut and the install folder).

---

## Troubleshooting

- **Nothing launches**  
  Check `%LOCALAPPDATA%\BootWorkspace\bootworkspace.log` for errors.  
  Ensure the Startup shortcut points to `%LOCALAPPDATA%\BootWorkspace\StartUp.exe`.

- **Double launches**  
  Remove duplicate launch points (Task Scheduler, Common Startup, Run keys).  
  The app also blocks duplicates via a mutex + debounce.

- **Teams path**  
  For New Teams use `%LOCALAPPDATA%\Microsoft\WindowsApps\ms-teams.exe`;  
  for Classic Teams use the paths under `%LOCALAPPDATA%\Microsoft\Teams\`.

---

## Contributing

Issues and PRs welcome. If you add features (e.g., window sizing/monitor placement), keep the defaults simple and safe for first-run.

---

## License

This is free and unencumbered software released into the public domain.

See the [LICENSE](LICENSE) file or <https://unlicense.org/> for details.

SPDX-License-Identifier: Unlicense

---

## Credits

- Virtual desktop wrapper: [`Slions.VirtualDesktop`](https://www.nuget.org/packages/Slions.VirtualDesktop)
beta Mon Oct 13 19:52:22 CDT 2025
