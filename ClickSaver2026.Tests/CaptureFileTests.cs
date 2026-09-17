using ClickSaver2026.Core.Capture;
using ClickSaver2026.Core.Hook;

namespace ClickSaver2026.Tests;

public sealed class CaptureFileTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "ClickSaver2026.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(this.folder))
        {
            Directory.Delete(this.folder, recursive: true);
        }
    }

    [Fact]
    public void MessagesRoundTrip()
    {
        string path = Path.Combine(this.folder, "a" + CaptureFormat.Extension);
        HookMessage[] messages =
        [
            new(10, HookMessageKind.IncomingMessage, new DateTime(2026, 9, 17, 1, 2, 3, DateTimeKind.Utc), [1, 2, 3, 4]),
            new(11, HookMessageKind.Dropped, new DateTime(2026, 9, 17, 1, 2, 4, DateTimeKind.Utc), [5, 0, 0, 0]),
            new(10, HookMessageKind.IncomingMessage, new DateTime(2026, 9, 17, 1, 2, 5, DateTimeKind.Utc), []),
        ];

        using (var writer = new CaptureWriter(path))
        {
            foreach (HookMessage message in messages)
            {
                writer.Write(message);
            }

            Assert.Equal(3, writer.MessageCount);
        }

        List<HookMessage> read = [.. CaptureReader.Read(path)];

        Assert.Equal(messages.Length, read.Count);
        for (int i = 0; i < messages.Length; i++)
        {
            Assert.Equal(messages[i].ProcessId, read[i].ProcessId);
            Assert.Equal(messages[i].Kind, read[i].Kind);
            Assert.Equal(messages[i].TimestampUtc, read[i].TimestampUtc);
            Assert.Equal(messages[i].Data, read[i].Data);
        }
    }

    [Fact]
    public void TruncatedLastRecordEndsTheCapture()
    {
        string path = Path.Combine(this.folder, "b" + CaptureFormat.Extension);
        using (var writer = new CaptureWriter(path))
        {
            writer.Write(new HookMessage(1, HookMessageKind.IncomingMessage, DateTime.UtcNow, [1, 2, 3]));
            writer.Write(new HookMessage(1, HookMessageKind.IncomingMessage, DateTime.UtcNow, [4, 5, 6]));
        }

        using (var file = new FileStream(path, FileMode.Open))
        {
            file.SetLength(file.Length - 2);
        }

        HookMessage only = Assert.Single(CaptureReader.Read(path));
        Assert.Equal(new byte[] { 1, 2, 3 }, only.Data);
    }

    [Fact]
    public void OtherFilesAreRefused()
    {
        Directory.CreateDirectory(this.folder);
        string path = Path.Combine(this.folder, "not-a-capture.bin");
        File.WriteAllBytes(path, new byte[32]);

        Assert.Throws<InvalidDataException>(() => CaptureReader.Read(path).ToList());
    }

    [Fact]
    public void ExistingCaptureIsNotOverwritten()
    {
        string path = Path.Combine(this.folder, "c" + CaptureFormat.Extension);
        using (new CaptureWriter(path))
        {
        }

        Assert.Throws<IOException>(() => new CaptureWriter(path));
    }
}
