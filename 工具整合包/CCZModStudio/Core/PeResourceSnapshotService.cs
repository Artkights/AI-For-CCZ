using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace CCZModStudio.Core;

internal static class PeResourceSnapshotService
{
    private const uint LoadLibraryAsDataFile = 0x00000002;
    private const uint LoadLibraryAsImageResource = 0x00000020;

    public static IReadOnlyList<PeResourceSnapshot> Capture(string path)
    {
        var module = LoadLibraryExW(Path.GetFullPath(path), IntPtr.Zero,
            LoadLibraryAsDataFile | LoadLibraryAsImageResource);
        if (module == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to load the PE file for resource verification.");

        try
        {
            var snapshots = new List<PeResourceSnapshot>();
            Exception? failure = null;
            EnumResTypeProc? typeCallback = null;
            EnumResNameProc? nameCallback = null;
            EnumResLangProc? languageCallback = null;

            languageCallback = (handle, typePointer, namePointer, languageId, _) =>
            {
                try
                {
                    var resource = FindResourceExW(handle, typePointer, namePointer, languageId);
                    if (resource == IntPtr.Zero)
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to locate an enumerated PE resource.");
                    var size = SizeofResource(handle, resource);
                    var bytes = new byte[checked((int)size)];
                    if (size > 0)
                    {
                        var loaded = LoadResource(handle, resource);
                        var pointer = loaded == IntPtr.Zero ? IntPtr.Zero : LockResource(loaded);
                        if (pointer == IntPtr.Zero)
                            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to read an enumerated PE resource.");
                        Marshal.Copy(pointer, bytes, 0, bytes.Length);
                    }

                    snapshots.Add(new PeResourceSnapshot(
                        ReadIdentifier(typePointer),
                        ReadIdentifier(namePointer),
                        languageId,
                        bytes.Length,
                        Convert.ToHexString(SHA256.HashData(bytes))));
                    return true;
                }
                catch (Exception ex)
                {
                    failure = ex;
                    return false;
                }
            };

            nameCallback = (handle, typePointer, namePointer, _) =>
            {
                try
                {
                    return EnumResourceLanguagesW(handle, typePointer, namePointer, languageCallback!, IntPtr.Zero);
                }
                catch (Exception ex)
                {
                    failure = ex;
                    return false;
                }
            };

            typeCallback = (handle, typePointer, _) =>
            {
                try
                {
                    return EnumResourceNamesW(handle, typePointer, nameCallback!, IntPtr.Zero);
                }
                catch (Exception ex)
                {
                    failure = ex;
                    return false;
                }
            };

            var completed = EnumResourceTypesW(module, typeCallback, IntPtr.Zero);
            GC.KeepAlive(typeCallback);
            GC.KeepAlive(nameCallback);
            GC.KeepAlive(languageCallback);
            if (failure != null)
                throw new InvalidOperationException("PE resource enumeration failed.", failure);
            if (!completed)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "PE resource enumeration did not complete.");

            return snapshots
                .OrderBy(resource => resource.Type.SortKey, StringComparer.Ordinal)
                .ThenBy(resource => resource.Name.SortKey, StringComparer.Ordinal)
                .ThenBy(resource => resource.LanguageId)
                .ToArray();
        }
        finally
        {
            FreeLibrary(module);
        }
    }

    private static PeResourceIdentifier ReadIdentifier(IntPtr value)
    {
        var raw = unchecked((ulong)value.ToInt64());
        if (raw <= ushort.MaxValue)
            return new PeResourceIdentifier(true, checked((int)raw), string.Empty);
        return new PeResourceIdentifier(false, 0, Marshal.PtrToStringUni(value) ?? string.Empty);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate bool EnumResTypeProc(IntPtr module, IntPtr type, IntPtr parameter);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate bool EnumResNameProc(IntPtr module, IntPtr type, IntPtr name, IntPtr parameter);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate bool EnumResLangProc(IntPtr module, IntPtr type, IntPtr name, ushort languageId, IntPtr parameter);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryExW(string fileName, IntPtr file, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr module);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool EnumResourceTypesW(IntPtr module, EnumResTypeProc callback, IntPtr parameter);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool EnumResourceNamesW(IntPtr module, IntPtr type, EnumResNameProc callback, IntPtr parameter);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool EnumResourceLanguagesW(IntPtr module, IntPtr type, IntPtr name, EnumResLangProc callback, IntPtr parameter);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindResourceExW(IntPtr module, IntPtr type, IntPtr name, ushort languageId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SizeofResource(IntPtr module, IntPtr resource);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LoadResource(IntPtr module, IntPtr resource);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LockResource(IntPtr resourceData);
}

internal sealed record PeResourceIdentifier(bool IsInteger, int IntegerId, string Text)
{
    public string SortKey => IsInteger ? $"#{IntegerId:D10}" : Text;
}

internal sealed record PeResourceSnapshot(
    PeResourceIdentifier Type,
    PeResourceIdentifier Name,
    ushort LanguageId,
    int Size,
    string Sha256);
