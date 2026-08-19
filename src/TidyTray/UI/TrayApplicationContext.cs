using TidyTray.Core;

namespace TidyTray.UI;

/// <summary>
/// The whole "app" from the OS's perspective: no main window, just a tray
/// icon, its context menu, and the settings window opened on demand.
/// </summary>
internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly PreferenceStore _preferences;
    private readonly ReconciliationService _reconciliation;
    private SettingsForm? _settingsForm;

    public TrayApplicationContext()
    {
        _preferences = new PreferenceStore();
        _reconciliation = new ReconciliationService(new NotifyIconSettingsRepository(), _preferences);
        _reconciliation.Reconciled += OnReconciled;

        var startupItem = new ToolStripMenuItem("Start with Windows")
        {
            CheckOnClick = true,
            Checked = StartupManager.IsEnabled,
        };
        startupItem.CheckedChanged += (_, _) => StartupManager.SetEnabled(startupItem.Checked);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Settings...", null, (_, _) => ShowSettings());
        menu.Items.Add("Sweep now", null, (_, _) => _reconciliation.Sweep());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(startupItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());

        _notifyIcon = new NotifyIcon
        {
            Icon = TrayIconFactory.CreateAppIcon(),
            Text = "TidyTray -- keeping your tray icons where you put them",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _notifyIcon.DoubleClick += (_, _) => ShowSettings();

        _reconciliation.Start();
    }

    private void OnReconciled(IReadOnlyList<ReconciliationChange> changes)
    {
        var corrected = changes.Where(c => c.Reason == ReconciliationReason.DriftCorrected).ToList();
        if (corrected.Count == 0)
            return;

        // Only surface corrections (Windows undoing a preference), not
        // every newly-discovered app -- that would be noisy on first run
        // with dozens of pre-existing tray entries.
        var summary = string.Join(", ", corrected.Take(3).Select(c => c.DisplayName));
        if (corrected.Count > 3)
            summary += $", +{corrected.Count - 3} more";

        _notifyIcon.BalloonTipTitle = "TidyTray";
        _notifyIcon.BalloonTipText = $"Restored your visibility preference for: {summary}";
        _notifyIcon.ShowBalloonTip(4000);
    }

    private void ShowSettings()
    {
        if (_settingsForm is { IsDisposed: false })
        {
            _settingsForm.Activate();
            _settingsForm.WindowState = FormWindowState.Normal;
            return;
        }

        _settingsForm = new SettingsForm(_preferences, _reconciliation);
        _settingsForm.Show();
    }

    private void ExitApp()
    {
        _notifyIcon.Visible = false;
        _reconciliation.Dispose();
        Application.Exit();
    }
}
