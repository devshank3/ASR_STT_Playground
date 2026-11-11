using Whisper.net.Ggml;

namespace WhisperNetPlay.Speech;

/// <summary>
/// Resolves the Whisper ggml model file used for transcription, downloading the default model on
/// first use.
/// </summary>
/// <remarks>
/// Resolution order:
/// <list type="number">
///   <item>The path in the <c>WHISPERNETPLAY_MODEL</c> environment variable, when it exists.</item>
///   <item>A <c>*.bin</c> model already present in <c>%LOCALAPPDATA%\WhisperNetPlay\models</c>.</item>
///   <item>Otherwise the default <see cref="GgmlType.LargeV3Turbo"/> model is downloaded into
///   that directory.</item>
/// </list>
/// </remarks>
public sealed class WhisperModelProvider
{
    public const string ModelPathEnvironmentVariable = WhisperNetPlayStorage.ModelPathEnvironmentVariable;

    private const GgmlType DefaultModel = GgmlType.LargeV3Turbo;
    private const string DefaultModelFileName = "ggml-large-v3-turbo.bin";

    private readonly string _modelDirectory;

    public WhisperModelProvider(string? modelDirectory = null)
    {
        _modelDirectory = modelDirectory ?? WhisperNetPlayStorage.ModelsDirectory;
    }

    /// <summary>The directory where downloaded / user-supplied models are stored.</summary>
    public string ModelDirectory => _modelDirectory;

    /// <summary>
    /// Returns the path to a usable model file, downloading the default model if none is present.
    /// </summary>
    public async Task<string> GetModelPathAsync(
        IProgress<string>? progress,
        CancellationToken cancellationToken = default)
    {
        var overridePath = Environment.GetEnvironmentVariable(ModelPathEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath))
            return overridePath;

        Directory.CreateDirectory(_modelDirectory);

        var localDefault = Path.Combine(_modelDirectory, DefaultModelFileName);
        if (IsUsable(localDefault))
            return localDefault;

        var localExisting = FindUsableBin(_modelDirectory);
        if (localExisting is not null)
            return localExisting;

        progress?.Report("Downloading speech model (large-v3-turbo, ~1.5 GB — one time)…");
        var downloadPath = localDefault + ".download";
        try
        {
            await using var modelStream = await WhisperGgmlDownloader.Default
                .GetGgmlModelAsync(DefaultModel, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            await using (var fileWriter = File.Create(downloadPath))
            {
                var buffer = new byte[1 << 20];
                long totalBytes = 0;
                long lastReportedBytes = 0;
                int read;
                while ((read = await modelStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await fileWriter.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    totalBytes += read;
                    if (totalBytes - lastReportedBytes >= 25L * 1024 * 1024)
                    {
                        lastReportedBytes = totalBytes;
                        progress?.Report(
                            $"Downloading speech model… {totalBytes / (1024 * 1024)} MB (~1.5 GB total, one time)");
                    }
                }
            }

            File.Move(downloadPath, localDefault, overwrite: true);
        }
        catch
        {
            TryDelete(downloadPath);
            throw;
        }

        return localDefault;
    }

    private static string? FindUsableBin(string directory)
    {
        if (!Directory.Exists(directory))
            return null;

        foreach (var existing in Directory.EnumerateFiles(directory, "*.bin"))
        {
            if (IsUsable(existing))
                return existing;
        }

        return null;
    }

    private static bool IsUsable(string path)
        => File.Exists(path) && new FileInfo(path).Length > 0;

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup of a partial download.
        }
    }
}
