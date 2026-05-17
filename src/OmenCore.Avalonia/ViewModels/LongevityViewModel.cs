using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmenCore.Avalonia.Services;

namespace OmenCore.Avalonia.ViewModels;

/// <summary>
/// Battery health and longevity tuning — charge limit, CPU power cap, thermal presets.
/// </summary>
public partial class LongevityViewModel : ObservableObject
{
    private readonly IHardwareService _hardwareService;

    // Battery health
    [ObservableProperty] private double _batteryHealthPercent = 100;
    [ObservableProperty] private int _batteryCycleCount;
    [ObservableProperty] private string _batteryHealthLabel = "Loading...";

    // Charge limit
    [ObservableProperty] private bool _supportsChargeLimit;
    [ObservableProperty] private int _chargeEndThreshold = 80;

    // CPU power limit (RAPL PL1)
    [ObservableProperty] private bool _supportsCpuPowerLimit;
    [ObservableProperty] private int _cpuPowerLimitWatts = 28;
    [ObservableProperty] private int _cpuPowerLimitMin = 10;
    [ObservableProperty] private int _cpuPowerLimitMax = 64;

    // Status
    [ObservableProperty] private string _statusMessage = string.Empty;

    public LongevityViewModel(IHardwareService hardwareService)
    {
        _hardwareService = hardwareService;
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            var caps = await _hardwareService.GetCapabilitiesAsync();
            SupportsCpuPowerLimit = caps.SupportsCpuPowerLimit;
            SupportsChargeLimit = caps.SupportsBatteryChargeLimit;

            var (health, cycles) = await _hardwareService.GetBatteryHealthAsync();
            BatteryHealthPercent = health;
            BatteryCycleCount = cycles;
            BatteryHealthLabel = health >= 90 ? "Excellent" :
                                 health >= 75 ? "Good" :
                                 health >= 60 ? "Fair" : "Replace Soon";

            if (SupportsCpuPowerLimit)
            {
                var pl1 = await _hardwareService.GetCpuPowerLimitAsync();
                if (pl1.HasValue)
                    CpuPowerLimitWatts = Math.Clamp(pl1.Value, CpuPowerLimitMin, CpuPowerLimitMax);
            }

            if (SupportsChargeLimit)
            {
                var threshold = await _hardwareService.GetChargeEndThresholdAsync();
                if (threshold > 0)
                    ChargeEndThreshold = threshold;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Initialization error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ApplyEcoPreset()
    {
        StatusMessage = "Applying Eco preset...";
        try
        {
            // Platform profile → cool (quietest/lowest power)
            await _hardwareService.SetPerformanceModeAsync(PerformanceMode.Quiet);

            // CPU PL1 → 15W sustained
            if (SupportsCpuPowerLimit)
            {
                CpuPowerLimitWatts = 15;
                await _hardwareService.SetCpuPowerLimitAsync(15);
            }

            // Charge limit → 80% (sweet spot for longevity)
            if (SupportsChargeLimit)
            {
                ChargeEndThreshold = 80;
                await _hardwareService.SetChargeEndThresholdAsync(80);
            }

            StatusMessage = "Eco preset applied — cool profile, 15W CPU cap, 80% charge limit";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Eco preset failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ApplyBalancedPreset()
    {
        StatusMessage = "Applying Balanced preset...";
        try
        {
            await _hardwareService.SetPerformanceModeAsync(PerformanceMode.Balanced);

            if (SupportsCpuPowerLimit)
            {
                CpuPowerLimitWatts = 28;
                await _hardwareService.SetCpuPowerLimitAsync(28);
            }

            if (SupportsChargeLimit)
            {
                ChargeEndThreshold = 85;
                await _hardwareService.SetChargeEndThresholdAsync(85);
            }

            StatusMessage = "Balanced preset applied — balanced profile, 28W CPU cap";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Balanced preset failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ApplyPerformancePreset()
    {
        StatusMessage = "Applying Performance preset...";
        try
        {
            await _hardwareService.SetPerformanceModeAsync(PerformanceMode.Performance);

            if (SupportsCpuPowerLimit)
            {
                CpuPowerLimitWatts = 45;
                await _hardwareService.SetCpuPowerLimitAsync(45);
            }

            StatusMessage = "Performance preset applied — unrestricted thermal, 45W CPU cap";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Performance preset failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ApplyCpuPowerLimit()
    {
        try
        {
            await _hardwareService.SetCpuPowerLimitAsync(CpuPowerLimitWatts);
            StatusMessage = $"CPU sustained power capped at {CpuPowerLimitWatts}W";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Power limit failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ApplyChargeLimit()
    {
        try
        {
            await _hardwareService.SetChargeEndThresholdAsync(ChargeEndThreshold);
            StatusMessage = $"Charge limit set to {ChargeEndThreshold}%";
        }
        catch (NotSupportedException ex)
        {
            StatusMessage = ex.Message;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Charge limit failed: {ex.Message}";
        }
    }
}
