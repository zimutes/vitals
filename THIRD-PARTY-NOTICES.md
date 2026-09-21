# Third-party notices

Vitals is MIT licensed (see `LICENSE`), but it ships and links against the following
third-party components. Their licenses apply to their own code.

## LibreHardwareMonitorLib

- Source: https://github.com/LibreHardwareMonitor/LibreHardwareMonitor
- License: **Mozilla Public License 2.0 (MPL-2.0)**
- Used for: reading GPU sensors (including GDDR memory junction temperature), CPU load,
  system memory and SMART data from drives.

MPL-2.0 is a file-level copyleft license. Redistributing Vitals in binary form is allowed,
including as part of a closed-source product, **provided that**:

1. this notice and a copy of the MPL-2.0 text are made available to recipients, and
2. any modifications made to LibreHardwareMonitorLib's own source files are published
   under MPL-2.0.

Vitals does not modify LibreHardwareMonitorLib: it consumes the published NuGet package
unchanged. The full license text is available at https://mozilla.org/MPL/2.0/.

Note: LibreHardwareMonitorLib may load a kernel driver (`WinRing0`) to read CPU MSRs.
Vitals never requires it — see the FAQ in the README about CPU temperature.

## System.Management

- Source: https://github.com/dotnet/runtime
- License: **MIT**
- Used for: mapping volume letters (`C:`) to physical drives through WMI.

## NVIDIA NVML (`nvml.dll`)

Not redistributed. Vitals loads the copy installed by the NVIDIA display driver, at
runtime, and only calls read-only query functions. If no NVIDIA driver is present, the
library simply fails to load and the related readings are reported as unavailable.
