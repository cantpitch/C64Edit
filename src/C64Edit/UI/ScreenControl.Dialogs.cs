using Avalonia.Input;

namespace C64Edit.UI;

public sealed partial class ScreenControl
{
    private MessageDialog? _dialog;
    private InputDialog? _input;
    private readonly FindPanel _find = new();
    private readonly List<(int X, int Y, int W, int Index)> _buttonHits = [];

    // ================================================================ message box

    /// <summary>Shows an in-screen message box and returns the index of the chosen button.</summary>
    public Task<int> ShowMessageAsync(string title, string message, string[] buttons, int cancelIndex = -1)
    {
        _dialog?.Result.TrySetResult(_dialog.CancelIndex);
        CloseMenus();
        _dialog = new MessageDialog
        {
            Title = title,
            Lines = message.Split('\n'),
            Buttons = buttons,
            CancelIndex = cancelIndex < 0 ? buttons.Length - 1 : cancelIndex,
        };
        Invalidate();
        return _dialog.Result.Task;
    }

    private void CloseDialog(int result)
    {
        var d = _dialog;
        _dialog = null;
        Invalidate();
        d?.Result.TrySetResult(result);
    }

    /// <summary>Word-wraps text to a width in cells.</summary>
    private static List<string> Wrap(IEnumerable<string> lines, int width)
    {
        var result = new List<string>();
        foreach (var raw in lines)
        {
            var line = raw;
            if (line.Length == 0) { result.Add(""); continue; }
            while (line.Length > width)
            {
                int cut = line.LastIndexOf(' ', width);
                if (cut <= 0) cut = width;
                result.Add(line[..cut].TrimEnd());
                line = line[cut..].TrimStart();
            }
            result.Add(line);
        }
        return result;
    }

    /// <summary>Draws a reverse-video box with a title in its top edge; returns the inner origin and size in pixels/cells.</summary>
    private (int X, int Y, int Inner) DrawBox(string title, int innerWidth, int innerRows)
    {
        uint fg = BackColor, bg = TextColor;
        innerWidth = Math.Min(innerWidth, Math.Max(4, _cols - 2));
        int boxW = innerWidth + 2;
        int boxH = innerRows + 2;
        int col = Math.Max(0, (_cols - boxW) / 2);
        int row = Math.Max(0, (_rows - boxH) / 2);
        int x = _textLeft + col * CellW;
        int y = _textTop + row * CellH;

        var titleText = title.Length > innerWidth - 2 ? title[..(innerWidth - 2)] : title;
        var top = "┌" + titleText.PadRight(innerWidth, '─') + "┐";
        DrawText(x, y, top, fg, bg);
        for (int r = 0; r < innerRows; r++)
        {
            DrawText(x, y + (r + 1) * CellH, "│" + new string(' ', innerWidth) + "│", fg, bg);
        }
        DrawText(x, y + (innerRows + 1) * CellH, "└" + new string('─', innerWidth) + "┘", fg, bg);
        return (x + CellW, y + CellH, innerWidth);
    }

    private void DrawButtons(int x, int y, int inner, string[] buttons, int selected)
    {
        _buttonHits.Clear();
        var labels = buttons.Select(b => " " + b + " ").ToArray();
        int total = labels.Sum(l => l.Length) + labels.Length - 1;
        int bx = x + Math.Max(0, (inner - total) / 2) * CellW;
        for (int i = 0; i < labels.Length; i++)
        {
            bool sel = i == selected;
            DrawText(bx, y, labels[i], sel ? TextColor : BackColor, sel ? BackColor : TextColor);
            _buttonHits.Add((bx, y, labels[i].Length * CellW, i));
            bx += (labels[i].Length + 1) * CellW;
        }
    }

    private void DrawMessageDialog(MessageDialog d)
    {
        int maxInner = Math.Max(10, _cols - 4);
        int buttonsW = d.Buttons.Sum(b => b.Length + 2) + d.Buttons.Length - 1;
        int want = Math.Max(Math.Max(d.Title.Length + 4, buttonsW + 2), d.Lines.Max(l => l.Length) + 2);
        int inner = Math.Min(maxInner, want);
        var lines = Wrap(d.Lines, inner - 2);
        int maxLines = Math.Max(1, _rows - 6);
        if (lines.Count > maxLines) lines = lines.Take(maxLines).ToList();
        var (x, y, w) = DrawBox(d.Title, inner, lines.Count + 3);
        for (int i = 0; i < lines.Count; i++)
            DrawText(x + CellW, y + (i + 1) * CellH, lines[i], BackColor, TextColor, w - 2);
        DrawButtons(x, y + (lines.Count + 2) * CellH, w, d.Buttons, d.Selected);
    }

    private bool DialogKeyDown(KeyEventArgs e)
    {
        if (_dialog is { } d)
        {
            switch (e.Key)
            {
                case Key.Left:
                case Key.Up:
                    d.Selected = (d.Selected - 1 + d.Buttons.Length) % d.Buttons.Length;
                    break;
                case Key.Right:
                case Key.Down:
                case Key.Tab:
                    d.Selected = (d.Selected + (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? d.Buttons.Length - 1 : 1)) % d.Buttons.Length;
                    break;
                case Key.Enter:
                case Key.Space:
                    CloseDialog(d.Selected);
                    return true;
                case Key.Escape:
                    CloseDialog(d.CancelIndex);
                    return true;
                default:
                    // First letter of a button picks it, like classic text-mode dialogs.
                    if (e.Key >= Key.A && e.Key <= Key.Z && e.KeyModifiers == KeyModifiers.None)
                    {
                        char letter = (char)('A' + (e.Key - Key.A));
                        int idx = Array.FindIndex(d.Buttons, b => b.StartsWith(letter));
                        if (idx >= 0) { CloseDialog(idx); return true; }
                    }
                    break;
            }
            Invalidate();
            return true;
        }

        if (_input is { } inp)
        {
            switch (e.Key)
            {
                case Key.Enter:
                    CloseInput(inp.Field.Text);
                    return true;
                case Key.Escape:
                    CloseInput(null);
                    return true;
                default:
                    if (!FieldKeyDown(inp.Field, e)) return true;
                    break;
            }
            Invalidate();
            return true;
        }
        return false;
    }

    private bool DialogPointerPressed(int mx, int my)
    {
        if (_dialog is null && _input is null) return false;
        foreach (var (x, y, w, index) in _buttonHits)
        {
            if (mx >= x && mx < x + w && my >= y && my < y + CellH)
            {
                if (_dialog is not null) CloseDialog(index);
                else if (_input is not null) CloseInput(index == 0 ? _input.Field.Text : null);
                return true;
            }
        }
        return true; // modal: swallow clicks elsewhere
    }

    // ================================================================ input box

    public Task<string?> ShowInputAsync(string title, string prompt, string initial = "")
    {
        _input?.Result.TrySetResult(null);
        CloseMenus();
        _input = new InputDialog { Title = title, Prompt = prompt, Field = new LineEditor(initial) };
        Invalidate();
        return _input.Result.Task;
    }

    private void CloseInput(string? result)
    {
        var d = _input;
        _input = null;
        Invalidate();
        d?.Result.TrySetResult(result);
    }

    private void DrawInputDialog(InputDialog d)
    {
        int inner = Math.Min(Math.Max(10, _cols - 4), Math.Max(d.Title.Length + 4, 30));
        var (x, y, w) = DrawBox(d.Title, inner, 6);
        DrawText(x + CellW, y + CellH, d.Prompt, BackColor, TextColor, w - 2);
        DrawField(x + CellW, y + 2 * CellH, w - 2, d.Field, true);
        DrawButtons(x, y + 4 * CellH, w, ["OK", "CANCEL"], -1);
    }

    /// <summary>A text field: normal video on the reversed dialog, with its own blinking cursor.</summary>
    private void DrawField(int x, int y, int width, LineEditor field, bool focused)
    {
        int scroll = field.ScrollFor(width);
        var visible = field.Text.Length > scroll ? field.Text[scroll..] : "";
        if (visible.Length > width) visible = visible[..width];
        uint fg = field.SelectAllOnType && focused ? BackColor : TextColor;
        uint bg = field.SelectAllOnType && focused ? TextColor : BackColor;
        _fb.FillRect(x, y, width * CellW, CellH, BackColor);
        DrawText(x, y, visible, fg, bg);
        if (focused && CursorOn)
        {
            int cx = field.Caret - scroll;
            if (cx >= 0 && cx < width)
            {
                char under = field.Caret < field.Text.Length ? field.Text[field.Caret] : ' ';
                DrawCell(x + cx * CellW, y, under, BackColor, TextColor);
            }
        }
    }

    /// <summary>Editing keys shared by all one-line fields. Returns false when the key was not handled.</summary>
    private bool FieldKeyDown(LineEditor field, KeyEventArgs e)
    {
        var cmd = CommandModifier;
        switch (e.Key)
        {
            case Key.Left: field.Left(); break;
            case Key.Right: field.Right(); break;
            case Key.Home: field.Home(); break;
            case Key.End: field.End(); break;
            case Key.Back: field.Backspace(); break;
            case Key.Delete: field.Delete(); break;
            case Key.V when e.KeyModifiers.HasFlag(cmd):
                _ = PasteIntoFieldAsync(field);
                break;
            case Key.A when e.KeyModifiers.HasFlag(cmd):
                field.Set(field.Text);
                break;
            default:
                return false;
        }
        ResetBlink();
        return true;
    }

    private async Task PasteIntoFieldAsync(LineEditor field)
    {
        var text = await GetClipboardTextAsync();
        if (string.IsNullOrEmpty(text)) return;
        field.Insert(text.Split('\n')[0]);
        Invalidate();
    }

    /// <summary>Routes typed characters to whichever field currently has focus. Returns true if one did.</summary>
    private bool FieldTextInput(string text)
    {
        LineEditor? target = _input?.Field ?? (_find.Visible && _dialog is null ? _find.Focused : null);
        if (target is null) return _dialog is not null;
        target.Insert(text);
        if (target == _find.Find && !_find.ReplaceMode) IncrementalFind();
        ResetBlink();
        Invalidate();
        return true;
    }

    // ================================================================ find / replace

    public void ShowFind(bool replace)
    {
        CloseMenus();
        _find.Visible = true;
        _find.ReplaceMode = replace;
        _find.Focus = 0;
        _find.Message = "";
        var tab = ActiveTab;
        if (tab.HasSelection && tab.SelectionStart.Line == tab.SelectionEnd.Line)
            _find.Find.Set(tab.SelectedText);
        else
            _find.Find.Set(_find.Find.Text);
        EnsureCaretVisible();
        Invalidate();
    }

    private void HideFind()
    {
        _find.Visible = false;
        _find.Message = "";
        EnsureCaretVisible();
        Invalidate();
    }

    private void IncrementalFind()
    {
        var tab = ActiveTab;
        _find.Message = tab.Find(_find.Find.Text, true, fromSelectionStart: true) || _find.Find.Text.Length == 0 ? "" : "?NOT FOUND ERROR";
        EnsureCaretVisible();
    }

    public void FindNext(bool forward)
    {
        CloseMenus();
        if (_find.Find.Text.Length == 0) { ShowFind(false); return; }
        _find.Message = ActiveTab.Find(_find.Find.Text, forward) ? "" : "?NOT FOUND ERROR";
        if (_find.Message.Length > 0 && !_find.Visible) _ = ShowMessageAsync("FIND", "?NOT FOUND ERROR\n\n\"" + _find.Find.Text + "\"", ["OK"]);
        EnsureCaretVisible();
        Invalidate();
    }

    private int FindPanelTop => _textTop + (_rows - _find.Rows) * CellH;

    private void DrawFindPanel()
    {
        int y = FindPanelTop;
        int labelW = 6;
        int fieldW = Math.Max(4, _cols - labelW - 1);
        _fb.FillRect(_textLeft, y, _cols * CellW, _find.Rows * CellH, TextColor);
        DrawText(_textLeft, y, " FIND ", BackColor, TextColor);
        DrawField(_textLeft + labelW * CellW, y, fieldW, _find.Find, _find.Focus == 0 && _dialog is null);
        if (_find.ReplaceMode)
        {
            DrawText(_textLeft, y + CellH, " REPL ", BackColor, TextColor);
            DrawField(_textLeft + labelW * CellW, y + CellH, fieldW, _find.Replace, _find.Focus == 1 && _dialog is null);
        }
    }

    private bool FindKeyDown(KeyEventArgs e)
    {
        if (!_find.Visible) return false;
        var tab = ActiveTab;
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        bool cmd = e.KeyModifiers.HasFlag(CommandModifier);
        switch (e.Key)
        {
            case Key.Escape:
                HideFind();
                return true;
            case Key.Tab:
            case Key.Up when _find.ReplaceMode:
            case Key.Down when _find.ReplaceMode:
                if (_find.ReplaceMode) _find.Focus = 1 - _find.Focus;
                break;
            case Key.Enter when _find.ReplaceMode && cmd:
                int n = tab.ReplaceAll(_find.Find.Text, _find.Replace.Text);
                _find.Message = n == 0 ? "?NOT FOUND ERROR" : $"{n} REPLACED";
                break;
            case Key.Enter when _find.ReplaceMode && !shift:
                _find.Message = tab.ReplaceCurrent(_find.Find.Text, _find.Replace.Text) ? "" : "?NOT FOUND ERROR";
                break;
            case Key.Enter:
                _find.Message = tab.Find(_find.Find.Text, !shift) ? "" : "?NOT FOUND ERROR";
                break;
            case Key.F3:
                _find.Message = tab.Find(_find.Find.Text, !shift) ? "" : "?NOT FOUND ERROR";
                break;
            default:
                if (FieldKeyDown(_find.Focused, e))
                {
                    if (_find.Focus == 0 && !_find.ReplaceMode && e.Key is Key.Back or Key.Delete) IncrementalFind();
                    break;
                }
                return false; // let editor-wide shortcuts (save, undo...) through
        }
        EnsureCaretVisible();
        ResetBlink();
        Invalidate();
        return true;
    }

    private bool FindPointerPressed(int mx, int my)
    {
        if (!_find.Visible) return false;
        int top = FindPanelTop;
        if (my < top || my >= top + _find.Rows * CellH || mx < _textLeft || mx >= _textLeft + _cols * CellW) return false;
        int row = (my - top) / CellH;
        _find.Focus = _find.ReplaceMode ? row : 0;
        var field = _find.Focused;
        int col = (mx - _textLeft) / CellW - 6;
        if (col >= 0) field.PlaceCaret(field.ScrollFor(_cols - 7) + col);
        ResetBlink();
        Invalidate();
        return true;
    }
}
