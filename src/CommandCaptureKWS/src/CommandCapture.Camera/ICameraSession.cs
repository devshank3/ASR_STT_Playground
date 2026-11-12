namespace CommandCapture.Camera;

public interface ICameraSession : IAsyncDisposable
{
    event Action<VideoFrame>? FrameAvailable;

    Task StartAsync(CancellationToken cancellationToken);

    Task<string> CaptureStillAsync(CancellationToken cancellationToken);
}
