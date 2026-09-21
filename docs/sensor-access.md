# Sensor access notes

Field notes from making Vitals work, so nobody has to repeat the archaeology. Everything
here was verified on the machine described at the bottom, not taken from a forum post.

**Governing rule:** a sensor that is not really there is reported as missing. No zeros, no
interpolation, no "close enough" substitutes. Every workaround below either produces a real
number or produces nothing.

---

## NVIDIA GPU temperatures

### What each interface actually gives you

| Interface | Core | Memory junction | Hot spot | Notes |
|---|---|---|---|---|
| `nvidia-smi` | yes | `N/A` | no | Also reports T.Limit, see below |
| NVML `nvmlDeviceGetTemperature` | yes | no | no | |
| NVML field values (swept 1–512) | — | no | no | 52 fields answer; none is a usable extra temperature |
| NVAPI `NvAPI_GPU_GetThermalSettings` (public) | yes | no | no | Reports exactly **one** sensor, target `GPU` |
| NVAPI `ThermChannelGetStatus` (private) | yes | yes | **no** | Two channels only — see below |
| HWiNFO ≥ 8.53 | yes | yes | **yes** | Own kernel driver; also per-GDDR7-module temperatures |

### The private NVAPI thermal call

`nvapi_QueryInterface(0x65FE3AAD)` resolves to `NvAPI_GPU_ThermChannelGetStatus`. The
driver itself reveals both the name and the accepted struct versions when you feed it a
wrong one — it prints to stderr:

```
NvAPI_GPU_ThermChannelGetStatus received version: 100a0
While allowed versions are as below
Ver-1:1003c   Ver-2:200a8   Ver-3:334c8
```

Versions encode `size | (version << 16)`: **Ver-1 = 60 bytes, Ver-2 = 168, Ver-3 = 13512**.

Gotchas, in the order they bite:

1. **The function takes two arguments**, `(NvPhysicalGpuHandle, void* struct)`. Calling it
   with three returns `-14` (invalid pointer), because the second argument is read as the
   pointer.
2. **Ver-3 needs the dword at offset 8 set to 1**, otherwise it returns `-121`.
3. Ver-3 accepted with everything else zeroed returns almost nothing: `[4] = 2`,
   `[72] = 0xFF00`, `[76] = 2`. No temperatures.
4. **Ver-1 is the useful one.** Layout: `version`, `mask`, eight unknown dwords, then five
   slots starting at **offset 40**. Values are °C × 256; `0xFF00` means the channel is
   absent.

Mask bits map to slots, and on an RTX 5070 only two are populated:

```
mask 0x2 → slot 1 → 58.8 °C   = core            (matched the library's 58.9)
mask 0x4 → slot 2 → 66.0 °C   = memory junction (matched exactly)
mask 0x1, 0x8, 0x10 → absent
```

So the hotspot is genuinely not behind this interface. LibreHardwareMonitor uses the same
call (its assembly contains both `0x65FE3AAD` and the Ver-3 constant `0x334C8`) and reports
the same two temperatures.

### Thermal headroom (T.Limit) — the number that *is* published

`nvmlDeviceGetTemperatureThreshold` returns the card's absolute limits:

```
threshold 3 (gpu max operating)  85 °C   ← starts reducing clocks
threshold 1 (slowdown)           87 °C
threshold 0 (shutdown)           90 °C
```

`nvidia-smi`'s "GPU Current T.Limit Temp" is simply `85 − current core`. Verified twice:
core 74 °C → T.Limit 11; core 48 °C → T.Limit 37. Vitals shows this as `margem` and also
uses these limits to colour the core row, instead of hardcoded thresholds.

---

## HWiNFO integration

Vitals reads HWiNFO when it is running, for the sensors no public API exposes. Two
channels, tried in this order:

### 1. Shared memory (preferred)

Memory-mapped file `Global\HWiNFO_SENS_SM2`.

**The trap:** the header must be read packed. With default .NET marshalling the 64-bit
`poll_time` field gets 8-byte aligned, four bytes of padding appear before it, and every
subsequent offset shifts — labels come back with garbage prefixes (`L@Temperatura GPU`)
and values read as zero. Use `[StructLayout(LayoutKind.Sequential, Pack = 1)]`.

Header (all offsets from 0):

```
 0  signature "SiWH" (0x53695748)
 4  version                      8  revision
12  poll_time (8 bytes)
20  sensor section offset       24  sensor element size    28  sensor count
32  reading section offset      36  reading element size   40  reading count
```

Observed on this machine: sensor element 392 bytes (22 sensors), reading element 460 bytes
(388 readings). Always use the sizes from the header as the stride; only the offsets
*within* an element are fixed:

```
 12  label, original (128 bytes, ANSI)
140  label, user-renamed (128 bytes)
268  unit (16 bytes)
284  value (double)      292 min      300 max      308 average
```

Labels follow HWiNFO's interface language, so matching must accept several spellings
(`ponto quente`, `hot spot`, `hotspot`).

**Requirements**, all three or nothing appears:

- HWiNFO **8.53 or newer** — 8.30 does not have the hotspot sensor on Blackwell cards, and
  publishes 361 readings instead of 388
- *Shared Memory Support* enabled. In the free version this lasts **12 hours per launch**
- HWiNFO **running elevated** — its driver needs it, and an unelevated launch loses the
  sensors it exists for

**HWiNFO stores its settings in `HWiNFO64.INI` next to the executable, not in the
registry**, and writes the file on exit. The relevant keys:

```
SensorsSM=1               shared memory
OpenSensors=1             start with sensors running
MinimalizeMainWnd=1       no main window
MinimalizeSensors=1       straight to the tray
MinimalizeSensorsClose=1  closing hides instead of quitting
OpenSystemSummary=0       no summary window on startup
ShowWelcomeAndProgress=0  no splash
Autorun=1
```

### 2. Registry (fallback)

`HKCU\Software\HWiNFO64\VSB`, the "HWiNFO Gadget" reporting channel, with entries named
`Sensor0`/`Label0`/`Value0`/`ValueRaw0`. Per-sensor and enabled by hand, so it carries only
what you pick. Used only when the shared memory is unavailable.

---

## CPU temperature

Reading Tctl/Tdie (AMD) or `CPU Package` (Intel) requires model-specific registers, which
requires a kernel driver. `WinRing0`, used by LibreHardwareMonitorLib, is on Microsoft's
vulnerable-driver blocklist (`VulnerableDriverBlocklistEnable = 1`), so on an up-to-date
system it does not load. The symptoms are not an error but plausible-looking garbage:

```
Core (Tctl/Tdie)   0.0 °C
Package power      0.0 W
Core VID           1.55 V   (for every core)
Ryzen SMU: PM table version 0x00000000, layout defined: False
```

Vitals treats a temperature of exactly zero as "no reading" and hides the row. On machines
where the driver does load, the row appears by itself. Disabling the blocklist is not a
supported workaround.

---

## Disks

- **SMART needs elevation.** Unelevated, the storage list comes back completely empty.
- **Space does not.** It comes from `DriveInfo`, so disks still appear with correct space
  and `n/d` temperature when running as a normal user.
- **Volume → physical disk mapping** goes through WMI. The query must be
  `SELECT * FROM Win32_DiskDrive`: a projection like `SELECT Model` returns a partial
  instance without its key, and `GetRelated` then fails silently, leaving you with model
  names instead of drive letters.
- Size is not a usable key for matching — on this machine the NVMe and one hard drive both
  report exactly 1000.2 GB.
- Volumes under 1 GB are skipped: empty card readers show up as 0/0 GB disks otherwise.
- Temperature belongs to the physical disk, so it is shown only on the first volume of
  each; otherwise `H:`, `I:` and `J:` repeat the same number three times.
- NVMe drives publish their own thresholds (`Warning Temperature` 89 °C, `Critical
  Temperature` 93 °C here). Vitals uses them, and falls back to a conservative 55/70 °C for
  drives that don't.
- Reading SMART is expensive — about 1 % of a core per second on this machine — so disks
  are re-read every 10 seconds regardless of the refresh interval.

---

## Elevation and autostart

The `HKCU\...\Run` key starts programs **unelevated**. For this project that silently
removes two features: disk temperatures, and HWiNFO's driver-sourced sensors. A scheduled
task at logon with `-RunLevel Highest` is what actually works:

```powershell
$opcoes = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
$gatilho = New-ScheduledTaskTrigger -AtLogOn -User "$env:USERDOMAIN\$env:USERNAME"
$accao = New-ScheduledTaskAction -Execute "C:\path\to\Vitals.exe"
Register-ScheduledTask -TaskName "Vitals" -Action $accao -Trigger $gatilho `
    -Settings $opcoes -RunLevel Highest
```

Vitals recognises both routes, so the *start with Windows* menu item stays honest.

---

## Reference machine

Everything above was measured on a single Windows 11 desktop: a Blackwell-generation
GeForce with GDDR7, a recent Ryzen, one NVMe drive alongside several SATA drives, and more
than one display. That is the only configuration any of it was verified against.

Results on other hardware — especially AMD and Intel GPUs — are welcome as issues; the
output of `vitals --sensores` and `vitals --hwinfo` is what makes them actionable.
