using SkiaSharp;

namespace CommandCapture.Camera;

public static class StillImage
{
    public static string SavePng(VideoFrame frame, string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var info = new SKImageInfo(frame.Width, frame.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        using var surface = SKSurface.Create(info);
        var pixels = surface.PeekPixels();
        var destination = pixels.GetPixels();
        var rowBytes = pixels.RowBytes;
        var packed = frame.Width * 4;
        for (var y = 0; y < frame.Height; y++)
        {
            System.Runtime.InteropServices.Marshal.Copy(
                frame.Bgra,
                y * packed,
                destination + (y * rowBytes),
                packed);
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("PNG encoding failed.");
        using var stream = File.Create(path);
        data.SaveTo(stream);
        return path;
    }
}
