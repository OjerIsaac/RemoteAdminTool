using System.Windows;

namespace RemoteAdminTool.Viewer;

public partial class CreateTenantDialog : Window
{
    public string DisplayName { get; private set; } = string.Empty;
    public string AdminEmail  { get; private set; } = string.Empty;
    public int    MaxHosts    { get; private set; } = 25;

    public CreateTenantDialog()
    {
        InitializeComponent();
    }

    private void CreateButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(DisplayNameBox.Text))
        {
            MessageBox.Show("Display name is required.", "Validation",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(MaxHostsBox.Text.Trim(), out var maxHosts) || maxHosts < 0)
        {
            MessageBox.Show("Max Allowed Hosts must be 0 (unlimited) or a positive whole number.", "Validation",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DisplayName  = DisplayNameBox.Text.Trim();
        AdminEmail   = AdminEmailBox.Text.Trim();
        MaxHosts     = maxHosts;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
