using System.ComponentModel;
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
