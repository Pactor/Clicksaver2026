using ClickSaver2026.Core.Hook;

namespace ClickSaver2026.Tests;

public sealed class HookProtocolTests
{
    [Fact]
    public void HelloRoundTrips()
    {
        var hello = new HookHello(HookStatus.ExportNotFound, 4242, 7, HookCapabilities.CanRequestMissions);

        HookHello parsed = HookProtocol.ParseHello(HookProtocol.EncodeHello(hello));

        Assert.Equal(hello, parsed);
        Assert.True(parsed.CanRequestMissions);
    }

    [Fact]
    public void HelloBytesMatchTheHooksStruct()
    {
        byte[] bytes = HookProtocol.EncodeHello(new HookHello(HookStatus.Hooked, 0x01020304, 2, HookCapabilities.CanRequestMissions));

        // Hello in Wire.cs: magic, protocolVersion, status, processId, hookVersion, capabilities.
        Assert.Equal(20, bytes.Length);
        Assert.Equal("CS26"u8.ToArray(), bytes[..4]);
        Assert.Equal(new byte[] { 2, 0, 0, 0, 4, 3, 2, 1, 2, 0, 0, 0, 1, 0, 0, 0 }, bytes[4..]);
    }

    [Fact]
    public void HelloFromSomethingElseIsRefused()
    {
        byte[] bytes = HookProtocol.EncodeHello(new HookHello(HookStatus.Hooked, 1, 2, HookCapabilities.None));
        bytes[0] = (byte)'X';

        Assert.Throws<InvalidDataException>(() => HookProtocol.ParseHello(bytes));
    }

    [Fact]
    public void HelloFromAnotherProtocolVersionIsRefused()
    {
        byte[] bytes = HookProtocol.EncodeHello(new HookHello(HookStatus.Hooked, 1, 2, HookCapabilities.None));
        bytes[4] = 99;

        Assert.Throws<InvalidDataException>(() => HookProtocol.ParseHello(bytes));
    }

    [Fact]
    public void MissionRequestedRoundTrips()
    {
        var info = new byte[HookProtocol.MissionGenerateInfoSize];
        info[0] = 11;
        info[^1] = 0x2A;
        byte[] payload = [.. BitConverter.GetBytes((uint)RequestSource.ClickSaver), .. info];

        var (source, parsed) = HookProtocol.ParseMissionRequested(payload);

        Assert.Equal(RequestSource.ClickSaver, source);
        Assert.Equal(info, parsed);
    }

    [Fact]
    public void CommandResultRoundTrips()
    {
        byte[] payload = [.. BitConverter.GetBytes(7u), .. BitConverter.GetBytes((uint)CommandStatus.Busy)];

        var (id, status) = HookProtocol.ParseCommandResult(payload);

        Assert.Equal(7u, id);
        Assert.Equal(CommandStatus.Busy, status);
    }

    [Fact]
    public void CommandEncodesHeaderAndPayload()
    {
        byte[] command = HookProtocol.EncodeCommand(HookProtocol.RequestMissionsCommand, 3, [9, 9]);

        Assert.Equal(HookProtocol.CommandHeaderSize + 2, command.Length);
        Assert.Equal(new byte[] { 1, 0, 0, 0, 3, 0, 0, 0, 2, 0, 0, 0, 9, 9 }, command);
    }

    [Fact]
    public void FrameHeaderRoundTrips()
    {
        var time = new DateTime(2026, 9, 17, 12, 34, 56, DateTimeKind.Utc);

        byte[] frame = HookProtocol.EncodeFrame(HookMessageKind.IncomingMessage, time, [1, 2, 3]);
        var (kind, length, timestamp) = HookProtocol.ParseFrameHeader(frame);

        Assert.Equal(HookMessageKind.IncomingMessage, kind);
        Assert.Equal(3, length);
        Assert.Equal(time, timestamp);
        Assert.Equal(new byte[] { 1, 2, 3 }, frame[HookProtocol.FrameHeaderSize..]);
    }

    [Fact]
    public void OversizedFrameIsRefused()
    {
        byte[] frame = HookProtocol.EncodeFrame(HookMessageKind.IncomingMessage, DateTime.UtcNow, []);
        BitConverter.TryWriteBytes(frame.AsSpan(4), HookProtocol.MaxMessageSize + 1);

        Assert.Throws<InvalidDataException>(() => HookProtocol.ParseFrameHeader(frame));
    }
}
