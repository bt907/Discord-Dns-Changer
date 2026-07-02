using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace DNS_Switcher.Services;

public class PowerShellResult
{
    public int    ExitCode  { get; set; }
    public string Stdout    { get; set; } = string.Empty;
    public string Stderr    { get; set; } = string.Empty;
    public bool   TimedOut  { get; set; }

    public bool Success => !TimedOut && ExitCode == 0 && string.IsNullOrWhiteSpace(Stderr);
}

/// <summary>
/// Runs PowerShell commands via -EncodedCommand.
/// Strips CLIXML from stderr so log output is always human-readable.
/// Hard 30-second timeout prevents UI freeze if powershell.exe hangs.
/// </summary>
public static class PowerShellService
{
    public static TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public static async Task<PowerShellResult> RunAsync(string command)
    {
        // Set console output encoding inside the PS session so we read UTF-8 correctly.
        // Do NOT touch [Console]::Error — AutoFlush doesn't exist on PS 5.1's error stream.
        var fullCommand =
            "[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; " +
            command;

        var encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(fullCommand));

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
            // CLIXML is PowerShell 5.1's serialised error format when stderr is redirected.
            // Strip it so the log shows plain English instead of XML noise.
            Stderr   = CleanStderr(stderrBuilder.ToString().Trim())
        };
    }

    // ── CLIXML cleaner ───────────────────────────────────────────────────────

    /// <summary>
    /// PowerShell 5.1 wraps stderr in CLIXML when redirected:
    ///   #&lt; CLIXML &lt;Objs ...&gt;&lt;S S="Error"&gt;message&lt;/S&gt;&lt;/Objs&gt;
    /// This method extracts the plain-text error strings from that XML.
    /// If the string is not CLIXML, it is returned unchanged.
    /// </summary>
    private static string CleanStderr(string raw)
    {
        if (!raw.Contains("<S S=\"Error\">"))
            return raw;

        var matches = Regex.Matches(raw, @"<S S=""Error"">(.*?)</S>",
                                    RegexOptions.Singleline);

        var lines = matches
            .Cast<Match>()
            .Select(m => m.Groups[1].Value
                .Replace("_x000D__x000A_", " ")   // embedded CRLF tokens
                .Replace("&lt;",  "<")
                .Replace("&gt;",  ">")
                .Replace("&amp;", "&")
                .Replace("&apos;", "'")
                .Replace("&quot;", "\"")
                .Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct()
            .ToList();

        return lines.Count > 0 ? string.Join("\n", lines) : string.Empty;
    }
}
