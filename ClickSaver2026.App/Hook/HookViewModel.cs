using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Threading;
using ClickSaver2026.Core;
using ClickSaver2026.Core.Capture;
using ClickSaver2026.Core.Hook;
using ClickSaver2026.Core.Missions;

namespace ClickSaver2026.App.Hook;

/// <summary>The Hook tab: attaching to clients, the live message list and capture files.</summary>
public sealed class HookViewModel : ObservableObject, IAsyncDisposable
{
    private const int MaxRows = 500;
    private const int MaxPendingRows = 5000;
    private const int MaxHexDumpBytes = 64 * 1024;

    private readonly HookServer server = new();
    private readonly HookCommandServer commandServer = new();
    private readonly ConcurrentQueue<HookMessage> pending = new();
    private readonly ConcurrentDictionary<int, HookHello> connections = new();
    private readonly Lock captureGate = new();
    private readonly DispatcherTimer drainTimer;
    private readonly DispatcherTimer scanTimer;
    private readonly string hookPath = Path.Combine(AppContext.BaseDirectory, HookInjector.HookFileName);

    private CaptureWriter? capture;
    private volatile bool captureLegacyOnly;
    private long totalMessages;
    private long legacyMatches;
    private long dropped;
    private bool autoAttach;
    private bool scanning;
    private string status = "Starting...";
    private GameClientRow? selectedClient;
    private MessageRow? selectedMessage;
    private string captureFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClickSaver2026", "Captures");

    public HookViewModel(bool autoAttach = true)
    {
        this.autoAttach = autoAttach;
        this.server.MessageReceived += this.OnMessage;
        this.server.ClientConnected += hello => this.connections[hello.ProcessId] = hello;
        this.server.ClientDisconnected += (hello, error) => this.connections.TryRemove(hello.ProcessId, out HookHello? _);
        this.server.Faulted += e => this.Post(() => this.Status = "Pipe error: " + e.Message);
        this.server.Start();
        this.commandServer.Start();

        this.AttachCommand = new RelayCommand(() => this.RunOnClient(this.SelectedClient, attach: true), () => this.SelectedClient is { Busy: false });
        this.DetachCommand = new RelayCommand(() => this.RunOnClient(this.SelectedClient, attach: false), () => this.SelectedClient is { Busy: false, HookLoaded: true });
        this.RefreshCommand = new RelayCommand(this.Scan);
        this.DetachAllCommand = new RelayCommand(this.DetachAll, () => this.Clients.Any(c => c is { HookLoaded: true, Busy: false, ProcessId: not 0 }));
        this.ReattachAllCommand = new RelayCommand(this.ReattachAll, () => this.Clients.All(c => !c.Busy));
        this.ClearCommand = new RelayCommand(this.Messages.Clear);
        this.OpenCaptureFolderCommand = new RelayCommand(this.OpenCaptureFolder);

        this.drainTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => this.Drain(), Dispatcher.CurrentDispatcher);
        this.scanTimer = new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Background, (_, _) => this.Scan(), Dispatcher.CurrentDispatcher);
        this.Scan();

        this.Status = File.Exists(this.hookPath)
            ? "Waiting for game clients."
            : $"{HookInjector.HookFileName} is missing next to ClickSaver2026.exe - build it with build.ps1.";
    }

    /// <summary>A mission terminal answered. Raised on the UI thread.</summary>
    public event Action<MissionList, HookMessage>? MissionListReceived;

    /// <summary>A roll finished in the hook (id, status), for the buying agent. UI thread.</summary>
    public event Action<int, uint, CommandStatus>? CommandResultReceived;

    /// <summary>A roll was requested in the client - by the player or the agent. UI thread.</summary>
    public event Action<int, RequestSource>? MissionRequestedReceived;

    public ObservableCollection<GameClientRow> Clients { get; } = [];

    public ObservableCollection<MessageRow> Messages { get; } = [];

    public RelayCommand AttachCommand { get; }

    public RelayCommand DetachCommand { get; }

    public RelayCommand RefreshCommand { get; }

    public RelayCommand DetachAllCommand { get; }

    public RelayCommand ReattachAllCommand { get; }

    public RelayCommand ClearCommand { get; }

    public RelayCommand OpenCaptureFolderCommand { get; }

    public string Status
    {
        get => this.status;
        private set => this.Set(ref this.status, value);
    }

    /// <summary>Attach to every client found, as ClickSaver always did.</summary>
    public bool AutoAttach
    {
        get => this.autoAttach;
        set
        {
            if (this.Set(ref this.autoAttach, value) && value)
            {
                this.Scan();
            }
        }
    }

    public GameClientRow? SelectedClient
    {
        get => this.selectedClient;
        set => this.Set(ref this.selectedClient, value);
    }

    public MessageRow? SelectedMessage
    {
        get => this.selectedMessage;
        set
        {
            if (this.Set(ref this.selectedMessage, value))
            {
                this.OnPropertyChanged(nameof(this.SelectedMessageDump));
            }
        }
    }

    public string SelectedMessageDump
    {
        get
        {
            if (this.SelectedMessage is not { } row)
            {
                return string.Empty;
            }

            byte[] data = row.Message.Data;
            IReadOnlyList<int> markers = row.Kind == HookMessageKind.IncomingMessage ? LegacyMissionSignature.FindMarkers(data) : [];
            string header = string.Create(
                CultureInfo.CurrentCulture,
                $"{row.Time}  pid {row.ProcessId}  {row.Kind}  {data.Length} bytes  legacy signature: {(row.LegacyMatch ? "yes" : "no")}  00 00 DA C3 at: {(markers.Count == 0 ? "none" : string.Join(", ", markers.Select(m => "0x" + m.ToString("X", CultureInfo.InvariantCulture))))}");
            return header + Environment.NewLine + Environment.NewLine + HexDump.Format(data, MaxHexDumpBytes);
        }
    }

    public string CaptureFolder
    {
        get => this.captureFolder;
        set => this.Set(ref this.captureFolder, value);
    }

    public bool CaptureEnabled
    {
        get
        {
            lock (this.captureGate)
            {
                return this.capture is not null;
            }
        }

        set
        {
            try
            {
                this.SetCapture(value);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                this.Status = "Could not start a capture: " + e.Message;
            }

            this.OnPropertyChanged();
            this.OnPropertyChanged(nameof(this.CaptureFile));
        }
    }

    /// <summary>Only write messages ClickSaver 2.x would have treated as mission lists.</summary>
    public bool CaptureLegacyOnly
    {
        get => this.captureLegacyOnly;
        set
        {
            this.captureLegacyOnly = value;
            this.OnPropertyChanged();
        }
    }

    public string CaptureFile
    {
        get
        {
            lock (this.captureGate)
            {
                return this.capture?.Path ?? "Not capturing";
            }
        }
    }

    public string Counters => string.Create(
        CultureInfo.CurrentCulture,
        $"Messages: {Interlocked.Read(ref this.totalMessages):N0}    Legacy mission signature: {Interlocked.Read(ref this.legacyMatches):N0}    Dropped by hook: {Interlocked.Read(ref this.dropped):N0}");

    /// <summary>Ask the hook in <paramref name="processId"/> to roll missions once.</summary>
    /// <returns>False when no command channel to that client is open.</returns>
    public Task<bool> SendRollAsync(int processId, uint id, CancellationToken cancellation) =>
        this.commandServer.SendAsync(processId, HookProtocol.RequestMissionsCommand, id, ReadOnlyMemory<byte>.Empty, cancellation);

    /// <summary>True when the hook in <paramref name="processId"/> can roll and its command channel is open.</summary>
    public bool CanRoll(int processId) =>
        this.commandServer.CanSend(processId) && this.connections.TryGetValue(processId, out HookHello? hello) && hello.CanRequestMissions;

    /// <summary>The client row for <paramref name="processId"/>, creating it if a roll arrives before a scan.</summary>
    public GameClientRow GetOrAddClient(int processId, string label)
    {
        GameClientRow? row = this.Clients.FirstOrDefault(r => r.ProcessId == processId);
        if (row is null)
        {
            row = new GameClientRow(processId) { Title = label };
            this.Clients.Add(row);
            this.SelectedClient ??= row;
        }

        return row;
    }

    public async ValueTask DisposeAsync()
    {
        this.drainTimer.Stop();
        this.scanTimer.Stop();

        // Detach cleanly from every client so we never leave a hooked, subclassed client with no app.
        foreach (GameClientRow row in this.Clients.Where(r => r.HookLoaded).ToList())
        {
            try
            {
                await Task.Run(() => HookInjector.Eject(row.ProcessId, this.hookPath)).ConfigureAwait(true);
            }
            catch (Exception e) when (e is Win32Exception or InvalidOperationException or ArgumentException or TimeoutException)
            {
                // The client may have exited; nothing to detach.
            }
        }

        await this.server.DisposeAsync().ConfigureAwait(true);
        await this.commandServer.DisposeAsync().ConfigureAwait(true);
        this.SetCapture(false);
    }

    // Pipe threads.
    private void OnMessage(HookMessage message)
    {
        if (message.Kind == HookMessageKind.Dropped && message.Data.Length >= 4)
        {
            Interlocked.Add(ref this.dropped, BinaryPrimitives.ReadUInt32LittleEndian(message.Data));
        }

        bool legacy = message.Kind == HookMessageKind.IncomingMessage && LegacyMissionSignature.Matches(message.Data);
        Interlocked.Increment(ref this.totalMessages);

        if (message.Kind == HookMessageKind.IncomingMessage && MissionListParser.TryParse(message.Data) is { } missions)
        {
            this.Post(() => this.MissionListReceived?.Invoke(missions, message));
        }

        switch (message.Kind)
        {
            case HookMessageKind.CommandResult when message.Data.Length >= 8:
            {
                var (id, status) = HookProtocol.ParseCommandResult(message.Data);
                this.Post(() => this.CommandResultReceived?.Invoke(message.ProcessId, id, status));
                break;
            }

            case HookMessageKind.MissionRequested when message.Data.Length >= 4:
            {
                var (source, _) = HookProtocol.ParseMissionRequested(message.Data);
                this.Post(() => this.MissionRequestedReceived?.Invoke(message.ProcessId, source));
                break;
            }
        }

        if (legacy)
        {
            Interlocked.Increment(ref this.legacyMatches);
        }

        lock (this.captureGate)
        {
            if (this.capture is not null && (legacy || !this.captureLegacyOnly))
            {
                try
                {
                    this.capture.Write(message);
                }
                catch (IOException e)
                {
                    this.capture.Dispose();
                    this.capture = null;
                    this.Post(() =>
                    {
                        this.Status = "Capture stopped: " + e.Message;
                        this.OnPropertyChanged(nameof(this.CaptureEnabled));
                        this.OnPropertyChanged(nameof(this.CaptureFile));
                    });
                }
            }
        }

        // The list is for looking at; under a flood it shows the start and the capture keeps the rest.
        if (this.pending.Count < MaxPendingRows)
        {
            this.pending.Enqueue(message);
        }
    }

    private void Drain()
    {
        while (this.pending.TryDequeue(out HookMessage? message))
        {
            this.Messages.Add(new MessageRow(message));
        }

        while (this.Messages.Count > MaxRows)
        {
            this.Messages.RemoveAt(0);
        }

        foreach (GameClientRow row in this.Clients)
        {
            row.Connection = this.connections.GetValueOrDefault(row.ProcessId);
            if (row.Connection is not null)
            {
                row.HookLoaded = true;
            }
        }

        this.OnPropertyChanged(nameof(this.Counters));
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }

    private async void Scan()
    {
        if (this.scanning)
        {
            return;
        }

        this.scanning = true;
        try
        {
            var found = await Task.Run(() => GameClients.Find()
                .Select(window => (window, loaded: IsHookLoaded(window.ProcessId)))
                .ToList()).ConfigureAwait(true);

            // Keep the pseudo "Capture" client (pid 0); only prune real clients that are gone.
            foreach (GameClientRow gone in this.Clients.Where(row => row.ProcessId != 0 && found.All(f => f.window.ProcessId != row.ProcessId)).ToList())
            {
                this.Clients.Remove(gone);
            }

            foreach (var (window, loaded) in found)
            {
                GameClientRow? row = this.Clients.FirstOrDefault(r => r.ProcessId == window.ProcessId);
                if (row is null)
                {
                    row = new GameClientRow(window.ProcessId);
                    this.Clients.Add(row);
                }

                row.Title = window.Title;
                if (loaded is { } isLoaded && !row.Busy)
                {
                    row.HookLoaded = isLoaded;
                }

                if (this.AutoAttach && loaded == false && row.Error is null && !row.Busy && File.Exists(this.hookPath))
                {
                    this.RunOnClient(row, attach: true);
                }
            }

            this.SelectedClient ??= this.Clients.FirstOrDefault();
            if (this.Clients.Count == 0 && File.Exists(this.hookPath))
            {
                this.Status = "No game client running.";
            }
        }
        finally
        {
            this.scanning = false;
        }
    }

    private static bool? IsHookLoaded(int processId)
    {
        try
        {
            using ProcessModule? module = HookInjector.FindLoadedHook(processId);
            return module is not null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException)
        {
            // No access (elevated client) or the client just exited.
            return null;
        }
    }

    private async void RunOnClient(GameClientRow? row, bool attach)
    {
        if (row is not null)
        {
            await this.RunOnClientAsync(row, attach).ConfigureAwait(true);
        }
    }

    private async Task RunOnClientAsync(GameClientRow row, bool attach)
    {
        if (row.Busy || row.ProcessId == 0)
        {
            return;
        }

        row.Busy = true;
        row.Error = null;
        try
        {
            await Task.Run(() =>
            {
                if (attach)
                {
                    HookInjector.Inject(row.ProcessId, this.hookPath);
                }
                else
                {
                    HookInjector.Eject(row.ProcessId, this.hookPath);
                }
            }).ConfigureAwait(true);

            row.HookLoaded = attach;
            this.Status = string.Create(CultureInfo.CurrentCulture, $"{(attach ? "Attached to" : "Detached from")} process {row.ProcessId}.");
        }
        catch (Exception e)
        {
            // Shown on the row; auto attach will not retry a client until the error is cleared by a manual attempt.
            row.Error = e.Message;
            this.Status = string.Create(CultureInfo.CurrentCulture, $"Process {row.ProcessId}: {e.Message}");
        }
        finally
        {
            row.Busy = false;
        }
    }

    private async void DetachAll()
    {
        foreach (GameClientRow row in this.Clients.Where(r => r.HookLoaded).ToList())
        {
            await this.RunOnClientAsync(row, attach: false).ConfigureAwait(true);
        }

        this.Status = "Detached from every client.";
    }

    // Detach then attach each client, to reset the hook without closing the app.
    private async void ReattachAll()
    {
        this.Scan();
        foreach (GameClientRow row in this.Clients.Where(r => r.ProcessId != 0).ToList())
        {
            if (row.HookLoaded)
            {
                await this.RunOnClientAsync(row, attach: false).ConfigureAwait(true);
            }

            await this.RunOnClientAsync(row, attach: true).ConfigureAwait(true);
        }

        this.Status = "Re-attached to every client.";
    }

    private void SetCapture(bool enabled)
    {
        lock (this.captureGate)
        {
            if (enabled == this.capture is not null)
            {
                return;
            }

            if (enabled)
            {
                string name = "capture-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + CaptureFormat.Extension;
                this.capture = new CaptureWriter(Path.Combine(this.CaptureFolder, name));
            }
            else
            {
                this.capture!.Dispose();
                this.capture = null;
            }
        }
    }

    private void OpenCaptureFolder()
    {
        Directory.CreateDirectory(this.CaptureFolder);
        Process.Start(new ProcessStartInfo(this.CaptureFolder) { UseShellExecute = true })?.Dispose();
    }

    private void Post(Action action) => this.drainTimer.Dispatcher.BeginInvoke(action);
}
