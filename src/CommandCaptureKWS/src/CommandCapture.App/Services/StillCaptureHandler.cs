using CommandCapture.App;
using CommandCapture.Camera;
using CommandCapture.Voice;

namespace CommandCapture.App.Services;

public sealed class StillCaptureHandler : IVoiceDirectiveHandler
{
    private readonly ICameraSession _camera;
    private readonly IUiMarshal _ui;
    private readonly Action<string, string, PhraseTrace> _saved;

    public StillCaptureHandler(ICameraSession camera, IUiMarshal ui, Action<string, string, PhraseTrace> saved)
    {
        _camera = camera;
        _ui = ui;
        _saved = saved;
    }

    public string CommandName => CommandCatalog.CaptureStill;

    public async Task HandleAsync(VoiceCommand command, CancellationToken cancellationToken)
    {
        var path = await _camera.CaptureStillAsync(cancellationToken);
        var elapsed = DateTimeOffset.UtcNow - command.Timestamp;
        var keyword = command.Slots.TryGetValue(PhraseTrace.KeywordSlot, out var raw) ? raw : command.Phrase;
        var fileName = Path.GetFileName(path);
        var trace = new PhraseTrace(
            command.Timestamp,
            keyword,
            command.Phrase,
            command.Name,
            $"Saved in {elapsed.TotalMilliseconds:0} ms. {fileName}");
        _ui.Post(() => _saved(fileName, $"Heard \"{command.Phrase}\" and saved in {elapsed.TotalMilliseconds:0} ms. {path}", trace));
    }
}
