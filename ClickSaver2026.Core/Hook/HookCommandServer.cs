using System.Collections.Concurrent;
using System.IO.Pipes;

namespace ClickSaver2026.Core.Hook;

/// <summary>
/// Hosts the command pipe the hooks read from. Each hook connects, writes its uint32 process id,
/// then reads commands the app sends it (the app never reads more from that pipe). One connection
/// per hooked client, held open until the server stops or a send to it fails.
/// </summary>
public sealed class HookCommandServer : IAsyncDisposable
{
    private readonly string pipeName;
    private readonly CancellationTokenSource stop = new();
    private readonly ConcurrentDictionary<int, Connection> connections = new();
    private Task? acceptLoop;

    public HookCommandServer(string pipeName = HookProtocol.CommandPipeName)
    {
        this.pipeName = pipeName;
    }

    public event Action<int>? ClientConnected;

    public event Action<int>? ClientDisconnected;

    public void Start() => this.acceptLoop ??= Task.Run(() => this.AcceptLoopAsync(this.stop.Token));

    /// <summary>True when a hook for <paramref name="processId"/> is connected to the command pipe.</summary>
    public bool CanSend(int processId) => this.connections.ContainsKey(processId);

    /// <summary>Sends one command to the hook in <paramref name="processId"/>.</summary>
    /// <returns>True when it was written, false when no such hook is connected.</returns>
    public async Task<bool> SendAsync(int processId, uint kind, uint id, ReadOnlyMemory<byte> payload, CancellationToken cancellation = default)
    {
        if (!this.connections.TryGetValue(processId, out Connection? connection))
        {
            return false;
        }

        byte[] command = HookProtocol.EncodeCommand(kind, id, payload.Span);
        await connection.Gate.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            await connection.Stream.WriteAsync(command, cancellation).ConfigureAwait(false);
            await connection.Stream.FlushAsync(cancellation).ConfigureAwait(false);
            return true;
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
            this.Drop(processId, connection);
            return false;
        }
        finally
        {
            connection.Gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await this.stop.CancelAsync().ConfigureAwait(false);
        if (this.acceptLoop is not null)
        {
            try
            {
                await this.acceptLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        foreach (Connection connection in this.connections.Values)
        {
            connection.Dispose();
        }

        this.connections.Clear();
        this.stop.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            // A real output buffer so small commands do not block until the hook happens to read.
            var pipe = new NamedPipeServerStream(
                this.pipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous,
                inBufferSize: 4096,
                outBufferSize: 4096);

            try
            {
                await pipe.WaitForConnectionAsync(cancellation).ConfigureAwait(false);

                // The hook announces which process it is, then only reads commands.
                var idBytes = new byte[4];
                await pipe.ReadExactlyAsync(idBytes, cancellation).ConfigureAwait(false);
                int processId = BitConverter.ToInt32(idBytes);

                var connection = new Connection(pipe);
                pipe = null!; // owned by the connection now
                if (this.connections.TryGetValue(processId, out Connection? previous))
                {
                    previous.Dispose();
                }

                this.connections[processId] = connection;
                this.ClientConnected?.Invoke(processId);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception e) when (e is IOException or EndOfStreamException)
            {
                // This connection failed before it announced itself; accept the next one.
            }
            finally
            {
                pipe?.Dispose();
            }
        }
    }

    private void Drop(int processId, Connection connection)
    {
        if (this.connections.TryRemove(new KeyValuePair<int, Connection>(processId, connection)))
        {
            connection.Dispose();
            this.ClientDisconnected?.Invoke(processId);
        }
    }

    private sealed class Connection(NamedPipeServerStream stream) : IDisposable
    {
        public NamedPipeServerStream Stream { get; } = stream;

        public SemaphoreSlim Gate { get; } = new(1, 1);

        public void Dispose()
        {
            this.Stream.Dispose();
            this.Gate.Dispose();
        }
    }
}
