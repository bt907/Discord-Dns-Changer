namespace DNS_Switcher.Models;

/// <summary>
/// Describes a DNS provider with its IPv4 and IPv6 server addresses.
/// </summary>
public class DnsProvider
{
    public string Name        { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string PrimaryIPv4   { get; set; } = string.Empty;
    public string SecondaryIPv4 { get; set; } = string.Empty;
    public string PrimaryIPv6   { get; set; } = string.Empty;
    public string SecondaryIPv6 { get; set; } = string.Empty;

    // ── Built-in presets ────────────────────────────────────────────────────

    public static readonly DnsProvider Google = new()
    {
        Name           = "Google",
        Description    = "Fast & reliable",
        PrimaryIPv4    = "8.8.8.8",
        SecondaryIPv4  = "8.8.4.4",
        PrimaryIPv6    = "2001:4860:4860::8888",
        SecondaryIPv6  = "2001:4860:4860::8844"
    };

    public static readonly DnsProvider Cloudflare = new()
    {
        Name           = "Cloudflare",
        Description    = "Privacy-focused, fastest",
        PrimaryIPv4    = "1.1.1.1",
        SecondaryIPv4  = "1.0.0.1",
        PrimaryIPv6    = "2606:4700:4700::1111",
        SecondaryIPv6  = "2606:4700:4700::1001"
    };

    public static readonly DnsProvider Quad9 = new()
    {
        Name           = "Quad9",
        Description    = "Security-focused, blocks malware",
        PrimaryIPv4    = "9.9.9.9",
        SecondaryIPv4  = "149.112.112.112",
        PrimaryIPv6    = "2620:fe::fe",
        SecondaryIPv6  = "2620:fe::9"
    };
}
