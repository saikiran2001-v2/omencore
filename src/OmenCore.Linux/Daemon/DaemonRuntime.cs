using System.Runtime.InteropServices;

namespace OmenCore.Linux.Daemon;

/// <summary>
/// Lightweight checks for whether the root omencore daemon is running.
/// Used by the GUI to avoid fighting the daemon for hardware control.
/// </summary>
public static class DaemonRuntime
{
    private const string PidFilePath = "/var/run/omencore.pid";
    private const int Sighup = 1;

    [DllImport("libc", SetLastError = true)]
    private static extern int kill(int pid, int sig);

    public static bool IsServiceActive() => TryGetDaemonPid(out _);

    public static bool TryGetDaemonPid(out int pid)
    {
        pid = 0;
        try
        {
            if (!File.Exists(PidFilePath))
                return false;

            var pidText = File.ReadAllText(PidFilePath).Trim();
            if (!int.TryParse(pidText, out pid) || pid <= 0)
                return false;

            if (!Directory.Exists($"/proc/{pid}"))
            {
                pid = 0;
                return false;
            }

            return true;
        }
        catch
        {
            pid = 0;
            return false;
        }
    }

    /// <summary>
    /// Ask the running daemon to reload user preferences and fan curve from disk.
    /// </summary>
    public static bool RequestPreferencesReload()
    {
        if (!OperatingSystem.IsLinux() || !TryGetDaemonPid(out var pid))
            return false;

        return kill(pid, Sighup) == 0;
    }
}
