using OmenCore.Linux.Config;

namespace OmenCore.Avalonia.Services;

public interface IUserPreferencesService
{
    UserHardwarePreferences Current { get; }
    Task LoadAsync();
    Task SaveAsync();
    Task SaveAndSyncDaemonConfigAsync();
}

/// <summary>
/// Persists GUI hardware preferences to ~/.config/omencore and /var/lib/omencore.
/// </summary>
public sealed class UserPreferencesService : IUserPreferencesService
{
    public UserHardwarePreferences Current { get; } = new();

    public UserPreferencesService()
    {
        LoadFromDisk();
    }

    public Task LoadAsync()
    {
        LoadFromDisk();
        return Task.CompletedTask;
    }

    private void LoadFromDisk()
    {
        var loaded = UserPreferencesStore.LoadBestAvailable();
        Current.Keyboard = loaded.Keyboard ?? new KeyboardLightingPreferences();
        Current.Fan = loaded.Fan ?? new FanControlPreferences();

        if (File.Exists(OmenCorePaths.GetUserPreferencesPath())
            || File.Exists(UserPreferencesStore.SharedPreferencesPath))
        {
            return;
        }

        MigrateFromDaemonConfig();
    }

    private void MigrateFromDaemonConfig()
    {
        try
        {
            var config = OmenCoreConfig.Load();
            if (!TryParseHexColor(config.Keyboard.Color, out var r, out var g, out var b))
                return;

            Current.Keyboard.Zone1R = r;
            Current.Keyboard.Zone1G = g;
            Current.Keyboard.Zone1B = b;

            if (TryParseHexColor(config.Keyboard.Zone1Color, out var r1, out var g1, out var b1))
            {
                Current.Keyboard.Zone1R = r1;
                Current.Keyboard.Zone1G = g1;
                Current.Keyboard.Zone1B = b1;
            }

            if (TryParseHexColor(config.Keyboard.Zone2Color, out var r2, out var g2, out var b2))
            {
                Current.Keyboard.Zone2R = r2;
                Current.Keyboard.Zone2G = g2;
                Current.Keyboard.Zone2B = b2;
            }
            else
            {
                Current.Keyboard.Zone2R = r;
                Current.Keyboard.Zone2G = g;
                Current.Keyboard.Zone2B = b;
            }

            if (TryParseHexColor(config.Keyboard.Zone3Color, out var r3, out var g3, out var b3))
            {
                Current.Keyboard.Zone3R = r3;
                Current.Keyboard.Zone3G = g3;
                Current.Keyboard.Zone3B = b3;
            }
            else
            {
                Current.Keyboard.Zone3R = r;
                Current.Keyboard.Zone3G = g;
                Current.Keyboard.Zone3B = b;
            }

            if (TryParseHexColor(config.Keyboard.Zone4Color, out var r4, out var g4, out var b4))
            {
                Current.Keyboard.Zone4R = r4;
                Current.Keyboard.Zone4G = g4;
                Current.Keyboard.Zone4B = b4;
            }
            else
            {
                Current.Keyboard.Zone4R = r;
                Current.Keyboard.Zone4G = g;
                Current.Keyboard.Zone4B = b;
            }

            Current.Keyboard.Brightness = Math.Clamp(config.Keyboard.Brightness, 0, 100);
            Current.Keyboard.AnimationIndex = Math.Clamp(config.Keyboard.AnimationMode, 0, 255);

            if (string.Equals(config.Fan.Profile, "custom", StringComparison.OrdinalIgnoreCase)
                && config.Fan.Curve.Enabled
                && config.Fan.Curve.Points.Count >= 2)
            {
                Current.Fan.ActiveFanProfile = "manual";
                Current.Fan.IsCustomCurveEnabled = true;
                Current.Fan.CurveHysteresis = Math.Clamp(config.Fan.Curve.Hysteresis, 1, 10);
                Current.Fan.CurveRampUpDelay = Math.Clamp(config.Fan.Curve.RampUpDelaySeconds, 0, 30);
                Current.Fan.CurveRampDownDelay = Math.Clamp(config.Fan.Curve.RampDownDelaySeconds, 0, 30);
                Current.Fan.CpuCurve = config.Fan.Curve.Points
                    .OrderBy(p => p.Temp)
                    .Select(p => new SavedFanCurvePoint { Temperature = p.Temp, FanSpeed = p.Speed })
                    .ToList();
                Current.Fan.SelectedPreset = "Balanced";
            }
        }
        catch
        {
            // Ignore migration failures and keep defaults.
        }
    }

    private static bool TryParseHexColor(string? input, out int r, out int g, out int b)
    {
        r = g = b = 0;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var value = input.Trim().TrimStart('#');
        if (value.Length != 6 || !int.TryParse(value, System.Globalization.NumberStyles.HexNumber, null, out var rgb))
            return false;

        r = (rgb >> 16) & 0xFF;
        g = (rgb >> 8) & 0xFF;
        b = rgb & 0xFF;
        return true;
    }

    public Task SaveAsync()
    {
        UserPreferencesStore.SaveToAllLocations(Current);
        return Task.CompletedTask;
    }

    public async Task SaveAndSyncDaemonConfigAsync()
    {
        UserPreferencesStore.SyncAll(Current);
        await Task.CompletedTask;
    }
}
