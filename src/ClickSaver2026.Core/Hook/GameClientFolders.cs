using System.ComponentModel;
using System.Diagnostics;
using ClickSaver2026.Core.GameData;

namespace ClickSaver2026.Core.Hook;

public static class GameClientFolders
{
    /// <summary>The install folder of a running client, or null when none is running or it cannot be read.</summary>
    public static string? FromRunningClient()
    {
        foreach (GameClientWindow client in GameClients.Find())
        {
            try
            {
                using var process = Process.GetProcessById(client.ProcessId);
                string? folder = Path.GetDirectoryName(process.MainModule?.FileName);
                if (GameDatabase.IsClientFolder(folder))
                {
                    return folder;
                }
            }
            catch (Exception e) when (e is Win32Exception or InvalidOperationException or ArgumentException)
            {
                // Elevated or already gone; try the next one.
            }
        }

        return null;
    }
}
