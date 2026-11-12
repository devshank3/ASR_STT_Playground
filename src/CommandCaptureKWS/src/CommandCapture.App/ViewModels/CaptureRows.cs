using CommandCapture.Voice;

namespace CommandCapture.App.ViewModels;

public sealed class CatalogPhraseRow
{
    public CatalogPhraseRow(string phrase) => Phrase = phrase;

    public string Phrase { get; }
}

public sealed class PhraseLogRow
{
    public PhraseLogRow(PhraseTrace trace)
    {
        Time = trace.Timestamp.ToLocalTime().ToString("HH:mm:ss");
        Keyword = Show(trace.RawKeyword);
        Phrase = Show(trace.Phrase);
        Outcome = trace.Outcome;
    }

    public string Time { get; }

    public string Keyword { get; }

    public string Phrase { get; }

    public string Outcome { get; }

    private static string Show(string value) => string.IsNullOrWhiteSpace(value) ? "—" : value;
}
