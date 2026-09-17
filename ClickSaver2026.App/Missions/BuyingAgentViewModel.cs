using System.Collections.Concurrent;
using System.Globalization;
using ClickSaver2026.App.Hook;
using ClickSaver2026.Core.Hook;
using ClickSaver2026.Core.Missions;

namespace ClickSaver2026.App.Missions;

/// <summary>
/// The buying agent: rolls missions through the hook until a reward item matches the watch, with
/// no mouse control. Each roll repeats the player's own last Request, so the terminal must have
/// been used by hand once first.
/// </summary>
public sealed class BuyingAgentViewModel : ObservableObject
{
    private static readonly TimeSpan RollResultTimeout = TimeSpan.FromSeconds(15);

    private readonly HookViewModel hook;
    private readonly MissionsViewModel missions;
    private readonly ConcurrentDictionary<uint, TaskCompletionSource<CommandStatus>> pendingRolls = new();

    private uint nextCommandId;
    private int targetProcessId;
    private BuyingAgent? agent;
    private CancellationTokenSource? cancel;
    private string itemWatch = string.Empty;
    private int maxRolls = 10;
    private int rollsDone;
    private bool running;
    private string status = "Roll a mission at the terminal by hand once, then the agent can repeat it.";

    public BuyingAgentViewModel(HookViewModel hook, MissionsViewModel missions)
    {
        this.hook = hook;
        this.missions = missions;
        this.hook.CommandResultReceived += this.OnCommandResult;

        this.StartCommand = new RelayCommand(this.Start, () => this.CanStart);
        this.StopCommand = new RelayCommand(this.Stop, () => this.Running);
    }

    public RelayCommand StartCommand { get; }

    public RelayCommand StopCommand { get; }

    public string ItemWatch
    {
        get => this.itemWatch;
        set => this.Set(ref this.itemWatch, value);
    }

    public int MaxRolls
    {
        get => this.maxRolls;
        set => this.Set(ref this.maxRolls, Math.Clamp(value, 1, 1000));
    }

    public int RollsDone
    {
        get => this.rollsDone;
        private set => this.Set(ref this.rollsDone, value);
    }

    public bool Running
    {
        get => this.running;
        private set
        {
            if (this.Set(ref this.running, value))
            {
                this.OnPropertyChanged(nameof(this.CanStart));
            }
        }
    }

    public string Status
    {
        get => this.status;
        private set => this.Set(ref this.status, value);
    }

    public bool CanStart =>
        !this.Running
        && this.hook.SelectedClient is { CanRoll: true, HasRecording: true }
        && !string.IsNullOrWhiteSpace(this.ItemWatch);

    /// <summary>Feed a mission list the client produced, so a running roll can check it.</summary>
    public void OnMissionList(int processId, MissionList list)
    {
        if (this.Running && processId == this.targetProcessId)
        {
            this.agent?.Submit(list);
        }
    }

    private async void Start()
    {
        if (this.hook.SelectedClient is not { CanRoll: true } client)
        {
            this.Status = "Select an attached client that can roll missions.";
            return;
        }

        if (!client.HasRecording)
        {
            this.Status = "Roll a mission at the terminal by hand once on this account first.";
            return;
        }

        int processId = client.ProcessId;

        var watch = WatchQuery.Parse(this.ItemWatch);
        if (watch.IsEmpty)
        {
            this.Status = "Enter one or more item names to watch for.";
            return;
        }

        this.targetProcessId = processId;
        this.cancel = new CancellationTokenSource();
        this.agent = new BuyingAgent(ct => this.RollAsync(processId, ct), list => this.missions.Matches(list, watch));
        this.Running = true;
        this.RollsDone = 0;
        this.Status = "Rolling...";

        try
        {
            BuyingAgentResult result = await this.agent.RunAsync(
                this.MaxRolls,
                new Progress<int>(n => { this.RollsDone = n; this.Status = string.Create(CultureInfo.CurrentCulture, $"Rolling {n} of {this.MaxRolls}..."); }),
                this.cancel.Token);
            this.Status = result.Message;
        }
        catch (OperationCanceledException)
        {
            this.Status = string.Create(CultureInfo.CurrentCulture, $"Stopped after {this.RollsDone} rolls.");
        }
        catch (Exception e)
        {
            this.Status = "Buying agent error: " + e.Message;
        }
        finally
        {
            this.Running = false;
            this.pendingRolls.Clear();
        }
    }

    private void Stop()
    {
        this.cancel?.Cancel();
        this.Status = "Stopping...";
    }

    private async Task<CommandStatus> RollAsync(int processId, CancellationToken cancellation)
    {
        uint id = Interlocked.Increment(ref this.nextCommandId);
        var result = new TaskCompletionSource<CommandStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        this.pendingRolls[id] = result;
        try
        {
            if (!await this.hook.SendRollAsync(processId, id, cancellation).ConfigureAwait(true))
            {
                return CommandStatus.NotSupported;
            }

            return await result.Task.WaitAsync(RollResultTimeout, cancellation).ConfigureAwait(true);
        }
        catch (TimeoutException)
        {
            // The hook did not answer; treat as a transient miss so the loop keeps trying.
            return CommandStatus.Busy;
        }
        finally
        {
            this.pendingRolls.TryRemove(id, out _);
        }
    }

    private void OnCommandResult(int processId, uint id, CommandStatus status)
    {
        if (this.pendingRolls.TryGetValue(id, out TaskCompletionSource<CommandStatus>? result))
        {
            result.TrySetResult(status);
        }
    }
}
