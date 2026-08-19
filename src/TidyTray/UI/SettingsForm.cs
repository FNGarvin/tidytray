using TidyTray.Core;

namespace TidyTray.UI;

/// <summary>
/// The "very simple UI" -- one flat, checkable list of every app TidyTray
/// has ever seen in the tray, keyed by resolved friendly name (not by raw
/// exe path / registry hash, so an app update doesn't spawn a duplicate
/// row). Checking/unchecking and hitting Apply writes straight through to
/// the preference store and immediately re-runs reconciliation so the
/// change is visible in the real tray right away.
///
/// Uses CheckedListBox rather than a single-column ListView: a ListView
/// column has a fixed pixel width you have to manually keep in sync with
/// the control's size, where CheckedListBox has no column concept at all
/// -- it just fills whatever width it's given.
/// </summary>
internal sealed class SettingsForm : Form
{
    private readonly PreferenceStore _preferences;
    private readonly ReconciliationService _reconciliation;
    private readonly CheckedListBox _checklist;

    public SettingsForm(PreferenceStore preferences, ReconciliationService reconciliation)
    {
        _preferences = preferences;
        _reconciliation = reconciliation;

        Text = "TidyTray -- uncheck to hide";
        StartPosition = FormStartPosition.CenterScreen;
        Width = 640;
        Height = 620;
        MinimumSize = new Size(420, 360);
        Icon = TrayIconFactory.CreateAppIcon();

        var instructionLabel = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 6, 0, 6),
            Text = "Check to show in tray, uncheck to hide:",
        };

        _checklist = new CheckedListBox
        {
            Dock = DockStyle.Fill,
            CheckOnClick = true,
            IntegralHeight = false,
        };

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8, 6, 8, 6),
        };
        var closeButton = new Button { Text = "Close", AutoSize = true };
        var applyButton = new Button { Text = "Apply", AutoSize = true };
        var refreshButton = new Button { Text = "Refresh", AutoSize = true };
        closeButton.Click += (_, _) => Close();
        applyButton.Click += (_, _) => ApplyChanges();
        refreshButton.Click += (_, _) => PopulateList();
        buttonPanel.Controls.Add(closeButton);
        buttonPanel.Controls.Add(applyButton);
        buttonPanel.Controls.Add(refreshButton);

        // Three rows via TableLayoutPanel, all non-list rows AutoSize
        // rather than a hardcoded pixel height -- that's what actually fits
        // reliably under different DPI/font scaling, not the row-stacking
        // order (docking order was only ever half the earlier bug).
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(instructionLabel, 0, 0);
        layout.Controls.Add(_checklist, 0, 1);
        layout.Controls.Add(buttonPanel, 0, 2);

        Controls.Add(layout);

        PopulateList();
    }

    private void PopulateList()
    {
        _checklist.Items.Clear();

        foreach (var (key, pref) in _preferences.All.OrderBy(kv => kv.Value.DisplayName, StringComparer.OrdinalIgnoreCase))
            _checklist.Items.Add(new AppListItem(key, pref.DisplayName), pref.Visible);
    }

    private void ApplyChanges()
    {
        for (var i = 0; i < _checklist.Items.Count; i++)
        {
            var listItem = (AppListItem)_checklist.Items[i]!;
            var isChecked = _checklist.GetItemChecked(i);

            if (_preferences.TryGet(listItem.Key, out var pref) && pref.Visible != isChecked)
                _preferences.Set(listItem.Key, pref.DisplayName, isChecked, pref.LastKnownPath);
        }

        _reconciliation.Sweep();
    }

    private sealed record AppListItem(string Key, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }
}
