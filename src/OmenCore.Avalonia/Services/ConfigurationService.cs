namespace OmenCore.Avalonia.Services;

/// <summary>
/// Service for managing application configuration.
/// </summary>
public interface IConfigurationService
{
    /// <summary>
    /// Gets a configuration value.
    /// </summary>
    T? Get<T>(string key);
    
    /// <summary>
    /// Sets a configuration value.
    /// </summary>
    void Set<T>(string key, T value);
    
    /// <summary>
    /// Saves configuration to disk.
    /// </summary>
    Task SaveAsync();
    
    /// <summary>
    /// Loads configuration from disk.
    /// </summary>
    Task LoadAsync();
}

/// <summary>
/// TOML-based configuration service for Linux.
/// </summary>
public class ConfigurationService : IConfigurationService
{
    private readonly string _configPath;
    private Dictionary<string, object> _config = new();

    public ConfigurationService()
    {
        var configDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(configDir))
        {
            configDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        }
        
        var omenConfigDir = Path.Combine(configDir, "omencore");
        Directory.CreateDirectory(omenConfigDir);
        _configPath = Path.Combine(omenConfigDir, "config.toml");
    }

    public T? Get<T>(string key)
    {
        if (_config.TryGetValue(key, out var value))
        {
            try
            {
                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                return default;
            }
        }
        return default;
    }

    public void Set<T>(string key, T value)
    {
        if (value != null)
            _config[key] = value;
    }

    public async Task SaveAsync()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# OmenCore Configuration");
        sb.AppendLine($"# Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();
        
        foreach (var kvp in _config)
        {
            var value = kvp.Value switch
            {
                bool b => b.ToString().ToLower(),
                string s => $"\"{s}\"",
                _ => kvp.Value.ToString()
            };
            sb.AppendLine($"{kvp.Key} = {value}");
        }

        await File.WriteAllTextAsync(_configPath, sb.ToString());
    }

    private static Dictionary<string, object> CreateDefaults() => new()
    {
        ["start_minimized"] = false,
        ["dark_theme"] = true,
        ["polling_interval_ms"] = 1000,
        ["auto_apply_profile"] = true,
        ["default_performance_mode"] = "default"
    };

    public async Task LoadAsync()
    {
        if (!File.Exists(_configPath))
        {
            _config = CreateDefaults();
            await SaveAsync();
            return;
        }

        try
        {
            var lines = await File.ReadAllLinesAsync(_configPath);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
                    continue;

                var parts = line.Split('=', 2);
                if (parts.Length == 2)
                {
                    var key = parts[0].Trim();
                    var valueStr = parts[1].Trim();
                    
                    // Parse value
                    object value;
                    if (valueStr == "true") value = true;
                    else if (valueStr == "false") value = false;
                    else if (valueStr.StartsWith('"') && valueStr.EndsWith('"'))
                        value = valueStr[1..^1];
                    else if (int.TryParse(valueStr, out var intVal))
                        value = intVal;
                    else if (double.TryParse(valueStr, out var doubleVal))
                        value = doubleVal;
                    else
                        value = valueStr;

                    _config[key] = value;
                }
            }
        }
        catch
        {
            // Unreadable/corrupt file: fall back to defaults instead of
            // re-reading the same broken file (previously recursed forever).
            _config = CreateDefaults();
        }
    }
}
