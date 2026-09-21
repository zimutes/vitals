# Vitals

A small Windows monitoring widget, plus a terminal command that reads the same sensors.
The terminal command runs on Linux too.

Shows GPU temperatures — including the **GDDR memory junction temperature** and the
**thermal headroom** to throttling — CPU load, memory, and per-volume disk temperature and
space. It is deliberately quiet: you pick the lines you want, and a sensor that isn't
really there shows `n/d` instead of a convincing zero.

*[Leia-me em português](LEIAME.md)*

*Digging into sensors? [docs/sensor-access.md](docs/sensor-access.md) has the field notes:
which API gives what, the traps, and how each number is obtained.*

---

> ### Read this first
>
> **Built for one machine, shared as-is.** Vitals was written to read the sensors of the
> author's own PC, against that hardware and no other. There is **no guarantee it works on
> your system** — sensors vary enormously between boards, vendors and drivers, and a reading
> this code expects in one place may live somewhere else entirely on yours, or not be
> exposed at all. Expect rows to be missing. Nothing here is tested broadly. No warranty of
> any kind, express or implied — see [LICENSE](LICENSE).
>
> **The GPU hot spot needs HWiNFO.** NVIDIA does not publish that sensor through any public
> interface, so Vitals reads it from HWiNFO's shared memory. The `hotspot` row and the
> per-module GDDR temperatures only appear with **HWiNFO 8.53 or newer actually running, and
> *Shared Memory Support* enabled in its settings** — the details are in
> [the FAQ](#gpu-hot-spot-not-from-nvidia-but-hwinfo-has-it). Without it nothing breaks;
> those rows simply disappear.
>
> **This README was written by AI** (Claude), from the source code, and reviewed by the
> author before publishing.

---

## Two programs, one reader

| | |
|---|---|
| `Vitals.exe` | Borderless widget: always on top, drag to move, resize, collapses to the system tray, starts with Windows. |
| `vitals.exe` | Terminal command: one-shot, live `--watch`, or `--json` for other tools. |

```
  Vitals 1.0                    by zimutes

  GPU
    core               51 °C  ━━━━━━━───────
    junção             58 °C  ━━━━━━━╸──────
    margem             34 °C  ━━━━━━━━━━━╸──
    uso                  3 %  ╸─────────────
    vram         1,5/16,0 GB  ━╸────────────

  CPU
    uso                 15 %  ━━────────────

  MEMÓRIA
    ram         10,4/32,0 GB  ━━━━━━━───────

  DISCOS
    C: temp            46 °C  ━━━━━━━───────
    C: espaço     412/999 GB  ━━━━━━━━━━━━╸─

  ligado há 1d 14h24              13:08:11
```

---

## What it reads

| Reading | Source | Needs admin |
|---|---|---|
| GPU core temperature | NVML / vendor API | no |
| **GPU memory junction temperature** | vendor API | no |
| **Thermal headroom (T.Limit)** | NVML thresholds | no |
| **GPU hot spot** | HWiNFO shared memory | no, but HWiNFO must run |
| Per-module GDDR7 temperatures | HWiNFO shared memory | no, but HWiNFO must run |
| GPU load, VRAM, power, fan | vendor API | no |
| CPU load | Windows counters | no |
| CPU temperature | MSR (kernel driver) | yes, *and* an unblocked driver — [see FAQ](#why-is-there-no-cpu-temperature) |
| System memory | Windows | no |
| Disk space, per volume | Windows | no |
| Disk temperature and activity | SMART | **yes** |
| Laptop battery | Windows | no |

Anything unavailable is reported as `n/d` and its row disappears from the widget. Vitals
never substitutes a plausible-looking number for a missing sensor.

## Hardware support

| Hardware | Status | Notes |
|---|---|---|
| NVIDIA GeForce (Ada, Blackwell) | **Tested** | Core, memory junction, headroom, load, VRAM, power, fan |
| NVIDIA GeForce (older) | Expected to work | Junction temperature depends on the board exposing it |
| AMD Radeon | **Untested** | Falls back to `GPU Hot Spot` for the junction row; the headroom row disappears (NVIDIA-only API) |
| Intel Arc / integrated | **Untested** | Core temperature and load only, most likely |
| AMD Ryzen | **Tested** | Load yes; temperature blocked on the test machine, see FAQ |
| Intel Core | **Untested** | Reads `CPU Package` when the driver is available |
| NVMe drives | **Tested** | Uses the drive's own warning/critical thresholds from SMART |
| SATA SSD / HDD | **Tested** | Conservative 55 °C / 70 °C thresholds |
| Laptops | **Untested** | Battery row appears automatically; the discrete GPU is preferred over the integrated one |
| Multiple monitors | **Tested** | The widget stays on the display you left it on |

If you run it on hardware that isn't listed, `vitals --sensores` prints every sensor the
library can see, which is exactly what is needed to add support. Issues with that output
attached are very welcome.

---

## Requirements

**Windows** — 10 or 11, x64, for the widget and the terminal command.

- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) — or build
  self-contained (see below) and carry no dependency at all
- HWiNFO 8.53+ running, *Shared Memory Support* on, if you want the hot spot row

**Linux** — x64 or arm64, for the terminal command only. The published build is
self-contained: no .NET to install, no root. See [Linux](#linux) below.

## Running

Clone the repository and run the installer from a PowerShell prompt, **as administrator**:

```powershell
.\install.ps1
```

It builds both programs, installs them into `%LOCALAPPDATA%\Programs\Vitals`, adds a Start
menu shortcut, puts `vitals` on your `PATH`, and schedules the widget to start with
Windows. Administrator matters: the logon task is then created elevated, which is what disk
temperatures need. Without it the installer still works, and says what you lose.

`.\uninstall.ps1` removes all of it — add `-Tudo` to drop your saved preferences as well.

Once installed, the widget is in the Start menu as **Vitals**, and in a new terminal:

```
vitals                     one reading
vitals --watch             live, redraws in place (↑↓ or j/k to scroll, q to quit)
vitals --watch --intervalo 2
vitals --json              machine-readable
vitals --sensores          every sensor detected, with its real name
vitals --hwinfo            what HWiNFO is publishing, if it is running
vitals --help
```

### The widget

- Drag with the left button; the position is remembered, per display.
- Drag the **grip in the bottom-right corner** to resize.
- **Closing hides it next to the clock.** Double-click the tray icon to bring it back; only
  *Sair* really quits.
- Right-click for the menu:
  - **Mostrar** — turn each line on or off. Hovering a line explains what it is, and
    *"O que é cada linha..."* opens a window with every explanation, including hidden rows.
  - **Intervalo** — 0.5s, 1s, 2s or 5s.
  - Always on top, start with Windows, copy reading, **about**, fit to content, reset,
    hide, quit.
- The footer shows system uptime, the clock, the refresh interval and a dot that pulses on
  every reading.

### How it arranges itself

Left alone it sizes itself: one column, and two side by side once the content passes about
430 px tall, never splitting a group across columns.

Resize it by hand and it reacts in **both** directions:

- **Width** decides the number of columns, up to four. A 740 × 62 strip shows twelve values
  across four columns.
- **Height** decides how many rows fit. What doesn't fit is **dropped, not scrolled** —
  small means "I'm playing, show me what matters", so rows are ordered by importance
  (core, hotspot, delta, headroom, junction, loads, memory, disks) and cut at the bottom.
  The tooltip says how many are hidden.
- **Small or narrow switches to compact**: no header, no footer, no section titles, minimal
  spacing. Below 190 px wide the bars go too, so labels and numbers stay readable.

The *Densidade* menu forces compact or roomy if you would rather decide yourself.

### Disk temperatures need administrator

Reading SMART requires elevation. Disk **space** does not, so unelevated the disks still
appear with their space and `n/d` for temperature.

The plain `HKCU\...\Run` key starts programs **unelevated**, so `install.ps1` creates a
logon scheduled task with `-RunLevel Highest` instead, and removes the Run entry so nothing
starts twice. The widget recognises both routes, so its *start with Windows* menu item
tells the truth either way.

The same applies to HWiNFO if you want the hot spot: its driver needs elevation too, so it
belongs in an elevated logon task rather than in the Run key.

---

## Linux

The terminal command runs on Linux. The widget does not — it is WPF, which is Windows-only.

```sh
curl -fsSL https://raw.githubusercontent.com/zimutes/vitals/main/install.sh | sh
```

That downloads the self-contained binary for your architecture, checks it against the
published `SHA256SUMS`, installs it under `~/.local/share/vitals` and links it into
`~/.local/bin`. No root and no .NET needed. `--sistema` installs into `/usr/local` for
everyone instead, `--versao v1.1` pins a version, and `--desinstalar` removes it again.

To build it yourself instead, see [Building](#building).

### What changes

There is no LibreHardwareMonitor here and no driver to load: the kernel publishes
everything as text files, and `FonteLinux` reads them directly.

| Reading | Where it comes from | Notes |
|---|---|---|
| CPU temperature | `k10temp`, `zenpower`, `coretemp` | **Works** — unlike Windows, where the driver is usually blocked |
| CPU load and name | `/proc/stat`, `/proc/cpuinfo` | |
| Memory | `/proc/meminfo` | |
| AMD GPU: core, junction, load, VRAM | `amdgpu` hwmon, `/sys/class/drm` | The junction temperature comes free — the kernel exposes it as `temp2_input` |
| NVIDIA GPU: core, thermal headroom | `nvidia` hwmon, NVML | **Load and VRAM stay blank** — not read on Linux yet |
| GPU power and fan | hwmon | |
| Disk temperature | `nvme`, `drivetemp` hwmon | SATA drives need `modprobe drivetemp`; the mapping to mount points is approximate |
| Battery | `/sys/class/power_supply/BAT*` | |
| Hot spot, per-module GDDR | — | Not available: those come from HWiNFO, which is Windows-only |

Motherboard chips — case fans, voltages — need their kernel module loaded first:

```sh
sudo dnf install lm_sensors      # Debian/Ubuntu: sudo apt install lm-sensors
sudo sensors-detect --auto
```

If a row you expected is missing, `vitals --sensores` lists every chip and sensor found
under `/sys/class/hwmon`. That output is what an issue should carry.

The same caveat as everywhere else applies, only more so: the Linux path was tried on a
couple of machines, not many.

---

## FAQ

### GPU Hot Spot: not from NVIDIA, but HWiNFO has it

NVIDIA does not expose the hotspot through any public interface. On an RTX 5070 this was
checked several ways — `nvidia-smi`, LibreHardwareMonitor, a sweep of NVML field values,
and the private NVAPI `ThermChannelGetStatus`, mapped bit by bit, which returns exactly two
channels: core and memory junction.

**HWiNFO reads it anyway**, with its own signed kernel driver, straight off the board — and
that is also how it gets a temperature for each individual GDDR7 module. Vitals therefore
reads HWiNFO's shared memory (`HWiNFO_SENS_SM2`) when it is available, and shows a
`hotspot` row plus a `delta` of hotspot minus core. Requirements:

- **HWiNFO 8.53 or newer** — older builds do not have the sensor on Blackwell cards
- running, with *Shared Memory Support* enabled (free version: 12 hours per launch)

Without it, nothing breaks: the row disappears and `delta` falls back to memory junction
minus core. The `--hwinfo` command shows exactly what is being published.

The other number the driver does publish is **T.Limit**: how many degrees are left before
the card throttles.

Vitals computes that headroom from the card's own limits:

```
gpu max operating  85 °C   ← starts reducing clocks
slowdown           87 °C
shutdown           90 °C
```

Verified twice against `nvidia-smi`: at 74 °C the reported T.Limit was 11 (85−74); at
48 °C it was 37 (85−48). Those same limits drive the colour of the core row, instead of
hardcoded guesses.

The **memory junction** temperature — the one that actually matters for GDDR6/7 — does
exist and is shown.

### Why is there no CPU temperature?

Reading Tctl/Tdie on a Ryzen (or `CPU Package` on Intel) needs access to model-specific
registers, which requires a kernel driver. The `WinRing0` driver used by the sensor library
is on Microsoft's vulnerable-driver blocklist, so on many up-to-date systems it refuses to
load and every CPU temperature reads `0.0`.

Vitals treats a zero temperature as "no reading" and hides the row rather than showing a
fake value. It will appear by itself on machines where the driver does load. Disabling the
blocklist is not a recommended workaround.

### Windows says the app is unrecognised

The executable is unsigned. *More info → Run anyway*, or build it yourself.

### How much does it cost to run?

About 6–7 % of one core at a one-second interval, most of it inside the sensor library's
GPU enumeration, and ~190 MB of working set. A 2-second interval roughly halves the CPU
cost. Disks are re-read every 10 seconds regardless, because SMART queries are expensive
and drive temperatures move slowly.

---

## Building

```
dotnet build
dotnet publish Sensores.Widget -c Release -r win-x64 -p:SelfContained=false -p:PublishSingleFile=true -o dist/widget
dotnet publish Sensores.Cli    -c Release -r win-x64 -p:SelfContained=false -p:PublishSingleFile=true -o dist/cli
```

On Linux, only the terminal command builds — and self-contained, so it carries its own
runtime:

```sh
dotnet publish Sensores.Cli -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -o dist/linux
```

Each Windows binary is ~3.5 MB and needs the .NET 8 Desktop Runtime. Swap
`SelfContained=false` for `true` to get a standalone ~150 MB executable that runs anywhere;
that is what the published releases ship, built by
[.github/workflows/release.yml](.github/workflows/release.yml) on every `v*` tag.

```
Sensores.Core/      sensor reading, shared        (Leitor, Leitura, Disco, Bateria, LimitesNvidia)
Sensores.Widget/    WPF widget                    → Vitals.exe
Sensores.Cli/       terminal command              → vitals.exe
```

The name and byline live in `Sensores.Core/Marca.cs`, in one place.

## Roadmap

- English user interface (currently Portuguese only)
- NVIDIA GPU load and VRAM on Linux (needs the NVML calls)
- Verify AMD and Intel GPUs on real hardware
- Optional CPU temperature through HWiNFO's shared memory, for blocked-driver systems
- Lower memory footprint
- Signed releases

## License

MIT — see [LICENSE](LICENSE). Third-party components, including the MPL-2.0 licensed
LibreHardwareMonitorLib, are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
