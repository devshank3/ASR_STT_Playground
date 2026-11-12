namespace CommandCapture.Voice;

public sealed record PhraseTrace(
    DateTimeOffset Timestamp,
    string RawKeyword,
    string Phrase,
    string Command,
    string Outcome)
{
    public const string KeywordSlot = "keyword";
}
