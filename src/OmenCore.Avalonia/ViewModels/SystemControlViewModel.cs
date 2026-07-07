using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmenCore.Avalonia.Services;
using OmenCore.Linux.Daemon;

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
    /// Raised when the user changes a zone color (not during programmatic restore).
    /// </summary>
    public Action? ColorChanged;

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
            ColorChanged?.Invoke();
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

    partial void OnRChanged(int value) => NotifyColorChanged();
    partial void OnGChanged(int value) => NotifyColorChanged();
    partial void OnBChanged(int value) => NotifyColorChanged();

    private void NotifyColorChanged()
    {
        if (!_suppressColorSync)
            ColorChanged?.Invoke();
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
    private readonly IUserPreferencesService _preferences;
    private bool _suppressPerformanceModeSelectionChange;
    private bool _isRestoringPreferences;
    private string _performanceProfileReason = "Performance mode control is unavailable on this Linux board/kernel path.";
    private bool _canSetKeyboardBrightness = true;
    private string _keyboardBrightnessReason = "Keyboard brightness control is unavailable on this Linux board/kernel path.";
    private bool _suppressKeyboardAnimationSelectionChange;

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

    [ObservableProperty]
    private bool _canSetKeyboardAnimation;

    [ObservableProperty]
    private int _selectedKeyboardAnimationIndex;

    [ObservableProperty]
    private string _currentKeyboardAnimation = "Static";

    // Backlight idle timeout (BIOS-style). Index maps into KeyboardTimeoutSeconds.
    [ObservableProperty]
    private int _selectedKeyboardTimeoutIndex;

    // NVIDIA Dynamic Boost — live GPU power readout
    [ObservableProperty]
    private bool _hasGpuPower;

    [ObservableProperty]
    private bool _gpuSuspended;

    [ObservableProperty]
    private double _gpuPowerDraw;

    [ObservableProperty]
    private double _gpuPowerLimit;

    [ObservableProperty]
    private double _gpuPowerDefaultLimit;

    [ObservableProperty]
    private double _gpuPowerMaxLimit;

    [ObservableProperty]
    private bool _dynamicBoostActive;

    [ObservableProperty]
    private string _dynamicBoostStatus = "Checking…";

    // NVIDIA Settings launcher
    [ObservableProperty]
    private bool _hasNvidiaSettings;

    // Status
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public string[] PerformanceModes { get; } = { "Default", "Balanced", "Performance", "Cool" };
    public string[] GpuModes { get; } = { "Hybrid", "Discrete", "Integrated" };
    public string[] KeyboardAnimations { get; } = { "Static", "Breathing", "Wave", "Spectrum", "Off" };

    /// <summary>
    /// BIOS-style backlight idle timeout options. The daemon turns the backlight off after
    /// this much inactivity and restores it on the next keypress. Index-aligned with
    /// <see cref="KeyboardTimeoutSeconds"/>.
    /// </summary>
    public string[] KeyboardTimeouts { get; } = { "Never", "5 seconds", "15 seconds", "30 seconds", "1 minute", "5 minutes" };
    private static readonly int[] KeyboardTimeoutSeconds = { 0, 5, 15, 30, 60, 300 };

    /// <summary>
    /// Per-zone base colors. Brightness scaling is applied on write so
    /// these always store the full-brightness target, not the hardware value.
    /// Physical mapping on the OMEN 4-zone keyboard (left to right):
    /// Zone 3 (left edge) | Zone 4 (WASD) | Zone 2 (middle) | Zone 1 (right + numpad).
    /// </summary>
    public ZoneColorViewModel[] ZoneColors { get; } =
    {
        new("Zone 1 — Right / Numpad"),
        new("Zone 2 — Middle"),
        new("Zone 3 — Left Edge"),
        new("Zone 4 — WASD"),
    };

    private readonly DispatcherTimer _gpuPowerTimer;

    public SystemControlViewModel(IHardwareService hardwareService, IUserPreferencesService preferences)
    {
        _hardwareService = hardwareService;
        _preferences = preferences;
        foreach (var zone in ZoneColors)
            zone.ColorChanged = () => OnZoneColorChanged();
        _gpuPowerTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _gpuPowerTimer.Tick += async (_, _) => await RefreshGpuPowerAsync();
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
            CanSetKeyboardAnimation = capabilities.SupportsKeyboardAnimation;

            if (CanSetKeyboardAnimation)
            {
                var animationMode = await _hardwareService.GetKeyboardAnimationModeAsync();
                UpdateKeyboardAnimationSelection(animationMode);
            }

            if (!CanSetPerformanceMode)
            {
                StatusMessage = _performanceProfileReason;
            }

            var mode = await _hardwareService.GetPerformanceModeAsync();
            SetSelectedPerformanceModeIndex(mode);
            CurrentPerformanceMode = GetPerformanceModeName(mode);

            CurrentGpuMode = await _hardwareService.GetGpuModeAsync();

            HasNvidiaSettings = capabilities.HasNvidiaSettings;

            await RefreshGpuPowerAsync();
            if (HasGpuPower)
                _gpuPowerTimer.Start();

            _ = ScheduleRestoreKeyboardPreferencesAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Initialization error: {ex.Message}";
        }
    }

    private async Task RefreshGpuPowerAsync()
    {
        try
        {
            var info = await _hardwareService.GetGpuPowerAsync();
            if (info == null)
            {
                HasGpuPower = false;
                _gpuPowerTimer.Stop();
                return;
            }

            HasGpuPower = true;
            GpuSuspended = info.Suspended;

            if (info.Suspended)
            {
                DynamicBoostActive = false;
                GpuPowerDraw = 0;
                DynamicBoostStatus = "dGPU suspended (power saving)";
                return;
            }

            GpuPowerDraw = info.DrawWatts;
            GpuPowerLimit = info.LimitWatts;
            GpuPowerDefaultLimit = info.DefaultLimitWatts;
            GpuPowerMaxLimit = info.MaxLimitWatts;
            DynamicBoostActive = info.DynamicBoostActive;
            DynamicBoostStatus = info.DynamicBoostActive
                ? $"Active · +{Math.Max(0, info.LimitWatts - info.DefaultLimitWatts):0} W over base"
                : "Idle · at base TGP";
        }
        catch
        {
            // Telemetry only — keep the last-known values on a transient failure.
        }
    }

    private void OnZoneColorChanged()
    {
        if (_isRestoringPreferences)
            return;

        _ = ApplyLightingAsync(resetAnimationToStatic: true);
    }

    private Task ScheduleRestoreKeyboardPreferencesAsync()
    {
        return Task.Run(async () =>
        {
            try
            {
                await Task.Delay(750);
                await RestoreKeyboardPreferencesAsync();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to restore keyboard lighting: {ex.Message}";
            }
        });
    }

    private async Task RestoreKeyboardPreferencesAsync()
    {
        var kb = _preferences.Current.Keyboard;
        var daemonActive = DaemonRuntime.IsServiceActive();
        _isRestoringPreferences = true;
        try
        {
            ZoneColors[0].SetColor((byte)kb.Zone1R, (byte)kb.Zone1G, (byte)kb.Zone1B);
            ZoneColors[1].SetColor((byte)kb.Zone2R, (byte)kb.Zone2G, (byte)kb.Zone2B);
            ZoneColors[2].SetColor((byte)kb.Zone3R, (byte)kb.Zone3G, (byte)kb.Zone3B);
            ZoneColors[3].SetColor((byte)kb.Zone4R, (byte)kb.Zone4G, (byte)kb.Zone4B);
            KeyboardBrightness = Math.Clamp(kb.Brightness, 0, 100);
            UpdateKeyboardAnimationSelection(kb.AnimationIndex);
            SelectedKeyboardTimeoutIndex = TimeoutIndexForSeconds(kb.BacklightTimeoutSeconds);

            // Daemon already applied the saved palette/animation at boot — only push
            // to hardware when it is not running (standalone GUI session).
            if (daemonActive || (!HasFourZoneRgb && !HasKeyboardBacklight))
                return;

            await ApplyLightingAsync();

            if (kb.AnimationIndex > 0 && CanSetKeyboardAnimation)
                await ApplyKeyboardAnimationAsync(AnimationUiIndexForMode(kb.AnimationIndex));
        }
        finally
        {
            _isRestoringPreferences = false;
        }
    }

    private async Task PersistKeyboardPreferencesAsync()
    {
        if (_isRestoringPreferences)
            return;

        var kb = _preferences.Current.Keyboard;
        kb.Brightness = KeyboardBrightness;
        kb.AnimationIndex = SelectedKeyboardAnimationIndex;
        kb.BacklightTimeoutSeconds = SecondsForTimeoutIndex(SelectedKeyboardTimeoutIndex);
        kb.Zone1R = ZoneColors[0].R;
        kb.Zone1G = ZoneColors[0].G;
        kb.Zone1B = ZoneColors[0].B;
        kb.Zone2R = ZoneColors[1].R;
        kb.Zone2G = ZoneColors[1].G;
        kb.Zone2B = ZoneColors[1].B;
        kb.Zone3R = ZoneColors[2].R;
        kb.Zone3G = ZoneColors[2].G;
        kb.Zone3B = ZoneColors[2].B;
        kb.Zone4R = ZoneColors[3].R;
        kb.Zone4G = ZoneColors[3].G;
        kb.Zone4B = ZoneColors[3].B;

        await _preferences.SaveAndSyncDaemonConfigAsync();
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

    partial void OnSelectedKeyboardTimeoutIndexChanged(int value)
    {
        if (_isRestoringPreferences)
            return;

        // No immediate hardware effect — the daemon enforces the timeout and reads it at
        // startup, so persist the choice and tell the user to restart the daemon.
        _ = PersistKeyboardTimeoutAsync();
    }

    private async Task PersistKeyboardTimeoutAsync()
    {
        try
        {
            await PersistKeyboardPreferencesAsync();
            StatusMessage = SecondsForTimeoutIndex(SelectedKeyboardTimeoutIndex) == 0
                ? "Backlight timeout: Never (restart the daemon to apply)"
                : $"Backlight timeout: {KeyboardTimeouts[SelectedKeyboardTimeoutIndex]} (restart the daemon to apply)";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to save backlight timeout: {ex.Message}";
        }
    }

    private static int TimeoutIndexForSeconds(int seconds)
    {
        var index = Array.IndexOf(KeyboardTimeoutSeconds, seconds);
        return index >= 0 ? index : 0;
    }

    private static int SecondsForTimeoutIndex(int index)
    {
        return index >= 0 && index < KeyboardTimeoutSeconds.Length ? KeyboardTimeoutSeconds[index] : 0;
    }

    partial void OnSelectedKeyboardAnimationIndexChanged(int value)
    {
        if (_suppressKeyboardAnimationSelectionChange || !CanSetKeyboardAnimation)
            return;

        _ = ApplyKeyboardAnimationAsync(value);
    }

    /// <summary>
    /// Unified lighting write — always composites zone base colors with current brightness.
    /// This fixes the previous bug where SetAllZonesColor (full brightness) and
    /// SetBrightness (read-and-scale in sysfs) composed incorrectly.
    /// </summary>
    private async Task ApplyLightingAsync(bool resetAnimationToStatic = false)
    {
        if (resetAnimationToStatic && CanSetKeyboardAnimation && SelectedKeyboardAnimationIndex != 0)
        {
            try
            {
                await _hardwareService.SetKeyboardAnimationModeAsync(0);
            }
            catch
            {
                // Best effort — sysfs colors are still applied below.
            }

            UpdateKeyboardAnimationSelection(0);
        }

        double factor = Math.Clamp(KeyboardBrightness, 0, 100) / 100.0;
        var directApplySucceeded = false;

        if (HasFourZoneRgb)
        {
            var failedZones = 0;
            for (int i = 0; i < ZoneColors.Length; i++)
            {
                var zone = ZoneColors[i];
                byte r = (byte)(Math.Clamp(zone.R, 0, 255) * factor);
                byte g = (byte)(Math.Clamp(zone.G, 0, 255) * factor);
                byte b = (byte)(Math.Clamp(zone.B, 0, 255) * factor);
                try
                {
                    await _hardwareService.SetKeyboardZoneColorAsync(i, r, g, b);
                    directApplySucceeded = true;
                }
                catch
                {
                    failedZones++;
                }
            }

            if (failedZones == ZoneColors.Length)
            {
                StatusMessage = "Saved lighting — the daemon will apply it shortly (no direct sysfs access).";
            }
            else if (failedZones > 0)
            {
                StatusMessage = "Partially applied lighting; saved remaining settings for the daemon.";
            }
        }
        else if (_canSetKeyboardBrightness)
        {
            try
            {
                await _hardwareService.SetKeyboardBrightnessAsync(KeyboardBrightness);
                directApplySucceeded = true;
            }
            catch
            {
                StatusMessage = "Saved brightness — the daemon will apply it shortly.";
            }
        }

        await PersistKeyboardPreferencesAsync();

        if (directApplySucceeded && string.IsNullOrWhiteSpace(StatusMessage))
            StatusMessage = "Keyboard lighting applied";
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
    private async Task SetKeyboardAnimation()
    {
        if (!CanSetKeyboardAnimation)
        {
            StatusMessage = "Keyboard animation control is unavailable on this kernel/board.";
            return;
        }

        await ApplyKeyboardAnimationAsync(SelectedKeyboardAnimationIndex);
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

        _ = ApplyLightingAsync(resetAnimationToStatic: true);
    }

    private async Task ApplyKeyboardAnimationAsync(int index)
    {
        if (!CanSetKeyboardAnimation)
            return;

        try
        {
            var mode = index switch
            {
                0 => 0, // static
                1 => 1, // breathing
                2 => 2, // wave
                3 => 3, // spectrum
                4 => 255, // off (best effort mapping)
                _ => 0
            };

            await _hardwareService.SetKeyboardAnimationModeAsync(mode);
            CurrentKeyboardAnimation = KeyboardAnimations[Math.Clamp(index, 0, KeyboardAnimations.Length - 1)];
            StatusMessage = $"Keyboard animation set to {CurrentKeyboardAnimation}";
            await PersistKeyboardPreferencesAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Animation error: {ex.Message}";
        }
    }

    private static int AnimationUiIndexForMode(int mode) => mode switch
    {
        1 => 1,
        2 => 2,
        3 => 3,
        255 => 4,
        _ => 0,
    };

    private void UpdateKeyboardAnimationSelection(int mode)
    {
        var index = mode switch
        {
            1 => 1,
            2 => 2,
            3 => 3,
            255 => 4,
            _ => 0
        };

        _suppressKeyboardAnimationSelectionChange = true;
        try
        {
            SelectedKeyboardAnimationIndex = index;
            CurrentKeyboardAnimation = KeyboardAnimations[index];
        }
        finally
        {
            _suppressKeyboardAnimationSelectionChange = false;
        }
    }

    private static bool TryParsePerformanceModeName(string modeName, out PerformanceMode mode)
    {
        switch (modeName.Trim())
        {
            case "Default":
                mode = PerformanceMode.Default;
                return true;
            case "Balanced":
                mode = PerformanceMode.Balanced;
                return true;
            case "Performance":
                mode = PerformanceMode.Performance;
                return true;
            case "Cool":
                mode = PerformanceMode.Cool;
                return true;
            default:
                mode = PerformanceMode.Default;
                return false;
        }
    }

    private static bool TryGetPerformanceModeFromIndex(int index, out PerformanceMode mode)
    {
        switch (index)
        {
            case 0:
                mode = PerformanceMode.Default;
                return true;
            case 1:
                mode = PerformanceMode.Balanced;
                return true;
            case 2:
                mode = PerformanceMode.Performance;
                return true;
            case 3:
                mode = PerformanceMode.Cool;
                return true;
            default:
                mode = PerformanceMode.Default;
                return false;
        }
    }

    private static int GetPerformanceModeIndex(PerformanceMode mode)
    {
        return mode switch
        {
            PerformanceMode.Default => 0,
            PerformanceMode.Balanced => 1,
            PerformanceMode.Performance => 2,
            PerformanceMode.Cool => 3,
            _ => 1
        };
    }

    private static string GetPerformanceModeName(PerformanceMode mode)
    {
        return mode switch
        {
            PerformanceMode.Default => "Default",
            PerformanceMode.Balanced => "Balanced",
            PerformanceMode.Performance => "Performance",
            PerformanceMode.Cool => "Cool",
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

    [RelayCommand]
    private void LaunchNvidiaSettings()
    {
        try
        {
            var candidates = new[] { "/usr/bin/nvidia-settings", "/usr/local/bin/nvidia-settings" };
            var binary = candidates.FirstOrDefault(File.Exists);
            if (binary == null)
            {
                StatusMessage = "nvidia-settings is not installed.";
                return;
            }

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = binary,
                UseShellExecute = false
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to launch nvidia-settings: {ex.Message}";
        }
    }
}
