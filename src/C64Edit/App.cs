using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace C64Edit;

public sealed class App : Application
{
    public override void Initialize()
    {
        Name = "C64Edit";
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var settings = Settings.Load();
            var files = (desktop.Args ?? []).Where(a => !a.StartsWith('-')).ToList();
            desktop.MainWindow = new MainWindow(settings, files);
        }
        base.OnFrameworkInitializationCompleted();
    }
}
