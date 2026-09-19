using System.Globalization;
using ClickSaver2026.Core.GameData;

namespace ClickSaver2026.Core.Missions;

/// <summary>A mission a watch selected from a roll, with a short description of why it matched.</summary>
public sealed record MissionMatch(Mission Mission, string Description);

/// <summary>
/// Matches a rolled mission list against an item watch and an area watch. A mission matches when it
/// satisfies every watch that is set: its reward item (or find item) matches the item watch, AND its
/// playfield matches the area watch. Empty watches are ignored, so the two combine ("this item, in
/// this area"), and at least one must be set to match anything.
/// </summary>
/// <remarks>
/// Lives in Core with no UI dependency, so the app and other tools (e.g. a bot) can share it. Reward
/// item names come from the <see cref="GameDatabase"/> when one is set.
/// </remarks>
public sealed class MissionMatcher(GameDatabase? database)
{
    public bool Matches(MissionList list, WatchList items, WatchList areas) =>
        this.FirstMatch(list, items, areas) is not null;

    /// <summary>The first mission in the list that satisfies both watches, or null if none does.</summary>
    public MissionMatch? FirstMatch(MissionList list, WatchList items, WatchList areas)
    {
        if (items.IsEmpty && areas.IsEmpty)
        {
            return null;
        }

        foreach (Mission mission in list.Missions)
        {
            string? item = items.IsEmpty ? null : this.MatchingItem(mission, items);
            if (!items.IsEmpty && item is null)
            {
                continue;
            }

            string area = this.AreaName(mission);
            if (!areas.IsEmpty && !areas.Matches(area))
            {
                continue;
            }

            return new MissionMatch(mission, Describe(item, areas.IsEmpty ? null : area));
        }

        return null;
    }

    /// <summary>The mission's playfield name, or a "Playfield N" fallback when no database is set.</summary>
    public string AreaName(Mission mission) =>
        database?.GetPlayfieldName(mission.PlayfieldId) ?? "Playfield " + mission.PlayfieldId.ToString(CultureInfo.InvariantCulture);

    // The first reward-item name (resolved from the database) or find-item the item watch matches.
    private string? MatchingItem(Mission mission, WatchList items)
    {
        if (database is { } db)
        {
            foreach (MissionRewardItem reward in mission.Rewards)
            {
                string name = db.Resolve(reward).Name;
                if (items.Matches(name))
                {
                    return name;
                }
            }
        }

        return mission.FindItem is { } find && items.Matches(find) ? find : null;
    }

    private static string Describe(string? item, string? area) => (item, area) switch
    {
        (not null, not null) => item + " in " + area,
        (not null, null) => item,
        (null, not null) => "a mission in " + area,
        _ => "a watched mission",
    };
}
