using System.Buffers.Binary;
using System.Text;
using ClickSaver2026.Core.Missions;

namespace ClickSaver2026.Tests;

/// <summary>
/// Builds mission list messages in the layout retail terminals send, for tests that must not
/// depend on a capture. The retail capture test checks the same parser against the real thing.
/// </summary>
internal static class MissionMessageBuilder
{
    public static byte[] Build(int difficulty, sbyte[] sliders, params Mission[] missions)
    {
        var message = new List<byte>();
        message.AddRange(new byte[] { 0xDF, 0xDF, 0x00, 0x0A, 0x00, 0x01, 0, 0 }); // length patched below
        Int32(message, 0x0DAD);
        Int32(message, 0x59C66753);
        UInt32(message, MissionListParser.MessageType);
        Int32(message, 0xC350);
        Int32(message, 0x59C66753);
        message.Add(0);                  // unknown
        message.Add(4);                  // version
        message.Add((byte)difficulty);
        message.AddRange(sliders.Select(s => (byte)s));
        Int32(message, 0x74309A1D);      // seed
        message.Add(1);                  // originator
        Int32(message, 0xDAC1);
        Int32(message, unchecked((int)0xC000028F));
        message.Add((byte)missions.Length);

        foreach (Mission mission in missions)
        {
            Int32(message, MissionListParser.QuestIdentityType);
            Int32(message, mission.Instance);
            Int32(message, 15);
            Int32(message, 0);
            Int32(message, 0);
            Int32(message, 0);
            message.AddRange(Encoding.Latin1.GetBytes(mission.ShortDescription));
            message.Add(0);
            byte[] description = Encoding.Latin1.GetBytes(mission.Description);
            Int32(message, description.Length);
            message.AddRange(description);

            Int32(message, 0xDAC1);
            Int32(message, unchecked((int)0xC000028F));
            Int32(message, 6);
            Int32(message, mission.Cash);
            Int32(message, 0);
            Int32(message, mission.Experience);
            Int32(message, 1009);
            Int32(message, 1009);
            Int32(message, (mission.Rewards.Count + 1) * 1009);
            foreach (MissionRewardItem reward in mission.Rewards)
            {
                Int32(message, reward.LowId);
                Int32(message, reward.HighId);
                Int32(message, reward.Quality);
                Int32(message, 0);
            }

            // The stretch after the items, with the fields the parser reads in place.
            var tail = new byte[0xC4 + 0x40];
            BinaryPrimitives.WriteInt32BigEndian(tail.AsSpan(0x10), mission.Quality);
            BinaryPrimitives.WriteInt32BigEndian(tail.AsSpan(0x2C), mission.TypeCode);
            BinaryPrimitives.WriteInt32BigEndian(tail.AsSpan(0xAC), mission.PlayfieldId);
            BinaryPrimitives.WriteSingleBigEndian(tail.AsSpan(0xB8), mission.X);
            BinaryPrimitives.WriteSingleBigEndian(tail.AsSpan(0xBC), 51.5f);
            BinaryPrimitives.WriteSingleBigEndian(tail.AsSpan(0xC0), mission.Z);
            message.AddRange(tail);
        }

        byte[] bytes = [.. message];
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(6), (ushort)bytes.Length);
        return bytes;
    }

    private static void Int32(List<byte> bytes, int value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buffer, value);
        bytes.AddRange(buffer);
    }

    private static void UInt32(List<byte> bytes, uint value) => Int32(bytes, unchecked((int)value));
}
