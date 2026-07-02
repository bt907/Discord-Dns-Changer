using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace DNS_Switcher.Services;

/// <summary>
/// Detects whether Discord is installed and provides a helper to open
/// the Discord download page in the default browser.
/// </summary>
public static class DiscordService
{
    public const string DownloadUrl = "https://discord.com/download";

    // ── Detection ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true when Discord appears to be installed on this machine.
    /// Checks the three most common installation paths and the registry.
    /// </summary>
    public static bool IsDiscordInstalled()
    {
        // Path 1: standard user-level install (Update.exe is Discord's launcher stub)
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var updateExe    = Path.Combine(localAppData, "Discord", "Update.exe");
        if (File.Exists(updateExe)) return true;

        // Path 2: the app folder itself (Discord.exe inside a versioned subfolder)
        var discordFolder = Path.Combine(localAppData, "Discord");
        if (Directory.Exists(discordFolder))
        {
            // Versioned subdirs look like "app-1.0.9xxx"
            var appDirs = Directory.GetDirectories(discordFolder, "app-*");
            foreach (var dir in appDirs)
            {
                if (File.Exists(Path.Combine(dir, "Discord.exe")))
                    return true;
            }
        }

        // Path 3: system-wide / managed install (rare but possible)
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (File.Exists(Path.Combine(programFiles, "Discord", "Discord.exe")))
            return true;

        // Path 4: registry uninstall key (covers both per-user and machine-wide installs)
        if (IsInRegistry(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", "Discord"))
            return true;
        if (IsInRegistry(@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", "Discord"))
            return true;

        return false;
    }

    /// <summary>Returns the version string of the installed Discord, or null if not found.</summary>
    public static string? GetDiscordVersion()
    {
        var localAppData  = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var discordFolder = Path.Combine(localAppData, "Discord");

        if (!Directory.Exists(discordFolder)) return null;

        // Highest versioned subfolder wins
        var latest = Directory.GetDirectories(discordFolder, "app-*")
            .Select(Path.GetFileName)
            .Where(n => n is not null)
            .OrderByDescending(n => n)
            .FirstOrDefault();

        // Strip the "app-" prefix for display
        return latest?.Replace("app-", "");
    }

    // ── Actions ──────────────────────────────────────────────────────────────

    /// <summary>Opens the Discord download page in the system default browser.</summary>
    public static void OpenDownloadPage()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName        = DownloadUrl,
            UseShellExecute = true   // Lets Windows choose the default browser
        });
    }

    /// <summary>Launches Discord if it is installed.</summary>
    /// <returns>True when Discord was found and started.</returns>
    public static bool LaunchDiscord()
    {
        var localAppData  = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var discordFolder = Path.Combine(localAppData, "Discord");

        // Prefer Update.exe which handles the versioned launch correctly
        var updateExe = Path.Combine(discordFolder, "Update.exe");
        if (File.Exists(updateExe))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName        = updateExe,
                Arguments       = "--processStart Discord.exe",
                UseShellExecute = true
            });
            return true;
        }

        // Fall back: find Discord.exe in the latest app- folder
        var latest = Directory.GetDirectories(discordFolder, "app-*")
            .OrderByDescending(d => d)
            .FirstOrDefault();

        if (latest is not null)
        {
            var exe = Path.Combine(latest, "Discord.exe");
            if (File.Exists(exe))
            {
                Process.Start(new ProcessStartInfo { FileName = exe, UseShellExecute = true });
                return true;
            }
        }

        return false;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static bool IsInRegistry(string keyPath, string displayNameSubstring)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(keyPath)
                         ?? Registry.CurrentUser.OpenSubKey(keyPath);

            if (key is null) return false;

            foreach (var subName in key.GetSubKeyNames())
            {
                using var sub = key.OpenSubKey(subName);
                var name = sub?.GetValue("DisplayName")?.ToString() ?? string.Empty;
                if (name.Contains(displayNameSubstring, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch
        {
            // Registry access denied or key missing — not installed via this path
        }

        return false;
    }
}
