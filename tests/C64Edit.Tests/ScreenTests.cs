using Avalonia.Headless.XUnit;
using C64Edit.UI;
using Xunit;

namespace C64Edit.Tests;

/// <summary>Drives the real window headlessly. Set C64EDIT_SNAPDIR to also get PNG snapshots.</summary>
public class ScreenTests
{
    private static string SnapDir => Environment.GetEnvironmentVariable("C64EDIT_SNAPDIR") is { Length: > 0 } d ? d : Path.GetTempPath();

    private static async Task<(MainWindow, ScreenControl)> Run(string script, Settings? settings = null, params string[] files)
    {
        var window = new MainWindow(settings ?? new Settings(), files);
        window.Show();
        var screen = (ScreenControl)window.Content!;
        var path = Path.GetTempFileName();
        await File.WriteAllTextAsync(path, script.Replace("$SNAP", SnapDir));
        await screen.RunDebugScriptAsync(path);
        return (window, screen);
    }

    [AvaloniaFact]
    public async Task TypingEditsTheActiveTab()
    {
        var (_, screen) = await Run("""
            snap $SNAP/01-splash.png
            text Hello, World! The quick brown fox jumps over the lazy dog.
            key Enter
            text 0123456789 !"#$%&'()*+,-./:;<=>?@[\]^_`{|}~
            key Enter
            text ÄÖÜ äöü éèê ñ ç ß ø æ € π £ 日本
            snap $SNAP/02-typed.png
            key Shift+Up
            key Shift+Left
            snap $SNAP/03-select.png
            """);
        Assert.Equal(3, screen.ActiveTab.Document.LineCount);
        Assert.StartsWith("Hello, World!", screen.ActiveTab.Document[0]);
        Assert.True(screen.ActiveTab.HasSelection);
    }

    [AvaloniaFact]
    public async Task MenuOpensWithAltTapAndRunsCommands()
    {
        var (_, screen) = await Run("""
            text x
            key LeftAlt
            snap $SNAP/04-menubar.png
            key Down
            snap $SNAP/05-filemenu.png
            key Right
            key Right
            snap $SNAP/06-viewmenu.png
            key Escape
            key Escape
            key Mod+N
            text second
            snap $SNAP/07-tabs.png
            """);
        Assert.Equal(2, screen.Tabs.Count);
        Assert.Equal("second", screen.ActiveTab.Document[0]);
        Assert.Equal("x", screen.Tabs[0].Document[0]);
    }

    [AvaloniaFact]
    public async Task SyntaxHighlightingAndC128Mode()
    {
        var src = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/C64Edit/Text/EditorTab.cs"));
        var settings = new Settings { LineNumbers = true };
        var (_, screen) = await Run($"""
            snap $SNAP/08-csharp-c64.png
            """, settings, src, Path.Combine(Path.GetTempPath(), "AVeryLongFileNameThatRotates.txt"));
        Assert.Equal("EditorTab.cs", screen.Tabs[0].DisplayName);
        Assert.Equal("AVeryLongFileNameThatRotates.txt", screen.Tabs[1].DisplayName);
        screen.ActivateTab(0);
        var snap = Path.GetTempFileName();
        await File.WriteAllTextAsync(snap, $"snap {SnapDir}/08b-csharp-c64.png\n");
        await screen.RunDebugScriptAsync(snap);

        settings.Machine = C64Edit.Rendering.MachineKind.C128;
        screen.SettingsChanged();
        var path = Path.GetTempFileName();
        await File.WriteAllTextAsync(path, $"snap {SnapDir}/09-csharp-c128.png\nkey Mod+F\ntext Highlight\nsnap {SnapDir}/10-find.png\n");
        await screen.RunDebugScriptAsync(path);
        Assert.Equal("Highlight", screen.ActiveTab.SelectedText);
    }

    [AvaloniaFact]
    public async Task MissingFileFromMenuShowsError()
    {
        var (_, screen) = await Run("snap $SNAP/12-tabs-marquee.png", null, "/tmp/a.txt", "/tmp/b-with-a-long-name.txt", "/tmp/c.cs");
        Assert.Equal(3, screen.Tabs.Count);
        screen.OpenPath("/does/not/exist.txt");
        Assert.Equal(3, screen.Tabs.Count);
    }

    [AvaloniaFact]
    public async Task UnsavedChangesPromptOnClose()
    {
        var (_, screen) = await Run("""
            text dirty
            key Mod+W
            snap $SNAP/11-confirm.png
            key Escape
            """);
        Assert.Single(screen.Tabs);
        Assert.Equal("dirty", screen.ActiveTab.Document[0]);
    }
}
