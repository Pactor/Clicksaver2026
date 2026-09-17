using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ClickSaver2026.Core.Hook;

internal static partial class NativeMethods
{
    internal const uint ProcessCreateThread = 0x0002;
    internal const uint ProcessVmOperation = 0x0008;
    internal const uint ProcessVmRead = 0x0010;
    internal const uint ProcessVmWrite = 0x0020;
    internal const uint ProcessQueryInformation = 0x0400;

    internal const uint MemCommit = 0x1000;
    internal const uint MemReserve = 0x2000;
    internal const uint MemRelease = 0x8000;
    internal const uint PageReadWrite = 0x04;

    internal const uint WaitObject0 = 0;
    internal const uint WaitTimeout = 0x102;

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint FindWindowExW(nint parent, nint childAfter, string? className, string? windowName);

    [LibraryImport("user32.dll")]
    internal static partial uint GetWindowThreadProcessId(nint window, out uint processId);

    [LibraryImport("user32.dll")]
    internal static partial int GetWindowTextLengthW(nint window);

    [LibraryImport("user32.dll")]
    internal static unsafe partial int GetWindowTextW(nint window, char* text, int maxCount);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWow64Process(SafeProcessHandle process, [MarshalAs(UnmanagedType.Bool)] out bool isWow64);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial nint VirtualAllocEx(SafeProcessHandle process, nint address, nuint size, uint allocationType, uint protect);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool VirtualFreeEx(SafeProcessHandle process, nint address, nuint size, uint freeType);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static unsafe partial bool WriteProcessMemory(SafeProcessHandle process, nint address, byte* buffer, nuint size, out nuint written);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial SafeWaitHandle CreateRemoteThread(
        SafeProcessHandle process, nint threadAttributes, nuint stackSize, nint startAddress, nint parameter, uint creationFlags, out uint threadId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial uint WaitForSingleObject(SafeWaitHandle handle, uint milliseconds);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetExitCodeThread(SafeWaitHandle thread, out uint exitCode);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    internal static partial nint GetModuleHandleW(string moduleName);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    internal static partial nint GetProcAddress(nint module, string procName);
}
