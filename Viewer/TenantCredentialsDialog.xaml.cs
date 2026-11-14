using System.Windows;
using RemoteAdminTool.Shared;

namespace RemoteAdminTool.Viewer;

public partial class TenantCredentialsDialog : Window
{
    private ViewerClient? _client;
    private string _tenantId   = string.Empty;
    private string _licenseKey = string.Empty;
    private string _adminToken = string.Empty;
    private string _hostToken  = string.Empty;

    public TenantCredentialsDialog()
    {
        InitializeComponent();
        Closed += (_, _) =>
        {
            if (_client is not null)
                _client.TenantListReceived -= OnTenantListReceived;
        };
    }

    /// <summary>Show credentials after creating a new tenant.</summary>
    public static void ShowNewTenant(Window owner, ViewerClient? client, string tenantId, string licenseKey, string adminToken, string hostToken, int maxHosts = 25)
    {
        var dlg = new TenantCredentialsDialog { Owner = owner };
        dlg.TitleRun.Text = "Tenant Created Successfully";
        dlg.SubtitleText.Text =
            "Save these credentials. You need the License Key AND Host Token to build Host.exe for this sub-admin.";
        dlg.Load(client, tenantId, licenseKey, adminToken, hostToken, maxHosts);
        dlg.ShowDialog();
    }

    /// <summary>Show credentials for an existing tenant (from the tenant list).</summary>
    public static void ShowExisting(Window owner, ViewerClient? client, string displayName, string tenantId,
        string licenseKey, string adminToken, string hostToken, int maxHosts)
    {
        var dlg = new TenantCredentialsDialog { Owner = owner };
        dlg.TitleRun.Text = $"Credentials - {displayName}";
        dlg.SubtitleText.Text =
            "These are the stored credentials and seat limits for this sub-admin tenant.";
        dlg.Load(client, tenantId, licenseKey, adminToken, hostToken, maxHosts);
        dlg.ShowDialog();
    }

    private void Load(ViewerClient? client, string tenantId, string licenseKey, string adminToken, string hostToken, int maxHosts)
    {
        _client     = client;
        _tenantId   = tenantId;
        _licenseKey = licenseKey;
        _adminToken = adminToken;
        _hostToken  = hostToken;

        if (_client is not null)
            _client.TenantListReceived += OnTenantListReceived;

        TenantIdBox.Text   = tenantId;
        LicenseKeyBox.Text = licenseKey;
        AdminTokenBox.Text = adminToken;
        HostTokenBox.Text  = hostToken;
        MaxHostsBox.Text   = maxHosts.ToString();
    }

    private void OnTenantListReceived(object? sender, TenantInfoMessage[] tenants)
    {
        Dispatcher.Invoke(() =>
        {
            var match = tenants.FirstOrDefault(t => t.TenantId == _tenantId);
            if (!string.IsNullOrEmpty(match.TenantId))
            {
                _adminToken        = match.AdminToken;
                AdminTokenBox.Text = match.AdminToken;
                MaxHostsBox.Text   = match.MaxHosts.ToString();
            }
        });
    }

    private async void RotateAdminToken_Click(object sender, RoutedEventArgs e)
    {
        if (_client is null) return;
        var confirm = MessageBox.Show(
            "Rotate the Admin Token for this sub-admin?\n\n" +
            "• Their old Admin Token will stop working immediately.\n" +
            "• Any currently open Viewer using the old token will be kicked out.\n" +
            "• Deployed Host.exe machines are NOT affected.",
            "Rotate Admin Token",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        await _client.RotateAdminTokenAsync(_tenantId);
        MessageBox.Show("Admin Token rotated! Copy the new Admin Token from the box above.",
            "Token Rotated", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void SaveMaxHosts_Click(object sender, RoutedEventArgs e)
    {
        if (_client is null) return;
        if (!int.TryParse(MaxHostsBox.Text.Trim(), out var maxHosts) || maxHosts < 0)
        {
            MessageBox.Show("Max Allowed Hosts must be 0 (unlimited) or a positive whole number.",
                "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await _client.UpdateTenantMaxHostsAsync(_tenantId, maxHosts);
        MessageBox.Show(
            maxHosts == 0
                ? "Seat limit updated to Unlimited (0)."
                : $"Seat limit updated to {maxHosts} hosts.",
            "Seat Limit Saved", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void CopyTenantId_Click(object sender, RoutedEventArgs e)
        => CopyToClipboard(_tenantId, "Tenant ID");

    private void CopyLicenseKey_Click(object sender, RoutedEventArgs e)
        => CopyToClipboard(_licenseKey, "License Key");

    private void CopyAdminToken_Click(object sender, RoutedEventArgs e)
        => CopyToClipboard(_adminToken, "Admin Token");

    private void CopyHostToken_Click(object sender, RoutedEventArgs e)
        => CopyToClipboard(_hostToken, "Host Token");

    private static void CopyToClipboard(string text, string label)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch
        {
            MessageBox.Show($"Could not copy {label} automatically.\n\n{text}",
                "Copy Manually", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
