using System.Diagnostics;
using System.Text.RegularExpressions;

namespace TidyTray.Core;

/// <summary>
/// Turns a raw NotifyIconSettings entry into a stable, human-readable
/// grouping key -- the thing preferences actually get keyed on. This is the
/// piece that makes preferences survive an app update: Explorer's own
/// identity (subkey hash of ExecutablePath+UID) breaks the moment an
/// auto-updater installs into a new versioned folder
/// (".../app-1.25927.0/claude.exe" -> ".../app-1.26001.0/claude.exe"), but a
/// resolved product name does not.
/// </summary>
internal static partial class FriendlyNameResolver
{
    // Matches a trailing version-stamped path segment such as
    // "app-1.25927.0" (Electron/Squirrel-style updaters: Claude, Discord,
    // Slack, ...) so two versions of the same app still normalize to the
    // same identity even if version info / tooltip are both unavailable.
    [GeneratedRegex(@"[\\/]app-[\d]+(\.[\d]+)*[\\/]", RegexOptions.IgnoreCase)]
    private static partial Regex VersionedSegmentPattern();

    public static FriendlyName Resolve(NotifyIconEntry entry)
    {
        // 1. Version resource on the exe itself -- most stable source,
        //    survives updates as long as the publisher keeps the field
        //    consistent (nearly universal in practice). FileDescription is
        //    checked before ProductName deliberately: FileDescription is
        //    the per-binary description ("Task Manager", "Windows
        //    Explorer"), while ProductName is the umbrella suite name and
        //    is IDENTICAL across dozens of unrelated Windows-shipped
        //    binaries ("Microsoft(R) Windows(R) Operating System" for
        //    explorer.exe, Taskmgr.exe, SecurityHealthSystray.exe, ...).
        //    Preferring the more specific field first means most of that
        //    collision never needs disambiguating in ResolveAll at all.
        var (productName, description) = TryReadVersionInfo(entry.ResolvedExecutablePath);
        if (!string.IsNullOrWhiteSpace(description))
            return new FriendlyName(CleanTrademarkSymbols(description.Trim()), FriendlyNameSource.FileDescription);

        if (!string.IsNullOrWhiteSpace(productName))
            return new FriendlyName(CleanTrademarkSymbols(productName.Trim()), FriendlyNameSource.ProductName);

        // 2. Tooltip -- often good ("Steam", "VLC media player") but can be
        //    dynamic/stateful ("Steam - synchronizing", "7 unread messages"),
        //    so only take the first line and only as a fallback.
        if (!string.IsNullOrWhiteSpace(entry.InitialTooltip))
        {
            var firstLine = entry.InitialTooltip.Split('\n', '\r')[0].Trim();
            if (firstLine.Length > 0)
                return new FriendlyName(firstLine, FriendlyNameSource.Tooltip);
        }

        // 3. Publisher (present on some IconGuid-identified system icons).
        if (!string.IsNullOrWhiteSpace(entry.Publisher))
            return new FriendlyName(entry.Publisher.Trim(), FriendlyNameSource.Publisher);

        // 4. Bare exe filename, with any versioned path segment collapsed
        //    out so at least repeated runs of an unresolvable app converge.
        var normalizedPath = VersionedSegmentPattern().Replace(entry.ResolvedExecutablePath, @"\app\");
        var fileName = Path.GetFileNameWithoutExtension(normalizedPath);
        if (!string.IsNullOrWhiteSpace(fileName))
            return new FriendlyName(fileName, FriendlyNameSource.FileName);

        // 5. Last resort -- an icon with no path-derived info at all
        //    (shouldn't happen in practice; ExecutablePath is required to
        //    even reach this point).
        return new FriendlyName($"Unknown icon ({entry.IconIdentity})", FriendlyNameSource.Fallback);
    }

    /// <summary>Strips (R)/(TM) trademark clutter ("Microsoft(R) Windows(R)" -> "Microsoft Windows") that version resources routinely carry -- pure visual noise in a settings list.</summary>
    private static string CleanTrademarkSymbols(string name) =>
        name.Replace("®", "").Replace("™", "").Replace("  ", " ").Trim();

    private static (string? productName, string? description) TryReadVersionInfo(string exePath)
    {
        try
        {
            if (!File.Exists(exePath))
                return (null, null);

            var info = FileVersionInfo.GetVersionInfo(exePath);
            return (info.ProductName, info.FileDescription);
        }
        catch
        {
            // Locked file, malformed resource, missing permissions, etc. --
            // none of that should ever crash the reconciliation loop.
            return (null, null);
        }
    }

    /// <summary>Normalizes a friendly name into the key preferences are actually stored/grouped under.</summary>
    public static string ToPreferenceKey(string friendlyName) => friendlyName.Trim().ToLowerInvariant();

    /// <summary>
    /// Resolves a whole sweep's worth of entries at once, so that name
    /// collisions can be detected across the set rather than guessed at per
    /// entry. Several distinct Windows-owned tray icons (explorer.exe,
    /// rundll32.exe, wlrmdr.exe, SecurityHealthSystray.exe, Taskmgr.exe, ...)
    /// all report the identical generic ProductName ("Microsoft(R)
    /// Windows(R) Operating System"), and would otherwise silently share one
    /// preference entry -- toggling one would toggle all of them. Any name
    /// shared by more than one distinct icon identity in this sweep gets a
    /// short, stable disambiguating suffix; unique names are left untouched.
    /// </summary>
    public static IReadOnlyList<ResolvedIdentity> ResolveAll(IReadOnlyList<NotifyIconEntry> entries)
    {
        var resolved = entries.Select(e => (Entry: e, Name: Resolve(e))).ToList();

        var identityCountByKey = resolved
            .GroupBy(r => ToPreferenceKey(r.Name.DisplayName))
            .ToDictionary(g => g.Key, g => g.Select(r => r.Entry.IconIdentity).Distinct().Count());

        var results = new List<ResolvedIdentity>(resolved.Count);
        foreach (var (entry, name) in resolved)
        {
            var baseKey = ToPreferenceKey(name.DisplayName);
            if (identityCountByKey[baseKey] <= 1)
            {
                results.Add(new ResolvedIdentity(entry, name.DisplayName, baseKey));
                continue;
            }

            var disambiguatedName = $"{name.DisplayName} - {Disambiguator(entry)}";
            results.Add(new ResolvedIdentity(entry, disambiguatedName, ToPreferenceKey(disambiguatedName)));
        }

        return results;
    }

    /// <summary>Prefers the icon's own tooltip when disambiguating (e.g. "eggclicker 1k.ahk") -- far more human-readable than a hash. Falls back to a short stable hash only when no tooltip exists.</summary>
    private static string Disambiguator(NotifyIconEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.InitialTooltip))
        {
            var firstLine = entry.InitialTooltip.Split('\n', '\r')[0].Trim();
            if (firstLine.Length > 0)
                return firstLine;
        }

        return ShortHash(entry.IconIdentity);
    }

    private static string ShortHash(string input)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(hash)[..6];
    }
}

internal readonly record struct ResolvedIdentity(NotifyIconEntry Entry, string DisplayName, string PreferenceKey);

internal enum FriendlyNameSource
{
    ProductName,
    FileDescription,
    Tooltip,
    Publisher,
    FileName,
    Fallback,
}

internal readonly record struct FriendlyName(string DisplayName, FriendlyNameSource Source);
