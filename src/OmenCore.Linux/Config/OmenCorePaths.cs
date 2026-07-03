namespace OmenCore.Linux.Config;

/// <summary>
/// Resolves config/data paths for both the root daemon and the desktop GUI.
/// </summary>
public static class OmenCorePaths
{
    public const string SharedConfigDir = "/var/lib/omencore";
    public static string SharedConfigPath => Path.Combine(SharedConfigDir, "config.toml");

    /// <summary>
    /// Returns the real interactive user's home directory, even when the
    /// process was started via sudo (uses SUDO_UID / SUDO_USER).
    /// </summary>
    public static string GetRealUserHomeDirectory()
    {
        var sudoUid = Environment.GetEnvironmentVariable("SUDO_UID");
        if (!string.IsNullOrWhiteSpace(sudoUid)
            && int.TryParse(sudoUid, out var uid)
            && uid > 0)
        {
            var passwdHome = TryGetHomeFromPasswd(uid);
            if (!string.IsNullOrWhiteSpace(passwdHome))
                return passwdHome;
        }

        var sudoUser = Environment.GetEnvironmentVariable("SUDO_USER");
        if (!string.IsNullOrWhiteSpace(sudoUser))
        {
            var passwdHome = TryGetHomeFromPasswd(sudoUser);
            if (!string.IsNullOrWhiteSpace(passwdHome))
                return passwdHome;
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
            return userProfile;

        return Path.Combine("/home", Environment.UserName);
    }

    public static string GetUserConfigDirectory()
    {
        var home = GetRealUserHomeDirectory();
        return Path.Combine(home, ".config", "omencore");
    }

    public static string GetUserConfigPath() =>
        Path.Combine(GetUserConfigDirectory(), "config.toml");

    public static string GetUserPreferencesPath() =>
        Path.Combine(GetUserConfigDirectory(), "user-preferences.json");

    public static string GetGuiSettingsPath() =>
        Path.Combine(GetUserConfigDirectory(), "gui-settings.toml");

    public static string SharedPreferencesPath =>
        Path.Combine(SharedConfigDir, "user-preferences.json");

    public static void EnsureSharedConfigDirectory()
    {
        if (!Directory.Exists(SharedConfigDir))
            Directory.CreateDirectory(SharedConfigDir);

        try
        {
            // Allow the desktop user to sync hardware prefs without sudo.
            if (OperatingSystem.IsLinux())
                File.SetUnixFileMode(SharedConfigDir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute);
        }
        catch
        {
            // Best effort only — install may already have set permissions.
        }
    }

    private static string? TryGetHomeFromPasswd(int uid)
    {
        try
        {
            foreach (var line in File.ReadAllLines("/etc/passwd"))
            {
                var parts = line.Split(':');
                if (parts.Length >= 6
                    && int.TryParse(parts[2], out var entryUid)
                    && entryUid == uid)
                {
                    return parts[5];
                }
            }
        }
        catch
        {
            // Ignore lookup failures.
        }

        return null;
    }

    private static string? TryGetHomeFromPasswd(string username)
    {
        try
        {
            foreach (var line in File.ReadAllLines("/etc/passwd"))
            {
                var parts = line.Split(':');
                if (parts.Length >= 6
                    && string.Equals(parts[0], username, StringComparison.Ordinal))
                {
                    return parts[5];
                }
            }
        }
        catch
        {
            // Ignore lookup failures.
        }

        return null;
    }
}
