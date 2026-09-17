using System.Runtime.InteropServices;
using ClickSaver2026.Core.Hook;

namespace ClickSaver2026.Tests;

/// <summary>
/// Verifies the thiscall/cdecl trampoline bytes against hand-written x86 targets and callers, so
/// the mission-roll hook never relies on the runtime emitting a correct __thiscall callback (which
/// crashed the client). No runtime __thiscall codegen is used here - every thiscall is raw bytes.
/// </summary>
public sealed unsafe class TrampolineTests
{
    [Fact]
    public void CdeclToThiscallReplayCallsTheThiscallWithThisAndArg()
    {
        Assert.SkipUnless(Environment.Is64BitProcess == false, "32-bit only.");

        int* recorded = Zeroed();
        // A __thiscall recorder: writes ecx (this) and [esp+4] (arg), then ret 4.
        nint recorder = Exec(ThiscallRecorder((nint)recorded));
        nint replay = Exec(Trampolines.CdeclToThiscallReplay(recorder));

        var call = (delegate* unmanaged[Cdecl]<nint, nint, void>)replay;
        for (int i = 1; i <= 100; i++)
        {
            call(0x1000 + i, 0x2000 + i); // if the stack were unbalanced, this loop would crash
        }

        Assert.Equal(0x1000 + 100, recorded[0]); // this
        Assert.Equal(0x2000 + 100, recorded[1]); // arg
    }

    [Fact]
    public void ThiscallToCdeclDetourCallsTheCallbackThenTheOriginal()
    {
        Assert.SkipUnless(Environment.Is64BitProcess == false, "32-bit only.");

        int* fromCallback = Zeroed();
        int* fromOriginal = Zeroed();
        nint callback = Exec(CdeclRecorder((nint)fromCallback)); // records (this, arg) as __cdecl
        nint original = Exec(ThiscallRecorder((nint)fromOriginal)); // records (this, arg) as __thiscall
        nint detour = Exec(Trampolines.ThiscallToCdeclDetour(callback, original));
        nint invokeAsThiscall = Exec(ThiscallCaller());

        var invoke = (delegate* unmanaged[Cdecl]<nint, nint, nint, void>)invokeAsThiscall;
        for (int i = 1; i <= 100; i++)
        {
            invoke(detour, 0x3000 + i, 0x4000 + i);
        }

        // The callback ran with (this, arg)...
        Assert.Equal(0x3000 + 100, fromCallback[0]);
        Assert.Equal(0x4000 + 100, fromCallback[1]);
        // ...and the original thiscall still ran with the same (this, arg).
        Assert.Equal(0x3000 + 100, fromOriginal[0]);
        Assert.Equal(0x4000 + 100, fromOriginal[1]);
    }

    private static int* Zeroed() => (int*)NativeMemory.AllocZeroed(8);

    // __thiscall: mov eax,buf; mov [eax],ecx; mov edx,[esp+4]; mov [eax+4],edx; ret 4
    private static byte[] ThiscallRecorder(nint buffer)
    {
        var code = new List<byte> { 0xB8 };
        AppendInt32(code, buffer);
        code.AddRange([0x89, 0x08, 0x8B, 0x54, 0x24, 0x04, 0x89, 0x50, 0x04, 0xC2, 0x04, 0x00]);
        return [.. code];
    }

    // __cdecl: mov eax,buf; mov edx,[esp+4]; mov [eax],edx; mov edx,[esp+8]; mov [eax+4],edx; ret
    private static byte[] CdeclRecorder(nint buffer)
    {
        var code = new List<byte> { 0xB8 };
        AppendInt32(code, buffer);
        code.AddRange([0x8B, 0x54, 0x24, 0x04, 0x89, 0x10, 0x8B, 0x54, 0x24, 0x08, 0x89, 0x50, 0x04, 0xC3]);
        return [.. code];
    }

    // __cdecl Invoke(stub, this, arg): call stub as __thiscall (ecx=this, arg pushed), then ret.
    // mov ecx,[esp+8]; mov eax,[esp+12]; push eax; mov eax,[esp+8]; call eax; ret
    private static byte[] ThiscallCaller() =>
        [0x8B, 0x4C, 0x24, 0x08, 0x8B, 0x44, 0x24, 0x0C, 0x50, 0x8B, 0x44, 0x24, 0x08, 0xFF, 0xD0, 0xC3];

    private static nint Exec(byte[] code)
    {
        nint memory = VirtualAlloc(0, (nuint)code.Length, 0x3000, 0x40);
        Marshal.Copy(code, 0, memory, code.Length);
        FlushInstructionCache(GetCurrentProcess(), memory, (nuint)code.Length);
        return memory;
    }

    private static void AppendInt32(List<byte> code, nint value)
    {
        uint v = unchecked((uint)value);
        code.Add((byte)v);
        code.Add((byte)(v >> 8));
        code.Add((byte)(v >> 16));
        code.Add((byte)(v >> 24));
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint VirtualAlloc(nint address, nuint size, uint allocationType, uint protect);

    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentProcess();

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlushInstructionCache(nint process, nint baseAddress, nuint size);
}
