using System.Buffers.Binary;
using System.Text;

namespace ClickSaver2026.Core.Missions;

/// <summary>
/// Reads the message a mission terminal sends back when missions are requested, as the hook
/// receives it (packet header included). Big-endian throughout.
/// </summary>
/// <remarks>
/// Checked against four retail answers (captures/mp_200346_s2.csv, 20 missions). The offsets
/// after the reward items are the ones ClickSaver 2.x has used since the 16.3 client; they still
/// hold for every captured mission.
/// </remarks>
public static class MissionListParser
{
    public const uint MessageType = 0x5C436609;
    public const int QuestIdentityType = 0xDAC3;

    private const int PacketHeaderSize = 16;
    private const int FirstMission = 0x33;
    private const int QuestVersion = 15;

    // From the end of the reward items.
    private const int QualityOffset = 0x10;
    private const int TypeOffset = 0x2C;
    private const int PlayfieldOffset = 0xAC;
    private const int XOffset = 0xB8;
    private const int ZOffset = 0xC0;

    public static bool IsMissionList(ReadOnlySpan<byte> message) =>
        message.Length > FirstMission && BinaryPrimitives.ReadUInt32BigEndian(message[PacketHeaderSize..]) == MessageType;

    /// <summary>The missions, or null when this is not a mission list or cannot be read.</summary>
    public static MissionList? TryParse(ReadOnlySpan<byte> message)
    {
        if (!IsMissionList(message))
        {
            return null;
        }

        int difficulty = message[0x1E];
        int[] sliders = [.. message.Slice(0x1F, 6).ToArray().Select(b => (int)(sbyte)b)];
        int terminal = BinaryPrimitives.ReadInt32BigEndian(message[0x2E..]);
        int count = message[0x32];

        var missions = new List<Mission>(count);
        int at = FirstMission;
        for (int i = 0; i < count; i++)
        {
            at = FindMissionStart(message, at);
            if (at < 0 || TryParseMission(message, at, out int end) is not { } mission)
            {
                break;
            }

            missions.Add(mission);
            at = end;
        }

        return missions.Count == 0 ? null : new MissionList(difficulty, sliders, terminal, missions);
    }

    private static Mission? TryParseMission(ReadOnlySpan<byte> message, int start, out int end)
    {
        end = start;
        var reader = new Reader(message, start);
        try
        {
            reader.Skip(4); // identity type, checked by FindMissionStart
            int instance = reader.Int32();
            reader.Skip(16); // version, two unread ints, flags
            string shortDescription = reader.NullTerminatedString();
            string description = reader.String(reader.Int32());

            reader.Skip(8); // the terminal's identity
            reader.Skip(4); // reward descriptor version
            int cash = reader.Int32();
            reader.Skip(4);
            int experience = reader.Int32();
            reader.Skip(8 * reader.Count3F1());
            reader.Skip(8 * reader.Count3F1());

            int rewardCount = reader.Count3F1();
            var rewards = new List<MissionRewardItem>(rewardCount);
            for (int i = 0; i < rewardCount; i++)
            {
                int low = reader.Int32();
                int high = reader.Int32();
                int quality = reader.Int32();
                reader.Skip(4);
                rewards.Add(new MissionRewardItem(low, high, quality));
            }

            int items = reader.Position;
            reader.Require(items + ZOffset + 4);
            end = items + ZOffset + 4;
            return new Mission(
                instance,
                shortDescription,
                description,
                cash,
                experience,
                rewards,
                BinaryPrimitives.ReadInt32BigEndian(message[(items + QualityOffset)..]),
                BinaryPrimitives.ReadInt32BigEndian(message[(items + TypeOffset)..]),
                BinaryPrimitives.ReadInt32BigEndian(message[(items + PlayfieldOffset)..]),
                BinaryPrimitives.ReadSingleBigEndian(message[(items + XOffset)..]),
                BinaryPrimitives.ReadSingleBigEndian(message[(items + ZOffset)..]));
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    // Each mission starts with its Identity (type 0xDAC3) and version 15. What sits between the
    // coordinates and the next mission varies with the mission, so the next start is found.
    private static int FindMissionStart(ReadOnlySpan<byte> message, int from)
    {
        ReadOnlySpan<byte> marker = [0x00, 0x00, 0xDA, 0xC3];
        for (int at = from; at + 12 <= message.Length; at++)
        {
            int found = message[at..].IndexOf(marker);
            if (found < 0)
            {
                return -1;
            }

            at += found;
            if (at + 12 <= message.Length && BinaryPrimitives.ReadInt32BigEndian(message[(at + 8)..]) == QuestVersion)
            {
                return at;
            }
        }

        return -1;
    }

    private ref struct Reader(ReadOnlySpan<byte> data, int position)
    {
        private readonly ReadOnlySpan<byte> data = data;

        public int Position { get; private set; } = position;

        public void Require(int end)
        {
            if (end > this.data.Length || end < this.Position)
            {
                throw new InvalidDataException("Mission runs past the end of the message.");
            }
        }

        public void Skip(int count)
        {
            this.Require(this.Position + count);
            this.Position += count;
        }

        public int Int32()
        {
            this.Require(this.Position + 4);
            int value = BinaryPrimitives.ReadInt32BigEndian(this.data[this.Position..]);
            this.Position += 4;
            return value;
        }

        // Counts in AO messages are stored as (count + 1) * 1009.
        public int Count3F1()
        {
            int encoded = this.Int32();
            if (encoded < 1009 || encoded % 1009 != 0 || encoded / 1009 > 1000)
            {
                throw new InvalidDataException($"Bad count {encoded}.");
            }

            return (encoded / 1009) - 1;
        }

        public string String(int length)
        {
            if (length < 0)
            {
                throw new InvalidDataException("Negative string length.");
            }

            this.Require(this.Position + length);
            string text = Encoding.Latin1.GetString(this.data.Slice(this.Position, length)).TrimEnd('\0');
            this.Position += length;
            return text;
        }

        public string NullTerminatedString()
        {
            int length = this.data[this.Position..].IndexOf((byte)0);
            if (length < 0)
            {
                throw new InvalidDataException("Unterminated string.");
            }

            string text = Encoding.Latin1.GetString(this.data.Slice(this.Position, length));
            this.Position += length + 1;
            return text;
        }
    }
}
