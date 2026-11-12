using System.Collections.ObjectModel;
using CommandCapture.Voice;

namespace CommandCapture.App.ViewModels;

public sealed class PhraseLog
{
    public const int Limit = 40;

    public ObservableCollection<PhraseLogRow> Entries { get; } = [];

    public void Insert(PhraseTrace trace)
    {
        Entries.Insert(0, new PhraseLogRow(trace));
        while (Entries.Count > Limit)
        {
            Entries.RemoveAt(Entries.Count - 1);
        }
    }
}
