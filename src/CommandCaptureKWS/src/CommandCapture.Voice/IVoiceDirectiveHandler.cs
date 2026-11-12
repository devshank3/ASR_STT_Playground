namespace CommandCapture.Voice;

public interface IVoiceDirectiveHandler
{
    string CommandName { get; }

    Task HandleAsync(VoiceCommand command, CancellationToken cancellationToken);
}
