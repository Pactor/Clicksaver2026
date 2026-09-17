namespace ClickSaver2026.Core.Missions;

/// <summary>
/// The item a find or return mission is about is only named in its description; this pulls it
/// out by the sentences the mission generator writes around it.
/// </summary>
public static class MissionDescriptions
{
    // ClickSaver 2.x's table (mission.c), then wording seen in retail captures since.
    private static readonly (string Before, string After)[] ItemPhrases =
    [
        ("The enemy is in the process of creating a new prototype ", ". It is of utmost importance"),
        ("The enemy is currently making a new prototype ", ". It is of utmost importance"),
        ("We have reason to believe finding the ", " in "),
        ("In this case it is the ", " that has gone missing."),
        ("we have at last found a copy of the ", " in "),
        ("According to our sources, the ", " found in "),
        ("Last night, the ", " was stolen from a production facility"),
        ("Last night, one ", " was stolen from a production facility"),
        ("One of our ", " have been stolen from our "),
        ("One ", " has been stolen from our "),
        ("A hacker wiped the ", " from our database"),
        ("I am interested in obtaining a certain ", ". My contacts have"),
        ("I am interested in obtaining one ", ". My contacts have"),
        ("have developed a prototype ", ".  We would very,"),
        ("If we could steal the ", " from the enemy, we would"),
        ("you can find the entrance to the place where the ", " has been hidden."),
        ("you can find the entrance to where the ", " has been hidden."),
        ("Would you please find the ", " in "),
        ("you might be able to find the ", ". Please bring it back to us"),
        ("Oh yeah, the ", " is set to blow up in"),
        ("who or where the traitor is, before you collect the ", " from "),
        ("who or where he is, before you collect the ", " from "),
        ("you might be able to find one ", ". Bring it back to us"),
        ("we intercepted a message that a prototype ", " will be moved from"),
        ("It is the ", ", please retrieve it "),
        ("pick up the ", ", and destroy it"),
        ("to get me that ", "! You right-click the item"),
        ("you should be able to find the ", ".  Please bring it back"),
    ];

    /// <summary>The item the description names, or null.</summary>
    public static string? FindItem(string description)
    {
        foreach (var (before, after) in ItemPhrases)
        {
            int start = description.IndexOf(before, StringComparison.Ordinal);
            if (start < 0)
            {
                continue;
            }

            start += before.Length;
            int end = description.IndexOf(after, start, StringComparison.Ordinal);
            if (end > start)
            {
                return description[start..end];
            }
        }

        return null;
    }
}
