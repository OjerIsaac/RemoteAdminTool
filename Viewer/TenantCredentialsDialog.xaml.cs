using System.Windows;

namespace RemoteAdminTool.Viewer;

public partial class TenantCredentialsDialog : Window
{
    private string _tenantId    = string.Empty;
    private string _licenseKey  = string.Empty;
    private string _adminToken  = string.Empty;
    private string _hostToken   = string.Empty;

    public TenantCredentialsDialog()
    {
        InitializeComponent();
    }

    /// <summary>Show credentials after creating a new tenant.</summary>
    public static void ShowNewTenant(Window owner, string tenantId, string licenseKey, string adminToken, string hostToken)
    {
        var dlg = new TenantCredentialsDialog { Owner = owner };
        dlg.TitleRun.Text = "Tenant Created Successfully";
        dlg.SubtitleText.Text =
            "Save these credentials. You need the License Key AND Host Token to build Host.exe for this sub-admin.";
        dlg.Load(tenantId, licenseKey, adminToken, hostToken);
        dlg.ShowDialog();
    }

    /// <summary>Show credentials for an existing tenant (from the tenant list).</summary>
    public static void ShowExisting(Window owner, string displayName, string tenantId,
        string licenseKey, string adminToken, string hostToken)
    {
        var dlg = new TenantCredentialsDialog { Owner = owner };
        dlg.TitleRun.Text = $"Credentials — {displayName}";
        dlg.SubtitleText.Text =
            "These are the stored credentials for this sub-admin tenant.";
        dlg.Load(tenantId, licenseKey, adminToken, hostToken);
        dlg.ShowDialog();
    }

    private void Load(string tenantId, string licenseKey, string adminToken, string hostToken)
    {
        _tenantId   = tenantId;
        _licenseKey = licenseKey;
        _adminToken = adminToken;
        _hostToken  = hostToken;

        TenantIdBox.Text   = tenantId;
        LicenseKeyBox.Text = licenseKey;
        AdminTokenBox.Text = adminToken;
        HostTokenBox.Text  = hostToken;
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
