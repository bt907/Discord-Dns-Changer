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
    private DnsProvider? _selectedPreset;
    private bool _isLoading;

    // Accent / neutral brushes reused for preset card selection highlighting
    private static readonly SolidColorBrush AccentBrush  = new(Color.FromRgb(0x5B, 0x6C, 0xF5));
    private static readonly SolidColorBrush NeutralBrush = new(Color.FromRgb(0x2E, 0x2E, 0x4A));

    // ── Constructor ──────────────────────────────────────────────────────────

    public MainWindow()
    {
        InitializeComponent();

        // Bind the adapter list to the ListBox
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
        }
        else
        {
            AdminWarningPanel.Visibility = Visibility.Visible;

            // Update the badge to show "No Admin" state
            AdminBadge.Background   = new SolidColorBrush(Color.FromRgb(0x2A, 0x0A, 0x0A));
            AdminBadge.BorderBrush  = new SolidColorBrush(Color.FromRgb(0xE8, 0x50, 0x50));
            AdminBadgeText.Text     = "● No Admin Rights";
            AdminBadgeText.Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0x50, 0x50));

            // Disable mutating actions — user can still test / inspect
            ApplyBtn.IsEnabled       = false;
            RestoreBtn.IsEnabled     = false;
            ChkDisableIPv6.IsEnabled = false;

            Log("[WARN] Not running as Administrator. DNS changes are disabled.");
            Log("       Click \"Restart as Administrator\" to elevate.");
        }
    }

    private void RestartAdmin_Click(object sender, RoutedEventArgs e)
        => AdminService.RestartAsAdmin();

    // ── Adapter loading ──────────────────────────────────────────────────────

    private async Task LoadAdaptersAsync()
    {
        if (_isLoading) return;
        _isLoading = true;
        RefreshBtn.IsEnabled = false;

        Log("Scanning for active physical adapters…");

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
                Log("[WARN] No active physical adapters found.");
            }
            else
            {
                NoAdaptersText.Visibility = Visibility.Collapsed;
                Log($"Found {_adapters.Count} active adapter(s).");

                // Auto-select first adapter
                if (AdapterListBox.SelectedItem is null)
                    AdapterListBox.SelectedIndex = 0;
            }
        }
        catch (Exception ex)
        {
            Log($"[ERROR] Failed to load adapters: {ex.Message}");
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
            Log($"Selected: {adapter.Name}  ({adapter.Description})");
        }
    }

    private void UpdateStatusLabel(NetworkAdapterInfo adapter)
    {
        var label = adapter.CurrentDnsLabel;
        var detail = adapter.IPv4Dns.Count > 0
            ? $"  [{adapter.IPv4DnsDisplay}]"
            : string.Empty;

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
        _selectedPreset = provider;

        // Reset all card borders to neutral
        GoogleBtn.BorderBrush     = NeutralBrush;
        CloudflareBtn.BorderBrush = NeutralBrush;
        Quad9Btn.BorderBrush      = NeutralBrush;
        GoogleBtn.BorderThickness     = new Thickness(1);
        CloudflareBtn.BorderThickness = new Thickness(1);
        Quad9Btn.BorderThickness      = new Thickness(1);

        // Highlight the selected card
        clickedBtn.BorderBrush     = AccentBrush;
        clickedBtn.BorderThickness = new Thickness(2);

        // Fill in the text fields so the user can review or tweak before applying
        TxtIPv4Primary.Text   = provider.PrimaryIPv4;
        TxtIPv4Secondary.Text = provider.SecondaryIPv4;
        TxtIPv6Primary.Text   = provider.PrimaryIPv6;
        TxtIPv6Secondary.Text = provider.SecondaryIPv6;

        Log($"Preset selected: {provider.Name}  " +
            $"(IPv4 {provider.PrimaryIPv4} / {provider.SecondaryIPv4})");
    }

    // ── Apply DNS ────────────────────────────────────────────────────────────

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        // Gather and validate inputs
        var primaryIPv4   = TxtIPv4Primary.Text.Trim();
        var secondaryIPv4 = TxtIPv4Secondary.Text.Trim();
        var primaryIPv6   = TxtIPv6Primary.Text.Trim();
        var secondaryIPv6 = TxtIPv6Secondary.Text.Trim();
        bool setIPv6      = ChkSetIPv6.IsChecked == true;

        if (string.IsNullOrEmpty(primaryIPv4))
        {
            MessageBox.Show(
                "Please select a DNS preset or enter a Primary IPv4 address.",
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

        if (setIPv6 && !string.IsNullOrEmpty(secondaryIPv6) && !IPAddress.TryParse(secondaryIPv6, out _))
        {
            MessageBox.Show($"Invalid Secondary IPv6 address: {secondaryIPv6}",
                "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Determine target adapters
        var targets = GetTargetAdapters();
        if (targets is null) return;

        ApplyBtn.IsEnabled = false;

        try
        {
            foreach (var adapter in targets)
            {
                Log($"─── Applying DNS to {adapter.Name} ───");
                var (_, logText) = await DnsService.ApplyDnsAsync(
                    adapter.Name,
                    primaryIPv4,   secondaryIPv4,
                    primaryIPv6,   secondaryIPv6,
                    setIPv6);
                Log(logText);
            }

            // Refresh the adapter list so the UI reflects the new DNS
            await LoadAdaptersAsync();
        }
        catch (Exception ex)
        {
            Log($"[ERROR] {ex.Message}");
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

        var adapterLabel = ChkApplyAll.IsChecked == true
            ? "all active adapters"
            : targets[0].Name;

        var confirm = MessageBox.Show(
            $"Reset DNS to automatic (DHCP) for {adapterLabel}?\n\n" +
            "This will remove any manually configured DNS servers.",
            "Confirm Reset", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        RestoreBtn.IsEnabled = false;

        try
        {
            foreach (var adapter in targets)
            {
                Log($"─── Restoring DNS for {adapter.Name} ───");
                var (_, logText) = await DnsService.RestoreAutomaticDnsAsync(adapter.Name);
                Log(logText);
            }

            await LoadAdaptersAsync();
        }
        catch (Exception ex)
        {
            Log($"[ERROR] {ex.Message}");
        }
        finally
        {
            RestoreBtn.IsEnabled = true;
        }
    }

    // ── Test DNS ─────────────────────────────────────────────────────────────

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        TestBtn.IsEnabled = false;
        Log("─── Running DNS connectivity tests ───");

        try
        {
            var result = await DnsService.TestDnsAsync();
            Log(result);
        }
        catch (Exception ex)
        {
            Log($"[ERROR] Test failed: {ex.Message}");
        }
        finally
        {
            TestBtn.IsEnabled = true;
        }
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
            "This disables IPv6 connectivity on the adapter.\n" +
            "Only proceed if setting the IPv6 DNS did not solve the problem.",
            "Confirm Disable IPv6", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
        {
            ChkDisableIPv6.IsChecked = false;
            return;
        }

        Log($"─── Disabling IPv6 on {adapter.Name} ───");
        var (_, logText) = await DnsService.DisableIPv6Async(adapter.Name);
        Log(logText);
    }

    private async void ChkDisableIPv6_Unchecked(object sender, RoutedEventArgs e)
    {
        var adapter = AdapterListBox.SelectedItem as NetworkAdapterInfo;
        if (adapter is null) return;

        // When the user unchecks the box, re-enable IPv6
        Log($"─── Re-enabling IPv6 on {adapter.Name} ───");
        var (_, logText) = await DnsService.EnableIPv6Async(adapter.Name);
        Log(logText);
    }

    // ── Log helpers ──────────────────────────────────────────────────────────

    /// <summary>Appends a timestamped line to the log panel and scrolls to the bottom.</summary>
    private void Log(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        var timestamp = DateTime.Now.ToString("HH:mm:ss");

        Dispatcher.Invoke(() =>
        {
            foreach (var line in message.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                LogBox.AppendText($"[{timestamp}] {line.TrimEnd()}{Environment.NewLine}");
            }
            LogBox.ScrollToEnd();
        });
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e)
        => LogBox.Clear();

    // ── Discord ──────────────────────────────────────────────────────────────

    private void CheckDiscordStatus()
    {
        var installed = DiscordService.IsDiscordInstalled();
        var version   = DiscordService.GetDiscordVersion();

        if (installed)
        {
            // Header badge
            DiscordBadge.Background  = new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x3A));
            DiscordBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(0x58, 0x65, 0xF2));
            DiscordBadgeText.Text       = "● Discord: Installed";
            DiscordBadgeText.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x99, 0xFF));
            DiscordActionBtn.Visibility = Visibility.Collapsed;

            // Panel
            DiscordStatusText.Text       = "Discord is installed on this computer.";
            DiscordStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x99, 0xFF));
            if (!string.IsNullOrEmpty(version))
            {
                DiscordVersionText.Text       = $"Version {version}";
                DiscordVersionText.Visibility = Visibility.Visible;
            }
            LaunchDiscordBtn.Visibility   = Visibility.Visible;
            DownloadDiscordBtn.Visibility = Visibility.Collapsed;

            Log($"Discord detected. Version: {version ?? "unknown"}");
        }
        else
        {
            // Header badge — warn
            DiscordBadge.Background  = new SolidColorBrush(Color.FromRgb(0x2A, 0x15, 0x0A));
            DiscordBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(0xE8, 0x50, 0x50));
            DiscordBadgeText.Text       = "● Discord: Not found";
            DiscordBadgeText.Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0x80, 0x70));
            DiscordActionBtn.Content    = "Download";
            DiscordActionBtn.Visibility = Visibility.Visible;

            // Panel
            DiscordStatusText.Text       = "Discord is not installed on this computer.";
            DiscordStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0x80, 0x70));
            DownloadDiscordBtn.Visibility = Visibility.Visible;
            LaunchDiscordBtn.Visibility   = Visibility.Collapsed;

            Log("[INFO] Discord not detected. Use the Download button to install it.");
        }
    }

    /// <summary>Opens the Discord download page — used by both the header badge and the panel button.</summary>
    private void DiscordAction_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            DiscordService.OpenDownloadPage();
            Log($"Opened Discord download page: {DiscordService.DownloadUrl}");
        }
        catch (Exception ex)
        {
            Log($"[ERROR] Could not open browser: {ex.Message}");
        }
    }

    private void LaunchDiscord_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!DiscordService.LaunchDiscord())
                Log("[WARN] Discord executable not found. Try reinstalling Discord.");
            else
                Log("Discord launched.");
        }
        catch (Exception ex)
        {
            Log($"[ERROR] Could not launch Discord: {ex.Message}");
        }
    }

    // ── Utility ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the list of target adapters based on the current selection and
    /// the "Apply to all" checkbox.  Shows a warning and returns null if no
    /// adapter is selected and "Apply to all" is off.
    /// </summary>
    private List<NetworkAdapterInfo>? GetTargetAdapters()
    {
        if (ChkApplyAll.IsChecked == true)
            return new List<NetworkAdapterInfo>(_adapters);

        if (AdapterListBox.SelectedItem is NetworkAdapterInfo selected)
            return new List<NetworkAdapterInfo> { selected };

        MessageBox.Show(
            "Please select an adapter from the left panel,\n" +
            "or check \"Apply to all active adapters\".",
            "No Adapter Selected", MessageBoxButton.OK, MessageBoxImage.Warning);

        return null;
    }
}
