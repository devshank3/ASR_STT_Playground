namespace WhisperNetPlay.Speech;

/// <summary>
/// Energy-based utterance splitter for 16 kHz mono float audio. A segment is committed after a
/// trailing pause (or a hard max length); interim hypotheses are requested on a timer while speech
/// is in progress.
/// </summary>
internal sealed class EnergyEndpointDetector
{
    public const int SampleRate = 16000;

    private const int SamplesPerMs = SampleRate / 1000;
    private const double SilenceRmsThreshold = 0.012;
    private const int EndpointSilenceMs = 700;
    private const int MinUtteranceMs = 350;
    private const int MaxUtteranceMs = 20_000;
    private const int InterimIntervalMs = 900;
    private const int MaxLeadingSilenceMs = 400;

    public enum Decision
    {
        WaitForSpeech,
        Hold,
        RequestInterim,
        Commit,
    }

    private readonly List<float> _utterance = new();
    private int _trailingSilenceMs;
    private int _msSinceInterim;
    private bool _hasSpeech;

    public Decision AddChunk(float[] chunk)
    {
        var chunkMs = chunk.Length / SamplesPerMs;
        var isSilent = Rms(chunk) < SilenceRmsThreshold;
        _utterance.AddRange(chunk);

        if (isSilent)
        {
            _trailingSilenceMs += chunkMs;
        }
        else
        {
            _hasSpeech = true;
            _trailingSilenceMs = 0;
        }

        _msSinceInterim += chunkMs;
        var utteranceMs = _utterance.Count / SamplesPerMs;

        if (!_hasSpeech)
        {
            if (utteranceMs > MaxLeadingSilenceMs)
            {
                var keep = MaxLeadingSilenceMs * SamplesPerMs;
                _utterance.RemoveRange(0, _utterance.Count - keep);
            }

            return Decision.WaitForSpeech;
        }

        var reachedEndpoint = _trailingSilenceMs >= EndpointSilenceMs || utteranceMs >= MaxUtteranceMs;
        if (reachedEndpoint && utteranceMs >= MinUtteranceMs)
            return Decision.Commit;

        if (_msSinceInterim >= InterimIntervalMs && utteranceMs >= MinUtteranceMs)
        {
            _msSinceInterim = 0;
            return Decision.RequestInterim;
        }

        return Decision.Hold;
    }

    public float[] CopyUtterance() => _utterance.ToArray();

    public void ResetAfterCommit()
    {
        _utterance.Clear();
        _hasSpeech = false;
        _trailingSilenceMs = 0;
        _msSinceInterim = 0;
    }

    private static double Rms(float[] samples)
    {
        if (samples.Length == 0)
            return 0;

        double sum = 0;
        foreach (var sample in samples)
            sum += (double)sample * sample;

        return Math.Sqrt(sum / samples.Length);
    }
}
