namespace ClickSaver2026.Core.Hook;

public sealed record GameClientWindow(int ProcessId, nint Window, string Title);

/// <summary>Finds running Anarchy Online clients by their window class.</summary>
public static class GameClients
{
    public const string WindowClass = "Anarchy client";

    public static IReadOnlyList<GameClientWindow> Find()
    {
        var clients = new List<GameClientWindow>();
        nint window = 0;
        while ((window = NativeMethods.FindWindowExW(0, window, WindowClass, null)) != 0)
        {
            NativeMethods.GetWindowThreadProcessId(window, out uint processId);
            clients.Add(new GameClientWindow((int)processId, window, GetTitle(window)));
        }

        return clients;
    }

    private static unsafe string GetTitle(nint window)
    {
        int length = NativeMethods.GetWindowTextLengthW(window);
        if (length <= 0)
        {
            return string.Empty;
        }

        var buffer = new char[length + 1];
        fixed (char* text = buffer)
        {
            int copied = NativeMethods.GetWindowTextW(window, text, buffer.Length);
            return new string(buffer, 0, Math.Max(copied, 0));
        }
    }
}
