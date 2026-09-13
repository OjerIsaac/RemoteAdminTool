using System.Diagnostics;
using System.Windows;

namespace RemoteAdminTool.Viewer;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Check for a newer Viewer version — if updated, shut down and let the new EXE launch
        var updated = await ViewerUpdater.CheckAndUpdateAsync(
            ViewerConstants.RelayHttpUrl,
            ViewerConstants.AppVersion);

        if (updated)
        {
            // New process launched by updater — exit this instance cleanly
            Shutdown();
        }
    }
}
