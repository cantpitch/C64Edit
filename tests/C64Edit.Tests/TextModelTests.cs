using C64Edit.Text;
using Xunit;

namespace C64Edit.Tests;

public class TextModelTests
{
    private static EditorTab NewTab(string text = "")
    {
        var tab = new EditorTab(new TextDocument(), null);
        if (text.Length > 0) tab.Paste(text);
        tab.SetCaret(new TextPos(0, 0), false);
        return tab;
    }

    [Fact]
    public void TypingIsCoalescedIntoOneUndoStep()
    {
        var tab = NewTab();
        foreach (var c in "hello") tab.TypeText(c.ToString());
        Assert.Equal("hello", tab.Document[0]);
        tab.Undo();
        Assert.Equal("", tab.Document[0]);
        tab.Redo();
        Assert.Equal("hello", tab.Document[0]);
    }

    [Fact]
    public void NewLineKeepsIndentationAndUndoRestoresIt()
    {
        var tab = NewTab("    if (x) {");
        tab.End(false);
        tab.NewLine();
        Assert.Equal(2, tab.Document.LineCount);
        Assert.Equal("        ", tab.Document[1]);
        Assert.Equal(new TextPos(1, 8), tab.Caret);
        tab.Undo();
        Assert.Equal(1, tab.Document.LineCount);
    }

    [Fact]
    public void ModifiedFlagFollowsUndoToSavePoint()
    {
        var tab = NewTab();
        Assert.False(tab.Document.IsModified);
        tab.TypeText("x");
        Assert.True(tab.Document.IsModified);
        tab.Undo();
        Assert.False(tab.Document.IsModified);
    }

    [Fact]
    public void SoftWrapBreaksAtSpacesAndMapsPositionsBothWays()
    {
        var tab = NewTab("the quick brown fox jumps");
        tab.WrapWidth = 10;
        Assert.Equal(new[] { 0, 10, 20 }, tab.Segments(0));
        Assert.Equal(3, tab.VisualRowCount);
        var pos = new TextPos(0, 12);
        var (row, x) = tab.VisualOf(pos);
        Assert.Equal((1, 2), (row, x));
        Assert.Equal(pos, tab.PosFromVisual(row, x));
    }

    [Fact]
    public void LongWordsAreHardWrapped()
    {
        var tab = NewTab(new string('a', 25));
        tab.WrapWidth = 10;
        Assert.Equal(new[] { 0, 10, 20 }, tab.Segments(0));
    }

    [Fact]
    public void TabsExpandToTabStops()
    {
        var tab = NewTab("\tx\ty");
        tab.TabSize = 4;
        Assert.Equal(4, tab.DisplayCol(tab.Document[0], 1));
        Assert.Equal(8, tab.DisplayCol(tab.Document[0], 3));
    }

    [Fact]
    public void FindWrapsAroundAndUsesSmartCase()
    {
        var tab = NewTab("Foo bar foo\nfoo");
        Assert.True(tab.Find("foo", true));
        Assert.Equal(new TextPos(0, 0), tab.SelectionStart);
        Assert.True(tab.Find("foo", true));
        Assert.Equal(new TextPos(0, 8), tab.SelectionStart);
        Assert.True(tab.Find("foo", true));
        Assert.Equal(new TextPos(1, 0), tab.SelectionStart);
        Assert.True(tab.Find("foo", true));
        Assert.Equal(new TextPos(0, 0), tab.SelectionStart);
        Assert.True(tab.Find("Foo", false));
        Assert.Equal(new TextPos(0, 0), tab.SelectionStart);
        Assert.True(tab.Find("foo", false));
        Assert.Equal(new TextPos(1, 0), tab.SelectionStart);
    }

    [Fact]
    public void ReplaceAllIsOneUndoStep()
    {
        var tab = NewTab("a-a-a");
        Assert.Equal(3, tab.ReplaceAll("a", "bb"));
        Assert.Equal("bb-bb-bb", tab.Document[0]);
        tab.Undo();
        Assert.Equal("a-a-a", tab.Document[0]);
    }

    [Fact]
    public void IndentAndOutdentSelection()
    {
        var tab = NewTab("a\nb\nc");
        tab.Select(new TextPos(0, 0), new TextPos(2, 1));
        tab.Tab(false);
        Assert.Equal(new[] { "    a", "    b", "    c" }, tab.Document.Lines);
        tab.Tab(true);
        Assert.Equal(new[] { "a", "b", "c" }, tab.Document.Lines);
    }

    [Fact]
    public void LoadAndSavePreserveCrLf()
    {
        var path = Path.Combine(Path.GetTempPath(), $"c64edit-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, "one\r\ntwo\r\n");
        try
        {
            var doc = TextDocument.Load(path);
            Assert.Equal("\r\n", doc.NewLine);
            Assert.Equal(3, doc.LineCount);
            doc.Save(path);
            Assert.Equal("one\r\ntwo\r\n", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }
}
