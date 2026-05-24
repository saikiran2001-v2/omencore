using System.CommandLine;
using OmenCore.Linux.Config;
using OmenCore.Linux.Hardware;

namespace OmenCore.Linux.Commands;

/// <summary>
/// Performance mode control command.
///
/// Examples:
///   omencore-cli perf --mode balanced
///   omencore-cli perf --mode performance --power-limit 5 --hold
/// </summary>
public static class PerformanceCommand
{
    public static Command Create()
    {
        var command = new Command("perf", "Control performance mode settings");

        var modeOption = new Option<string?>(
            aliases: new[] { "--mode", "-m" },
            description: "Performance mode: default, balanced, performance, cool");

        var tccOption = new Option<int?>(
            name: "--tcc",
            description: "TCC offset value (0-15)");

        var powerOption = new Option<int?>(
            name: "--power-limit",
            description: "Thermal power limit multiplier (0-5)");

        var holdOption = new Option<bool>(
            name: "--hold",
            description: "Persist this performance request for the daemon watchdog");

        var holdIntervalOption = new Option<int?>(
            name: "--hold-interval",
            description: "Daemon hold interval in seconds (10-300, default 30)");

        command.AddOption(modeOption);
        command.AddOption(tccOption);
        command.AddOption(powerOption);
        command.AddOption(holdOption);
        command.AddOption(holdIntervalOption);

        command.SetHandler(async (mode, tcc, power, hold, holdInterval) =>
        {
            await HandlePerformanceCommandAsync(mode, tcc, power, hold, holdInterval);
        }, modeOption, tccOption, powerOption, holdOption, holdIntervalOption);

        return command;
    }

    private static async Task HandlePerformanceCommandAsync(
        string? mode, int? tcc, int? power, bool hold, int? holdInterval)
    {
        if (!LinuxEcController.CheckRootAccess())
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: Root privileges required. Run with sudo.");
            Console.ResetColor();
            return;
        }

        var ec = new LinuxEcController();

        if (!ec.IsAvailable)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: Cannot access EC. Ensure ec_sys module is loaded.");
            Console.ResetColor();
            return;
        }

        var handled = false;
        var allSucceeded = true;

        if (!string.IsNullOrEmpty(mode))
        {
            handled = true;
            var perfMode = ParsePerformanceMode(mode);
            var success = perfMode.HasValue && ec.SetPerformanceMode(perfMode.Value);

            if (success)
            {
                var readback = ec.GetPerformanceMode();
                if (perfMode.HasValue && readback == perfMode.Value)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"OK: Performance mode set to: {mode}");
                    Console.ResetColor();
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"WARN: Performance mode write returned success, but readback is {readback} (requested {mode})");
                    var profilePath = LinuxSysfsPathMap.ResolveThermalProfilePath();
                    if (!string.IsNullOrWhiteSpace(profilePath))
                    {
                        Console.WriteLine($"      Active profile path: {profilePath}");
                        Console.WriteLine($"      Active profile value: {ReadTrimmed(profilePath) ?? "<unreadable>"}");
                    }
                    else
                    {
                        Console.WriteLine("      Active profile path: <none>");
                    }

                    var acpiValue = ReadTrimmed(LinuxSysfsPathMap.AcpiPlatformProfilePath);
                    if (!string.IsNullOrWhiteSpace(acpiValue))
                    {
                        Console.WriteLine($"      /sys/firmware/acpi/platform_profile: {acpiValue}");
                    }
                    Console.ResetColor();
                }
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Failed to set performance mode: {mode}");
                Console.WriteLine("Valid modes: default, balanced, performance, cool");
                Console.ResetColor();
                allSucceeded = false;
            }
        }

        if (tcc.HasValue)
        {
            handled = true;
            var offset = Math.Clamp(tcc.Value, 0, 15);
            if (ec.SetTccOffset(offset))
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"OK: TCC offset set to: {offset}");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Failed to set TCC offset");
                Console.ResetColor();
                allSucceeded = false;
            }
        }

        if (power.HasValue)
        {
            handled = true;
            var limit = Math.Clamp(power.Value, 0, 5);
            if (!ec.HasEcAccess)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"WARN: Thermal power limit is unavailable on this backend ({ec.AccessMethod}).");
                Console.WriteLine("      This model can apply profile mode, but EC thermal power multiplier writes require direct ec_sys access.");
                Console.ResetColor();
                allSucceeded = false;
            }
            else if (ec.SetThermalPowerLimit(limit))
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"OK: Thermal power limit set to: {limit}");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Failed to set thermal power limit");
                Console.ResetColor();
                allSucceeded = false;
            }
        }

        if (hold)
        {
            handled = true;
            SaveHoldConfig(ec, mode, power, holdInterval);
        }

        if (!handled)
        {
            ShowPerformanceStatus(ec);
        }
        else if (!allSucceeded)
        {
            Environment.ExitCode = 1;
        }

        await Task.CompletedTask;
    }

    private static PerformanceMode? ParsePerformanceMode(string mode)
    {
        return mode.Trim().ToLowerInvariant() switch
        {
            "default" => PerformanceMode.Default,
            "balanced" => PerformanceMode.Balanced,
            "performance" => PerformanceMode.Performance,
            "cool" => PerformanceMode.Cool,
            _ => null
        };
    }

    private static void SaveHoldConfig(LinuxEcController ec, string? mode, int? power, int? holdInterval)
    {
        var config = OmenCoreConfig.Load(OmenCoreConfig.SystemConfigPath);
        if (!string.IsNullOrWhiteSpace(mode))
        {
            config.Performance.Mode = mode.Trim().ToLowerInvariant();
        }

        if (power.HasValue)
        {
            if (ec.HasEcAccess)
            {
                config.Performance.ThermalPowerLimit = Math.Clamp(power.Value, 0, 5);
            }
            else
            {
                config.Performance.ThermalPowerLimit = null;
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"WARN: Not persisting thermal power limit in hold config because backend '{ec.AccessMethod}' cannot write EC thermal power registers.");
                Console.ResetColor();
            }
        }

        config.Performance.HoldEnabled = true;
        config.Performance.HoldIntervalSeconds = Math.Clamp(holdInterval ?? 30, 10, 300);
        config.Save(OmenCoreConfig.SystemConfigPath);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"OK: Performance hold enabled: mode={config.Performance.Mode}, interval={config.Performance.HoldIntervalSeconds}s");
        if (config.Performance.ThermalPowerLimit.HasValue)
        {
            Console.WriteLine($"Thermal power limit will be reasserted: {config.Performance.ThermalPowerLimit.Value}");
        }
        Console.ResetColor();
        Console.WriteLine("Ensure the daemon is running: sudo omencore-cli daemon --start");
    }

    private static void ShowPerformanceStatus(LinuxEcController ec)
    {
        Console.WriteLine();
        Console.WriteLine("OmenCore Linux - Performance Status");

        var mode = ec.GetPerformanceMode();
        var modeStr = mode switch
        {
            PerformanceMode.Default => "Default",
            PerformanceMode.Balanced => "Balanced",
            PerformanceMode.Performance => "Performance",
            PerformanceMode.Cool => "Cool",
            _ => "Unknown"
        };

        Console.WriteLine($"Mode: {modeStr}");
        var profilePath = LinuxSysfsPathMap.ResolveThermalProfilePath();
        if (!string.IsNullOrWhiteSpace(profilePath))
        {
            Console.WriteLine($"Profile path: {profilePath}");
            Console.WriteLine($"Profile raw:  {ReadTrimmed(profilePath) ?? "<unreadable>"}");
        }
        Console.WriteLine();
    }

    private static string? ReadTrimmed(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
        catch
        {
            return null;
        }
    }
}
