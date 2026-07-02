using System.Collections.ObjectModel;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DNS_Switcher.Models;
using DNS_Switcher.Services;

namespace DNS_Switcher;

public partial class MainWindow : Window
{
    // ── State ────────────────────────────────────────────────────────────────

    private readonly ObservableCollection<NetworkAdapterInfo> _adapters = new();
    private bool _isLoading;

    private static readonly SolidColorBrush AccentBrush  = new(Color.FromRgb(0x5B, 0x6C, 0xF5));
    private static readonly SolidColorBrush NeutralBrush = new(Color.FromRgb(0x2E, 0x2E, 0x4A));

    // ── Constructor ──────────────────────────────────────────────────────────

    public MainWindow()
    {
        InitializeComponent();
        AdapterListBox.ItemsSource = _adapters;

        Loaded += async (_, _) =>
        {
            CheckAdminStatus();
            CheckDiscordStatus();
            await LoadAdaptersAsync();
        };
    }

    // ── Admin check ──────────────────────────────────────────────────────────

    private void CheckAdminStatus()
    {
        if (AdminService.IsRunningAsAdmin())
        {
            AdminWarningPanel.Visibility = Visibility.Collapsed;
            Log("INFO", "Running as Administrator.");
        }
        else
        {
            AdminWarningPanel.Visibility = Visibility.Visible;

            AdminBadge.Background   = new SolidColorBrush(Color.FromRgb(0x2A, 0x0A, 0x0A));
            AdminBadge.BorderBrush  = new SolidColorBrush(Color.FromRgb(0xE8, 0x50, 0x50));
            AdminBadgeText.Text       = "● No Admin Rights";
            AdminBadgeText.Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0x50, 0x50));

            // DNS changes require elevation — disable mutation buttons
            ApplyBtn.IsEnabled       = false;
            RestoreBtn.IsEnabled     = false;
            ChkDisableIPv6.IsEnabled = false;

            Log("WARN", "NOT running as Administrator.");
            Log("WARN", "DNS changes will be BLOCKED by Windows until you restart as Administrator.");
            Log("WARN", "Click the orange 'Restart as Administrator' button above.");
        }
    }

    private void RestartAdmin_Click(object sender, RoutedEventArgs e)
        => AdminService.RestartAsAdmin();

    // ── Discord status ───────────────────────────────────────────────────────

    private void CheckDiscordStatus()
    {
        var installed = DiscordService.IsDiscordInstalled();
        var version   = DiscordService.GetDiscordVersion();

        if (installed)
        {
            DiscordBadge.Background  = new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x3A));
            DiscordBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(0x58, 0x65, 0xF2));
            DiscordBadgeText.Text       = $"● Discord {version ?? "installed"}";
            DiscordBadgeText.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x99, 0xFF));
            Log("INFO", $"Discord detected — version {version ?? "unknown"}.");
        }
        else
        {
            DiscordBadge.Background  = new SolidColorBrush(Color.FromRgb(0x2A, 0x15, 0x0A));
            DiscordBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(0xE8, 0x50, 0x50));
            DiscordBadgeText.Text       = "● Discord: Not installed";
            DiscordBadgeText.Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0x80, 0x70));
            Log("WARN", "Discord not detected on this machine.");
        }
    }

    // ── Test Discord connectivity ─────────────────────────────────────────────

    private async void TestDiscord_Click(object sender, RoutedEventArgs e)
    {
        TestDiscordBtn.IsEnabled = false;
        Log("----", "Starting Discord connectivity test…");

        try
        {
            var result = await DnsService.TestDiscordConnectivityAsync();
            LogBlock(result);
        }
        catch (Exception ex)
        {
            Log("ERR ", $"Test threw exception: {ex.Message}");
        }
        finally
        {
            TestDiscordBtn.IsEnabled = true;
        }
    }

    // ── Adapter loading ──────────────────────────────────────────────────────

    private async Task LoadAdaptersAsync()
    {
        if (_isLoading) return;
        _isLoading = true;
        RefreshBtn.IsEnabled = false;

        Log("----", "Scanning for active physical adapters…");

        try
        {
            var found = await AdapterService.GetActiveAdaptersAsync();

            _adapters.Clear();
            foreach (var a in found)
                _adapters.Add(a);

            if (_adapters.Count == 0)
            {
                NoAdaptersText.Visibility = Visibility.Visible;
                StatusLabel.Text = "Current DNS: No active adapters found";
                Log("WARN", "No active physical adapters found.");
            }
            else
            {
                NoAdaptersText.Visibility = Visibility.Collapsed;
                Log("INFO", $"Found {_adapters.Count} adapter(s):");
                foreach (var a in _adapters)
                    Log("    ", $"{a.Name}  |  IPv4: {a.IPv4DnsDisplay}  |  IPv6: {a.IPv6DnsDisplay}");

                if (AdapterListBox.SelectedItem is null)
                    AdapterListBox.SelectedIndex = 0;
            }
        }
        catch (Exception ex)
        {
            Log("ERR ", $"Failed to load adapters: {ex.Message}");
        }
        finally
        {
            RefreshBtn.IsEnabled = true;
            _isLoading = false;
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
        => await LoadAdaptersAsync();

    private void AdapterListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AdapterListBox.SelectedItem is NetworkAdapterInfo adapter)
        {
            UpdateStatusLabel(adapter);
            Log("INFO", $"Selected adapter: {adapter.Name}  ({adapter.Description})");
            Log("    ", $"IPv4 DNS: {adapter.IPv4DnsDisplay}");
            Log("    ", $"IPv6 DNS: {adapter.IPv6DnsDisplay}");
        }
    }

    private void UpdateStatusLabel(NetworkAdapterInfo adapter)
    {
        var label  = adapter.CurrentDnsLabel;
        var detail = adapter.IPv4Dns.Count > 0 ? $"  [{adapter.IPv4DnsDisplay}]" : string.Empty;
        StatusLabel.Text = $"Current DNS: {label}{detail}";
    }

    // ── Preset selection ─────────────────────────────────────────────────────

    private void Google_Click(object sender, RoutedEventArgs e)
        => SelectPreset(DnsProvider.Google, GoogleBtn);

    private void Cloudflare_Click(object sender, RoutedEventArgs e)
        => SelectPreset(DnsProvider.Cloudflare, CloudflareBtn);

    private void Quad9_Click(object sender, RoutedEventArgs e)
        => SelectPreset(DnsProvider.Quad9, Quad9Btn);

    private void SelectPreset(DnsProvider provider, Button clickedBtn)
    {
        // Reset all card borders
        GoogleBtn.BorderBrush     = NeutralBrush;
        CloudflareBtn.BorderBrush = NeutralBrush;
        Quad9Btn.BorderBrush      = NeutralBrush;
        GoogleBtn.BorderThickness     = new Thickness(1);
        CloudflareBtn.BorderThickness = new Thickness(1);
        Quad9Btn.BorderThickness      = new Thickness(1);

        clickedBtn.BorderBrush     = AccentBrush;
        clickedBtn.BorderThickness = new Thickness(2);

        // Fill fields so the user can review before applying
        TxtIPv4Primary.Text   = provider.PrimaryIPv4;
        TxtIPv4Secondary.Text = provider.SecondaryIPv4;
        TxtIPv6Primary.Text   = provider.PrimaryIPv6;
        TxtIPv6Secondary.Text = provider.SecondaryIPv6;

        Log("INFO", $"Preset: {provider.Name}  —  IPv4: {provider.PrimaryIPv4} / {provider.SecondaryIPv4}");
        Log("    ",  $"IPv6: {provider.PrimaryIPv6} / {provider.SecondaryIPv6}");
    }

    // ── Apply DNS ────────────────────────────────────────────────────────────

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        var primaryIPv4   = TxtIPv4Primary.Text.Trim();
        var secondaryIPv4 = TxtIPv4Secondary.Text.Trim();
        var primaryIPv6   = TxtIPv6Primary.Text.Trim();
        var secondaryIPv6 = TxtIPv6Secondary.Text.Trim();
        bool setIPv6      = ChkSetIPv6.IsChecked == true;

        // ── Validate ─────────────────────────────────────────────────────────
        if (string.IsNullOrEmpty(primaryIPv4))
        {
            MessageBox.Show("Please select a DNS preset or enter a Primary IPv4 address.",
                "Missing DNS", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!IPAddress.TryParse(primaryIPv4, out _))
        {
            MessageBox.Show($"Invalid Primary IPv4 address: {primaryIPv4}",
                "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!string.IsNullOrEmpty(secondaryIPv4) && !IPAddress.TryParse(secondaryIPv4, out _))
        {
            MessageBox.Show($"Invalid Secondary IPv4 address: {secondaryIPv4}",
                "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (setIPv6 && !string.IsNullOrEmpty(primaryIPv6) && !IPAddress.TryParse(primaryIPv6, out _))
        {
            MessageBox.Show($"Invalid Primary IPv6 address: {primaryIPv6}",
                "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var targets = GetTargetAdapters();
        if (targets is null) return;

        ApplyBtn.IsEnabled = false;

        try
        {
            foreach (var adapter in targets)
            {
                Log("====", $"Applying DNS to [{adapter.Name}]");
                var (success, logText) = await DnsService.ApplyDnsAsync(
                    adapter.Name, primaryIPv4, secondaryIPv4, primaryIPv6, secondaryIPv6, setIPv6);
                LogBlock(logText);
                Log(success ? " OK " : "FAIL", success
                    ? $"DNS change completed on [{adapter.Name}]."
                    : $"DNS change had errors on [{adapter.Name}]. See output above.");
            }

            await LoadAdaptersAsync();
        }
        catch (Exception ex)
        {
            Log("ERR ", $"Unhandled exception: {ex.Message}");
        }
        finally
        {
            ApplyBtn.IsEnabled = true;
        }
    }

    // ── Restore automatic DNS ────────────────────────────────────────────────

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        var targets = GetTargetAdapters();
        if (targets is null) return;

        var label = ChkApplyAll.IsChecked == true ? "all active adapters" : targets[0].Name;

        var confirm = MessageBox.Show(
            $"Reset DNS to automatic (DHCP) for {label}?\n\nThis removes any manually configured DNS servers.",
            "Confirm Reset", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        RestoreBtn.IsEnabled = false;

        try
        {
            foreach (var adapter in targets)
            {
                Log("====", $"Restoring automatic DNS on [{adapter.Name}]");
                var (success, logText) = await DnsService.RestoreAutomaticDnsAsync(adapter.Name);
                LogBlock(logText);
                Log(success ? " OK " : "FAIL", success
                    ? $"Automatic DNS restored on [{adapter.Name}]."
                    : $"Restore had errors on [{adapter.Name}].");
            }

            await LoadAdaptersAsync();
        }
        catch (Exception ex)
        {
            Log("ERR ", $"Unhandled exception: {ex.Message}");
        }
        finally
        {
            RestoreBtn.IsEnabled = true;
        }
    }

    // ── Generic DNS test ─────────────────────────────────────────────────────

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        TestBtn.IsEnabled = false;
        Log("----", "Running DNS connectivity test…");
        try
        {
            var result = await DnsService.TestDiscordConnectivityAsync();
            LogBlock(result);
        }
        catch (Exception ex) { Log("ERR ", ex.Message); }
        finally { TestBtn.IsEnabled = true; }
    }

    // ── Disable / enable IPv6 ────────────────────────────────────────────────

    private async void ChkDisableIPv6_Checked(object sender, RoutedEventArgs e)
    {
        var adapter = AdapterListBox.SelectedItem as NetworkAdapterInfo;
        if (adapter is null)
        {
            MessageBox.Show("Please select an adapter first.",
                "No Adapter Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
            ChkDisableIPv6.IsChecked = false;
            return;
        }

        var confirm = MessageBox.Show(
            $"Disable IPv6 on \"{adapter.Name}\"?\n\n" +
            "This removes IPv6 connectivity from the adapter entirely.\n" +
            "Only do this if setting the IPv6 DNS did not fix Discord.",
            "Confirm Disable IPv6", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
        {
            ChkDisableIPv6.IsChecked = false;
            return;
        }

        Log("====", $"Disabling IPv6 on [{adapter.Name}]");
        var (_, logText) = await DnsService.DisableIPv6Async(adapter.Name);
        LogBlock(logText);
    }

    private async void ChkDisableIPv6_Unchecked(object sender, RoutedEventArgs e)
    {
        var adapter = AdapterListBox.SelectedItem as NetworkAdapterInfo;
        if (adapter is null) return;

        Log("====", $"Re-enabling IPv6 on [{adapter.Name}]");
        var (_, logText) = await DnsService.EnableIPv6Async(adapter.Name);
        LogBlock(logText);
    }

    // ── Log helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Appends a single log line with timestamp and a 4-char level tag.
    /// Format:  [HH:mm:ss] [INFO] message
    /// </summary>
    private int _logLineCount;

    private void Log(string level, string message)
    {
        var ts   = DateTime.Now.ToString("HH:mm:ss");
        var line = $"[{ts}] [{level}] {message}{Environment.NewLine}";

        Dispatcher.Invoke(() =>
        {
            LogBox.AppendText(line);
            LogBox.ScrollToEnd();
            _logLineCount++;
            LogCountLabel.Text = $"{_logLineCount} lines";
        });
    }

    /// <summary>
    /// Appends a multi-line block from a service method, giving each
    /// individual line its own timestamp so nothing gets lost.
    /// </summary>
    private void LogBlock(string block)
    {
        if (string.IsNullOrWhiteSpace(block)) return;

        foreach (var line in block.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(trimmed)) continue;
            Log("    ", trimmed);
        }
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e)
    {
        LogBox.Clear();
        _logLineCount = 0;
        LogCountLabel.Text = "0 lines";
    }

    // ── Utility ──────────────────────────────────────────────────────────────

    private List<NetworkAdapterInfo>? GetTargetAdapters()
    {
        if (ChkApplyAll.IsChecked == true)
            return new List<NetworkAdapterInfo>(_adapters);

        if (AdapterListBox.SelectedItem is NetworkAdapterInfo selected)
            return new List<NetworkAdapterInfo> { selected };

        MessageBox.Show(
            "Please select an adapter from the left panel,\nor tick \"Apply to all active adapters\".",
            "No Adapter Selected", MessageBoxButton.OK, MessageBoxImage.Warning);

        return null;
    }
}
