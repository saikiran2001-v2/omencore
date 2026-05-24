using System.Runtime.InteropServices;
using OmenCore.Linux.Hardware;

namespace OmenCore.Avalonia.Services;

/// <summary>
/// Linux implementation of hardware service using sysfs and ACPI interfaces.
/// </summary>
public class LinuxHardwareService : IHardwareService, IDisposable
{
    private readonly System.Timers.Timer _pollingTimer;
    private readonly SemaphoreSlim _ioLock = new(1, 1);
    private HardwareStatus _lastStatus = new();
    private SystemCapabilities? _capabilities;
    private bool _disposed;
    private bool _pollingInProgress;
    private PerformanceMode? _lastFanFallbackMode;
    private OmenCore.Linux.Hardware.FanProfile _activeFanProfile = OmenCore.Linux.Hardware.FanProfile.Auto;

    // HP OMEN specific paths
    private const string HP_WMI_PATH = LinuxSysfsPathMap.HpWmiRoot;
    private const string HWMON_BASE = "/sys/class/hwmon";
    private const string POWER_SUPPLY = "/sys/class/power_supply";
    private const string BACKLIGHT_PATH = LinuxSysfsPathMap.KeyboardBacklightPath;
    private const string HP_WMI_FAN1_OUTPUT = "/sys/devices/platform/hp-wmi/fan1_output";
    private const string HP_WMI_FAN2_OUTPUT = "/sys/devices/platform/hp-wmi/fan2_output";
    private const string HP_WMI_FAN_ALWAYS_ON = "/sys/devices/platform/hp-wmi/fan_always_on";
    private const string RaplPl1Path = "/sys/class/powercap/intel-rapl/intel-rapl:0/constraint_0_power_limit_uw";
    private static readonly string[] PowerProfilesCtlCandidates =
    {
        "/usr/bin/powerprofilesctl",
        "/bin/powerprofilesctl"
    };
    
    private string? _resolvedThermalPath; // Cached resolved path

    // RAPL power tracking — store last reading to compute delta without sleeping
    private long _raplLastEnergyUj;
    private DateTime _raplLastReadTime = DateTime.MinValue;
    
    public event EventHandler<HardwareStatus>? StatusChanged;

    public LinuxHardwareService()
    {
        _pollingTimer = new System.Timers.Timer(2500)
        {
            AutoReset = false
        };
        _pollingTimer.Elapsed += async (s, e) =>
        {
            await PollHardwareAsync();
            if (!_disposed)
            {
                _pollingTimer.Start();
            }
        };
        _pollingTimer.Start();

        // Best-effort re-apply of the selected profile at startup.
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            _ = Task.Run(() => ReapplyFanProfile(_activeFanProfile));
    }

    private static void ReapplyFanProfile(OmenCore.Linux.Hardware.FanProfile profile)
    {
        try
        {
            var ec = new OmenCore.Linux.Hardware.LinuxEcController();
            ec.SetFanProfile(profile);
        }
        catch { }
    }

    private async Task PollHardwareAsync()
    {
        if (_pollingInProgress || _disposed)
        {
            return;
        }

        _pollingInProgress = true;
        try
        {
            var status = await GetStatusAsync();
            if (HasStatusChanged(_lastStatus, status))
            {
                _lastStatus = status;
                StatusChanged?.Invoke(this, status);
            }
        }
        catch
        {
            // Ignore polling errors
        }
        finally
        {
            _pollingInProgress = false;
        }
    }

    private static bool HasStatusChanged(HardwareStatus old, HardwareStatus current)
    {
        return Math.Abs(old.CpuTemperature - current.CpuTemperature) > 1 ||
               Math.Abs(old.GpuTemperature - current.GpuTemperature) > 1 ||
               old.CpuFanRpm != current.CpuFanRpm ||
               old.GpuFanRpm != current.GpuFanRpm;
    }

    public async Task<HardwareStatus> GetStatusAsync()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Return mock data for testing on Windows
            return GetMockStatus();
        }

        return await ExecuteWithIoLockAsync(async () =>
        {
            var status = new HardwareStatus();

            // Read CPU temperature from hwmon
            status.CpuTemperature = await ReadTemperatureAsync("coretemp") / 1000.0;

            // Read GPU temperature (NVIDIA or AMD)
            status.GpuTemperature = await ReadGpuTemperatureAsync() / 1000.0;

            // Read fan speeds
            status.CpuFanRpm = await ReadFanRpmAsync("cpu");
            status.GpuFanRpm = await ReadFanRpmAsync("gpu");

            // Read CPU/memory usage from /proc
            status.CpuUsage = await ReadCpuUsageAsync();
            var (memPercentage, memUsedGb, memTotalGb) = await ReadMemoryUsageAsync();
            status.MemoryUsage = memPercentage;
            status.MemoryUsedGb = memUsedGb;
            status.MemoryTotalGb = memTotalGb;

            // Read battery status
            (status.BatteryPercentage, status.IsOnBattery) = await ReadBatteryStatusAsync();

            // Read GPU utilization
            status.GpuUsage = await ReadGpuUsageAsync();

            // Read power consumption (RAPL on Intel, power_now fallback)
            status.PowerConsumption = await ReadPowerConsumptionAsync();

            return status;
        });
    }

    private static HardwareStatus GetMockStatus()
    {
        var rng = new Random();
        var cpuFanPercent = 30 + rng.Next(0, 40);
        var gpuFanPercent = 25 + rng.Next(0, 45);
        var memUsed = 8.0 + rng.NextDouble() * 12;
        var memTotal = 32.0;
        return new HardwareStatus
        {
            CpuTemperature = 45 + rng.Next(0, 20),
            GpuTemperature = 40 + rng.Next(0, 25),
            CpuFanRpm = 2000 + rng.Next(0, 1000),
            GpuFanRpm = 2500 + rng.Next(0, 1500),
            CpuFanPercent = cpuFanPercent,
            GpuFanPercent = gpuFanPercent,
            CpuUsage = 10 + rng.Next(0, 50),
            GpuUsage = 5 + rng.Next(0, 60),
            MemoryUsage = (memUsed / memTotal) * 100,
            MemoryUsedGb = memUsed,
            MemoryTotalGb = memTotal,
            PowerConsumption = 25 + rng.Next(0, 50),
            BatteryPercentage = 75 + rng.Next(-20, 25),
            IsOnBattery = false,
            IsThrottling = false,
            ThrottlingReason = null
        };
    }

    public async Task<SystemCapabilities> GetCapabilitiesAsync()
    {
        if (_capabilities != null)
            return _capabilities;

        var pending = new SystemCapabilities();
        try
        {
            await PopulateCapabilitiesAsync(pending);
            _capabilities = pending;
        }
        catch
        {
            // Don't cache a half-initialised object — let the next call retry.
        }
        return _capabilities ?? pending;
    }

    private async Task PopulateCapabilitiesAsync(SystemCapabilities cap)
    {
        var _capabilities = cap; // local alias so the body below compiles unchanged

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Mock capabilities for testing on non-Linux
            cap.HasKeyboardBacklight = true;
            cap.HasFourZoneRgb = true;
            cap.HasDiscreteGpu = true;
            cap.HasGpuMuxSwitch = true;
            cap.SupportsFanControl = true;
            cap.SupportsFanSurface = true;
            cap.SupportsPerformanceProfiles = true;
            cap.SupportsKeyboardBrightness = true;
            cap.SupportsKeyboardAnimation = true;
            cap.FanControlCapabilityClass = "full-control";
            cap.FanControlCapabilityReason = "Mock environment reports full control.";
            cap.ModelName = "HP OMEN 16 (Mock)";
            cap.CpuName = "AMD Ryzen 9 7945HX";
            cap.GpuName = "NVIDIA GeForce RTX 4070";
            cap.HasNvidiaSettings = true;
            return;
        }

        // Check for HP OMEN thermal/profile interfaces using centralized path normalization.
        _resolvedThermalPath = ResolveThermalProfilePath();
        bool hasDirectFanControl = File.Exists(HP_WMI_FAN1_OUTPUT) ||
                       File.Exists(HP_WMI_FAN2_OUTPUT) ||
                       ResolveHwmonFanTargetPath(1) != null ||
                       ResolveHwmonFanTargetPath(2) != null;
        var hasHpWmiThermalProfilePath = LinuxSysfsPathMap.AnyPathExists(LinuxSysfsPathMap.HpWmiThermalProfilePaths);
        var hasHpWmiPlatformProfilePath = LinuxSysfsPathMap.AnyPathExists(LinuxSysfsPathMap.PlatformProfilePaths);

        var capabilityAssessment = LinuxCapabilityClassifier.Assess(
            CheckRootAccess(),
            File.Exists(LinuxSysfsPathMap.EcIoPath),
            Directory.Exists(HP_WMI_PATH),
            hasHpWmiThermalProfilePath,
            hasHpWmiPlatformProfilePath,
            File.Exists(LinuxSysfsPathMap.AcpiPlatformProfilePath),
            File.Exists(HP_WMI_FAN1_OUTPUT),
            File.Exists(HP_WMI_FAN2_OUTPUT),
            ResolveHwmonFanTargetPath(1) != null,
            ResolveHwmonFanTargetPath(2) != null,
            ResolveHwmonPwmEnablePath(1) != null || ResolveHwmonPwmEnablePath(2) != null,
            Directory.Exists(HWMON_BASE) || Directory.Exists(HP_WMI_PATH),
            IsUnsafeEcModel(),
            await ReadDmiStringAsync("product_name"),
            await ReadDmiStringAsync("board_name"));
        _capabilities.SupportsFanControl = capabilityAssessment.SupportsManualFanControl;
        _capabilities.SupportsFanSurface = capabilityAssessment.SupportsManualFanControl || capabilityAssessment.SupportsProfileControl || capabilityAssessment.SupportsTelemetry;
        _capabilities.SupportsPerformanceProfiles = capabilityAssessment.SupportsProfileControl || ResolvePowerProfilesCtlPath() != null;
        _capabilities.PerformanceProfileReason = _capabilities.SupportsPerformanceProfiles
            ? string.Empty
            : "No writable platform_profile/thermal_profile interface was detected.";
        _capabilities.FanControlCapabilityClass = capabilityAssessment.CapabilityKey;
        _capabilities.FanControlCapabilityReason = capabilityAssessment.Reason;
        
        // Keyboard: check both legacy hp::kbd_backlight and the fourzone_color interface
        var kbCtrl = new OmenCore.Linux.Hardware.LinuxKeyboardController();
        _capabilities.HasKeyboardBacklight = kbCtrl.IsAvailable;
        _capabilities.SupportsKeyboardBrightness = kbCtrl.SupportsBrightnessControl;
        _capabilities.KeyboardBrightnessReason = _capabilities.SupportsKeyboardBrightness
            ? string.Empty
            : "Keyboard brightness sysfs path was not detected on this kernel/board.";
        _capabilities.SupportsKeyboardAnimation = kbCtrl.HasFourZoneAnimationControl;
        _capabilities.KeyboardAnimationReason = _capabilities.SupportsKeyboardAnimation
            ? string.Empty
            : "fourzone_animation sysfs path was not detected on this kernel/board.";

        // Detect RGB capabilities — include fourzone_color for newer OMEN models
        _capabilities.HasFourZoneRgb = kbCtrl.HasFourZoneControl || DetectFourZoneRgbSupport();
        _capabilities.HasPerKeyRgb = kbCtrl.IsPerKeyRgb || DetectPerKeyRgbSupport();

        // Check for discrete GPU
        _capabilities.HasDiscreteGpu = await HasDiscreteGpuAsync();

        // Read model name from DMI
        _capabilities.ModelName = await ReadDmiStringAsync("product_name") ?? "Unknown HP OMEN";

        // Read CPU name from /proc/cpuinfo
        _capabilities.CpuName = await ReadCpuNameAsync();

        // Read GPU name
        _capabilities.GpuName = await ReadGpuNameAsync();

        // Longevity: CPU RAPL power limit
        _capabilities.SupportsCpuPowerLimit = File.Exists(RaplPl1Path);

        // Longevity: battery charge limit (sysfs)
        _capabilities.SupportsBatteryChargeLimit = await FindChargeThresholdPathAsync() != null;

        // Detect nvidia-settings for the GPU settings launcher button
        _capabilities.HasNvidiaSettings = ResolveNvidiaSettingsPath() != null;
    }

    private static bool DetectFourZoneRgbSupport()
    {
        if (File.Exists("/sys/class/leds/hp::kbd_backlight/color") ||
            File.Exists("/sys/class/leds/hp::kbd_backlight/multi_intensity"))
        {
            return true;
        }

        try
        {
            if (!Directory.Exists("/sys/class/leds"))
            {
                return false;
            }

            foreach (var ledPath in Directory.EnumerateDirectories("/sys/class/leds", "*", SearchOption.TopDirectoryOnly))
            {
                var ledName = Path.GetFileName(ledPath);
                if (string.IsNullOrWhiteSpace(ledName))
                {
                    continue;
                }

                if (ledName.Contains("zone", StringComparison.OrdinalIgnoreCase) ||
                    ledName.Contains("multicolor", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (File.Exists(Path.Combine(ledPath, "multi_intensity")) ||
                    File.Exists(Path.Combine(ledPath, "multi_index")))
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static bool DetectPerKeyRgbSupport()
    {
        try
        {
            if (!Directory.Exists("/sys/class/leds"))
            {
                return false;
            }

            return Directory.EnumerateDirectories("/sys/class/leds", "hp::*key*", SearchOption.TopDirectoryOnly).Any()
                || Directory.EnumerateDirectories("/sys/class/leds", "*kbd*perkey*", SearchOption.TopDirectoryOnly).Any();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Find the first existing thermal profile sysfs path.
    /// </summary>
    private static string? ResolveThermalProfilePath()
    {
        return LinuxSysfsPathMap.ResolveThermalProfilePath();
    }

    private static string? ResolveHwmonPwmEnablePath(int index)
    {
        return LinuxSysfsPathMap.ResolveHpWmiPwmEnablePath(index);
    }

    private static bool CheckRootAccess()
    {
        try
        {
            return geteuid() == 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsUnsafeEcModel()
    {
        try
        {
            var modelName = ReadDmiString("product_name");
            var boardName = ReadDmiString("board_name");
            if (modelName?.Contains("transcend 14", StringComparison.OrdinalIgnoreCase) == true)
            {
                return true;
            }

            if (boardName != null &&
                (string.Equals(boardName, "8C58", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(boardName, "8E41", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static string? ReadDmiString(string name)
    {
        var paths = new[]
        {
            $"/sys/devices/virtual/dmi/id/{name}",
            $"/sys/class/dmi/id/{name}"
        };

        foreach (var path in paths)
        {
            try
            {
                if (File.Exists(path))
                {
                    return File.ReadAllText(path).Trim();
                }
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    [DllImport("libc")]
    private static extern uint geteuid();

    /// <summary>
    /// Convert kernel platform_profile string to OmenCore PerformanceMode.
    /// The kernel interface uses: "low-power", "cool", "quiet", "balanced", "balanced-performance", "performance".
    /// </summary>
    private static PerformanceMode ParsePlatformProfile(string profile)
    {
        return profile.Trim().ToLower() switch
        {
            "low-power" or "cool" or "quiet" => PerformanceMode.Cool,
            "balanced" => PerformanceMode.Balanced,
            "balanced-performance" => PerformanceMode.Performance,
            "performance" => PerformanceMode.Performance,
            "default" => PerformanceMode.Default,
            _ => PerformanceMode.Default
        };
    }

    /// <summary>
    /// Convert OmenCore PerformanceMode to kernel platform_profile string.
    /// Uses the standard kernel values from /sys/firmware/acpi/platform_profile_choices.
    /// </summary>
    private async Task<string> GetKernelProfileStringAsync(PerformanceMode mode)
    {
        // Read available choices from the kernel to use exact supported values
        var choices = await ReadAvailableProfileChoicesAsync();
        
        return mode switch
        {
            PerformanceMode.Cool => 
                choices.Contains("low-power") ? "low-power" :
                choices.Contains("quiet") ? "quiet" :
                choices.Contains("cool") ? "cool" : "low-power",
            PerformanceMode.Default => "balanced",
            PerformanceMode.Balanced => "balanced",
            PerformanceMode.Performance =>
                choices.Contains("performance") ? "performance" :
                choices.Contains("balanced-performance") ? "balanced-performance" :
                "performance",
            _ => "balanced"
        };
    }

    /// <summary>
    /// Read available profile choices from the kernel.
    /// </summary>
    private async Task<HashSet<string>> ReadAvailableProfileChoicesAsync()
    {
        var choices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var choicesPath in LinuxSysfsPathMap.ThermalProfileChoicePaths)
            {
                if (!File.Exists(choicesPath))
                {
                    continue;
                }

                var content = await File.ReadAllTextAsync(choicesPath);
                foreach (var choice in content.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    choices.Add(choice.Trim());

                if (choices.Count > 0)
                {
                    break;
                }
            }
        }
        catch { }
        
        // Fallback defaults if we couldn't read choices
        if (choices.Count == 0)
        {
            choices.Add("low-power");
            choices.Add("balanced");
            choices.Add("performance");
        }
        
        return choices;
    }

    public async Task<PerformanceMode> GetPerformanceModeAsync()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return PerformanceMode.Balanced;

        try
        {
            var thermalPath = _resolvedThermalPath ?? ResolveThermalProfilePath();
            if (thermalPath != null)
            {
                var profile = await File.ReadAllTextAsync(thermalPath);
                return ParsePlatformProfile(profile);
            }
        }
        catch { }

        var fallbackMode = await TryGetPowerProfilesCtlModeAsync();
        if (fallbackMode.HasValue)
        {
            return fallbackMode.Value;
        }

        return PerformanceMode.Balanced;
    }

    public async Task SetPerformanceModeAsync(PerformanceMode mode)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return;

        await ExecuteWithIoLockAsync(() => SetPerformanceModeCoreAsync(mode));
    }

    private async Task SetPerformanceModeCoreAsync(PerformanceMode mode)
    {
        var thermalPath = _resolvedThermalPath ?? ResolveThermalProfilePath();
        if (thermalPath == null)
        {
            if (await TrySetPerformanceModeViaPowerProfilesCtlAsync(mode))
            {
                return;
            }

            var boardId = await ReadFirstExistingTextAsync(new[]
            {
                "/sys/class/dmi/id/board_name",
                "/sys/devices/virtual/dmi/id/board_name"
            }) ?? "unknown";

            throw new InvalidOperationException(
                $"No thermal profile interface found (board {boardId}). If hp-wmi is loaded but platform_profile/thermal_profile are missing, run 'omencore-cli diagnose --report' to capture model-specific sysfs capabilities.");
        }

        var profile = await GetKernelProfileStringAsync(mode);

        // Strategy 1: Direct sysfs write (works when running as root)
        try
        {
            // Use File.Open with FileMode.Open to avoid Create semantics that sysfs rejects
            await using var fs = new FileStream(thermalPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
            var bytes = System.Text.Encoding.UTF8.GetBytes(profile);
            await fs.WriteAsync(bytes);
            return; // Success
        }
        catch (UnauthorizedAccessException)
        {
            // Not running with permission to write this sysfs control.
        }
        catch (IOException)
        {
            // sysfs write failed.
        }

        if (await TrySetPerformanceModeViaPowerProfilesCtlAsync(mode))
        {
            return;
        }

        throw new InvalidOperationException(
            $"Could not write to {thermalPath}. Start OmenCore with the required permissions or configure a distro policy rule for this sysfs path.");
    }

    private static string? ResolvePowerProfilesCtlPath()
    {
        foreach (var candidate in PowerProfilesCtlCandidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static PerformanceMode ParsePowerProfilesCtlMode(string raw)
    {
        return raw.Trim().ToLowerInvariant() switch
        {
            "power-saver" => PerformanceMode.Cool,
            "performance" => PerformanceMode.Performance,
            _ => PerformanceMode.Balanced
        };
    }

    private static string ToPowerProfilesCtlMode(PerformanceMode mode)
    {
        return mode switch
        {
            PerformanceMode.Cool => "power-saver",
            PerformanceMode.Performance => "performance",
            _ => "balanced"
        };
    }

    private static async Task<(int exitCode, string stdout)> RunPowerProfilesCtlAsync(string arguments)
    {
        var binary = ResolvePowerProfilesCtlPath();
        if (binary == null)
        {
            return (-1, string.Empty);
        }

        try
        {
            using var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = binary,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            if (!process.Start())
            {
                return (-1, string.Empty);
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var waitTask = process.WaitForExitAsync();
            await Task.WhenAll(stdoutTask, waitTask);

            return (process.ExitCode, (await stdoutTask).Trim());
        }
        catch
        {
            return (-1, string.Empty);
        }
    }

    private static async Task<PerformanceMode?> TryGetPowerProfilesCtlModeAsync()
    {
        var (exitCode, stdout) = await RunPowerProfilesCtlAsync("get");
        if (exitCode != 0 || string.IsNullOrWhiteSpace(stdout))
        {
            return null;
        }

        return ParsePowerProfilesCtlMode(stdout);
    }

    private static async Task<bool> TrySetPerformanceModeViaPowerProfilesCtlAsync(PerformanceMode mode)
    {
        var profile = ToPowerProfilesCtlMode(mode);
        var (exitCode, _) = await RunPowerProfilesCtlAsync($"set {profile}");
        return exitCode == 0;
    }

    public async Task SetFanProfileAsync(string profile)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return;

        await ExecuteWithIoLockAsync(() =>
        {
            var ec = new OmenCore.Linux.Hardware.LinuxEcController();
            var fanProfile = profile.Trim().ToLowerInvariant() switch
            {
                "silent"   => OmenCore.Linux.Hardware.FanProfile.Silent,
                "balanced" => OmenCore.Linux.Hardware.FanProfile.Balanced,
                "gaming"   => OmenCore.Linux.Hardware.FanProfile.Gaming,
                "max"      => OmenCore.Linux.Hardware.FanProfile.Max,
                "constant" => OmenCore.Linux.Hardware.FanProfile.Constant,
                _          => OmenCore.Linux.Hardware.FanProfile.Auto
            };
            ec.SetFanProfile(fanProfile);
            _activeFanProfile = fanProfile;
            return Task.CompletedTask;
        });
    }

    public async Task SetCpuFanSpeedAsync(int percentage)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return;

        await ExecuteWithIoLockAsync(async () =>
        {
            int clamped = Math.Clamp(percentage, 0, 100);

            await TryEnableManualFanOverrideAsync();

            // Prefer direct hp-wmi fan output when exposed by kernel/firmware.
            try
            {
                if (File.Exists(HP_WMI_FAN1_OUTPUT))
                {
                    await File.WriteAllTextAsync(HP_WMI_FAN1_OUTPUT, clamped.ToString());
                    return;
                }

                var fanTargetPath = ResolveHwmonFanTargetPath(1);
                if (fanTargetPath != null)
                {
                    await File.WriteAllTextAsync(fanTargetPath, clamped.ToString());
                    return;
                }
            }
            catch
            {
                // Fall through to profile-based fallback.
            }

            // Fallback for hp_wmi-only boards that expose only thermal_profile:
            // approximate requested fan intensity by switching platform performance profile.
            var mode = clamped switch
            {
                <= 35 => PerformanceMode.Cool,
                <= 70 => PerformanceMode.Balanced,
                _ => PerformanceMode.Performance
            };

            await ApplyFanFallbackProfileAsync(mode);
        });
    }

    public async Task SetGpuFanSpeedAsync(int percentage)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return;

        await ExecuteWithIoLockAsync(async () =>
        {
            int clamped = Math.Clamp(percentage, 0, 100);

            await TryEnableManualFanOverrideAsync();

            try
            {
                if (File.Exists(HP_WMI_FAN2_OUTPUT))
                {
                    await File.WriteAllTextAsync(HP_WMI_FAN2_OUTPUT, clamped.ToString());
                    return;
                }

                var fanTargetPath = ResolveHwmonFanTargetPath(2);
                if (fanTargetPath != null)
                {
                    await File.WriteAllTextAsync(fanTargetPath, clamped.ToString());
                    return;
                }
            }
            catch
            {
                // Fall through to profile-based fallback.
            }

            var mode = clamped switch
            {
                <= 35 => PerformanceMode.Cool,
                <= 70 => PerformanceMode.Balanced,
                _ => PerformanceMode.Performance
            };

            await ApplyFanFallbackProfileAsync(mode);
        });
    }

    private async Task ApplyFanFallbackProfileAsync(PerformanceMode mode)
    {
        if (_lastFanFallbackMode == mode)
            return;

        try
        {
            await SetPerformanceModeCoreAsync(mode);
            _lastFanFallbackMode = mode;
        }
        catch
        {
            // No profile interface available on this model/kernel. Keep best-effort behavior.
        }
    }

    public async Task<string> GetGpuModeAsync()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return "hybrid";

        var gpuVendors = await ReadDrmGpuVendorsAsync();
        var hasDiscrete = gpuVendors.Any(v => IsDiscreteGpuVendor(v.vendorId));
        var hasIntegrated = gpuVendors.Any(v => IsIntegratedGpuVendor(v.vendorId));

        if (hasDiscrete && hasIntegrated)
        {
            return "hybrid";
        }

        return hasDiscrete ? "discrete" : "integrated";
    }

    public async Task SetGpuModeAsync(string mode)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return;

        await Task.CompletedTask;
        throw new NotSupportedException("GPU mode switching is distro-specific and is not invoked through external tools by OmenCore. Use BIOS or your distro's GPU profile manager.");
    }

    public async Task SetKeyboardBrightnessAsync(int brightness)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return;

        await ExecuteWithIoLockAsync(() =>
        {
            var ctrl = new OmenCore.Linux.Hardware.LinuxKeyboardController();
            if (!ctrl.SupportsBrightnessControl)
                throw new NotSupportedException("Keyboard brightness control is not available on this kernel/board.");
            if (!ctrl.SetBrightness(brightness))
                throw new InvalidOperationException("Failed to set keyboard brightness.");
            return Task.CompletedTask;
        });
    }

    public async Task SetKeyboardColorAsync(byte r, byte g, byte b)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return;

        await ExecuteWithIoLockAsync(() =>
        {
            var ctrl = new OmenCore.Linux.Hardware.LinuxKeyboardController();
            if (!ctrl.IsAvailable)
                throw new NotSupportedException("Keyboard RGB interface is not available on this kernel/board.");
            if (!ctrl.SetAllZonesColor(r, g, b))
                throw new InvalidOperationException("Failed to set keyboard color.");
            return Task.CompletedTask;
        });
    }

    public async Task SetKeyboardZoneColorAsync(int zone, byte r, byte g, byte b)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return;

        await ExecuteWithIoLockAsync(() =>
        {
            var ctrl = new OmenCore.Linux.Hardware.LinuxKeyboardController();
            if (!ctrl.IsAvailable)
                throw new NotSupportedException("Keyboard RGB interface is not available on this kernel/board.");
            if (!ctrl.SetZoneColor(zone, r, g, b))
                throw new InvalidOperationException($"Failed to set keyboard zone {zone} color.");
            return Task.CompletedTask;
        });
    }

    public async Task<int> GetKeyboardAnimationModeAsync()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return 0;

        return await ExecuteWithIoLockAsync(() =>
        {
            var ctrl = new OmenCore.Linux.Hardware.LinuxKeyboardController();
            return Task.FromResult(ctrl.GetAnimationMode());
        });
    }

    public async Task SetKeyboardAnimationModeAsync(int mode)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return;

        await ExecuteWithIoLockAsync(() =>
        {
            var ctrl = new OmenCore.Linux.Hardware.LinuxKeyboardController();
            if (!ctrl.HasFourZoneAnimationControl)
                throw new NotSupportedException("Keyboard animation control is not available on this kernel/board.");

            var value = Math.Clamp(mode, 0, 255);
            if (!ctrl.SetAnimationMode((byte)value))
                throw new InvalidOperationException($"Failed to set keyboard animation mode {value}.");

            return Task.CompletedTask;
        });
    }

    #region Private Helpers

    private async Task ExecuteWithIoLockAsync(Func<Task> operation)
    {
        await _ioLock.WaitAsync();
        try
        {
            await operation();
        }
        finally
        {
            _ioLock.Release();
        }
    }

    private async Task<T> ExecuteWithIoLockAsync<T>(Func<Task<T>> operation)
    {
        await _ioLock.WaitAsync();
        try
        {
            return await operation();
        }
        finally
        {
            _ioLock.Release();
        }
    }

    private static async Task TryEnableManualFanOverrideAsync()
    {
        try
        {
            if (File.Exists(HP_WMI_FAN_ALWAYS_ON))
            {
                await File.WriteAllTextAsync(HP_WMI_FAN_ALWAYS_ON, "1");
            }
        }
        catch
        {
            // Best-effort only.
        }
    }

    private static async Task<string?> ReadFirstExistingTextAsync(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            try
            {
                if (File.Exists(path))
                {
                    return (await File.ReadAllTextAsync(path)).Trim();
                }
            }
            catch
            {
                // Ignore and continue to next candidate.
            }
        }

        return null;
    }

    private static string? ResolveHwmonFanTargetPath(int fanIndex)
    {
        try
        {
            foreach (var hwmonDir in LinuxSysfsPathMap.EnumerateHpWmiHwmonDirectories())
            {
                var targetPath = Path.Combine(hwmonDir, $"fan{fanIndex}_target");
                if (File.Exists(targetPath))
                    return targetPath;
            }
        }
        catch
        {
            // Best-effort resolution.
        }

        return null;
    }

    private static async Task<int> ReadCoretempackageTemp(string hwmonDir)
    {
        try
        {
            foreach (var tempFile in Directory.GetFiles(hwmonDir, "temp*_input"))
            {
                var labelFile = tempFile.Replace("_input", "_label");
                if (!File.Exists(labelFile)) continue;
                var label = (await File.ReadAllTextAsync(labelFile)).Trim();
                if (label.StartsWith("Package", StringComparison.OrdinalIgnoreCase))
                {
                    var tempStr = await File.ReadAllTextAsync(tempFile);
                    if (int.TryParse(tempStr.Trim(), out var temp) && temp > 0)
                        return temp;
                }
            }
        }
        catch { }
        return 0;
    }

    private static async Task<int> ReadTemperatureAsync(string type)
    {
        try
        {
            foreach (var hwmon in Directory.GetDirectories(HWMON_BASE))
            {
                try
                {
                    var namePath = Path.Combine(hwmon, "name");
                    if (!File.Exists(namePath)) continue;
                    
                    var name = (await File.ReadAllTextAsync(namePath)).Trim().ToLower();
                    
                    // Match multiple CPU temperature sensor names
                    // Intel: coretemp, AMD: k10temp, zenpower, amd_energy
                    bool isCpuSensor = type == "coretemp" && 
                        (name == "coretemp" || name == "k10temp" || name == "zenpower" || 
                         name == "amd_energy" || name.Contains("cpu") || name.Contains("tctl"));
                    
                    if (isCpuSensor || name == type)
                    {
                        // For Intel coretemp: prefer "Package id 0" over individual cores
                        if (name == "coretemp")
                        {
                            var packageTemp = await ReadCoretempackageTemp(hwmon);
                            if (packageTemp > 0) return packageTemp;
                        }

                        // AMD / fallback: try common temp file names
                        var tempFiles = new[] { "temp1_input", "temp2_input", "temp3_input", "Tctl" };
                        foreach (var tempFile in tempFiles)
                        {
                            var tempPath = Path.Combine(hwmon, tempFile);
                            if (File.Exists(tempPath))
                            {
                                var tempStr = await File.ReadAllTextAsync(tempPath);
                                if (int.TryParse(tempStr.Trim(), out var temp) && temp > 0)
                                    return temp;
                            }
                        }
                    }
                }
                catch { }
            }
        }
        catch { }
        
        // Fallback: Try to find any temperature sensor
        try
        {
            foreach (var hwmon in Directory.GetDirectories(HWMON_BASE))
            {
                var tempFiles = Directory.GetFiles(hwmon, "temp*_input");
                foreach (var tempFile in tempFiles)
                {
                    var tempStr = await File.ReadAllTextAsync(tempFile);
                    if (int.TryParse(tempStr.Trim(), out var temp) && temp > 1000 && temp < 150000)
                        return temp; // Valid temperature in millidegrees
                }
            }
        }
        catch { }
        
        return 0;
    }

    private static async Task<int> ReadGpuTemperatureAsync()
    {
        foreach (var hwmon in SafeEnumerateDirectories(HWMON_BASE))
        {
            try
            {
                var namePath = Path.Combine(hwmon, "name");
                if (!File.Exists(namePath)) continue;

                var name = (await File.ReadAllTextAsync(namePath)).Trim().ToLower();
                if (name == "amdgpu" ||
                    name == "nouveau" ||
                    name.Contains("nvidia") ||
                    name.Contains("radeon") ||
                    name.Contains("gpu") ||
                    name.Contains("i915") ||
                    name.Contains("intel"))
                {
                    var tempFiles = new[] { "temp1_input", "temp2_input", "temp3_input", "edge", "junction" };
                    foreach (var tempFile in tempFiles)
                    {
                        var tempPath = Path.Combine(hwmon, tempFile);
                        if (File.Exists(tempPath))
                        {
                            var tempStr = await File.ReadAllTextAsync(tempPath);
                            if (int.TryParse(tempStr.Trim(), out var temp) && temp > 0)
                                return temp;
                        }
                    }
                }
            }
            catch { }
        }

        // Fallback: read GPU temperature from EC register 0xB7 (HP OMEN hybrid GPU path)
        try
        {
            var ec = new OmenCore.Linux.Hardware.LinuxEcController();
            var ecTemp = ec.GetGpuTemperature();
            if (ecTemp.HasValue && ecTemp.Value > 0)
                return ecTemp.Value * 1000; // EC returns °C, callers expect millidegrees
        }
        catch { }

        return 0;
    }

    private static async Task<double> ReadGpuUsageAsync()
    {
        // NVIDIA: query via nvidia-smi
        var nvidiaSmi = new[] { "/usr/bin/nvidia-smi", "/usr/local/bin/nvidia-smi" }
            .FirstOrDefault(File.Exists);
        if (nvidiaSmi != null)
        {
            try
            {
                using var proc = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = nvidiaSmi,
                        Arguments = "--query-gpu=utilization.gpu --format=csv,noheader,nounits",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };
                proc.Start();
                var stdout = await proc.StandardOutput.ReadToEndAsync();
                await proc.WaitForExitAsync();
                if (proc.ExitCode == 0 &&
                    double.TryParse(stdout.Trim(), System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var pct))
                {
                    return pct;
                }
            }
            catch { }
        }

        // AMD / Intel: gpu_busy_percent in DRM sysfs
        foreach (var card in SafeEnumerateDirectories("/sys/class/drm"))
        {
            var busyPath = Path.Combine(card, "device", "gpu_busy_percent");
            if (!File.Exists(busyPath)) continue;
            try
            {
                var raw = await File.ReadAllTextAsync(busyPath);
                if (double.TryParse(raw.Trim(), out var busy))
                    return busy;
            }
            catch { }
        }

        return 0;
    }

    private static async Task<int> ReadFanRpmAsync(string type)
    {
        try
        {
            foreach (var hwmon in Directory.GetDirectories(HWMON_BASE))
            {
                try
                {
                    // Check if this hwmon is related to the requested type
                    var namePath = Path.Combine(hwmon, "name");
                    var deviceName = File.Exists(namePath) ? 
                        (await File.ReadAllTextAsync(namePath)).Trim().ToLower() : "";
                    
                    // Look for fan speed inputs
                    var fanInputs = Directory.GetFiles(hwmon, "fan*_input");
                    if (fanInputs.Length == 0)
                    {
                        // Some HP laptops expose fans via PWM files
                        fanInputs = Directory.GetFiles(hwmon, "pwm*");
                    }
                    
                    foreach (var fanInput in fanInputs)
                    {
                        var rpm = await File.ReadAllTextAsync(fanInput);
                        if (int.TryParse(rpm.Trim(), out var rpmValue) && rpmValue > 0)
                            return rpmValue;
                    }
                }
                catch { }
            }
            
            // Try HP-specific fan paths
            var hpFanPaths = new[]
            {
                "/sys/devices/platform/hp-wmi/fan1_input",
                "/sys/devices/platform/hp-wmi/fan2_input",
                "/sys/class/hwmon/hwmon*/fan1_input"
            };
            
            foreach (var fanPath in hpFanPaths)
            {
                if (fanPath.Contains("*"))
                {
                    var matches = Directory.GetFiles(Path.GetDirectoryName(fanPath) ?? "", Path.GetFileName(fanPath));
                    foreach (var match in matches)
                    {
                        if (File.Exists(match))
                        {
                            var rpm = await File.ReadAllTextAsync(match);
                            if (int.TryParse(rpm.Trim(), out var rpmValue) && rpmValue > 0)
                                return rpmValue;
                        }
                    }
                }
                else if (File.Exists(fanPath))
                {
                    var rpm = await File.ReadAllTextAsync(fanPath);
                    if (int.TryParse(rpm.Trim(), out var rpmValue) && rpmValue > 0)
                        return rpmValue;
                }
            }
        }
        catch { }
        return 0;
    }

    private static async Task<double> ReadCpuUsageAsync()
    {
        try
        {
            var stat1 = await File.ReadAllLinesAsync("/proc/stat");
            await Task.Delay(100);
            var stat2 = await File.ReadAllLinesAsync("/proc/stat");

            var cpu1 = ParseCpuLine(stat1[0]);
            var cpu2 = ParseCpuLine(stat2[0]);

            var total1 = cpu1.Sum();
            var total2 = cpu2.Sum();
            var idle1 = cpu1[3];
            var idle2 = cpu2[3];

            var totalDiff = total2 - total1;
            var idleDiff = idle2 - idle1;

            return totalDiff > 0 ? (1.0 - (double)idleDiff / totalDiff) * 100 : 0;
        }
        catch { }
        return 0;
    }

    private static long[] ParseCpuLine(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Skip(1).Select(long.Parse).ToArray();
    }

    private static async Task<(double percentage, double usedGb, double totalGb)> ReadMemoryUsageAsync()
    {
        try
        {
            var meminfo = await File.ReadAllLinesAsync("/proc/meminfo");
            long total = 0, available = 0;

            foreach (var line in meminfo)
            {
                if (line.StartsWith("MemTotal:"))
                    total = long.Parse(line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1]);
                else if (line.StartsWith("MemAvailable:"))
                    available = long.Parse(line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1]);
            }

            if (total > 0)
            {
                var totalGb = total / 1024.0 / 1024.0; // Convert KB to GB
                var usedGb = (total - available) / 1024.0 / 1024.0;
                var percentage = (1.0 - (double)available / total) * 100;
                return (percentage, usedGb, totalGb);
            }
        }
        catch { }
        return (0, 0, 0);
    }

    private static async Task<(int percentage, bool onBattery)> ReadBatteryStatusAsync()
    {
        try
        {
            var batteries = Directory.GetDirectories(POWER_SUPPLY);
            foreach (var battery in batteries)
            {
                var type = await File.ReadAllTextAsync(Path.Combine(battery, "type"));
                if (type.Trim() == "Battery")
                {
                    var capacity = await File.ReadAllTextAsync(Path.Combine(battery, "capacity"));
                    var status = await File.ReadAllTextAsync(Path.Combine(battery, "status"));
                    var onBattery = status.Trim() == "Discharging";
                    return (int.Parse(capacity.Trim()), onBattery);
                }
            }
        }
        catch { }
        return (100, false);
    }

    private async Task<double> ReadPowerConsumptionAsync()
    {
        // Intel RAPL: delta between successive calls — no sleep needed since we poll every ~2.5s
        const string raplPath = "/sys/class/powercap/intel-rapl/intel-rapl:0/energy_uj";
        try
        {
            if (File.Exists(raplPath))
            {
                var raw = await File.ReadAllTextAsync(raplPath);
                if (long.TryParse(raw.Trim(), out var energyUj))
                {
                    var now = DateTime.UtcNow;
                    if (_raplLastReadTime != DateTime.MinValue && energyUj >= _raplLastEnergyUj)
                    {
                        var elapsedUs = (now - _raplLastReadTime).TotalSeconds * 1_000_000.0;
                        if (elapsedUs > 0)
                        {
                            var watts = Math.Round((energyUj - _raplLastEnergyUj) / elapsedUs, 1);
                            _raplLastEnergyUj = energyUj;
                            _raplLastReadTime = now;
                            return watts;
                        }
                    }
                    _raplLastEnergyUj = energyUj;
                    _raplLastReadTime = now;
                }
            }
        }
        catch { }

        // Fallback: battery power_now (µW → W) when on battery
        try
        {
            foreach (var bat in Directory.GetDirectories(POWER_SUPPLY))
            {
                var typeFile = Path.Combine(bat, "type");
                var powerFile = Path.Combine(bat, "power_now");
                if (!File.Exists(typeFile) || !File.Exists(powerFile)) continue;
                var type = (await File.ReadAllTextAsync(typeFile)).Trim();
                if (type != "Battery") continue;
                var powerRaw = (await File.ReadAllTextAsync(powerFile)).Trim();
                if (long.TryParse(powerRaw, out var uw))
                    return Math.Round(uw / 1_000_000.0, 1);
            }
        }
        catch { }
        return 0;
    }

    private static async Task<bool> HasDiscreteGpuAsync()
    {
        var gpuVendors = await ReadDrmGpuVendorsAsync();
        return gpuVendors.Any(v => IsDiscreteGpuVendor(v.vendorId));
    }

    private static async Task<string?> ReadDmiStringAsync(string field)
    {
        var path = $"/sys/class/dmi/id/{field}";
        try
        {
            if (File.Exists(path))
                return (await File.ReadAllTextAsync(path)).Trim();
        }
        catch { }
        return null;
    }

    private static async Task<string> ReadCpuNameAsync()
    {
        try
        {
            var cpuinfo = await File.ReadAllLinesAsync("/proc/cpuinfo");
            var modelLine = cpuinfo.FirstOrDefault(l => l.StartsWith("model name"));
            if (modelLine != null)
            {
                return modelLine.Split(':')[1].Trim();
            }
        }
        catch { }
        return "Unknown CPU";
    }

    private static async Task<string> ReadGpuNameAsync()
    {
        var gpuVendors = await ReadDrmGpuVendorsAsync();
        var selected = gpuVendors.FirstOrDefault(v => IsDiscreteGpuVendor(v.vendorId));
        if (string.IsNullOrWhiteSpace(selected.vendorId))
        {
            selected = gpuVendors.FirstOrDefault();
        }

        if (!string.IsNullOrWhiteSpace(selected.vendorId))
        {
            return FormatGpuName(selected.vendorId, selected.deviceId);
        }

        return "Unknown GPU";
    }

    private static async Task<IReadOnlyList<(string vendorId, string deviceId)>> ReadDrmGpuVendorsAsync()
    {
        var result = new List<(string vendorId, string deviceId)>();
        const string drmPath = "/sys/class/drm";

        foreach (var card in SafeEnumerateDirectories(drmPath))
        {
            var cardName = Path.GetFileName(card);
            if (string.IsNullOrWhiteSpace(cardName) ||
                !cardName.StartsWith("card", StringComparison.Ordinal) ||
                cardName.Contains("-", StringComparison.Ordinal))
            {
                continue;
            }

            var devicePath = Path.Combine(card, "device");
            var vendorPath = Path.Combine(devicePath, "vendor");
            if (!File.Exists(vendorPath))
            {
                continue;
            }

            try
            {
                var vendor = (await File.ReadAllTextAsync(vendorPath)).Trim().ToLowerInvariant();
                var deviceFile = Path.Combine(devicePath, "device");
                var device = File.Exists(deviceFile)
                    ? (await File.ReadAllTextAsync(deviceFile)).Trim().ToLowerInvariant()
                    : string.Empty;

                result.Add((vendor, device));
            }
            catch
            {
                // Best-effort sysfs probing.
            }
        }

        return result;
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string path)
    {
        try
        {
            return Directory.Exists(path)
                ? Directory.EnumerateDirectories(path).ToArray()
                : Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static bool IsDiscreteGpuVendor(string vendorId)
    {
        return string.Equals(vendorId, "0x10de", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(vendorId, "0x1002", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsIntegratedGpuVendor(string vendorId)
    {
        return string.Equals(vendorId, "0x8086", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatGpuName(string vendorId, string deviceId)
    {
        var suffix = string.IsNullOrWhiteSpace(deviceId) ? string.Empty : $" ({deviceId})";
        return vendorId.ToLowerInvariant() switch
        {
            "0x10de" => $"NVIDIA GPU{suffix}",
            "0x1002" => $"AMD Radeon GPU{suffix}",
            "0x8086" => $"Intel Integrated Graphics{suffix}",
            _ => $"GPU {vendorId}{suffix}"
        };
    }

    #endregion

    #region Longevity / Battery Care

    public async Task<(double healthPercent, int cycleCount)> GetBatteryHealthAsync()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return (100.0, 0);

        double health = 100.0;
        int cycles = 0;
        try
        {
            var batteries = Directory.GetDirectories(POWER_SUPPLY);
            foreach (var bat in batteries)
            {
                var typePath = Path.Combine(bat, "type");
                if (!File.Exists(typePath)) continue;
                if ((await File.ReadAllTextAsync(typePath)).Trim() != "Battery") continue;

                var fullDesignPath = Path.Combine(bat, "energy_full_design");
                var fullPath = Path.Combine(bat, "energy_full");
                var cyclePath = Path.Combine(bat, "cycle_count");

                if (File.Exists(fullDesignPath) && File.Exists(fullPath))
                {
                    if (long.TryParse((await File.ReadAllTextAsync(fullDesignPath)).Trim(), out var design) &&
                        long.TryParse((await File.ReadAllTextAsync(fullPath)).Trim(), out var full) &&
                        design > 0)
                    {
                        health = Math.Round(100.0 * full / design, 1);
                    }
                }
                if (File.Exists(cyclePath) &&
                    int.TryParse((await File.ReadAllTextAsync(cyclePath)).Trim(), out var c))
                {
                    cycles = c;
                }
                break;
            }
        }
        catch { }
        return (health, cycles);
    }

    public async Task<int?> GetCpuPowerLimitAsync()
    {
        if (!File.Exists(RaplPl1Path)) return null;
        try
        {
            var raw = await File.ReadAllTextAsync(RaplPl1Path);
            if (long.TryParse(raw.Trim(), out var uw))
                return (int)(uw / 1_000_000);
        }
        catch { }
        return null;
    }

    public async Task SetCpuPowerLimitAsync(int watts)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return;
        var uw = (long)watts * 1_000_000;
        await WriteSysfsAsync(RaplPl1Path, uw.ToString());
    }

    private static async Task<string?> FindChargeThresholdPathAsync()
    {
        if (!Directory.Exists(POWER_SUPPLY)) return null;
        foreach (var bat in Directory.GetDirectories(POWER_SUPPLY))
        {
            var typePath = Path.Combine(bat, "type");
            var threshPath = Path.Combine(bat, "charge_control_end_threshold");
            if (!File.Exists(typePath) || !File.Exists(threshPath)) continue;
            try
            {
                if ((await File.ReadAllTextAsync(typePath)).Trim() == "Battery")
                    return threshPath;
            }
            catch { }
        }
        return null;
    }

    public async Task<int> GetChargeEndThresholdAsync()
    {
        var path = await FindChargeThresholdPathAsync();
        if (path == null) return 0;
        try
        {
            var raw = await File.ReadAllTextAsync(path);
            if (int.TryParse(raw.Trim(), out var pct))
                return pct;
        }
        catch { }
        return 0;
    }

    public async Task SetChargeEndThresholdAsync(int percent)
    {
        var path = await FindChargeThresholdPathAsync();
        if (path == null)
            throw new NotSupportedException("Battery charge limit not available via sysfs on this hardware. Set it in BIOS: F10 → Advanced → Battery Care Mode.");
        await WriteSysfsAsync(path, percent.ToString());
    }

    private static async Task WriteSysfsAsync(string path, string value)
    {
        try
        {
            await using var fs = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
            var bytes = System.Text.Encoding.UTF8.GetBytes(value);
            await fs.WriteAsync(bytes);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new UnauthorizedAccessException($"Cannot write to {path}. Run OmenCore as root or ensure the udev rule is applied.", ex);
        }
    }

    #endregion

    #region NVIDIA Settings

    private static readonly string[] NvidiaSettingsCandidates =
    {
        "/usr/bin/nvidia-settings",
        "/usr/local/bin/nvidia-settings"
    };

    private static string? ResolveNvidiaSettingsPath()
    {
        foreach (var candidate in NvidiaSettingsCandidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    #endregion

    public void Dispose()
    {
        if (!_disposed)
        {
            _pollingTimer.Stop();
            _pollingTimer.Dispose();
            _ioLock.Dispose();
            _disposed = true;
        }
    }
}
