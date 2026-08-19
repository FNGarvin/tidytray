using System.Text.Json;
using System.Text.Json.Serialization;

namespace TidyTray.Core;

internal sealed class AppPreference
{
    public required string DisplayName { get; set; }
    public required bool Visible { get; set; }
    public DateTimeOffset FirstSeenUtc { get; set; }
    public DateTimeOffset LastSeenUtc { get; set; }
    public string? LastKnownPath { get; set; }
}

/// <summary>
/// Persisted preferences, keyed by FriendlyNameResolver's normalized key --
/// deliberately NOT keyed by the registry subkey hash or raw exe path,
/// since both of those churn across app updates. This file is the thing
/// that actually survives an update; the OS registry state is treated as
/// disposable and gets re-derived from this every reconciliation pass.
/// </summary>
internal sealed class PreferenceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _filePath;
    private readonly Dictionary<string, AppPreference> _preferences;
    private readonly Lock _lock = new();

    public PreferenceStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TidyTray", "preferences.json");

        _preferences = Load(_filePath);
    }

    public bool TryGet(string preferenceKey, out AppPreference preference)
    {
        lock (_lock)
            return _preferences.TryGetValue(preferenceKey, out preference!);
    }

    public IReadOnlyDictionary<string, AppPreference> All
    {
        get { lock (_lock) return new Dictionary<string, AppPreference>(_preferences); }
    }

    /// <summary>Records/updates a preference and persists immediately -- this app runs unattended, so we never risk losing a change to an unclean shutdown.</summary>
    public void Set(string preferenceKey, string displayName, bool visible, string? lastKnownPath)
    {
        lock (_lock)
        {
            var now = DateTimeOffset.UtcNow;
            if (_preferences.TryGetValue(preferenceKey, out var existing))
            {
                existing.DisplayName = displayName;
                existing.Visible = visible;
                existing.LastSeenUtc = now;
                existing.LastKnownPath = lastKnownPath;
            }
            else
            {
                _preferences[preferenceKey] = new AppPreference
                {
                    DisplayName = displayName,
                    Visible = visible,
                    FirstSeenUtc = now,
                    LastSeenUtc = now,
                    LastKnownPath = lastKnownPath,
                };
            }

            Save();
        }
    }

    /// <summary>Touches LastSeenUtc/LastKnownPath without changing the user's Visible choice.</summary>
    public void TouchSeen(string preferenceKey, string? lastKnownPath)
    {
        lock (_lock)
        {
            if (!_preferences.TryGetValue(preferenceKey, out var existing))
                return;

            existing.LastSeenUtc = DateTimeOffset.UtcNow;
            existing.LastKnownPath = lastKnownPath;
            Save();
        }
    }

    private static Dictionary<string, AppPreference> Load(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                return new Dictionary<string, AppPreference>();

            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<Dictionary<string, AppPreference>>(json, JsonOptions)
                   ?? new Dictionary<string, AppPreference>();
        }
        catch
        {
            // Corrupt file shouldn't take the whole app down -- start clean
            // rather than crash-looping on launch.
            return new Dictionary<string, AppPreference>();
        }
    }

    private void Save()
    {
        var dir = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(_preferences, JsonOptions);
        var tmpPath = _filePath + ".tmp";
        File.WriteAllText(tmpPath, json);
        File.Move(tmpPath, _filePath, overwrite: true);
    }
}
