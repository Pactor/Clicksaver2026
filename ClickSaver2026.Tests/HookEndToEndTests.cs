using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using ClickSaver2026.Core.Hook;
using ClickSaver2026.Core.Missions;

namespace ClickSaver2026.Tests;

/// <summary>
/// Attaches the real hook to HookHost.exe, the stand-in client from ClickSaver2026.HookHarness, and
/// checks messages arrive and the host keeps working after detaching.
/// </summary>
public sealed class HookEndToEndTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task AttachReceiveAndDetach()
    {
        var (hook, host) = BuiltHarness();
        CancellationToken cancellation = TestContext.Current.CancellationToken;

        await using var session = await HostSession.StartAsync(host, messageFile: null, messagesWanted: 10, cancellation);
        HookInjector.Inject(session.Process.Id, hook);
        Assert.NotNull(HookInjector.FindLoadedHook(session.Process.Id));

        HookHello hello = await session.Connected.Task.WaitAsync(Timeout, cancellation);
        Assert.Equal(new HookHello(HookStatus.Hooked, session.Process.Id, 2, HookCapabilities.None), hello);

        await session.EnoughMessages.Task.WaitAsync(Timeout, cancellation);
        HookMessage[] received = [.. session.Messages];
        Assert.All(received, message =>
        {
            Assert.Equal(HookMessageKind.IncomingMessage, message.Kind);
            Assert.Equal(64, message.Data.Length);
            Assert.Equal("HookHost"u8.ToArray(), message.Data[..8]);
        });

        // HookHost numbers its calls; every one of them reached the app, in order.
        uint[] calls = [.. received.Select(message => BinaryPrimitives.ReadUInt32LittleEndian(message.Data.AsSpan(8)))];
        Assert.Equal(Enumerable.Range((int)calls[0], calls.Length).Select(call => (uint)call), calls);

        // Detaching removes the hooks; the DLL stays resident, so the host's calls reach
        // MessageProtocol directly again. It exits 2 if any call is ever missed.
        HookInjector.Eject(session.Process.Id, hook);
        await Task.Delay(TimeSpan.FromMilliseconds(300), cancellation);
        Assert.Equal(0, await session.StopAsync(cancellation));
    }

    [Fact]
    public async Task MissionListFromTheClientArrivesAndParses()
    {
        var (hook, host) = BuiltHarness();
        CancellationToken cancellation = TestContext.Current.CancellationToken;

        var mission = new Mission(
            0x55D0C9B7, "Short", "pick up the Radioactive Isotope Container, and destroy it", 5374, 992,
            [new MissionRewardItem(121762, 121763, 30)], 30, 0x2C49, 687, 760.7f, 1154.5f);
        byte[] message = MissionMessageBuilder.Build(6, [11, 70, 1, -35, 92, -2], mission, mission with { Instance = 0x55D0C9B8 });
        string file = Path.Combine(Path.GetTempPath(), "ClickSaver2026.Tests." + Guid.NewGuid().ToString("N") + ".bin");
        await File.WriteAllBytesAsync(file, message, cancellation);
        try
        {
            await using var session = await HostSession.StartAsync(host, file, messagesWanted: 1, cancellation);
            HookInjector.Inject(session.Process.Id, hook);

            await session.EnoughMessages.Task.WaitAsync(Timeout, cancellation);
            HookMessage received = session.Messages.First();
            Assert.Equal(message, received.Data);

            MissionList? list = MissionListParser.TryParse(received.Data);
            Assert.NotNull(list);
            Assert.Equal(2, list.Missions.Count);
            Assert.Equal(new MissionRewardItem(121762, 121763, 30), Assert.Single(list.Missions[0].Rewards));
            Assert.Equal("Radioactive Isotope Container", list.Missions[0].FindItem);

            HookInjector.Eject(session.Process.Id, hook);
            Assert.Equal(0, await session.StopAsync(cancellation));
        }
        finally
        {
            File.Delete(file);
        }
    }

    private static (string Hook, string Host) BuiltHarness()
    {
        string? hook = TestPaths.Hook;
        string? host = TestPaths.HookHost;
        Assert.SkipWhen(hook is null || host is null, "Build the hook and the harness first (build.ps1).");
        Assert.SkipWhen(Environment.Is64BitProcess, "Attaching needs a 32-bit test process.");
        return (hook!, host!);
    }

    private sealed class HostSession : IAsyncDisposable
    {
        private HostSession(HookServer server, Process process)
        {
            this.Server = server;
            this.Process = process;
        }

        public HookServer Server { get; }

        public Process Process { get; }

        public TaskCompletionSource<HookHello> Connected { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<Exception?> Disconnected { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource EnoughMessages { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ConcurrentQueue<HookMessage> Messages { get; } = new();

        public static async Task<HostSession> StartAsync(string host, string? messageFile, int messagesWanted, CancellationToken cancellation)
        {
            string pipeName = "ClickSaver2026.Tests." + Guid.NewGuid().ToString("N");
            var server = new HookServer(pipeName);

            var start = new ProcessStartInfo(host)
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            if (messageFile is not null)
            {
                start.ArgumentList.Add(messageFile);
            }

            start.Environment["CLICKSAVER2026_PIPE"] = pipeName;
            var session = new HostSession(server, Process.Start(start)!);
            server.ClientConnected += hello => session.Connected.TrySetResult(hello);
            server.ClientDisconnected += (_, error) => session.Disconnected.TrySetResult(error);
            server.MessageReceived += message =>
            {
                session.Messages.Enqueue(message);
                if (session.Messages.Count >= messagesWanted)
                {
                    session.EnoughMessages.TrySetResult();
                }
            };
            server.Start();

            string? ready = await session.Process.StandardOutput.ReadLineAsync(cancellation).AsTask().WaitAsync(Timeout, cancellation);
            Assert.StartsWith("ready", ready, StringComparison.Ordinal);
            return session;
        }

        public async Task<int> StopAsync(CancellationToken cancellation)
        {
            Assert.False(this.Process.HasExited);
            this.Process.StandardInput.Close();
            await this.Process.WaitForExitAsync(cancellation).WaitAsync(Timeout, cancellation);
            return this.Process.ExitCode;
        }

        public async ValueTask DisposeAsync()
        {
            if (!this.Process.HasExited)
            {
                this.Process.Kill();
            }

            this.Process.Dispose();
            await this.Server.DisposeAsync();
        }
    }
}
