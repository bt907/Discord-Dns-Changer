using System.IO;
using System.Text;

namespace DNS_Switcher.Services;

/// <summary>
/// Patches C:\Windows\System32\drivers\etc\hosts with direct IP entries for
/// all Discord domains resolved via Google 8.8.8.8.
///
/// Why this helps on Turkish ISPs (e.g. Turkcell Superonline):
///   - ISPs may poison DNS responses AND/OR block specific Cloudflare IP ranges.
///   - Writing IPs from a trusted resolver (Google) directly to hosts bypasses
///     any DNS-level manipulation entirely.
///   - Discord's Cloudflare anycast pool is large; the IP returned by Google may
///     route to a node the ISP does NOT block.
///   - This specifically fixes "stuck on receiving updates" because the update
///     endpoint (cdn.discordapp.com / dl.discordapp.net) is resolved correctly.
/// </summary>
public static class HostsService
{
    private static readonly string HostsPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                     @"drivers\etc\hosts");

    private const string BlockStart = "# ── Discord DNS Switcher ──────────────";
    private const string BlockEnd   = "# ── End Discord DNS Switcher ──────────";

    private static readonly string[] DiscordDomains =
    {
        "discord.com",
        "www.discord.com",
        "discordapp.com",
        "www.discordapp.com",
        "gateway.discord.gg",
        "cdn.discordapp.com",
        "dl.discordapp.net",
        "media.discordapp.net",
        "images-ext-1.discordapp.net",
        "images-ext-2.discordapp.net",
        "status.discordapp.com",
        "updates.discord.com"
    };

    // ── Patch ────────────────────────────────────────────────────────────────

    public static async Task<(bool success, string log)> PatchHostsAsync()
    {
        var log     = new StringBuilder();
        var entries = new List<(string ip, string domain)>();

        log.AppendLine("Resolving Discord domains via Google 8.8.8.8…");

        foreach (var domain in DiscordDomains)
        {
            var cmd =
                $"try {{ " +
                $"  $r = Resolve-DnsName '{domain}' -Server 8.8.8.8 -Type A -ErrorAction Stop | " +
                $"       Where-Object {{ $_.IPAddress }} | " +
                $"       Select-Object -First 1 -ExpandProperty IPAddress; " +
                $"  if ($r) {{ $r }} else {{ '' }} " +
                $"}} catch {{ '' }}";

            var result = await PowerShellService.RunAsync(cmd);
            var ip     = result.Stdout.Trim().Split('\n')[0].Trim();

            if (!string.IsNullOrEmpty(ip) && System.Net.IPAddress.TryParse(ip, out _))
            {
                entries.Add((ip, domain));
                log.AppendLine($"  {domain,-45} → {ip}");
            }
            else
            {
                log.AppendLine($"  {domain,-45} → (could not resolve)");
            }
        }

        if (entries.Count == 0)
        {
            log.AppendLine("[FAIL] Could not resolve any Discord domain via Google DNS.");
            return (false, log.ToString());
        }

        // Build new block
        var block = new StringBuilder();
        block.AppendLine();
        block.AppendLine(BlockStart);
        block.AppendLine($"# Added by Discord DNS Switcher  {DateTime.Now:yyyy-MM-dd HH:mm}");
        block.AppendLine($"# Resolved via Google 8.8.8.8 to bypass ISP DNS manipulation");
        foreach (var (ip, domain) in entries)
            block.AppendLine($"{ip,-20} {domain}");
        block.AppendLine(BlockEnd);

        // Read, strip old block, append new
        var existing = File.Exists(HostsPath) ? File.ReadAllText(HostsPath) : string.Empty;
        File.WriteAllText(HostsPath, StripBlock(existing) + block.ToString(),
                          Encoding.UTF8);

        log.AppendLine(string.Empty);
        log.AppendLine($"[OK] Wrote {entries.Count} entries to hosts file.");

        // Flush DNS cache so the new hosts entries take effect immediately
        log.AppendLine("Flushing DNS cache…");
        var flush = await PowerShellService.RunAsync("ipconfig /flushdns");
        log.AppendLine(flush.ExitCode == 0 ? "[OK] DNS cache flushed." : $"[WARN] flush exit {flush.ExitCode}");

        return (true, log.ToString());
    }

    // ── Remove ───────────────────────────────────────────────────────────────

    public static (bool success, string log) RemoveHosts()
    {
        if (!File.Exists(HostsPath))
            return (true, "Hosts file not found.");

        var content = File.ReadAllText(HostsPath);
        if (!content.Contains(BlockStart))
            return (true, "No Discord entries in hosts file — nothing to remove.");

        File.WriteAllText(HostsPath, StripBlock(content), Encoding.UTF8);
        return (true, "[OK] Discord entries removed from hosts file.");
    }

    // ── Status ───────────────────────────────────────────────────────────────

    public static bool HasHostsEntries()
    {
        try
        {
            return File.Exists(HostsPath) &&
                   File.ReadAllText(HostsPath).Contains(BlockStart);
        }
        catch { return false; }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string StripBlock(string content)
    {
        var start = content.IndexOf(BlockStart, StringComparison.Ordinal);
        if (start < 0) return content;

        var end = content.IndexOf(BlockEnd, start, StringComparison.Ordinal);
        if (end < 0) return content[..start].TrimEnd() + Environment.NewLine;

        var after = content[(end + BlockEnd.Length)..];
        return content[..start].TrimEnd() + Environment.NewLine + after.TrimStart('\r', '\n');
    }
}
