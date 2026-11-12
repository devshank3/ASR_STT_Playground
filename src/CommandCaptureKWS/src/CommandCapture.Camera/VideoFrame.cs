namespace CommandCapture.Camera;

public sealed class VideoFrame
{
    public VideoFrame(int width, int height, byte[] bgra)
    {
        if (bgra.Length < width * height * 4)
        {
            throw new ArgumentException("The frame buffer is smaller than width × height × 4.", nameof(bgra));
        }

        Width = width;
        Height = height;
        Bgra = bgra;
    }

    public int Width { get; }

    public int Height { get; }

    public byte[] Bgra { get; }
}
