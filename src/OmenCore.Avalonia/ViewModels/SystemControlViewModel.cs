using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmenCore.Avalonia.Services;

namespace OmenCore.Avalonia.ViewModels;

/// <summary>
/// Per-zone color state. Exposes a Color property for ColorPicker binding
/// and R/G/B ints for manual input fallback.
/// </summary>
public partial class ZoneColorViewModel : ObservableObject
{
    private bool _suppressColorSync;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Color))]
    [NotifyPropertyChangedFor(nameof(PreviewBrush))]
    private int _r;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Color))]
    [NotifyPropertyChangedFor(nameof(PreviewBrush))]
    private int _g;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Color))]
    [NotifyPropertyChangedFor(nameof(PreviewBrush))]
    private int _b;

    public string Name { get; }

    /// <summary>
    /// Avalonia.Media.Color bridging R/G/B — binds directly to ColorPicker.
    /// </summary>
    public Color Color
    {
        get => Color.FromRgb((byte)Math.Clamp(R, 0, 255), (byte)Math.Clamp(G, 0, 255), (byte)Math.Clamp(B, 0, 255));
        set
        {
            if (_suppressColorSync) return;
            _suppressColorSync = true;
            R = value.R;
            G = value.G;
            B = value.B;
            _suppressColorSync = false;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PreviewBrush));
        }
    }

    public IBrush PreviewBrush => new SolidColorBrush(Color);

    public ZoneColorViewModel(string name, byte r = 0, byte g = 191, byte b = 255)
    {
        Name = name;
        _r = r;
        _g = g;
        _b = b;
    }

    public void SetColor(byte r, byte g, byte b)
    {
        _suppressColorSync = true;
        R = r;
        G = g;
        B = b;
        _suppressColorSync = false;
        OnPropertyChanged(nameof(Color));
        OnPropertyChanged(nameof(PreviewBrush));
    }
}

/// <summary>
/// System control ViewModel for performance modes, GPU switching, and keyboard lighting.
/// </summary>
public partial class SystemControlViewModel : ObservableObject
{
    private readonly IHardwareService _hardwareService;
    private bool _suppressPerformanceModeSelectionChange;
    private string _performanceProfileReason = "Performance mode control is unavailable on this Linux board/kernel path.";
    private bool _canSetKeyboardBrightness = true;
    private string _keyboardBrightnessReason = "Keyboard brightness control is unavailable on this Linux board/kernel path.";

    // Performance Mode
    [ObservableProperty]
    private int _selectedPerformanceModeIndex;

    [ObservableProperty]
    private string _currentPerformanceMode = "Balanced";

    [ObservableProperty]
    private bool _isPerformanceModeChanging;

    [ObservableProperty]
    private bool _canSetPerformanceMode = true;

    // GPU Mode
    [ObservableProperty]
    private string _currentGpuMode = "hybrid";

    [ObservableProperty]
    private bool _isGpuModeChanging;

    [ObservableProperty]
    private bool _hasGpuMuxSwitch;

    // Keyboard Lighting
    [ObservableProperty]
    private bool _hasKeyboardBacklight;

    [ObservableProperty]
    private int _keyboardBrightness = 100;

    [ObservableProperty]
    private bool _hasFourZoneRgb;

    // Status
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public string[] PerformanceModes { get; } = { "Quiet", "Balanced", "Performance" };
    public string[] GpuModes { get; } = { "Hybrid", "Discrete", "Integrated" };

    /// <summary>
    /// Per-zone base colors. Brightness scaling is applied on write so
    /// these always store the full-brightness target, not the hardware value.
    /// </summary>
    public ZoneColorViewModel[] ZoneColors { get; } =
    {
        new("Zone 1"),
        new("Zone 2"),
        new("Zone 3"),
        new("Zone 4"),
    };

    public SystemControlViewModel(IHardwareService hardwareService)
    {
        _hardwareService = hardwareService;
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            var capabilities = await _hardwareService.GetCapabilitiesAsync();
            HasKeyboardBacklight = capabilities.HasKeyboardBacklight;
            HasFourZoneRgb = capabilities.HasFourZoneRgb;
            HasGpuMuxSwitch = capabilities.HasGpuMuxSwitch;
            CanSetPerformanceMode = capabilities.SupportsPerformanceProfiles;
            _performanceProfileReason = string.IsNullOrWhiteSpace(capabilities.PerformanceProfileReason)
                ? "Performance mode control is unavailable on this Linux board/kernel path."
                : capabilities.PerformanceProfileReason;

            _canSetKeyboardBrightness = capabilities.SupportsKeyboardBrightness;
            _keyboardBrightnessReason = string.IsNullOrWhiteSpace(capabilities.KeyboardBrightnessReason)
                ? "Keyboard brightness control is unavailable on this Linux board/kernel path."
                : capabilities.KeyboardBrightnessReason;

            if (!CanSetPerformanceMode)
            {
                StatusMessage = _performanceProfileReason;
            }

            var mode = await _hardwareService.GetPerformanceModeAsync();
            SetSelectedPerformanceModeIndex(mode);
            CurrentPerformanceMode = GetPerformanceModeName(mode);

            CurrentGpuMode = await _hardwareService.GetGpuModeAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Initialization error: {ex.Message}";
        }
    }

    partial void OnSelectedPerformanceModeIndexChanged(int value)
    {
        if (_suppressPerformanceModeSelectionChange)
        {
            return;
        }

        if (!TryGetPerformanceModeFromIndex(value, out var mode))
        {
            StatusMessage = $"Unknown performance mode index: {value}";
            return;
        }

        _ = SetPerformanceModeByIndexAsync(mode);
    }

    [RelayCommand]
    private async Task SetPerformanceMode(string modeName)
    {
        if (IsPerformanceModeChanging)
            return;

        if (!TryParsePerformanceModeName(modeName, out var mode))
        {
            StatusMessage = $"Unsupported performance mode: {modeName}";
            return;
        }

        await SetPerformanceModeByIndexAsync(mode);
    }

    private async Task SetPerformanceModeByIndexAsync(PerformanceMode mode)
    {
        if (IsPerformanceModeChanging)
            return;

        if (!CanSetPerformanceMode)
        {
            StatusMessage = _performanceProfileReason;
            return;
        }

        try
        {
            IsPerformanceModeChanging = true;
            StatusMessage = $"Setting performance mode to {mode}...";
            await _hardwareService.SetPerformanceModeAsync(mode);
            CurrentPerformanceMode = GetPerformanceModeName(mode);
            SetSelectedPerformanceModeIndex(mode);
            StatusMessage = $"Performance mode set to {mode}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed: {ex.Message}";
        }
        finally
        {
            IsPerformanceModeChanging = false;
        }
    }

    [RelayCommand]
    private async Task SetGpuMode(string mode)
    {
        if (IsGpuModeChanging)
            return;

        try
        {
            IsGpuModeChanging = true;
            StatusMessage = $"Switching GPU to {mode} mode...";
            await _hardwareService.SetGpuModeAsync(mode);
            CurrentGpuMode = mode;
            StatusMessage = $"GPU mode changed to {mode}. A reboot may be required.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"GPU switch failed: {ex.Message}";
        }
        finally
        {
            IsGpuModeChanging = false;
        }
    }

    partial void OnKeyboardBrightnessChanged(int value)
    {
        _ = ApplyLightingAsync();
    }

    /// <summary>
    /// Unified lighting write — always composites zone base colors with current brightness.
    /// This fixes the previous bug where SetAllZonesColor (full brightness) and
    /// SetBrightness (read-and-scale in sysfs) composed incorrectly.
    /// </summary>
    private async Task ApplyLightingAsync()
    {
        double factor = Math.Clamp(KeyboardBrightness, 0, 100) / 100.0;

        if (HasFourZoneRgb)
        {
            for (int i = 0; i < ZoneColors.Length; i++)
            {
                var zone = ZoneColors[i];
                byte r = (byte)(Math.Clamp(zone.R, 0, 255) * factor);
                byte g = (byte)(Math.Clamp(zone.G, 0, 255) * factor);
                byte b = (byte)(Math.Clamp(zone.B, 0, 255) * factor);
                try
                {
                    await _hardwareService.SetKeyboardZoneColorAsync(i, r, g, b);
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Zone {i + 1} error: {ex.Message}";
                    return;
                }
            }
        }
        else if (_canSetKeyboardBrightness)
        {
            try
            {
                await _hardwareService.SetKeyboardBrightnessAsync(KeyboardBrightness);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Brightness error: {ex.Message}";
            }
        }
    }

    [RelayCommand]
    private async Task ApplyKeyboardColor()
    {
        try
        {
            await ApplyLightingAsync();
            StatusMessage = "Keyboard lighting applied";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Color error: {ex.Message}";
        }
    }

    [RelayCommand]
    private void SetPresetColor(string colorName)
    {
        (byte r, byte g, byte b) = colorName.ToLower() switch
        {
            "blue"   => ((byte)0,   (byte)191, (byte)255),
            "red"    => ((byte)227, (byte)24,  (byte)55),
            "green"  => ((byte)57,  (byte)255, (byte)20),
            "purple" => ((byte)157, (byte)78,  (byte)221),
            "orange" => ((byte)255, (byte)107, (byte)53),
            "white"  => ((byte)255, (byte)255, (byte)255),
            "cyan"   => ((byte)0,   (byte)255, (byte)255),
            "yellow" => ((byte)255, (byte)255, (byte)0),
            _        => ((byte)0,   (byte)191, (byte)255)
        };

        foreach (var zone in ZoneColors)
            zone.SetColor(r, g, b);

        _ = ApplyLightingAsync();
    }

    private static bool TryParsePerformanceModeName(string modeName, out PerformanceMode mode)
    {
        switch (modeName.Trim())
        {
            case "Quiet":
                mode = PerformanceMode.Quiet;
                return true;
            case "Balanced":
                mode = PerformanceMode.Balanced;
                return true;
            case "Performance":
                mode = PerformanceMode.Performance;
                return true;
            default:
                mode = PerformanceMode.Balanced;
                return false;
        }
    }

    private static bool TryGetPerformanceModeFromIndex(int index, out PerformanceMode mode)
    {
        switch (index)
        {
            case 0:
                mode = PerformanceMode.Quiet;
                return true;
            case 1:
                mode = PerformanceMode.Balanced;
                return true;
            case 2:
                mode = PerformanceMode.Performance;
                return true;
            default:
                mode = PerformanceMode.Balanced;
                return false;
        }
    }

    private static int GetPerformanceModeIndex(PerformanceMode mode)
    {
        return mode switch
        {
            PerformanceMode.Quiet => 0,
            PerformanceMode.Balanced => 1,
            PerformanceMode.Performance => 2,
            _ => 1
        };
    }

    private static string GetPerformanceModeName(PerformanceMode mode)
    {
        return mode switch
        {
            PerformanceMode.Quiet => "Quiet",
            PerformanceMode.Balanced => "Balanced",
            PerformanceMode.Performance => "Performance",
            _ => "Balanced"
        };
    }

    private void SetSelectedPerformanceModeIndex(PerformanceMode mode)
    {
        var index = GetPerformanceModeIndex(mode);
        if (SelectedPerformanceModeIndex == index)
        {
            return;
        }

        _suppressPerformanceModeSelectionChange = true;
        try
        {
            SelectedPerformanceModeIndex = index;
        }
        finally
        {
            _suppressPerformanceModeSelectionChange = false;
        }
    }
}
