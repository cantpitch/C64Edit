using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using C64Edit.UI;

namespace C64Edit;

public sealed class MainWindow : Window
{
    private readonly Settings _settings;
    private readonly ScreenControl _screen;
    private bool _closeConfirmed;

    public MainWindow(Settings settings, IReadOnlyList<string> files)
    {
        _settings = settings;
        Title = "C64Edit";
        Background = Brushes.Black;
        _screen = new ScreenControl(settings);
        Content = _screen;

        foreach (var file in files) _screen.OpenPath(file, createIfMissing: true);
        if (_screen.Tabs.Count == 0)
        {
            _screen.NewFile();
            _screen.ShowSplash();
        }

        if (settings.SizeMode == SizeMode.Dynamic && settings.DynamicWidth > 100 && settings.DynamicHeight > 100)
        {
            Width = settings.DynamicWidth;
            Height = settings.DynamicHeight;
        }
        else
        {
            var size = _screen.TrueClientSize();
            Width = size.Width;
            Height = size.Height;
        }

        Opened += (_, _) =>
        {
            ApplySizeMode();
#if DEBUG
            if (Environment.GetEnvironmentVariable("C64EDIT_SCRIPT") is { Length: > 0 } script)
                Avalonia.Threading.DispatcherTimer.RunOnce(() => _ = _screen.RunDebugScriptAsync(script), TimeSpan.FromMilliseconds(500));
#endif
        };
        ScalingChanged += (_, _) => _screen.SettingsChanged();
        Closing += OnClosing;
    }

    /// <summary>True size locks the window to 40x25 / 80x25 plus border; dynamic lets it resize freely.</summary>
    public void ApplySizeMode()
    {
        var min = _screen.MinimumClientSize();
        if (_settings.SizeMode == SizeMode.TrueSize)
        {
            var size = _screen.TrueClientSize();
            MinWidth = 0;
            MinHeight = 0;
            CanResize = false;
            Width = size.Width;
            Height = size.Height;
        }
        else
        {
            CanResize = true;
            MinWidth = min.Width;
            MinHeight = min.Height;
            if (Width < min.Width) Width = min.Width;
            if (Height < min.Height) Height = min.Height;
        }
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeConfirmed) return;
        e.Cancel = true;
        if (!await _screen.ConfirmCloseAllAsync()) return;
        if (_settings.SizeMode == SizeMode.Dynamic)
        {
            _settings.DynamicWidth = Width;
            _settings.DynamicHeight = Height;
        }
        _settings.Save();
        _closeConfirmed = true;
        Close();
    }
}
