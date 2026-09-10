using System.Windows;

namespace RemoteAdminTool.Viewer;

public partial class CreateTenantDialog : Window
{
    public string DisplayName { get; private set; } = string.Empty;
    public string AdminEmail { get; private set; } = string.Empty;

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

        DisplayName = DisplayNameBox.Text.Trim();
        AdminEmail = AdminEmailBox.Text.Trim();
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
