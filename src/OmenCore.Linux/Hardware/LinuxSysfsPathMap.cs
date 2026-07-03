namespace OmenCore.Linux.Hardware;

/// <summary>
/// Centralized Linux sysfs path normalization for hp-wmi and ACPI capability probing.
/// </summary>
public static class LinuxSysfsPathMap
{
    public const string EcIoPath = "/sys/kernel/debug/ec/ec0/io";
    public const string HpWmiRoot = "/sys/devices/platform/hp-wmi";
    public const string HpWmiHwmonRoot = "/sys/devices/platform/hp-wmi/hwmon";
    public const string AcpiPlatformProfilePath = "/sys/firmware/acpi/platform_profile";
    public const string AcpiPlatformProfileChoicesPath = "/sys/firmware/acpi/platform_profile_choices";
    public const string KeyboardBacklightPath = "/sys/class/leds/hp::kbd_backlight";

    // Kernel 6.13+ registers platform profiles under /sys/class/platform-profile/<name>/profile
    // The device name is assigned by the kernel and may vary ("hp-wmi", "platform-profile-0", etc.)
    // Older kernels use /sys/firmware/acpi/platform_profile (single global node)
    public static readonly string[] ThermalProfilePaths =
    {
        "/sys/class/platform-profile/hp-wmi/profile",
        "/sys/class/platform-profile/platform-profile-0/profile",
        "/sys/firmware/acpi/platform_profile",
        "/sys/devices/platform/hp-wmi/thermal_profile",
        "/sys/devices/platform/hp-wmi/thermal-profile",
        "/sys/devices/platform/hp-wmi/platform_profile",
        "/sys/devices/platform/hp-wmi/platform-profile",
        "/sys/devices/platform/hp-wmi/performance_profile",
        "/sys/devices/platform/hp-wmi/performance-profile"
    };

    public static readonly string[] ThermalProfileChoicePaths =
    {
        "/sys/class/platform-profile/hp-wmi/choices",
        "/sys/class/platform-profile/platform-profile-0/choices",
        "/sys/firmware/acpi/platform_profile_choices",
        "/sys/devices/platform/hp-wmi/platform_profile_choices",
        "/sys/devices/platform/hp-wmi/platform-profile-choices",
        "/sys/devices/platform/hp-wmi/thermal_profile_choices",
        "/sys/devices/platform/hp-wmi/thermal-profile-choices"
    };

    public static readonly string[] PlatformProfilePaths =
    {
        "/sys/class/platform-profile/hp-wmi/profile",
        "/sys/class/platform-profile/platform-profile-0/profile",
        "/sys/devices/platform/hp-wmi/platform_profile",
        "/sys/devices/platform/hp-wmi/platform-profile"
    };

    public static readonly string[] HpWmiThermalProfilePaths =
    {
        "/sys/class/platform-profile/hp-wmi/profile",
        "/sys/class/platform-profile/platform-profile-0/profile",
        "/sys/devices/platform/hp-wmi/thermal_profile",
        "/sys/devices/platform/hp-wmi/thermal-profile"
    };

    public static readonly string[] HpWmiPlatformProfileChoicePaths =
    {
        "/sys/devices/platform/hp-wmi/platform_profile_choices",
        "/sys/devices/platform/hp-wmi/platform-profile-choices"
    };

    public static readonly string[] HpWmiThermalProfileChoicePaths =
    {
        "/sys/devices/platform/hp-wmi/thermal_profile_choices",
        "/sys/devices/platform/hp-wmi/thermal-profile-choices"
    };

    public static string? ResolveFirstExistingFile(IEnumerable<string> candidates)
    {
        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public static string? ResolveThermalProfilePath()
    {
        // First check known static paths
        var known = ResolveFirstExistingFile(ThermalProfilePaths);
        if (known != null) return known;

        // Scan /sys/class/platform-profile/ for any registered handler (kernel 6.13+ API)
        const string profileClassRoot = "/sys/class/platform-profile";
        try
        {
            if (Directory.Exists(profileClassRoot))
            {
                foreach (var dir in Directory.EnumerateDirectories(profileClassRoot))
                {
                    var profileFile = Path.Combine(dir, "profile");
                    if (File.Exists(profileFile)) return profileFile;
                }
            }
        }
        catch { }

        return null;
    }

    public static string? ResolveThermalProfileChoicesPath()
    {
        var known = ResolveFirstExistingFile(ThermalProfileChoicePaths);
        if (known != null) return known;

        const string profileClassRoot = "/sys/class/platform-profile";
        try
        {
            if (Directory.Exists(profileClassRoot))
            {
                foreach (var dir in Directory.EnumerateDirectories(profileClassRoot))
                {
                    var choicesFile = Path.Combine(dir, "choices");
                    if (File.Exists(choicesFile)) return choicesFile;
                }
            }
        }
        catch { }

        return null;
    }

    public static bool AnyPathExists(IEnumerable<string> candidates) => candidates.Any(File.Exists);

    public static IEnumerable<string> EnumerateHpWmiHwmonDirectories()
    {
        var directories = new List<string>();

        if (Directory.Exists(HpWmiHwmonRoot))
        {
            try
            {
                directories.AddRange(
                    Directory.GetDirectories(HpWmiHwmonRoot, "hwmon*", SearchOption.TopDirectoryOnly));
            }
            catch
            {
                // Fall through to /sys/class/hwmon scan.
            }
        }

        const string classHwmonRoot = "/sys/class/hwmon";
        if (Directory.Exists(classHwmonRoot))
        {
            try
            {
                foreach (var hwmonDir in Directory.GetDirectories(classHwmonRoot))
                {
                    var namePath = Path.Combine(hwmonDir, "name");
                    if (!File.Exists(namePath))
                        continue;

                    var name = File.ReadAllText(namePath).Trim();
                    if (name.Equals("hp", StringComparison.OrdinalIgnoreCase) &&
                        !directories.Contains(hwmonDir))
                    {
                        directories.Add(hwmonDir);
                    }
                }
            }
            catch
            {
                // Best-effort only.
            }
        }

        return directories;
    }

    public static bool HasHpWmiPwmDutyAccess() =>
        ResolveHpWmiPwmEnablePath(1) != null && ResolveHpWmiPwmPath(1) != null;

    public static string? ResolveHpWmiFanTargetPath(int fanIndex)
    {
        foreach (var hwmonDir in EnumerateHpWmiHwmonDirectories())
        {
            var candidate = Path.Combine(hwmonDir, $"fan{fanIndex}_target");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public static bool HasHpWmiFanTarget(int fanIndex) => ResolveHpWmiFanTargetPath(fanIndex) != null;

    public static string? ResolveHpWmiPwmEnablePath(int index)
    {
        foreach (var hwmonDir in EnumerateHpWmiHwmonDirectories())
        {
            var candidate = Path.Combine(hwmonDir, $"pwm{index}_enable");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public static bool HasHpWmiPwmEnable(int index) => ResolveHpWmiPwmEnablePath(index) != null;

    public static string? ResolveHpWmiPwmPath(int index)
    {
        foreach (var hwmonDir in EnumerateHpWmiHwmonDirectories())
        {
            var candidate = Path.Combine(hwmonDir, $"pwm{index}");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public static bool HasHpWmiPwm(int index) => ResolveHpWmiPwmPath(index) != null;

    public static string? ResolveHpWmiFanInputPath(int index)
    {
        foreach (var hwmonDir in EnumerateHpWmiHwmonDirectories())
        {
            var candidate = Path.Combine(hwmonDir, $"fan{index}_input");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public static bool HasHpWmiFanInput(int index) => ResolveHpWmiFanInputPath(index) != null;
}
