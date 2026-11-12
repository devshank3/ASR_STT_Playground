using CommandCapture.Voice;

namespace CommandCapture.Voice.Tests;

public sealed class CommandCatalogTests
{
    [Fact]
    public void Load_ReadsShippedCommands()
    {
        var catalog = CommandCatalog.Load(Path.Combine(AppContext.BaseDirectory, "commands.json"));

        Assert.Equal(["capture_still"], catalog.Directives.Select(command => command.Name).ToArray());
        Assert.Contains("capture image", catalog.Directives[0].Phrases);
        Assert.Equal(800, catalog.CooldownMs("capture_still"));
    }

    [Fact]
    public void AddPhrase_RejectsBlankAndDuplicate()
    {
        var catalog = new CommandCatalog([new VoiceDirective("capture_still", ["capture image"], 800)]);

        Assert.Throws<InvalidDataException>(() => catalog.AddPhrase("capture_still", "  "));
        Assert.Throws<InvalidDataException>(() => catalog.AddPhrase("capture_still", "Capture Image"));
        Assert.Throws<InvalidDataException>(() => catalog.AddPhrase("missing", "hello"));
    }

    [Fact]
    public void Save_RoundTripsAddedPhrase()
    {
        var directory = Path.Combine(Path.GetTempPath(), "command-capture-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "commands.json");
        try
        {
            var catalog = new CommandCatalog([new VoiceDirective("capture_still", ["capture image"], 800)]);
            catalog.AddPhrase("capture_still", "grab frame").Save(path);

            var loaded = CommandCatalog.Load(path);

            Assert.Equal(["capture image", "grab frame"], loaded.Directives[0].Phrases);
            Assert.Equal(800, loaded.CooldownMs("capture_still"));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
