using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MessageProtocol;

/// <summary>
/// The client function the hook targets, exported under the client's exact decorated name.
/// Returns a running call count (a stand-in for a Message_t*), so the host can check every call
/// still reaches here.
/// </summary>
public static class Exports
{
    private static int calls;

    [UnmanagedCallersOnly(EntryPoint = "DataBlockToMessage", CallConvs = [typeof(CallConvCdecl)])]
    public static nint DataBlockToMessage(uint size, nint data) => ++calls;
}
