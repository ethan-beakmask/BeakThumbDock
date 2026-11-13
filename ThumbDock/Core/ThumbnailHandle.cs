using ThumbDock.Native;

namespace ThumbDock.Core;

/// <summary>One registered DWM thumbnail. Always released exactly once.</summary>
internal sealed class ThumbnailHandle : IDisposable
{
    private nint _id;

    private ThumbnailHandle(nint id, nint source)
    {
        _id = id;
        Source = source;
    }

    public nint Source { get; }

    /// <summary>Registers a thumbnail of <paramref name="source"/> drawn on <paramref name="destination"/>; null on failure.</summary>
    public static ThumbnailHandle? Register(nint destination, nint source)
    {
        if (!User32.IsWindow(source)) return null;
        nint id = Dwm.RegisterThumbnail(destination, source);
        return id == 0 ? null : new ThumbnailHandle(id, source);
    }

    public bool IsSourceAlive => _id != 0 && User32.IsWindow(Source);

    public Size? SourceSize => Dwm.QuerySourceSize(_id);

    public bool Update(Rectangle destination, bool visible) => Dwm.UpdateThumbnail(_id, destination, visible);

    public void Dispose()
    {
        Release();
        GC.SuppressFinalize(this);
    }

    ~ThumbnailHandle() => Release();

    private void Release()
    {
        nint id = Interlocked.Exchange(ref _id, 0);
        Dwm.UnregisterThumbnail(id);
    }
}
