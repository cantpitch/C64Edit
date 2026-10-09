using Avalonia;
using Avalonia.Input;
using Avalonia.Threading;
using SkiaSharp;

namespace C64Edit.UI;

/// <summary>
/// Test/debug helper (debug builds also honor C64EDIT_SCRIPT): set C64EDIT_SCRIPT to a file of commands to drive the editor through its real
/// input handlers and dump frames to PNG. Commands, one per line:
///   key [Mods+]Key     e.g. "key Ctrl+N", "key Shift+Down", "key LeftAlt" (press and release)
///   text abc           typed text
///   click X Y [N]      click at machine-pixel coordinates (N = click count)
///   move X Y           move the mouse
///   wait MS
///   snap file.png      write the current frame (2x) to a PNG
///   quit
/// </summary>
public sealed partial class ScreenControl
{
    public async Task RunDebugScriptAsync(string path)
    {
        foreach (var raw in await File.ReadAllLinesAsync(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var parts = line.Split(' ', 2);
            var arg = parts.Length > 1 ? parts[1] : "";
            switch (parts[0])
            {
                case "key": SendKey(arg); break;
                case "text": RaiseEvent(new TextInputEventArgs { RoutedEvent = TextInputEvent, Text = arg }); break;
                case "wait": await Task.Delay(int.Parse(arg)); break;
                case "click":
                {
                    var a = arg.Split(' ').Select(int.Parse).ToArray();
                    SimulateClick(a[0], a[1], a.Length > 2 ? a[2] : 1);
                    break;
                }
                case "move":
                {
                    var a = arg.Split(' ').Select(int.Parse).ToArray();
                    MenuPointerMoved(a[0], a[1]);
                    break;
                }
                case "snap": Snapshot(arg); break;
                case "quit":
                    Environment.Exit(0);
                    return;
            }
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        }
    }

    private void SendKey(string spec)
    {
        var mods = KeyModifiers.None;
        var parts = spec.Split('+');
        foreach (var m in parts[..^1])
        {
            mods |= m switch
            {
                "Ctrl" => KeyModifiers.Control,
                "Shift" => KeyModifiers.Shift,
                "Alt" => KeyModifiers.Alt,
                "Cmd" => KeyModifiers.Meta,
                "Mod" => CommandModifier,
                _ => KeyModifiers.None,
            };
        }
        var key = Enum.Parse<Key>(parts[^1]);
        RaiseEvent(new KeyEventArgs { RoutedEvent = KeyDownEvent, Key = key, KeyModifiers = mods });
        RaiseEvent(new KeyEventArgs { RoutedEvent = KeyUpEvent, Key = key, KeyModifiers = mods });
    }

    private void SimulateClick(int mx, int my, int clicks)
    {
        // Mirrors OnPointerPressed for the left button without needing a real pointer device.
        _lastMx = mx;
        _lastMy = my;
        if (_splash) { _splash = false; Invalidate(); }
        if (DialogPointerPressed(mx, my)) return;
        if (MenuPointerPressed(mx, my)) return;
        if (TabAt(mx, my) is { } tabIndex) { ActivateTab(tabIndex); return; }
        if (FindPointerPressed(mx, my)) return;
        var tab = ActiveTab;
        var pos = PosAt(mx, my);
        if (clicks == 2) { var (s, e) = tab.WordAt(pos); tab.Select(s, e); }
        else tab.SetCaret(pos, false);
        AfterEdit();
    }

    private void Snapshot(string file)
    {
        ComposeFrame();
        int scale = 2;
        using var bmp = new SKBitmap(_fb.Width * scale, _fb.Height * scale, SKColorType.Bgra8888, SKAlphaType.Opaque);
        for (int y = 0; y < bmp.Height; y++)
            for (int x = 0; x < bmp.Width; x++)
            {
                uint c = _fb.Pixels[(y / scale) * _fb.Width + x / scale];
                bmp.SetPixel(x, y, new SKColor(c));
            }
        using var data = bmp.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(file, data.ToArray());
    }
}
