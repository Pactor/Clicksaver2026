using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClickSaver2026.Core.GameData;

namespace ClickSaver2026.App.Missions;

/// <summary>Item icons as frozen WPF images, decoded once per icon.</summary>
public sealed class IconCache
{
    private readonly ConcurrentDictionary<int, ImageSource?> images = new();

    public ImageSource? Get(GameDatabase? database, int iconId) =>
        database is null || iconId == 0 ? null : this.images.GetOrAdd(iconId, id => Decode(database.GetIcon(id)));

    public void Clear() => this.images.Clear();

    private static ImageSource? Decode(byte[]? png)
    {
        if (png is null)
        {
            return null;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = new MemoryStream(png);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception e) when (e is NotSupportedException or FileFormatException or InvalidOperationException)
        {
            return null;
        }
    }
}
