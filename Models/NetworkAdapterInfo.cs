namespace DNS_Switcher.Models;

/// <summary>
/// Represents a physical network adapter with its current DNS configuration.
/// </summary>
public class NetworkAdapterInfo
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string LinkSpeed { get; set; } = string.Empty;

    /// <summary>IPv4 DNS server addresses currently configured on this adapter.</summary>
    public List<string> IPv4Dns { get; set; } = new();

    /// <summary>IPv6 DNS server addresses currently configured on this adapter.</summary>
    public List<string> IPv6Dns { get; set; } = new();

    public string IPv4DnsDisplay => IPv4Dns.Count > 0
        ? string.Join(", ", IPv4Dns)
        : "Automatic";

    public string IPv6DnsDisplay => IPv6Dns.Count > 0
        ? string.Join(", ", IPv6Dns)
        : "Automatic";

    /// <summary>
    /// Returns a human-readable label for the current DNS configuration,
    /// matching against known provider primary addresses.
    /// </summary>
    public string CurrentDnsLabel
    {
        get
        {
            var primary = IPv4Dns.FirstOrDefault() ?? string.Empty;
            return primary switch
            {
                "8.8.8.8"   => "Google",
                "1.1.1.1"   => "Cloudflare",
                "9.9.9.9"   => "Quad9",
                ""          => "Automatic",
                _           => "Custom"
            };
        }
    }
}
