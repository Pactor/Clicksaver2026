using ClickSaver2026.App.Missions;
using ClickSaver2026.Core.Hook;

namespace ClickSaver2026.App.Hook;

public sealed class GameClientRow(int processId) : ObservableObject
{
    private string title = string.Empty;
    private bool hookLoaded;
    private HookHello? connection;
    private string? error;
    private bool busy;
    private MissionListView? currentRoll;
    private bool hasRecording;

    public int ProcessId { get; } = processId;

    /// <summary>The most recent mission roll from this client. Fire and forget: replaced each roll.</summary>
    public MissionListView? CurrentRoll
    {
        get => this.currentRoll;
        set
        {
            if (this.Set(ref this.currentRoll, value))
            {
                this.OnPropertyChanged(nameof(this.RollSummary));
            }
        }
    }

    /// <summary>True once the player has rolled here by hand, so the agent has a request to repeat.</summary>
    public bool HasRecording
    {
        get => this.hasRecording;
        set => this.Set(ref this.hasRecording, value);
    }

    /// <summary>True when this client's hook can roll missions and its command channel is open.</summary>
    public bool CanRoll => this.Connection?.CanRequestMissions ?? false;

    public string RollSummary => this.CurrentRoll?.Title ?? "No roll yet";

    /// <summary>A short account label for the client list.</summary>
    public string Label => string.IsNullOrWhiteSpace(this.Title) ? $"Client {this.ProcessId}" : this.Title;

    public string Title
    {
        get => this.title;
        set
        {
            if (this.Set(ref this.title, value))
            {
                this.OnPropertyChanged(nameof(this.Label));
            }
        }
    }

    public bool HookLoaded
    {
        get => this.hookLoaded;
        set
        {
            if (this.Set(ref this.hookLoaded, value))
            {
                this.OnPropertyChanged(nameof(this.State));
            }
        }
    }

    public HookHello? Connection
    {
        get => this.connection;
        set
        {
            if (this.Set(ref this.connection, value))
            {
                this.OnPropertyChanged(nameof(this.State));
                this.OnPropertyChanged(nameof(this.CanRoll));
            }
        }
    }

    /// <summary>The last attach or detach failure.</summary>
    public string? Error
    {
        get => this.error;
        set
        {
            if (this.Set(ref this.error, value))
            {
                this.OnPropertyChanged(nameof(this.State));
            }
        }
    }

    public bool Busy
    {
        get => this.busy;
        set
        {
            if (this.Set(ref this.busy, value))
            {
                this.OnPropertyChanged(nameof(this.State));
            }
        }
    }

    public string State =>
        this.Busy ? "Working..." :
        this.Error is not null ? this.Error :
        this.Connection is { } hello ? HookStatusText.Describe(hello.Status) + ", connected" :
        this.HookLoaded ? "Loaded, not connected" :
        "Not attached";
}
