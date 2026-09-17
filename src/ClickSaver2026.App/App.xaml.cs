using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ClickSaver2026.App;

/// <remarks>
/// Command line, for checking the window without the game:
/// <c>--client-folder path</c> reads names and icons from that install without saving it,
/// <c>--open-capture file.cs26cap</c> shows the mission lists in a capture, and
/// <c>--screenshot file.png</c> saves the window to a PNG and exits. Neither attaches to a client.
/// </remarks>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        string? capture = Argument(e.Args, "--open-capture");
        string? screenshot = Argument(e.Args, "--screenshot");
        string? clientFolder = Argument(e.Args, "--client-folder");

        bool checking = capture is not null || screenshot is not null || clientFolder is not null;
        var window = new MainWindow(new MainViewModel(autoAttach: !checking));
        this.MainWindow = window;
        window.Show();

        if (clientFolder is not null)
        {
            window.Model.Missions.UseClientFolder(clientFolder);
        }

        if (capture is not null)
        {
            window.Model.Missions.LoadCapture(capture);
        }

        if (screenshot is not null)
        {
            // Let layout and rendering settle before capturing.
            window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
            {
                SaveScreenshot(window, screenshot);
                window.Close();
            });
        }
    }

    private static string? Argument(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static void SaveScreenshot(Window window, string path)
    {
        var content = (FrameworkElement)window.Content;
        var dpi = VisualTreeHelper.GetDpi(window);
        var bitmap = new RenderTargetBitmap(
            (int)(content.ActualWidth * dpi.DpiScaleX), (int)(content.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using FileStream file = File.Create(path);
        encoder.Save(file);
    }
}
