using System.IO.Pipes;
using ClickSaver2026.Core.Hook;

namespace ClickSaver2026.Tests;

public sealed class HookCommandServerTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);


    [Fact]
    public async Task DeliversCommandsToTheConnectedHook()
    {
        string pipeName = "ClickSaver2026.Tests.cmd." + Guid.NewGuid().ToString("N");
        await using var server = new HookCommandServer(pipeName);
        var connected = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.ClientConnected += pid => connected.TrySetResult(pid);
        server.Start();
        CancellationToken ct = TestContext.Current.CancellationToken;

        // Stand in for the hook: connect, announce a process id, then read a command.
        await using var hook = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
        await hook.ConnectAsync(ct).WaitAsync(Timeout, ct);
        await hook.WriteAsync(BitConverter.GetBytes(4242), ct);
        await hook.FlushAsync(ct);

        Assert.Equal(4242, await connected.Task.WaitAsync(Timeout, ct));
        Assert.True(server.CanSend(4242));
        Assert.False(server.CanSend(1));

        Assert.True(await server.SendAsync(4242, HookProtocol.RequestMissionsCommand, 7, ReadOnlyMemory<byte>.Empty, ct));

        var header = new byte[HookProtocol.CommandHeaderSize];
        await hook.ReadExactlyAsync(header, ct).AsTask().WaitAsync(Timeout, ct);
        Assert.Equal(HookProtocol.RequestMissionsCommand, BitConverter.ToUInt32(header, 0));
        Assert.Equal(7u, BitConverter.ToUInt32(header, 4));
        Assert.Equal(0u, BitConverter.ToUInt32(header, 8));
    }

    [Fact]
    public async Task SendToAnUnknownClientReportsFalse()
    {
        await using var server = new HookCommandServer("ClickSaver2026.Tests.cmd." + Guid.NewGuid().ToString("N"));
        server.Start();

        Assert.False(await server.SendAsync(999, HookProtocol.RequestMissionsCommand, 1, ReadOnlyMemory<byte>.Empty, TestContext.Current.CancellationToken));
    }
}
