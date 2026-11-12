using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace CommandCapture.Audio;

public sealed class WasapiPcmSource : IPcmSource
{
    public const int OutputRate = 16_000;
    private const int RawCapacity = 64;
    private const int FrameCapacity = 32;

    private readonly Queue<byte[]> _raw = new();
    private readonly Queue<float[]> _frames = new();
    private readonly SemaphoreSlim _rawSignal = new(0);
    private readonly SemaphoreSlim _frameSignal = new(0);

    private WasapiCapture? _capture;
    private Task? _convert;
    private CancellationTokenSource? _convertCancellation;
    private long _overruns;
    private int _started;

    public int SampleRate => OutputRate;

    public long OverrunCount => Interlocked.Read(ref _overruns);

    public event Action<float>? LevelChanged;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _started, 1) == 1)
        {
            return Task.CompletedTask;
        }

        try
        {
            _capture = new WasapiCapture();
        }
        catch (Exception exception)
        {
            Interlocked.Exchange(ref _started, 0);
            throw new InvalidOperationException(
                "No microphone is available for WASAPI capture. " + exception.Message,
                exception);
        }

        var format = _capture.WaveFormat;
        _convertCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _convert = Task.Run(() => ConvertAsync(format, _convertCancellation.Token), CancellationToken.None);
        _capture.DataAvailable += OnDataAvailable;
        _capture.StartRecording();
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<float[]> ReadFramesAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (await _frameSignal.WaitAsync(Timeout.Infinite, cancellationToken).ConfigureAwait(false))
        {
            float[]? frame = null;
            lock (_frames)
            {
                if (_frames.Count > 0)
                {
                    frame = _frames.Dequeue();
                }
            }

            if (frame is not null)
            {
                yield return frame;
            }
        }
    }

    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref _started, 0) == 0)
        {
            return;
        }

        if (_capture is not null)
        {
            _capture.DataAvailable -= OnDataAvailable;
            _capture.StopRecording();
        }

        if (_convertCancellation is not null)
        {
            await _convertCancellation.CancelAsync().ConfigureAwait(false);
        }

        _rawSignal.Release();
        if (_convert is not null)
        {
            try
            {
                await _convert.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _frameSignal.Release();
        _capture?.Dispose();
        _capture = null;
        _convertCancellation?.Dispose();
        _convertCancellation = null;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _rawSignal.Dispose();
        _frameSignal.Dispose();
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0)
        {
            return;
        }

        var copy = new byte[e.BytesRecorded];
        Buffer.BlockCopy(e.Buffer, 0, copy, 0, e.BytesRecorded);
        lock (_raw)
        {
            if (_raw.Count >= RawCapacity)
            {
                _raw.Dequeue();
                Interlocked.Increment(ref _overruns);
            }

            _raw.Enqueue(copy);
        }

        _rawSignal.Release();
    }

    private async Task ConvertAsync(WaveFormat format, CancellationToken cancellationToken)
    {
        using var resampler = format.SampleRate == OutputRate
            ? null
            : new StreamingResampler(format.SampleRate, OutputRate);
        var pending = new List<float>(OutputRate / 5);

        try
        {
            while (await _rawSignal.WaitAsync(Timeout.Infinite, cancellationToken).ConfigureAwait(false))
            {
                while (TryTakeRaw(out var block))
                {
                    var mono = WaveBufferConverter.ToMonoFloat(block, block.Length, format);
                    var output = resampler is null ? mono : resampler.Read(mono);
                    if (output.Length == 0)
                    {
                        continue;
                    }

                    RaiseLevel(output);
                    pending.AddRange(output);
                    if (pending.Count >= OutputRate / 10)
                    {
                        Publish(pending);
                        pending.Clear();
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }

        if (pending.Count > 0)
        {
            Publish(pending);
        }
    }

    private bool TryTakeRaw(out byte[] block)
    {
        lock (_raw)
        {
            if (_raw.Count == 0)
            {
                block = [];
                return false;
            }

            block = _raw.Dequeue();
            return true;
        }
    }

    private void Publish(List<float> samples)
    {
        var block = samples.ToArray();
        lock (_frames)
        {
            if (_frames.Count >= FrameCapacity)
            {
                _frames.Dequeue();
                Interlocked.Increment(ref _overruns);
            }

            _frames.Enqueue(block);
        }

        _frameSignal.Release();
    }

    private void RaiseLevel(ReadOnlySpan<float> samples)
    {
        var peak = 0f;
        foreach (var sample in samples)
        {
            var absolute = Math.Abs(sample);
            if (absolute > peak)
            {
                peak = absolute;
            }
        }

        LevelChanged?.Invoke(Math.Clamp(peak, 0f, 1f));
    }

    private sealed class StreamingResampler : IDisposable
    {
        private readonly QueueSampleProvider _source;
        private readonly WdlResamplingSampleProvider _resampler;
        private readonly float[] _buffer = new float[4096];

        public StreamingResampler(int sourceRate, int targetRate)
        {
            _source = new QueueSampleProvider(sourceRate);
            _resampler = new WdlResamplingSampleProvider(_source, targetRate);
        }

        public float[] Read(float[] mono)
        {
            _source.Enqueue(mono);
            var output = new List<float>();
            int read;
            while ((read = _resampler.Read(_buffer, 0, _buffer.Length)) > 0)
            {
                output.AddRange(_buffer.AsSpan(0, read));
            }

            return output.ToArray();
        }

        public void Dispose()
        {
        }
    }

    private sealed class QueueSampleProvider : ISampleProvider
    {
        private readonly Queue<float> _samples = new();

        public QueueSampleProvider(int sampleRate)
        {
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1);
        }

        public WaveFormat WaveFormat { get; }

        public void Enqueue(ReadOnlySpan<float> samples)
        {
            foreach (var sample in samples)
            {
                _samples.Enqueue(sample);
            }
        }

        public int Read(float[] buffer, int offset, int count)
        {
            var copied = Math.Min(count, _samples.Count);
            for (var i = 0; i < copied; i++)
            {
                buffer[offset + i] = _samples.Dequeue();
            }

            return copied;
        }
    }
}
