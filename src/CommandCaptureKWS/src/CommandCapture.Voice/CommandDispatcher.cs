using System.Threading.Channels;

namespace CommandCapture.Voice;

public sealed class CommandDispatcher
{
    private readonly IClock _clock;
    private readonly Dictionary<string, IVoiceDirectiveHandler> _handlers;
    private readonly Dictionary<string, DateTimeOffset> _lastAccepted = new(StringComparer.Ordinal);
    private readonly Channel<VoiceCommand> _queue;
    private CommandCatalog _catalog;

    public CommandDispatcher(
        CommandCatalog catalog,
        IReadOnlyList<IVoiceDirectiveHandler> handlers,
        IClock clock,
        int queueCapacity = 8)
    {
        _catalog = catalog;
        _clock = clock;
        _handlers = handlers.ToDictionary(handler => handler.CommandName, StringComparer.Ordinal);
        _queue = Channel.CreateBounded<VoiceCommand>(new BoundedChannelOptions(queueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    public event Action<string>? Status;

    public event Action<PhraseTrace>? Traced;

    public void UseCatalog(CommandCatalog catalog) => _catalog = catalog;

    public bool TrySubmit(VoiceCommand command)
    {
        if (!_handlers.ContainsKey(command.Name))
        {
            Report(command, $"Dropped unknown command {command.Name}.", "Dropped unknown command");
            return false;
        }

        if (_queue.Writer.TryWrite(command))
        {
            return true;
        }

        Report(command, $"Dropped {command.Name} while busy.", "Dropped while busy");
        return false;
    }

    public void Complete() => _queue.Writer.TryComplete();

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await foreach (var command in _queue.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            var now = _clock.UtcNow;
            var cooldown = _catalog.CooldownMs(command.Name);
            if (_lastAccepted.TryGetValue(command.Name, out var last) &&
                (now - last).TotalMilliseconds < cooldown)
            {
                Report(command, $"Dropped {command.Name} during cooldown.", "Cooldown");
                continue;
            }

            try
            {
                await _handlers[command.Name].HandleAsync(command, cancellationToken);
                _lastAccepted[command.Name] = _clock.UtcNow;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                Report(command, $"{command.Name} failed: {exception.Message}", $"Failed: {exception.Message}");
            }
        }
    }

    private void Report(VoiceCommand command, string status, string outcome)
    {
        Status?.Invoke(status);
        var keyword = command.Slots.TryGetValue(PhraseTrace.KeywordSlot, out var raw) && raw.Length > 0
            ? raw
            : command.Phrase;
        Traced?.Invoke(new PhraseTrace(command.Timestamp, keyword, command.Phrase, command.Name, outcome));
    }
}
