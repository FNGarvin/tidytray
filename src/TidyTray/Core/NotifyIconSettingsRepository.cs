using Microsoft.Win32;

namespace TidyTray.Core;

/// <summary>
/// Thin wrapper around HKCU\Control Panel\NotifyIconSettings -- the
/// undocumented store Explorer itself uses to decide whether a tray icon
/// shows in the visible tray or the overflow flyout. Confirmed by hand
/// (see project notes) that writing IsPromoted here takes effect live,
/// no Explorer restart needed, in either direction.
/// </summary>
internal sealed class NotifyIconSettingsRepository
{
    private const string RootPath = @"Control Panel\NotifyIconSettings";

    /// <summary>Full path form needed for RegNotifyChangeKeyValue / SafeRegistryHandle access.</summary>
    public const string FullRootPath = "HKEY_CURRENT_USER\\" + RootPath;

    public IReadOnlyList<NotifyIconEntry> GetAll()
    {
        using var root = Registry.CurrentUser.OpenSubKey(RootPath, writable: false);
        if (root is null)
            return [];

        var results = new List<NotifyIconEntry>();
        foreach (var subKeyName in root.GetSubKeyNames())
        {
            using var sub = root.OpenSubKey(subKeyName, writable: false);
            if (sub is null)
                continue;

            var rawPath = sub.GetValue("ExecutablePath") as string ?? string.Empty;
            if (rawPath.Length == 0)
                continue;

            uint? uid = sub.GetValue("UID") is int i ? unchecked((uint)i) : null;

            Guid? iconGuid = null;
            if (sub.GetValue("IconGuid") is byte[] { Length: 16 } guidBytes)
                iconGuid = new Guid(guidBytes);

            bool? isPromoted = sub.GetValue("IsPromoted") switch
            {
                int v => v != 0,
                _ => null,
            };

            results.Add(new NotifyIconEntry
            {
                RegistrySubKeyName = subKeyName,
                RawExecutablePath = rawPath,
                ResolvedExecutablePath = KnownFolderResolver.Resolve(rawPath),
                Uid = uid,
                IconGuid = iconGuid,
                InitialTooltip = sub.GetValue("InitialTooltip") as string,
                Publisher = sub.GetValue("Publisher") as string,
                IsPromotedRaw = isPromoted,
            });
        }

        return results;
    }

    /// <summary>Sets IsPromoted for a given subkey. Confirmed to apply live.</summary>
    public void SetPromoted(string registrySubKeyName, bool visible)
    {
        using var sub = Registry.CurrentUser.OpenSubKey(
            $@"{RootPath}\{registrySubKeyName}", writable: true)
            ?? throw new InvalidOperationException($"NotifyIconSettings subkey '{registrySubKeyName}' no longer exists.");

        sub.SetValue("IsPromoted", visible ? 1 : 0, RegistryValueKind.DWord);
    }
}
