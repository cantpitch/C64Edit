using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(C64Edit.Tests.TestApp))]

namespace C64Edit.Tests;

public static class TestApp
{
    public static AppBuilder BuildAvaloniaApp()
    {
        Environment.SetEnvironmentVariable("C64EDIT_SETTINGS_DIR", Path.Combine(Path.GetTempPath(), "c64edit-tests"));
        return AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }
}
