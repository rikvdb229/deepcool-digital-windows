# deepcool-digital-windows

A small, lightweight replacement for the DeepCool software on Windows: it drives the digital
displays of DeepCool LD-series AIO coolers and CH-series cases, and nothing else.
The Windows counterpart of [deepcool-digital-linux](https://github.com/Nortank12/deepcool-digital-linux).

> **Unofficial.** Not affiliated with or endorsed by DeepCool. DeepCool and the product names
> mentioned here are trademarks of their respective owner.

- One small background process (a SYSTEM startup task), no tray app, no UI.
- No per-reading logging: only connect/disconnect events, in a log capped at 256 KB.
- Calm, readable digits: readings are averaged and only change when they really move (configurable).

**Why:** the official DeepCool service (`deep_service.exe`) writes every sensor reading to a
`service.log` that is never rotated. On the author's PC it reached **90 GB** and filled the drive.

## Supported devices

| Device | USB ID | Status |
|---|---|---|
| DeepCool **LD360** AIO (CPU temperature, power, usage) | `3633:000A` | ✅ Tested |
| DeepCool **CH560 DIGITAL** case (CPU + GPU temperature or usage, usage bars) | `3633:0005` | ✅ Tested |
| DeepCool LD240 | `3633:000A` | Should work (same protocol) |
| DeepCool CH360 DIGITAL, MORPHEUS | `3633:0005` / `0007` / `0015` | Should work (same protocol) |
| AK-Digital, LS, AG, LP, LQ, ASSASSIN IV, CH510 | | ❌ Not yet: protocols exist in the Linux project, not ported |

Tested on Windows 11 with an AMD Ryzen 7 7800X3D and an AMD Radeon RX 7800 XT. Intel CPUs and
NVIDIA/Intel GPUs are supported by the sensor library but untested. Please open an issue to report
whether it works on your hardware.

## Install

1. Download `deepcool-digital-windows-<version>-win-x64.zip` from **Releases** and unzip it.
2. Open **PowerShell as administrator** in the unzipped folder and run:
   ```powershell
   powershell -ExecutionPolicy Bypass -File .\install.ps1
   ```

The installer:
- installs the [PawnIO](https://pawnio.eu) driver (via winget), needed to read the CPU temperature;
- stops and disables the DeepCool software (it stays installed) and deletes its runaway `service.log`;
- copies the program (`DeepCoolDisplay.exe`) to `C:\Program Files\DeepCoolDisplay` and registers a
  startup task named `DeepCoolDisplay` running as SYSTEM;
- prints the detected devices and sensor readings.

To undo: `.\uninstall.ps1` (as administrator) removes DeepCoolDisplay and turns the DeepCool software back on.

## Options

| Option | Default | Meaning |
|---|---|---|
| `--ch-mode temp\|usage\|auto` | `temp` | CH case display shows temperatures, usage, or alternates every 5 s |
| `--smooth <seconds>` | `5` | Averaging time for all readings; `0` shows the raw value every second |
| `--deadband-temp <°C>` | `1` | Minimum change before a shown temperature moves |
| `--deadband-usage <%>` | `3` | Same for CPU/GPU usage |
| `--deadband-power <W>` | `3` | Same for CPU power |

Raw sensor values jump several units per second (Ryzen Tctl, bursty GPU load); the defaults keep
the digits calm while still following real changes within a few seconds.

Set them at install time, for example `.\install.ps1 -ChMode auto -Smooth 10 -DeadbandUsage 5`
(parameters `-ChMode`, `-Smooth`, `-DeadbandTemp`, `-DeadbandUsage`, `-DeadbandPower`),
or edit the `DeepCoolDisplay` task's arguments in Task Scheduler.

`DeepCoolDisplay.exe --list` shows the detected devices and all sensor readings (run as administrator).

## Build from source

Requires the .NET 9 SDK.

```powershell
dotnet publish -c Release -o publish          # framework-dependent build for development
.\build-release.ps1                            # self-contained release zip in .\dist
```

## Credits

The USB protocol is ported from [deepcool-digital-linux](https://github.com/Nortank12/deepcool-digital-linux)
by Nortank12 (GPL-3.0). Sensors via [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor),
USB via [HidSharp](https://www.zer7.com/software/hidsharp).

Built with AI assistance (Claude); reviewed and tested on real hardware by the author.

## License

Copyright (C) 2026 Rik Vanden Boer.
GPL-3.0, see [LICENSE](LICENSE). Unofficial; not affiliated with or endorsed by DeepCool.
