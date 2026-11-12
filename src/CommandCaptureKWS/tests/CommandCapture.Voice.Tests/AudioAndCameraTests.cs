using CommandCapture.Audio;
using CommandCapture.Camera;
using NAudio.Wave;

namespace CommandCapture.Voice.Tests;

public sealed class AudioAndCameraTests
{
    [Fact]
    public void Resampler_ConvertsOneSecondFrom48KTo16K()
    {
        var output = PcmResampler.Resample(new float[48_000], 48_000, 16_000);

        Assert.InRange(output.Length, 15_000, 16_500);
    }

    [Fact]
    public void Converter_AveragesStereoPcm16()
    {
        var format = new WaveFormat(16_000, 16, 2);
        var bytes = new byte[4];
        BitConverter.TryWriteBytes(bytes.AsSpan(0, 2), (short)16384);
        var mono = WaveBufferConverter.ToMonoFloat(bytes, bytes.Length, format);

        Assert.Equal(0.25f, mono[0], precision: 3);
    }

    [Fact]
    public async Task Preview_SavesADifferentPngOnEachCapture()
    {
        var directory = Path.Combine(Path.GetTempPath(), "command-capture-tests", Guid.NewGuid().ToString("N"));
        await using var camera = new PreviewCameraSession(directory);
        await camera.StartAsync(CancellationToken.None);

        var first = await camera.CaptureStillAsync(CancellationToken.None);
        await Task.Delay(180);
        var second = await camera.CaptureStillAsync(CancellationToken.None);

        Assert.NotEqual(await File.ReadAllBytesAsync(first), await File.ReadAllBytesAsync(second));
    }

    [Fact]
    public void TestPattern_PaintsDifferentNumbers()
    {
        var first = TestPattern.Create(640, 360, 1234, frame: 0);
        var second = TestPattern.Create(640, 360, 5678, frame: 0);

        Assert.False(first.Bgra.AsSpan().SequenceEqual(second.Bgra));
        Assert.Contains(first.Bgra, value => value == 255);
    }

    [Fact]
    public async Task Preview_DisposeDoesNotNeedTheCallerContext()
    {
        var directory = Path.Combine(Path.GetTempPath(), "command-capture-tests", Guid.NewGuid().ToString("N"));
        var camera = new PreviewCameraSession(directory);
        await camera.StartAsync(CancellationToken.None);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new IdleContext());
            try
            {
                camera.DisposeAsync().AsTask().GetAwaiter().GetResult();
                finished.SetResult();
            }
            catch (Exception exception)
            {
                finished.SetException(exception);
            }
        })
        {
            IsBackground = true,
        };
        thread.Start();

        var completed = await Task.WhenAny(finished.Task, Task.Delay(TimeSpan.FromSeconds(3)));
        Assert.Same(finished.Task, completed);
        await finished.Task;
    }

    private sealed class IdleContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
        }

        public override void Send(SendOrPostCallback d, object? state) =>
            throw new InvalidOperationException("Dispose waited on the caller context.");
    }
}
