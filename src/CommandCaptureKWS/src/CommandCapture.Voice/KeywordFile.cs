using System.Globalization;

namespace CommandCapture.Voice;

public sealed class PhraseMap
{
    private readonly Dictionary<string, (string Command, string Phrase)> _map;

    public PhraseMap(string path, Dictionary<string, (string Command, string Phrase)> map)
    {
        Path = path;
        _map = map;
    }

    public string Path { get; }

    public bool TryResolve(string keyword, out string command, out string phrase)
    {
        keyword = keyword.Trim();
        var at = keyword.LastIndexOf('@');
        if (at >= 0 && at < keyword.Length - 1)
        {
            keyword = keyword[(at + 1)..].Trim();
        }

        if (_map.TryGetValue(keyword, out var hit))
        {
            command = hit.Command;
            phrase = hit.Phrase;
            return true;
        }

        command = string.Empty;
        phrase = string.Empty;
        return false;
    }
}

public static class KeywordFile
{
    public const float Boost = 1.5f;
    public const float Threshold = 0.25f;

    public static PhraseMap Write(string directory, CommandCatalog catalog, SentencePieceEncoder encoder)
    {
        Directory.CreateDirectory(directory);
        var lines = new List<string>();
        var map = new Dictionary<string, (string Command, string Phrase)>(StringComparer.OrdinalIgnoreCase);
        foreach (var command in catalog.Directives)
        {
            foreach (var phrase in command.Phrases)
            {
                var tokens = encoder.EncodePhrase(phrase);
                var alias = phrase.Replace(' ', '_');
                var line = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{tokens} :{Boost} #{Threshold} @{alias}");
                lines.Add(line);
                map[phrase] = (command.Name, phrase);
                map[alias] = (command.Name, phrase);
                map[tokens] = (command.Name, phrase);
            }
        }

        var path = Path.Combine(directory, "commands-keywords.txt");
        File.WriteAllLines(path, lines);
        return new PhraseMap(path, map);
    }
}
