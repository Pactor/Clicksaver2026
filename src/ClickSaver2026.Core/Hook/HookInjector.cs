using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ClickSaver2026.Core.Hook;

/// <summary>Loads the hook DLL into a game client, and unloads it again.</summary>
public static class HookInjector
{
    public const string HookFileName = "ClickSaver2026.Hook.dll";
    public const string ShutdownExport = "ClickSaverShutdown";

    private const uint Access =
        NativeMethods.ProcessCreateThread | NativeMethods.ProcessQueryInformation |
        NativeMethods.ProcessVmOperation | NativeMethods.ProcessVmRead | NativeMethods.ProcessVmWrite;

    private static readonly TimeSpan RemoteThreadTimeout = TimeSpan.FromSeconds(15);

    /// <summary>The hook module in the client, matched by file name, or null when it is not loaded.</summary>
    public static ProcessModule? FindLoadedHook(int processId)
    {
        using var process = Process.GetProcessById(processId);
        foreach (ProcessModule module in process.Modules)
        {
            if (string.Equals(module.ModuleName, HookFileName, StringComparison.OrdinalIgnoreCase))
            {
                return module;
            }

            module.Dispose();
        }

        return null;
    }

    /// <summary>Loads <paramref name="hookPath"/> into the client by starting LoadLibraryW in it.</summary>
    public static unsafe void Inject(int processId, string hookPath)
    {
        // kernel32 is mapped at the same address in every process of the same bitness, so a
        // 32-bit app can hand its own LoadLibraryW address to the 32-bit client.
        if (Environment.Is64BitProcess)
        {
            throw new PlatformNotSupportedException("ClickSaver2026 must run as a 32-bit process to attach to the 32-bit game client.");
        }

        hookPath = Path.GetFullPath(hookPath);
        if (!File.Exists(hookPath))
        {
            throw new FileNotFoundException("The hook DLL is missing next to ClickSaver2026.exe.", hookPath);
        }

        using SafeProcessHandle process = Open(processId);
        if (!NativeMethods.IsWow64Process(process, out bool isWow64))
        {
            throw Win32("check the game client's bitness");
        }

        if (Environment.Is64BitOperatingSystem && !isWow64)
        {
            throw new NotSupportedException($"Process {processId} is 64-bit; the hook only works in the 32-bit client.");
        }

        byte[] path = Encoding.Unicode.GetBytes(hookPath + '\0');
        nint remotePath = NativeMethods.VirtualAllocEx(
            process, 0, (nuint)path.Length, NativeMethods.MemCommit | NativeMethods.MemReserve, NativeMethods.PageReadWrite);
        if (remotePath == 0)
        {
            throw Win32("allocate memory in the game client");
        }

        bool canFree = true;
        try
        {
            fixed (byte* bytes = path)
            {
                if (!NativeMethods.WriteProcessMemory(process, remotePath, bytes, (nuint)path.Length, out _))
                {
                    throw Win32("write to the game client");
                }
            }

            nint loadLibrary = NativeMethods.GetProcAddress(NativeMethods.GetModuleHandleW("kernel32.dll"), "LoadLibraryW");
            uint module;
            try
            {
                module = RunRemoteThread(process, loadLibrary, remotePath);
            }
            catch (TimeoutException)
            {
                // LoadLibraryW may still be reading the path; leave the memory alone.
                canFree = false;
                throw;
            }

            if (module == 0)
            {
                throw new InvalidOperationException("The game client could not load the hook DLL.");
            }
        }
        finally
        {
            if (canFree)
            {
                NativeMethods.VirtualFreeEx(process, remotePath, 0, NativeMethods.MemRelease);
            }
        }
    }

    /// <summary>
    /// Unloads the hook by starting its ClickSaverShutdown export in the client, which removes the
    /// detour before the DLL goes away. Does nothing when the hook is not loaded.
    /// </summary>
    public static void Eject(int processId, string hookPath)
    {
        using ProcessModule? module = FindLoadedHook(processId);
        if (module is null)
        {
            return;
        }

        string file = File.Exists(module.FileName) ? module.FileName : hookPath;
        uint shutdownRva = PeExports.GetExportRva(file, ShutdownExport);

        using SafeProcessHandle process = Open(processId);
        uint exitCode = RunRemoteThread(process, module.BaseAddress + (nint)shutdownRva, 0);
        if (exitCode != 0)
        {
            throw new InvalidOperationException("The hook removed its detour but could not stop its pipe thread, so it stays loaded until the client exits.");
        }
    }

    private static SafeProcessHandle Open(int processId)
    {
        SafeProcessHandle process = NativeMethods.OpenProcess(Access, false, (uint)processId);
        if (process.IsInvalid)
        {
            int error = Marshal.GetLastPInvokeError();
            process.Dispose();
            throw new Win32Exception(error, $"Could not open game client process {processId}: {new Win32Exception(error).Message}. If the client runs as administrator, run ClickSaver2026 as administrator too.");
        }

        return process;
    }

    private static uint RunRemoteThread(SafeProcessHandle process, nint start, nint parameter)
    {
        using SafeWaitHandle thread = NativeMethods.CreateRemoteThread(process, 0, 0, start, parameter, 0, out _);
        if (thread.IsInvalid)
        {
            throw Win32("start a thread in the game client");
        }

        uint wait = NativeMethods.WaitForSingleObject(thread, (uint)RemoteThreadTimeout.TotalMilliseconds);
        if (wait == NativeMethods.WaitTimeout)
        {
            throw new TimeoutException("The game client did not finish in time.");
        }

        if (wait != NativeMethods.WaitObject0)
        {
            throw Win32("wait for the game client");
        }

        if (!NativeMethods.GetExitCodeThread(thread, out uint exitCode))
        {
            throw Win32("read the result from the game client");
        }

        return exitCode;
    }

    private static Win32Exception Win32(string what)
    {
        int error = Marshal.GetLastPInvokeError();
        return new Win32Exception(error, $"Could not {what}: {new Win32Exception(error).Message}");
    }
}
