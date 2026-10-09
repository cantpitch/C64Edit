namespace C64Edit.Rendering;

public enum MachineKind
{
    C64,
    C128,
}

/// <summary>Token classes produced by the syntax highlighter.</summary>
public enum TokenKind : byte
{
    Text,
    Keyword,
    Type,
    String,
    Number,
    Comment,
    Directive,
    Tag,
    Attribute,
}

/// <summary>
/// Everything that differs between the C64 (VIC-II, 40x25) and the C128 80-column (VDC, 80x25) look.
/// All measurements are in "machine pixels"; the screen control scales them up by an integer factor.
/// </summary>
public sealed class MachineProfile
{
    public required MachineKind Kind { get; init; }
    public required string Name { get; init; }
    public required uint[] Palette { get; init; }
    public required string[] ColorNames { get; init; }

    /// <summary>Width/height of one character cell. The C128 VDC has 640x200 pixels shown on a 4:3
    /// monitor, so each pixel is roughly twice as tall as it is wide: we draw every font row twice.</summary>
    public required int CellWidth { get; init; }
    public required int CellHeight { get; init; }

    public required int NativeColumns { get; init; }
    public required int NativeRows { get; init; }

    public required int BorderLeft { get; init; }
    public required int BorderRight { get; init; }
    public required int BorderTop { get; init; }
    public required int BorderBottom { get; init; }

    public required int DefaultBorder { get; init; }
    public required int DefaultBackground { get; init; }
    public required int DefaultText { get; init; }

    /// <summary>Palette index per <see cref="TokenKind"/>; index 0 (Text) means "use the text color".</summary>
    public required int[] SyntaxColors { get; init; }

    /// <summary>Wide label area of a file tab, in characters.</summary>
    public required int TabLabelWidth { get; init; }

    /// <summary>Cursor blink half-period. The C64 KERNAL toggles the cursor every 20 jiffies.</summary>
    public TimeSpan BlinkInterval { get; init; } = TimeSpan.FromMilliseconds(333);

    public static readonly MachineProfile C64 = new()
    {
        Kind = MachineKind.C64,
        Name = "C64",
        // "Colodore" palette by Philip "Pepto" Timmermann - a measured approximation of a real VIC-II.
        Palette =
        [
            0xFF000000, 0xFFFFFFFF, 0xFF813338, 0xFF75CEC8,
            0xFF8E3C97, 0xFF56AC4D, 0xFF2E2C9B, 0xFFEDF171,
            0xFF8E5029, 0xFF553800, 0xFFC46C71, 0xFF4A4A4A,
            0xFF7B7B7B, 0xFFA9FF9F, 0xFF706DEB, 0xFFB2B2B2,
        ],
        ColorNames =
        [
            "BLACK", "WHITE", "RED", "CYAN", "PURPLE", "GREEN", "BLUE", "YELLOW",
            "ORANGE", "BROWN", "LT.RED", "DK.GREY", "GREY", "LT.GREEN", "LT.BLUE", "LT.GREY",
        ],
        CellWidth = 8,
        CellHeight = 8,
        NativeColumns = 40,
        NativeRows = 25,
        BorderLeft = 32,
        BorderRight = 32,
        BorderTop = 32,
        BorderBottom = 32,
        DefaultBorder = 14,
        DefaultBackground = 6,
        DefaultText = 14,
        //              Text Keyword Type String Number Comment Directive Tag Attribute
        SyntaxColors = [0, 1, 3, 13, 7, 12, 10, 3, 7],
        TabLabelWidth = 10,
    };

    public static readonly MachineProfile C128 = new()
    {
        Kind = MachineKind.C128,
        Name = "C128",
        // The VDC drives an RGBI monitor: the same 16 colors as CGA.
        Palette =
        [
            0xFF000000, 0xFF555555, 0xFF0000AA, 0xFF5555FF,
            0xFF00AA00, 0xFF55FF55, 0xFF00AAAA, 0xFF55FFFF,
            0xFFAA0000, 0xFFFF5555, 0xFFAA00AA, 0xFFFF55FF,
            0xFFAA5500, 0xFFFFFF55, 0xFFAAAAAA, 0xFFFFFFFF,
        ],
        ColorNames =
        [
            "BLACK", "DK.GREY", "DK.BLUE", "LT.BLUE", "DK.GREEN", "LT.GREEN", "DK.CYAN", "LT.CYAN",
            "DK.RED", "LT.RED", "DK.PURPLE", "LT.PURPLE", "BROWN", "YELLOW", "LT.GREY", "WHITE",
        ],
        CellWidth = 8,
        CellHeight = 16,
        NativeColumns = 80,
        NativeRows = 25,
        BorderLeft = 16,
        BorderRight = 16,
        BorderTop = 32,
        BorderBottom = 32,
        DefaultBorder = 0,
        DefaultBackground = 0,
        DefaultText = 7,
        //              Text Keyword Type String Number Comment Directive Tag Attribute
        SyntaxColors = [0, 13, 5, 9, 11, 6, 3, 5, 13],
        TabLabelWidth = 14,
    };

    public static MachineProfile For(MachineKind kind) => kind == MachineKind.C128 ? C128 : C64;
}
