using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using ClickSaver2026.Core.Hook;

namespace ClickSaver2026.Tests;

/// <summary>
/// Attaches the real hook to HookHost.exe, the stand-in client from tests/HookHarness, and
/// checks messages arrive and the host keeps working after detaching.
/// </summary>
public sealed class HookEndToEndTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task AttachReceiveAndDetach()
    {
        string? hook = TestPaths.Built("hook", HookInjector.HookFileName);
        string? host = TestPaths.Built("harness", "HookHost.exe");
        Assert.SkipWhen(hook is null || host is null, "Build the hook and the harness first (build.ps1).");
        Assert.SkipWhen(Environment.Is64BitProcess, "Attaching needs a 32-bit test process.");
        CancellationToken cancellation = TestContext.Current.CancellationToken;

        string pipeName = "ClickSaver2026.Tests." + Guid.NewGuid().ToString("N");
        await using var server = new HookServer(pipeName);
        var connected = new TaskCompletionSource<HookHello>(TaskCreationOptions.RunContinuationsAsynchronously);
        var disconnected = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var enoughMessages = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var messages = new ConcurrentQueue<HookMessage>();
        server.ClientConnected += hello => connected.TrySetResult(hello);
        server.ClientDisconnected += (_, error) => disconnected.TrySetResult(error);
        server.MessageReceived += message =>
        {
            messages.Enqueue(message);
            if (messages.Count >= 10)
            {
                enoughMessages.TrySetResult();
            }
        };
        server.Start();

        var start = new ProcessStartInfo(host)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.Environment["CLICKSAVER2026_PIPE"] = pipeName;
        using Process process = Process.Start(start)!;
        try
        {
            string? ready = await process.StandardOutput.ReadLineAsync(cancellation).AsTask().WaitAsync(Timeout, cancellation);
            Assert.StartsWith("ready", ready, StringComparison.Ordinal);

            HookInjector.Inject(process.Id, hook);
            Assert.NotNull(HookInjector.FindLoadedHook(process.Id));

            HookHello hello = await connected.Task.WaitAsync(Timeout, cancellation);
            Assert.Equal(new HookHello(HookStatus.Hooked, process.Id, 1), hello);

            await enoughMessages.Task.WaitAsync(Timeout, cancellation);
            HookMessage[] received = [.. messages];
            Assert.All(received, message =>
            {
                Assert.Equal(HookMessageKind.IncomingMessage, message.Kind);
                Assert.Equal(64, message.Data.Length);
                Assert.Equal("HookHost"u8.ToArray(), message.Data[..8]);
            });

            // HookHost numbers its calls; every one of them reached the app, in order.
            uint[] calls = [.. received.Select(message => BinaryPrimitives.ReadUInt32LittleEndian(message.Data.AsSpan(8)))];
            Assert.Equal(Enumerable.Range((int)calls[0], calls.Length).Select(call => (uint)call), calls);

            HookInjector.Eject(process.Id, hook);
            Assert.Null(await disconnected.Task.WaitAsync(Timeout, cancellation));
            Assert.Null(HookInjector.FindLoadedHook(process.Id));

            // The host checks each call still reaches MessageProtocol.dll, and exits 2 if not.
            await Task.Delay(TimeSpan.FromMilliseconds(300), cancellation);
            Assert.False(process.HasExited);
            process.StandardInput.Close();
            await process.WaitForExitAsync(cancellation).WaitAsync(Timeout, cancellation);
            Assert.Equal(0, process.ExitCode);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill();
            }
        }
    }
}
