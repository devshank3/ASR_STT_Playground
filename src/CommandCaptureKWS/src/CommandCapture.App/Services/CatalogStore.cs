using CommandCapture.Voice;

namespace CommandCapture.App.Services;

public sealed class CatalogStore : IDisposable
{
    private readonly string _path;
    private readonly object _textGate = new();
    private readonly object _reloadGate = new();
    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _reload;
    private CancellationTokenSource? _lifetime;
    private string _applied = string.Empty;

    public CatalogStore(string path) => _path = path;

    public CommandCatalog? Current { get; private set; }

    public bool Loaded { get; private set; }

    public event Action<CommandCatalog>? Reloaded;

    public event Action<string>? Failed;

    public CommandCatalog Require()
    {
        if (Current is null)
        {
            Load();
        }

        return Current ?? throw new InvalidDataException($"Command catalog {_path} is empty.");
    }

    public void Load()
    {
        var json = File.ReadAllText(_path);
        Current = CommandCatalog.Load(_path);
        Remember(json);
        Loaded = true;
    }

    public CommandCatalog AddCapturePhrase(string phrase)
    {
        var current = Current ?? CommandCatalog.Load(_path);
        var next = current.AddPhrase(CommandCatalog.CaptureStill, phrase);
        Remember(next.Save(_path));
        Current = next;
        return next;
    }

    public void Watch(CancellationToken lifetime)
    {
        var directory = Path.GetDirectoryName(_path);
        var name = Path.GetFileName(_path);
        if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(name))
        {
            return;
        }

        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        _watcher = new FileSystemWatcher(directory, name)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        _watcher.Changed += OnFileChanged;
        _watcher.Created += OnFileChanged;
        _watcher.Renamed += OnFileChanged;
        _watcher.EnableRaisingEvents = true;
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _lifetime?.Cancel();
        lock (_reloadGate)
        {
            _reload?.Cancel();
            _reload?.Dispose();
            _reload = null;
        }

        _lifetime?.Dispose();
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if (_lifetime is null || _lifetime.IsCancellationRequested)
        {
            return;
        }

        CancellationToken token;
        lock (_reloadGate)
        {
            if (_lifetime.IsCancellationRequested)
            {
                return;
            }

            _reload?.Cancel();
            _reload?.Dispose();
            _reload = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            token = _reload.Token;
        }

        _ = ReloadAsync(token);
    }

    private async Task ReloadAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(400, token);
            var json = await ReadTextAsync(token);
            if (IsCurrent(json))
            {
                return;
            }

            var catalog = CommandCatalog.Load(_path);
            Current = catalog;
            Remember(json);
            Reloaded?.Invoke(catalog);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            Failed?.Invoke(exception.Message);
        }
    }

    private async Task<string> ReadTextAsync(CancellationToken token)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await File.ReadAllTextAsync(_path, token);
            }
            catch (IOException) when (attempt < 2)
            {
                await Task.Delay(150, token);
            }
        }
    }

    private bool IsCurrent(string json)
    {
        lock (_textGate)
        {
            return string.Equals(json, _applied, StringComparison.Ordinal);
        }
    }

    private void Remember(string json)
    {
        lock (_textGate)
        {
            _applied = json;
        }
    }
}
