using System.Runtime.InteropServices;

namespace ClickSaver2026.Hook;

/// <summary>
/// Import Address Table hooking: redirect a client module's calls to an imported function by
/// swapping the pointer in its IAT, in place of an inline detour. Patches every loaded module
/// that imports the function, so every cross-module caller is caught.
/// </summary>
internal static unsafe partial class IatHook
{
    [LibraryImport("kernel32.dll", EntryPoint = "K32EnumProcessModules", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumProcessModules(nint process, nint* modules, uint size, out uint needed);

    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentProcess();

    /// <summary>
    /// Points every loaded module's IAT slot for <paramref name="importDll"/>!<paramref name="importName"/>
    /// at <paramref name="detour"/>. Returns the original function, or 0 when nothing imports it.
    /// With <paramref name="prefix"/>, the import name only has to start with <paramref name="importName"/>
    /// (the client's decorated names carry a signature suffix a stand-in may not).
    /// </summary>
    public static nint PatchAll(string importDll, string importName, nint detour, bool prefix = false)
    {
        nint original = 0;
        foreach (nint module in Modules())
        {
            foreach (nint slot in ImportSlots(module, importDll, importName, prefix))
            {
                nint current = *(nint*)slot;
                if (current == detour)
                {
                    continue;
                }

                if (original == 0)
                {
                    original = current;
                }

                Write(slot, detour);
            }
        }

        return original;
    }

    /// <summary>The current value in the first IAT slot for the import, or 0 when nothing imports it.</summary>
    public static nint Find(string importDll, string importName, bool prefix = false)
    {
        foreach (nint module in Modules())
        {
            foreach (nint slot in ImportSlots(module, importDll, importName, prefix))
            {
                return *(nint*)slot;
            }
        }

        return 0;
    }

    /// <summary>Puts <paramref name="original"/> back wherever <paramref name="detour"/> was written.</summary>
    public static void Restore(string importDll, string importName, nint detour, nint original, bool prefix = false)
    {
        foreach (nint module in Modules())
        {
            foreach (nint slot in ImportSlots(module, importDll, importName, prefix))
            {
                if (*(nint*)slot == detour)
                {
                    Write(slot, original);
                }
            }
        }
    }

    private static void Write(nint slot, nint value)
    {
        if (NativeApi.VirtualProtect(slot, (nuint)sizeof(nint), NativeApi.PageExecuteReadWrite, out uint old))
        {
            *(nint*)slot = value;
            NativeApi.VirtualProtect(slot, (nuint)sizeof(nint), old, out _);
        }
    }

    private static nint[] Modules()
    {
        var buffer = new nint[1024];
        fixed (nint* p = buffer)
        {
            if (!EnumProcessModules(GetCurrentProcess(), p, (uint)(buffer.Length * sizeof(nint)), out uint needed))
            {
                return [];
            }

            int count = Math.Min(buffer.Length, (int)(needed / sizeof(nint)));
            return buffer[..count];
        }
    }

    // The IAT slots in one module that import importDll!importName. Reads the mapped PE image.
    private static IEnumerable<nint> ImportSlots(nint module, string importDll, string importName, bool prefix)
    {
        var slots = new List<nint>();
        try
        {
            byte* baseAddress = (byte*)module;
            int lfanew = *(int*)(baseAddress + 0x3C);
            byte* nt = baseAddress + lfanew;
            if (*(uint*)nt != 0x00004550) // "PE\0\0"
            {
                return slots;
            }

            // 32-bit optional header: import directory is data directory entry 1 at +0x68.
            uint importRva = *(uint*)(nt + 0x18 + 0x68);
            uint importSize = *(uint*)(nt + 0x18 + 0x68 + 4);
            if (importRva == 0 || importSize == 0)
            {
                return slots;
            }

            // IMAGE_IMPORT_DESCRIPTOR: OriginalFirstThunk(0), TimeDateStamp(4), Forwarder(8),
            // Name(12), FirstThunk(16); 20 bytes; terminated by an all-zero descriptor.
            for (byte* descriptor = baseAddress + importRva; *(uint*)(descriptor + 12) != 0; descriptor += 20)
            {
                uint nameRva = *(uint*)(descriptor + 12);
                if (!EqualsAscii(baseAddress + nameRva, importDll))
                {
                    continue;
                }

                uint namesRva = *(uint*)descriptor;         // OriginalFirstThunk (the name array)
                uint iatRva = *(uint*)(descriptor + 16);    // FirstThunk (the writable slots)
                if (namesRva == 0)
                {
                    namesRva = iatRva;
                }

                uint* names = (uint*)(baseAddress + namesRva);
                nint* iat = (nint*)(baseAddress + iatRva);
                for (int i = 0; names[i] != 0; i++)
                {
                    uint entry = names[i];
                    if ((entry & 0x80000000) != 0)
                    {
                        continue; // imported by ordinal, so it has no name
                    }

                    // IMAGE_IMPORT_BY_NAME: hint (2 bytes), then the ASCII name.
                    if (MatchesAscii(baseAddress + entry + 2, importName, prefix))
                    {
                        slots.Add((nint)(iat + i));
                    }
                }
            }
        }
        catch
        {
            // A module we cannot parse is simply skipped.
        }

        return slots;
    }

    private static bool EqualsAscii(byte* text, string expected) => MatchesAscii(text, expected, prefix: false);

    // Compares a null-terminated ASCII string with expected. With prefix, text only has to start
    // with expected; otherwise it must equal it.
    private static bool MatchesAscii(byte* text, string expected, bool prefix)
    {
        for (int i = 0; i < expected.Length; i++)
        {
            byte b = text[i];
            if (b == 0 || char.ToLowerInvariant((char)b) != char.ToLowerInvariant(expected[i]))
            {
                return false;
            }
        }

        return prefix || text[expected.Length] == 0;
    }
}
