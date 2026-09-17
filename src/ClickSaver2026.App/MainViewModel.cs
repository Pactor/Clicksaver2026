using System.Globalization;
using ClickSaver2026.App.Hook;
using ClickSaver2026.App.Missions;
using ClickSaver2026.Core.Settings;

namespace ClickSaver2026.App;

public sealed class MainViewModel : ObservableObject, IAsyncDisposable
{
    public MainViewModel(bool autoAttach = true)
    {
        this.Missions = new MissionsViewModel(AppSettings.Load());
        this.Hook = new HookViewModel(autoAttach);
        this.Hook.MissionListReceived += (list, message) =>
            this.Missions.Add(list, message.TimestampUtc, "process " + message.ProcessId.ToString(CultureInfo.CurrentCulture));
    }

    public MissionsViewModel Missions { get; }

    public HookViewModel Hook { get; }

    public async ValueTask DisposeAsync()
    {
        await this.Hook.DisposeAsync().ConfigureAwait(true);
        this.Missions.Dispose();
    }
}
