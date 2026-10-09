using Avalonia.Input;
using C64Edit.Rendering;
using C64Edit.Syntax;

namespace C64Edit.UI;

public sealed partial class ScreenControl
{
    private List<Menu>? _menus;
    private bool _menuKeyboard;     // menu bar has keyboard focus (Alt / Option / F10)
    private int _openMenu = -1;     // index of the open drop-down, or -1
    private int _menuHot;           // highlighted title
    private int _menuItem = -1;     // highlighted item in the open drop-down
    private bool _mouseNearTop;

    private static bool IsMac => OperatingSystem.IsMacOS();
    private static string Mod => IsMac ? "⌘" : "CTRL+";
    private static string Sh => IsMac ? "⇧" : "SHIFT+";
    private static string Opt => IsMac ? "⌥" : "ALT+";

    private bool MenuBarVisible => _menuKeyboard || _openMenu >= 0 || _mouseNearTop;
    private const int MenuY = 0;

    private List<Menu> Menus => _menus ??= BuildMenus();

    private List<Menu> BuildMenus()
    {
        MenuItem Toggle(string label, Func<bool> get, Action<bool> set) => new()
        {
            Label = label,
            Checked = get,
            Action = () => { set(!get()); SettingsChanged(); },
        };

        MenuItem ColorItem(string label, Func<ColorScheme, int> get, Action<ColorScheme, int> set) => new()
        {
            DynamicLabel = () => $"{label}: {_profile.ColorNames[get(Scheme)]}",
            Action = () => { set(Scheme, (get(Scheme) + 1) & 15); SettingsChanged(); _keepMenuOpen = true; },
        };

        var view = new List<MenuItem>
        {
            new() { Label = "C64  40 COLUMNS", Checked = () => _settings.Machine == MachineKind.C64, Action = () => SetMachine(MachineKind.C64) },
            new() { Label = "C128 80 COLUMNS", Checked = () => _settings.Machine == MachineKind.C128, Action = () => SetMachine(MachineKind.C128) },
            MenuItem.Separator,
            new() { Label = "TRUE SIZE", Checked = () => _settings.SizeMode == SizeMode.TrueSize, Action = () => SetSizeMode(SizeMode.TrueSize) },
            new() { Label = "DYNAMIC SIZE", Checked = () => _settings.SizeMode == SizeMode.Dynamic, Action = () => SetSizeMode(SizeMode.Dynamic) },
            MenuItem.Separator,
        };
        for (int s = 1; s <= 4; s++)
        {
            int scale = s;
            view.Add(new() { Label = $"SCALE {scale}X", Shortcut = Mod + scale, Checked = () => _settings.Scale == scale, Action = () => SetScale(scale) });
        }
        view.Add(MenuItem.Separator);
        view.Add(Toggle("LINE NUMBERS", () => _settings.LineNumbers, v => _settings.LineNumbers = v));
        view.Add(Toggle("WORD WRAP", () => _settings.WordWrap, v => _settings.WordWrap = v));
        view.Add(Toggle("STATUS LINE", () => _settings.StatusLine, v => _settings.StatusLine = v));
        view.Add(Toggle("SHOW WHITESPACE", () => _settings.ShowWhitespace, v => _settings.ShowWhitespace = v));
        view.Add(Toggle("SYNTAX COLORS", () => _settings.SyntaxHighlighting, v => _settings.SyntaxHighlighting = v));

        var lang = Languages.All.Select(l => new MenuItem
        {
            Label = l.Name,
            Checked = () => ActiveTab.Highlighter.Language == l,
            Action = () => { ActiveTab.Highlighter.SetLanguage(l); Invalidate(); },
        }).ToList();

        return
        [
            new Menu
            {
                Title = "FILE",
                Items =
                [
                    new() { Label = "NEW", Shortcut = Mod + "N", Action = NewFile },
                    new() { Label = "OPEN...", Shortcut = Mod + "O", Action = () => _ = OpenFileAsync() },
                    new() { Label = "SAVE", Shortcut = Mod + "S", Action = () => _ = SaveAsync(ActiveTab) },
                    new() { Label = "SAVE AS...", Shortcut = Mod + Sh + "S", Action = () => _ = SaveAsAsync(ActiveTab) },
                    new() { Label = "CLOSE", Shortcut = Mod + "W", Action = () => _ = CloseTabAsync(ActiveTab) },
                    MenuItem.Separator,
                    new() { Label = "LOAD CHAR ROM...", Action = () => _ = LoadCharRomAsync() },
                    new() { Label = "BUILT-IN FONT", Checked = () => _settings.CharRomPath is null, Action = UseBuiltInFont },
                    MenuItem.Separator,
                    new() { Label = "QUIT", Shortcut = Mod + "Q", Action = Quit },
                ],
            },
            new Menu
            {
                Title = "EDIT",
                Items =
                [
                    new() { Label = "UNDO", Shortcut = Mod + "Z", Action = () => Edit(t => t.Undo()) },
                    new() { Label = "REDO", Shortcut = IsMac ? Mod + Sh + "Z" : Mod + "Y", Action = () => Edit(t => t.Redo()) },
                    MenuItem.Separator,
                    new() { Label = "CUT", Shortcut = Mod + "X", Action = () => _ = CutAsync() },
                    new() { Label = "COPY", Shortcut = Mod + "C", Action = () => _ = CopyAsync() },
                    new() { Label = "PASTE", Shortcut = Mod + "V", Action = () => _ = PasteAsync() },
                    new() { Label = "SELECT ALL", Shortcut = Mod + "A", Action = () => Edit(t => t.SelectAll()) },
                    MenuItem.Separator,
                    new() { Label = "FIND...", Shortcut = Mod + "F", Action = () => ShowFind(false) },
                    new() { Label = "FIND NEXT", Shortcut = IsMac ? Mod + "G" : "F3", Action = () => FindNext(true) },
                    new() { Label = "FIND PREVIOUS", Shortcut = IsMac ? Mod + Sh + "G" : Sh + "F3", Action = () => FindNext(false) },
                    new() { Label = "REPLACE...", Shortcut = IsMac ? Mod + Opt + "F" : Mod + "H", Action = () => ShowFind(true) },
                    new() { Label = "GO TO LINE...", Shortcut = IsMac ? Mod + "L" : Mod + "G", Action = () => _ = GoToLineAsync() },
                ],
            },
            new Menu { Title = "VIEW", Items = view },
            new Menu
            {
                Title = "COLORS",
                Items =
                [
                    ColorItem("BORDER", s => s.Border, (s, v) => s.Border = v),
                    ColorItem("SCREEN", s => s.Background, (s, v) => s.Background = v),
                    ColorItem("TEXT", s => s.Text, (s, v) => s.Text = v),
                    MenuItem.Separator,
                    new() { Label = "RESET COLORS", Action = ResetColors },
                ],
            },
            new Menu { Title = "LANG", Items = lang },
            new Menu
            {
                Title = "HELP",
                Items =
                [
                    new() { Label = "KEYBOARD", Shortcut = "F1", Action = () => _ = ShowKeysAsync() },
                    new() { Label = "ABOUT C64EDIT", Action = () => _ = ShowAboutAsync() },
                ],
            },
        ];
    }

    /// <summary>Set by actions (like color cycling) that want the drop-down to stay open so they can be repeated.</summary>
    private bool _keepMenuOpen;

    private int MenuTitleX(int index)
    {
        int x = _textLeft;
        for (int i = 0; i < index; i++) x += (Menus[i].Title.Length + 2) * CellW;
        return x;
    }

    private int MenuTitleAt(int mx)
    {
        for (int i = 0; i < Menus.Count; i++)
        {
            int x = MenuTitleX(i);
            if (mx >= x && mx < x + (Menus[i].Title.Length + 2) * CellW) return i;
        }
        return -1;
    }

    /// <summary>Drop-down geometry in cells: x/y of the frame's top-left corner, inner width, number of items.</summary>
    private (int X, int Y, int Inner) DropDownRect(int menuIndex)
    {
        var menu = Menus[menuIndex];
        int labelW = 0, shortW = 0;
        foreach (var item in menu.Items)
        {
            labelW = Math.Max(labelW, item.Text.Length);
            shortW = Math.Max(shortW, item.Shortcut.Length);
        }
        int inner = 2 + labelW + (shortW > 0 ? 2 + shortW : 0) + 1;
        int x = MenuTitleX(menuIndex);
        int maxX = _fb.Width - (inner + 2) * CellW;
        x = Math.Max(0, Math.Min(x, maxX));
        return (x, MenuY + CellH, inner);
    }

    private void DrawMenu()
    {
        uint barFg = BackColor, barBg = TextColor;
        _fb.FillRect(0, MenuY, _fb.Width, CellH, barBg);
        for (int i = 0; i < Menus.Count; i++)
        {
            bool hot = (_menuKeyboard || _openMenu >= 0) && i == (_openMenu >= 0 ? _openMenu : _menuHot);
            DrawText(MenuTitleX(i), MenuY, " " + Menus[i].Title + " ", hot ? barBg : barFg, hot ? barFg : barBg);
        }

        if (_openMenu < 0) return;
        var menu = Menus[_openMenu];
        var (x, y, inner) = DropDownRect(_openMenu);
        DrawText(x, y, "┌" + new string('─', inner) + "┐", barFg, barBg);
        for (int i = 0; i < menu.Items.Count; i++)
        {
            var item = menu.Items[i];
            int iy = y + (i + 1) * CellH;
            if (item.IsSeparator)
            {
                DrawText(x, iy, "├" + new string('─', inner) + "┤", barFg, barBg);
                continue;
            }
            var check = item.Checked?.Invoke() == true ? "✓" : " ";
            var label = check + " " + item.Text;
            var body = label.PadRight(inner - item.Shortcut.Length - 1) + item.Shortcut + " ";
            bool selected = i == _menuItem;
            DrawText(x, iy, "│", barFg, barBg);
            DrawText(x + CellW, iy, body, selected ? barBg : barFg, selected ? barFg : barBg);
            DrawText(x + (inner + 1) * CellW, iy, "│", barFg, barBg);
        }
        DrawText(x, y + (menu.Items.Count + 1) * CellH, "└" + new string('─', inner) + "┘", barFg, barBg);
    }

    private void OpenMenu(int index, bool selectFirst)
    {
        _openMenu = index;
        _menuHot = index;
        _menuItem = selectFirst ? NextItem(index, -1, 1) : -1;
        Invalidate();
    }

    private void CloseMenus()
    {
        _openMenu = -1;
        _menuKeyboard = false;
        _menuItem = -1;
        Invalidate();
    }

    private int NextItem(int menuIndex, int from, int dir)
    {
        var items = Menus[menuIndex].Items;
        int i = from;
        for (int n = 0; n < items.Count; n++)
        {
            i = ((i + dir) % items.Count + items.Count) % items.Count;
            if (!items[i].IsSeparator) return i;
        }
        return -1;
    }

    private void ActivateMenuItem(int menuIndex, int itemIndex)
    {
        var item = Menus[menuIndex].Items[itemIndex];
        if (item.IsSeparator) return;
        _keepMenuOpen = false;
        item.Action?.Invoke();
        if (!_keepMenuOpen)
        {
            CloseMenus();
            _mouseNearTop = false;
        }
        Invalidate();
    }

    private void ToggleMenuKeyboard()
    {
        if (_menuKeyboard || _openMenu >= 0) CloseMenus();
        else
        {
            _menuKeyboard = true;
            _menuHot = 0;
            Invalidate();
        }
    }

    /// <summary>Keyboard handling while the menu bar has focus. Returns true when the key was consumed.</summary>
    private bool MenuKeyDown(KeyEventArgs e)
    {
        if (!_menuKeyboard && _openMenu < 0) return false;
        int count = Menus.Count;
        switch (e.Key)
        {
            case Key.Escape:
                if (_openMenu >= 0 && _menuKeyboard) { _openMenu = -1; _menuItem = -1; }
                else CloseMenus();
                break;
            case Key.Left:
                _menuHot = ((_openMenu >= 0 ? _openMenu : _menuHot) - 1 + count) % count;
                if (_openMenu >= 0) OpenMenu(_menuHot, true);
                break;
            case Key.Right:
                _menuHot = ((_openMenu >= 0 ? _openMenu : _menuHot) + 1) % count;
                if (_openMenu >= 0) OpenMenu(_menuHot, true);
                break;
            case Key.Down:
                if (_openMenu < 0) OpenMenu(_menuHot, true);
                else _menuItem = NextItem(_openMenu, _menuItem, 1);
                break;
            case Key.Up:
                if (_openMenu < 0) OpenMenu(_menuHot, false);
                _menuItem = NextItem(_openMenu, _menuItem < 0 ? 0 : _menuItem, -1);
                break;
            case Key.Enter:
            case Key.Space:
                if (_openMenu < 0) OpenMenu(_menuHot, true);
                else if (_menuItem >= 0) ActivateMenuItem(_openMenu, _menuItem);
                break;
            case Key.F10:
            case Key.LeftAlt:
            case Key.RightAlt:
                CloseMenus();
                break;
            default:
                if (e.Key >= Key.A && e.Key <= Key.Z && (e.KeyModifiers & ~KeyModifiers.Alt) == KeyModifiers.None)
                {
                    char letter = (char)('A' + (e.Key - Key.A));
                    if (_openMenu < 0)
                    {
                        int m = Menus.FindIndex(x => x.Mnemonic == letter);
                        if (m >= 0) OpenMenu(m, true);
                    }
                    else
                    {
                        int it = Menus[_openMenu].Items.FindIndex(x => !x.IsSeparator && x.Text.StartsWith(letter));
                        if (it >= 0) ActivateMenuItem(_openMenu, it);
                    }
                    break;
                }
                // Any other key (a shortcut, say) leaves the menu and is handled normally.
                if (e.KeyModifiers != KeyModifiers.None)
                {
                    CloseMenus();
                    return false;
                }
                break;
        }
        Invalidate();
        return true;
    }

    /// <summary>Mouse press while the menu is showing. Returns true when the press was consumed.</summary>
    private bool MenuPointerPressed(int mx, int my)
    {
        if (!MenuBarVisible) return false;
        if (my >= MenuY && my < MenuY + CellH)
        {
            int m = MenuTitleAt(mx);
            if (m >= 0 && m != _openMenu) OpenMenu(m, false);
            else CloseMenus();
            return true;
        }
        if (_openMenu >= 0)
        {
            int item = DropDownItemAt(mx, my);
            if (item >= 0) ActivateMenuItem(_openMenu, item);
            else
            {
                CloseMenus();
                _mouseNearTop = false;
            }
            return true;
        }
        return false;
    }

    private int DropDownItemAt(int mx, int my)
    {
        if (_openMenu < 0) return -1;
        var (x, y, inner) = DropDownRect(_openMenu);
        if (mx < x || mx >= x + (inner + 2) * CellW) return -1;
        int row = (my - y) / CellH - 1;
        if (my < y || row < 0 || row >= Menus[_openMenu].Items.Count) return -1;
        return Menus[_openMenu].Items[row].IsSeparator ? -1 : row;
    }

    /// <summary>Tracks the mouse for "show the menu bar near the top" and drop-down hover.</summary>
    private void MenuPointerMoved(int mx, int my)
    {
        int hotZoneBottom = _tabs.Count > 1 ? TabY : _textTop;
        bool near = my >= 0 && my < Math.Max(CellH, hotZoneBottom);
        bool changed = false;
        if (near != _mouseNearTop && _openMenu < 0)
        {
            _mouseNearTop = near;
            changed = true;
        }
        if (_openMenu >= 0)
        {
            if (my >= MenuY && my < MenuY + CellH)
            {
                int m = MenuTitleAt(mx);
                if (m >= 0 && m != _openMenu) { OpenMenu(m, false); changed = true; }
            }
            int item = DropDownItemAt(mx, my);
            if (item >= 0 && item != _menuItem) { _menuItem = item; changed = true; }
        }
        if (changed) Invalidate();
    }
}
