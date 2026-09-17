using System.Collections.Concurrent;
using ClickSaver2026.Core.Missions;

namespace ClickSaver2026.Core.GameData;

/// <summary>A reward item resolved against the game database.</summary>
public sealed record ResolvedItem(MissionRewardItem Reward, string Name, int Value, int IconId, bool Found);

/// <summary>
/// Item names, values, icons and playfield names, read straight from a client install on
/// demand and cached. Safe to use from several threads.
/// </summary>
public sealed class GameDatabase : IDisposable
{
    private readonly ResourceDatabase database;
    private readonly ConcurrentDictionary<int, ItemRecord?> items = new();
    private readonly ConcurrentDictionary<int, byte[]?> icons = new();
    private readonly ConcurrentDictionary<int, string?> playfields = new();

    private GameDatabase(string clientFolder, ResourceDatabase database)
    {
        this.ClientFolder = clientFolder;
        this.database = database;
    }

    public string ClientFolder { get; }

    public int ItemCount => this.database.Count(ResourceTypes.Item);

    /// <summary>True when the folder is a client install: it has cd_image\data\db\ResourceDatabase.idx.</summary>
    public static bool IsClientFolder(string? folder) =>
        !string.IsNullOrWhiteSpace(folder) && File.Exists(Path.Combine(ResourceDatabase.FolderOf(folder), "ResourceDatabase.idx"));

    public static GameDatabase Open(string clientFolder) =>
        new(clientFolder, new ResourceDatabase(ResourceDatabase.FolderOf(clientFolder)));

    public ItemRecord? GetItem(int id) =>
        this.items.GetOrAdd(id, key => this.database.TryRead(ResourceTypes.Item, key) is { } record ? ItemRecordParser.TryParse(key, record) : null);

    /// <summary>A PNG with transparency, or null when there is no such icon.</summary>
    public byte[]? GetIcon(int iconId) =>
        iconId == 0 ? null : this.icons.GetOrAdd(iconId, key => this.database.TryRead(ResourceTypes.Icon, key) is { } record ? IconImage.ToTransparentPng(record) : null);

    public string? GetPlayfieldName(int id) =>
        this.playfields.GetOrAdd(id, key => this.database.TryRead(ResourceTypes.Playfield, key) is { } record ? PlayfieldName(record) : null);

    /// <summary>
    /// The name and icon of whichever template is nearer the reward's quality, and the value
    /// interpolated between the two, as ClickSaver did.
    /// </summary>
    public ResolvedItem Resolve(MissionRewardItem reward)
    {
        ItemRecord? low = this.GetItem(reward.LowId);
        ItemRecord? high = reward.HighId == reward.LowId || reward.HighId == 0 ? low : this.GetItem(reward.HighId);
        if (low is null || high is null)
        {
            return new ResolvedItem(reward, $"Unknown item {reward.LowId}/{reward.HighId}", 0, 0, false);
        }

        ItemRecord nearer = Math.Abs(reward.Quality - low.Quality) < Math.Abs(high.Quality - reward.Quality) ? low : high;
        int value = high.Quality == low.Quality
            ? low.Value
            : (int)(low.Value + ((long)(high.Value - low.Value) * (reward.Quality - low.Quality) / (high.Quality - low.Quality)));
        return new ResolvedItem(reward, nearer.Name, value, nearer.IconId, true);
    }

    /// <summary>Every item, parsed. Slow (seconds); run it off the UI thread.</summary>
    public IEnumerable<ItemRecord> EnumerateItems()
    {
        foreach (int id in this.database.Instances(ResourceTypes.Item))
        {
            if (this.GetItem(id) is { } item)
            {
                yield return item;
            }
        }
    }

    public void Dispose() => this.database.Dispose();

    // Playfield records: int32, int32 id, then the name as a zero-terminated string.
    private static string? PlayfieldName(byte[] record)
    {
        if (record.Length <= 8)
        {
            return null;
        }

        ReadOnlySpan<byte> text = record.AsSpan(8);
        int end = text.IndexOf((byte)0);
        return System.Text.Encoding.Latin1.GetString(end < 0 ? text : text[..end]);
    }
}
