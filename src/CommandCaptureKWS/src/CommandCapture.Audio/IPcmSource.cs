namespace CommandCapture.Audio;

public interface IPcmSource : IAsyncDisposable
{
    int SampleRate { get; }

    long OverrunCount { get; }

    event Action<float>? LevelChanged;

    Task StartAsync(CancellationToken cancellationToken);

    IAsyncEnumerable<float[]> ReadFramesAsync(CancellationToken cancellationToken);

    Task StopAsync();
}
