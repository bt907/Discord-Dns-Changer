using System.Diagnostics;
using System.Text;

namespace DNS_Switcher.Services;

/// <summary>Result of a PowerShell command execution.</summary>
public class PowerShellResult
{
    public int    ExitCode  { get; set; }
    public string Stdout    { get; set; } = string.Empty;
    public string Stderr    { get; set; } = string.Empty;
    public bool   TimedOut  { get; set; }

    /// <summary>True when the process exited with code 0 and produced no stderr output.</summary>
    public bool Success => !TimedOut && ExitCode == 0 && string.IsNullOrWhiteSpace(Stderr);
}

/// <summary>
/// Executes PowerShell commands asynchronously using System.Diagnostics.Process.
/// Uses -EncodedCommand to avoid quoting/escaping issues with complex commands.
/// A hard 30-second timeout prevents the UI from freezing if powershell.exe hangs.
/// </summary>
public static class PowerShellService
{
    /// <summary>Default hard timeout per command. Prevents the UI from freezing forever.</summary>
    public static TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Runs <paramref name="command"/> in a hidden PowerShell process.
    /// stdout and stderr are captured and returned in the result.
    /// If the process does not finish within <see cref="DefaultTimeout"/> it is killed
    /// and the result has <c>TimedOut = true</c>.
    /// </summary>
    public static async Task<PowerShellResult> RunAsync(string command)
    {
        // Base64-encode as UTF-16 LE — the format -EncodedCommand expects
        var encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));

        var startInfo = new ProcessStartInfo
        {
            FileName               = "powershell.exe",
            Arguments              = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encodedCommand}",
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding  = Encoding.UTF8
        };

        using var process = new Process { StartInfo = startInfo };
        using var cts     = new CancellationTokenSource(DefaultTimeout);

        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null) stdoutBuilder.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) stderrBuilder.AppendLine(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Timed out — kill the process tree to avoid orphaned powershell windows
            try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }

            return new PowerShellResult
            {
                ExitCode = -1,
                TimedOut = true,
                Stdout   = stdoutBuilder.ToString().Trim(),
                Stderr   = $"Command timed out after {DefaultTimeout.TotalSeconds}s."
            };
        }

        return new PowerShellResult
        {
            ExitCode = process.ExitCode,
            Stdout   = stdoutBuilder.ToString().Trim(),
            Stderr   = stderrBuilder.ToString().Trim()
        };
    }
}
