using System.Runtime.InteropServices;

namespace TidyTray.Native;

/// <summary>
/// Raw Win32 interop. Kept to exactly what we need: resolving KNOWNFOLDERID
/// tokens that Explorer substitutes into NotifyIconSettings ExecutablePath
/// values, and watching a registry key for live changes without polling.
/// </summary>
internal static class NativeMethods
{
    // --- Known folder resolution (shell32) ---
    // Explorer stores ExecutablePath values like "{GUID}\rest\of\path.exe"
    // instead of a literal drive path, where {GUID} is a KNOWNFOLDERID
    // (e.g. FOLDERID_ProgramFilesX64, FOLDERID_System, FOLDERID_Windows).
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern int SHGetKnownFolderPath(
        [MarshalAs(UnmanagedType.LPStruct)] Guid rfid,
        uint dwFlags,
        nint hToken,
        out nint ppszPath);

    // --- Registry change notification (advapi32) ---
    // Blocking wait that returns as soon as something under the watched key
    // changes; we re-arm it in a loop on a background thread. This is what
    // lets us react to a new/updated tray icon within ~instantly instead of
    // relying purely on a periodic sweep.
    [DllImport("advapi32.dll", SetLastError = true, ExactSpelling = true)]
    internal static extern int RegNotifyChangeKeyValue(
        nint hKey,
        bool bWatchSubtree,
        RegNotifyFilter dwNotifyFilter,
        nint hEvent,
        bool fAsynchronous);

    [Flags]
    internal enum RegNotifyFilter
    {
        Name = 0x1,        // subkeys added or removed
        Attributes = 0x2,
        LastSet = 0x4,     // a value under the key changed
        Security = 0x8,
    }
}
