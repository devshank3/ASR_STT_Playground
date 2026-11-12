using System.Globalization;

namespace CommandCapture.Camera;

public sealed class PreviewCameraSession : CameraSession
{
    public PreviewCameraSession(string stillDirectory)
        : base(stillDirectory)
    {
    }

    protected override async Task RunAsync(CancellationToken cancellationToken)
    {
        var frame = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            Publish(TestPattern.Create(640, 360, Random.Shared.Next(0, 10_000), frame));
            frame++;
            try
            {
                await Task.Delay(33, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}

public static class TestPattern
{
    private static readonly string[][] Glyphs =
    [
        ["01110", "10001", "10011", "10101", "11001", "10001", "01110"],
        ["00100", "01100", "00100", "00100", "00100", "00100", "01110"],
        ["01110", "10001", "00001", "00010", "00100", "01000", "11111"],
        ["11110", "00001", "00001", "01110", "00001", "00001", "11110"],
        ["00010", "00110", "01010", "10010", "11111", "00010", "00010"],
        ["11111", "10000", "11110", "00001", "00001", "10001", "01110"],
        ["00110", "01000", "10000", "11110", "10001", "10001", "01110"],
        ["11111", "00001", "00010", "00100", "01000", "01000", "01000"],
        ["01110", "10001", "10001", "01110", "10001", "10001", "01110"],
        ["01110", "10001", "10001", "01111", "00001", "00010", "11100"],
    ];

    public static VideoFrame Create(int width, int height, int number, int frame)
    {
        var pixels = new byte[width * height * 4];
        var bar = width == 0 ? 0 : (frame * 8) % width;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = ((y * width) + x) * 4;
                var stripe = ((x / 40) + (y / 40)) % 2 == 0;
                pixels[index] = stripe ? (byte)32 : (byte)18;
                pixels[index + 1] = stripe ? (byte)48 : (byte)28;
                pixels[index + 2] = stripe ? (byte)72 : (byte)46;
                pixels[index + 3] = 255;
                if (Math.Abs(x - bar) < 8)
                {
                    pixels[index] = 230;
                    pixels[index + 1] = 230;
                    pixels[index + 2] = 230;
                }
            }
        }

        DrawNumber(pixels, width, height, number);
        return new VideoFrame(width, height, pixels);
    }

    private static void DrawNumber(byte[] pixels, int width, int height, int number)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var scale = Math.Clamp(Math.Min(width, height) / 30, 6, 14);
        var text = Math.Abs(number).ToString(CultureInfo.InvariantCulture);
        var glyphWidth = 5 * scale;
        var glyphHeight = 7 * scale;
        var gap = scale;
        var textWidth = (text.Length * glyphWidth) + ((text.Length - 1) * gap);
        var originX = (width - textWidth) / 2;
        var originY = (height - glyphHeight) / 2;
        var pad = scale;
        FillRect(
            pixels,
            width,
            height,
            originX - pad,
            originY - pad,
            textWidth + (pad * 2),
            glyphHeight + (pad * 2),
            0,
            0,
            0);

        for (var i = 0; i < text.Length; i++)
        {
            var digit = text[i] - '0';
            if (digit is < 0 or > 9)
            {
                continue;
            }

            var glyph = Glyphs[digit];
            var left = originX + (i * (glyphWidth + gap));
            for (var row = 0; row < glyph.Length; row++)
            {
                var bits = glyph[row];
                for (var col = 0; col < 5; col++)
                {
                    if (bits[col] != '1')
                    {
                        continue;
                    }

                    FillRect(
                        pixels,
                        width,
                        height,
                        left + (col * scale),
                        originY + (row * scale),
                        scale,
                        scale,
                        255,
                        255,
                        255);
                }
            }
        }
    }

    private static void FillRect(
        byte[] pixels,
        int width,
        int height,
        int x,
        int y,
        int rectWidth,
        int rectHeight,
        byte blue,
        byte green,
        byte red)
    {
        var x0 = Math.Max(0, x);
        var y0 = Math.Max(0, y);
        var x1 = Math.Min(width, x + rectWidth);
        var y1 = Math.Min(height, y + rectHeight);
        for (var py = y0; py < y1; py++)
        {
            var row = (py * width * 4) + (x0 * 4);
            for (var px = x0; px < x1; px++)
            {
                pixels[row] = blue;
                pixels[row + 1] = green;
                pixels[row + 2] = red;
                pixels[row + 3] = 255;
                row += 4;
            }
        }
    }
}
