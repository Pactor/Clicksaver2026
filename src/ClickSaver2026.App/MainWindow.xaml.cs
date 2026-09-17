using System.ComponentModel;
using System.Windows;
using ClickSaver2026.App.Hook;

namespace ClickSaver2026.App;

public partial class MainWindow : Window
{
    private readonly HookViewModel hook = new();

    public MainWindow()
    {
        this.InitializeComponent();
        this.DataContext = this.hook;
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        await this.hook.DisposeAsync().ConfigureAwait(true);
    }
}
