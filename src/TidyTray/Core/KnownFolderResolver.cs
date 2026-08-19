using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using TidyTray.Native;

namespace TidyTray.Core;

/// <summary>
/// Resolves the KNOWNFOLDERID-prefixed paths Explorer writes into
/// NotifyIconSettings, e.g. "{6D809377-6AF0-444B-8957-A3773F02200E}\Steam\steam.exe"
/// -> "C:\Program Files\Steam\steam.exe". Falls back to the raw value
/// unchanged if it isn't in that form (plenty of entries are already a
/// literal absolute path).
/// </summary>
internal static partial class KnownFolderResolver
{
    [GeneratedRegex(@"^\{(?<guid>[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12})\}\\(?<rest>.+)$")]
    private static partial Regex KnownFolderPattern();

    private static readonly Dictionary<Guid, string?> Cache = new();

    public static string Resolve(string rawExecutablePath)
    {
        if (string.IsNullOrEmpty(rawExecutablePath))
            return rawExecutablePath;

        var match = KnownFolderPattern().Match(rawExecutablePath);
        if (!match.Success)
            return rawExecutablePath;

        var guid = Guid.Parse(match.Groups["guid"].Value);
        var rest = match.Groups["rest"].Value;

        var basePath = ResolveKnownFolder(guid);
        return basePath is null ? rawExecutablePath : Path.Combine(basePath, rest);
    }

    private static string? ResolveKnownFolder(Guid folderId)
    {
        if (Cache.TryGetValue(folderId, out var cached))
            return cached;

        string? result = null;
        var hr = NativeMethods.SHGetKnownFolderPath(folderId, 0, 0, out var ptr);
        if (hr == 0 && ptr != 0)
        {
            try
            {
                result = Marshal.PtrToStringUni(ptr);
            }
            finally
            {
                Marshal.FreeCoTaskMem(ptr);
            }
        }

        Cache[folderId] = result;
        return result;
    }
}
