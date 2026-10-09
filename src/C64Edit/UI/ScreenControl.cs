using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using C64Edit.Rendering;
using C64Edit.Syntax;
using C64Edit.Text;

namespace C64Edit.UI;

/// <summary>
/// The whole editor surface. Everything - border, text, tab bar, menus, dialogs - is drawn as
/// character cells into a machine-resolution frame buffer, which is then scaled up with
/// nearest-neighbour sampling so pixels stay crisp at any size.
/// </summary>
public sealed partial class ScreenControl : Control
{
    private readonly Settings _settings;
    private readonly List<EditorTab> _tabs = [];
    private int _active;
    private BitmapFont _font;
    private readonly FrameBuffer _fb = new();
    private WriteableBitmap? _bitmap;
    private readonly DispatcherTimer _timer;
    private TokenKind[] _kinds = new TokenKind[1024];

    // Layout, in machine pixels unless noted.
    private MachineProfile _profile = MachineProfile.C64;
    private double _pixelSize = 2;      // DIPs per machine pixel
    private int _cols, _rows;           // text grid, including the line-number gutter
    private int _textLeft, _textTop;
    private int CellW => _profile.CellWidth;
    private int CellH => _profile.CellHeight;

    // Animation.
    private DateTime _blinkEpoch = DateTime.UtcNow;
    private long _lastAnimationKey = -1;
    private bool _dirty = true;
    private bool _splash;

    public ScreenControl(Settings settings)
    {
        _settings = settings;
        _font = LoadFont();
        Focusable = true;
        ClipToBounds = true;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(50), DispatcherPriority.Render, (_, _) => OnTick());
        Avalonia.Input.DragDrop.SetAllowDrop(this, true);
        Avalonia.Input.DragDrop.AddDropHandler(this, OnDrop);
    }

    public EditorTab ActiveTab => _tabs[_active];
    public IReadOnlyList<EditorTab> Tabs => _tabs;

    private ColorScheme Scheme => _settings.ColorsFor(_settings.Machine);
    private uint Pal(int index) => _profile.Palette[index & 15];
    private uint BorderColor => Pal(Scheme.Border);
    private uint BackColor => Pal(Scheme.Background);
    private uint TextColor => Pal(Scheme.Text);

    /// <summary>A color that shows up on the border: the screen color, unless it matches the border.</summary>
    private uint BorderInk
    {
        get
        {
            if (Scheme.Background != Scheme.Border) return BackColor;
            if (Scheme.Text != Scheme.Border) return TextColor;
            return Pal(Scheme.Border == 1 ? 0 : 1);
        }
    }

    private BitmapFont LoadFont()
    {
        if (_settings.CharRomPath is { } path && File.Exists(path))
        {
            try { return BitmapFont.LoadWithCharRom(path); }
            catch { _settings.CharRomPath = null; }
        }
        return BitmapFont.LoadBuiltIn();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _timer.Start();
        Focus();
        UpdateLayoutMetrics();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _timer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    public void ShowSplash() => _splash = true;

    // ================================================================ tabs

    public void AddTab(EditorTab tab)
    {
        // Replace a pristine "UNTITLED" tab instead of piling up empty ones.
        if (_tabs.Count == 1 && _tabs[0].FilePath is null && !_tabs[0].Document.IsModified
            && _tabs[0].Document.LineCount == 1 && _tabs[0].Document[0].Length == 0 && tab.FilePath is not null)
        {
            _tabs.Clear();
        }
        tab.TabSize = _settings.TabSize;
        _tabs.Add(tab);
        _active = _tabs.Count - 1;
        _splash = false;
        OnActiveTabChanged();
    }

    private void OnActiveTabChanged()
    {
        _find.Message = "";
        if (_cols > 0) // not laid out yet: UpdateLayoutMetrics will do this
        {
            ApplyWrapWidth();
            EnsureCaretVisible();
        }
        UpdateTitle();
        Invalidate();
    }

    public void ActivateTab(int index)
    {
        if (_tabs.Count == 0) return;
        _active = ((index % _tabs.Count) + _tabs.Count) % _tabs.Count;
        OnActiveTabChanged();
    }

    private void UpdateTitle()
    {
        if (TopLevel.GetTopLevel(this) is Window w && _tabs.Count > 0)
            w.Title = $"{ActiveTab.DisplayName}{(ActiveTab.Document.IsModified ? " *" : "")} - C64Edit";
    }

    // ================================================================ layout

    private int GutterWidth => _settings.LineNumbers ? Math.Max(3, ActiveTab.Document.LineCount.ToString().Length) + 1 : 0;
    private int TextCols => Math.Max(1, _cols - GutterWidth);
    private int VisibleRows => Math.Max(1, _rows - _find.Rows);

    /// <summary>Size of one machine pixel in DIPs, snapped so it covers a whole number of physical pixels.</summary>
    private double ComputePixelSize()
    {
        double renderScaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        double physical = Math.Max(1, Math.Round(_settings.Scale * renderScaling));
        return physical / renderScaling;
    }

    /// <summary>Client size (DIPs) of the window in true-size mode.</summary>
    public Size TrueClientSize()
    {
        var p = MachineProfile.For(_settings.Machine);
        double px = ComputePixelSize();
        int w = p.BorderLeft + p.NativeColumns * p.CellWidth + p.BorderRight;
        int h = p.BorderTop + p.NativeRows * p.CellHeight + p.BorderBottom;
        return new Size(w * px, h * px);
    }

    /// <summary>Smallest sensible client size (DIPs) in dynamic mode.</summary>
    public Size MinimumClientSize()
    {
        var p = MachineProfile.For(_settings.Machine);
        double px = ComputePixelSize();
        return new Size((p.BorderLeft + p.BorderRight + 24 * p.CellWidth) * px, (p.BorderTop + p.BorderBottom + 6 * p.CellHeight) * px);
    }

    private void UpdateLayoutMetrics()
    {
        _profile = MachineProfile.For(_settings.Machine);
        _pixelSize = ComputePixelSize();
        var p = _profile;
        int fbW, fbH;
        if (_settings.SizeMode == SizeMode.TrueSize)
        {
            _cols = p.NativeColumns;
            _rows = p.NativeRows;
            fbW = p.BorderLeft + _cols * CellW + p.BorderRight;
            fbH = p.BorderTop + _rows * CellH + p.BorderBottom;
        }
        else
        {
            fbW = (int)Math.Floor(Bounds.Width / _pixelSize);
            fbH = (int)Math.Floor(Bounds.Height / _pixelSize);
            _cols = Math.Max(10, (fbW - p.BorderLeft - p.BorderRight) / CellW);
            _rows = Math.Max(3, (fbH - p.BorderTop - p.BorderBottom) / CellH);
        }
        fbW = Math.Max(fbW, p.BorderLeft + _cols * CellW + p.BorderRight);
        fbH = Math.Max(fbH, p.BorderTop + _rows * CellH + p.BorderBottom);
        // Whatever doesn't fit a whole character cell goes to the border, split evenly.
        _textLeft = p.BorderLeft + (fbW - p.BorderLeft - p.BorderRight - _cols * CellW) / 2;
        _textTop = p.BorderTop + (fbH - p.BorderTop - p.BorderBottom - _rows * CellH) / 2;
        _fb.Resize(fbW, fbH);
        if (_tabs.Count > 0)
        {
            ApplyWrapWidth();
            EnsureCaretVisible();
        }
        Invalidate();
    }

    private void ApplyWrapWidth()
    {
        foreach (var t in _tabs)
        {
            t.TabSize = _settings.TabSize;
            t.WrapWidth = _settings.WordWrap ? TextCols : 0;
        }
        if (_settings.WordWrap) ActiveTab.ScrollCol = 0;
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateLayoutMetrics();
    }

    public void SettingsChanged()
    {
        _settings.Save();
        UpdateLayoutMetrics();
        if (TopLevel.GetTopLevel(this) is MainWindow w) w.ApplySizeMode();
    }

    public void Invalidate()
    {
        _dirty = true;
        InvalidateVisual();
    }

    /// <summary>Restarts the cursor blink so the cursor is visible right after any interaction.</summary>
    private void ResetBlink()
    {
        _blinkEpoch = DateTime.UtcNow;
    }

    private bool CursorOn
    {
        get
        {
            var period = _profile.BlinkInterval.TotalMilliseconds;
            return ((long)((DateTime.UtcNow - _blinkEpoch).TotalMilliseconds / period) & 1) == 0;
        }
    }

    private void OnTick()
    {
        AutoScrollWhileDragging();
        long key = (CursorOn ? 1 : 0) + 2 * MarqueeFrame();
        if (key != _lastAnimationKey)
        {
            _lastAnimationKey = key;
            Invalidate();
        }
    }

    public void EnsureCaretVisible()
    {
        if (_tabs.Count == 0) return;
        var tab = ActiveTab;
        var (row, x) = tab.VisualOf(tab.Caret);
        int visible = VisibleRows;
        if (row < tab.ScrollRow) tab.ScrollRow = row;
        else if (row >= tab.ScrollRow + visible) tab.ScrollRow = row - visible + 1;
        tab.ScrollRow = Math.Clamp(tab.ScrollRow, 0, Math.Max(0, tab.VisualRowCount - 1));

        if (_settings.WordWrap) { tab.ScrollCol = 0; return; }
        int cols = TextCols;
        int jump = Math.Max(1, cols / 4);
        if (x < tab.ScrollCol) tab.ScrollCol = Math.Max(0, x - jump);
        else if (x >= tab.ScrollCol + cols) tab.ScrollCol = x - cols + jump;
    }

    private void ScrollBy(int rows)
    {
        var tab = ActiveTab;
        int max = Math.Max(0, tab.VisualRowCount - VisibleRows);
        tab.ScrollRow = Math.Clamp(tab.ScrollRow + rows, 0, max);
        Invalidate();
    }

    // ================================================================ rendering

    public override void Render(DrawingContext context)
    {
        if (_dirty)
        {
            ComposeFrame();
            UploadFrame();
            _dirty = false;
        }
        context.FillRectangle(new SolidColorBrush(Color.FromUInt32(BorderColor)), new Rect(Bounds.Size));
        if (_bitmap is null) return;
        using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.None }))
        {
            context.DrawImage(_bitmap, new Rect(0, 0, _fb.Width, _fb.Height),
                new Rect(0, 0, _fb.Width * _pixelSize, _fb.Height * _pixelSize));
        }
    }

    private void UploadFrame()
    {
        if (_bitmap is null || _bitmap.PixelSize.Width != _fb.Width || _bitmap.PixelSize.Height != _fb.Height)
        {
            _bitmap?.Dispose();
            _bitmap = new WriteableBitmap(new PixelSize(_fb.Width, _fb.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        }
        using var locked = _bitmap.Lock();
        var src = MemoryMarshal.AsBytes(_fb.Pixels.AsSpan());
        int rowBytes = _fb.Width * 4;
        for (int y = 0; y < _fb.Height; y++)
        {
            unsafe
            {
                var dst = new Span<byte>((byte*)locked.Address + (long)y * locked.RowBytes, rowBytes);
                src.Slice(y * rowBytes, rowBytes).CopyTo(dst);
            }
        }
    }

    private void DrawCell(int x, int y, int codePoint, uint fg, uint bg) =>
        _fb.DrawGlyph(x, y, _font.Get(codePoint), CellH, fg, bg);

    /// <summary>Draws a string as character cells; returns the x after the last cell.</summary>
    private int DrawText(int x, int y, string text, uint fg, uint bg, int maxCells = int.MaxValue)
    {
        int cells = 0;
        for (int i = 0; i < text.Length && cells < maxCells; i++)
        {
            int cp = text[i];
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length) cp = char.ConvertToUtf32(text[i], text[++i]);
            DrawCell(x, y, cp, fg, bg);
            x += CellW;
            cells++;
        }
        return x;
    }

    private void ComposeFrame()
    {
        if (_tabs.Count == 0) return;
        _fb.Fill(BorderColor);
        _fb.FillRect(_textLeft, _textTop, _cols * CellW, _rows * CellH, BackColor);

        if (_splash) DrawSplash();
        else DrawText();

        if (_tabs.Count > 1) DrawTabBar();
        DrawScrollIndicator();
        if (_settings.StatusLine || _find.Visible) DrawStatusLine();
        if (_find.Visible) DrawFindPanel();
        if (MenuBarVisible) DrawMenu();
        if (_dialog is not null) DrawMessageDialog(_dialog);
        if (_input is not null) DrawInputDialog(_input);
    }

    private uint SyntaxColor(TokenKind kind)
    {
        if (!_settings.SyntaxHighlighting || kind == TokenKind.Text) return TextColor;
        int idx = _profile.SyntaxColors[(int)kind];
        return idx == Scheme.Background ? TextColor : Pal(idx);
    }

    private bool KeyboardInEditor => _dialog is null && _input is null && !_menuKeyboard && _openMenu < 0 && !_find.Visible;

    private void DrawText()
    {
        var tab = ActiveTab;
        var doc = tab.Document;
        int gutter = GutterWidth;
        int textCols = TextCols;
        int scrollCol = _settings.WordWrap ? 0 : tab.ScrollCol;
        var selStart = tab.SelectionStart;
        var selEnd = tab.SelectionEnd;
        bool hasSel = tab.HasSelection;
        uint back = BackColor, text = TextColor;
        uint dim = SyntaxColor(TokenKind.Comment);
        if (dim == text) dim = Pal(_profile.SyntaxColors[(int)TokenKind.Comment]);
        int rowCount = tab.VisualRowCount;
        int lastHighlighted = -1;

        for (int r = 0; r < VisibleRows; r++)
        {
            int visRow = tab.ScrollRow + r;
            if (visRow >= rowCount) break;
            int y = _textTop + r * CellH;
            var (lineIdx, start, end, startCol) = tab.RowInfo(visRow);
            var line = doc[lineIdx];

            if (gutter > 0 && start == 0)
            {
                var num = (lineIdx + 1).ToString().PadLeft(gutter - 1);
                DrawText(_textLeft, y, num, lineIdx == tab.Caret.Line ? text : dim, back);
            }

            if (lineIdx != lastHighlighted)
            {
                if (_kinds.Length < line.Length) _kinds = new TokenKind[line.Length * 2];
                if (_settings.SyntaxHighlighting) tab.Highlighter.Highlight(lineIdx, _kinds);
                else Array.Clear(_kinds, 0, line.Length);
                lastHighlighted = lineIdx;
            }

            int x0 = _textLeft + gutter * CellW;
            int col = startCol;
            for (int i = start; i < end; i++)
            {
                char ch = line[i];
                int width = ch == '\t' ? tab.NextTabStop(col) - col : 1;
                bool selected = hasSel && new TextPos(lineIdx, i) >= selStart && new TextPos(lineIdx, i) < selEnd;
                uint fg = SyntaxColor(_kinds[i]);
                for (int k = 0; k < width; k++)
                {
                    int screenCol = col + k - startCol - scrollCol;
                    if (screenCol < 0) continue;
                    if (screenCol >= textCols) break;
                    int cp = ch;
                    uint cellFg = fg;
                    if (ch == '\t') { cp = _settings.ShowWhitespace && k == 0 ? '→' : ' '; cellFg = dim; }
                    else if (ch == ' ' && _settings.ShowWhitespace) { cp = '·'; cellFg = dim; }
                    else if (char.IsHighSurrogate(ch) && i + 1 < line.Length) cp = char.ConvertToUtf32(ch, line[i + 1]);
                    else if (char.IsLowSurrogate(ch)) cp = ' ';
                    else if (ch < ' ') cp = '▒';
                    DrawCell(x0 + screenCol * CellW, y, cp, selected ? back : cellFg, selected ? cellFg : back);
                }
                col += width;
            }

            // A selected line break shows as one reversed cell after the line, like a selected newline in most editors.
            bool lastSegment = end == line.Length;
            if (hasSel && lastSegment && lineIdx >= selStart.Line && lineIdx < selEnd.Line)
            {
                int screenCol = col - startCol - scrollCol;
                if (screenCol >= 0 && screenCol < textCols) DrawCell(x0 + screenCol * CellW, y, ' ', back, text);
            }
        }

        DrawCursor(tab, gutter, scrollCol);
    }

    private void DrawCursor(EditorTab tab, int gutter, int scrollCol)
    {
        if (!CursorOn || !KeyboardInEditor || !IsKeyboardFocusWithin) return;
        var (row, x) = tab.VisualOf(tab.Caret);
        int screenRow = row - tab.ScrollRow;
        int screenCol = x - scrollCol;
        if (screenRow < 0 || screenRow >= VisibleRows || screenCol < 0 || screenCol >= TextCols) return;

        var line = tab.Document[tab.Caret.Line];
        int cp = tab.Caret.Col < line.Length && line[tab.Caret.Col] is not '\t' and >= ' ' ? line[tab.Caret.Col] : ' ';
        if (char.IsHighSurrogate((char)cp) && tab.Caret.Col + 1 < line.Length) cp = char.ConvertToUtf32(line[tab.Caret.Col], line[tab.Caret.Col + 1]);
        bool selected = tab.HasSelection && tab.Caret >= tab.SelectionStart && tab.Caret < tab.SelectionEnd;
        int px = _textLeft + (gutter + screenCol) * CellW;
        int py = _textTop + screenRow * CellH;

        if (tab.Overwrite)
        {
            // Overwrite mode: a thick underline instead of the classic reversed block.
            int h = Math.Max(2, CellH / 4);
            _fb.FillRect(px, py + CellH - h, CellW, h, TextColor);
            return;
        }
        // The classic C64 cursor: the character cell in reverse video.
        DrawCell(px, py, cp, selected ? TextColor : BackColor, selected ? BackColor : TextColor);
    }

    private void DrawSplash()
    {
        bool c128 = _profile.Kind == MachineKind.C128;
        string[] lines = c128
            ? [
                "",
                "C64EDIT 128 TEXT EDITOR V1.0",
                " 122365 BYTES FREE",
                " (C)2026 C64EDIT CONTRIBUTORS",
                "",
                "READY.",
            ]
            : [
                "",
                "    **** C64EDIT TEXT EDITOR V1 ****",
                "",
                " 64K RAM SYSTEM  38911 BYTES FREE",
                "",
                "READY.",
            ];
        for (int r = 0; r < lines.Length && r < _rows; r++)
            DrawText(_textLeft, _textTop + r * CellH, lines[r], TextColor, BackColor, _cols);
        if (CursorOn && IsKeyboardFocusWithin && lines.Length < _rows)
            DrawCell(_textLeft, _textTop + lines.Length * CellH, ' ', BackColor, TextColor);
    }

    // ---------------------------------------------------------------- status line & scroll indicator

    private int StatusY => _textTop + _rows * CellH + Math.Max(0, (_profile.BorderBottom - CellH) / 2) / 8 * 8;

    private void DrawStatusLine()
    {
        var tab = ActiveTab;
        string status;
        if (_find.Visible)
        {
            status = _find.Message.Length > 0 ? _find.Message
                : _find.ReplaceMode ? "RET:REPLACE " + Mod + "RET:ALL TAB:FIELD ESC" : "RET:NEXT SHIFT+RET:PREV ESC:CLOSE";
        }
        else
        {
            var mode = tab.Overwrite ? "OVR" : "INS";
            var lang = tab.Highlighter.Language.Name;
            var right = $"LN {tab.Caret.Line + 1},{tab.Caret.Col + 1} {mode}";
            var left = $"{lang}{(tab.Document.IsModified ? " *" : "")}";
            int pad = _cols - left.Length - right.Length;
            status = pad >= 1 ? left + new string(' ', pad) + right : right;
        }
        if (status.Length > _cols) status = status[.._cols];
        DrawText(_textLeft, StatusY, status, BorderInk, BorderColor, _cols);
    }

    private (int Top, int Height)? ScrollThumb()
    {
        var tab = ActiveTab;
        int total = tab.VisualRowCount;
        int visible = VisibleRows;
        if (total <= visible) return null;
        int trackH = _rows * CellH;
        int thumbH = Math.Max(CellH, trackH * visible / total);
        int maxScroll = total - visible;
        int top = (int)((long)(trackH - thumbH) * Math.Min(tab.ScrollRow, maxScroll) / maxScroll);
        return (_textTop + top, thumbH);
    }

    private int ScrollThumbX => _textLeft + _cols * CellW + (_profile.BorderRight - CellW / 2) / 2;

    private void DrawScrollIndicator()
    {
        if (_splash || ScrollThumb() is not { } thumb) return;
        _fb.FillRect(ScrollThumbX, thumb.Top, CellW / 2, thumb.Height, BorderInk);
    }

    // ---------------------------------------------------------------- tab bar

    private const int MarqueeStepMs = 300;
    private const int MarqueePauseMs = 1500;
    private const string MarqueeGap = "   ";
    private int _tabScroll;

    private int TabWidth => _profile.TabLabelWidth + 2;
    private int TabY => _textTop - CellH;

    /// <summary>Characters the label of a long file name has rotated by at this moment.</summary>
    private static int MarqueeOffset(EditorTab tab, int labelWidth)
    {
        var name = tab.DisplayName;
        if (name.Length <= labelWidth) return 0;
        int loop = name.Length + MarqueeGap.Length;
        long cycle = MarqueePauseMs + (long)loop * MarqueeStepMs;
        long t = (long)(DateTime.UtcNow - tab.Created).TotalMilliseconds % cycle;
        return t < MarqueePauseMs ? 0 : (int)((t - MarqueePauseMs) / MarqueeStepMs + 1) % loop;
    }

    private long MarqueeFrame()
    {
        if (_tabs.Count < 2) return 0;
        long frame = 0;
        foreach (var t in _tabs) frame = frame * 31 + MarqueeOffset(t, _profile.TabLabelWidth);
        return frame & 0x7FFFFFFF;
    }

    private static string TabLabel(EditorTab tab, int width)
    {
        var name = tab.DisplayName;
        if (name.Length <= width) return name.PadRight(width);
        var loop = name + MarqueeGap;
        int offset = MarqueeOffset(tab, width);
        return (loop + loop).Substring(offset, width);
    }

    /// <summary>How many tabs fit in the strip at once (each tab plus a one-cell gap).</summary>
    private int TabsPerScreen => Math.Max(1, (_cols + 1) / (TabWidth + 1));

    private void ClampTabScroll()
    {
        int perScreen = TabsPerScreen;
        if (_active < _tabScroll) _tabScroll = _active;
        if (_active >= _tabScroll + perScreen) _tabScroll = _active - perScreen + 1;
        _tabScroll = Math.Clamp(_tabScroll, 0, Math.Max(0, _tabs.Count - perScreen));
    }

    private void DrawTabBar()
    {
        ClampTabScroll();
        int y = TabY;
        int perScreen = TabsPerScreen;
        int labelWidth = _profile.TabLabelWidth;
        for (int i = _tabScroll; i < _tabs.Count && i < _tabScroll + perScreen; i++)
        {
            var tab = _tabs[i];
            int x = _textLeft + (i - _tabScroll) * (TabWidth + 1) * CellW;
            bool active = i == _active;
            // Active tab uses the screen's colors so it looks attached to the screen;
            // inactive tabs are reverse video.
            uint fg = active ? TextColor : BackColor;
            uint bg = active ? BackColor : TextColor;
            var dirtyMark = tab.Document.IsModified ? "*" : " ";
            DrawText(x, y, " " + TabLabel(tab, labelWidth) + dirtyMark, fg, bg);
        }
        if (_tabScroll > 0) DrawCell(_textLeft - CellW, y, '◄', BorderInk, BorderColor);
        if (_tabScroll + perScreen < _tabs.Count) DrawCell(_textLeft + _cols * CellW, y, '►', BorderInk, BorderColor);
    }

    private int? TabAt(int mx, int my)
    {
        if (_tabs.Count < 2 || my < TabY || my >= TabY + CellH) return null;
        if (mx < _textLeft) return Math.Max(0, _tabScroll - 1);
        if (mx >= _textLeft + _cols * CellW) return Math.Min(_tabs.Count - 1, _tabScroll + TabsPerScreen);
        int slot = (mx - _textLeft) / CellW / (TabWidth + 1);
        int index = _tabScroll + slot;
        return index < _tabs.Count ? index : null;
    }
}
