using System.Windows;

namespace RemoteAdminTool.Viewer;

public partial class TenantCredentialsDialog : Window
{
    private string _tenantId = string.Empty;
    private string _licenseKey = string.Empty;
    private string _adminToken = string.Empty;
    private string _buildCommand = string.Empty;

    public TenantCredentialsDialog()
    {
        InitializeComponent();
    }

    /// <summary>Show credentials after creating a new tenant.</summary>
    public static void ShowNewTenant(Window owner, string tenantId, string licenseKey, string adminToken)
    {
        var dlg = new TenantCredentialsDialog { Owner = owner };
        dlg.TitleRun.Text = "Tenant Created Successfully";
        dlg.SubtitleText.Text =
            "Save these credentials — the License Key is needed to build the Host EXE for this sub-admin.";
        dlg.Load(tenantId, licenseKey, adminToken);
        dlg.ShowDialog();
    }

    /// <summary>Show credentials for an existing tenant (from the tenant list).</summary>
    public static void ShowExisting(Window owner, string displayName, string tenantId,
        string licenseKey, string adminToken)
    {
        var dlg = new TenantCredentialsDialog { Owner = owner };
        dlg.TitleRun.Text = $"Credentials — {displayName}";
        dlg.SubtitleText.Text =
            "These are the stored credentials for this sub-admin tenant.";
        dlg.Load(tenantId, licenseKey, adminToken);
        dlg.ShowDialog();
    }

    private void Load(string tenantId, string licenseKey, string adminToken)
    {
        _tenantId = tenantId;
        _licenseKey = licenseKey;
        _adminToken = adminToken;
        // The 'command' is actually just the license key — user pastes it into BuildHostForSubAdmin.bat
        _buildCommand = licenseKey;

        TenantIdBox.Text = tenantId;
        LicenseKeyBox.Text = licenseKey;
        AdminTokenBox.Text = adminToken;
        BuildCommandText.Text = _buildCommand;
    }

    private void CopyTenantId_Click(object sender, RoutedEventArgs e)
        => CopyToClipboard(_tenantId, "Tenant ID");

    private void CopyLicenseKey_Click(object sender, RoutedEventArgs e)
        => CopyToClipboard(_licenseKey, "License Key");

    private void CopyAdminToken_Click(object sender, RoutedEventArgs e)
        => CopyToClipboard(_adminToken, "Admin Token");

    private void CopyBuildCommand_Click(object sender, RoutedEventArgs e)
        => CopyToClipboard(_buildCommand, "Build command");

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
