using C64Edit.Syntax;

namespace C64Edit.Text;

/// <summary>
/// One open file: the document plus caret, selection, scroll position and the visual (soft-wrap) layout.
/// "Display columns" are character cells after tab expansion.
/// </summary>
public sealed class EditorTab
{
    private static int _untitledCounter;

    private readonly List<int[]?> _segments = [];
    private int[] _rowStart = [];
    private bool _rowsDirty = true;
    private int _wrapWidth;
    private int _tabSize = 4;

    public TextDocument Document { get; }
    public Highlighter Highlighter { get; }
    public string? FilePath { get; private set; }
    public string DisplayName { get; private set; }

    public TextPos Caret { get; private set; }
    public TextPos? Anchor { get; private set; }
    /// <summary>Remembered x position for vertical caret movement, in display columns within a visual row.</summary>
    private int _preferredX = -1;

    public int ScrollRow { get; set; }
    public int ScrollCol { get; set; }

    public bool UseTabs { get; set; }
    public bool Overwrite { get; set; }

    /// <summary>Time the tab label started marquee-scrolling (reset when the tab is created).</summary>
    public DateTime Created { get; } = DateTime.UtcNow;

    public EditorTab(TextDocument document, string? path)
    {
        Document = document;
        FilePath = path;
        DisplayName = path is null ? $"UNTITLED{(++_untitledCounter > 1 ? _untitledCounter.ToString() : "")}" : Path.GetFileName(path);
        Highlighter = new Highlighter(document, Languages.ForPath(path));
        UseTabs = document.Lines.Take(500).Count(l => l.StartsWith('\t')) > document.Lines.Take(500).Count(l => l.StartsWith("  "));
        document.Changed += OnDocumentChanged;
        ResetSegments();
    }

    public static EditorTab Open(string path) => new(TextDocument.Load(path), path);

    public void SaveAs(string path)
    {
        Document.Save(path);
        FilePath = path;
        DisplayName = Path.GetFileName(path);
        Highlighter.SetLanguage(Languages.ForPath(path));
    }

    public bool HasSelection => Anchor is { } a && a != Caret;
    public TextPos SelectionStart => Anchor is { } a ? TextPos.Min(a, Caret) : Caret;
    public TextPos SelectionEnd => Anchor is { } a ? TextPos.Max(a, Caret) : Caret;
    public string SelectedText => HasSelection ? Document.GetText(SelectionStart, SelectionEnd) : "";

    // ================================================================ layout

    public int TabSize
    {
        get => _tabSize;
        set { if (_tabSize != value) { _tabSize = Math.Max(1, value); ResetSegments(); } }
    }

    /// <summary>Soft-wrap width in display columns, or 0 for no wrapping.</summary>
    public int WrapWidth
    {
        get => _wrapWidth;
        set { value = Math.Max(0, value); if (_wrapWidth != value) { _wrapWidth = value; ResetSegments(); } }
    }

    private void ResetSegments()
    {
        _segments.Clear();
        _segments.AddRange(new int[]?[Document.LineCount]);
        _rowsDirty = true;
    }

    private void OnDocumentChanged(DocumentChange c)
    {
        _segments.RemoveRange(c.StartLine, c.RemovedLines);
        _segments.InsertRange(c.StartLine, new int[]?[c.InsertedLines]);
        _rowsDirty = true;
    }

    public int NextTabStop(int col) => (col / _tabSize + 1) * _tabSize;

    /// <summary>Display column of character index <paramref name="idx"/> in a line.</summary>
    public int DisplayCol(string line, int idx)
    {
        int col = 0;
        for (int i = 0; i < idx && i < line.Length; i++)
            col = line[i] == '\t' ? NextTabStop(col) : col + 1;
        return col;
    }

    /// <summary>Character indices where each visual row of a line begins (always starts with 0).</summary>
    public int[] Segments(int line)
    {
        if (_wrapWidth <= 0) return [0];
        return _segments[line] ??= ComputeSegments(Document[line]);
    }

    private int[] ComputeSegments(string text)
    {
        var starts = new List<int> { 0 };
        int segStart = 0, segStartCol = 0, col = 0, lastBreak = -1;
        for (int i = 0; i < text.Length; i++)
        {
            int next = text[i] == '\t' ? NextTabStop(col) : col + 1;
            if (next - segStartCol > _wrapWidth && i > segStart)
            {
                int breakAt = lastBreak > segStart ? lastBreak : i;
                starts.Add(breakAt);
                segStart = breakAt;
                segStartCol = DisplayCol(text, breakAt);
                lastBreak = -1;
                // Re-measure the current character against the new row.
                col = DisplayCol(text, i);
                next = text[i] == '\t' ? NextTabStop(col) : col + 1;
            }
            if (text[i] == ' ' || text[i] == '\t') lastBreak = i + 1;
            col = next;
        }
        return starts.ToArray();
    }

    private void EnsureRows()
    {
        if (!_rowsDirty) return;
        int count = Document.LineCount;
        if (_rowStart.Length != count + 1) _rowStart = new int[count + 1];
        int row = 0;
        for (int l = 0; l < count; l++)
        {
            _rowStart[l] = row;
            row += _wrapWidth > 0 ? Segments(l).Length : 1;
        }
        _rowStart[count] = row;
        _rowsDirty = false;
    }

    public int VisualRowCount
    {
        get { if (_wrapWidth <= 0) return Document.LineCount; EnsureRows(); return _rowStart[^1]; }
    }

    public int FirstRowOfLine(int line)
    {
        if (_wrapWidth <= 0) return line;
        EnsureRows();
        return _rowStart[line];
    }

    /// <summary>Maps a visual row to (line, segment start index, segment end index, display col of segment start).</summary>
    public (int Line, int Start, int End, int StartCol) RowInfo(int row)
    {
        if (_wrapWidth <= 0)
        {
            int l = Math.Clamp(row, 0, Document.LineCount - 1);
            return (l, 0, Document[l].Length, 0);
        }
        EnsureRows();
        row = Math.Clamp(row, 0, _rowStart[^1] - 1);
        int lo = 0, hi = Document.LineCount - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (_rowStart[mid] <= row) lo = mid; else hi = mid - 1;
        }
        var segs = Segments(lo);
        int s = row - _rowStart[lo];
        int start = segs[s];
        int end = s + 1 < segs.Length ? segs[s + 1] : Document[lo].Length;
        return (lo, start, end, DisplayCol(Document[lo], start));
    }

    /// <summary>Visual row and x (display column relative to the row start) of a text position.</summary>
    public (int Row, int X) VisualOf(TextPos p)
    {
        var line = Document[p.Line];
        if (_wrapWidth <= 0) return (p.Line, DisplayCol(line, p.Col));
        var segs = Segments(p.Line);
        int s = segs.Length - 1;
        while (s > 0 && segs[s] > p.Col) s--;
        return (FirstRowOfLine(p.Line) + s, DisplayCol(line, p.Col) - DisplayCol(line, segs[s]));
    }

    /// <summary>Text position nearest to display column <paramref name="x"/> on visual row <paramref name="row"/>.</summary>
    public TextPos PosFromVisual(int row, int x)
    {
        if (row < 0) return new TextPos(0, 0);
        if (row >= VisualRowCount) return Document.End;
        var (lineIdx, start, end, startCol) = RowInfo(row);
        var line = Document[lineIdx];
        int target = startCol + Math.Max(0, x);
        int col = startCol;
        for (int i = start; i < end; i++)
        {
            int next = line[i] == '\t' ? NextTabStop(col) : col + 1;
            if (target < next) return new TextPos(lineIdx, target - col > (next - col) / 2 && line[i] == '\t' ? i + 1 : i);
            col = next;
        }
        // Past the end of a wrapped row: stay on this row (just before the next segment's first char).
        bool isLastSegment = end == line.Length;
        return new TextPos(lineIdx, isLastSegment ? end : Math.Max(start, end - 1));
    }

    // ================================================================ caret & selection

    public void SetCaret(TextPos pos, bool extend, bool keepPreferredX = false)
    {
        pos = Document.Clamp(pos);
        if (extend) Anchor ??= Caret;
        else Anchor = null;
        Caret = pos;
        if (!keepPreferredX) _preferredX = -1;
        Document.SealUndo();
    }

    public void Select(TextPos anchor, TextPos caret)
    {
        Anchor = Document.Clamp(anchor);
        Caret = Document.Clamp(caret);
        _preferredX = -1;
        Document.SealUndo();
    }

    public void SelectAll() => Select(new TextPos(0, 0), Document.End);

    public void ClearSelection() => Anchor = null;

    public void MoveLeft(bool extend)
    {
        if (HasSelection && !extend) { SetCaret(SelectionStart, false); return; }
        var p = Caret;
        if (p.Col > 0)
        {
            int step = p.Col >= 2 && char.IsLowSurrogate(Document[p.Line][p.Col - 1]) ? 2 : 1;
            SetCaret(p with { Col = p.Col - step }, extend);
        }
        else if (p.Line > 0) SetCaret(new TextPos(p.Line - 1, Document[p.Line - 1].Length), extend);
        else SetCaret(p, extend);
    }

    public void MoveRight(bool extend)
    {
        if (HasSelection && !extend) { SetCaret(SelectionEnd, false); return; }
        var p = Caret;
        var line = Document[p.Line];
        if (p.Col < line.Length)
        {
            int step = char.IsHighSurrogate(line[p.Col]) && p.Col + 1 < line.Length ? 2 : 1;
            SetCaret(p with { Col = p.Col + step }, extend);
        }
        else if (p.Line < Document.LineCount - 1) SetCaret(new TextPos(p.Line + 1, 0), extend);
        else SetCaret(p, extend);
    }

    public void MoveVertical(int rows, bool extend)
    {
        if (HasSelection && !extend && rows != 0)
            SetCaret(rows < 0 ? SelectionStart : SelectionEnd, false);
        var (row, x) = VisualOf(Caret);
        if (_preferredX < 0) _preferredX = x;
        int target = row + rows;
        TextPos pos;
        if (target < 0) pos = new TextPos(0, 0);
        else if (target >= VisualRowCount) pos = Document.End;
        else pos = PosFromVisual(target, _preferredX);
        SetCaret(pos, extend, keepPreferredX: target >= 0 && target < VisualRowCount);
    }

    /// <summary>Smart Home: toggles between the first non-blank character and column 0 (per visual row when wrapping).</summary>
    public void Home(bool extend)
    {
        var (row, _) = VisualOf(Caret);
        var (lineIdx, start, _, _) = RowInfo(row);
        if (start > 0) { SetCaret(new TextPos(lineIdx, start), extend); return; }
        var line = Document[lineIdx];
        int firstNonBlank = 0;
        while (firstNonBlank < line.Length && char.IsWhiteSpace(line[firstNonBlank])) firstNonBlank++;
        SetCaret(new TextPos(lineIdx, Caret.Col == firstNonBlank ? 0 : firstNonBlank), extend);
    }

    public void End(bool extend)
    {
        var (row, _) = VisualOf(Caret);
        var (lineIdx, _, end, _) = RowInfo(row);
        bool last = end == Document[lineIdx].Length;
        SetCaret(new TextPos(lineIdx, last ? end : Math.Max(0, end - 1)), extend);
    }

    private static int CharClass(char c) => char.IsWhiteSpace(c) ? 0 : char.IsLetterOrDigit(c) || c == '_' ? 1 : 2;

    public TextPos WordLeft(TextPos p)
    {
        if (p.Col == 0) return p.Line > 0 ? new TextPos(p.Line - 1, Document[p.Line - 1].Length) : p;
        var line = Document[p.Line];
        int i = p.Col;
        while (i > 0 && char.IsWhiteSpace(line[i - 1])) i--;
        if (i > 0)
        {
            int cls = CharClass(line[i - 1]);
            while (i > 0 && CharClass(line[i - 1]) == cls) i--;
        }
        return p with { Col = i };
    }

    public TextPos WordRight(TextPos p)
    {
        var line = Document[p.Line];
        if (p.Col >= line.Length) return p.Line < Document.LineCount - 1 ? new TextPos(p.Line + 1, 0) : p;
        int i = p.Col;
        int cls = CharClass(line[i]);
        while (i < line.Length && CharClass(line[i]) == cls) i++;
        while (i < line.Length && char.IsWhiteSpace(line[i])) i++;
        return p with { Col = i };
    }

    public (TextPos, TextPos) WordAt(TextPos p)
    {
        var line = Document[p.Line];
        if (line.Length == 0) return (p, p);
        int i = Math.Min(p.Col, line.Length - 1);
        int cls = CharClass(line[i]);
        int s = i, e = i;
        while (s > 0 && CharClass(line[s - 1]) == cls) s--;
        while (e < line.Length && CharClass(line[e]) == cls) e++;
        return (p with { Col = s }, p with { Col = e });
    }

    // ================================================================ editing

    private void ReplaceSelection(string text, bool typing = false)
    {
        var caretBefore = Caret;
        var anchorBefore = Anchor;
        var end = Document.Replace(SelectionStart, SelectionEnd, text, caretBefore, anchorBefore, typing);
        Anchor = null;
        Caret = end;
        _preferredX = -1;
    }

    public void TypeText(string text)
    {
        if (Overwrite && !HasSelection && text.Length == 1 && Caret.Col < Document[Caret.Line].Length)
        {
            Anchor = Caret;
            Caret = Caret with { Col = Caret.Col + 1 };
        }
        ReplaceSelection(text, typing: true);
    }

    public void Paste(string text) => ReplaceSelection(text);

    public void Backspace(bool word)
    {
        if (!HasSelection)
        {
            if (Caret == new TextPos(0, 0)) return;
            var line = Document[Caret.Line];
            TextPos from;
            if (word) from = WordLeft(Caret);
            else if (!UseTabs && Caret.Col > 0 && line[..Caret.Col].All(c => c == ' '))
            {
                // Delete back to the previous indentation stop.
                int col = Caret.Col;
                int stop = (col - 1) / _tabSize * _tabSize;
                from = Caret with { Col = stop };
            }
            else
            {
                Anchor = null;
                var save = Caret;
                MoveLeft(false);
                from = Caret;
                Caret = save;
            }
            Anchor = from;
        }
        ReplaceSelection("");
    }

    public void Delete(bool word)
    {
        if (!HasSelection)
        {
            if (Caret == Document.End) return;
            var save = Caret;
            if (word) Caret = WordRight(Caret);
            else MoveRight(false);
            Anchor = save;
        }
        ReplaceSelection("");
    }

    public void NewLine()
    {
        var line = Document[Caret.Line];
        int indentLen = 0;
        while (indentLen < line.Length && indentLen < Caret.Col && line[indentLen] is ' ' or '\t') indentLen++;
        var indent = line[..indentLen];
        // One extra level after an opening bracket.
        var before = line[..Caret.Col].TrimEnd();
        if (before.EndsWith('{') || before.EndsWith('[') || before.EndsWith('(') || before.EndsWith(':') && Highlighter.Language == Languages.Python)
            indent += IndentUnit;
        Document.SealUndo();
        ReplaceSelection("\n" + indent);
        Document.SealUndo();
    }

    private string IndentUnit => UseTabs ? "\t" : new string(' ', _tabSize);

    public void Tab(bool outdent)
    {
        if (!HasSelection || SelectionStart.Line == SelectionEnd.Line)
        {
            if (outdent) { IndentLines(Caret.Line, Caret.Line, false); return; }
            if (UseTabs) { ReplaceSelection("\t"); return; }
            int col = DisplayCol(Document[Caret.Line], SelectionStart.Col);
            ReplaceSelection(new string(' ', NextTabStop(col) - col));
            return;
        }
        int last = SelectionEnd.Col == 0 ? SelectionEnd.Line - 1 : SelectionEnd.Line;
        IndentLines(SelectionStart.Line, last, !outdent);
    }

    private void IndentLines(int first, int last, bool indent)
    {
        var caretBefore = Caret;
        var anchorBefore = Anchor;
        var lines = new List<string>();
        int caretDelta = 0, anchorDelta = 0;
        for (int l = first; l <= last; l++)
        {
            var text = Document[l];
            int delta;
            if (indent)
            {
                text = text.Length == 0 ? text : IndentUnit + text;
                delta = text.Length - Document[l].Length;
            }
            else
            {
                int remove = 0;
                if (text.StartsWith('\t')) remove = 1;
                else while (remove < _tabSize && remove < text.Length && text[remove] == ' ') remove++;
                text = text[remove..];
                delta = -remove;
            }
            if (l == Caret.Line) caretDelta = delta;
            if (Anchor is { } a && l == a.Line) anchorDelta = delta;
            lines.Add(text);
        }
        var start = new TextPos(first, 0);
        var end = new TextPos(last, Document[last].Length);
        Document.Replace(start, end, string.Join('\n', lines), caretBefore, anchorBefore);
        Caret = Document.Clamp(Caret with { Col = Math.Max(0, Caret.Col + caretDelta) });
        if (Anchor is { } an) Anchor = Document.Clamp(an with { Col = Math.Max(0, an.Col + anchorDelta) });
        Document.SealUndo();
    }

    public void Undo()
    {
        if (Document.Undo() is { } r)
        {
            Caret = Document.Clamp(r.caret);
            Anchor = r.anchor is { } a ? Document.Clamp(a) : null;
            _preferredX = -1;
        }
    }

    public void Redo()
    {
        if (Document.Redo() is { } c)
        {
            Caret = Document.Clamp(c);
            Anchor = null;
            _preferredX = -1;
        }
    }

    // ================================================================ search

    public bool Find(string query, bool forward, bool fromSelectionStart = false)
    {
        if (query.Length == 0) return false;
        var cmp = query.Any(char.IsUpper) ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var text = Document.GetAllText();
        int caretOffset = OffsetOf(forward ? (fromSelectionStart ? SelectionStart : SelectionEnd) : SelectionStart);
        int idx;
        if (forward)
        {
            idx = text.IndexOf(query, Math.Min(caretOffset, text.Length), cmp);
            if (idx < 0) idx = text.IndexOf(query, 0, cmp);
        }
        else
        {
            // LastIndexOf(value, startIndex) only finds matches that end at or before startIndex.
            idx = caretOffset > 0 ? text.LastIndexOf(query, caretOffset - 1, cmp) : -1;
            if (idx < 0) idx = text.LastIndexOf(query, cmp);
        }
        if (idx < 0) return false;
        Select(PosOf(idx), PosOf(idx + query.Length));
        return true;
    }

    public bool SelectionMatches(string query)
    {
        if (!HasSelection || query.Length == 0) return false;
        var cmp = query.Any(char.IsUpper) ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return string.Equals(SelectedText, query, cmp);
    }

    public bool ReplaceCurrent(string query, string replacement)
    {
        if (!SelectionMatches(query)) return Find(query, true, fromSelectionStart: true);
        ReplaceSelection(replacement);
        Document.SealUndo();
        Find(query, true);
        return true;
    }

    public int ReplaceAll(string query, string replacement)
    {
        if (query.Length == 0) return 0;
        var cmp = query.Any(char.IsUpper) ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var text = Document.GetAllText();
        int count = 0, i = 0;
        var sb = new System.Text.StringBuilder();
        while (true)
        {
            int idx = text.IndexOf(query, i, cmp);
            if (idx < 0) break;
            sb.Append(text, i, idx - i).Append(replacement);
            i = idx + query.Length;
            count++;
        }
        if (count == 0) return 0;
        sb.Append(text, i, text.Length - i);
        var caretBefore = Caret;
        Document.Replace(new TextPos(0, 0), Document.End, sb.ToString(), caretBefore, Anchor);
        Anchor = null;
        Caret = Document.Clamp(caretBefore);
        Document.SealUndo();
        return count;
    }

    private int OffsetOf(TextPos p)
    {
        int offset = 0;
        for (int l = 0; l < p.Line; l++) offset += Document[l].Length + 1;
        return offset + p.Col;
    }

    private TextPos PosOf(int offset)
    {
        for (int l = 0; l < Document.LineCount; l++)
        {
            int len = Document[l].Length;
            if (offset <= len) return new TextPos(l, offset);
            offset -= len + 1;
        }
        return Document.End;
    }

    public void GoToLine(int line)
    {
        SetCaret(new TextPos(Math.Clamp(line - 1, 0, Document.LineCount - 1), 0), false);
    }
}
