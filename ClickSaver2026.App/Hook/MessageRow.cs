using System.Globalization;
using ClickSaver2026.Core;
using ClickSaver2026.Core.Hook;
using ClickSaver2026.Core.Missions;

namespace ClickSaver2026.App.Hook;

public sealed class MessageRow
{
    public MessageRow(HookMessage message)
    {
        this.Message = message;
        this.Time = message.TimestampUtc.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.CurrentCulture);
        this.LegacyMatch = message.Kind == HookMessageKind.IncomingMessage && LegacyMissionSignature.Matches(message.Data);
        this.Preview = HexDump.Preview(message.Data, 32);
    }

    public HookMessage Message { get; }

    public string Time { get; }

    public int ProcessId => this.Message.ProcessId;

    public HookMessageKind Kind => this.Message.Kind;

    public int Size => this.Message.Data.Length;

    public bool LegacyMatch { get; }

    public string Preview { get; }
}
