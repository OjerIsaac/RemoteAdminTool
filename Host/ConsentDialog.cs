using System.Windows.Forms;

namespace RemoteAdminTool.Host;

/// <summary>
/// Shows a consent dialog when an admin requests a remote session.
/// The user on the host machine can Accept or Deny the connection.
/// Can be disabled for unattended/headless mode via configuration.
/// </summary>
public static class ConsentDialog
{
    /// <summary>
    /// Shows a consent prompt. Returns true if the user accepts.
    /// If unattended mode is enabled, auto-accepts.
    /// </summary>
    public static bool RequestConsent(bool unattendedMode)
    {
        if (unattendedMode)
        {
            return true; // Auto-accept in unattended mode
        }

        // Must run on the UI thread for WinForms dialogs
        var result = DialogResult.No;

        if (Application.OpenForms.Count > 0)
        {
            // Invoke on the existing UI thread
            var form = Application.OpenForms[0];
            if (form?.InvokeRequired == true)
            {
                form.Invoke(() =>
                {
                    result = ShowDialog();
                });
            }
            else
            {
                result = ShowDialog();
            }
        }
        else
        {
            // No forms open - show directly (we're likely on the main thread)
            result = ShowDialog();
        }

        return result == DialogResult.Yes;
    }

    private static DialogResult ShowDialog()
    {
        return MessageBox.Show(
            "An administrator is requesting remote access to this computer.\n\n" +
            "Do you want to allow this connection?",
            "Remote Admin - Connection Request",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2, // Default to "No" for safety
            MessageBoxOptions.DefaultDesktopOnly | MessageBoxOptions.ServiceNotification);
    }
}
