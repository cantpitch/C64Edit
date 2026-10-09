using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using C64Edit.Text;

namespace C64Edit.UI;

public sealed partial class ScreenControl
{
    private bool _altPending;           // Alt/Option went down with nothing else: a tap toggles the menu
    private bool _suppressTextInput;    // the key that produced the next TextInput was already consumed
    private bool _dragging;
    private int _dragUnit;              // 1 = characters, 2 = words, 3 = lines
    private TextPos _dragStart, _dragEnd;
    private bool _thumbDragging;
    private int _thumbGrab;
    private int _lastMx, _lastMy;
    private double _wheelY, _wheelX;

    private static readonly Cursor IBeam = new(StandardCursorType.Ibeam);
    private static readonly Cursor Arrow = new(StandardCursorType.Arrow);

    /// <summary>Cmd on macOS, Ctrl elsewhere.</summary>
    private static KeyModifiers CommandModifier => IsMac ? KeyModifiers.Meta : KeyModifiers.Control;

    private (int X, int Y) ToMachine(Point p) => ((int)Math.Floor(p.X / _pixelSize), (int)Math.Floor(p.Y / _pixelSize));

    // ================================================================ keyboard

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        _suppressTextInput = false;
        ResetBlink();

        if (e.Key is Key.LeftAlt or Key.RightAlt)
        {
            _altPending = (e.KeyModifiers & ~KeyModifiers.Alt) == KeyModifiers.None;
            return;
        }
        _altPending = false;
        if (IsModifierKey(e.Key)) return;

        if (_splash)
        {
            _splash = false;
            Invalidate();
        }

        e.Handled = true;
        if (_dialog is not null)
        {
            DialogKeyDown(e);
            _suppressTextInput = ProducesText(e);
            return;
        }
        if (_input is not null)
        {
            DialogKeyDown(e);
            return;
        }
        if (e.Key == Key.F10 && e.KeyModifiers == KeyModifiers.None)
        {
            ToggleMenuKeyboard();
            return;
        }
        if (MenuKeyDown(e))
        {
            _suppressTextInput = ProducesText(e);
            return;
        }
        if (!IsMac && e.KeyModifiers == KeyModifiers.Alt && e.Key >= Key.A && e.Key <= Key.Z)
        {
            char letter = (char)('A' + (e.Key - Key.A));
            int m = Menus.FindIndex(x => x.Mnemonic == letter);
            if (m >= 0)
            {
                _menuKeyboard = true;
                OpenMenu(m, true);
                return;
            }
        }
        if (FindKeyDown(e)) return;
        if (Shortcut(e)) return;
        if (_find.Visible && e.Key is Key.Left or Key.Right or Key.Home or Key.End or Key.Back or Key.Delete) return;
        if (EditorKeyDown(e)) return;
        e.Handled = false;
    }

    /// <summary>
    /// Whether a consumed key will also arrive as a TextInput event (so it must not be typed as well):
    /// plain or shifted printable keys. Command/Ctrl combinations never produce text input.
    /// </summary>
    private static bool ProducesText(KeyEventArgs e) =>
        (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) == 0
        && e.Key is (>= Key.A and <= Key.Z) or (>= Key.D0 and <= Key.D9) or Key.Space
            or (>= Key.OemSemicolon and <= Key.Oem102) or (>= Key.NumPad0 and <= Key.Divide);

    private static bool IsModifierKey(Key key) => key is Key.LeftShift or Key.RightShift or Key.LeftCtrl or Key.RightCtrl
        or Key.LWin or Key.RWin or Key.LeftAlt or Key.RightAlt or Key.CapsLock;

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.Key is Key.LeftAlt or Key.RightAlt && _altPending)
        {
            _altPending = false;
            if (_dialog is null && _input is null) ToggleMenuKeyboard();
            e.Handled = true;
        }
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (string.IsNullOrEmpty(e.Text)) return;
        e.Handled = true;
        _altPending = false;
        if (_suppressTextInput)
        {
            _suppressTextInput = false;
            return;
        }
        var text = new string(e.Text.Where(c => c >= ' ' && c != '\u007F').ToArray());
        if (text.Length == 0) return;
        if (_splash) _splash = false;
        if (FieldTextInput(text)) return;
        if (_menuKeyboard || _openMenu >= 0) return;
        Edit(t => t.TypeText(text));
    }

    /// <summary>Application shortcuts. Returns true when the key was a shortcut.</summary>
    private bool Shortcut(KeyEventArgs e)
    {
        var mods = e.KeyModifiers;
        bool cmd = mods.HasFlag(CommandModifier);
        bool shift = mods.HasFlag(KeyModifiers.Shift);
        bool alt = mods.HasFlag(KeyModifiers.Alt);
        bool ctrl = mods.HasFlag(KeyModifiers.Control);

        if (ctrl && e.Key == Key.Tab) { ActivateTab(_active + (shift ? -1 : 1)); return true; }
        if (e.Key == Key.F1) { _ = ShowKeysAsync(); return true; }
        if (e.Key == Key.F3) { FindNext(!shift); return true; }

        if (!cmd) return false;
        switch (e.Key)
        {
            case Key.N: NewFile(); return true;
            case Key.O: _ = OpenFileAsync(); return true;
            case Key.S when shift: _ = SaveAsAsync(ActiveTab); return true;
            case Key.S: _ = SaveAsync(ActiveTab); return true;
            case Key.W: _ = CloseTabAsync(ActiveTab); return true;
            case Key.Q: Quit(); return true;
            case Key.Z when shift: Edit(t => t.Redo()); return true;
            case Key.Z: Edit(t => t.Undo()); return true;
            case Key.Y: Edit(t => t.Redo()); return true;
            case Key.X: _ = CutAsync(); return true;
            case Key.C: _ = CopyAsync(); return true;
            case Key.V: _ = PasteAsync(); return true;
            case Key.A: Edit(t => t.SelectAll()); return true;
            case Key.F: ShowFind(alt); return true;
            case Key.H when !IsMac: ShowFind(true); return true;
            case Key.G when IsMac: FindNext(!shift); return true;
            case Key.G: _ = GoToLineAsync(); return true;
            case Key.L when IsMac: _ = GoToLineAsync(); return true;
            case Key.D1 or Key.NumPad1: SetScale(1); return true;
            case Key.D2 or Key.NumPad2: SetScale(2); return true;
            case Key.D3 or Key.NumPad3: SetScale(3); return true;
            case Key.D4 or Key.NumPad4: SetScale(4); return true;
            case Key.OemPlus or Key.Add: SetScale(_settings.Scale + 1); return true;
            case Key.OemMinus or Key.Subtract: SetScale(_settings.Scale - 1); return true;
            case Key.PageUp when !IsMac: ActivateTab(_active - 1); return true;
            case Key.PageDown when !IsMac: ActivateTab(_active + 1); return true;
            case Key.OemOpenBrackets when IsMac && shift: ActivateTab(_active - 1); return true;
            case Key.OemCloseBrackets when IsMac && shift: ActivateTab(_active + 1); return true;
        }
        return false;
    }

    /// <summary>Caret movement and editing keys. Returns true when handled.</summary>
    private bool EditorKeyDown(KeyEventArgs e)
    {
        var tab = ActiveTab;
        var mods = e.KeyModifiers;
        bool shift = mods.HasFlag(KeyModifiers.Shift);
        bool ctrl = mods.HasFlag(KeyModifiers.Control);
        bool alt = mods.HasFlag(KeyModifiers.Alt);
        bool meta = mods.HasFlag(KeyModifiers.Meta);
        bool word = IsMac ? alt : ctrl;
        bool docJump = IsMac ? meta : ctrl;

        switch (e.Key)
        {
            case Key.Left when IsMac && meta: tab.Home(shift); break;
            case Key.Right when IsMac && meta: tab.End(shift); break;
            case Key.Left when word: tab.SetCaret(tab.WordLeft(tab.Caret), shift); break;
            case Key.Right when word: tab.SetCaret(tab.WordRight(tab.Caret), shift); break;
            case Key.Left: tab.MoveLeft(shift); break;
            case Key.Right: tab.MoveRight(shift); break;
            case Key.Up when docJump: if (IsMac) tab.SetCaret(new TextPos(0, 0), shift); else { ScrollBy(-1); return true; } break;
            case Key.Down when docJump: if (IsMac) tab.SetCaret(tab.Document.End, shift); else { ScrollBy(1); return true; } break;
            case Key.Up: tab.MoveVertical(-1, shift); break;
            case Key.Down: tab.MoveVertical(1, shift); break;
            case Key.Home when docJump: tab.SetCaret(new TextPos(0, 0), shift); break;
            case Key.End when docJump: tab.SetCaret(tab.Document.End, shift); break;
            case Key.Home: tab.Home(shift); break;
            case Key.End: tab.End(shift); break;
            case Key.PageUp:
                tab.ScrollRow = Math.Max(0, tab.ScrollRow - (VisibleRows - 1));
                tab.MoveVertical(-(VisibleRows - 1), shift);
                break;
            case Key.PageDown:
                tab.ScrollRow = Math.Max(0, Math.Min(tab.ScrollRow + VisibleRows - 1, tab.VisualRowCount - VisibleRows));
                tab.MoveVertical(VisibleRows - 1, shift);
                break;
            case Key.Back: tab.Backspace(word); break;
            case Key.Delete: tab.Delete(word); break;
            case Key.Enter: tab.NewLine(); break;
            case Key.Tab: tab.Tab(shift); break;
            case Key.Insert: tab.Overwrite = !tab.Overwrite; break;
            case Key.Escape: tab.ClearSelection(); break;
            default: return false;
        }
        _splash = false;
        AfterEdit();
        return true;
    }

    // ================================================================ mouse

    /// <summary>Text position under a machine-pixel coordinate (clamped into the document).</summary>
    private TextPos PosAt(int mx, int my)
    {
        var tab = ActiveTab;
        int row = (int)Math.Floor((my - _textTop) / (double)CellH) + tab.ScrollRow;
        int scrollCol = _settings.WordWrap ? 0 : tab.ScrollCol;
        int x = (int)Math.Floor((mx - _textLeft - GutterWidth * CellW + CellW / 2.0) / CellW) + scrollCol;
        return tab.PosFromVisual(row, Math.Max(0, x));
    }

    private bool InTextArea(int mx, int my) =>
        mx >= _textLeft && mx < _textLeft + _cols * CellW && my >= _textTop && my < _textTop + VisibleRows * CellH;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        ResetBlink();
        var (mx, my) = ToMachine(e.GetPosition(this));
        _lastMx = mx;
        _lastMy = my;
        var point = e.GetCurrentPoint(this).Properties;
        e.Handled = true;

        if (_splash) { _splash = false; Invalidate(); }
        if (DialogPointerPressed(mx, my)) return;
        if (MenuPointerPressed(mx, my)) return;

        if (TabAt(mx, my) is { } tabIndex)
        {
            if (point.IsMiddleButtonPressed) _ = CloseTabAsync(_tabs[tabIndex]);
            else ActivateTab(tabIndex);
            return;
        }
        if (FindPointerPressed(mx, my)) return;

        if (mx >= _textLeft + _cols * CellW && my >= _textTop && my < _textTop + _rows * CellH && ScrollThumb() is { } thumb)
        {
            if (my >= thumb.Top && my < thumb.Top + thumb.Height)
            {
                _thumbDragging = true;
                _thumbGrab = my - thumb.Top;
                e.Pointer.Capture(this);
            }
            else ScrollBy(my < thumb.Top ? -VisibleRows : VisibleRows);
            return;
        }

        if (!point.IsLeftButtonPressed) return;
        var tab = ActiveTab;
        var pos = PosAt(mx, my);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        _dragUnit = Math.Clamp(e.ClickCount, 1, 3);
        switch (_dragUnit)
        {
            case 2:
                (_dragStart, _dragEnd) = tab.WordAt(pos);
                tab.Select(_dragStart, _dragEnd);
                break;
            case 3:
                _dragStart = new TextPos(pos.Line, 0);
                _dragEnd = pos.Line + 1 < tab.Document.LineCount ? new TextPos(pos.Line + 1, 0) : new TextPos(pos.Line, tab.Document[pos.Line].Length);
                tab.Select(_dragStart, _dragEnd);
                break;
            default:
                tab.SetCaret(pos, shift);
                break;
        }
        _dragging = true;
        e.Pointer.Capture(this);
        AfterEdit();
    }

    private void ExtendDragSelection(int mx, int my)
    {
        var tab = ActiveTab;
        var pos = PosAt(mx, my);
        switch (_dragUnit)
        {
            case 2:
                var (ws, we) = tab.WordAt(pos);
                if (pos < _dragStart) tab.Select(_dragEnd, ws); else tab.Select(_dragStart, we);
                break;
            case 3:
                var lineStart = new TextPos(pos.Line, 0);
                var lineEnd = pos.Line + 1 < tab.Document.LineCount ? new TextPos(pos.Line + 1, 0) : new TextPos(pos.Line, tab.Document[pos.Line].Length);
                if (pos < _dragStart) tab.Select(_dragEnd, lineStart); else tab.Select(_dragStart, lineEnd);
                break;
            default:
                tab.SetCaret(pos, true);
                break;
        }
        AfterEdit();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var (mx, my) = ToMachine(e.GetPosition(this));
        _lastMx = mx;
        _lastMy = my;

        if (_thumbDragging)
        {
            var tab = ActiveTab;
            int trackH = _rows * CellH;
            int total = tab.VisualRowCount;
            int visible = VisibleRows;
            int thumbH = Math.Max(CellH, trackH * visible / Math.Max(1, total));
            double frac = (my - _thumbGrab - _textTop) / (double)Math.Max(1, trackH - thumbH);
            tab.ScrollRow = Math.Clamp((int)Math.Round(frac * (total - visible)), 0, Math.Max(0, total - visible));
            Invalidate();
            return;
        }
        if (_dragging)
        {
            ExtendDragSelection(mx, my);
            return;
        }

        MenuPointerMoved(mx, my);
        bool overlay = _dialog is not null || _input is not null || _openMenu >= 0;
        var wanted = !overlay && InTextArea(mx, my) && !(MenuBarVisible && my < CellH) ? IBeam : Arrow;
        if (Cursor != wanted) Cursor = wanted;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragging || _thumbDragging) e.Pointer.Capture(null);
        _dragging = false;
        _thumbDragging = false;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_mouseNearTop && _openMenu < 0)
        {
            _mouseNearTop = false;
            Invalidate();
        }
    }

    /// <summary>Called from the animation timer: keeps scrolling while a selection is dragged past the edge.</summary>
    private void AutoScrollWhileDragging()
    {
        if (!_dragging) return;
        int dir = _lastMy < _textTop ? -1 : _lastMy >= _textTop + VisibleRows * CellH ? 1 : 0;
        if (dir == 0) return;
        ScrollBy(dir);
        ExtendDragSelection(_lastMx, Math.Clamp(_lastMy, _textTop - CellH, _textTop + VisibleRows * CellH));
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_dialog is not null || _input is not null) return;
        e.Handled = true;
        var tab = ActiveTab;
        double dx = e.Delta.X, dy = e.Delta.Y;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && dx == 0) (dx, dy) = (dy, 0);

        _wheelY += dy * 3;
        int rows = (int)_wheelY;
        _wheelY -= rows;
        if (rows != 0) ScrollBy(-rows);

        if (!_settings.WordWrap)
        {
            _wheelX += dx * 3;
            int cols = (int)_wheelX;
            _wheelX -= cols;
            if (cols != 0)
            {
                tab.ScrollCol = Math.Max(0, tab.ScrollCol - cols);
                Invalidate();
            }
        }
    }
}
