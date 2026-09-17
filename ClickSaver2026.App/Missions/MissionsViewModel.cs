using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using ClickSaver2026.Core.Capture;
using ClickSaver2026.Core.GameData;
using ClickSaver2026.Core.Hook;
using ClickSaver2026.Core.Missions;
using ClickSaver2026.Core.Settings;
using Microsoft.Win32;

namespace ClickSaver2026.App.Missions;

/// <summary>The Missions tab: every roll received, newest first, with the reward items resolved.</summary>
public sealed class MissionsViewModel : ObservableObject, IDisposable
{
    private const int MaxHistory = 100;

    private readonly AppSettings settings;
    private readonly IconCache icons = new();
    private GameDatabase? database;
    private MissionListView? selected;
    private string databaseStatus = string.Empty;

    public MissionsViewModel(AppSettings settings)
    {
        this.settings = settings;
        this.BrowseCommand = new RelayCommand(this.Browse);
        this.DetectCommand = new RelayCommand(() => this.DetectClientFolder(userAsked: true));
        this.OpenCaptureCommand = new RelayCommand(this.OpenCapture);

        if (GameDatabase.IsClientFolder(settings.ClientFolder))
        {
            this.OpenDatabase(settings.ClientFolder!);
        }
        else
        {
            this.DetectClientFolder(userAsked: false);
        }
    }

    public ObservableCollection<MissionListView> History { get; } = [];

    public RelayCommand BrowseCommand { get; }

    public RelayCommand DetectCommand { get; }

    public RelayCommand OpenCaptureCommand { get; }

    public string ClientFolder => this.database?.ClientFolder ?? "Not set";

    public string DatabaseStatus
    {
        get => this.databaseStatus;
        private set => this.Set(ref this.databaseStatus, value);
    }

    public MissionListView? Selected
    {
        get => this.selected;
        set => this.Set(ref this.selected, value);
    }

    public string Placeholder => this.History.Count == 0
        ? "No missions yet. Open a mission terminal in the game and request missions; they show up here."
        : string.Empty;

    /// <summary>Called on the UI thread for every mission list, live or from a capture.</summary>
    public void Add(MissionList list, DateTime receivedUtc, string source)
    {
        var view = new MissionListView(list, receivedUtc, source, this.database, this.icons);
        this.History.Insert(0, view);
        while (this.History.Count > MaxHistory)
        {
            this.History.RemoveAt(this.History.Count - 1);
        }

        this.Selected = view;
        this.OnPropertyChanged(nameof(this.Placeholder));
    }

    /// <summary>
    /// True when any mission in the list has a reward item name, or an item to find, that the
    /// watch matches. Reward names come from the game database when it is set.
    /// </summary>
    public bool Matches(MissionList list, WatchQuery watch)
    {
        if (watch.IsEmpty)
        {
            return false;
        }

        foreach (Mission mission in list.Missions)
        {
            foreach (MissionRewardItem reward in mission.Rewards)
            {
                if (this.database is { } db && watch.Matches(db.Resolve(reward).Name))
                {
                    return true;
                }
            }

            if (mission.FindItem is { } find && watch.Matches(find))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Shows every mission list in a capture file.</summary>
    public int LoadCapture(string path)
    {
        int lists = 0;
        foreach (HookMessage message in CaptureReader.Read(path))
        {
            if (message.Kind == HookMessageKind.IncomingMessage && MissionListParser.TryParse(message.Data) is { } list)
            {
                this.Add(list, message.TimestampUtc, Path.GetFileName(path));
                lists++;
            }
        }

        return lists;
    }

    public void Dispose() => this.database?.Dispose();

    private void OpenCapture()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open a ClickSaver2026 capture",
            Filter = $"Captures (*{CaptureFormat.Extension})|*{CaptureFormat.Extension}",
            InitialDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClickSaver2026", "Captures"),
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            int lists = this.LoadCapture(dialog.FileName);
            this.DatabaseStatus = string.Create(CultureInfo.CurrentCulture, $"{lists} mission lists in {Path.GetFileName(dialog.FileName)}.");
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            this.DatabaseStatus = "Could not read the capture: " + e.Message;
        }
    }

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

        this.DatabaseStatus = string.Create(CultureInfo.CurrentCulture, $"{opened.ItemCount:N0} items available.");
        this.OnPropertyChanged(nameof(this.ClientFolder));

        // Lists received before the folder was known get their names and icons now.
        var lists = this.History.Reverse().ToList();
        this.History.Clear();
        foreach (MissionListView list in lists)
        {
            this.Add(list.List, list.ReceivedUtc, list.Source);
        }
    }
}
