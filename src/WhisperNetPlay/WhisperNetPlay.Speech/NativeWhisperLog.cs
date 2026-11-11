using Whisper.net.Logger;

namespace WhisperNetPlay.Speech;

/// <summary>
/// Best-effort file logger for whisper.cpp / ggml native diagnostics
/// (<c>%LOCALAPPDATA%\WhisperNetPlay\whisper-native.log</c>).
/// </summary>
internal sealed class NativeWhisperLog : IDisposable
{
    private readonly IDisposable _subscription;

    private NativeWhisperLog(IDisposable subscription)
    {
        _subscription = subscription;
    }

    public static NativeWhisperLog Create()
    {
        string logPath;
        try
        {
            Directory.CreateDirectory(WhisperNetPlayStorage.RootDirectory);
            logPath = WhisperNetPlayStorage.NativeLogPath;
            File.WriteAllText(logPath, $"# WhisperNetPlay native log — {DateTime.Now:O}{Environment.NewLine}");
        }
        catch
        {
            return new NativeWhisperLog(LogProvider.AddLogger((_, _) => { }));
        }

        var sync = new object();
        return new NativeWhisperLog(LogProvider.AddLogger((level, message) =>
        {
            if (string.IsNullOrEmpty(message))
                return;
            lock (sync)
            {
                try { File.AppendAllText(logPath, $"[{level}] {message}{Environment.NewLine}"); }
                catch { /* best-effort diagnostics logging */ }
            }
        }));
    }

    public void Dispose() => _subscription.Dispose();
}
