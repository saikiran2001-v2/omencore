using System.Text.Json;

namespace OmenCore.Linux.Daemon;

public static class ReliabilityDiagnosticsStore
{
    public const string SingleWriterLockPath = "/var/run/omencore-single-writer.lock";
    public const string DiagnosticsDirPath = "/var/tmp/omencore";
    public const string SnapshotPath = "/var/tmp/omencore/reliability-status.json";
    public const string LogPath = "/var/tmp/omencore/reliability.log";
    public const string DaemonWriterRole = "daemon";
    public const string WriterRoleEnvVar = "OMENCORE_WRITER_ROLE";

    public static void EnsureDiagnosticsDirectory()
    {
        try
        {
            Directory.CreateDirectory(DiagnosticsDirPath);
        }
        catch
        {
            // Best effort only.
        }
    }

    public static ReliabilityStatusSnapshot? ReadSnapshot()
    {
        try
        {
            if (!File.Exists(SnapshotPath))
            {
                return null;
            }

            var json = File.ReadAllText(SnapshotPath);
            return JsonSerializer.Deserialize(json, LinuxJsonContext.Default.ReliabilityStatusSnapshot);
        }
        catch
        {
            return null;
        }
    }

    public static void WriteSnapshot(ReliabilityStatusSnapshot snapshot)
    {
        try
        {
            EnsureDiagnosticsDirectory();
            var json = JsonSerializer.Serialize(snapshot, LinuxJsonContext.Default.ReliabilityStatusSnapshot);
            File.WriteAllText(SnapshotPath, json);
        }
        catch
        {
            // Best effort only.
        }
    }

    public static void AppendLogLine(string line)
    {
        try
        {
            EnsureDiagnosticsDirectory();
            File.AppendAllText(LogPath, line + Environment.NewLine);
            RotateLogIfNeeded();
        }
        catch
        {
            // Best effort only.
        }
    }

    public static IReadOnlyList<string> ReadRecentLogLines(int maxLines)
    {
        if (maxLines <= 0)
        {
            return Array.Empty<string>();
        }

        try
        {
            if (!File.Exists(LogPath))
            {
                return Array.Empty<string>();
            }

            var lines = File.ReadLines(LogPath);
            var queue = new Queue<string>(maxLines);
            foreach (var line in lines)
            {
                if (queue.Count == maxLines)
                {
                    queue.Dequeue();
                }

                queue.Enqueue(line);
            }

            return queue.ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public static string? ReadSingleWriterOwner()
    {
        try
        {
            if (!File.Exists(SingleWriterLockPath))
            {
                return null;
            }

            var text = File.ReadAllText(SingleWriterLockPath).Trim();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch
        {
            return null;
        }
    }

    private static void RotateLogIfNeeded()
    {
        try
        {
            const long maxBytes = 2 * 1024 * 1024;
            if (!File.Exists(LogPath))
            {
                return;
            }

            var info = new FileInfo(LogPath);
            if (info.Length <= maxBytes)
            {
                return;
            }

            var backupPath = LogPath + ".1";
            File.Copy(LogPath, backupPath, overwrite: true);
            File.WriteAllText(LogPath, string.Empty);
        }
        catch
        {
            // Ignore rotation failures.
        }
    }
}

public class ReliabilityStatusSnapshot
{
    public bool Enabled { get; set; }
    public bool SingleWriterEnabled { get; set; }
    public bool SingleWriterActive { get; set; }
    public string WriterOwner { get; set; } = string.Empty;
    public string FanProfile { get; set; } = "auto";
    public bool WatchdogEnabled { get; set; }
    public int WatchdogTrips { get; set; }
    public long LastWatchdogKickUnix { get; set; }
    public string LastWatchdogReason { get; set; } = string.Empty;
    public bool AcBatteryAutomationEnabled { get; set; }
    public string PowerSource { get; set; } = "unknown";
    public string LastAutomationMode { get; set; } = string.Empty;
    public long LastAutomationUnix { get; set; }
    public int CpuTempC { get; set; }
    public int GpuTempC { get; set; }
    public int CpuFanRpm { get; set; }
    public int GpuFanRpm { get; set; }
    public string LastError { get; set; } = string.Empty;
    public long UpdatedAtUnix { get; set; }
}
