namespace CommandCapture.Camera;

public abstract class CameraSession : ICameraSession
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _cancellation = new();
    private VideoFrame? _latest;
    private int _stillCount;
    private Task? _loop;
    private int _started;
    private int _disposed;

    protected CameraSession(string stillDirectory)
    {
        StillDirectory = stillDirectory;
    }

    protected string StillDirectory { get; }

    public event Action<VideoFrame>? FrameAvailable;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _started, 1) == 1)
        {
            return;
        }

        Exception? loopError = null;
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnFirst(VideoFrame _)
        {
            FrameAvailable -= OnFirst;
            first.TrySetResult();
        }

        FrameAvailable += OnFirst;
        _loop = Task.Run(async () =>
        {
            try
            {
                await RunAsync(_cancellation.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                loopError = exception;
            }
        }, CancellationToken.None);

        var cancelled = false;
        try
        {
            await Task.WhenAny(first.Task, Task.Delay(TimeSpan.FromSeconds(4), cancellationToken));
            if (first.Task.IsCompleted)
            {
                return;
            }

            cancelled = cancellationToken.IsCancellationRequested;
        }
        finally
        {
            FrameAvailable -= OnFirst;
        }

        await DisposeAsync();
        if (cancelled)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        throw new InvalidOperationException(
            loopError?.Message ?? "The camera did not produce a frame.",
            loopError);
    }

    public Task<string> CaptureStillAsync(CancellationToken cancellationToken)
    {
        VideoFrame? frame;
        lock (_gate)
        {
            frame = _latest;
        }

        if (frame is null)
        {
            throw new InvalidOperationException("The preview has no frame yet.");
        }

        var count = Interlocked.Increment(ref _stillCount);
        var path = Path.Combine(
            StillDirectory,
            $"still-{DateTimeOffset.Now:yyyyMMdd-HHmmss-fff}-{count}.png");
        StillImage.SavePng(frame, path);
        return Task.FromResult(path);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        await _cancellation.CancelAsync().ConfigureAwait(false);
        if (_loop is not null)
        {
            try
            {
                await _loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _cancellation.Dispose();
    }

    protected void Publish(VideoFrame frame)
    {
        lock (_gate)
        {
            _latest = frame;
        }

        FrameAvailable?.Invoke(frame);
    }

    protected abstract Task RunAsync(CancellationToken cancellationToken);
}
