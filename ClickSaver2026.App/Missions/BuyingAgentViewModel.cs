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
    private string locationWatch = string.Empty;
    private int difficulty; // 0 = repeat the last hand-roll's difficulty; 1..11 = a fixed tick
    private bool customSliders;
    private readonly int[] sliderSteps = [2, 2, 2, 2, 2, 2]; // 0..4 = 0/25/50/75/100%; 2 = 50% default
    private int maxRolls = 10;
    private int rollsDone;
    private int replaysSeen;
    private int listsSeen;
    private CommandStatus lastStatus;
    private bool running;
    private string status = "Roll a mission at the terminal by hand once, then the agent can repeat it.";

    public BuyingAgentViewModel(HookViewModel hook, MissionsViewModel missions)
    {
        this.hook = hook;
        this.missions = missions;
        this.hook.CommandResultReceived += this.OnCommandResult;
        this.hook.MissionRequestedReceived += this.OnMissionRequested;

        this.StartCommand = new RelayCommand(this.Start, () => this.CanStart);
        this.StopCommand = new RelayCommand(this.Stop, () => this.Running);
    }

    /// <summary>Raised on the UI thread when a roll matches, so the app can alert the user and stop.</summary>
    public event Action<string>? MatchFound;

    public RelayCommand StartCommand { get; }

    public RelayCommand StopCommand { get; }

    public string ItemWatch
    {
        get => this.itemWatch;
        set => this.Set(ref this.itemWatch, value);
    }

    /// <summary>Areas (playfields) to watch for, one per line; combines with the item watch.</summary>
    public string LocationWatch
    {
        get => this.locationWatch;
        set => this.Set(ref this.locationWatch, value);
    }

    public int MaxRolls
    {
        get => this.maxRolls;
        set => this.Set(ref this.maxRolls, Math.Clamp(value, 1, 1000));
    }

    /// <summary>
    /// The difficulty tick every roll uses: 0 repeats the difficulty of your last hand-roll, 1..11
    /// force that slider tick. Bound to a combo whose first item (index 0) is "Repeat last roll".
    /// </summary>
    public int Difficulty
    {
        get => this.difficulty;
        set => this.Set(ref this.difficulty, Math.Clamp(value, 0, 11));
    }

    /// <summary>Combo options for each slider: 0% is the left pole, 100% the right pole.</summary>
    public static IReadOnlyList<string> SliderStepLabels { get; } = ["0%", "25%", "50%", "75%", "100%"];

    /// <summary>When on, every roll also sets the six sliders below; off leaves them as captured.</summary>
    public bool CustomSliders
    {
        get => this.customSliders;
        set => this.Set(ref this.customSliders, value);
    }

    // The six sliders as a 0..4 step (0/25/50/75/100%), bound to a combo's SelectedIndex each.
    public int GoodBad { get => this.sliderSteps[0]; set => this.SetSlider(0, value); }

    public int OrderChaos { get => this.sliderSteps[1]; set => this.SetSlider(1, value); }

    public int OpenHidden { get => this.sliderSteps[2]; set => this.SetSlider(2, value); }

    public int PhysicalMystical { get => this.sliderSteps[3]; set => this.SetSlider(3, value); }

    public int HeadStealth { get => this.sliderSteps[4]; set => this.SetSlider(4, value); }

    public int MoneyXp { get => this.sliderSteps[5]; set => this.SetSlider(5, value); }

    private void SetSlider(int index, int step, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        step = Math.Clamp(step, 0, 4);
        if (this.sliderSteps[index] != step)
        {
            this.sliderSteps[index] = step;
            this.OnPropertyChanged(name);
        }
    }

    // Empty = roll as captured; 1 byte = difficulty tick; 7 bytes = tick + the six slider bytes.
    private ReadOnlyMemory<byte> BuildRollPayload()
    {
        byte tick = (byte)this.Difficulty; // 0 = keep the captured difficulty
        if (!this.CustomSliders)
        {
            return tick is >= 1 and <= 11 ? new byte[] { tick } : ReadOnlyMemory<byte>.Empty;
        }

        var payload = new byte[1 + MissionSliders.Count];
        payload[0] = tick;
        for (int i = 0; i < MissionSliders.Count; i++)
        {
            payload[1 + i] = MissionSliders.Encode(this.sliderSteps[i] * 25);
        }

        return payload;
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
        && (!string.IsNullOrWhiteSpace(this.ItemWatch) || !string.IsNullOrWhiteSpace(this.LocationWatch));

    /// <summary>Feed a mission list the client produced, so a running roll can check it.</summary>
    public void OnMissionList(int processId, MissionList list)
    {
        if (this.Running && processId == this.targetProcessId)
        {
            this.listsSeen++;
            this.agent?.Submit(list);
            this.SetRunningStatus(this.RollsDone);
        }
    }

    // The hook reports a ClickSaver-sourced request only after it actually replayed the client call,
    // on the client's own thread. Counting these separates "the replay ran" from "the server rolled"
    // (a fresh mission list) - the two questions a stuck roll needs answered.
    private void OnMissionRequested(int processId, RequestSource source)
    {
        if (this.Running && processId == this.targetProcessId && source == RequestSource.ClickSaver)
        {
            this.replaysSeen++;
            this.SetRunningStatus(this.RollsDone);
        }
    }

    private void SetRunningStatus(int n) => this.Status = string.Create(
        CultureInfo.CurrentCulture,
        $"Rolling {n} of {this.MaxRolls}...  (replayed {this.replaysSeen}, new lists {this.listsSeen}, last {this.lastStatus})");

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

        var items = WatchList.Parse(this.ItemWatch);
        var areas = WatchList.Parse(this.LocationWatch);
        if (items.IsEmpty && areas.IsEmpty)
        {
            this.Status = "Enter item names and/or areas to watch for (one per line).";
            return;
        }

        MissionMatcher matcher = this.missions.CreateMatcher();
        this.targetProcessId = processId;
        this.cancel = new CancellationTokenSource();
        this.agent = new BuyingAgent(ct => this.RollAsync(processId, ct), list => matcher.Matches(list, items, areas));
        this.Running = true;
        this.RollsDone = 0;
        this.replaysSeen = 0;
        this.listsSeen = 0;
        this.lastStatus = default;
        this.Status = "Rolling...";

        BuyingAgentResult? matched = null;
        try
        {
            BuyingAgentResult result = await this.agent.RunAsync(
                this.MaxRolls,
                new Progress<int>(n => { this.RollsDone = n; this.SetRunningStatus(n); }),
                this.cancel.Token);
            this.Status = result.Message;
            if (result.Outcome == BuyingAgentOutcome.Matched)
            {
                matched = result;
            }
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

        if (matched is not null)
        {
            // Rolling has already stopped, so the matched mission is still on the terminal. Alert
            // the user to accept it, naming the actual item that matched (not the whole watch list),
            // before rolling again would replace it.
            MissionMatch? found = matched.Match is { } list ? matcher.FirstMatch(list, items, areas) : null;
            string what = found is null ? "a watched mission" : found.Description;
            this.MatchFound?.Invoke(string.Create(
                CultureInfo.CurrentCulture,
                $"Found {what} on roll {matched.Rolls}.\n\nRolling has stopped. Accept the mission at the terminal now - rolling again replaces it."));
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
            if (!await this.hook.SendRollAsync(processId, id, this.BuildRollPayload(), cancellation).ConfigureAwait(true))
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
            this.lastStatus = status;
            result.TrySetResult(status);
        }
    }
}
