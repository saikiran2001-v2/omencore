namespace OmenCore.Linux.Hardware;

/// <summary>
/// Linux HP WMI keyboard lighting controller.
/// 
/// Uses /sys/devices/platform/hp-wmi/* interface for controlling
/// the 4-zone RGB keyboard on HP OMEN laptops.
/// Per-key RGB models are detected but require USB HID protocol (not yet supported on Linux).
/// 
/// Requires hp-wmi kernel module:
///   modprobe hp-wmi
/// </summary>
public class LinuxKeyboardController
{
    private const string HP_WMI_PATH = "/sys/devices/platform/hp-wmi";
    private const string KEYBOARD_BACKLIGHT_PATH = "/sys/class/leds/hp::kbd_backlight";
    private const string DMI_PRODUCT_NAME_PATH = "/sys/class/dmi/id/product_name";
    private const string FOURZONE_COLOR_PATH_NAME = "fourzone_color";
    private const string FOURZONE_BRIGHTNESS_PATH_NAME = "fourzone_brightness";
    private const string FOURZONE_ANIMATION_PATH_NAME = "fourzone_animation";
    
    /// <summary>
    /// Model substrings known to have per-key RGB keyboards.
    /// Sourced from the Windows KeyboardModelDatabase.
    /// </summary>
    private static readonly string[] PerKeyModelPatterns = new[]
    {
        "16-wf0",     // OMEN 16 (2024) per-key
        "16-wf1",     // OMEN 16 (2025) per-key
        "16t-wf0",    // OMEN 16t (2024) per-key
        "16t-wf1",    // OMEN 16t (2025) per-key
        "17-wf0",     // OMEN 17 (2024) per-key
        "17-wf1",     // OMEN 17 (2025) per-key
        "17t-wf0",    // OMEN 17t (2024) per-key
        "16t-ah0",    // OMEN Max 16 (2025) per-key
        "16-ah0",     // OMEN Max 16 (2025) per-key
        "17t-ah0",    // OMEN Max 17 (2025) per-key
        "Transcend 14", // OMEN Transcend 14 per-key
        "Transcend 16", // OMEN Transcend 16 per-key
    };
    
    public bool IsAvailable { get; }
    public bool HasZoneControl { get; }
    public bool HasFourZoneControl { get; }
    public bool IsPerKeyRgb { get; }
    public bool HasFourZoneBrightnessControl { get; }
    public bool HasFourZoneAnimationControl { get; }
    public bool SupportsBrightnessControl => HasFourZoneBrightnessControl || HasFourZoneControl || File.Exists(Path.Combine(KEYBOARD_BACKLIGHT_PATH, "brightness"));
    public string KeyboardType => IsPerKeyRgb ? "Per-Key RGB" : "4-Zone";
    public int ZoneCount => IsPerKeyRgb ? 0 : 4;

    private string FourZoneColorPath => Path.Combine(HP_WMI_PATH, FOURZONE_COLOR_PATH_NAME);
    private string FourZoneBrightnessPath => Path.Combine(HP_WMI_PATH, FOURZONE_BRIGHTNESS_PATH_NAME);
    private string FourZoneAnimationPath => Path.Combine(HP_WMI_PATH, FOURZONE_ANIMATION_PATH_NAME);

    public LinuxKeyboardController()
    {
        HasFourZoneControl = File.Exists(Path.Combine(HP_WMI_PATH, FOURZONE_COLOR_PATH_NAME));
        HasFourZoneBrightnessControl = File.Exists(Path.Combine(HP_WMI_PATH, FOURZONE_BRIGHTNESS_PATH_NAME));
        HasFourZoneAnimationControl = File.Exists(Path.Combine(HP_WMI_PATH, FOURZONE_ANIMATION_PATH_NAME));
        IsAvailable = Directory.Exists(HP_WMI_PATH) || Directory.Exists(KEYBOARD_BACKLIGHT_PATH) ||
            HasFourZoneControl || HasFourZoneBrightnessControl || HasFourZoneAnimationControl;
        HasZoneControl = File.Exists(Path.Combine(HP_WMI_PATH, "keyboard_zones"));
        IsPerKeyRgb = DetectPerKeyRgb();
    }
    
    /// <summary>
    /// Detect if this model has a per-key RGB keyboard based on DMI product name.
    /// </summary>
    private static bool DetectPerKeyRgb()
    {
        try
        {
            if (!File.Exists(DMI_PRODUCT_NAME_PATH))
                return false;
                
            var productName = File.ReadAllText(DMI_PRODUCT_NAME_PATH).Trim();
            foreach (var pattern in PerKeyModelPatterns)
            {
                if (productName.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch { }
        return false;
    }
    
    /// <summary>
    /// Set color for a specific zone (0-3).
    /// </summary>
    public bool SetZoneColor(int zone, byte r, byte g, byte b)
    {
        if (!IsAvailable || zone < 0 || zone > 3)
            return false;

        try
        {
            // fourzone_color: single file with all 4 zones as RRGGBBRRGGBBRRGGBBRRGGBB
            if (HasFourZoneControl)
            {
                var current = File.ReadAllText(FourZoneColorPath).Trim();
                // Pad/truncate to exactly 24 chars (4 zones × 6 hex chars)
                if (current.Length < 24)
                    current = current.PadRight(24, '0');
                var chars = current.ToCharArray();
                var hex = $"{r:x2}{g:x2}{b:x2}";
                hex.CopyTo(0, chars, zone * 6, 6);
                File.WriteAllText(FourZoneColorPath, new string(chars));
                return true;
            }

            // Legacy: per-zone files (keyboard_zones + zone{n}_color)
            if (HasZoneControl)
            {
                var zonePath = Path.Combine(HP_WMI_PATH, $"zone{zone}_color");
                if (File.Exists(zonePath))
                {
                    File.WriteAllText(zonePath, $"{r:X2}{g:X2}{b:X2}");
                    return true;
                }
            }

            // Fallback: brightness-only control
            var brightnessPath = Path.Combine(KEYBOARD_BACKLIGHT_PATH, "brightness");
            if (File.Exists(brightnessPath))
            {
                File.WriteAllText(brightnessPath, ((r + g + b) / 3).ToString());
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Set all 4 zones to individual colors in a single sysfs write (fourzone only).
    /// </summary>
    public bool SetAllZoneColors(byte r0, byte g0, byte b0,
                                  byte r1, byte g1, byte b1,
                                  byte r2, byte g2, byte b2,
                                  byte r3, byte g3, byte b3)
    {
        if (!HasFourZoneControl) return false;
        try
        {
            var hex = $"{r0:x2}{g0:x2}{b0:x2}{r1:x2}{g1:x2}{b1:x2}{r2:x2}{g2:x2}{b2:x2}{r3:x2}{g3:x2}{b3:x2}";
            File.WriteAllText(FourZoneColorPath, hex);
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Set the same color for all zones.
    /// </summary>
    public bool SetAllZonesColor(byte r, byte g, byte b)
    {
        if (!IsAvailable)
            return false;

        // fourzone_color: write all 4 zones in one atomic write
        if (HasFourZoneControl)
        {
            try
            {
                var hex = $"{r:x2}{g:x2}{b:x2}";
                File.WriteAllText(FourZoneColorPath, string.Concat(hex, hex, hex, hex));
                return true;
            }
            catch
            {
                return false;
            }
        }

        // Legacy: per-zone files
        bool anySuccess = false;
        for (int i = 0; i < 4; i++)
        {
            if (SetZoneColor(i, r, g, b))
                anySuccess = true;
        }

        if (!anySuccess)
            return SetBrightness((r + g + b) / 3 * 100 / 255);

        return anySuccess;
    }
    
    /// <summary>
    /// Set keyboard backlight brightness (0-100) by scaling the current fourzone_color values.
    /// </summary>
    public bool SetBrightness(int percent)
    {
        if (!IsAvailable)
            return false;

        try
        {
            if (HasFourZoneBrightnessControl)
            {
                var raw = Math.Clamp((int)Math.Round(Math.Clamp(percent, 0, 100) * 255.0 / 100.0), 0, 255);
                File.WriteAllText(FourZoneBrightnessPath, raw.ToString());
                return true;
            }

            // Legacy fallback when dedicated fourzone brightness node is unavailable:
            // scale each zone's RGB channels directly in fourzone_color.
            if (HasFourZoneControl)
            {
                var current = File.ReadAllText(FourZoneColorPath).Trim();
                if (current.Length < 24)
                    current = current.PadRight(24, '0');

                double factor = Math.Clamp(percent, 0, 100) / 100.0;
                var result = new System.Text.StringBuilder(24);
                for (int i = 0; i < 4; i++)
                {
                    int r = Convert.ToInt32(current.Substring(i * 6 + 0, 2), 16);
                    int g = Convert.ToInt32(current.Substring(i * 6 + 2, 2), 16);
                    int b = Convert.ToInt32(current.Substring(i * 6 + 4, 2), 16);
                    result.Append($"{(int)(r * factor):x2}{(int)(g * factor):x2}{(int)(b * factor):x2}");
                }
                File.WriteAllText(FourZoneColorPath, result.ToString());
                return true;
            }

            var brightnessPath = Path.Combine(KEYBOARD_BACKLIGHT_PATH, "brightness");
            var maxBrightnessPath = Path.Combine(KEYBOARD_BACKLIGHT_PATH, "max_brightness");

            if (!File.Exists(brightnessPath))
                return false;

            int maxBrightness = 3;
            if (File.Exists(maxBrightnessPath))
            {
                var maxContent = File.ReadAllText(maxBrightnessPath).Trim();
                int.TryParse(maxContent, out maxBrightness);
                if (maxBrightness == 0) maxBrightness = 3;
            }

            File.WriteAllText(brightnessPath, Math.Clamp(percent * maxBrightness / 100, 0, maxBrightness).ToString());
            return true;
        }
        catch
        {
            return false;
        }
    }

    public string GetBrightnessUnavailableReason()
    {
        if (!IsAvailable)
            return "HP WMI keyboard interface is not available.";

        if (!HasFourZoneBrightnessControl && !HasFourZoneControl && !File.Exists(Path.Combine(KEYBOARD_BACKLIGHT_PATH, "brightness")))
            return $"No supported brightness interface found (checked fourzone_brightness, fourzone_color and {KEYBOARD_BACKLIGHT_PATH})";

        return "Unknown keyboard brightness error.";
    }
    
    /// <summary>
    /// Turn off keyboard lighting.
    /// </summary>
    public bool TurnOff()
    {
        return SetBrightness(0);
    }
    
    /// <summary>
    /// Get current brightness level (0-100).
    /// </summary>
    public int GetBrightness()
    {
        try
        {
            if (HasFourZoneBrightnessControl)
            {
                var rawText = File.ReadAllText(FourZoneBrightnessPath).Trim();
                if (int.TryParse(rawText, out var raw))
                {
                    return Math.Clamp((int)Math.Round(raw * 100.0 / 255.0), 0, 100);
                }
            }

            var brightnessPath = Path.Combine(KEYBOARD_BACKLIGHT_PATH, "brightness");
            var maxBrightnessPath = Path.Combine(KEYBOARD_BACKLIGHT_PATH, "max_brightness");
            
            if (!File.Exists(brightnessPath))
                return 0;
                
            var content = File.ReadAllText(brightnessPath).Trim();
            if (!int.TryParse(content, out var brightness))
                return 0;
                
            int maxBrightness = 3;
            if (File.Exists(maxBrightnessPath))
            {
                var maxContent = File.ReadAllText(maxBrightnessPath).Trim();
                int.TryParse(maxContent, out maxBrightness);
                if (maxBrightness == 0) maxBrightness = 3;
            }
            
            return brightness * 100 / maxBrightness;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Set four-zone animation mode as firmware enum value (0-255).
    /// Requires the fourzone_animation sysfs node.
    /// </summary>
    public bool SetAnimationMode(byte mode)
    {
        if (!HasFourZoneAnimationControl)
            return false;

        try
        {
            var before = GetAnimationMode();
            File.WriteAllText(FourZoneAnimationPath, mode.ToString());
            var after = GetAnimationMode();

            // Accept success only when readback reflects the requested value.
            // Some firmware accepts the write syscall but ignores unsupported modes.
            if (after == mode)
                return true;

            // If the mode was already set, treat as success.
            return before == mode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Read current four-zone animation mode as firmware enum value.
    /// Returns -1 when unsupported or unreadable.
    /// </summary>
    public int GetAnimationMode()
    {
        if (!HasFourZoneAnimationControl)
            return -1;

        try
        {
            var text = File.ReadAllText(FourZoneAnimationPath).Trim();
            return int.TryParse(text, out var mode) ? mode : -1;
        }
        catch
        {
            return -1;
        }
    }
}
