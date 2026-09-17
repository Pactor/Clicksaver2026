using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace ClickSaver2026.Hook;

/// <summary>
/// The ClickSaver2026 hook, loaded into the Anarchy Online client by the app.
///
/// - IAT-hooks DataBlockToMessage in MessageProtocol.dll (the call that turns each decoded server
///   block into a message) and forwards every block to the app. All parsing happens in the app.
/// - IAT-hooks N3InterfaceModule_t::N3Msg_GenerateMissions in Interfaces.dll (the call the mission
///   terminal's Request button makes) and records it. On the app's command it makes the same call
///   again, on the client's own UI thread through the game window, so missions roll without the
///   mouse.
///
/// The DLL is resident for the client's lifetime: attaching installs the hooks, detaching removes
/// them, and the pipe threads run throughout, reconnecting whenever the app comes and goes. The
/// design follows ClickSaver's AOHook.dll (Copyright (C) 2002 Morb and later maintainers).
/// </summary>
internal static unsafe class Hook
{
    private const string MessageProtocolDll = "MessageProtocol.dll";

    // The game imports "?DataBlockToMessage@@YAPAVMessage_t@@IPAX@Z"; matched by this prefix so a
    // stand-in that dropped the signature suffix ("?DataBlockToMessage") matches too.
    private const string DataBlockToMessagePrefix = "?DataBlockToMessage";

    // Where the Request button's call is imported from, in the game and in the test harness.
    private static readonly (string Dll, string Name)[] GenerateMissionsImports =
    [
        ("Interfaces.dll", "?N3Msg_GenerateMissions@N3InterfaceModule_t@@QAEXPAVMissionGenerateInfo_t@@@Z"),
        (MessageProtocolDll, "?N3Msg_GenerateMissions@N3InterfaceModule_t@@QAEXPAVMissionGenerateInfo_t@@@Z"),
    ];

    private const string GameWindowClass = "Anarchy client";
    private const long MaxQueuedBytes = 64 * 1024 * 1024;

    // A plain object because the queue uses Monitor.Wait/Pulse, not the Lock type.
    private static readonly object QueueLock = new();
    private static readonly Queue<byte[]> Queue = new();
    private static long queuedBytes;
    private static uint dropped;

    private static readonly Lock RequestLock = new();
    private static bool haveRecording;
    private static nint recordedThis;
    private static uint gameThread;
    private static readonly byte[] recorded = new byte[Wire.MissionGenerateInfoSize];

    private static bool havePending;
    private static uint pendingId;
    private static bool pendingUseRecorded;
    private static readonly byte[] pendingInfo = new byte[Wire.MissionGenerateInfoSize];

    private static string pipePath = string.Empty;
    private static uint requestMessage;

    private static nint dataBlockOriginal;
    private static nint generateOriginal;
    private static (string Dll, string Name) generateImport;
    private static bool messagesHooked;
    private static bool requestsHooked;
    private static HookStatus status = HookStatus.MessageProtocolNotLoaded;

    private static volatile bool active;
    private static volatile bool connected;
    private static int inFlight;
    private static bool started;

    private static nint gameWindow;
    private static nint originalWndProc;
    private static nint foundWindow;

    // Called by the app right after LoadLibrary, and again to re-attach. Installs the hooks and,
    // the first time, starts the pipe threads.
    [UnmanagedCallersOnly(EntryPoint = "ClickSaverStart")]
    public static uint Start(nint _)
    {
        // Install before the pipe threads so the first Hello carries the real status.
        status = InstallHooks();
        active = true;

        if (!started)
        {
            pipePath = PipePath();
            requestMessage = NativeApi.RegisterWindowMessageW("ClickSaver2026.RequestMissions");
            new Thread(WriterLoop) { IsBackground = true }.Start();
            new Thread(CommandLoop) { IsBackground = true }.Start();
            started = true;
        }

        return (uint)status;
    }

    // Called by the app as a remote thread to detach. Removes the hooks; the DLL stays resident.
    [UnmanagedCallersOnly(EntryPoint = "ClickSaverShutdown")]
    public static uint ClickSaverShutdown(nint _)
    {
        active = false;

        if (originalWndProc != 0)
        {
            // Only unhook the window if nothing has subclassed it on top of us since.
            if (NativeApi.GetWindowLongPtr(gameWindow, NativeApi.GwlpWndProc) == (nint)(delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nint>)&WindowProc)
            {
                NativeApi.SetWindowLongPtr(gameWindow, NativeApi.GwlpWndProc, originalWndProc);
                originalWndProc = 0;
            }
        }

        if (requestsHooked)
        {
            IatHook.Restore(generateImport.Dll, generateImport.Name, (nint)(delegate* unmanaged[Thiscall]<nint, nint, void>)&GenerateDetour, generateOriginal);
            requestsHooked = false;
        }

        if (messagesHooked)
        {
            IatHook.Restore(MessageProtocolDll, DataBlockToMessagePrefix, (nint)(delegate* unmanaged[Cdecl]<uint, nint, nint>)&DataBlockDetour, dataBlockOriginal, prefix: true);
            messagesHooked = false;
        }

        // A call may have entered a detour just before it was restored; let it leave.
        while (Volatile.Read(ref inFlight) != 0)
        {
            Thread.Sleep(20);
        }

        return 0;
    }

    private static HookStatus InstallHooks()
    {
        if (!messagesHooked)
        {
            // Prefix match: the game imports "?DataBlockToMessage@@..."; a stand-in may drop the suffix.
            dataBlockOriginal = IatHook.PatchAll(MessageProtocolDll, DataBlockToMessagePrefix, (nint)(delegate* unmanaged[Cdecl]<uint, nint, nint>)&DataBlockDetour, prefix: true);
            if (dataBlockOriginal == 0)
            {
                // Nothing imports it: either MessageProtocol.dll is not loaded yet (injected too
                // early) or this is not the game client.
                return NativeApi.GetModuleHandleW(MessageProtocolDll) == 0 ? HookStatus.MessageProtocolNotLoaded : HookStatus.ExportNotFound;
            }

            messagesHooked = true;
        }

        // Rolling missions is an extra: messages are still forwarded without it.
        if (!requestsHooked)
        {
            foreach ((string dll, string name) in GenerateMissionsImports)
            {
                nint original = IatHook.PatchAll(dll, name, (nint)(delegate* unmanaged[Thiscall]<nint, nint, void>)&GenerateDetour);
                if (original != 0)
                {
                    generateOriginal = original;
                    generateImport = (dll, name);
                    requestsHooked = true;
                    break;
                }
            }
        }

        return HookStatus.Hooked;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static nint DataBlockDetour(uint size, nint data)
    {
        Interlocked.Increment(ref inFlight);
        try
        {
            if (active && data != 0 && size != 0 && size <= Wire.MaxMessageSize)
            {
                Send(FrameKind.IncomingMessage, new ReadOnlySpan<byte>((void*)data, (int)size));
            }
        }
        catch
        {
            // Never let a forwarding error reach the client; still call the original.
        }

        nint result = ((delegate* unmanaged[Cdecl]<uint, nint, nint>)dataBlockOriginal)(size, data);
        Interlocked.Decrement(ref inFlight);
        return result;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvThiscall)])]
    private static void GenerateDetour(nint self, nint info)
    {
        Interlocked.Increment(ref inFlight);
        try
        {
            if (active && info != 0)
            {
                lock (RequestLock)
                {
                    new ReadOnlySpan<byte>((void*)info, Wire.MissionGenerateInfoSize).CopyTo(recorded);
                    recordedThis = self;
                    gameThread = NativeApi.GetCurrentThreadId();
                    haveRecording = true;
                }

                SendMissionRequested(RequestSource.Player, recorded);
            }
        }
        catch
        {
            // Never let a recording error reach the client; still make the real call.
        }

        ((delegate* unmanaged[Thiscall]<nint, nint, void>)generateOriginal)(self, info);
        Interlocked.Decrement(ref inFlight);
    }

    // Runs on the game window's thread - the thread the Request button itself calls from.
    private static void RunPendingRequest()
    {
        uint id;
        bool useRecorded;
        var info = new byte[Wire.MissionGenerateInfoSize];
        nint self;
        uint thread;
        lock (RequestLock)
        {
            if (!havePending)
            {
                return;
            }

            havePending = false;
            id = pendingId;
            useRecorded = pendingUseRecorded;
            if (useRecorded)
            {
                if (!haveRecording)
                {
                    SendCommandResult(id, CommandStatus.NothingRecorded);
                    return;
                }

                recorded.CopyTo(info, 0);
            }
            else
            {
                pendingInfo.CopyTo(info, 0);
            }

            self = recordedThis;
            thread = gameThread;
        }

        if (self == 0 || thread != NativeApi.GetCurrentThreadId())
        {
            SendCommandResult(id, CommandStatus.NothingRecorded);
            return;
        }

        fixed (byte* p = info)
        {
            ((delegate* unmanaged[Thiscall]<nint, nint, void>)generateOriginal)(self, (nint)p);
        }

        SendMissionRequested(RequestSource.ClickSaver, info);
        SendCommandResult(id, CommandStatus.Done);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WindowProc(nint window, uint message, nint wParam, nint lParam)
    {
        if (message == requestMessage && requestMessage != 0)
        {
            Interlocked.Increment(ref inFlight);
            try
            {
                RunPendingRequest();
            }
            catch
            {
                // A failed replay must not take the game's window thread down.
            }

            Interlocked.Decrement(ref inFlight);
            return 0;
        }

        return NativeApi.CallWindowProc(originalWndProc, window, message, wParam, lParam);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int FindWindowCallback(nint window, nint _)
    {
        var className = new char[64];
        NativeApi.GetWindowThreadProcessId(window, out uint processId);
        if (processId == NativeApi.GetCurrentProcessId()
            && NativeApi.GetClassNameW(window, className, className.Length) != 0
            && new string(className, 0, IndexOfZero(className)) == GameWindowClass)
        {
            foundWindow = window;
            return 0;
        }

        return 1;
    }

    private static bool EnsureWindowHooked()
    {
        if (originalWndProc != 0)
        {
            return true;
        }

        foundWindow = 0;
        NativeApi.EnumWindows((delegate* unmanaged[Stdcall]<nint, nint, int>)&FindWindowCallback, 0);
        if (foundWindow == 0)
        {
            return false;
        }

        gameWindow = foundWindow;
        originalWndProc = NativeApi.SetWindowLongPtr(gameWindow, NativeApi.GwlpWndProc, (nint)(delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nint>)&WindowProc);
        return originalWndProc != 0;
    }

    private static void HandleCommand(uint kind, uint id, ReadOnlySpan<byte> payload)
    {
        if (kind != (uint)CommandKind.RequestMissions || (payload.Length != 0 && payload.Length != Wire.MissionGenerateInfoSize))
        {
            SendCommandResult(id, CommandStatus.BadCommand);
            return;
        }

        if (!active || !requestsHooked)
        {
            SendCommandResult(id, CommandStatus.NotSupported);
            return;
        }

        lock (RequestLock)
        {
            if (havePending)
            {
                SendCommandResult(id, CommandStatus.Busy);
                return;
            }

            if (payload.Length == 0 && !haveRecording)
            {
                SendCommandResult(id, CommandStatus.NothingRecorded);
                return;
            }

            pendingId = id;
            pendingUseRecorded = payload.Length == 0;
            if (payload.Length != 0)
            {
                payload.CopyTo(pendingInfo);
            }

            havePending = true;
        }

        if (!EnsureWindowHooked() || !NativeApi.PostMessage(gameWindow, requestMessage, 0, 0))
        {
            lock (RequestLock)
            {
                havePending = false;
            }

            SendCommandResult(id, CommandStatus.WindowNotFound);
        }
    }

    // ---- Frames to the app ----

    private static void Send(FrameKind kind, ReadOnlySpan<byte> payload)
    {
        if (!connected)
        {
            return;
        }

        byte[] frame = new byte[16 + payload.Length];
        BitConverter.TryWriteBytes(frame.AsSpan(0), (uint)kind);
        BitConverter.TryWriteBytes(frame.AsSpan(4), (uint)payload.Length);
        BitConverter.TryWriteBytes(frame.AsSpan(8), Now());
        payload.CopyTo(frame.AsSpan(16));

        lock (QueueLock)
        {
            if (queuedBytes + frame.Length > MaxQueuedBytes)
            {
                dropped++;
                return;
            }

            queuedBytes += frame.Length;
            Queue.Enqueue(frame);
            Monitor.Pulse(QueueLock);
        }
    }

    private static void SendMissionRequested(RequestSource source, ReadOnlySpan<byte> info)
    {
        Span<byte> payload = stackalloc byte[4 + Wire.MissionGenerateInfoSize];
        BitConverter.TryWriteBytes(payload, (uint)source);
        info.CopyTo(payload[4..]);
        Send(FrameKind.MissionRequested, payload);
    }

    private static void SendCommandResult(uint id, CommandStatus resultStatus)
    {
        Span<byte> payload = stackalloc byte[8];
        BitConverter.TryWriteBytes(payload, id);
        BitConverter.TryWriteBytes(payload[4..], (uint)resultStatus);
        Send(FrameKind.CommandResult, payload);
    }

    // ---- Pipe threads (live for the process; reconnect as the app comes and goes) ----

    private static void WriterLoop()
    {
        while (true)
        {
            nint pipe = NativeApi.CreateFileW(pipePath, NativeApi.GenericWrite, 0, 0, NativeApi.OpenExisting, 0, 0);
            if (pipe == NativeApi.InvalidHandle)
            {
                Thread.Sleep(1000);
                continue;
            }

            Serve(pipe);
            NativeApi.CloseHandle(pipe);
        }
    }

    private static void Serve(nint pipe)
    {
        Span<byte> hello = stackalloc byte[20];
        BitConverter.TryWriteBytes(hello, Wire.HelloMagic);
        BitConverter.TryWriteBytes(hello[4..], Wire.ProtocolVersion);
        BitConverter.TryWriteBytes(hello[6..], (ushort)status);
        BitConverter.TryWriteBytes(hello[8..], NativeApi.GetCurrentProcessId());
        BitConverter.TryWriteBytes(hello[12..], Wire.HookVersion);
        BitConverter.TryWriteBytes(hello[16..], requestsHooked ? (uint)HookCapabilities.CanRequestMissions : 0u);
        if (!WriteAll(pipe, hello))
        {
            return;
        }

        connected = true;

        // A request made before the app connected is still the one to repeat.
        lock (RequestLock)
        {
            if (haveRecording)
            {
                SendMissionRequested(RequestSource.Player, recorded);
            }
        }

        Span<byte> notice = stackalloc byte[20];
        while (true)
        {
            byte[] frame;
            lock (QueueLock)
            {
                while (Queue.Count == 0)
                {
                    Monitor.Wait(QueueLock);
                }

                frame = Queue.Dequeue();
                queuedBytes -= frame.Length;
            }

            uint lost = Interlocked.Exchange(ref dropped, 0);
            if (lost != 0)
            {
                BitConverter.TryWriteBytes(notice, (uint)FrameKind.Dropped);
                BitConverter.TryWriteBytes(notice[4..], 4u);
                BitConverter.TryWriteBytes(notice[8..], Now());
                BitConverter.TryWriteBytes(notice[16..], lost);
                if (!WriteAll(pipe, notice))
                {
                    break;
                }
            }

            if (!WriteAll(pipe, frame))
            {
                break;
            }
        }

        connected = false;
        lock (QueueLock)
        {
            Queue.Clear();
            queuedBytes = 0;
        }
    }

    private static void CommandLoop()
    {
        string path = pipePath + Wire.CommandPipeSuffix;
        byte[] pid = new byte[4];
        BitConverter.TryWriteBytes(pid, NativeApi.GetCurrentProcessId());
        while (true)
        {
            nint pipe = NativeApi.CreateFileW(path, NativeApi.GenericRead | NativeApi.GenericWrite, 0, 0, NativeApi.OpenExisting, 0, 0);
            if (pipe == NativeApi.InvalidHandle)
            {
                Thread.Sleep(1000);
                continue;
            }

            if (WriteAll(pipe, pid))
            {
                var header = new byte[12];
                while (ReadAll(pipe, header))
                {
                    uint kind = BitConverter.ToUInt32(header, 0);
                    uint id = BitConverter.ToUInt32(header, 4);
                    uint length = BitConverter.ToUInt32(header, 8);
                    if (length > Wire.MissionGenerateInfoSize)
                    {
                        break;
                    }

                    var payload = new byte[length];
                    if (length != 0 && !ReadAll(pipe, payload))
                    {
                        break;
                    }

                    HandleCommand(kind, id, payload);
                }
            }

            NativeApi.CloseHandle(pipe);
        }
    }

    private static bool WriteAll(nint pipe, ReadOnlySpan<byte> data)
    {
        fixed (byte* p = data)
        {
            int offset = 0;
            while (offset < data.Length)
            {
                if (!NativeApi.WriteFile(pipe, p + offset, (uint)(data.Length - offset), out uint written, 0) || written == 0)
                {
                    return false;
                }

                offset += (int)written;
            }
        }

        return true;
    }

    private static bool ReadAll(nint pipe, byte[] data)
    {
        fixed (byte* p = data)
        {
            int offset = 0;
            while (offset < data.Length)
            {
                if (!NativeApi.ReadFile(pipe, p + offset, (uint)(data.Length - offset), out uint read, 0) || read == 0)
                {
                    return false;
                }

                offset += (int)read;
            }
        }

        return true;
    }

    private static long Now()
    {
        NativeApi.GetSystemTimePreciseAsFileTime(out long fileTime);
        return fileTime;
    }

    private static string PipePath()
    {
        var buffer = new char[128];
        uint length = NativeApi.GetEnvironmentVariableW(Wire.PipeNameVariable, buffer, (uint)buffer.Length);
        string name = length != 0 && length < buffer.Length ? new string(buffer, 0, (int)length) : Wire.DefaultPipeName;
        return @"\\.\pipe\" + name;
    }

    private static int IndexOfZero(char[] text)
    {
        int index = Array.IndexOf(text, '\0');
        return index < 0 ? text.Length : index;
    }
}
