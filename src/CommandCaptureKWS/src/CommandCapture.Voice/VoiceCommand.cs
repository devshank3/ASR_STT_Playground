namespace CommandCapture.Voice;

public sealed record VoiceCommand(
    string Name,
    string Phrase,
    float Confidence,
    DateTimeOffset Timestamp,
    IReadOnlyDictionary<string, string> Slots)
{
    public static IReadOnlyDictionary<string, string> EmptySlots { get; } =
        new Dictionary<string, string>();
}
