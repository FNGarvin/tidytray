namespace TidyTray.Core;

/// <summary>
/// One subkey under HKCU\Control Panel\NotifyIconSettings — Explorer's
/// record of a single tray icon it has ever seen. Icons are identified by
/// (ExecutablePath, UID) for classic Shell_NotifyIcon-registered icons, or
/// (ExecutablePath, IconGuid) for icons registered with a stable GUID
/// (mostly built-in Windows icons hosted by explorer.exe itself).
/// </summary>
internal sealed record NotifyIconEntry
{
    public required string RegistrySubKeyName { get; init; }
    public required string RawExecutablePath { get; init; }
    public required string ResolvedExecutablePath { get; init; }
    public uint? Uid { get; init; }
    public Guid? IconGuid { get; init; }
    public string? InitialTooltip { get; init; }
    public string? Publisher { get; init; }

    /// <summary>Null means the value isn't present in the registry at all, which Explorer treats the same as 0 (hidden/overflow).</summary>
    public bool? IsPromotedRaw { get; init; }

    public bool IsVisible => IsPromotedRaw == true;

    /// <summary>
    /// Identity used to tell genuinely distinct icons apart when two entries
    /// resolve to the same display name (see FriendlyNameResolver.ResolveAll).
    /// For UID-identified icons this is deliberately keyed on the bare exe
    /// filename rather than the full path: a Squirrel/Electron-style
    /// auto-updater installs each version into its own "app-x.y.z" folder,
    /// so the full path churns on every update even though it's the same
    /// app icon slot (same filename, same UID) -- that must NOT be treated
    /// as a collision, or every update would fork the preference in two.
    /// GUID-identified icons (mostly Windows' own built-in indicators) keep
    /// their full GUID, which is already update-stable by construction.
    /// </summary>
    public string IconIdentity => IconGuid is { } g
        ? $"guid:{g:D}"
        : $"uid:{Path.GetFileName(ResolvedExecutablePath).ToLowerInvariant()}|{Uid ?? 0}";
}
