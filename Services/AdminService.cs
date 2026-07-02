using System.Diagnostics;
using System.Security.Principal;

namespace DNS_Switcher.Services;

/// <summary>
/// Helpers for checking and requesting Windows Administrator privileges.
/// </summary>
public static class AdminService
{
    /// <summary>Returns true when the current process is running as Administrator.</summary>
    public static bool IsRunningAsAdmin()
    {
        using var identity  = WindowsIdentity.GetCurrent();
        var       principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// Relaunches the same executable with the "runas" verb (UAC elevation prompt).
    /// Shuts down the current (non-elevated) instance on success.
    /// </summary>
    public static void RestartAsAdmin()
    {
        // Prefer ProcessPath (.NET 6+); fall back to MainModule.FileName
        var exePath = Environment.ProcessPath
                   ?? Process.GetCurrentProcess().MainModule?.FileName;

        if (string.IsNullOrEmpty(exePath))
            return;

        var startInfo = new ProcessStartInfo
        {
            FileName        = exePath,
            UseShellExecute = true,
            Verb            = "runas"
        };

        try
        {
            Process.Start(startInfo);
            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception)
        {
            // User cancelled the UAC prompt — stay in the current instance.
        }
    }
}
