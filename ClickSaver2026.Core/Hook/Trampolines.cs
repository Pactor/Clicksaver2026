namespace ClickSaver2026.Core.Hook;

/// <summary>
/// Tiny x86 machine-code trampolines that adapt between __thiscall and __cdecl, so the hook does
/// not depend on the runtime emitting a correct __thiscall reverse callback (which crashes the
/// client). Only __cdecl reverse callbacks and forward calls are used from managed code; these
/// bytes bridge to the client's __thiscall N3Msg_GenerateMissions.
/// </summary>
/// <remarks>
/// Shared by the hook and its tests (linked into both), so the exact bytes are verified against
/// hand-written __thiscall targets and callers without relying on any runtime __thiscall codegen.
/// </remarks>
public static class Trampolines
{
    /// <summary>
    /// A stub to place in the import slot of a __thiscall function. On a __thiscall call
    /// (ecx = this, the one argument on the stack) it calls <paramref name="cdeclCallback"/>(this,
    /// arg) as __cdecl, then tail-jumps to <paramref name="originalThiscall"/> so the real function
    /// still runs and cleans its argument. Nets out identical to the original __thiscall callee.
    /// </summary>
    public static byte[] ThiscallToCdeclDetour(nint cdeclCallback, nint originalThiscall)
    {
        // push ecx                 ; save this
        // push dword [esp+8]       ; push arg
        // push dword [esp+4]       ; push this
        // mov  eax, cdeclCallback
        // call eax                 ; cdecl callback(this, arg)
        // add  esp, 8              ; drop the two args
        // pop  ecx                 ; restore this
        // mov  eax, originalThiscall
        // jmp  eax                 ; tail-call the real thiscall (it does ret 4)
        var code = new List<byte> { 0x51, 0xFF, 0x74, 0x24, 0x08, 0xFF, 0x74, 0x24, 0x04, 0xB8 };
        AppendInt32(code, cdeclCallback);
        code.AddRange([0xFF, 0xD0, 0x83, 0xC4, 0x08, 0x59, 0xB8]);
        AppendInt32(code, originalThiscall);
        code.AddRange([0xFF, 0xE0]);
        return [.. code];
    }

    /// <summary>
    /// A __cdecl stub that calls a __thiscall function: called as callback(this, arg) in __cdecl,
    /// it puts this in ecx, pushes arg, and calls <paramref name="originalThiscall"/> (which cleans
    /// its argument with ret 4). Lets managed code invoke the client's thiscall with a plain
    /// <c>delegate* unmanaged[Cdecl]</c>.
    /// </summary>
    public static byte[] CdeclToThiscallReplay(nint originalThiscall)
    {
        // mov  ecx, [esp+4]        ; this
        // push dword [esp+8]       ; push arg
        // mov  eax, originalThiscall
        // call eax                 ; thiscall(this, arg); ret 4 cleans arg
        // ret                      ; cdecl: caller cleans this, arg
        var code = new List<byte> { 0x8B, 0x4C, 0x24, 0x04, 0xFF, 0x74, 0x24, 0x08, 0xB8 };
        AppendInt32(code, originalThiscall);
        code.AddRange([0xFF, 0xD0, 0xC3]);
        return [.. code];
    }

    /// <summary>
    /// A __cdecl stub that calls a no-argument __thiscall member function: called as callback(this)
    /// in __cdecl, it puts this in ecx and calls <paramref name="originalThiscall"/> (which takes no
    /// stack arguments, so nothing to clean). Used to call the exported Message_t serialisers
    /// (CreateDataBlock, DataBlockSizeGet) on a message object.
    /// </summary>
    public static byte[] CdeclToThiscallCall0(nint originalThiscall)
    {
        // mov  ecx, [esp+4]        ; this
        // mov  eax, originalThiscall
        // call eax                 ; thiscall(this); no args to clean
        // ret                      ; cdecl: caller cleans this
        var code = new List<byte> { 0x8B, 0x4C, 0x24, 0x04, 0xB8 };
        AppendInt32(code, originalThiscall);
        code.AddRange([0xFF, 0xD0, 0xC3]);
        return [.. code];
    }

    /// <summary>
    /// A __cdecl stub that calls a three-argument __thiscall member function: called as
    /// callback(this, a, b, c) in __cdecl, it puts this in ecx, pushes a, b, c and calls
    /// <paramref name="originalThiscall"/> (which cleans its three arguments with ret 12). Used to
    /// replay Connection_t::Send(id, size, data).
    /// </summary>
    public static byte[] CdeclToThiscallCall3(nint originalThiscall)
    {
        // mov  ecx, [esp+4]        ; this
        // push dword [esp+0x10]    ; c   (each push shifts the next source by +4)
        // push dword [esp+0x10]    ; b
        // push dword [esp+0x10]    ; a
        // mov  eax, originalThiscall
        // call eax                 ; thiscall(this, a, b, c); ret 12 cleans the args
        // ret                      ; cdecl: caller cleans this, a, b, c
        var code = new List<byte> { 0x8B, 0x4C, 0x24, 0x04, 0xFF, 0x74, 0x24, 0x10, 0xFF, 0x74, 0x24, 0x10, 0xFF, 0x74, 0x24, 0x10, 0xB8 };
        AppendInt32(code, originalThiscall);
        code.AddRange([0xFF, 0xD0, 0xC3]);
        return [.. code];
    }

    /// <summary>
    /// A stub to place in the import slot of a two-argument __thiscall function. On a __thiscall
    /// call (ecx = this, two arguments on the stack) it calls <paramref name="cdeclCallback"/>(this,
    /// a, b) as __cdecl, then tail-jumps to <paramref name="originalThiscall"/> so the real function
    /// still runs and cleans its arguments. Used to observe Connection_t::Send(id, const Message_t&).
    /// </summary>
    public static byte[] ThiscallToCdeclDetour2(nint cdeclCallback, nint originalThiscall)
    {
        // push ecx                 ; save this        -> [0]=this [4]=ret [8]=a [C]=b
        // push dword [esp+0x0C]    ; b
        // push dword [esp+0x0C]    ; a
        // push dword [esp+0x08]    ; this
        // mov  eax, cdeclCallback
        // call eax                 ; cdecl callback(this, a, b)
        // add  esp, 0x0C           ; drop the three args
        // pop  ecx                 ; restore this
        // mov  eax, originalThiscall
        // jmp  eax                 ; tail-call the real thiscall (it does ret 8)
        var code = new List<byte> { 0x51, 0xFF, 0x74, 0x24, 0x0C, 0xFF, 0x74, 0x24, 0x0C, 0xFF, 0x74, 0x24, 0x08, 0xB8 };
        AppendInt32(code, cdeclCallback);
        code.AddRange([0xFF, 0xD0, 0x83, 0xC4, 0x0C, 0x59, 0xB8]);
        AppendInt32(code, originalThiscall);
        code.AddRange([0xFF, 0xE0]);
        return [.. code];
    }

    private static void AppendInt32(List<byte> code, nint value)
    {
        uint v = unchecked((uint)value);
        code.Add((byte)v);
        code.Add((byte)(v >> 8));
        code.Add((byte)(v >> 16));
        code.Add((byte)(v >> 24));
    }
}
