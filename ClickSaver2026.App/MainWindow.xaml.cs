using System.ComponentModel;
using System.Media;
using System.Windows;

namespace ClickSaver2026.App;

public partial class MainWindow : Window
{
    private bool disposed;

    public MainWindow(MainViewModel model)
    {
        this.Model = model;
        this.InitializeComponent();
        this.DataContext = model;
        model.BuyingAgent.MatchFound += this.OnMatchFound;
    }

    private void OnMatchFound(string message)
    {
        // Bring the window forward and alert, so the roll is not missed.
        if (this.WindowState == WindowState.Minimized)
        {
            this.WindowState = WindowState.Normal;
        }

        this.Activate();
        this.Topmost = true;
        this.Topmost = false;
        SystemSounds.Exclamation.Play();
        MessageBox.Show(this, message, "ClickSaver2026 - match found", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public MainViewModel Model { get; }

    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (!this.disposed)
        {
            this.disposed = true;
            await this.Model.DisposeAsync().ConfigureAwait(true);
        }
    }
}
