<div align="center">

<img src="docs/screenshots/githublogo.png" alt="OmenCore Logo" width="520" />

# OmenCore

## A Modern, Lightweight Control Center for HP OMEN & Victus Gaming Laptops

</div>

---

**OmenCore** is an **independent control center** for HP OMEN and Victus laptops. It runs without OMEN Gaming Hub installed, avoids bloatware/outbound telemetry/ads, and uses local WMI BIOS, EC, and platform backends where the hardware exposes them.

### Why OmenCore?

| Feature | Status |
|---------|--------|
| **100% OGH-Independent** | ✅ Works without OMEN Gaming Hub installed |
| **Zero Bloatware** | ✅ Self-contained artifacts, no runtime installs |
| **No Outbound Telemetry** | ✅ Diagnostics and config stay on your machine |
| **Ad-Free** | ✅ Clean, focused interface |
| **Offline Operation** | ✅ No sign-in required, fully local control |
| **Cross-Platform** | ✅ Windows WPF + Linux CLI & Avalonia GUI |

---

### ⚡ Quick Links

[![Version](https://img.shields.io/badge/version-3.6.2-red.svg?style=for-the-badge)](docs/CHANGELOG_v3.6.2.md)
[![License](https://img.shields.io/badge/license-MIT-green.svg?style=for-the-badge)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-purple.svg?style=for-the-badge)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![Discord](https://img.shields.io/badge/Discord-Join%20Server-5865F2.svg?style=for-the-badge&logo=discord&logoColor=white)](https://discord.gg/9WhJdabGk8)
[![Donate](https://img.shields.io/badge/Donate-PayPal-00457C.svg?style=for-the-badge&logo=paypal&logoColor=white)](https://www.paypal.com/donate/?business=XH8CKYF8T7EBU&no_recurring=0&item_name=Thank+you+for+your+generous+donation%2C+this+will+allow+me+to+continue+developing+my+programs.&currency_code=AUD)

---

### 📸 Interface Preview

![OmenCore Main Window](docs/screenshots/main-window.png)

## 🚀 **Quick Start**

### Windows

1. Open the [Releases](https://github.com/theantipopau/omencore/releases) page
2. Download the latest `OmenCoreSetup-<version>.exe` or `OmenCore-<version>-win-x64.zip`
3. Run OmenCore as Administrator

See the [Full Installation Guide](INSTALL.md#-windows-installation).

### Linux (CachyOS • Arch • Ubuntu • Fedora • Debian • Pika OS)

```bash
# Replace VERSION with the published release you want to install.
VERSION=<release-version>
wget "https://github.com/theantipopau/omencore/releases/download/v${VERSION}/OmenCore-${VERSION}-linux-x64.zip"
mkdir -p OmenCore-linux-x64
unzip "OmenCore-${VERSION}-linux-x64.zip" -d OmenCore-linux-x64
cd OmenCore-linux-x64
chmod +x omencore-cli omencore-gui

# CLI: Check status
sudo ./omencore-cli status

# GUI: Launch Avalonia
./omencore-gui
```

See the [Complete Linux Guide](docs/LINUX_INSTALL_GUIDE.md) or the [Quick Reference](INSTALL.md#-linux-installation).

### Switching Linux distros (OMEN Slim / four-zone RGB)

If keyboard lighting worked on one distro (e.g. CachyOS) but not after moving to another
(e.g. Debian, Ubuntu, Pika OS), the driver is usually fine — setup steps are easy to miss
on a fresh install. Work through this checklist:

1. **Install the enhanced `hp-wmi` DKMS module** (2023+ OMEN / Victus boards such as `8D40`):
   ```bash
   # Debian / Ubuntu / Pika OS
   sudo apt install dkms build-essential linux-headers-$(uname -r)

   # Arch / CachyOS
   sudo pacman -S dkms linux-headers
   ```
   Then install [hp-omen-dkms](https://github.com/saikiran2001-v2/hp-omen-dkms) using version
   **1.1.0** from its `dkms.conf` (not 1.0.0):
   ```bash
   sudo dkms add .
   sudo dkms install hp-wmi/1.1.0
   sudo modprobe -r hp_wmi && sudo modprobe hp_wmi
   modinfo -n hp_wmi   # should point at .../updates/dkms/hp-wmi.ko
   ```

2. **Install the udev rule** so the GUI can write sysfs without sudo:
   ```bash
   sudo cp scripts/99-omencore-hp-wmi.rules /etc/udev/rules.d/
   sudo udevadm control --reload-rules
   sudo udevadm trigger --subsystem-match=platform --action=change
   ls -l /sys/devices/platform/hp-wmi/fourzone_*   # should show rw-rw-rw-
   ```

3. **Check the hardware brightness gate** — four-zone keyboards expose both
   `fourzone_color` and `fourzone_brightness`. Colors can be set while brightness stays
   at `0`, which leaves the keyboard physically off:
   ```bash
   cat /sys/devices/platform/hp-wmi/fourzone_brightness   # must not be 0
   omencore-cli keyboard --brightness 100
   omencore-cli keyboard --color 00BFFF
   ```

4. **Verify nodes exist** before blaming the distro or kernel:
   ```bash
   ls /sys/devices/platform/hp-wmi/fourzone_*
   omencore-cli keyboard
   ```

Full walkthrough: [Linux install guide — Distro hopping](docs/LINUX_INSTALL_GUIDE.md#distro-hopping-checklist-four-zone-keyboard-rgb).

### Linux issue reporting (one-command triage bundle)

When reporting Linux model support issues (for example missing `hp-wmi` fan interfaces), run:

```bash
./qa/collect-linux-triage.sh
```

This generates a timestamped folder with:
- `omencore-linux-triage.txt` (kernel/OS, CLI status/diagnose output, sysfs snapshots)
- optional `acpidump.dat` when `acpidump` is available

Attach those files to your GitHub issue for faster triage.

## 🔥 **What's New in v3.6.2**

v3.6.2 is the current stabilization release, focused on runtime source-of-truth correctness, fan/performance mode confirmation flow hardening, RGB fallback reliability, Linux packaging/diagnostics refinement, and lower idle/tray UI overhead.

### v3.6.2 Highlights

- **Fan/performance state stabilization** across tray, hotkeys, OSD, dashboard, and linked-mode flows.
- **Reduced high-CPU fan-curve pressure** by bounding WMI keepalive writes and automatic curve verification.
- **EC coordination hardening** for fan, performance, keyboard, power verification, diagnostics, and GPU boost fallback paths.
- **Capability truth improvements** for undervolt readiness, unknown model fallback messaging, and Linux profile/fan capability reporting.
- **UI responsiveness and layout fixes** for tray/OSD state freshness, low-overhead dashboard updates, and high-DPI/narrow-window clipping.

### Release Notes

Current stable release is **v3.6.2**.

→ **[v3.6.2 Changelog](docs/CHANGELOG_v3.6.2.md)**

→ **[v3.6.1 Changelog](docs/CHANGELOG-3.6.1.md)**

→ **[Previous Stable Changelog (v3.4.1)](docs/CHANGELOG_v3.4.1.md)**

---

## 📦 **Downloads & Artifacts**

**Version:** v3.6.2 | **Status:** Stable

Release artifacts:

| Download | Platform | Details |
|----------|----------|----------|
| **OmenCoreSetup-3.6.2.exe** | Windows | Installer (Recommended) — Includes .NET 8 runtime |
| **OmenCore-3.6.2-win-x64.zip** | Windows | Portable — Extract and run, no installation |
| **OmenCore-3.6.2-linux-x64.zip** | Linux | CLI + Avalonia GUI, self-contained runtime |

### SHA256

`OmenCoreSetup-3.6.2.exe`  
`B97E1F2D2137498DCC3B170FB9E33ADF1505FB94F7603805CCD96B1AB4E30895`

`OmenCore-3.6.2-win-x64.zip`  
`DCAAAB9103FA5D574A49990E734E76A4F1A67AE63083F7195F204A2A043630BC`

`OmenCore-3.6.2-linux-x64.zip`  
`78F571EECBE16F38882453B7281759AE4592D3DB1CCFA1ACFF39E9DDC5579C99`

---

## 🔧 **Features**

### Thermal & Fan Management

- Custom fan curves with temperature breakpoints — CPU and GPU fans controlled independently
- WMI BIOS control — no driver required, works on AMD and Intel models
- EC-backed presets (Max, Auto, Manual) for instant fan switching
- Real-time monitoring with live CPU/GPU temperature history charts
- Per-fan telemetry — RPM and duty cycle for each cooling zone
- System tray badge — live CPU temperature on the notification icon
- CPU Temperature Limit — TCC offset control (Intel only)
- Fan preset save/load — name, export, import, and share `.omencore` profiles
- 0% duty remapping — curve interpolation can never stall fans below the configured minimum (v3.2.0)

### Performance Control

- CPU undervolting via Intel MSR with independent core/cache offset sliders (typical safe range: -80 to -125 mV)
- Performance modes (Quiet, Balanced, Performance, Turbo) — CPU/GPU wattage envelope management (decoupled from fan mode in v3.3.0)
- GPU Power Boost — +15W Dynamic Boost (PPAB)
- GPU mux switching — Hybrid, Discrete (dGPU), and Integrated (iGPU)
- Per-game profiles — auto-switch on game process detection
- External tool detection — defers MSR control when ThrottleStop/Intel XTU is active

### RGB Lighting

- Keyboard lighting profiles — Static, Breathing, Wave, Reactive (multi-zone)
- 4-zone OMEN keyboards with per-zone color and intensity
- Per-key RGB on OMEN Max 16 (individual key addressing)
- Peripheral sync — apply themes to Corsair/Logitech/Razer devices
- Linux sysfs-based RGB capability detection (v3.2.0)

### Hardware Monitoring

- Real-time telemetry — CPU/GPU temp, load, clocks, RAM usage, SSD temp
- Telemetry state model: `Valid`, `Inactive`, `Unavailable`, `Stale`, `Degraded`, `Invalid`
- Dashboard banners for Stale and Degraded states with contextual messaging (v3.2.0)
- Rolling 60-sample history charts with 0.5° / 0.5% change threshold
- Low overhead mode — disables charts; reduces idle CPU from ~2% to <0.5%

### System Optimization

- HP OMEN Gaming Hub removal — guided cleanup with dry-run mode
- Gaming Mode — one-click service/animation toggle
- Battery care — adjustable charge limit (60–100%)
- OSD in-game overlay — click-through, configurable metrics
- Memory optimizer — smart/deep RAM clean using Windows native API
- Bloatware scanner — AppX detection, startup item manager, scheduled task cleaner

### Auto-Update

- Polls GitHub Releases every 6 hours
- SHA256 verification required (updates rejected without hash for security)
- One-click download with progress indicator and integrity validation
- Manual fallback if SHA256 is absent from release notes

---

## 🎮 HP Gaming Hub Feature Parity

OmenCore is designed to replace the core local-control workflows of OMEN Gaming Hub on supported hardware.

| HP Gaming Hub Feature | OmenCore | Notes |
|----------------------|---------|-------|
| Fan Control | Supported models | Custom curves + WMI BIOS/EC presets where firmware exposes control |
| Performance Modes | Supported models | CPU/GPU power envelope via WMI/profile backends where available |
| CPU Undervolting | Intel-supported systems | Intel MSR with safety clamping; hidden when runtime access is blocked |
| GPU Power Boost | Supported OMEN models | +15W Dynamic Boost (PPAB) where BIOS exposes it |
| Keyboard RGB | Supported keyboards | Per-zone + per-key on supported models |
| Hardware Monitoring | ✅ Full | LibreHardwareMonitor integration |
| Gaming Mode | ✅ Full | Service/animation optimization |
| Battery Care | Supported models | Adjustable charge limit where firmware exposes it |
| Peripheral Control | Beta | Corsair/Logitech/Razer hardware detection ready |
| Hub Cleanup | ✅ Exclusive | Safe OGH removal tool |
| Per-Game Profiles | ✅ Full | Auto-switch on process detection |
| In-Game Overlay | ✅ Full | Click-through OSD |
| Network Booster | ✅ Out of scope | Use router/Windows QoS |
| Game Library | ✅ Out of scope | Use Steam/Epic/Xbox app |
| Omen Oasis | ✅ Out of scope | Cloud gaming out of scope |

**OmenCore covers the essential local-control Gaming Hub workflows on supported OMEN/Victus hardware** with better performance, no outbound telemetry, no ads, and full offline operation. Unsupported or unverified features are gated clearly rather than presented as guaranteed.

---

## 📋 Requirements

### System

- **OS:** Windows 10 (build 19041+) or Windows 11
- **Runtime:** Self-contained — .NET 8 embedded, no separate installation needed
- **Privileges:** Administrator for WMI BIOS/EC/MSR operations
- **Disk:** ~120 MB for app + ~50 MB logs/config
- **OGH:** NOT required — OmenCore works without OMEN Gaming Hub

### Hardware

- **CPU:** Intel 6th-gen+ for undervolting/TCC offset; AMD Ryzen supported for monitoring and fan control
- **Laptop:** HP OMEN 15/16/17 series and HP Victus (2019–2025 models)
  - ? Tested: OMEN 15-dh, 16-b, 16-k, 17-ck (2023/2024), Victus 15/16
  - ? OMEN Max 16 (2025): per-key RGB, RTX 50-series, full support
  - ? OMEN Transcend 14/16: WMI BIOS support
  - ? 2023+ models: full WMI BIOS support, no OGH needed
- **Desktop:** HP OMEN 25L/30L/40L/45L (limited support; monitoring, profiles, and OGH cleanup functional)

### Fan Control Driver Priority

1. **WMI BIOS** (default) — no driver, works on all OMEN laptops
2. **EC via PawnIO** — Secure Boot compatible
3. **EC via WinRing0** — legacy; may need Secure Boot disabled
4. **OGH Proxy** — last resort fallback

### Optional Drivers

- **PawnIO** — recommended for advanced EC/MSR access (Secure Boot compatible)
- **WinRing0 v1.2** — legacy kernel driver

> **Antivirus note:** Some AV products flag OmenCore's kernel driver as suspicious — this is a known false positive for hardware utilities that use low-level driver access. Known detections: `HackTool:Win64/WinRing0` (Windows Defender), `Gen:Application.Venus.Cynthia.Winring` (Bitdefender). See [ANTIVIRUS_FAQ.md](docs/ANTIVIRUS_FAQ.md) for per-vendor exclusion steps and [DEFENDER_FALSE_POSITIVE.md](docs/DEFENDER_FALSE_POSITIVE.md) for Windows Defender specifics.

**Compatibility:**
- HP Spectre: fan control and monitoring work; CPU/GPU power limits unavailable (different EC layout)
- HP Victus: fan control, monitoring, and keyboard backlight work; GPU TGP/PPAB and CPU undervolting unavailable (BIOS does not expose these on Victus)
- Non-OMEN HP laptops: monitoring only
- Other brands: not supported
- Virtual machines: monitoring-only mode

---

## 🏗️ Architecture

**Stack:** .NET 8.0 / WPF (Windows) / Avalonia (Linux) / LibreHardwareMonitor / EC Direct / Intel MSR

```
OmenCore/
+-- src/OmenCoreApp/              # Windows WPF app (ViewModels, Views, Services, Controls)
+-- src/OmenCore.HardwareWorker/  # Out-of-process hardware worker — crash isolation
+-- src/OmenCore.Avalonia/        # Avalonia cross-platform UI (ViewModels, Services)
+-- src/OmenCore.Desktop/         # Archived prototype (not part of OmenCore.sln shipping builds)
+-- src/OmenCore.Linux/           # Linux hardware: hp-wmi, ec_sys, sysfs RGB probing
+-- installer/                    # Inno Setup script
+-- config/                       # default_config.json
+-- docs/                         # Changelogs, audit reports, guides
+-- VERSION.txt                   # Current release/version marker
```

**Principles:** Safety-first EC write allowlist · Async by default · Telemetry change-detection (0.5°/0.5%) · Graceful per-service degradation · Out-of-process crash isolation

---

## 🛠️ Development

### Requirements

- Visual Studio 2022 (Community+), workload: .NET Desktop Development
- .NET 8 SDK — [download](https://dotnet.microsoft.com/download/dotnet/8.0)
- Inno Setup (installer only) — [download](https://jrsoftware.org/isdl.php)

### Build

```powershell
git clone https://github.com/theantipopau/omencore.git
cd omencore
dotnet restore OmenCore.sln
dotnet build OmenCore.sln --configuration Release

# Run (Administrator required)
cd src\OmenCoreApp\bin\Release\net8.0-windows10.0.19041.0
.\OmenCore.exe
```

### Build Installer

```powershell
pwsh ./build-installer.ps1
# Optional: -Configuration Release -Runtime win-x64 (these are the defaults)
# Outputs: artifacts/OmenCoreSetup-3.6.2.exe and artifacts/OmenCore-3.6.2-win-x64.zip
```

### Tests

```powershell
dotnet test OmenCore.sln
dotnet test OmenCore.sln --collect:"XPlat Code Coverage"
```

### Linux triage bundle (maintainers/reporters)

```bash
./qa/collect-linux-triage.sh [output_dir] [bin_dir]
# Example:
./qa/collect-linux-triage.sh ./triage ./
```

### Release Process

1. Update `VERSION.txt`
2. Add changelog under `docs/CHANGELOG_vX.Y.Z.md`
3. Tag and push: `git tag vX.Y.Z && git push origin main --tags`
4. Include SHA256 hash in GitHub Release notes — required for the in-app auto-updater

---

## 🔧 Troubleshooting

| Symptom | Likely Cause | Fix |
|---------|-------------|-----|
| Fan control has no effect | WMI not supported on this model | Try PawnIO/ec_sys mode; check logs |
| Access denied errors | Not running as Administrator | Right-click ? Run as administrator |
| WinRing0 not detected | Driver blocked by Secure Boot | Switch to PawnIO (Secure Boot compatible) |
| Undervolting not working | MSR locked in BIOS | Check BIOS overclocking settings; verify with Intel XTU |
| Auto-update fails | SHA256 missing from release notes | Download manually from the Releases page |
| High CPU at idle | Charts polling too aggressively | Enable Low Overhead Mode in Dashboard settings |
| Linux: permission denied | Hardware access needs root or udev rules | Install `scripts/99-omencore-hp-wmi.rules`; see [distro hopping](#switching-linux-distros-omen-slim--four-zone-rgb) |
| Linux: keyboard RGB set but no light | `fourzone_brightness` is 0 | `omencore-cli keyboard --brightness 100` then set color |
| Linux: ec_sys not found | Module not in this kernel | Use `hp-wmi` on 2023+ models |

Detailed logs are in `%LOCALAPPDATA%\OmenCore\`. On Linux, use `sudo omencore-cli --report > report.txt` for a diagnostics bundle.

> **AMD undervolting:** Ryzen does not support Intel-style MSR undervolting. Use BIOS Curve Optimizer or Ryzen Master. OmenCore still provides full fan control, monitoring, RGB, and performance modes on AMD systems.

---

## 📜 Version History

| Version | Key Changes |
|---------|------------|
| **v3.6.2** | Stabilization release: runtime source-of-truth hardening, fan/performance confirmation fixes, RGB fallback reliability, Linux diagnostics/package updates, and UI responsiveness cleanup |
| **v3.6.1** | Stabilization release: fan/performance sync, tray/OSD consistency, WMI fan CPU reduction, EC coordination, capability fallback hardening |
| **v3.6.0** | Lightweight runtime behavior, hardware-worker reliability, fan/RGB/hotkey hardening, and release packaging improvements |
| **v3.5.0** | Reliability release: fan/tuning diagnostics clarity, requested-vs-confirmed UI hardening, conflict/recovery safety guardrails, and roadmap split for deferred scope |
| **v3.4.1** | Hotfix for fan/profile regressions, brightness hotkeys, RGB reliability, Linux startup diagnostics, and 15-en0038ur support |
| **v3.4.0** | Correctness and reliability sweep: fan/power fixes, update safety hardening, CI/package alignment, model/support matrix expansion |
| **v3.3.0** | Fan curve stability, sleep recovery, OSD DPI/visual, RGB hardening, AMD power tuning, Lite Mode (74 items) |
| **v3.2.5** | Worker reconnect fix, fan/performance decoupling, 8BB1 model support, Quick Access improvements |
| **v3.2.1** | 23-fix hotfix rollup: telemetry hardening, OSD/premium UI polish, portable log hygiene, CPU temp oscillation guard |
| **v3.2.0** | Dashboard row fix, fan 0% safety, frozen temp watchdog, Avalonia preset save, Linux RGB detection |
| **v3.1.1** | CPU temp regression (17-ck1xxx), fan 0-RPM guard, worker crash on GPU driver install, PE header validation |
| **v3.1.0** | Telemetry state model, sleep/suspend fan hardening (#77), OMEN MAX 16 CPU temp override (#78) |
| **v3.0.2** | Hotfix: PE header validation, WinRing0 hash check |
| **v3.0.0** | Multi-project architecture, out-of-process HardwareWorker, full Avalonia Linux GUI |
| **v2.9.0** | Intel Core Ultra CPU temp fix, EC write reduction, memory optimizer, Afterburner coexistence |
| **v2.8.0** | AMD GPU OC (ADL2), OMEN desktop support, game library, Linux hwmon PWM control |

Older release notes: [docs/](docs/)

---

## 📚 Documentation

- [INSTALL.md](INSTALL.md) — Full installation guide for Windows and Linux
- [docs/LINUX_INSTALL_GUIDE.md](docs/LINUX_INSTALL_GUIDE.md) — Detailed Linux setup
- [docs/ANTIVIRUS_FAQ.md](docs/ANTIVIRUS_FAQ.md) — Antivirus false positive handling
- [docs/DEFENDER_FALSE_POSITIVE.md](docs/DEFENDER_FALSE_POSITIVE.md) — Windows Defender exclusion steps
- [docs/WINRING0_SETUP.md](docs/WINRING0_SETUP.md) — WinRing0 driver setup
- [docs/CHANGELOG_v3.6.2.md](docs/CHANGELOG_v3.6.2.md) — Current stabilization release changelog
- [docs/CHANGELOG-3.6.1.md](docs/CHANGELOG-3.6.1.md) — Prior stabilization release changelog
- [docs/CHANGELOG_v3.4.1.md](docs/CHANGELOG_v3.4.1.md) — Earlier stable release notes

---

## 🤝 Contributing

Contributions welcome! Priority areas:

- [ ] Corsair iCUE / Logitech G HUB SDK implementations (replace stubs)
- [ ] EC register maps for models not yet in the allowlist
- [ ] Testing on OMEN Max 16/17 (2025) with RTX 50-series
- [ ] Testing on OMEN 15-en, 16-n series
- [ ] Localization / translations

---

## ⚠️ Disclaimer

This software is provided "as is" without warranty. Modifying EC registers, undervolting, and mux switching can potentially damage hardware. Always create system restore points before making changes. The developers are not responsible for hardware damage, data loss, or warranty voids. HP does not endorse this project; use at your own risk.

---

## 🔗 Links

- **GitHub:** https://github.com/theantipopau/omencore
- **Releases:** https://github.com/theantipopau/omencore/releases/latest
- **Issues:** https://github.com/theantipopau/omencore/issues
- **Discord:** https://discord.gg/9WhJdabGk8
- **Donate:** https://www.paypal.com/donate/?business=XH8CKYF8T7EBU

---

## 📄 License

MIT License — see [LICENSE](LICENSE) for details.

**Third-party components:**
- [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) — MPL 2.0
- [Hardcodet.NotifyIcon.Wpf](https://github.com/hardcodet/wpf-notifyicon) — CPOL
- WinRing0 driver — OpenLibSys license

---

*Made with care for the HP OMEN community.*
