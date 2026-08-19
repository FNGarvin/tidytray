using TidyTray.UI;

namespace TidyTray;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var singleInstanceMutex = new Mutex(initiallyOwned: true, "TidyTray-SingleInstance-{6C6E0F0E-6D6E-4B7B-9D2F-6E7D6C7B4A1E}", out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show("TidyTray is already running -- check your tray icons.", "TidyTray",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext());
    }
}
