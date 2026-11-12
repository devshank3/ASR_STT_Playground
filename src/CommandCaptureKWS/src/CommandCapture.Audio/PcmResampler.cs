using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace CommandCapture.Audio;

public static class PcmResampler
{
    public static float[] Resample(ReadOnlySpan<float> mono, int sourceRate, int targetRate)
    {
        if (sourceRate <= 0 || targetRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceRate));
        }

        if (mono.Length == 0 || sourceRate == targetRate)
        {
            return mono.ToArray();
        }

        var source = new ArraySampleProvider(mono.ToArray(), sourceRate);
        var resampler = new WdlResamplingSampleProvider(source, targetRate);
        var output = new List<float>(mono.Length * targetRate / sourceRate + 64);
        var buffer = new float[1024];
        int read;
        while ((read = resampler.Read(buffer, 0, buffer.Length)) > 0)
        {
            output.AddRange(buffer.AsSpan(0, read));
        }

        return output.ToArray();
    }

    private sealed class ArraySampleProvider : ISampleProvider
    {
        private readonly float[] _samples;
        private int _position;

        public ArraySampleProvider(float[] samples, int sampleRate)
        {
            _samples = samples;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            var copied = Math.Min(count, _samples.Length - _position);
            if (copied <= 0)
            {
                return 0;
            }

            _samples.AsSpan(_position, copied).CopyTo(buffer.AsSpan(offset, copied));
            _position += copied;
            return copied;
        }
    }
}
