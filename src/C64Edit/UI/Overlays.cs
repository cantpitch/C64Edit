namespace C64Edit.UI;

/// <summary>A one-line text field used by dialogs and the find panel.</summary>
public sealed class LineEditor
{
    public string Text { get; private set; } = "";
    public int Caret { get; private set; }
    public bool SelectAllOnType { get; set; }

    public LineEditor(string initial = "")
    {
        Set(initial);
    }

    public void Set(string text)
    {
        Text = text.Replace("\r", "").Replace("\n", " ");
        Caret = Text.Length;
        SelectAllOnType = Text.Length > 0;
    }

    public void Insert(string s)
    {
        s = s.Replace("\r", "").Replace("\n", " ");
        if (SelectAllOnType) { Text = ""; Caret = 0; SelectAllOnType = false; }
        Text = Text.Insert(Caret, s);
        Caret += s.Length;
    }

    public void Backspace()
    {
        if (SelectAllOnType) { Set(""); return; }
        if (Caret == 0) return;
        Text = Text.Remove(Caret - 1, 1);
        Caret--;
    }

    public void Delete()
    {
        if (SelectAllOnType) { Set(""); return; }
        if (Caret >= Text.Length) return;
        Text = Text.Remove(Caret, 1);
    }

    public void Left() { SelectAllOnType = false; Caret = Math.Max(0, Caret - 1); }
    public void Right() { SelectAllOnType = false; Caret = Math.Min(Text.Length, Caret + 1); }
    public void Home() { SelectAllOnType = false; Caret = 0; }
    public void End() { SelectAllOnType = false; Caret = Text.Length; }
    public void PlaceCaret(int index) { SelectAllOnType = false; Caret = Math.Clamp(index, 0, Text.Length); }

    /// <summary>First character to show so that the caret stays visible in a field <paramref name="width"/> cells wide.</summary>
    public int ScrollFor(int width) => Math.Max(0, Caret - width + 1);
}

public sealed class MessageDialog
{
    public required string Title { get; init; }
    public required string[] Lines { get; init; }
    public required string[] Buttons { get; init; }
    public int CancelIndex { get; init; }
    public int Selected { get; set; }
    public TaskCompletionSource<int> Result { get; } = new();
}

public sealed class InputDialog
{
    public required string Title { get; init; }
    public required string Prompt { get; init; }
    public required LineEditor Field { get; init; }
    public TaskCompletionSource<string?> Result { get; } = new();
}

public sealed class FindPanel
{
    public bool Visible { get; set; }
    public bool ReplaceMode { get; set; }
    public LineEditor Find { get; } = new();
    public LineEditor Replace { get; } = new();
    /// <summary>0 = find field, 1 = replace field.</summary>
    public int Focus { get; set; }
    public string Message { get; set; } = "";
    public LineEditor Focused => Focus == 1 && ReplaceMode ? Replace : Find;
    public int Rows => !Visible ? 0 : ReplaceMode ? 2 : 1;
}

public sealed class MenuItem
{
    public string Label { get; init; } = "";
    public Func<string>? DynamicLabel { get; init; }
    public string Shortcut { get; init; } = "";
    public Action? Action { get; init; }
    public Func<bool>? Checked { get; init; }
    public bool IsSeparator { get; init; }

    public string Text => DynamicLabel?.Invoke() ?? Label;

    public static MenuItem Separator => new() { IsSeparator = true };
}

public sealed class Menu
{
    public required string Title { get; init; }
    public required List<MenuItem> Items { get; init; }
    public char Mnemonic => Title[0];
}
