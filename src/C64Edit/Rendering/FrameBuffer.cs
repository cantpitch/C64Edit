namespace C64Edit.Rendering;

/// <summary>
/// A plain ARGB pixel buffer at machine resolution. Everything on screen - border, text, menus,
/// tabs - is drawn here first and then blown up with nearest-neighbour scaling.
/// </summary>
public sealed class FrameBuffer
{
    public int Width { get; private set; }
    public int Height { get; private set; }
    public uint[] Pixels { get; private set; } = [];

    public void Resize(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (width == Width && height == Height) return;
        Width = width;
        Height = height;
        Pixels = new uint[width * height];
    }

    public void Fill(uint color) => Array.Fill(Pixels, color);

    public void FillRect(int x, int y, int w, int h, uint color)
    {
        int x0 = Math.Max(0, x), y0 = Math.Max(0, y);
        int x1 = Math.Min(Width, x + w), y1 = Math.Min(Height, y + h);
        if (x1 <= x0) return;
        for (int yy = y0; yy < y1; yy++)
            Array.Fill(Pixels, color, yy * Width + x0, x1 - x0);
    }

    /// <summary>Draws one 8-pixel-wide glyph; each font row is repeated to fill <paramref name="cellHeight"/>.</summary>
    public void DrawGlyph(int x, int y, ulong glyph, int cellHeight, uint fg, uint bg)
    {
        int rowRepeat = Math.Max(1, cellHeight / 8);
        for (int row = 0; row < 8; row++)
        {
            byte bits = BitmapFont.Row(glyph, row);
            for (int rep = 0; rep < rowRepeat; rep++)
            {
                int py = y + row * rowRepeat + rep;
                if ((uint)py >= (uint)Height) continue;
                int offset = py * Width;
                for (int bit = 0; bit < 8; bit++)
                {
                    int px = x + bit;
                    if ((uint)px >= (uint)Width) continue;
                    Pixels[offset + px] = (bits & (0x80 >> bit)) != 0 ? fg : bg;
                }
            }
        }
    }
}
