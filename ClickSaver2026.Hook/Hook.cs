using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using ClickSaver2026.Core.Hook; // Trampolines is linked in from Core (no assembly reference)

namespace ClickSaver2026.Hook;

/// <summary>
/// The ClickSaver2026 hook, loaded into the Anarchy Online client by the app.
///
/// - IAT-hooks DataBlockToMessage in MessageProtocol.dll (the call that turns each decoded server
///   block into a message) and forwards every block to the app. All parsing happens in the app.
/// - IAT-hooks N3InterfaceModule_t::N3Msg_GenerateMissions in Interfaces.dll (the call the mission
///   terminal's Request button makes) and records it. On the app's command it makes the same call
///   again from the message-pump hook (PeekMessage/GetMessage), which runs on the client's own UI
///   thread - the button's thread - so missions roll without the mouse and without a window subclass.
///
/// The DLL is resident for the client's lifetime: attaching installs the hooks, detaching removes
/// them, and the pipe threads run throughout, reconnecting whenever the app comes and goes. The
/// design follows ClickSaver's AOHook.dll (Copyright (C) 2002 Morb and later maintainers).
/// </summary>
internal static unsafe class Hook
{
    private const string MessageProtocolDll = "MessageProtocol.dll";
    private const string ConnectionDll = "Connection.dll";

    // The game imports "?DataBlockToMessage@@YAPAVMessage_t@@IPAX@Z"; matched by this prefix so a
    // stand-in that dropped the signature suffix ("?DataBlockToMessage") matches too.
    private const string DataBlockToMessagePrefix = "?DataBlockToMessage";

    // The Request button's call. Records the slider settings (for display) and arms request capture.
    // Interfaces.dll in the game; MessageProtocol.dll in the test harness stand-in.
    private static readonly (string Dll, string Name)[] GenerateMissionsImports =
    [
        ("Interfaces.dll", "?N3Msg_GenerateMissions@N3InterfaceModule_t@@QAEXPAVMissionGenerateInfo_t@@@Z"),
        (MessageProtocolDll, "?N3Msg_GenerateMissions@N3InterfaceModule_t@@QAEXPAVMissionGenerateInfo_t@@@Z"),
    ];

    // Connection_t::Send(unsigned int id, const Message_t&) - the outgoing send Interfaces.dll makes
    // for the mission request. Hooked in Interfaces.dll's IAT to capture the request's wire bytes.
    private const string ConnectionSendName = "?Send@Connection_t@@QAEHIABVMessage_t@@@Z";

    // The mission-request message carries the difficulty slider tick (1..11) at this byte. Patching
    // it lets the app roll at a chosen difficulty without the player hand-rolling at that setting.
    private const int RequestDifficultyOffset = 0x1E;
    private const byte MaxDifficultyTick = 11;

    // The six signed mission sliders (GoodBad, Order/Chaos, Open/Hidden, Physical/Mystical,
    // HeadOn/Stealth, Money/XP) sit right after the difficulty byte; each is (percent - 50) * 2.
    private const int RequestSlidersOffset = 0x1F;
    private const int SliderCount = 6;

    // Connection_t::Send(unsigned int id, unsigned int size, const void* data) - the raw send,
    // exported by Connection.dll and thread-safe (a send lock). Resending the captured bytes through
    // it rolls again, from any thread, without touching the mouse or the client's UI.
    private const string ConnectionSendRawName = "?Send@Connection_t@@QAEHIIPBX@Z";

    // Message_t serialisers, exported by MessageProtocol.dll, used to snapshot a request's wire
    // bytes. CreateDataBlock is virtual, so the concrete override is found in the object's vtable.
    private const string DataBlockSizeGetName = "?DataBlockSizeGet@Message_t@@QBEIXZ";
    private static readonly string[] CreateDataBlockNames =
    [
        "?CreateDataBlock@Message_t@@UBEPADXZ",
        "?CreateDataBlock@N3Message_t@@UBEPADXZ",
        "?CreateDataBlock@OperatorMessage_t@@UBEPADXZ",
        "?CreateDataBlock@PingMessage_t@@UBEPADXZ",
        "?CreateDataBlock@SystemMessage_t@@UBEPADXZ",
        "?CreateDataBlock@TextMessage_t@@UBEPADXZ",
    ];

    private const long MaxQueuedBytes = 64 * 1024 * 1024;

    // A plain object because the queue uses Monitor.Wait/Pulse, not the Lock type.
    private static readonly object QueueLock = new();
    private static readonly Queue<byte[]> Queue = new();
    private static long queuedBytes;
    private static uint dropped;

    private static readonly Lock RequestLock = new();
    private static bool haveRecording;                          // the 40-byte slider info, for display
    private static readonly byte[] recorded = new byte[Wire.MissionGenerateInfoSize];

    // The captured outgoing mission-request send, resent to roll.
    private static bool haveSendRecording;
    private static nint sendConnection;                         // Connection_t* (the send's this)
    private static uint sendMessageId;                          // the send's id argument
    private static byte[] sendBytes = [];                       // the request's wire bytes

    // Arms request capture: set on the player's N3Msg_GenerateMissions, consumed by the next
    // Connection_t::Send on the same thread.
    private static volatile bool expectSend;
    private static uint expectSendThread;

    private static string pipePath = string.Empty;

    private static nint dataBlockOriginal;
    private static nint generateOriginal;    // the client's real __thiscall N3Msg_GenerateMissions
    private static nint generateDetourStub;  // __thiscall->__cdecl detour in the Request import slot
    private static (string Dll, string Name) generateImport;

    private static nint sendOriginal;        // the client's real Connection_t::Send(Message_t&)
    private static nint sendDetourStub;      // __thiscall(2-arg)->__cdecl detour in the Send slot
    private static bool sendHooked;

    private static nint sendRawStub;         // __cdecl->__thiscall(3-arg) call to the raw Send
    private static nint sizeGetStub;         // __cdecl->__thiscall(0-arg) DataBlockSizeGet
    private static nint[] createDataBlockAddrs = [];  // exported CreateDataBlock override addresses
    private static nint createStub;          // cached __cdecl->__thiscall(0-arg) for a CreateDataBlock
    private static nint createStubFn;        // which CreateDataBlock address createStub wraps

    private static bool messagesHooked;
    private static bool requestsHooked;      // the Request-button hook (display + arming) is installed
    private static HookStatus status = HookStatus.MessageProtocolNotLoaded;

    private static volatile bool active;
    private static volatile bool connected;
    private static int inFlight;
    private static bool started;

    // Rolling needs the whole chain: the Request hook to arm capture, the Send hook to capture, and
    // the raw send + serialisers to snapshot and resend. Reported to the app as the roll capability.
    private static bool RollReady =>
        requestsHooked && sendHooked && sendRawStub != 0 && sizeGetStub != 0 && createDataBlockAddrs.Length > 0;

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

        if (sendHooked)
        {
            IatHook.Restore(ConnectionDll, ConnectionSendName, sendDetourStub, sendOriginal);
            sendHooked = false;
        }

        if (requestsHooked)
        {
            IatHook.Restore(generateImport.Dll, generateImport.Name, generateDetourStub, generateOriginal);
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

        // Rolling missions is an extra: messages are still forwarded without it. The Request call is
        // __thiscall, which the runtime does not emit correctly as a reverse callback, so it is
        // hooked through hand-written trampolines rather than a managed __thiscall detour. It records
        // the slider settings for display and arms capture of the outgoing request.
        if (!requestsHooked)
        {
            foreach ((string dll, string name) in GenerateMissionsImports)
            {
                nint original = IatHook.Find(dll, name);
                if (original == 0)
                {
                    continue;
                }

                nint onGenerate = (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&OnGenerate;
                generateDetourStub = AllocThunk(Trampolines.ThiscallToCdeclDetour(onGenerate, original));
                if (generateDetourStub == 0)
                {
                    break;
                }

                generateOriginal = original;
                generateImport = (dll, name);
                IatHook.PatchAll(dll, name, generateDetourStub);
                requestsHooked = true;
                break;
            }
        }

        // The outgoing mission-request send. Hooked in Interfaces.dll's IAT to capture the request's
        // wire bytes (a __thiscall with two stack arguments, bridged by a trampoline).
        if (requestsHooked && !sendHooked)
        {
            nint sendImport = IatHook.Find(ConnectionDll, ConnectionSendName);
            if (sendImport != 0)
            {
                nint onSend = (nint)(delegate* unmanaged[Cdecl]<nint, uint, nint, void>)&OnSend;
                sendDetourStub = AllocThunk(Trampolines.ThiscallToCdeclDetour2(onSend, sendImport));
                if (sendDetourStub != 0)
                {
                    sendOriginal = sendImport;
                    IatHook.PatchAll(ConnectionDll, ConnectionSendName, sendDetourStub);
                    sendHooked = true;
                }
            }
        }

        // The exported raw send + serialisers used to snapshot and resend the request. Resolved once;
        // available only once Connection.dll and MessageProtocol.dll are loaded.
        if (sendRawStub == 0)
        {
            nint raw = ExportAddress(ConnectionDll, ConnectionSendRawName);
            if (raw != 0)
            {
                sendRawStub = AllocThunk(Trampolines.CdeclToThiscallCall3(raw));
            }
        }

        if (sizeGetStub == 0)
        {
            nint sizeGet = ExportAddress(MessageProtocolDll, DataBlockSizeGetName);
            if (sizeGet != 0)
            {
                sizeGetStub = AllocThunk(Trampolines.CdeclToThiscallCall0(sizeGet));
            }
        }

        if (createDataBlockAddrs.Length == 0)
        {
            var addrs = new List<nint>();
            foreach (string name in CreateDataBlockNames)
            {
                nint a = ExportAddress(MessageProtocolDll, name);
                if (a != 0)
                {
                    addrs.Add(a);
                }
            }

            createDataBlockAddrs = [.. addrs];
        }

        return HookStatus.Hooked;
    }

    private static nint ExportAddress(string module, string name)
    {
        nint handle = NativeApi.GetModuleHandleW(module);
        return handle == 0 ? 0 : NativeApi.GetProcAddress(handle, name);
    }

    private static nint AllocThunk(ReadOnlySpan<byte> code)
    {
        nint memory = NativeApi.VirtualAlloc(0, (nuint)code.Length, NativeApi.MemCommitReserve, NativeApi.PageExecuteReadWrite);
        if (memory == 0)
        {
            return 0;
        }

        code.CopyTo(new Span<byte>((void*)memory, code.Length));
        NativeApi.FlushInstructionCache(NativeApi.GetCurrentProcess(), memory, (nuint)code.Length);
        return memory;
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

        // Rolls are not driven from here: a roll resends the captured request over the network
        // (Connection_t::Send is thread-safe), so nothing needs to run on this decode thread.
        Interlocked.Decrement(ref inFlight);
        return result;
    }

    // Called (as __cdecl) by the Request detour before the real call runs. Records the slider
    // settings for display and arms capture of the send the real call is about to make. The
    // trampoline tail-calls the original __thiscall function itself.
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnGenerate(nint self, nint info)
    {
        Interlocked.Increment(ref inFlight);
        try
        {
            if (active && info != 0)
            {
                lock (RequestLock)
                {
                    new ReadOnlySpan<byte>((void*)info, Wire.MissionGenerateInfoSize).CopyTo(recorded);
                    haveRecording = true;
                }

                // The next Connection_t::Send on this thread is the mission request.
                expectSendThread = NativeApi.GetCurrentThreadId();
                expectSend = true;
                SendMissionRequested(RequestSource.Player, recorded);
            }
        }
        catch
        {
            // Never let a recording error reach the client; the real call still runs.
        }

        Interlocked.Decrement(ref inFlight);
    }

    // Called (as __cdecl) by the Send detour before the real Connection_t::Send runs. When it is the
    // send the player's Request armed, on the same thread, it snapshots the message's wire bytes so a
    // roll can resend them. The trampoline tail-calls the real __thiscall Send afterwards.
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnSend(nint self, uint id, nint message)
    {
        Interlocked.Increment(ref inFlight);
        try
        {
            if (active && message != 0 && expectSend && NativeApi.GetCurrentThreadId() == expectSendThread)
            {
                expectSend = false;
                byte[]? bytes = SnapshotMessage(message);
                if (bytes is not null)
                {
                    lock (RequestLock)
                    {
                        sendConnection = self;
                        sendMessageId = id;
                        sendBytes = bytes;
                        haveSendRecording = true;
                    }
                }
            }
        }
        catch
        {
            // A capture error must never reach the client; the real send still runs.
        }

        Interlocked.Decrement(ref inFlight);
    }

    // Serialises a live Message_t to the bytes it will send: calls its (virtual) CreateDataBlock via
    // the object's own vtable, then DataBlockSizeGet, and copies the block out.
    private static byte[]? SnapshotMessage(nint message)
    {
        if (sizeGetStub == 0)
        {
            return null;
        }

        nint createFn = FindCreateDataBlock(message);
        if (createFn == 0)
        {
            return null;
        }

        nint stub = CreateBlockStub(createFn);
        if (stub == 0)
        {
            return null;
        }

        nint data = ((delegate* unmanaged[Cdecl]<nint, nint>)stub)(message);
        uint size = ((delegate* unmanaged[Cdecl]<nint, uint>)sizeGetStub)(message);
        if (data == 0 || size == 0 || size > Wire.MaxMessageSize)
        {
            return null;
        }

        var buffer = new byte[size];
        new ReadOnlySpan<byte>((void*)data, (int)size).CopyTo(buffer);
        return buffer;
    }

    // The message object's own CreateDataBlock, found by matching its vtable against the exported
    // overrides. Only the first slots are scanned, all inside MessageProtocol.dll's .rdata.
    private static nint FindCreateDataBlock(nint message)
    {
        nint vptr = *(nint*)message; // the vtable pointer is the object's first field
        if (vptr == 0)
        {
            return 0;
        }

        nint* vtable = (nint*)vptr;
        for (int slot = 0; slot < 32; slot++)
        {
            nint fn = vtable[slot];
            foreach (nint known in createDataBlockAddrs)
            {
                if (fn == known)
                {
                    return fn;
                }
            }
        }

        return 0;
    }

    private static nint CreateBlockStub(nint createFn)
    {
        if (createFn == createStubFn && createStub != 0)
        {
            return createStub;
        }

        nint stub = AllocThunk(Trampolines.CdeclToThiscallCall0(createFn));
        if (stub != 0)
        {
            createStub = stub;
            createStubFn = createFn;
        }

        return stub;
    }

    private static void HandleCommand(uint kind, uint id, ReadOnlySpan<byte> payload)
    {
        // Payload: empty = as captured; 1 byte = difficulty tick; 7 bytes = tick + the six sliders.
        if (kind != (uint)CommandKind.RequestMissions || payload.Length is not (0 or 1 or 7))
        {
            SendCommandResult(id, CommandStatus.BadCommand);
            return;
        }

        if (!active || !RollReady)
        {
            SendCommandResult(id, CommandStatus.NotSupported);
            return;
        }

        byte tick = payload.Length >= 1 ? payload[0] : (byte)0;
        ReadOnlySpan<byte> sliders = payload.Length == 7 ? payload.Slice(1, SliderCount) : default;

        // The raw send is thread-safe, so the roll runs right here on the command thread.
        Interlocked.Increment(ref inFlight);
        try
        {
            DoRoll(id, tick, sliders);
        }
        catch
        {
            SendCommandResult(id, CommandStatus.NotSupported);
        }
        finally
        {
            Interlocked.Decrement(ref inFlight);
        }
    }

    // Resends the captured mission-request bytes through the exported raw Connection_t::Send, which
    // takes its own send lock, so this is safe from any thread and never touches the client's UI.
    private static void DoRoll(uint id, byte tick, ReadOnlySpan<byte> sliders)
    {
        nint connection;
        uint messageId;
        byte[] bytes;
        lock (RequestLock)
        {
            if (!haveSendRecording || sendBytes.Length == 0)
            {
                SendCommandResult(id, CommandStatus.NothingRecorded);
                return;
            }

            connection = sendConnection;
            messageId = sendMessageId;

            // Send stamps a sequence number into the buffer in place, so resend from a fresh copy to
            // keep the capture pristine (and every roll deterministic).
            bytes = (byte[])sendBytes.Clone();
        }

        if (connection == 0 || sendRawStub == 0)
        {
            SendCommandResult(id, CommandStatus.NotSupported);
            return;
        }

        // Roll at the requested difficulty tick, else at whatever the capture held.
        if (tick is >= 1 and <= MaxDifficultyTick && bytes.Length > RequestDifficultyOffset)
        {
            bytes[RequestDifficultyOffset] = tick;
        }

        // Overwrite the six signed slider bytes when supplied (each already percent-encoded).
        if (sliders.Length == SliderCount && bytes.Length >= RequestSlidersOffset + SliderCount)
        {
            sliders.CopyTo(bytes.AsSpan(RequestSlidersOffset, SliderCount));
        }

        fixed (byte* p = bytes)
        {
            ((delegate* unmanaged[Cdecl]<nint, uint, uint, nint, int>)sendRawStub)(connection, messageId, (uint)bytes.Length, (nint)p);
        }

        SendMissionRequested(RequestSource.ClickSaver, recorded);
        SendCommandResult(id, CommandStatus.Done);
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
        BitConverter.TryWriteBytes(hello[16..], RollReady ? (uint)HookCapabilities.CanRequestMissions : 0u);
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
}
