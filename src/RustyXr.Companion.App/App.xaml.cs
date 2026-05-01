using System.Runtime.InteropServices;
using System.Windows;
using RustyXr.Companion.Core;

namespace RustyXr.Companion.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        WindowsTaskbarIdentity.Apply(AppBuildIdentity.Detect());
        base.OnStartup(e);
    }
}

internal static class WindowsTaskbarIdentity
{
    public static void Apply(AppBuildIdentity identity)
    {
        try
        {
            _ = SetCurrentProcessExplicitAppUserModelID(identity.AppUserModelId);
        }
        catch
        {
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appID);
}
