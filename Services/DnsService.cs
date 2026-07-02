using System.Net;
using System.Net.Sockets;
using System.Text;

namespace DNS_Switcher.Services;

/// <summary>
/// High-level DNS operations with full logging, read-back verification,
/// and compatibility with all Windows 10 / 11 PowerShell builds.
///
/// Key design decision:
///   Set-DnsClientServerAddress does NOT support -AddressFamily on some
///   Windows / PS builds (throws "parameter not found"). We therefore pass
///   ALL desired server addresses (IPv4 + IPv6) in a single call and let
///   Windows route them to the correct interface family automatically.
/// </summary>
public static class DnsService
{
    // ── Apply DNS ────────────────────────────────────────────────────────────

    public static async Task<(bool success, string log)> ApplyDnsAsync(
        string adapterName,
        string primaryIPv4,   string secondaryIPv4,
        string primaryIPv6,   string secondaryIPv6,
        bool   setIPv6)
    {
        var log     = new StringBuilder();
        bool success = true;

        // ── Read BEFORE ──────────────────────────────────────────────────────
        log.AppendLine($"BEFORE on [{adapterName}]");
        var (beforeV4, beforeV6) = await ReadDnsAsync(adapterName);
        log.AppendLine($"  IPv4: {beforeV4}");
        log.AppendLine($"  IPv6: {beforeV6}");
        log.AppendLine(string.Empty);

        // ── Build combined address list ───────────────────────────────────────
        // -AddressFamily is NOT available in all Windows PS builds.
        // We pass all addresses in one call; Windows applies IPv4 addresses to
        // the IPv4 interface and IPv6 addresses to the IPv6 interface automatically.
        var allAddresses = new List<string> { primaryIPv4 };
        if (!string.IsNullOrEmpty(secondaryIPv4)) allAddresses.Add(secondaryIPv4);

        if (setIPv6)
        {
            if (!string.IsNullOrEmpty(primaryIPv6))   allAddresses.Add(primaryIPv6);
            if (!string.IsNullOrEmpty(secondaryIPv6)) allAddresses.Add(secondaryIPv6);
        }

        var addressArgs = string.Join(",", allAddresses.Select(a => $"'{a}'"));
        var setCmd =
            $"$ErrorActionPreference = 'Stop'; " +
            $"Set-DnsClientServerAddress " +
            $"-InterfaceAlias '{EscapePs(adapterName)}' " +
            $"-ServerAddresses {addressArgs}";

        log.AppendLine($"CMD: {setCmd}");
        var setResult = await PowerShellService.RunAsync(setCmd);
        LogPsResult(log, setResult);

        if (setResult.ExitCode == 0)
        {
            // ── Verify ───────────────────────────────────────────────────────
            var (afterV4, afterV6) = await ReadDnsAsync(adapterName);
            log.AppendLine($"  AFTER IPv4: {afterV4}");
            log.AppendLine($"  AFTER IPv6: {afterV6}");

            bool v4ok = afterV4.Contains(primaryIPv4);
            log.AppendLine(v4ok
                ? $"  [OK] DNS confirmed → IPv4: {afterV4}"
                : $"  [WARN] Read-back IPv4 shows: {afterV4}  (may need a moment to update)");
        }
        else
        {
            log.AppendLine("  [FAIL] DNS was NOT changed. See error above.");
            success = false;
        }

        log.AppendLine(string.Empty);

        // ── Flush DNS cache ──────────────────────────────────────────────────
        log.AppendLine("CMD: ipconfig /flushdns");
        var flush = await PowerShellService.RunAsync("ipconfig /flushdns");
        LogPsResult(log, flush);
        log.AppendLine(flush.ExitCode == 0
            ? "  [OK] DNS resolver cache flushed."
            : $"  [WARN] DNS flush exited {flush.ExitCode}");

        return (success, log.ToString());
    }

    // ── Restore automatic DNS ────────────────────────────────────────────────

    public static async Task<(bool success, string log)> RestoreAutomaticDnsAsync(string adapterName)
    {
        var log = new StringBuilder();

        var (beforeV4, _) = await ReadDnsAsync(adapterName);
        log.AppendLine($"BEFORE IPv4: {beforeV4}");

        // -ResetServerAddresses works on all Windows versions, no -AddressFamily needed
        var cmd =
            $"$ErrorActionPreference = 'Stop'; " +
            $"Set-DnsClientServerAddress " +
            $"-InterfaceAlias '{EscapePs(adapterName)}' " +
            $"-ResetServerAddresses";

        log.AppendLine($"CMD: {cmd}");
        var result = await PowerShellService.RunAsync(cmd);
        LogPsResult(log, result);

        if (result.ExitCode != 0)
        {
            log.AppendLine("  [FAIL] Could not restore automatic DNS.");
            return (false, log.ToString());
        }

        var (afterV4, _) = await ReadDnsAsync(adapterName);
        log.AppendLine($"  AFTER IPv4: {afterV4}");
        log.AppendLine($"  [OK] DNS reset on {adapterName}.");

        log.AppendLine(string.Empty);
        log.AppendLine("CMD: ipconfig /flushdns");
        var flush = await PowerShellService.RunAsync("ipconfig /flushdns");
        LogPsResult(log, flush);
        log.AppendLine(flush.ExitCode == 0 ? "  [OK] DNS cache flushed." : $"  [WARN] flush exit {flush.ExitCode}");

        return (true, log.ToString());
    }

    // ── IPv6 binding ─────────────────────────────────────────────────────────

    public static async Task<(bool success, string log)> DisableIPv6Async(string adapterName)
    {
        var cmd =
            $"$ErrorActionPreference = 'Stop'; " +
            $"Disable-NetAdapterBinding " +
            $"-Name '{EscapePs(adapterName)}' " +
            $"-ComponentID ms_tcpip6";

        var log = new StringBuilder();
        log.AppendLine($"CMD: {cmd}");
        var result = await PowerShellService.RunAsync(cmd);
        LogPsResult(log, result);
        log.AppendLine(result.ExitCode == 0
            ? $"  [OK] IPv6 binding disabled on {adapterName}."
            : $"  [FAIL] exit {result.ExitCode}");

        return (result.ExitCode == 0, log.ToString());
    }

    public static async Task<(bool success, string log)> EnableIPv6Async(string adapterName)
    {
        var cmd =
            $"$ErrorActionPreference = 'Stop'; " +
            $"Enable-NetAdapterBinding " +
            $"-Name '{EscapePs(adapterName)}' " +
            $"-ComponentID ms_tcpip6";

        var log = new StringBuilder();
        log.AppendLine($"CMD: {cmd}");
        var result = await PowerShellService.RunAsync(cmd);
        LogPsResult(log, result);
        log.AppendLine(result.ExitCode == 0
            ? $"  [OK] IPv6 binding re-enabled on {adapterName}."
            : $"  [FAIL] exit {result.ExitCode}");

        return (result.ExitCode == 0, log.ToString());
    }

    // ── Discord connectivity test ────────────────────────────────────────────

    public static async Task<string> TestDiscordConnectivityAsync()
    {
        var log = new StringBuilder();
        log.AppendLine("══ Discord Connectivity Test ══════════════════════════");

        log.AppendLine(string.Empty);
        log.AppendLine("1) Resolve discord.com  [system DNS]");
        var sysResolve = await PowerShellService.RunAsync(
            "try { " +
            "  $r = Resolve-DnsName discord.com -ErrorAction Stop | " +
            "       Where-Object { $_.IPAddress } | " +
            "       Select-Object -ExpandProperty IPAddress -First 4; " +
            "  if ($r) { $r -join ', ' } else { 'No records returned' } " +
            "} catch { 'FAILED: ' + $_.Exception.Message }");
        log.AppendLine($"   → {(string.IsNullOrEmpty(sysResolve.Stdout) ? "no output" : sysResolve.Stdout)}");
        if (!string.IsNullOrEmpty(sysResolve.Stderr))
            log.AppendLine($"   stderr: {sysResolve.Stderr}");

        log.AppendLine(string.Empty);
        log.AppendLine("2) Resolve discord.com  [Google 8.8.8.8 — bypass current DNS]");
        var googleResolve = await PowerShellService.RunAsync(
            "try { " +
            "  $r = Resolve-DnsName discord.com -Server 8.8.8.8 -ErrorAction Stop | " +
            "       Where-Object { $_.IPAddress } | " +
            "       Select-Object -ExpandProperty IPAddress -First 4; " +
            "  if ($r) { $r -join ', ' } else { 'No records returned' } " +
            "} catch { 'FAILED: ' + $_.Exception.Message }");
        log.AppendLine($"   → {(string.IsNullOrEmpty(googleResolve.Stdout) ? "no output" : googleResolve.Stdout)}");
        if (!string.IsNullOrEmpty(googleResolve.Stderr))
            log.AppendLine($"   stderr: {googleResolve.Stderr}");

        log.AppendLine(string.Empty);
        log.AppendLine("3) Ping 8.8.8.8  [basic internet reachability]");
        var ping = await PowerShellService.RunAsync(
            "try { " +
            "  $p = Test-Connection 8.8.8.8 -Count 3 -ErrorAction Stop; " +
            "  $prop = if ($p[0].PSObject.Properties['Latency']) { 'Latency' } else { 'ResponseTime' }; " +
            "  $avg = [math]::Round(($p | Measure-Object -Property $prop -Average).Average,1); " +
            "  'Reachable  avg ' + $avg + ' ms (' + $p.Count + '/3)' " +
            "} catch { 'UNREACHABLE: ' + $_.Exception.Message }");
        log.AppendLine($"   → {(string.IsNullOrEmpty(ping.Stdout) ? "no response" : ping.Stdout)}");

        log.AppendLine(string.Empty);
        log.AppendLine("4) TCP connect  discord.com:443");
        var tcp = await PowerShellService.RunAsync(
            "try { " +
            "  $c = Test-NetConnection -ComputerName discord.com -Port 443 " +
            "       -InformationLevel Quiet -ErrorAction Stop; " +
            "  if ($c) { 'SUCCESS — TCP 443 open' } else { 'BLOCKED — TCP 443 refused' } " +
            "} catch { 'FAILED: ' + $_.Exception.Message }");
        log.AppendLine($"   → {(string.IsNullOrEmpty(tcp.Stdout) ? "no response" : tcp.Stdout)}");

        log.AppendLine(string.Empty);
        log.AppendLine("══════════════════════════════════════════════════════");
        return log.ToString();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads ALL current DNS addresses for an adapter, then splits them into
    /// IPv4 and IPv6 lists using C# IPAddress parsing (no -AddressFamily needed).
    /// </summary>
    public static async Task<(string ipv4, string ipv6)> ReadDnsAsync(string adapterName)
    {
        // Without -AddressFamily, Get-DnsClientServerAddress returns both families.
        // We expand all ServerAddresses into one flat list, then classify in C#.
        var cmd =
            $"Get-DnsClientServerAddress " +
            $"-InterfaceAlias '{EscapePs(adapterName)}' " +
            $"-ErrorAction SilentlyContinue | " +
            $"Select-Object -ExpandProperty ServerAddresses";

        var result = await PowerShellService.RunAsync(cmd);

        if (string.IsNullOrWhiteSpace(result.Stdout))
            return ("(automatic)", "(automatic)");

        var all = result.Stdout
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(a => a.Trim())
            .Where(a => !string.IsNullOrEmpty(a))
            .ToList();

        var v4 = all.Where(a => IPAddress.TryParse(a, out var ip)
                                 && ip.AddressFamily == AddressFamily.InterNetwork)
                    .ToList();

        var v6 = all.Where(a => IPAddress.TryParse(a, out var ip)
                                 && ip.AddressFamily == AddressFamily.InterNetworkV6)
                    .ToList();

        return (
            v4.Count > 0 ? string.Join(", ", v4) : "(automatic)",
            v6.Count > 0 ? string.Join(", ", v6) : "(automatic)"
        );
    }

    private static void LogPsResult(StringBuilder log, PowerShellResult r)
    {
        log.AppendLine($"  exit={r.ExitCode}" + (r.TimedOut ? " [TIMED OUT]" : string.Empty));
        if (!string.IsNullOrWhiteSpace(r.Stdout))
            log.AppendLine($"  stdout: {r.Stdout.Trim()}");
        if (!string.IsNullOrWhiteSpace(r.Stderr))
            log.AppendLine($"  stderr: {r.Stderr.Trim()}");
    }

    private static string EscapePs(string s) => s.Replace("'", "''");
}
