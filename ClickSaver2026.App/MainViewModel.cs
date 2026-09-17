using System.Globalization;
using ClickSaver2026.App.Hook;
using ClickSaver2026.App.Missions;
using ClickSaver2026.Core.Hook;
using ClickSaver2026.Core.Settings;

namespace ClickSaver2026.App;

public sealed class MainViewModel : ObservableObject, IAsyncDisposable
{
    public MainViewModel(bool autoAttach = true)
    {
        this.Missions = new MissionsViewModel(AppSettings.Load());
        this.Hook = new HookViewModel(autoAttach);
        this.BuyingAgent = new BuyingAgentViewModel(this.Hook, this.Missions);

        // A roll arrived: replace that client's current roll (fire and forget) and feed the agent.
        this.Hook.MissionListReceived += (list, message) =>
        {
            GameClientRow row = this.Hook.GetOrAddClient(message.ProcessId, ClientLabel(message.ProcessId));
            row.CurrentRoll = this.Missions.BuildView(list, message.TimestampUtc, row.Label);
            this.BuyingAgent.OnMissionList(message.ProcessId, list);
        };

        // A request was made in the client, so there is now a roll for the agent to repeat.
        this.Hook.MissionRequestedReceived += (processId, _) =>
            this.Hook.GetOrAddClient(processId, ClientLabel(processId)).HasRecording = true;

        // A capture file's roll: show it under a pseudo "Capture" client.
        this.Missions.CaptureRollLoaded += (list, receivedUtc, label) =>
        {
            GameClientRow row = this.Hook.GetOrAddClient(0, "Capture");
            row.CurrentRoll = this.Missions.BuildView(list, receivedUtc, label);
            this.Hook.SelectedClient = row;
        };
    }

    public MissionsViewModel Missions { get; }

    public HookViewModel Hook { get; }

    public BuyingAgentViewModel BuyingAgent { get; }

    public async ValueTask DisposeAsync()
    {
        await this.Hook.DisposeAsync().ConfigureAwait(true);
        this.Missions.Dispose();
    }

    private static string ClientLabel(int processId) => "Client " + processId.ToString(CultureInfo.CurrentCulture);
}
