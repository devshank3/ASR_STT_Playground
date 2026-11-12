using System.Speech.Synthesis;
using CommandCapture.Voice;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace CommandCapture.Voice.Tests;

[Collection("keyword-model")]
public sealed class KeywordSpotterTests
{
    private readonly KeywordModelFixture _fixture;

    public KeywordSpotterTests(KeywordModelFixture fixture) => _fixture = fixture;

    [Fact]
    public void CatalogPhrases_EncodeToKnownPieces()
    {
        var catalog = CommandCatalog.Load(Path.Combine(AppContext.BaseDirectory, "commands.json"));
        var lines = File.ReadAllLines(_fixture.Paths.Tokens);
        foreach (var phrase in catalog.Directives.SelectMany(command => command.Phrases))
        {
            var encoded = _fixture.Encoder.EncodePhrase(phrase);
            Assert.False(string.IsNullOrWhiteSpace(encoded));
            foreach (var piece in encoded.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                Assert.Contains(lines, line => line.Split(' ')[0] == piece);
            }
        }
    }

    [Fact]
    public void Silence_ProducesNoKeyword()
    {
        var heard = _fixture.Listener.DetectUtterance(new float[16_000]);

        Assert.Empty(heard);
    }

    [Fact]
    public void SpokenCapture_MapsToCaptureStill()
    {
        var samples = Speak("capture image", noise: 0f);
        var heard = _fixture.Listener.DetectUtterance(samples);

        Assert.Contains(heard, keyword => Resolves(keyword, "capture_still"));
    }

    [Fact]
    public void SpokenCaptureWithRoomNoise_StillMapsToCaptureStill()
    {
        var samples = Speak("capture image", noise: 0.02f);
        var heard = _fixture.Listener.DetectUtterance(samples);

        Assert.Contains(heard, keyword => Resolves(keyword, "capture_still"));
    }

    [Fact]
    public void SimilarSentence_IsNotCaptureStill()
    {
        var samples = Speak("capture the flag", noise: 0f);
        var heard = _fixture.Listener.DetectUtterance(samples);

        Assert.DoesNotContain(heard, keyword => Resolves(keyword, "capture_still"));
    }

    private bool Resolves(string keyword, string command) =>
        _fixture.Phrases.TryResolve(keyword, out var name, out _) && name == command;

    private static float[] Speak(string text, float noise)
    {
        var path = Path.Combine(Path.GetTempPath(), $"command-capture-{Guid.NewGuid():N}.wav");
        using (var synthesizer = new SpeechSynthesizer())
        {
            synthesizer.SetOutputToWaveFile(path);
            synthesizer.Speak(text);
        }

        try
        {
            using var reader = new WaveFileReader(path);
            ISampleProvider samples = reader.ToSampleProvider();
            if (reader.WaveFormat.Channels == 2)
            {
                samples = new StereoToMonoSampleProvider(samples);
            }

            var buffer = new float[reader.WaveFormat.SampleRate];
            var collected = new List<float>();
            int read;
            while ((read = samples.Read(buffer, 0, buffer.Length)) > 0)
            {
                collected.AddRange(buffer.AsSpan(0, read));
            }

            var mono = Pcm.Resample(collected.ToArray(), reader.WaveFormat.SampleRate);
            if (noise > 0)
            {
                var random = new Random(17);
                for (var i = 0; i < mono.Length; i++)
                {
                    mono[i] += (float)((random.NextDouble() * 2) - 1) * noise;
                }
            }

            return mono;
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static class Pcm
    {
        public static float[] Resample(float[] mono, int sourceRate) =>
            CommandCapture.Audio.PcmResampler.Resample(mono, sourceRate, 16_000);
    }
}

[CollectionDefinition("keyword-model")]
public sealed class KeywordModelCollection : ICollectionFixture<KeywordModelFixture>;

public sealed class KeywordModelFixture : IAsyncLifetime
{
    public KeywordModelPaths Paths { get; private set; } = null!;

    public SentencePieceEncoder Encoder { get; private set; } = null!;

    public PhraseMap Phrases { get; private set; } = null!;

    public SherpaKeywordListener Listener { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Paths = await KeywordModelInstaller.EnsureAsync(progress: null, CancellationToken.None);
        Encoder = SentencePieceEncoder.Load(Paths.BpeModel);
        var catalog = CommandCatalog.Load(Path.Combine(AppContext.BaseDirectory, "commands.json"));
        Phrases = KeywordFile.Write(Path.Combine(Path.GetTempPath(), "command-capture-keywords"), catalog, Encoder);
        Listener = new SherpaKeywordListener(Paths, Phrases.Path);
    }

    public Task DisposeAsync()
    {
        Listener?.Dispose();
        return Task.CompletedTask;
    }
}
