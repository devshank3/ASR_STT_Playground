using System.Globalization;
using System.Text;

namespace CommandCapture.Voice;

public sealed class SentencePieceEncoder
{
    public const char WordStart = '\u2581';

    private readonly Dictionary<string, int> _rank;
    private readonly Dictionary<string, float> _score;
    private readonly int _modelType;

    private SentencePieceEncoder(
        Dictionary<string, int> rank,
        Dictionary<string, float> score,
        int modelType)
    {
        _rank = rank;
        _score = score;
        _modelType = modelType;
    }

    public static SentencePieceEncoder Load(string modelPath)
    {
        var bytes = File.ReadAllBytes(modelPath);
        var pieces = new List<Piece>();
        var modelType = 1;
        var offset = 0;
        while (offset < bytes.Length)
        {
            var key = ReadVarint(bytes, ref offset);
            var field = (int)(key >> 3);
            var wire = (int)(key & 7);
            switch (field)
            {
                case 1:
                    pieces.Add(ReadPiece(ReadBytes(bytes, ref offset)));
                    break;
                case 2:
                    modelType = ReadModelType(ReadBytes(bytes, ref offset));
                    break;
                default:
                    Skip(bytes, ref offset, wire);
                    break;
            }
        }

        var rank = new Dictionary<string, int>(pieces.Count, StringComparer.Ordinal);
        var score = new Dictionary<string, float>(pieces.Count, StringComparer.Ordinal);
        for (var i = 0; i < pieces.Count; i++)
        {
            var piece = pieces[i];
            if (piece.Type is 2 or 3 or 5 || piece.Text.Length == 0)
            {
                continue;
            }

            rank.TryAdd(piece.Text, i);
            score.TryAdd(piece.Text, piece.Score);
        }

        return new SentencePieceEncoder(rank, score, modelType);
    }

    public string EncodePhrase(string phrase)
    {
        var text = string.Join(' ', phrase.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        text = text.ToUpperInvariant();
        var tokens = new List<string>();
        foreach (var word in text.Split(' '))
        {
            tokens.AddRange(_modelType == 2 ? EncodeBpe(WordStart + word) : EncodeUnigram(WordStart + word));
        }

        return string.Join(' ', tokens);
    }

    private IEnumerable<string> EncodeBpe(string word)
    {
        var symbols = word.EnumerateRunes().Select(rune => rune.ToString()).ToList();
        foreach (var symbol in symbols)
        {
            if (!_rank.ContainsKey(symbol))
            {
                throw new InvalidOperationException(
                    $"The keyword model has no piece for '{symbol}' in \"{word}\".");
            }
        }

        while (symbols.Count > 1)
        {
            var bestIndex = -1;
            var bestRank = int.MaxValue;
            for (var i = 0; i < symbols.Count - 1; i++)
            {
                var merged = symbols[i] + symbols[i + 1];
                if (_rank.TryGetValue(merged, out var rank) && rank < bestRank)
                {
                    bestRank = rank;
                    bestIndex = i;
                }
            }

            if (bestIndex < 0)
            {
                break;
            }

            symbols[bestIndex] += symbols[bestIndex + 1];
            symbols.RemoveAt(bestIndex + 1);
        }

        return symbols;
    }

    private IEnumerable<string> EncodeUnigram(string word)
    {
        var count = word.EnumerateRunes().Count();
        var best = new double[count + 1];
        var previous = new int[count + 1];
        Array.Fill(best, double.NegativeInfinity);
        best[0] = 0;
        var runes = word.EnumerateRunes().Select(rune => rune.ToString()).ToArray();
        for (var start = 0; start < count; start++)
        {
            if (double.IsNegativeInfinity(best[start]))
            {
                continue;
            }

            var builder = new StringBuilder();
            for (var end = start; end < count && end - start < 16; end++)
            {
                builder.Append(runes[end]);
                var text = builder.ToString();
                if (!_score.TryGetValue(text, out var score))
                {
                    continue;
                }

                var candidate = best[start] + score;
                if (candidate > best[end + 1])
                {
                    best[end + 1] = candidate;
                    previous[end + 1] = start;
                }
            }
        }

        if (double.IsNegativeInfinity(best[count]))
        {
            throw new InvalidOperationException($"The keyword model cannot encode \"{word}\".");
        }

        var parts = new Stack<string>();
        for (var end = count; end > 0;)
        {
            var start = previous[end];
            parts.Push(string.Concat(runes[start..end]));
            end = start;
        }

        return parts;
    }

    private readonly record struct Piece(string Text, float Score, int Type);

    private static Piece ReadPiece(byte[] message)
    {
        var text = string.Empty;
        var score = 0f;
        var type = 1;
        var offset = 0;
        while (offset < message.Length)
        {
            var key = ReadVarint(message, ref offset);
            var field = (int)(key >> 3);
            var wire = (int)(key & 7);
            switch (field)
            {
                case 1:
                    text = Encoding.UTF8.GetString(ReadBytes(message, ref offset));
                    break;
                case 2:
                    score = BitConverter.ToSingle(ReadFixed(message, ref offset, 4));
                    break;
                case 3:
                    type = (int)ReadVarint(message, ref offset);
                    break;
                default:
                    Skip(message, ref offset, wire);
                    break;
            }
        }

        return new Piece(text, score, type);
    }

    private static int ReadModelType(byte[] message)
    {
        var modelType = 1;
        var offset = 0;
        while (offset < message.Length)
        {
            var key = ReadVarint(message, ref offset);
            var field = (int)(key >> 3);
            var wire = (int)(key & 7);
            if (field == 3 && wire == 0)
            {
                modelType = (int)ReadVarint(message, ref offset);
            }
            else
            {
                Skip(message, ref offset, wire);
            }
        }

        return modelType;
    }

    private static byte[] ReadBytes(byte[] data, ref int offset)
    {
        var length = (int)ReadVarint(data, ref offset);
        var slice = data.AsSpan(offset, length).ToArray();
        offset += length;
        return slice;
    }

    private static byte[] ReadFixed(byte[] data, ref int offset, int length)
    {
        var slice = data.AsSpan(offset, length).ToArray();
        offset += length;
        return slice;
    }

    private static ulong ReadVarint(byte[] data, ref int offset)
    {
        ulong result = 0;
        var shift = 0;
        while (offset < data.Length && shift < 64)
        {
            var value = data[offset++];
            result |= (ulong)(value & 0x7F) << shift;
            if ((value & 0x80) == 0)
            {
                return result;
            }

            shift += 7;
        }

        throw new InvalidDataException("The sentence-piece model has a truncated varint.");
    }

    private static void Skip(byte[] data, ref int offset, int wire)
    {
        switch (wire)
        {
            case 0:
                ReadVarint(data, ref offset);
                break;
            case 1:
                offset += 8;
                break;
            case 2:
                var length = (int)ReadVarint(data, ref offset);
                offset += length;
                break;
            case 5:
                offset += 4;
                break;
            default:
                throw new InvalidDataException(
                    string.Create(CultureInfo.InvariantCulture, $"Unsupported protobuf wire type {wire}."));
        }
    }
}
