using System.IO.Pipes;
using ClickSaver2026.Core.Hook;

namespace ClickSaver2026.Tests;

public sealed class HookServerTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task DeliversHelloMessagesAndDisconnect()
    {
        string pipeName = "ClickSaver2026.Tests." + Guid.NewGuid().ToString("N");
        await using var server = new HookServer(pipeName);

        var connected = new TaskCompletionSource<HookHello>(TaskCreationOptions.RunContinuationsAsynchronously);
        var received = new TaskCompletionSource<HookMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var disconnected = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.ClientConnected += hello => connected.TrySetResult(hello);
        server.MessageReceived += message => received.TrySetResult(message);
        server.ClientDisconnected += (_, error) => disconnected.TrySetResult(error);
        server.Start();

        var time = new DateTime(2026, 9, 17, 8, 0, 0, DateTimeKind.Utc);
        await using (var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out))
        {
            await client.ConnectAsync(TestContext.Current.CancellationToken).WaitAsync(Timeout, TestContext.Current.CancellationToken);
            await client.WriteAsync(HookProtocol.EncodeHello(new HookHello(HookStatus.Hooked, 1234, 2, HookCapabilities.CanRequestMissions)), TestContext.Current.CancellationToken);
            await client.WriteAsync(HookProtocol.EncodeFrame(HookMessageKind.IncomingMessage, time, [0xDE, 0xAD]), TestContext.Current.CancellationToken);

            HookHello hello = await connected.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
            Assert.Equal(new HookHello(HookStatus.Hooked, 1234, 2, HookCapabilities.CanRequestMissions), hello);

            HookMessage message = await received.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
            Assert.Equal(1234, message.ProcessId);
            Assert.Equal(HookMessageKind.IncomingMessage, message.Kind);
            Assert.Equal(time, message.TimestampUtc);
            Assert.Equal(new byte[] { 0xDE, 0xAD }, message.Data);
        }

        Assert.Null(await disconnected.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken));
    }
}
