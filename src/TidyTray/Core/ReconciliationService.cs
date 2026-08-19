using Microsoft.Win32;
using TidyTray.Native;

namespace TidyTray.Core;

internal enum ReconciliationReason
{
    /// <summary>Never seen before -- registered with the default preference (visible), fighting Windows' own default-to-hidden behavior.</summary>
    NewApp,

    /// <summary>Seen before with a known preference, but the OS had drifted from it (classic case: an app update reset it).</summary>
    DriftCorrected,
}

internal sealed record ReconciliationChange(string DisplayName, bool NowVisible, ReconciliationReason Reason);

/// <summary>
/// The core loop: enumerate every icon Explorer currently knows about,
/// resolve each to a stable friendly identity, and make the live
/// IsPromoted state match the user's stored preference -- creating a
/// default (visible) preference the first time an identity is seen.
///
/// Driven two ways:
///  - Near-instant: a background thread blocked on RegNotifyChangeKeyValue
///    against the NotifyIconSettings key, which wakes up whenever Explorer
///    adds or updates an entry (confirmed live, no Explorer restart needed).
///  - Backstop: a periodic sweep, in case a notification is ever missed.
/// </summary>
internal sealed class ReconciliationService : IDisposable
{
    private readonly NotifyIconSettingsRepository _repository;
    private readonly PreferenceStore _preferences;
    private readonly bool _defaultVisibleForNewApps;
    private readonly System.Threading.Timer _sweepTimer;
    private readonly CancellationTokenSource _cts = new();
    private RegistryKey? _watchedKey;
    private Thread? _watchThread;

    public event Action<IReadOnlyList<ReconciliationChange>>? Reconciled;

    public ReconciliationService(
        NotifyIconSettingsRepository repository,
        PreferenceStore preferences,
        bool defaultVisibleForNewApps = true,
        TimeSpan? sweepInterval = null)
    {
        _repository = repository;
        _preferences = preferences;
        _defaultVisibleForNewApps = defaultVisibleForNewApps;

        var interval = sweepInterval ?? TimeSpan.FromMinutes(5);
        _sweepTimer = new System.Threading.Timer(_ => SafeSweep(), null, interval, interval);
    }

    public void Start()
    {
        SafeSweep(); // catch up on anything that changed while we weren't running

        _watchThread = new Thread(WatchLoop) { IsBackground = true, Name = "TidyTray-RegistryWatch" };
        _watchThread.Start();
    }

    private void WatchLoop()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                _watchedKey = Registry.CurrentUser.OpenSubKey(@"Control Panel\NotifyIconSettings", writable: false);
                if (_watchedKey is null)
                {
                    // Key doesn't exist yet on this machine/profile -- wait via the sweep timer instead of busy-looping.
                    return;
                }

                using var registration = _cts.Token.Register(() => _watchedKey?.Dispose());

                var handle = _watchedKey.Handle.DangerousGetHandle();
                var result = NativeMethods.RegNotifyChangeKeyValue(
                    handle,
                    bWatchSubtree: true,
                    dwNotifyFilter: NativeMethods.RegNotifyFilter.Name | NativeMethods.RegNotifyFilter.LastSet,
                    hEvent: 0,
                    fAsynchronous: false);

                _watchedKey.Dispose();
                _watchedKey = null;

                if (_cts.IsCancellationRequested)
                    return;

                if (result == 0) // ERROR_SUCCESS -- something changed
                    SafeSweep();
            }
        }
        catch (ObjectDisposedException)
        {
            // Expected on shutdown: Dispose() unblocks the pending native call.
        }
    }

    private void SafeSweep()
    {
        try
        {
            var changes = Sweep();
            if (changes.Count > 0)
                Reconciled?.Invoke(changes);
        }
        catch
        {
            // A single bad sweep (e.g. transient registry access failure)
            // must never take down the watch thread or the timer.
        }
    }

    /// <summary>Runs one enumerate-and-correct pass. Public so the settings UI can trigger an immediate re-check after the user changes a preference.</summary>
    public IReadOnlyList<ReconciliationChange> Sweep()
    {
        var changes = new List<ReconciliationChange>();
        var resolvedEntries = FriendlyNameResolver.ResolveAll(_repository.GetAll());

        foreach (var (entry, displayName, key) in resolvedEntries)
        {
            if (_preferences.TryGet(key, out var pref))
            {
                _preferences.TouchSeen(key, entry.ResolvedExecutablePath);

                if (entry.IsVisible != pref.Visible)
                {
                    _repository.SetPromoted(entry.RegistrySubKeyName, pref.Visible);
                    changes.Add(new ReconciliationChange(displayName, pref.Visible, ReconciliationReason.DriftCorrected));
                }
            }
            else
            {
                _preferences.Set(key, displayName, _defaultVisibleForNewApps, entry.ResolvedExecutablePath);
                if (entry.IsVisible != _defaultVisibleForNewApps)
                    _repository.SetPromoted(entry.RegistrySubKeyName, _defaultVisibleForNewApps);

                changes.Add(new ReconciliationChange(displayName, _defaultVisibleForNewApps, ReconciliationReason.NewApp));
            }
        }

        return changes;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _watchedKey?.Dispose();
        _sweepTimer.Dispose();
        _watchThread?.Join(TimeSpan.FromSeconds(2));
        _cts.Dispose();
    }
}
