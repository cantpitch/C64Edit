using System.Text.Json;
using System.Text.Json.Serialization;
using C64Edit.Rendering;

namespace C64Edit;

public enum SizeMode
{
    /// <summary>Window is locked to the machine's native text size (40x25 or 80x25) plus border.</summary>
    TrueSize,
    /// <summary>Window resizes freely; the character grid grows or shrinks, glyph size stays fixed.</summary>
    Dynamic,
}

public sealed class ColorScheme
{
    public int Border { get; set; }
    public int Background { get; set; }
    public int Text { get; set; }
}

public sealed class Settings
{
    public MachineKind Machine { get; set; } = MachineKind.C64;
    public SizeMode SizeMode { get; set; } = SizeMode.TrueSize;
    public int Scale { get; set; } = 2;
    public bool LineNumbers { get; set; }
    public bool WordWrap { get; set; } = true;
    public bool StatusLine { get; set; } = true;
    public bool SyntaxHighlighting { get; set; } = true;
    public bool ShowWhitespace { get; set; }
    public int TabSize { get; set; } = 4;
    public string? CharRomPath { get; set; }
    public double DynamicWidth { get; set; }
    public double DynamicHeight { get; set; }
    public Dictionary<MachineKind, ColorScheme> Colors { get; set; } = new();

    public ColorScheme ColorsFor(MachineKind kind)
    {
        if (!Colors.TryGetValue(kind, out var scheme))
        {
            var p = MachineProfile.For(kind);
            scheme = new ColorScheme { Border = p.DefaultBorder, Background = p.DefaultBackground, Text = p.DefaultText };
            Colors[kind] = scheme;
        }
        return scheme;
    }

    private static string FilePath => Path.Combine(
        Environment.GetEnvironmentVariable("C64EDIT_SETTINGS_DIR") is { Length: > 0 } dir
            ? dir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "C64Edit"),
        "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), JsonOptions);
                if (s is not null)
                {
                    s.Scale = Math.Clamp(s.Scale, 1, 4);
                    s.TabSize = Math.Clamp(s.TabSize, 1, 16);
                    return s;
                }
            }
        }
        catch
        {
            // A broken settings file should never stop the editor from starting.
        }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch
        {
            // Not being able to persist settings is not worth interrupting the user for.
        }
    }
}
