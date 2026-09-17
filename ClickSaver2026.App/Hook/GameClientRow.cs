using ClickSaver2026.Core.Hook;

namespace ClickSaver2026.App.Hook;

public sealed class GameClientRow(int processId) : ObservableObject
{
    private string title = string.Empty;
    private bool hookLoaded;
    private HookHello? connection;
    private string? error;
    private bool busy;

    public int ProcessId { get; } = processId;

    public string Title
    {
        get => this.title;
        set => this.Set(ref this.title, value);
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
