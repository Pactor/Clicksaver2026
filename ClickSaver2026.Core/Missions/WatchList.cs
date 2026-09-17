namespace ClickSaver2026.Core.Missions;

/// <summary>
/// A list of watch entries, one per line. A text matches if any line matches. Each line is a
/// <see cref="WatchQuery"/>, so a whole line is a substring to look for - partial names and exact
/// names both work - and a line may still use the search syntax (spaces, -exclude, "quotes").
/// </summary>
public sealed class WatchList
{
    private readonly WatchQuery[] entries;

    private WatchList(WatchQuery[] entries)
    {
        this.entries = entries;
    }

    public bool IsEmpty => this.entries.Length == 0;

    public static WatchList Parse(string? text)
    {
        WatchQuery[] entries = (text ?? string.Empty)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(WatchQuery.Parse)
            .Where(query => !query.IsEmpty)
            .ToArray();
        return new WatchList(entries);
    }

    /// <summary>True when any watch line matches the text.</summary>
    public bool Matches(string? text) => this.entries.Any(entry => entry.Matches(text));

    /// <summary>True when any watch line matches any of the texts.</summary>
    public bool MatchesAny(IEnumerable<string> texts)
    {
        var list = texts as ICollection<string> ?? texts.ToList();
        return this.entries.Any(entry => entry.MatchesAny(list));
    }
}
