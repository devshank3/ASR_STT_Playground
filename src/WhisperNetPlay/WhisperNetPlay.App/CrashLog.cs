using Avalonia.Threading;
using WhisperNetPlay.Speech;

namespace WhisperNetPlay.App;

/// <summary>
/// Writes unhandled exceptions to the console and to
/// <c>%LOCALAPPDATA%\WhisperNetPlay\crash.log</c>.
/// </summary>
internal static class CrashLog
{
    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Write("AppDomain.UnhandledException", e.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Write("UnobservedTaskException", e.Exception);
            e.SetObserved();
        };

        Dispatcher.UIThread.UnhandledException += (_, e) =>
            Write("Dispatcher.UnhandledException", e.Exception);
    }

    public static void Write(string source, Exception? exception)
    {
        var text = $"""

            ===== {DateTime.Now:yyyy-MM-dd HH:mm:ss} · {source} =====
            {exception}

            """;

        Console.Error.WriteLine(text);
        try
        {
            Directory.CreateDirectory(WhisperNetPlayStorage.RootDirectory);
            File.AppendAllText(WhisperNetPlayStorage.CrashLogPath, text);
        }
        catch
        {
            // Console copy above is the fallback.
        }
    }
}
