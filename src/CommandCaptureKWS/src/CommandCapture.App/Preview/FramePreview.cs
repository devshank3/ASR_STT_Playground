using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommandCapture.App;
using CommandCapture.Camera;

namespace CommandCapture.App.Preview;

public sealed class FramePreview : IDisposable
{
    private readonly IUiMarshal _ui;
    private WriteableBitmap? _bitmap;
    private VideoFrame? _pending;
    private int _paintScheduled;
    private int _stopped;

    public FramePreview(IUiMarshal ui) => _ui = ui;

    public Bitmap? Image => _bitmap;

    public event Action<bool>? Painted;

    public void Submit(VideoFrame frame)
    {
        _pending = frame;
        if (Interlocked.CompareExchange(ref _paintScheduled, 1, 0) != 0)
        {
            return;
        }

        _ui.Post(Paint);
    }

    public void Stop() => Interlocked.Exchange(ref _stopped, 1);

    public void Dispose() => _bitmap?.Dispose();

    private void Paint()
    {
        if (Volatile.Read(ref _stopped) == 1)
        {
            Interlocked.Exchange(ref _paintScheduled, 0);
            return;
        }

        var frame = _pending;
        Interlocked.Exchange(ref _paintScheduled, 0);
        if (frame is null)
        {
            return;
        }

        var replaced = false;
        if (_bitmap is null || _bitmap.PixelSize.Width != frame.Width || _bitmap.PixelSize.Height != frame.Height)
        {
            _bitmap?.Dispose();
            _bitmap = new WriteableBitmap(
                new PixelSize(frame.Width, frame.Height),
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Unpremul);
            replaced = true;
        }

        using (var buffer = _bitmap.Lock())
        {
            var packed = frame.Width * 4;
            for (var y = 0; y < frame.Height; y++)
            {
                var bytes = Math.Min(packed, buffer.RowBytes);
                System.Runtime.InteropServices.Marshal.Copy(
                    frame.Bgra,
                    y * packed,
                    buffer.Address + (y * buffer.RowBytes),
                    bytes);
            }
        }

        Painted?.Invoke(replaced);
    }
}
