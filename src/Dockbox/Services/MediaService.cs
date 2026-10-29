using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Media.Control;
using static Dockbox.Interop.NativeMethods;

namespace Dockbox.Services;

public sealed record MediaInfo(string Title, string Artist, bool IsPlaying, ImageSource? Thumbnail);

/// <summary>Música/vídeo tocando no Windows (Spotify, navegador, etc.) via controles de mídia do sistema.</summary>
public static class MediaService
{
    private static GlobalSystemMediaTransportControlsSessionManager? _manager;
    private static string? _lastThumbKey;
    private static ImageSource? _lastThumb;

    public static async Task<MediaInfo?> GetAsync()
    {
        try
        {
            _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var session = _manager.GetCurrentSession();
            if (session == null) return null;

            var props = await session.TryGetMediaPropertiesAsync();
            var playback = session.GetPlaybackInfo();
            bool playing = playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

            string key = $"{props.Title}|{props.Artist}";
            if (key != _lastThumbKey)
            {
                _lastThumbKey = key;
                _lastThumb = props.Thumbnail != null ? await LoadThumbAsync(props.Thumbnail) : null;
            }
            return new MediaInfo(props.Title ?? "", props.Artist ?? "", playing, _lastThumb);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<ImageSource?> LoadThumbAsync(Windows.Storage.Streams.IRandomAccessStreamReference reference)
    {
        try
        {
            using var ras = await reference.OpenReadAsync();
            using var stream = ras.AsStreamForRead();
            var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            ms.Position = 0;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 160;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    public static void PlayPause() => Key(VK_MEDIA_PLAY_PAUSE);
    public static void Next() => Key(VK_MEDIA_NEXT_TRACK);
    public static void Previous() => Key(VK_MEDIA_PREV_TRACK);

    private static void Key(byte vk)
    {
        keybd_event(vk, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
        keybd_event(vk, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
    }
}
