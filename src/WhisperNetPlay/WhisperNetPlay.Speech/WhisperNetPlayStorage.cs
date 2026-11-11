namespace WhisperNetPlay.Speech;

/// <summary>
/// Local application-data layout for WhisperNetPlay
/// (<c>%LOCALAPPDATA%\WhisperNetPlay</c>).
/// </summary>
public static class WhisperNetPlayStorage
{
    public const string FolderName = "WhisperNetPlay";
    public const string ModelPathEnvironmentVariable = "WHISPERNETPLAY_MODEL";

    public static string RootDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), FolderName);

    public static string ModelsDirectory => Path.Combine(RootDirectory, "models");

    public static string CrashLogPath => Path.Combine(RootDirectory, "crash.log");

    public static string NativeLogPath => Path.Combine(RootDirectory, "whisper-native.log");
}
