namespace OmenCore.Linux.Config;

public sealed class UserHardwarePreferences
{
    public KeyboardLightingPreferences Keyboard { get; set; } = new();
    public FanControlPreferences Fan { get; set; } = new();
}

public sealed class KeyboardLightingPreferences
{
    public int Brightness { get; set; } = 100;
    public int AnimationIndex { get; set; }
    public int Zone1R { get; set; } = 0;
    public int Zone1G { get; set; } = 191;
    public int Zone1B { get; set; } = 255;
    public int Zone2R { get; set; } = 0;
    public int Zone2G { get; set; } = 191;
    public int Zone2B { get; set; } = 255;
    public int Zone3R { get; set; } = 0;
    public int Zone3G { get; set; } = 191;
    public int Zone3B { get; set; } = 255;
    public int Zone4R { get; set; } = 0;
    public int Zone4G { get; set; } = 191;
    public int Zone4B { get; set; } = 255;
}

public sealed class FanControlPreferences
{
    public string ActiveFanProfile { get; set; } = "auto";
    public bool IsCustomCurveEnabled { get; set; }
    public int ManualFanSpeed { get; set; } = 50;
    public int CurveHysteresis { get; set; } = 3;
    public double CurveRampUpDelay { get; set; } = 1.0;
    public double CurveRampDownDelay { get; set; } = 3.0;
    public string SelectedPreset { get; set; } = "Balanced";
    public List<SavedFanPreset> CustomPresets { get; set; } = new();
    public List<SavedFanCurvePoint> CpuCurve { get; set; } = new();
}

public sealed class SavedFanPreset
{
    public string Name { get; set; } = string.Empty;
    public List<SavedFanCurvePoint> Cpu { get; set; } = new();
    public List<SavedFanCurvePoint> Gpu { get; set; } = new();
}

public sealed class SavedFanCurvePoint
{
    public int Temperature { get; set; }
    public int FanSpeed { get; set; }
}
