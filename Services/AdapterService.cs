using System.Text.Json;
using DNS_Switcher.Models;

namespace DNS_Switcher.Services;

/// <summary>
/// Discovers active physical network adapters and their current DNS settings
/// by executing PowerShell commands and parsing the JSON output.
/// </summary>
public static class AdapterService
{
    // Keywords whose presence in the adapter description indicates a virtual/software adapter.
    // Use whole-word / prefix matching where possible to avoid false positives
    // (e.g. "Virtual" catches VMware/VirtualBox but NOT e.g. "Intel Virtual Wire").
    private static readonly string[] VirtualKeywords =
    {
        "hyper-v", "docker", "vpn", "bluetooth pan", "loopback",
        "virtual", "vmware", "virtualbox", "tap-windows", "tap adapter",
        "wan miniport", "isatap", "teredo", "6to4", "pseudo", "ndis capture"
    };

    /// <summary>
    /// Returns all active physical adapters together with their IPv4/IPv6 DNS configuration.
    /// </summary>
    public static async Task<List<NetworkAdapterInfo>> GetActiveAdaptersAsync()
    {
        var adapters = await FetchAdaptersAsync();
        if (adapters.Count > 0)
            await PopulateDnsAsync(adapters);
        return adapters;
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static async Task<List<NetworkAdapterInfo>> FetchAdaptersAsync()
    {
        const string cmd =
            "Get-NetAdapter | " +
            "Where-Object { $_.Status -eq 'Up' -and $_.HardwareInterface -eq $true } | " +
            "Select-Object Name, InterfaceDescription, Status, LinkSpeed | " +
            "ConvertTo-Json -Depth 3";

        var result = await PowerShellService.RunAsync(cmd);
        var adapters = new List<NetworkAdapterInfo>();

        if (string.IsNullOrWhiteSpace(result.Stdout))
            return adapters;

        try
        {
            foreach (var element in ParseJsonArray(result.Stdout))
            {
                var name = GetString(element, "Name");
                var desc = GetString(element, "InterfaceDescription");

                if (IsVirtualAdapter(desc))
                    continue;

                adapters.Add(new NetworkAdapterInfo
                {
                    Name        = name,
                    Description = desc,
                    Status      = GetString(element, "Status"),
                    LinkSpeed   = GetLinkSpeed(element)
                });
            }
        }
        catch
        {
            // If parsing fails, return an empty list rather than crashing.
        }

        return adapters;
    }

    private static async Task PopulateDnsAsync(List<NetworkAdapterInfo> adapters)
    {
        // Fetch all DNS entries; filter out adapters with no server addresses.
        const string cmd =
            "Get-DnsClientServerAddress | " +
            "Where-Object { $_.ServerAddresses.Count -gt 0 } | " +
            "Select-Object InterfaceAlias, AddressFamily, ServerAddresses | " +
            "ConvertTo-Json -Depth 5";

        var result = await PowerShellService.RunAsync(cmd);

        if (string.IsNullOrWhiteSpace(result.Stdout))
            return;

        try
        {
            foreach (var element in ParseJsonArray(result.Stdout))
            {
                var alias     = GetString(element, "InterfaceAlias");
                var family    = ParseAddressFamily(element);
                var addresses = ParseAddressList(element);

                var adapter = adapters.FirstOrDefault(
                    a => a.Name.Equals(alias, StringComparison.OrdinalIgnoreCase));

                if (adapter is null) continue;

                // AddressFamily numeric: 2 = IPv4, 23 = IPv6
                if (family == 2)       adapter.IPv4Dns = addresses;
                else if (family == 23) adapter.IPv6Dns = addresses;
            }
        }
        catch
        {
            // Silently ignore DNS parse errors; adapters will show "Automatic".
        }
    }

    // ── JSON helpers ─────────────────────────────────────────────────────────

    /// <summary>Parses a JSON string that is either an object or an array into a list of elements.</summary>
    private static IEnumerable<JsonElement> ParseJsonArray(string json)
    {
        var trimmed = json.Trim();

        if (trimmed.StartsWith('['))
        {
            var array = JsonSerializer.Deserialize<List<JsonElement>>(trimmed);
            return array ?? Enumerable.Empty<JsonElement>();
        }

        if (trimmed.StartsWith('{'))
        {
            var single = JsonSerializer.Deserialize<JsonElement>(trimmed);
            return new[] { single };
        }

        return Enumerable.Empty<JsonElement>();
    }

    private static string GetString(JsonElement el, string key)
    {
        if (el.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.String)
            return prop.GetString() ?? string.Empty;
        return string.Empty;
    }

    private static string GetLinkSpeed(JsonElement el)
    {
        if (!el.TryGetProperty("LinkSpeed", out var prop)) return string.Empty;

        // PowerShell may return LinkSpeed as a number (bps) or a string
        if (prop.ValueKind == JsonValueKind.String) return prop.GetString() ?? string.Empty;
        if (prop.ValueKind == JsonValueKind.Number)
        {
            // Convert bps → human-readable
            var bps = prop.GetInt64();
            return bps >= 1_000_000_000
                ? $"{bps / 1_000_000_000} Gbps"
                : $"{bps / 1_000_000} Mbps";
        }
        return string.Empty;
    }

    /// <summary>
    /// Reads AddressFamily from a JSON element.
    /// Handles both numeric values (2, 23) and string names (InterNetwork, InterNetworkV6, IPv4, IPv6).
    /// </summary>
    private static int ParseAddressFamily(JsonElement el)
    {
        if (!el.TryGetProperty("AddressFamily", out var prop)) return -1;

        if (prop.ValueKind == JsonValueKind.Number)
            return prop.GetInt32();

        if (prop.ValueKind == JsonValueKind.String)
        {
            return prop.GetString() switch
            {
                "IPv4" or "InterNetwork"   => 2,
                "IPv6" or "InterNetworkV6" => 23,
                _                          => -1
            };
        }

        return -1;
    }

    private static List<string> ParseAddressList(JsonElement el)
    {
        var list = new List<string>();

        if (!el.TryGetProperty("ServerAddresses", out var prop)) return list;

        if (prop.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in prop.EnumerateArray())
            {
                var addr = item.GetString();
                if (!string.IsNullOrEmpty(addr)) list.Add(addr);
            }
        }

        return list;
    }

    private static bool IsVirtualAdapter(string description)
    {
        var lower = description.ToLowerInvariant();
        return VirtualKeywords.Any(lower.Contains);
    }
}
