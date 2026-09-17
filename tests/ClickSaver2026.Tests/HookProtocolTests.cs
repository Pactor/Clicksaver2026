using ClickSaver2026.Core.Hook;

namespace ClickSaver2026.Tests;

public sealed class HookProtocolTests
{
    [Fact]
    public void HelloRoundTrips()
    {
        var hello = new HookHello(HookStatus.ExportNotFound, 4242, 7);

        HookHello parsed = HookProtocol.ParseHello(HookProtocol.EncodeHello(hello));

        Assert.Equal(hello, parsed);
    }

    [Fact]
    public void HelloBytesMatchTheHooksStruct()
    {
        byte[] bytes = HookProtocol.EncodeHello(new HookHello(HookStatus.Hooked, 0x01020304, 1));

        // cs::Hello in protocol.h: magic, protocolVersion, status, processId, hookVersion, reserved.
        Assert.Equal(20, bytes.Length);
        Assert.Equal("CS26"u8.ToArray(), bytes[..4]);
        Assert.Equal(new byte[] { 1, 0, 0, 0, 4, 3, 2, 1, 1, 0, 0, 0, 0, 0, 0, 0 }, bytes[4..]);
    }

    [Fact]
    public void HelloFromSomethingElseIsRefused()
    {
        byte[] bytes = HookProtocol.EncodeHello(new HookHello(HookStatus.Hooked, 1, 1));
        bytes[0] = (byte)'X';

        Assert.Throws<InvalidDataException>(() => HookProtocol.ParseHello(bytes));
    }

    [Fact]
    public void HelloFromAnotherProtocolVersionIsRefused()
    {
        byte[] bytes = HookProtocol.EncodeHello(new HookHello(HookStatus.Hooked, 1, 1));
        bytes[4] = 2;

        Assert.Throws<InvalidDataException>(() => HookProtocol.ParseHello(bytes));
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
