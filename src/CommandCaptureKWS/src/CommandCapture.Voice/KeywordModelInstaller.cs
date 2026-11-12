using System.Security.Cryptography;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace CommandCapture.Voice;

public sealed record KeywordModelPaths(
    string Encoder,
    string Decoder,
    string Joiner,
    string Tokens,
    string BpeModel,
    string Directory);

public static class KeywordModelInstaller
{
    public const string ArchiveSha256 = "f170013b4716e41b62b9bfd809687c207cef798ef9bc6534d524e17af9b6561a";

    public const string FileHost =
        "https://modelscope.cn/models/pkufool/sherpa-onnx-kws-zipformer-gigaspeech-3.3M-2024-01-01/resolve/master/";

    public static readonly string[] ArchiveUrls =
    [
        "https://github.com/k2-fsa/sherpa-onnx/releases/download/kws-models/sherpa-onnx-kws-zipformer-gigaspeech-3.3M-2024-01-01.tar.bz2",
    ];

    private static readonly (string Name, string Sha256)[] ModelFiles =
    [
        ("encoder-epoch-12-avg-2-chunk-16-left-64.int8.onnx", "1e721676515bcd42a186979733981213c66c80db680e1cc582dfedf3be76e678"),
        ("decoder-epoch-12-avg-2-chunk-16-left-64.onnx", "f61ebd3eed3773a44d088d53dfae92dbb6aec4839f4dcaee2d402414741663a3"),
        ("joiner-epoch-12-avg-2-chunk-16-left-64.int8.onnx", "eae9da0c7e1e6c6a3f4cc42d167899c388f6c6701b94cb96320e4f55df79624c"),
        ("tokens.txt", "fd2ded4050a55d2b1578870ba8697d02371980217806b7558bd0a5cc60f3ba53"),
        ("bpe.model", "c8a2a0129c4ab8e463164c142f82d25649661b122c8cd0b7aab5c9e80b90ad24"),
    ];

    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CommandCapture",
        "models");

    public static string ModelDirectory =>
        Path.Combine(Root, "sherpa-onnx-kws-zipformer-gigaspeech-3.3M-2024-01-01");

    public static bool IsInstalled() => FilesAreValid();

    public static async Task<KeywordModelPaths> EnsureAsync(
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        if (FilesAreValid())
        {
            return Describe();
        }

        Directory.CreateDirectory(ModelDirectory);
        var failures = new List<string>();
        var archivePath = Path.Combine(Root, "sherpa-onnx-kws-zipformer-gigaspeech-3.3M-2024-01-01.tar.bz2");
        foreach (var url in ArchiveUrls)
        {
            try
            {
                progress?.Report("Downloading the keyword model archive.");
                using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                limit.CancelAfter(TimeSpan.FromSeconds(20));
                await DownloadAsync(url, archivePath, progress, limit.Token);
                var actual = await HashAsync(archivePath, cancellationToken);
                if (!string.Equals(actual, ArchiveSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"Keyword model hash was {actual}, expected {ArchiveSha256}.");
                }

                progress?.Report("Unpacking the keyword model.");
                Extract(archivePath, Root);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                failures.Add(url + ": " + exception.Message);
            }
        }

        if (!FilesAreValid())
        {
            foreach (var (name, sha256) in ModelFiles)
            {
                var path = Path.Combine(ModelDirectory, name);
                if (await FileMatchesAsync(path, sha256, cancellationToken))
                {
                    continue;
                }

                var url = FileHost + name;
                progress?.Report($"Downloading {name}.");
                try
                {
                    await DownloadAsync(url, path, progress, cancellationToken);
                    if (!await FileMatchesAsync(path, sha256, cancellationToken))
                    {
                        var actual = await HashAsync(path, cancellationToken);
                        throw new InvalidDataException($"{name} hash was {actual}, expected {sha256}.");
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    failures.Add(url + ": " + exception.Message);
                }
            }
        }

        if (!FilesAreValid())
        {
            throw new InvalidOperationException(
                "The keyword model could not be downloaded. " + string.Join(" | ", failures));
        }

        progress?.Report("Keyword model is ready.");
        return Describe();
    }

    private static KeywordModelPaths Describe()
    {
        var directory = ModelDirectory;
        return new KeywordModelPaths(
            Path.Combine(directory, ModelFiles[0].Name),
            Path.Combine(directory, ModelFiles[1].Name),
            Path.Combine(directory, ModelFiles[2].Name),
            Path.Combine(directory, ModelFiles[3].Name),
            Path.Combine(directory, ModelFiles[4].Name),
            directory);
    }

    private static bool FilesAreValid()
    {
        foreach (var (name, sha256) in ModelFiles)
        {
            var path = Path.Combine(ModelDirectory, name);
            if (!File.Exists(path))
            {
                return false;
            }

            using var stream = File.OpenRead(path);
            var actual = Convert.ToHexString(SHA256.HashData(stream));
            if (!string.Equals(actual, sha256, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static async Task<bool> FileMatchesAsync(string path, string sha256, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        return string.Equals(await HashAsync(path, cancellationToken), sha256, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task DownloadAsync(
        string url,
        string destination,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 CommandCapture");
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = File.Create(destination);
        var buffer = new byte[81920];
        long received = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            received += read;
            if (total is > 0)
            {
                progress?.Report($"Downloading the keyword model, {received * 100 / total.Value}%.");
            }
        }
    }

    private static async Task<string> HashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static void Extract(string archivePath, string destination)
    {
        using var stream = File.OpenRead(archivePath);
        using var reader = ReaderFactory.OpenReader(stream);
        while (reader.MoveToNextEntry())
        {
            if (reader.Entry.IsDirectory)
            {
                continue;
            }

            reader.WriteEntryToDirectory(destination, new ExtractionOptions
            {
                ExtractFullPath = true,
                Overwrite = true,
            });
        }
    }
}
