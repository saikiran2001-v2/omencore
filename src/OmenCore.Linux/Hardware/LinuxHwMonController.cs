namespace OmenCore.Linux.Hardware;

/// <summary>
/// Linux hwmon sensor interface for temperature monitoring.
/// 
/// Reads from /sys/class/hwmon/* to get CPU and GPU temperatures.
/// This is preferred over EC-based temperature reading when available.
/// 
/// Enhanced in v2.7.0 (#24):
/// - Multiple fallback sensor detection
/// - Sensor health tracking
/// - Cached paths for low-overhead mode
/// </summary>
public class LinuxHwMonController
{
    private const string HWMON_PATH = "/sys/class/hwmon";
    private const string THERMAL_ZONE_PATH = "/sys/class/thermal";
    
    private readonly List<string> _cpuSensorPaths = new();
    private readonly List<string> _gpuSensorPaths = new();
    private readonly Dictionary<string, int> _sensorFailureCount = new();
    private readonly int _maxFailuresBeforeSkip = 5;
    private DateTime _lastFullScan = DateTime.MinValue;
    private readonly TimeSpan _rescanInterval = TimeSpan.FromMinutes(5);
    
    public bool HasCpuSensor => _cpuSensorPaths.Count > 0;
    public bool HasGpuSensor => _gpuSensorPaths.Count > 0;
    public int AvailableSensorCount => _cpuSensorPaths.Count + _gpuSensorPaths.Count;
    
    public LinuxHwMonController()
    {
        DiscoverSensors();
    }
    
    /// <summary>
    /// Discover all available sensors (hwmon + thermal zones).
    /// </summary>
    public void DiscoverSensors()
    {
        _cpuSensorPaths.Clear();
        _gpuSensorPaths.Clear();
        _lastFullScan = DateTime.Now;
        
        DiscoverHwmonSensors();
        DiscoverThermalZones();
    }
    
    private void DiscoverHwmonSensors()
    {
        if (!Directory.Exists(HWMON_PATH))
            return;
            
        foreach (var hwmonDir in Directory.GetDirectories(HWMON_PATH))
        {
            try
            {
                var namePath = Path.Combine(hwmonDir, "name");
                if (!File.Exists(namePath))
                    continue;
                    
                var name = File.ReadAllText(namePath).Trim().ToLower();
                
                // CPU temperature sensors — only genuine CPU package sensors.
                // Exclude generic ACPI thermal zones (acpitz) which lag and can
                // misreport, and HP WMI hwmon (hp) which has no temp files.
                if (name == "coretemp" || name == "k10temp" || name == "zenpower" ||
                    name == "amd_energy" || name.Contains("thinkpad"))
                {
                    AddCpuSensorPaths(hwmonDir, preferPackageSensor: name == "coretemp");
                }
                
                // GPU temperature sensors (in priority order)
                if (name.Contains("nvidia") || name.Contains("nouveau") || 
                    name.Contains("amdgpu") || name.Contains("radeon"))
                {
                    AddGpuSensorPaths(hwmonDir);
                }
            }
            catch
            {
                // Ignore errors during discovery
            }
        }
    }
    
    private void DiscoverThermalZones()
    {
        if (!Directory.Exists(THERMAL_ZONE_PATH))
            return;
            
        foreach (var zoneDir in Directory.GetDirectories(THERMAL_ZONE_PATH, "thermal_zone*"))
        {
            try
            {
                var typePath = Path.Combine(zoneDir, "type");
                var tempPath = Path.Combine(zoneDir, "temp");
                
                if (!File.Exists(typePath) || !File.Exists(tempPath))
                    continue;
                    
                var type = File.ReadAllText(typePath).Trim().ToLower();
                
                // CPU-related thermal zones — only high-fidelity package sensors.
                // Skip "acpitz" and generic ACPI zones (they lag and are imprecise).
                if (type.Contains("x86_pkg"))
                {
                    if (!_cpuSensorPaths.Contains(tempPath))
                        _cpuSensorPaths.Add(tempPath);
                }
                
                // GPU thermal zones (less common but worth checking)
                if (type.Contains("gpu") || type.Contains("nvidia"))
                {
                    if (!_gpuSensorPaths.Contains(tempPath))
                        _gpuSensorPaths.Add(tempPath);
                }
            }
            catch { }
        }
    }
    
    private void AddCpuSensorPaths(string hwmonDir, bool preferPackageSensor = false)
    {
        if (preferPackageSensor)
        {
            // For Intel coretemp: find the "Package id 0" sensor (highest priority)
            // then fall back to any other temp_input files
            string? packagePath = null;
            var allTempInputs = new List<string>();

            try
            {
                foreach (var tempFile in Directory.GetFiles(hwmonDir, "temp*_input"))
                {
                    allTempInputs.Add(tempFile);
                    var labelFile = tempFile.Replace("_input", "_label");
                    if (File.Exists(labelFile))
                    {
                        var label = File.ReadAllText(labelFile).Trim();
                        if (label.StartsWith("Package", StringComparison.OrdinalIgnoreCase) && packagePath == null)
                            packagePath = tempFile;
                    }
                }
            }
            catch { }

            if (packagePath != null && !_cpuSensorPaths.Contains(packagePath))
                _cpuSensorPaths.Add(packagePath);

            // Add remaining sensors as fallbacks
            foreach (var p in allTempInputs.Where(p => p != packagePath && !_cpuSensorPaths.Contains(p)))
                _cpuSensorPaths.Add(p);

            return;
        }

        // Default: add temp1_input, temp2_input, temp3_input
        foreach (var suffix in new[] { "temp1_input", "temp2_input", "temp3_input" })
        {
            var path = Path.Combine(hwmonDir, suffix);
            if (File.Exists(path) && !_cpuSensorPaths.Contains(path))
            {
                _cpuSensorPaths.Add(path);
            }
        }
    }
    
    private void AddGpuSensorPaths(string hwmonDir)
    {
        foreach (var suffix in new[] { "temp1_input", "temp2_input" })
        {
            var path = Path.Combine(hwmonDir, suffix);
            if (File.Exists(path) && !_gpuSensorPaths.Contains(path))
            {
                _gpuSensorPaths.Add(path);
            }
        }
    }
    
    /// <summary>
    /// Get CPU temperature with fallback to multiple sources.
    /// </summary>
    public int? GetCpuTemperature()
    {
        return GetCpuTemperatureReading()?.Temperature;
    }
    
    /// <summary>
    /// Get GPU temperature with fallback to multiple sources.
    /// </summary>
    public int? GetGpuTemperature()
    {
        return GetGpuTemperatureReading()?.Temperature;
    }

    public LinuxTemperatureReading? GetCpuTemperatureReading()
    {
        return GetTemperatureReading(_cpuSensorPaths);
    }

    public LinuxTemperatureReading? GetGpuTemperatureReading()
    {
        return GetTemperatureReading(_gpuSensorPaths);
    }
    
    private bool ShouldSkipSensor(string path)
    {
        return _sensorFailureCount.TryGetValue(path, out var count) && count >= _maxFailuresBeforeSkip;
    }

    private LinuxTemperatureReading? GetTemperatureReading(List<string> sensorPaths)
    {
        if (DateTime.Now - _lastFullScan > _rescanInterval)
        {
            DiscoverSensors();
        }

        foreach (var path in sensorPaths)
        {
            if (ShouldSkipSensor(path))
            {
                continue;
            }

            var temp = ReadTemperatureFile(path);
            if (temp.HasValue)
            {
                ResetSensorFailure(path);
                return new LinuxTemperatureReading
                {
                    Temperature = temp.Value,
                    Source = GetSensorSource(path),
                    Path = path
                };
            }

            RecordSensorFailure(path);
        }

        return null;
    }

    private static string GetSensorSource(string path)
    {
        if (path.StartsWith(HWMON_PATH, StringComparison.Ordinal))
        {
            return "hwmon";
        }

        if (path.StartsWith(THERMAL_ZONE_PATH, StringComparison.Ordinal))
        {
            return "thermal-zone";
        }

        return "sysfs";
    }
    
    private void RecordSensorFailure(string path)
    {
        _sensorFailureCount.TryGetValue(path, out var count);
        _sensorFailureCount[path] = count + 1;
    }
    
    private void ResetSensorFailure(string path)
    {
        _sensorFailureCount[path] = 0;
    }
    
    /// <summary>
    /// Read temperature from a file. Handles both hwmon (millidegrees) 
    /// and thermal_zone (millidegrees) formats.
    /// </summary>
    private int? ReadTemperatureFile(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;
                
            var content = File.ReadAllText(path).Trim();
            if (int.TryParse(content, out var value))
            {
                // Both hwmon and thermal_zone report millidegrees
                return value / 1000;
            }
        }
        catch
        {
            // Ignore read errors
        }
        
        return null;
    }
    
    /// <summary>
    /// Get all available temperature sensors.
    /// </summary>
    public IEnumerable<(string Name, string Path, int Temperature)> GetAllSensors()
    {
        var results = new List<(string Name, string Path, int Temperature)>();
        
        if (!Directory.Exists(HWMON_PATH))
            return results;
            
        foreach (var hwmonDir in Directory.GetDirectories(HWMON_PATH))
        {
            string? name = null;
            try
            {
                var namePath = Path.Combine(hwmonDir, "name");
                if (File.Exists(namePath))
                    name = File.ReadAllText(namePath).Trim();
            }
            catch { }
            
            // Find temp files
            try
            {
                foreach (var tempFile in Directory.GetFiles(hwmonDir, "temp*_input"))
                {
                    try
                    {
                        var content = File.ReadAllText(tempFile).Trim();
                        if (int.TryParse(content, out var millidegrees))
                        {
                            var label = Path.GetFileNameWithoutExtension(tempFile);
                            results.Add((name ?? "unknown", label, millidegrees / 1000));
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }
        
        return results;
    }
}
