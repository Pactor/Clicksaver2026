using System.Buffers.Binary;
using System.IO.Compression;
using ClickSaver2026.Core.GameData;
using ClickSaver2026.Core.Missions;

namespace ClickSaver2026.Tests;

/// <summary>
/// The parser and the game database against a real terminal session and a real client install.
/// Neither is part of the repository: set CLICKSAVER2026_RETAIL_CSV to the stream CSV of a
/// session with mission rolls (server lines: 16 plain bytes, then one zlib stream) and AO_CLIENT
/// to an Anarchy Online folder.
/// </summary>
public sealed class RetailCaptureTests
{
    private static readonly string? CapturePath = Environment.GetEnvironmentVariable("CLICKSAVER2026_RETAIL_CSV");
    private static readonly string? ClientFolder = Environment.GetEnvironmentVariable("AO_CLIENT");

    [Fact]
    public void EveryRetailMissionListParses()
    {
        Assert.SkipUnless(File.Exists(CapturePath), "Set CLICKSAVER2026_RETAIL_CSV to a retail stream CSV.");

        List<byte[]> lists = [.. ServerMessages(CapturePath).Where(m => MissionListParser.IsMissionList(m))];
        Assert.NotEmpty(lists);

        foreach (byte[] message in lists)
        {
            Assert.True(LegacyMissionSignature.Matches(message));
            MissionList? list = MissionListParser.TryParse(message);
            Assert.NotNull(list);
            Assert.Equal(message[0x32], list.Missions.Count);
            Assert.All(list.Missions, mission =>
            {
                Assert.NotEqual(MissionType.Unknown, mission.Type);
                Assert.NotEmpty(mission.Rewards);
                Assert.InRange(mission.Quality, 1, 300);
                Assert.True(mission.Cash > 0 && mission.Experience > 0);
                Assert.InRange(mission.X, 1, 10000);
                Assert.InRange(mission.Z, 1, 10000);
            });
        }
    }

    [Fact]
    public void EveryRetailRewardAndPlayfieldResolves()
    {
        Assert.SkipUnless(File.Exists(CapturePath), "Set CLICKSAVER2026_RETAIL_CSV to a retail stream CSV.");
        Assert.SkipUnless(GameDatabase.IsClientFolder(ClientFolder), "Set AO_CLIENT to an Anarchy Online folder.");

        using GameDatabase database = GameDatabase.Open(ClientFolder!);
        var missions = ServerMessages(CapturePath)
            .Select(m => MissionListParser.TryParse(m))
            .OfType<MissionList>()
            .SelectMany(list => list.Missions)
            .ToList();
        Assert.NotEmpty(missions);

        foreach (Mission mission in missions)
        {
            Assert.False(string.IsNullOrWhiteSpace(database.GetPlayfieldName(mission.PlayfieldId)));
            foreach (MissionRewardItem reward in mission.Rewards)
            {
                ResolvedItem item = database.Resolve(reward);
                Assert.True(item.Found, $"Reward {reward.LowId}/{reward.HighId} did not resolve.");
                Assert.NotNull(database.GetIcon(item.IconId));
            }
        }
    }

    [Fact]
    public void KnownRetailItemResolves()
    {
        Assert.SkipUnless(GameDatabase.IsClientFolder(ClientFolder), "Set AO_CLIENT to an Anarchy Online folder.");

        using GameDatabase database = GameDatabase.Open(ClientFolder!);

        // From the first retail roll: a QL 30 reward between two templates of the same item.
        ResolvedItem item = database.Resolve(new MissionRewardItem(121762, 121763, 30));

        Assert.True(item.Found);
        Assert.Equal("Cast-Off Mini Axe", item.Name);
        byte[] icon = database.GetIcon(item.IconId)!;
        Assert.True(IconImage.IsPng(icon));
        Assert.Equal("Galway Shire", database.GetPlayfieldName(687));
    }

    private static IEnumerable<byte[]> ServerMessages(string path)
    {
        var server = File.ReadLines(path)
            .Select(line => line.Split(','))
            .Where(parts => parts.Length == 3 && parts[1] == "server")
            .Select(parts => Convert.FromHexString(parts[2]))
            .ToList();
        if (server.Count == 0)
        {
            yield break;
        }

        using var compressed = new MemoryStream();
        compressed.Write(server[0].AsSpan(Math.Min(16, server[0].Length)));
        foreach (byte[] chunk in server.Skip(1))
        {
            compressed.Write(chunk);
        }

        compressed.Position = 0;
        using var plain = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionMode.Decompress))
        {
            try
            {
                zlib.CopyTo(plain);
            }
            catch (InvalidDataException)
            {
                // A capture that stops mid-stream still has every whole message before that point.
            }
        }

        byte[] data = plain.ToArray();
        for (int at = 0; at + 16 <= data.Length;)
        {
            int length = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(at + 6));
            if (length < 16 || at + length > data.Length)
            {
                yield break;
            }

            yield return data.AsSpan(at, length).ToArray();
            at += length;
        }
    }
}
