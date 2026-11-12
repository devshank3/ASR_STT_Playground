using NAudio.Wave;

namespace CommandCapture.Audio;

public static class WaveBufferConverter
{
    public static float[] ToMonoFloat(byte[] buffer, int count, WaveFormat format)
    {
        var bytesPerSample = format.BitsPerSample / 8;
        if (bytesPerSample <= 0 || format.Channels <= 0)
        {
            throw new InvalidOperationException($"Unsupported capture format {format}.");
        }

        var frameSize = bytesPerSample * format.Channels;
        var frames = count / frameSize;
        var mono = new float[frames];
        for (var frame = 0; frame < frames; frame++)
        {
            double sum = 0;
            for (var channel = 0; channel < format.Channels; channel++)
            {
                var offset = (frame * format.Channels + channel) * bytesPerSample;
                sum += ReadSample(buffer, offset, format);
            }

            mono[frame] = (float)(sum / format.Channels);
        }

        return mono;
    }

    private static float ReadSample(byte[] buffer, int offset, WaveFormat format)
    {
        return format.Encoding switch
        {
            WaveFormatEncoding.IeeeFloat when format.BitsPerSample == 32 =>
                BitConverter.ToSingle(buffer, offset),
            WaveFormatEncoding.Pcm when format.BitsPerSample == 16 =>
                BitConverter.ToInt16(buffer, offset) / 32768f,
            WaveFormatEncoding.Pcm when format.BitsPerSample == 32 =>
                BitConverter.ToInt32(buffer, offset) / 2147483648f,
            _ => throw new InvalidOperationException(
                $"Unsupported capture format {format.Encoding} {format.BitsPerSample}-bit."),
        };
    }
}
