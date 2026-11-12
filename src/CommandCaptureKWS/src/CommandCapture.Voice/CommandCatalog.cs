using System.Text.Json;

namespace CommandCapture.Voice;

public sealed record VoiceDirective(string Name, IReadOnlyList<string> Phrases, int CooldownMs);

public sealed class CommandCatalog
{
    public const string CaptureStill = "capture_still";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly Dictionary<string, VoiceDirective> _byName;

    public CommandCatalog(IReadOnlyList<VoiceDirective> directives)
    {
        Directives = directives;
        _byName = directives.ToDictionary(command => command.Name, StringComparer.Ordinal);
    }

    public IReadOnlyList<VoiceDirective> Directives { get; }

    public int CooldownMs(string commandName) =>
        _byName.TryGetValue(commandName, out var command) ? command.CooldownMs : 800;

    public CommandCatalog AddPhrase(string commandName, string phrase)
    {
        if (string.IsNullOrWhiteSpace(phrase))
        {
            throw new InvalidDataException("A phrase cannot be empty.");
        }

        var trimmed = phrase.Trim();
        var found = false;
        var directives = new List<VoiceDirective>(Directives.Count);
        foreach (var command in Directives)
        {
            if (!string.Equals(command.Name, commandName, StringComparison.Ordinal))
            {
                directives.Add(command);
                continue;
            }

            found = true;
            if (command.Phrases.Any(existing => string.Equals(existing, trimmed, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException($"\"{trimmed}\" is already a command.");
            }

            directives.Add(new VoiceDirective(command.Name, [.. command.Phrases, trimmed], command.CooldownMs));
        }

        if (!found)
        {
            throw new InvalidDataException($"Command {commandName} was not found.");
        }

        return new CommandCatalog(directives);
    }

    public string Save(string path)
    {
        var json = ToJson();
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, json);
        return json;
    }

    public static CommandCatalog Load(string path)
    {
        var json = File.ReadAllText(path);
        var file = JsonSerializer.Deserialize<CatalogFile>(json, JsonOptions)
            ?? throw new InvalidDataException($"Command catalog {path} is empty.");

        if (file.Directives is null || file.Directives.Count == 0)
        {
            throw new InvalidDataException($"Command catalog {path} has no directives.");
        }

        var directives = file.Directives.Select(command =>
        {
            if (string.IsNullOrWhiteSpace(command.Name))
            {
                throw new InvalidDataException("A voice directive is missing its name.");
            }

            var phrases = (command.Phrases ?? [])
                .Select(phrase => phrase.Trim())
                .Where(phrase => phrase.Length > 0)
                .ToArray();
            if (phrases.Length == 0)
            {
                throw new InvalidDataException($"Command {command.Name} has no phrases.");
            }

            return new VoiceDirective(command.Name, phrases, command.CooldownMs <= 0 ? 800 : command.CooldownMs);
        }).ToArray();

        return new CommandCatalog(directives);
    }

    private string ToJson()
    {
        var file = new CatalogFile
        {
            Directives = Directives.Select(command => new CatalogDirective
            {
                Name = command.Name,
                Phrases = command.Phrases.ToList(),
                CooldownMs = command.CooldownMs,
            }).ToList(),
        };

        return JsonSerializer.Serialize(file, JsonOptions) + "\n";
    }

    private sealed class CatalogFile
    {
        public List<CatalogDirective>? Directives { get; set; }
    }

    private sealed class CatalogDirective
    {
        public string? Name { get; set; }

        public List<string>? Phrases { get; set; }

        public int CooldownMs { get; set; }
    }
}
