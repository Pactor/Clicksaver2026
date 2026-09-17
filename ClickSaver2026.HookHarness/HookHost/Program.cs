using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace HookHost;

/// <summary>
/// Stand-in for the game client. Every 20 ms it calls MessageProtocol!DataBlockToMessage with a
/// 64-byte "HookHost" + call-number block (or a file's bytes) and checks the call still reaches
/// MessageProtocol. Exits 0 when stdin closes, 2 if a call is missed, 4 if the file cannot be read.
/// </summary>
internal static unsafe class Program
{
    private static volatile bool stopping;

    private static int Main(string[] args)
    {
        byte[]? file = null;
        string? path = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));
        if (path is not null)
        {
            file = File.ReadAllBytes(path);
            if (file.Length == 0)
            {
                return 4;
            }
        }

        new Thread(WatchStdin) { IsBackground = true }.Start();

        Console.WriteLine($"ready {Native.GetCurrentProcessId()}");
        Console.Out.Flush();

        byte[] numbered = new byte[64];
        "HookHost"u8.CopyTo(numbered);

        for (uint call = 1; call <= 6000 && !stopping; call++)
        {
            nint returned;
            if (file is not null)
            {
                fixed (byte* p = file)
                {
                    returned = Native.DataBlockToMessage((uint)file.Length, (nint)p);
                }
            }
            else
            {
                BitConverter.TryWriteBytes(numbered.AsSpan(8), call);
                fixed (byte* p = numbered)
                {
                    returned = Native.DataBlockToMessage(64, (nint)p);
                }
            }

            // MessageProtocol returns a running count; every call must still reach it.
            if ((uint)returned != call)
            {
                return 2;
            }

            Thread.Sleep(20);
        }

        return 0;
    }

    private static void WatchStdin()
    {
        using Stream input = Console.OpenStandardInput();
        var buffer = new byte[64];
        while (input.Read(buffer, 0, buffer.Length) > 0)
        {
        }

        stopping = true;
    }
}

internal static partial class Native
{
    [LibraryImport("MessageProtocol", EntryPoint = "?DataBlockToMessage@@YAPAVMessage_t@@IPAX@Z")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial nint DataBlockToMessage(uint size, nint data);

    [LibraryImport("kernel32.dll")]
    public static partial uint GetCurrentProcessId();
}
