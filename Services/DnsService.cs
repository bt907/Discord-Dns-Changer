using System.Text;

namespace DNS_Switcher.Services;

/// <summary>
/// High-level DNS operations: apply, restore, disable IPv6, and test connectivity.
/// Every public method returns a (success, log) tuple so the caller can display results.
/// </summary>
public static class DnsService
{
    // ── Apply DNS ────────────────────────────────────────────────────────────

    /// <summary>
    /// Sets IPv4 DNS (and optionally IPv6 DNS) on <paramref name="adapterName"/>,
    /// then flushes the DNS cache.
    /// </summary>
    public static async Task<(bool success, string log)> ApplyDnsAsync(
        string adapterName,
        string primaryIPv4,   string secondaryIPv4,
        string primaryIPv6,   string secondaryIPv6,
        bool   setIPv6)
    {
        var log     = new StringBuilder();
        bool success = true;

        // --- IPv4 ---
        var ipv4Addresses = string.IsNullOrEmpty(secondaryIPv4)
            ? $"'{primaryIPv4}'"
            : $"'{primaryIPv4}','{secondaryIPv4}'";

        var ipv4Cmd =
            $"Set-DnsClientServerAddress " +
            $"-InterfaceAlias '{EscapePs(adapterName)}' " +
            $"-AddressFamily IPv4 " +
            $"-ServerAddresses {ipv4Addresses}";

        var ipv4Result = await PowerShellService.RunAsync(ipv4Cmd);

        if (ipv4Result.ExitCode == 0)
            log.AppendLine($"[OK]    IPv4 DNS → {primaryIPv4}" +
                           (string.IsNullOrEmpty(secondaryIPv4) ? "" : $", {secondaryIPv4}") +
                           $"  ({adapterName})");
        else
        {
            log.AppendLine($"[ERROR] IPv4 DNS failed on {adapterName}: {ipv4Result.Stderr}");
            success = false;
        }

        // --- IPv6 (optional) ---
        if (setIPv6 && !string.IsNullOrEmpty(primaryIPv6))
        {
            var ipv6Addresses = string.IsNullOrEmpty(secondaryIPv6)
                ? $"'{primaryIPv6}'"
                : $"'{primaryIPv6}','{secondaryIPv6}'";

            var ipv6Cmd =
                $"Set-DnsClientServerAddress " +
                $"-InterfaceAlias '{EscapePs(adapterName)}' " +
                $"-AddressFamily IPv6 " +
                $"-ServerAddresses {ipv6Addresses}";

            var ipv6Result = await PowerShellService.RunAsync(ipv6Cmd);

            if (ipv6Result.ExitCode == 0)
                log.AppendLine($"[OK]    IPv6 DNS → {primaryIPv6}" +
                               (string.IsNullOrEmpty(secondaryIPv6) ? "" : $", {secondaryIPv6}") +
                               $"  ({adapterName})");
            else
                // IPv6 errors are warnings, not hard failures — the adapter may not support IPv6.
                log.AppendLine($"[WARN]  IPv6 DNS on {adapterName}: {ipv6Result.Stderr}");
        }

        // --- Flush DNS cache ---
        log.Append(await FlushDnsAsync());

        return (success, log.ToString());
    }

    // ── Restore automatic DNS ────────────────────────────────────────────────

    /// <summary>
    /// Resets the DNS configuration on <paramref name="adapterName"/> to DHCP-automatic,
    /// then flushes the DNS cache.
    /// </summary>
    public static async Task<(bool success, string log)> RestoreAutomaticDnsAsync(string adapterName)
    {
        var log = new StringBuilder();

        var cmd =
            $"Set-DnsClientServerAddress " +
            $"-InterfaceAlias '{EscapePs(adapterName)}' " +
            $"-ResetServerAddresses";

        var result = await PowerShellService.RunAsync(cmd);

        if (result.ExitCode == 0)
            log.AppendLine($"[OK]    Automatic DNS restored for {adapterName}");
        else
        {
            log.AppendLine($"[ERROR] Restore failed on {adapterName}: {result.Stderr}");
            return (false, log.ToString());
        }

        log.Append(await FlushDnsAsync());
        return (true, log.ToString());
    }

    // ── IPv6 binding ─────────────────────────────────────────────────────────

    /// <summary>Disables the IPv6 (ms_tcpip6) binding on the specified adapter.</summary>
    public static async Task<(bool success, string log)> DisableIPv6Async(string adapterName)
    {
        var cmd =
            $"Disable-NetAdapterBinding " +
            $"-Name '{EscapePs(adapterName)}' " +
            $"-ComponentID ms_tcpip6";

        var result = await PowerShellService.RunAsync(cmd);

        return result.ExitCode == 0
            ? (true,  $"[OK]    IPv6 binding disabled on {adapterName}")
            : (false, $"[ERROR] Failed to disable IPv6 on {adapterName}: {result.Stderr}");
    }

    /// <summary>Re-enables the IPv6 (ms_tcpip6) binding on the specified adapter.</summary>
    public static async Task<(bool success, string log)> EnableIPv6Async(string adapterName)
    {
        var cmd =
            $"Enable-NetAdapterBinding " +
            $"-Name '{EscapePs(adapterName)}' " +
            $"-ComponentID ms_tcpip6";

        var result = await PowerShellService.RunAsync(cmd);

        return result.ExitCode == 0
            ? (true,  $"[OK]    IPv6 binding re-enabled on {adapterName}")
            : (false, $"[ERROR] Failed to enable IPv6 on {adapterName}: {result.Stderr}");
    }

    // ── DNS tests ────────────────────────────────────────────────────────────

    /// <summary>
    /// Tests DNS resolution and connectivity:
    ///   • Resolve discord.com via system DNS
    ///   • Resolve discord.com via Google 8.8.8.8
    ///   • Ping 8.8.8.8
    /// </summary>
    public static async Task<string> TestDnsAsync()
    {
        var log = new StringBuilder();
        log.AppendLine("════ DNS Test ════════════════════════════════════");

        // System DNS resolution
        var sysCmd =
            "try { " +
            "  $r = Resolve-DnsName discord.com -ErrorAction Stop | " +
            "       Where-Object { $_.IPAddress } | " +
            "       Select-Object -ExpandProperty IPAddress -First 3; " +
            "  $r -join ', ' " +
            "} catch { 'FAILED: ' + $_.Exception.Message }";

        var sysResult = await PowerShellService.RunAsync(sysCmd);
        log.AppendLine($"[System DNS]      discord.com  →  " +
                       (string.IsNullOrEmpty(sysResult.Stdout) ? "No response" : sysResult.Stdout));

        // Google DNS resolution
        var googleCmd =
            "try { " +
            "  $r = Resolve-DnsName discord.com -Server 8.8.8.8 -ErrorAction Stop | " +
            "       Where-Object { $_.IPAddress } | " +
            "       Select-Object -ExpandProperty IPAddress -First 3; " +
            "  $r -join ', ' " +
            "} catch { 'FAILED: ' + $_.Exception.Message }";

        var googleResult = await PowerShellService.RunAsync(googleCmd);
        log.AppendLine($"[Google 8.8.8.8]  discord.com  →  " +
                       (string.IsNullOrEmpty(googleResult.Stdout) ? "No response" : googleResult.Stdout));

        // Ping 8.8.8.8 — compatible with both PS 5.1 (ResponseTime) and PS 7+ (Latency)
        var pingCmd =
            "try { " +
            "  $p = Test-Connection 8.8.8.8 -Count 3 -ErrorAction Stop; " +
            "  $prop = if ($p[0].PSObject.Properties['Latency']) { 'Latency' } else { 'ResponseTime' }; " +
            "  $avg = [math]::Round(($p | Measure-Object -Property $prop -Average).Average, 1); " +
            "  'Avg latency: ' + $avg + ' ms  (' + $p.Count + '/3 replies)' " +
            "} catch { 'Unreachable: ' + $_.Exception.Message }";

        var pingResult = await PowerShellService.RunAsync(pingCmd);
        log.AppendLine($"[Ping 8.8.8.8]    " +
                       (string.IsNullOrEmpty(pingResult.Stdout) ? "No response" : pingResult.Stdout));

        log.AppendLine("══════════════════════════════════════════════════");
        return log.ToString();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static async Task<string> FlushDnsAsync()
    {
        var result = await PowerShellService.RunAsync("ipconfig /flushdns");
        return result.ExitCode == 0
            ? "[OK]    DNS resolver cache flushed.\n"
            : $"[WARN]  DNS flush: {result.Stderr}\n";
    }

    /// <summary>
    /// Escapes single quotes in adapter names so they can be safely embedded
    /// inside PowerShell single-quoted strings.
    /// </summary>
    private static string EscapePs(string s) => s.Replace("'", "''");
}
