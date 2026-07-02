using System.Text;

namespace DNS_Switcher.Services;

/// <summary>
/// High-level DNS operations.
/// Every method returns a detailed log string so the caller can show exactly
/// what happened — including the raw PowerShell output and a verification
/// step that reads the DNS back after applying to confirm it actually changed.
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
        log.AppendLine($"BEFORE — reading current DNS on [{adapterName}]");
        var beforeIPv4 = await ReadCurrentDnsAsync(adapterName, "IPv4");
        var beforeIPv6 = await ReadCurrentDnsAsync(adapterName, "IPv6");
        log.AppendLine($"  IPv4 was: {beforeIPv4}");
        log.AppendLine($"  IPv6 was: {beforeIPv6}");
        log.AppendLine(string.Empty);

        // ── Apply IPv4 ───────────────────────────────────────────────────────
        var ipv4Addresses = string.IsNullOrEmpty(secondaryIPv4)
            ? $"'{primaryIPv4}'"
            : $"'{primaryIPv4}','{secondaryIPv4}'";

        // $ErrorActionPreference = 'Stop' converts non-terminating PS errors into
        // terminating ones, so the process exits with code != 0 on any failure.
        // Without this, Set-DnsClientServerAddress can silently "succeed" (exit 0)
        // even when Windows rejects the change (e.g. access denied).
        var ipv4Cmd =
            $"$ErrorActionPreference = 'Stop'; " +
            $"Set-DnsClientServerAddress " +
            $"-InterfaceAlias '{EscapePs(adapterName)}' " +
            $"-AddressFamily IPv4 " +
            $"-ServerAddresses {ipv4Addresses}";

        log.AppendLine($"CMD (IPv4): {ipv4Cmd}");
        var ipv4Result = await PowerShellService.RunAsync(ipv4Cmd);
        LogPsResult(log, ipv4Result);

        if (ipv4Result.ExitCode == 0)
        {
            var afterIPv4 = await ReadCurrentDnsAsync(adapterName, "IPv4");
            log.AppendLine($"  VERIFY IPv4: {afterIPv4}");

            if (afterIPv4.Contains(primaryIPv4))
                log.AppendLine($"  [OK] IPv4 DNS confirmed → {afterIPv4}");
            else
                log.AppendLine($"  [WARN] IPv4 DNS set but read-back shows: {afterIPv4} (may need a moment)");
        }
        else
        {
            log.AppendLine($"  [FAIL] IPv4 DNS was NOT changed.");
            success = false;
        }

        log.AppendLine(string.Empty);

        // ── Apply IPv6 (optional) ────────────────────────────────────────────
        if (setIPv6 && !string.IsNullOrEmpty(primaryIPv6))
        {
            var ipv6Addresses = string.IsNullOrEmpty(secondaryIPv6)
                ? $"'{primaryIPv6}'"
                : $"'{primaryIPv6}','{secondaryIPv6}'";

            var ipv6Cmd =
                $"$ErrorActionPreference = 'Stop'; " +
                $"Set-DnsClientServerAddress " +
                $"-InterfaceAlias '{EscapePs(adapterName)}' " +
                $"-AddressFamily IPv6 " +
                $"-ServerAddresses {ipv6Addresses}";

            log.AppendLine($"CMD (IPv6): {ipv6Cmd}");
            var ipv6Result = await PowerShellService.RunAsync(ipv6Cmd);
            LogPsResult(log, ipv6Result);

            if (ipv6Result.ExitCode == 0)
            {
                var afterIPv6 = await ReadCurrentDnsAsync(adapterName, "IPv6");
                log.AppendLine($"  VERIFY IPv6: {afterIPv6}");
                log.AppendLine(afterIPv6.Contains(primaryIPv6.Split(':')[0])
                    ? $"  [OK] IPv6 DNS confirmed → {afterIPv6}"
                    : $"  [WARN] IPv6 read-back: {afterIPv6}");
            }
            else
            {
                // IPv6 failure is a warning, not fatal — adapter may not have IPv6
                log.AppendLine("  [WARN] IPv6 DNS change failed (adapter may not support IPv6).");
            }

            log.AppendLine(string.Empty);
        }

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

        var beforeIPv4 = await ReadCurrentDnsAsync(adapterName, "IPv4");
        log.AppendLine($"BEFORE IPv4: {beforeIPv4}");

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
            log.AppendLine($"  [FAIL] Could not restore automatic DNS.");
            return (false, log.ToString());
        }

        var afterIPv4 = await ReadCurrentDnsAsync(adapterName, "IPv4");
        log.AppendLine($"  AFTER IPv4: {afterIPv4}");
        log.AppendLine(afterIPv4 == "(automatic)" || string.IsNullOrEmpty(afterIPv4)
            ? "  [OK] DNS restored to automatic (DHCP)."
            : $"  [OK] DNS reset on {adapterName}. Current: {afterIPv4}");

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

    /// <summary>
    /// Tests whether Discord can actually be reached after a DNS change.
    /// Resolves discord.com via system DNS AND via Google 8.8.8.8, pings 8.8.8.8,
    /// and attempts a TCP connection to Discord's gateway (162.159.x.x:443).
    /// </summary>
    public static async Task<string> TestDiscordConnectivityAsync()
    {
        var log = new StringBuilder();
        log.AppendLine("══ Discord Connectivity Test ══════════════════════════");

        // 1. Resolve discord.com using the current system DNS
        log.AppendLine(string.Empty);
        log.AppendLine("1) Resolve discord.com  [system DNS]");
        var sysResolve = await PowerShellService.RunAsync(
            "try { " +
            "  $r = Resolve-DnsName discord.com -ErrorAction Stop | " +
            "       Where-Object { $_.IPAddress } | " +
            "       Select-Object -ExpandProperty IPAddress -First 4; " +
            "  if ($r) { $r -join ', ' } else { 'No A/AAAA records returned' } " +
            "} catch { 'FAILED — ' + $_.Exception.Message }");
        log.AppendLine($"   → {(string.IsNullOrEmpty(sysResolve.Stdout) ? "no output" : sysResolve.Stdout)}");
        if (!string.IsNullOrEmpty(sysResolve.Stderr)) log.AppendLine($"   stderr: {sysResolve.Stderr}");

        // 2. Resolve discord.com via Google 8.8.8.8 (bypass current DNS)
        log.AppendLine(string.Empty);
        log.AppendLine("2) Resolve discord.com  [Google 8.8.8.8 — bypass current DNS]");
        var googleResolve = await PowerShellService.RunAsync(
            "try { " +
            "  $r = Resolve-DnsName discord.com -Server 8.8.8.8 -ErrorAction Stop | " +
            "       Where-Object { $_.IPAddress } | " +
            "       Select-Object -ExpandProperty IPAddress -First 4; " +
            "  if ($r) { $r -join ', ' } else { 'No A/AAAA records returned' } " +
            "} catch { 'FAILED — ' + $_.Exception.Message }");
        log.AppendLine($"   → {(string.IsNullOrEmpty(googleResolve.Stdout) ? "no output" : googleResolve.Stdout)}");

        // 3. Ping 8.8.8.8 (basic internet check)
        log.AppendLine(string.Empty);
        log.AppendLine("3) Ping 8.8.8.8  [basic internet reachability]");
        var ping = await PowerShellService.RunAsync(
            "try { " +
            "  $p = Test-Connection 8.8.8.8 -Count 3 -ErrorAction Stop; " +
            "  $prop = if ($p[0].PSObject.Properties['Latency']) { 'Latency' } else { 'ResponseTime' }; " +
            "  $avg = [math]::Round(($p | Measure-Object -Property $prop -Average).Average,1); " +
            "  'Reachable  avg ' + $avg + ' ms (' + $p.Count + '/3)' " +
            "} catch { 'UNREACHABLE — ' + $_.Exception.Message }");
        log.AppendLine($"   → {(string.IsNullOrEmpty(ping.Stdout) ? "no response" : ping.Stdout)}");

        // 4. TCP connect to Discord's HTTPS port
        log.AppendLine(string.Empty);
        log.AppendLine("4) TCP connect  discord.com:443");
        var tcp = await PowerShellService.RunAsync(
            "try { " +
            "  $c = Test-NetConnection -ComputerName discord.com -Port 443 -InformationLevel Quiet -ErrorAction Stop; " +
            "  if ($c) { 'SUCCESS — TCP 443 open' } else { 'BLOCKED — TCP 443 refused' } " +
            "} catch { 'FAILED — ' + $_.Exception.Message }");
        log.AppendLine($"   → {(string.IsNullOrEmpty(tcp.Stdout) ? "no response" : tcp.Stdout)}");

        log.AppendLine(string.Empty);
        log.AppendLine("══════════════════════════════════════════════════════");
        return log.ToString();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads the current DNS addresses for a specific address family.
    /// Returns "(automatic)" when no manual DNS is configured.
    /// </summary>
    public static async Task<string> ReadCurrentDnsAsync(string adapterName, string family)
    {
        var cmd =
            $"$a = (Get-DnsClientServerAddress " +
            $"-InterfaceAlias '{EscapePs(adapterName)}' " +
            $"-AddressFamily {family} " +
            $"-ErrorAction SilentlyContinue).ServerAddresses; " +
            $"if ($a) {{ $a -join ', ' }} else {{ '(automatic)' }}";

        var result = await PowerShellService.RunAsync(cmd);
        return string.IsNullOrWhiteSpace(result.Stdout) ? "(automatic)" : result.Stdout.Trim();
    }

    private static async Task<string> FlushDnsAsync()
    {
        var result = await PowerShellService.RunAsync("ipconfig /flushdns");
        return result.ExitCode == 0
            ? "[OK]    DNS resolver cache flushed.\n"
            : $"[WARN]  DNS flush exit {result.ExitCode}: {result.Stderr}\n";
    }

    /// <summary>Appends exit code, stdout, and stderr from a PS result to the log.</summary>
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
