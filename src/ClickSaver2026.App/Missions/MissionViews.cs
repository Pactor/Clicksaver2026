using System.Globalization;
using System.Windows.Media;
using ClickSaver2026.Core.GameData;
using ClickSaver2026.Core.Missions;

namespace ClickSaver2026.App.Missions;

public sealed class RewardView(ResolvedItem item, ImageSource? icon)
{
    public ImageSource? Icon { get; } = icon;

    public string Name { get; } = item.Name;

    public string Details { get; } = string.Create(CultureInfo.CurrentCulture, $"QL {item.Reward.Quality}   Value {item.Value:N0}");

    public int Value { get; } = item.Value;
}

public sealed class MissionView
{
    public MissionView(int number, Mission mission, GameDatabase? database, IconCache icons)
    {
        this.Mission = mission;
        this.Title = string.Create(CultureInfo.CurrentCulture, $"{number}. {MissionTypes.Describe(mission.Type)}   QL {mission.Quality}");
        string playfield = database?.GetPlayfieldName(mission.PlayfieldId) ?? "Playfield " + mission.PlayfieldId.ToString(CultureInfo.CurrentCulture);
        this.Location = string.Create(CultureInfo.CurrentCulture, $"{playfield} ({mission.X:F1}, {mission.Z:F1})");
        this.Money = string.Create(CultureInfo.CurrentCulture, $"{mission.Cash:N0} credits   {mission.Experience:N0} XP");
        this.Rewards = [.. mission.Rewards.Select(reward =>
        {
            ResolvedItem item = database?.Resolve(reward)
                ?? new ResolvedItem(reward, string.Create(CultureInfo.CurrentCulture, $"Item {reward.LowId}/{reward.HighId} (set the AO folder for names)"), 0, 0, false);
            return new RewardView(item, icons.Get(database, item.IconId));
        })];
        this.FindItem = mission.FindItem is { } find ? "Find: " + find : null;
    }

    public Mission Mission { get; }

    public string Title { get; }

    public string Location { get; }

    public string Money { get; }

    public IReadOnlyList<RewardView> Rewards { get; }

    public string? FindItem { get; }

    public string Description => this.Mission.Description;
}

public sealed class MissionListView
{
    public MissionListView(MissionList list, DateTime receivedUtc, string source, GameDatabase? database, IconCache icons)
    {
        this.List = list;
        this.ReceivedUtc = receivedUtc;
        this.Source = source;
        this.Missions = [.. list.Missions.Select((mission, i) => new MissionView(i + 1, mission, database, icons))];

        string time = receivedUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture);
        string quality = list.Missions.Count == 0 ? string.Empty : "QL " + list.Missions.Max(m => m.Quality).ToString(CultureInfo.CurrentCulture);
        this.Title = string.Create(CultureInfo.CurrentCulture, $"{time}   {list.Missions.Count} missions   {quality}");
        this.Header = string.Create(
            CultureInfo.CurrentCulture,
            $"Rolled {receivedUtc.ToLocalTime():G} ({source})   Difficulty {list.Difficulty}   Sliders {string.Join(" ", list.Sliders)}");
    }

    public MissionList List { get; }

    public DateTime ReceivedUtc { get; }

    public string Source { get; }

    public string Title { get; }

    public string Header { get; }

    public IReadOnlyList<MissionView> Missions { get; }
}
