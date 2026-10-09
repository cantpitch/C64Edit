using System.Globalization;
using System.Reflection;
using System.Text;
using SkiaSharp;

namespace C64Edit.Rendering;

/// <summary>
/// An 8x8 one-bit font keyed by Unicode code point. Each glyph is a ulong: byte 0 is the top row,
/// bit 7 of each byte is the leftmost pixel (the same layout as a Commodore character ROM).
/// </summary>
public sealed class BitmapFont
{
    private readonly Dictionary<int, ulong> _glyphs = new();
    private readonly Dictionary<int, ulong> _derived = new();

    public string Source { get; private set; } = "BUILT-IN";

    private BitmapFont() { }

    public static BitmapFont LoadBuiltIn()
    {
        var font = new BitmapFont();
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("C64Edit.font8x8.txt")
            ?? throw new InvalidOperationException("Built-in font resource is missing.");
        using var reader = new StreamReader(stream);
        font.ParseText(reader.ReadToEnd());
        return font;
    }

    /// <summary>
    /// Overlays glyphs from a Commodore character ROM image (2 KB set, 4 KB C64 "chargen" or 8 KB C128 ROM).
    /// The upper/lower case set is used; characters the ROM lacks (backslash, braces, ...) keep the built-in look.
    /// </summary>
    public static BitmapFont LoadWithCharRom(string path)
    {
        var data = File.ReadAllBytes(path);
        if (data.Length < 2048)
            throw new InvalidDataException("A character ROM must be at least 2048 bytes.");

        var font = LoadBuiltIn();
        // In a 4K/8K image the second 2K bank is the upper/lower case set.
        int bank = data.Length >= 4096 ? 2048 : 0;
        foreach (var (codePoint, screenCode) in PetsciiScreenCodes)
        {
            ulong g = 0;
            for (int row = 0; row < 8; row++)
                g |= (ulong)data[bank + screenCode * 8 + row] << (row * 8);
            font._glyphs[codePoint] = g;
        }
        font._derived.Clear();
        font.Source = Path.GetFileName(path).ToUpperInvariant();
        return font;
    }

    private void ParseText(string text)
    {
        int? current = null;
        int row = 0;
        ulong bits = 0;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            bool inGlyph = current is not null && row < 8;
            // Glyph rows may start with '#' (ink), so '#' only starts a comment between glyphs.
            if (line.Length == 0 || (!inGlyph && line.StartsWith('#'))) continue;
            if (line.StartsWith("U+", StringComparison.Ordinal))
            {
                var hex = line[2..].Split(' ', 2)[0];
                current = int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                row = 0;
                bits = 0;
                continue;
            }
            if (current is null || row >= 8) continue;
            for (int x = 0; x < 8 && x < line.Length; x++)
                if (line[x] == '#') bits |= 1UL << (row * 8 + (7 - x));
            row++;
            if (row == 8) _glyphs[current.Value] = bits;
        }
    }

    /// <summary>Returns the row byte (bit 7 = leftmost pixel) of a glyph.</summary>
    public static byte Row(ulong glyph, int row) => (byte)(glyph >> (row * 8));

    public ulong Get(int codePoint)
    {
        if (_glyphs.TryGetValue(codePoint, out var g)) return g;
        if (_derived.TryGetValue(codePoint, out g)) return g;
        g = Derive(codePoint);
        _derived[codePoint] = g;
        return g;
    }

    public bool Has(int codePoint) => _glyphs.ContainsKey(codePoint);

    private ulong Derive(int codePoint)
    {
        // Non-breaking space and friends look like a space.
        if (codePoint is 0xA0 or 0x2007 or 0x202F || (codePoint >= 0x2000 && codePoint <= 0x200A))
            return _glyphs[' '];

        if (TryCompose(codePoint, out var composed)) return composed;
        if (TryRasterize(codePoint, out var raster)) return raster;
        return UnknownGlyph;
    }

    /// <summary>A reverse-video question mark, so unsupported characters stand out but stay readable.</summary>
    private ulong UnknownGlyph => ~_glyphs['?'];

    /// <summary>Builds accented Latin letters from the base glyph plus a small accent drawing.</summary>
    private bool TryCompose(int codePoint, out ulong glyph)
    {
        glyph = 0;
        if (codePoint > 0xFFFF || char.IsSurrogate((char)codePoint)) return false;
        var decomposed = ((char)codePoint).ToString().Normalize(NormalizationForm.FormD);
        if (decomposed.Length != 2 || !_glyphs.TryGetValue(decomposed[0], out var baseGlyph)) return false;

        byte accent;
        bool below = false;
        switch (decomposed[1])
        {
            case '̀': accent = 0b00110000; break; // grave
            case '́': accent = 0b00001100; break; // acute
            case '̂': accent = 0b00111100; break; // circumflex (drawn flat at this size)
            case '̃': accent = 0b01101100; break; // tilde
            case '̈': accent = 0b01100110; break; // diaeresis
            case '̊': accent = 0b00011000; break; // ring
            case '̌': accent = 0b00111100; break; // caron
            case '̄': accent = 0b01111110; break; // macron
            case '̧': accent = 0b00011000; below = true; break; // cedilla
            case '̨': accent = 0b00001100; below = true; break; // ogonek
            default: return false;
        }

        var rows = new byte[8];
        for (int r = 0; r < 8; r++) rows[r] = Row(baseGlyph, r);

        if (below)
        {
            rows[7] = accent;
        }
        else if (rows[0] == 0 && rows[1] == 0)
        {
            // Lowercase: plenty of room above the x-height.
            rows[0] = accent;
        }
        else
        {
            // Capitals fill rows 0-6: squeeze the letter down one row by dropping row 1.
            for (int r = 1; r < 7; r++) rows[r] = r == 1 ? rows[0] : rows[r];
            rows[0] = accent;
        }

        for (int r = 0; r < 8; r++) glyph |= (ulong)rows[r] << (r * 8);
        return true;
    }

    /// <summary>Last resort: render the character with a system font into an 8x8 cell, without anti-aliasing.</summary>
    private static bool TryRasterize(int codePoint, out ulong glyph)
    {
        glyph = 0;
        try
        {
            var text = char.ConvertFromUtf32(codePoint);
            using var typeface = SKFontManager.Default.MatchCharacter(codePoint) ?? SKTypeface.Default;
            using var font = new SKFont(typeface, 9) { Edging = SKFontEdging.Alias, Hinting = SKFontHinting.Full };
            using var bitmap = new SKBitmap(new SKImageInfo(8, 8, SKColorType.Gray8, SKAlphaType.Opaque));
            using var canvas = new SKCanvas(bitmap);
            using var paint = new SKPaint { Color = SKColors.White, IsAntialias = false };
            canvas.Clear(SKColors.Black);
            float width = font.MeasureText(text);
            canvas.DrawText(text, MathF.Max(0, (8 - width) / 2), 7, SKTextAlign.Left, font, paint);
            canvas.Flush();

            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                    if (bitmap.GetPixel(x, y).Red > 127)
                        glyph |= 1UL << (y * 8 + (7 - x));
            return glyph != 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Unicode code point -> screen code in the C64 upper/lower case character set.</summary>
    private static readonly (int, int)[] PetsciiScreenCodes = BuildScreenCodeMap();

    private static (int, int)[] BuildScreenCodeMap()
    {
        var map = new List<(int, int)>
        {
            ('@', 0), ('[', 27), ('£', 28), (']', 29), ('↑', 30), ('←', 31),
            ('─', 64), ('┼', 91), ('│', 93), ('▒', 102),
            ('▌', 97), ('▄', 98), ('├', 107), ('└', 109), ('┐', 110),
            ('┌', 112), ('┴', 113), ('┬', 114), ('┤', 115), ('┘', 125),
            ('✓', 122),
        };
        for (int c = 'a'; c <= 'z'; c++) map.Add((c, c - 'a' + 1));
        for (int c = 'A'; c <= 'Z'; c++) map.Add((c, c - 'A' + 65));
        for (int c = ' '; c <= '?'; c++) map.Add((c, c));
        return map.ToArray();
    }
}
