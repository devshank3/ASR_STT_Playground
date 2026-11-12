using CommandCapture.Audio;

namespace CommandCapture.Voice;

public sealed class VoicePipeline : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly object _rebuild = new();
    private readonly IPcmSource _source;
    private readonly CommandDispatcher _dispatcher;
    private readonly KeywordModelPaths _model;
    private CommandCatalog _catalog;
    private SentencePieceEncoder? _encoder;
    private SherpaKeywordListener? _listener;
    private PhraseMap? _phrases;
    private int _catalogGeneration;
    private int _running;

    public VoicePipeline(
        IPcmSource source,
        CommandCatalog catalog,
        CommandDispatcher dispatcher,
        KeywordModelPaths model)
    {
        _source = source;
        _catalog = catalog;
        _dispatcher = dispatcher;
        _model = model;
        _dispatcher.Status += message => StatusMessage?.Invoke(message);
        _dispatcher.Traced += trace => Traced?.Invoke(trace);
    }

    public event Action<string>? StatusMessage;

    public event Action<PhraseTrace>? Traced;

    public void ReloadCatalog(CommandCatalog catalog)
    {
        int generation;
        SentencePieceEncoder? encoder;
        lock (_gate)
        {
            _catalog = catalog;
            generation = ++_catalogGeneration;
            encoder = _running == 1 ? _encoder : null;
        }

        if (encoder is null)
        {
            lock (_gate)
            {
                if (generation == _catalogGeneration)
                {
                    _dispatcher.UseCatalog(catalog);
                }
            }

            return;
        }

        lock (_rebuild)
        {
            SentencePieceEncoder current;
            lock (_gate)
            {
                if (generation != _catalogGeneration || _running == 0 || _encoder is not { } ready)
                {
                    return;
                }

                current = ready;
            }

            var phrases = KeywordFile.Write(_model.Directory, catalog, current);
            var next = new SherpaKeywordListener(_model, phrases.Path);
            SherpaKeywordListener? previous;
            lock (_gate)
            {
                if (generation != _catalogGeneration || _running == 0 || _encoder is null)
                {
                    next.Dispose();
                    return;
                }

                previous = _listener;
                _listener = next;
                _phrases = phrases;
                _dispatcher.UseCatalog(catalog);
            }

            previous?.Dispose();
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var encoder = SentencePieceEncoder.Load(_model.BpeModel);
        lock (_gate)
        {
            _encoder = encoder;
            _running = 1;
        }

        var dispatching = _dispatcher.RunAsync(cancellationToken);
        try
        {
            EnsureListener();
            await _source.StartAsync(cancellationToken).ConfigureAwait(false);
            StatusMessage?.Invoke("Listening.");
            await foreach (var samples in _source.ReadFramesAsync(cancellationToken).ConfigureAwait(false))
            {
                foreach (var hit in ReadHits(samples))
                {
                    if (!hit.Resolved)
                    {
                        StatusMessage?.Invoke($"Heard \"{hit.Keyword}\" with no command.");
                        Traced?.Invoke(new PhraseTrace(
                            DateTimeOffset.UtcNow,
                            hit.Keyword,
                            string.Empty,
                            string.Empty,
                            "No matching command"));
                        continue;
                    }

                    _dispatcher.TrySubmit(new VoiceCommand(
                        hit.Command,
                        hit.Phrase,
                        Confidence: 1f,
                        DateTimeOffset.UtcNow,
                        new Dictionary<string, string> { [PhraseTrace.KeywordSlot] = hit.Keyword }));
                }
            }
        }
        finally
        {
            try
            {
                _dispatcher.Complete();
                try
                {
                    await dispatching.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }

                await _source.StopAsync().ConfigureAwait(false);
            }
            finally
            {
                SherpaKeywordListener? listener;
                lock (_gate)
                {
                    _running = 0;
                    listener = _listener;
                    _listener = null;
                    _phrases = null;
                    _encoder = null;
                }

                listener?.Dispose();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _source.StopAsync().ConfigureAwait(false);
    }

    private void EnsureListener()
    {
        lock (_rebuild)
        {
            int generation;
            CommandCatalog catalog;
            SentencePieceEncoder encoder;
            lock (_gate)
            {
                if (_listener is not null || _encoder is null || _running == 0)
                {
                    return;
                }

                catalog = _catalog;
                encoder = _encoder;
                generation = _catalogGeneration;
            }

            var phrases = KeywordFile.Write(_model.Directory, catalog, encoder);
            var listener = new SherpaKeywordListener(_model, phrases.Path);
            lock (_gate)
            {
                if (_listener is not null || generation != _catalogGeneration || _running == 0 || _encoder is null)
                {
                    listener.Dispose();
                    return;
                }

                _listener = listener;
                _phrases = phrases;
            }
        }
    }

    private List<HeardKeyword> ReadHits(float[] samples)
    {
        lock (_gate)
        {
            if (_listener is null || _phrases is null)
            {
                return [];
            }

            var heard = _listener.AcceptLive(samples);
            var hits = new List<HeardKeyword>(heard.Count);
            foreach (var keyword in heard)
            {
                if (_phrases.TryResolve(keyword, out var command, out var phrase))
                {
                    hits.Add(new HeardKeyword(keyword, command, phrase, true));
                }
                else
                {
                    hits.Add(new HeardKeyword(keyword, string.Empty, string.Empty, false));
                }
            }

            return hits;
        }
    }

    private readonly record struct HeardKeyword(string Keyword, string Command, string Phrase, bool Resolved);
}
