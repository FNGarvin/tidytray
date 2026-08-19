using Microsoft.Win32;

namespace TidyTray.Core;

/// <summary>
/// Per-user auto-start via the standard Run key -- no admin rights needed,
/// no installer, no scheduled task. Deliberately exposed as a manual
/// tray-menu toggle rather than something TidyTray enables itself on first
/// run: registering standing "run at every login" configuration is a
/// decision the user should make explicitly.
/// </summary>
internal static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TidyTray";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            var existing = key?.GetValue(ValueName) as string;
            return existing is not null && existing.Equals(QuotedExePath, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);

        if (enabled)
            key.SetValue(ValueName, QuotedExePath, RegistryValueKind.String);
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    private static string QuotedExePath => $"\"{Environment.ProcessPath}\"";
}
