# Hearth Glove Cursor for Windows

[中文](README.md) · [Latest release](https://github.com/Pizzzza11/hearth-glove-cursor/releases/latest)

An ivory-and-gold fantasy glove cursor with a bent-finger click pose, a roughly 10-degree left tilt, matching busy/working animations and a gold-trimmed text I-beam.

![Cursor states](cursor-pack/preview.png)
![Loading animations](cursor-pack/loading-preview.gif)

## Install

Download `HearthGloveCursor-VERSION-windows.zip` from Releases, extract the entire ZIP, and double-click **Install.cmd**. No administrator rights, Python, PowerShell upgrade or development tools are needed. Installation and controls are built into the EXE.

Windows 7 SP1 (32/64-bit) compatibility targets its built-in .NET 3.5 / CLR 2. Windows 10/11 use .NET 4.x / CLR 4. Keep the `.exe.config` file next to the AnyCPU EXE. Windows 7 compatibility has been compiled against the older APIs but has not yet been verified on a Win7 machine/VM. ARM64, remote desktop and all game configurations are also unverified. See Microsoft's [runtime table](https://learn.microsoft.com/en-us/dotnet/framework/install/on-windows-10) and [runtime selection rules](https://learn.microsoft.com/en-us/dotnet/framework/configure-apps/file-schema/startup/supportedruntime-element).

This release is unsigned. Verify the source and published SHA256 before running a download. Corporate execution policies may prevent installation.

## Controls

| File | Action |
| --- | --- |
| Install.cmd | Install/upgrade; enable login startup by default |
| Stop.cmd | Pause the guard and click detection, including future logins |
| Start.cmd | Resume the complete cursor set |
| Status.cmd | Show process count, login command, paths and runtime version |
| Uninstall.cmd | Stop both processes and restore the saved cursor/login settings |

Files are installed under `%LOCALAPPDATA%\HearthGloveCursor`. Upgrades preserve `original-backup-v2.xml`. Uninstall retains files and archived backups for recovery. You may delete the installation folder after successful uninstall. A fresh install can skip startup with `Install.cmd --no-startup`; this does not remove existing startup entries. For pre-release JSON-backup versions, first use the old uninstaller, preserve/move the old installation folder, then install this release.

The main process samples mouse buttons roughly every 8 ms and verifies five cursor roles roughly every second. A second instance of the same executable watches the main process and attempts to restart it after two seconds. A current-user Run entry starts the EXE directly at login. Named mutexes prevent duplicate instances. There is no keyboard logging, networking, telemetry or automatic updating.

**Pause or uninstall before switching to another system cursor theme.** The enabled guard deliberately restores all five managed roles when any is replaced. Text I-beam styling is static; it does not change an application's blinking insertion caret. Resize, forbidden and other unmanaged cursor roles are unchanged.

Very short clicks can fall between samples. Games rendering their own pointers, secure desktops and lock screens may ignore system cursors. Simultaneous termination of both processes, disabled startup entries and security policies can prevent recovery. Reboot behavior is not guaranteed across every machine; build checks do not substitute for real-machine reboot testing.

If startup fails, run Status.cmd, check startup settings (msconfig on Win7) and run Start.cmd. The latest management result is also saved to `%TEMP%\HearthGloveCursor-management.txt`. When filing an issue, include Windows version and reproduction steps, but redact your username/paths. Do not upload backup XML/JSON files.

## Build

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Build.ps1 -Version 1.0.0
```

The build script requires Windows PowerShell 5.1+ on the build machine. It uses the .NET 3.5 compiler, or downloads Microsoft's 3.5 reference assemblies from NuGet and uses the system C# compiler. It checks every asset through the Windows loader and creates a ZIP plus SHA256SUMS.txt under `dist`. End users do not need PowerShell. Move aside an existing version staging directory before rebuilding the same version.

To regenerate assets, install Python 3.10+ and `requirements-dev.txt`, then run `python tools/build_cursors.py`. Run structural/PNG integrity checks with `python -m unittest discover -s tests -v`. GitHub Actions builds commits and PRs; pushing a `v*` tag publishes a release.

MIT for original code and self-made assets. Unofficial fan project, not affiliated with Blizzard Entertainment. See [NOTICE.md](NOTICE.md) for design provenance and third-party rights.
