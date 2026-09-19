using System.Globalization;
using System.IO;
using System.Windows;
using ClickSaver2026.Core.Capture;
using ClickSaver2026.Core.GameData;
using ClickSaver2026.Core.Hook;
using ClickSaver2026.Core.Missions;
using ClickSaver2026.Core.Settings;
using Microsoft.Win32;

namespace ClickSaver2026.App.Missions;

/// <summary>
/// The Missions tab's game-data and matching. Rolls are fire and forget: each client keeps only its
/// current roll (on its <see cref="Hook.GameClientRow"/>), so nothing accumulates here.
/// </summary>
public sealed class MissionsViewModel : ObservableObject, IDisposable
{
    private readonly AppSettings settings;
    private readonly IconCache icons = new();
    private GameDatabase? database;
    private string databaseStatus = string.Empty;

    public MissionsViewModel(AppSettings settings)
    {
        this.settings = settings;
        this.BrowseCommand = new RelayCommand(this.Browse);
        this.DetectCommand = new RelayCommand(() => this.DetectClientFolder(userAsked: true));

        if (GameDatabase.IsClientFolder(settings.ClientFolder))
        {
            this.OpenDatabase(settings.ClientFolder!);
        }
        else
        {
            this.DetectClientFolder(userAsked: false);
        }
    }

    /// <summary>Raised when a capture file's roll is loaded, so the app can show it as a client's roll.</summary>
    public event Action<MissionList, DateTime, string>? CaptureRollLoaded;

    public RelayCommand BrowseCommand { get; }

    public RelayCommand DetectCommand { get; }

    public string ClientFolder => this.database?.ClientFolder ?? "Not set";

    /// <summary>The folder controls (Detect / Set folder) show only until the game data is found.</summary>
    public Visibility SetupVisibility => this.database is null ? Visibility.Visible : Visibility.Collapsed;

    public string DatabaseStatus
    {
        get => this.databaseStatus;
        private set => this.Set(ref this.databaseStatus, value);
    }

    /// <summary>Builds the view for one roll, resolving reward items against the game database.</summary>
    public MissionListView BuildView(MissionList list, DateTime receivedUtc, string source) =>
        new(list, receivedUtc, source, this.database, this.icons);

    /// <summary>A matcher over the current game database, for item + area watches (see Core).</summary>
    public MissionMatcher CreateMatcher() => new(this.database);

    /// <summary>Reads a capture file and raises <see cref="CaptureRollLoaded"/> for each roll in it.</summary>
    public int LoadCapture(string path)
    {
        int lists = 0;
        string label = Path.GetFileName(path);
        foreach (HookMessage message in CaptureReader.Read(path))
        {
            if (message.Kind == HookMessageKind.IncomingMessage && MissionListParser.TryParse(message.Data) is { } list)
            {
                this.CaptureRollLoaded?.Invoke(list, message.TimestampUtc, label);
                lists++;
            }
        }

        return lists;
    }

    public void Dispose() => this.database?.Dispose();

    private void Browse()
    {
        var dialog = new OpenFolderDialog { Title = "Choose the Anarchy Online folder (the one containing cd_image)" };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (!GameDatabase.IsClientFolder(dialog.FolderName))
        {
            this.DatabaseStatus = dialog.FolderName + " is not an Anarchy Online folder: it has no cd_image\\data\\db\\ResourceDatabase.idx.";
            return;
        }

        this.OpenDatabase(dialog.FolderName);
    }

    private void DetectClientFolder(bool userAsked)
    {
        string? folder = GameClientFolders.FromRunningClient();
        if (folder is not null)
        {
            this.OpenDatabase(folder);
        }
        else
        {
            this.DatabaseStatus = userAsked
                ? "No running client found. Start the game, or use Browse to pick the Anarchy Online folder."
                : "Set the Anarchy Online folder (Browse), or start the game and press Detect, to see item names and icons.";
        }
    }

    /// <summary>Uses a client folder for this session only, without saving it.</summary>
    public void UseClientFolder(string folder) => this.OpenDatabase(folder, save: false);

    private void OpenDatabase(string folder, bool save = true)
    {
        GameDatabase opened;
        try
        {
            opened = GameDatabase.Open(folder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            this.DatabaseStatus = "Could not open the game database in " + folder + ": " + e.Message;
            return;
        }

        this.database?.Dispose();
        this.database = opened;
        this.icons.Clear();
        if (save)
        {
            this.settings.ClientFolder = folder;
            try
            {
                this.settings.Save();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // The folder still works for this session.
            }
        }

        this.DatabaseStatus = string.Create(CultureInfo.CurrentCulture, $"{opened.ItemCount:N0} items available. New rolls will show item names and icons.");
        this.OnPropertyChanged(nameof(this.ClientFolder));
        this.OnPropertyChanged(nameof(this.SetupVisibility));
    }
}
