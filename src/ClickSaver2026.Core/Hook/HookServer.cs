using System.Collections.Concurrent;
using System.IO.Pipes;

namespace ClickSaver2026.Core.Hook;

/// <summary>
/// Hosts the named pipe the hooks connect to. One connection per hooked client; any number of
/// clients at once.
/// </summary>
/// <remarks>Events are raised on thread-pool threads.</remarks>
public sealed class HookServer : IAsyncDisposable
{
    private readonly string pipeName;
    private readonly CancellationTokenSource stop = new();
    private readonly ConcurrentDictionary<Task, byte> connections = new();
    private Task? acceptLoop;

    public HookServer(string pipeName = HookProtocol.PipeName)
    {
        this.pipeName = pipeName;
    }

    public event Action<HookHello>? ClientConnected;

    /// <summary>The exception is null when the hook disconnected normally.</summary>
    public event Action<HookHello, Exception?>? ClientDisconnected;

    public event Action<HookMessage>? MessageReceived;

    /// <summary>The pipe could not be created, typically because another instance is running.</summary>
    public event Action<Exception>? Faulted;

    public void Start()
    {
        this.acceptLoop ??= Task.Run(() => this.AcceptLoopAsync(this.stop.Token));
    }

    public async ValueTask DisposeAsync()
    {
        await this.stop.CancelAsync().ConfigureAwait(false);
        if (this.acceptLoop is not null)
        {
            await this.acceptLoop.ConfigureAwait(false);
        }

        await Task.WhenAll(this.connections.Keys).ConfigureAwait(false);
        this.stop.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            NamedPipeServerStream pipe;
            try
            {
                pipe = new NamedPipeServerStream(
                    this.pipeName,
                    PipeDirection.In,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                this.Faulted?.Invoke(e);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellation).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                continue;
            }

            try
            {
                await pipe.WaitForConnectionAsync(cancellation).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
                return;
            }

            Task connection = this.ServeAsync(pipe, cancellation);
            this.connections.TryAdd(connection, 0);
            _ = connection.ContinueWith(done => this.connections.TryRemove(done, out _), TaskScheduler.Default);
        }
    }

    private async Task ServeAsync(NamedPipeServerStream pipe, CancellationToken cancellation)
    {
        await using (pipe.ConfigureAwait(false))
        {
            HookHello? hello = null;
            Exception? error = null;
            try
            {
                var helloBytes = new byte[HookProtocol.HelloSize];
                await pipe.ReadExactlyAsync(helloBytes, cancellation).ConfigureAwait(false);
                hello = HookProtocol.ParseHello(helloBytes);
                this.ClientConnected?.Invoke(hello);

                var header = new byte[HookProtocol.FrameHeaderSize];
                while (true)
                {
                    await pipe.ReadExactlyAsync(header, cancellation).ConfigureAwait(false);
                    var (kind, length, timestamp) = HookProtocol.ParseFrameHeader(header);
                    var payload = new byte[length];
                    await pipe.ReadExactlyAsync(payload, cancellation).ConfigureAwait(false);
                    this.MessageReceived?.Invoke(new HookMessage(hello.ProcessId, kind, timestamp, payload));
                }
            }
            catch (EndOfStreamException)
            {
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e) when (e is IOException or InvalidDataException)
            {
                error = e;
            }

            if (hello is not null)
            {
                this.ClientDisconnected?.Invoke(hello, error);
            }
            else if (error is not null)
            {
                this.Faulted?.Invoke(error);
            }
        }
    }
}
