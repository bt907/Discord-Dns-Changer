# DNS Switcher

A clean, dark-themed Windows desktop utility for quickly changing DNS settings
on Wi-Fi and Ethernet adapters — with special handling for the Windows 11
IPv6 DNS override problem.

---

## Requirements

| Requirement       | Version               |
|-------------------|-----------------------|
| .NET SDK          | 8.0 or later          |
| OS                | Windows 10 / 11       |
| Privilege         | Administrator (to change DNS) |

---

## How to Build

```powershell
# From the project folder (where DNS_Switcher.csproj lives)
dotnet build -c Release

# Or publish a self-contained single-file executable
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

The compiled executable lands in `bin\Release\net8.0-windows\`.

---

## How to Run as Administrator

DNS changes on Windows require elevated privileges.  DNS Switcher does **not**
auto-elevate itself.  You have two options:

### Option A — Right-click → "Run as administrator"
Right-click `DNS_Switcher.exe` in Explorer and choose **Run as administrator**.

### Option B — Use the in-app button
Launch the app normally.  If it detects it is **not** elevated it shows an
orange warning banner at the top.  Click **"Restart as Administrator"** and
approve the UAC prompt.  The app relaunches with full privileges automatically.

---

## DNS Providers

### Google Public DNS
| Type | Primary            | Secondary          |
|------|--------------------|--------------------|
| IPv4 | `8.8.8.8`          | `8.8.4.4`          |
| IPv6 | `2001:4860:4860::8888` | `2001:4860:4860::8844` |

Google DNS is fast, globally distributed, and widely used.  Good default choice.

### Cloudflare DNS (1.1.1.1)
| Type | Primary            | Secondary          |
|------|--------------------|--------------------|
| IPv4 | `1.1.1.1`          | `1.0.0.1`          |
| IPv6 | `2606:4700:4700::1111` | `2606:4700:4700::1001` |

Cloudflare DNS is consistently ranked the **fastest** DNS resolver worldwide.
It also has a strong privacy policy — they do not log personal data.

### Quad9
| Type | Primary            | Secondary          |
|------|--------------------|--------------------|
| IPv4 | `9.9.9.9`          | `149.112.112.112`  |
| IPv6 | `2620:fe::fe`      | `2620:fe::9`       |

Quad9 **blocks domains known to host malware, ransomware, and phishing** using
threat intelligence from multiple security partners.  Choose this for extra
security without installing additional software.

---

## Why IPv6 DNS Matters on Windows 11

Windows prefers IPv6 over IPv4 when both are available.  This means the
**system resolves DNS names using the IPv6 DNS server first**.

If you set only the IPv4 DNS (e.g. `8.8.8.8`) but leave the IPv6 DNS
untouched (pointing at your ISP), Windows will often **ignore your IPv4 DNS**
and use the IPv6 one instead.

### Symptoms
- Websites load slowly even after changing DNS.
- `nslookup discord.com` resolves differently than expected.
- Discord, Roblox, or other services still fail after changing DNS.

### Solution
- **Always check "Set IPv6 DNS too"** (enabled by default).
- If that still does not work, use **"Disable IPv6 on selected adapter"** to
  remove IPv6 entirely from the adapter.  Only do this as a last resort.

---

## How to Restore Automatic DNS

Click **"↺ Restore Automatic DNS"** in the Actions section.

This runs:
```powershell
Set-DnsClientServerAddress -InterfaceAlias "<adapter>" -ResetServerAddresses
```

The adapter will then receive DNS servers from your router via DHCP — the
same behaviour as a factory-reset network connection.

---

## Safety Notes

- The app **never silently disables IPv6**.  A confirmation dialog always
  appears first.
- The "Disable IPv6" checkbox re-enables IPv6 automatically when unchecked.
- Virtual adapters (Hyper-V, Docker, VPN, Bluetooth PAN, Loopback) are
  **excluded** from the adapter list to prevent accidental changes.
- All PowerShell commands are run with `-NonInteractive` and
  `-ExecutionPolicy Bypass` so they never prompt the user.
- Errors are displayed in the log panel; the app never crashes silently.

---

## Project Structure

```
DNS_Switcher/
├── DNS_Switcher.csproj       – .NET 8 WPF project file
├── app.manifest              – UAC manifest (asInvoker) + DPI awareness
├── App.xaml / App.xaml.cs    – WPF application entry point
├── MainWindow.xaml           – Full dark-mode UI layout
├── MainWindow.xaml.cs        – UI event handlers & orchestration
├── Models/
│   ├── NetworkAdapterInfo.cs – Adapter data model
│   └── DnsProvider.cs        – DNS preset definitions
└── Services/
    ├── AdminService.cs       – Admin check & UAC elevation
    ├── PowerShellService.cs  – Runs PowerShell commands safely (async)
    ├── AdapterService.cs     – Discovers adapters + reads current DNS
    └── DnsService.cs         – Apply / restore / disable-IPv6 / test
```

---

## Keyboard Shortcuts

| Action                  | Shortcut     |
|-------------------------|--------------|
| Refresh adapter list    | Click ↻      |
| Clear log               | Click Clear  |
| Apply DNS               | Click ▶ Apply DNS |

---

## Troubleshooting

**"No active adapters found"**  
Make sure at least one physical adapter is connected and shows Status = Up
in Device Manager.

**"Access Denied" in the log**  
The app is not running as Administrator.  Use the banner button to restart elevated.

**IPv6 DNS command fails**  
Some adapters or older Windows builds do not expose an IPv6 interface.
This is a warning, not an error — the IPv4 DNS change still applies.

**DNS test shows "FAILED"**  
Your network may be blocking DNS queries on port 53 to external servers
(common on corporate or school networks).
