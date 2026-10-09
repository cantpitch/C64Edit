using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using C64Edit.Rendering;
using C64Edit.Text;

namespace C64Edit.UI;

public sealed partial class ScreenControl
{
    /// <summary>Runs an editing action on the active tab and does the bookkeeping every edit needs.</summary>
    private void Edit(Action<EditorTab> action)
    {
        _splash = false;
        action(ActiveTab);
        AfterEdit();
    }

    private void AfterEdit()
    {
        EnsureCaretVisible();
        ResetBlink();
        UpdateTitle();
        Invalidate();
    }

    // ================================================================ files

    public void NewFile()
    {
        AddTab(new EditorTab(new TextDocument(), null));
    }

    /// <param name="createIfMissing">Command-line paths that don't exist yet open as new files that save there.</param>
    public void OpenPath(string path, bool createIfMissing = false)
    {
        var full = Path.GetFullPath(path);
        int existing = _tabs.FindIndex(t => t.FilePath is { } p && string.Equals(Path.GetFullPath(p), full, StringComparison.Ordinal));
        if (existing >= 0) { ActivateTab(existing); return; }
        try
        {
            if (createIfMissing && !File.Exists(full) && Directory.Exists(Path.GetDirectoryName(full)))
                AddTab(new EditorTab(new TextDocument(), full));
            else
                AddTab(EditorTab.Open(full));
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            _ = ShowMessageAsync("LOAD", $"?FILE NOT FOUND ERROR\n\n{Path.GetFileName(path)}", ["OK"]);
        }
        catch (Exception ex)
        {
            _ = ShowMessageAsync("LOAD", $"?LOAD ERROR\n\n{ex.Message}", ["OK"]);
        }
    }

    private IStorageProvider? Storage => TopLevel.GetTopLevel(this)?.StorageProvider;

    private async Task OpenFileAsync()
    {
        if (Storage is not { } storage) return;
        var startFolder = ActiveTab.FilePath is { } p ? await storage.TryGetFolderFromPathAsync(Path.GetDirectoryName(p)!) : null;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "LOAD",
            AllowMultiple = true,
            SuggestedStartLocation = startFolder,
        });
        foreach (var file in files)
            if (file.TryGetLocalPath() is { } path) OpenPath(path);
        Focus();
    }

    public async Task<bool> SaveAsync(EditorTab tab)
    {
        if (tab.FilePath is null) return await SaveAsAsync(tab);
        try
        {
            tab.Document.Save(tab.FilePath);
            AfterEdit();
            return true;
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("SAVE", $"?SAVE ERROR\n\n{ex.Message}", ["OK"]);
            return false;
        }
    }

    public async Task<bool> SaveAsAsync(EditorTab tab)
    {
        if (Storage is not { } storage) return false;
        var startFolder = tab.FilePath is { } p ? await storage.TryGetFolderFromPathAsync(Path.GetDirectoryName(p)!) : null;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "SAVE",
            SuggestedFileName = tab.DisplayName,
            SuggestedStartLocation = startFolder,
            ShowOverwritePrompt = true,
        });
        Focus();
        if (file?.TryGetLocalPath() is not { } path) return false;
        try
        {
            tab.SaveAs(path);
            AfterEdit();
            return true;
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("SAVE", $"?SAVE ERROR\n\n{ex.Message}", ["OK"]);
            return false;
        }
    }

    /// <summary>Asks about unsaved changes. Returns false if the user cancelled.</summary>
    private async Task<bool> ConfirmDiscardAsync(EditorTab tab)
    {
        if (!tab.Document.IsModified) return true;
        int index = _tabs.IndexOf(tab);
        if (index >= 0 && index != _active) ActivateTab(index);
        int choice = await ShowMessageAsync("CLOSE", $"SAVE CHANGES TO {tab.DisplayName.ToUpperInvariant()}?", ["SAVE", "DISCARD", "CANCEL"], 2);
        return choice switch
        {
            0 => await SaveAsync(tab),
            1 => true,
            _ => false,
        };
    }

    public async Task<bool> CloseTabAsync(EditorTab tab)
    {
        if (!await ConfirmDiscardAsync(tab)) return false;
        int index = _tabs.IndexOf(tab);
        if (index < 0) return true;
        _tabs.RemoveAt(index);
        if (_tabs.Count == 0)
        {
            _active = 0;
            NewFile();
            return true;
        }
        if (index < _active) _active--;
        _active = Math.Min(_active, _tabs.Count - 1);
        OnActiveTabChanged();
        return true;
    }

    /// <summary>Walks through every modified tab asking to save. Returns false if the user cancelled.</summary>
    public async Task<bool> ConfirmCloseAllAsync()
    {
        foreach (var tab in _tabs.ToList())
            if (!await ConfirmDiscardAsync(tab)) return false;
        return true;
    }

    private void Quit()
    {
        if (TopLevel.GetTopLevel(this) is Window w) w.Close();
    }

    private async Task LoadCharRomAsync()
    {
        if (Storage is not { } storage) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "LOAD CHARACTER ROM", AllowMultiple = false });
        Focus();
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        try
        {
            _font = BitmapFont.LoadWithCharRom(path);
            _settings.CharRomPath = path;
            SettingsChanged();
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("CHAR ROM", $"?BAD DATA ERROR\n\n{ex.Message}\n\nUSE A 2K, 4K OR 8K COMMODORE CHARACTER ROM IMAGE.", ["OK"]);
        }
    }

    private void UseBuiltInFont()
    {
        _settings.CharRomPath = null;
        _font = BitmapFont.LoadBuiltIn();
        SettingsChanged();
    }

    // ================================================================ view settings

    private void SetMachine(MachineKind machine)
    {
        _settings.Machine = machine;
        _menus = null; // labels (color names) depend on the machine
        SettingsChanged();
    }

    private void SetSizeMode(SizeMode mode)
    {
        _settings.SizeMode = mode;
        SettingsChanged();
    }

    private void SetScale(int scale)
    {
        _settings.Scale = Math.Clamp(scale, 1, 4);
        SettingsChanged();
    }

    private void ResetColors()
    {
        _settings.Colors.Remove(_settings.Machine);
        SettingsChanged();
    }

    // ================================================================ clipboard

    private IClipboard? Clipboard => TopLevel.GetTopLevel(this)?.Clipboard;

    private async Task<string?> GetClipboardTextAsync()
    {
        if (Clipboard is not { } cb) return null;
        try { return await cb.TryGetTextAsync(); }
        catch { return null; }
    }

    /// <summary>Text to copy: the selection, or the whole current line (with its line break) when nothing is selected.</summary>
    private string CopyText(EditorTab tab) =>
        tab.HasSelection ? tab.SelectedText : tab.Document[tab.Caret.Line] + "\n";

    private async Task CopyAsync()
    {
        if (Clipboard is not { } cb) return;
        await cb.SetTextAsync(CopyText(ActiveTab).Replace("\n", ActiveTab.Document.NewLine));
    }

    private async Task CutAsync()
    {
        if (Clipboard is not { } cb) return;
        var tab = ActiveTab;
        await cb.SetTextAsync(CopyText(tab).Replace("\n", tab.Document.NewLine));
        Edit(t =>
        {
            if (!t.HasSelection)
            {
                int line = t.Caret.Line;
                var start = new TextPos(line, 0);
                var end = line + 1 < t.Document.LineCount ? new TextPos(line + 1, 0) : new TextPos(line, t.Document[line].Length);
                t.Select(start, end);
            }
            t.Paste("");
        });
    }

    private async Task PasteAsync()
    {
        var text = await GetClipboardTextAsync();
        if (string.IsNullOrEmpty(text)) return;
        Edit(t => t.Paste(text));
    }

    // ================================================================ dialogs

    private async Task GoToLineAsync()
    {
        var tab = ActiveTab;
        var answer = await ShowInputAsync("GO TO", $"LINE NUMBER (1-{tab.Document.LineCount}):", (tab.Caret.Line + 1).ToString());
        if (answer is null) return;
        if (int.TryParse(answer.Trim(), out int line)) Edit(t => t.GoToLine(line));
        else await ShowMessageAsync("GO TO", "?SYNTAX ERROR", ["OK"]);
    }

    private Task ShowKeysAsync()
    {
        string word = IsMac ? Opt : "CTRL+";
        var text = string.Join('\n',
            $"{Opt.TrimEnd('+')} (TAP) OR F10  MENU",
            "MOUSE AT TOP     MENU",
            $"{Mod}N/O/S/W        NEW/OPEN/SAVE/CLOSE",
            $"{Mod}Z / {(IsMac ? Mod + Sh + "Z" : Mod + "Y")}      UNDO / REDO",
            $"{Mod}X/C/V          CUT/COPY/PASTE",
            $"{Mod}F / {(IsMac ? Mod + Opt + "F" : Mod + "H")}      FIND / REPLACE",
            $"{(IsMac ? Mod + "G" : "F3")}               FIND NEXT",
            $"{(IsMac ? Mod + "L" : Mod + "G")}               GO TO LINE",
            $"{word}←/→          WORD LEFT/RIGHT",
            "CTRL+TAB         NEXT TAB",
            $"{Mod}1..4           SCALE 1X..4X",
            "INS              INSERT/OVERWRITE",
            "TAB / " + Sh + "TAB      INDENT / OUTDENT");
        return ShowMessageAsync("KEYBOARD", text, ["OK"]);
    }

    private Task ShowAboutAsync() => ShowMessageAsync("ABOUT",
        "C64EDIT V1.0\n\nA MODERN TEXT EDITOR WEARING A COMMODORE 64 COSTUME.\n\n" +
        $"FONT: {_font.Source}\nMACHINE: {_profile.Name}  {_cols}X{_rows}",
        ["OK"]);

    // ================================================================ drag & drop

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        var files = e.DataTransfer.TryGetFiles();
        if (files is null) return;
        foreach (var file in files)
            if (file.TryGetLocalPath() is { } path && File.Exists(path)) OpenPath(path);
        await Task.CompletedTask;
    }
}
