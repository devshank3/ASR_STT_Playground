using SherpaOnnx;

namespace CommandCapture.Voice;

public sealed class SherpaKeywordListener : IDisposable
{
    private readonly KeywordSpotter _spotter;
    private readonly float[] _tail = new float[8_000];
    private OnlineStream? _live;

    public SherpaKeywordListener(KeywordModelPaths paths, string keywordsFile)
    {
        var config = new KeywordSpotterConfig();
        config.FeatConfig.SampleRate = 16_000;
        config.FeatConfig.FeatureDim = 80;
        config.ModelConfig.Transducer.Encoder = paths.Encoder;
        config.ModelConfig.Transducer.Decoder = paths.Decoder;
        config.ModelConfig.Transducer.Joiner = paths.Joiner;
        config.ModelConfig.Tokens = paths.Tokens;
        config.ModelConfig.Provider = "cpu";
        config.ModelConfig.NumThreads = 1;
        config.ModelConfig.Debug = 0;
        config.KeywordsFile = keywordsFile;
        _spotter = new KeywordSpotter(config);
    }

    public IReadOnlyList<string> DetectUtterance(float[] samples)
    {
        using var stream = _spotter.CreateStream();
        return Drain(stream, samples, finish: true);
    }

    public IReadOnlyList<string> AcceptLive(float[] samples)
    {
        _live ??= _spotter.CreateStream();
        return Drain(_live, samples, finish: false);
    }

    public void Dispose()
    {
        _live?.Dispose();
        _live = null;
        _spotter.Dispose();
    }

    private List<string> Drain(OnlineStream stream, float[] samples, bool finish)
    {
        if (samples.Length > 0)
        {
            stream.AcceptWaveform(16_000, samples);
        }

        if (finish)
        {
            stream.AcceptWaveform(16_000, _tail);
            stream.InputFinished();
        }

        var found = new List<string>();
        var steps = 0;
        while (_spotter.IsReady(stream))
        {
            _spotter.Decode(stream);
            var keyword = _spotter.GetResult(stream).Keyword;
            if (!string.IsNullOrEmpty(keyword))
            {
                _spotter.Reset(stream);
                found.Add(keyword);
            }

            if (++steps > 10_000)
            {
                break;
            }
        }

        return found;
    }
}
