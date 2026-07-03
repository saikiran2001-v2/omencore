using System.Text.Json;
using OmenCore.Linux.Hardware;

namespace OmenCore.Linux.Config;

/// <summary>
/// Persists GUI hardware preferences to the user home and /var/lib/omencore so
/// settings survive rebuilds and the root daemon can apply them without sudo in the GUI.
/// </summary>
public static class UserPreferencesStore
{
    public static string SharedPreferencesPath =>
        Path.Combine(OmenCorePaths.SharedConfigDir, "user-preferences.json");

    public static UserHardwarePreferences LoadBestAvailable()
    {
        var homePath = OmenCorePaths.GetUserPreferencesPath();
        var sharedPath = SharedPreferencesPath;

        var home = TryLoad(homePath);
        var shared = TryLoad(sharedPath);

        if (home != null && shared != null)
        {
            var homeTime = File.GetLastWriteTimeUtc(homePath);
            var sharedTime = File.GetLastWriteTimeUtc(sharedPath);
            return homeTime >= sharedTime ? home : shared;
        }

        return home ?? shared ?? new UserHardwarePreferences();
    }

    public static UserHardwarePreferences? TryLoad(string path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize(json, UserPreferencesJsonContext.Default.UserHardwarePreferences);
        }
        catch
        {
            return null;
        }
    }

    public static void SaveToAllLocations(UserHardwarePreferences preferences)
    {
        var homePath = OmenCorePaths.GetUserPreferencesPath();
        Directory.CreateDirectory(Path.GetDirectoryName(homePath)!);
        WriteJson(homePath, preferences);

        OmenCorePaths.EnsureSharedConfigDirectory();
        WriteJson(SharedPreferencesPath, preferences);
    }

    private static void WriteJson(string path, UserHardwarePreferences preferences)
    {
        var json = JsonSerializer.Serialize(preferences, UserPreferencesJsonContext.Default.UserHardwarePreferences);
        File.WriteAllText(path, json);
        try
        {
            if (OperatingSystem.IsLinux())
            {
                File.SetUnixFileMode(path,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite |
                    UnixFileMode.GroupRead | UnixFileMode.GroupWrite |
                    UnixFileMode.OtherRead);
            }
        }
        catch
        {
            // Best effort only.
        }
    }

    public static void SyncDaemonConfig(UserHardwarePreferences preferences)
    {
        var config = OmenCoreConfig.Load();
        ApplyToOmenCoreConfig(config, preferences);
        config.Startup.ApplyOnBoot = true;

        OmenCorePaths.EnsureSharedConfigDirectory();
        try
        {
            config.Save(OmenCoreConfig.SharedConfigPath);
        }
        catch
        {
            config.Save();
        }
    }

    public static void ApplyToOmenCoreConfig(OmenCoreConfig config, UserHardwarePreferences preferences)
    {
        var kb = preferences.Keyboard;
        var fan = preferences.Fan;

        config.Keyboard.Enabled = true;
        config.Keyboard.Brightness = Math.Clamp(kb.Brightness, 0, 100);
        config.Keyboard.Color = $"{kb.Zone1R:X2}{kb.Zone1G:X2}{kb.Zone1B:X2}";
        config.Keyboard.Zone1Color = config.Keyboard.Color;
        config.Keyboard.Zone2Color = $"{kb.Zone2R:X2}{kb.Zone2G:X2}{kb.Zone2B:X2}";
        config.Keyboard.Zone3Color = $"{kb.Zone3R:X2}{kb.Zone3G:X2}{kb.Zone3B:X2}";
        config.Keyboard.Zone4Color = $"{kb.Zone4R:X2}{kb.Zone4G:X2}{kb.Zone4B:X2}";
        config.Keyboard.AnimationMode = kb.AnimationIndex;

        if (string.Equals(fan.ActiveFanProfile, "manual", StringComparison.OrdinalIgnoreCase)
            && fan.IsCustomCurveEnabled
            && fan.CpuCurve.Count >= 2)
        {
            config.Fan.Profile = "custom";
            config.Fan.Curve.Enabled = true;
            config.Fan.Curve.Hysteresis = Math.Clamp(fan.CurveHysteresis, 1, 10);
            config.Fan.Curve.RampUpDelaySeconds = Math.Clamp(fan.CurveRampUpDelay, 0, 30);
            config.Fan.Curve.RampDownDelaySeconds = Math.Clamp(fan.CurveRampDownDelay, 0, 30);
            config.Fan.Curve.Points = fan.CpuCurve
                .OrderBy(p => p.Temperature)
                .Select(p => new FanCurvePoint
                {
                    Temp = p.Temperature,
                    Speed = p.FanSpeed
                })
                .ToList();
        }
        else
        {
            config.Fan.Profile = fan.ActiveFanProfile.Trim().ToLowerInvariant() switch
            {
                "silent" => "silent",
                "balanced" => "balanced",
                "gaming" or "performance" => "gaming",
                "max" => "max",
                _ => "auto"
            };
            config.Fan.Curve.Enabled = false;
        }
    }

    public static bool ApplyKeyboard(LinuxKeyboardController keyboard, KeyboardLightingPreferences kb)
    {
        if (!keyboard.IsAvailable)
            return false;

        var factor = Math.Clamp(kb.Brightness, 0, 100) / 100.0;
        var zones = new[]
        {
            (kb.Zone1R, kb.Zone1G, kb.Zone1B),
            (kb.Zone2R, kb.Zone2G, kb.Zone2B),
            (kb.Zone3R, kb.Zone3G, kb.Zone3B),
            (kb.Zone4R, kb.Zone4G, kb.Zone4B),
        };

        var applied = false;
        if (keyboard.HasFourZoneControl)
        {
            for (var i = 0; i < zones.Length; i++)
            {
                var (r, g, b) = zones[i];
                var scaledR = (byte)(Math.Clamp(r, 0, 255) * factor);
                var scaledG = (byte)(Math.Clamp(g, 0, 255) * factor);
                var scaledB = (byte)(Math.Clamp(b, 0, 255) * factor);
                if (keyboard.SetZoneColor(i, scaledR, scaledG, scaledB))
                    applied = true;
            }
        }
        else if (TryParseHexColor(configColor: $"{kb.Zone1R:X2}{kb.Zone1G:X2}{kb.Zone1B:X2}",
                     out var r, out var g, out var b))
        {
            applied = keyboard.SetAllZonesColor(
                (byte)(r * factor), (byte)(g * factor), (byte)(b * factor));
        }

        if (keyboard.SupportsBrightnessControl)
            keyboard.SetBrightness(kb.Brightness);

        if (kb.AnimationIndex > 0 && keyboard.HasFourZoneAnimationControl)
            keyboard.SetAnimationMode((byte)kb.AnimationIndex);

        return applied;
    }

    private static bool TryParseHexColor(string configColor, out double r, out double g, out double b)
    {
        r = g = b = 0;
        var value = configColor.Trim().TrimStart('#');
        if (value.Length != 6 || !int.TryParse(value, System.Globalization.NumberStyles.HexNumber, null, out var rgb))
            return false;

        r = (rgb >> 16) & 0xFF;
        g = (rgb >> 8) & 0xFF;
        b = rgb & 0xFF;
        return true;
    }
}
