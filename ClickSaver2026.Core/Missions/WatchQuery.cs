namespace ClickSaver2026.Core.Missions;

/// <summary>
/// A ClickSaver-style watch search over a piece of text: space-separated terms that must all be
/// present, terms prefixed with '-' that must be absent, and "quoted phrases" matched whole. Case
/// insensitive. This is the item/location search syntax from ClickSaver 2.x.
/// </summary>
/// <remarks>
/// Examples (from ClickSaver's readme): <c>decus -gloves</c> matches every decus item except
/// gloves; <c>decus armor</c> matches "decus body armor" and "decus armor boots"; <c>"decus armor"</c>
/// matches "decus armor boots" but not "decus body armor".
/// </remarks>
public sealed class WatchQuery
{
    private readonly string[] includes;
    private readonly string[] excludes;

    private WatchQuery(string[] includes, string[] excludes)
    {
        this.includes = includes;
        this.excludes = excludes;
    }

    /// <summary>True when the query has no terms, so it can never usefully match.</summary>
    public bool IsEmpty => this.includes.Length == 0 && this.excludes.Length == 0;

    public static WatchQuery Parse(string? query)
    {
        var includes = new List<string>();
        var excludes = new List<string>();
        foreach ((string term, bool exclude) in Tokenize(query ?? string.Empty))
        {
            (exclude ? excludes : includes).Add(term);
        }

        return new WatchQuery([.. includes], [.. excludes]);
    }

    /// <summary>True when <paramref name="text"/> has every include term and no exclude term.</summary>
    public bool Matches(string? text)
    {
        if (this.IsEmpty)
        {
            return false;
        }

        string haystack = (text ?? string.Empty).ToLowerInvariant();
        foreach (string term in this.excludes)
        {
            if (haystack.Contains(term, StringComparison.Ordinal))
            {
                return false;
            }
        }

        foreach (string term in this.includes)
        {
            if (!haystack.Contains(term, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>True when any of the texts matches.</summary>
    public bool MatchesAny(IEnumerable<string> texts) => texts.Any(this.Matches);

    private static IEnumerable<(string Term, bool Exclude)> Tokenize(string query)
    {
        int i = 0;
        while (i < query.Length)
        {
            while (i < query.Length && query[i] == ' ')
            {
                i++;
            }

            if (i >= query.Length)
            {
                break;
            }

            bool exclude = query[i] == '-';
            if (exclude)
            {
                i++;
            }

            var term = new System.Text.StringBuilder();
            bool quoted = false;
            while (i < query.Length && (quoted || query[i] != ' '))
            {
                if (query[i] == '"')
                {
                    quoted = !quoted;
                }
                else
                {
                    term.Append(char.ToLowerInvariant(query[i]));
                }

                i++;
            }

            if (term.Length > 0)
            {
                yield return (term.ToString(), exclude);
            }
        }
    }
}
