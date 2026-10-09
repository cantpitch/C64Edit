using System.Text;

namespace C64Edit.Text;

public readonly record struct TextPos(int Line, int Col) : IComparable<TextPos>
{
    public int CompareTo(TextPos other) => Line != other.Line ? Line.CompareTo(other.Line) : Col.CompareTo(other.Col);
    public static bool operator <(TextPos a, TextPos b) => a.CompareTo(b) < 0;
    public static bool operator >(TextPos a, TextPos b) => a.CompareTo(b) > 0;
    public static bool operator <=(TextPos a, TextPos b) => a.CompareTo(b) <= 0;
    public static bool operator >=(TextPos a, TextPos b) => a.CompareTo(b) >= 0;
    public static TextPos Min(TextPos a, TextPos b) => a <= b ? a : b;
    public static TextPos Max(TextPos a, TextPos b) => a >= b ? a : b;
}

/// <summary>Raised after an edit: lines [StartLine, StartLine+RemovedLines) were replaced by InsertedLines lines.</summary>
public readonly record struct DocumentChange(int StartLine, int RemovedLines, int InsertedLines);

/// <summary>A single undoable edit: at <see cref="Start"/>, <see cref="Removed"/> was replaced by <see cref="Inserted"/>.</summary>
internal sealed class EditRecord
{
    public required TextPos Start;
    public required string Removed;
    public required string Inserted;
    public required TextPos CaretBefore;
    public required TextPos? AnchorBefore;
    public TextPos CaretAfter;
    public bool IsTyping;
    public DateTime Time;
    public int Id;
}

/// <summary>Line-based text buffer with undo/redo.</summary>
public sealed class TextDocument
{
    private readonly List<string> _lines = [""];
    private readonly List<EditRecord> _undo = [];
    private readonly List<EditRecord> _redo = [];
    private int _nextId = 1;
    private int _savedId;

    public event Action<DocumentChange>? Changed;

    public string NewLine { get; set; } = Environment.NewLine;
    public Encoding Encoding { get; set; } = new UTF8Encoding(false);

    public int LineCount => _lines.Count;
    public string this[int line] => _lines[line];
    public IReadOnlyList<string> Lines => _lines;

    /// <summary>Monotonic counter bumped by every change, for cheap cache invalidation.</summary>
    public int Version { get; private set; }

    public bool IsModified => CurrentId != _savedId;
    private int CurrentId => _undo.Count == 0 ? 0 : _undo[^1].Id;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void MarkSaved() => _savedId = CurrentId;

    public static TextDocument Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var doc = new TextDocument();
        Encoding encoding = new UTF8Encoding(false);
        int skip = 0;
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) { encoding = new UTF8Encoding(true); skip = 3; }
        else if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) { encoding = new UnicodeEncoding(false, true); skip = 2; }
        else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) { encoding = new UnicodeEncoding(true, true); skip = 2; }
        else
        {
            try { _ = new UTF8Encoding(false, true).GetCharCount(bytes); }
            catch (DecoderFallbackException) { encoding = Encoding.Latin1; }
        }

        var text = encoding.GetString(bytes, skip, bytes.Length - skip);
        doc.Encoding = encoding;
        int crlf = CountOf(text, "\r\n");
        int lf = CountOf(text, "\n") - crlf;
        doc.NewLine = crlf > lf ? "\r\n" : crlf == 0 && lf == 0 ? Environment.NewLine : "\n";
        doc.SetTextInternal(text);
        return doc;
    }

    private static int CountOf(string text, string value)
    {
        int count = 0, i = 0;
        while ((i = text.IndexOf(value, i, StringComparison.Ordinal)) >= 0) { count++; i += value.Length; }
        return count;
    }

    public void Save(string path)
    {
        var text = string.Join(NewLine, _lines);
        using var stream = File.Create(path);
        var preamble = Encoding.GetPreamble();
        stream.Write(preamble);
        stream.Write(Encoding.GetBytes(text));
        MarkSaved();
    }

    private void SetTextInternal(string text)
    {
        _lines.Clear();
        _lines.AddRange(SplitLines(text));
        Version++;
    }

    private static string[] SplitLines(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    public string GetAllText() => string.Join("\n", _lines);

    public TextPos Clamp(TextPos p)
    {
        int line = Math.Clamp(p.Line, 0, _lines.Count - 1);
        return new TextPos(line, Math.Clamp(p.Col, 0, _lines[line].Length));
    }

    public TextPos End => new(_lines.Count - 1, _lines[^1].Length);

    public string GetText(TextPos a, TextPos b)
    {
        var (s, e) = a <= b ? (a, b) : (b, a);
        if (s.Line == e.Line) return _lines[s.Line].Substring(s.Col, e.Col - s.Col);
        var sb = new StringBuilder();
        sb.Append(_lines[s.Line], s.Col, _lines[s.Line].Length - s.Col);
        for (int l = s.Line + 1; l < e.Line; l++) sb.Append('\n').Append(_lines[l]);
        sb.Append('\n').Append(_lines[e.Line], 0, e.Col);
        return sb.ToString();
    }

    /// <summary>Replaces the range [a,b) with <paramref name="text"/> and records it for undo. Returns the end of the inserted text.</summary>
    public TextPos Replace(TextPos a, TextPos b, string text, TextPos caretBefore, TextPos? anchorBefore, bool isTyping = false)
    {
        var (s, e) = a <= b ? (a, b) : (b, a);
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var removed = GetText(s, e);
        var end = ApplyRaw(s, e, text);

        _redo.Clear();
        var last = _undo.Count > 0 ? _undo[^1] : null;
        var now = DateTime.UtcNow;
        bool merge = isTyping && last is { IsTyping: true } && removed.Length == 0 && last.Removed.Length == 0
            && last.CaretAfter == s && last.Id != _savedId && (now - last.Time).TotalSeconds < 2
            && !text.Contains('\n') && !(text == " " && !last.Inserted.EndsWith(' '));
        if (merge)
        {
            last!.Inserted += text;
            last.CaretAfter = end;
            last.Time = now;
        }
        else
        {
            _undo.Add(new EditRecord
            {
                Start = s, Removed = removed, Inserted = text,
                CaretBefore = caretBefore, AnchorBefore = anchorBefore, CaretAfter = end,
                IsTyping = isTyping, Time = now, Id = _nextId++,
            });
        }
        return end;
    }

    /// <summary>Breaks typing coalescing so the next keystroke starts a new undo step.</summary>
    public void SealUndo()
    {
        if (_undo.Count > 0) _undo[^1].IsTyping = false;
    }

    /// <summary>Undoes the last edit; returns the caret/anchor to restore, or null if there was nothing to undo.</summary>
    public (TextPos caret, TextPos? anchor)? Undo()
    {
        if (_undo.Count == 0) return null;
        var rec = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        ApplyRaw(rec.Start, EndOf(rec.Start, rec.Inserted), rec.Removed);
        _redo.Add(rec);
        return (rec.CaretBefore, rec.AnchorBefore);
    }

    public TextPos? Redo()
    {
        if (_redo.Count == 0) return null;
        var rec = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        ApplyRaw(rec.Start, EndOf(rec.Start, rec.Removed), rec.Inserted);
        rec.IsTyping = false;
        _undo.Add(rec);
        return rec.CaretAfter;
    }

    private static TextPos EndOf(TextPos start, string text)
    {
        int nl = text.LastIndexOf('\n');
        if (nl < 0) return start with { Col = start.Col + text.Length };
        int lines = 0;
        foreach (var ch in text) if (ch == '\n') lines++;
        return new TextPos(start.Line + lines, text.Length - nl - 1);
    }

    private TextPos ApplyRaw(TextPos s, TextPos e, string text)
    {
        var prefix = _lines[s.Line][..s.Col];
        var suffix = _lines[e.Line][e.Col..];
        var parts = text.Split('\n');
        int removedLines = e.Line - s.Line + 1;

        var newLines = new string[parts.Length];
        for (int i = 0; i < parts.Length; i++) newLines[i] = parts[i];
        newLines[0] = prefix + newLines[0];
        var endPos = new TextPos(s.Line + parts.Length - 1, newLines[^1].Length);
        newLines[^1] += suffix;

        if (removedLines == newLines.Length)
        {
            for (int i = 0; i < newLines.Length; i++) _lines[s.Line + i] = newLines[i];
        }
        else
        {
            _lines.RemoveRange(s.Line, removedLines);
            _lines.InsertRange(s.Line, newLines);
        }
        Version++;
        Changed?.Invoke(new DocumentChange(s.Line, removedLines, newLines.Length));
        return endPos;
    }
}
