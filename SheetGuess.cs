using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;

namespace AmosLikeBasic;

// Guesses how a sprite sheet is split into frames from the empty (transparent or magenta) gaps
// between the figures: 11 figures side by side in a 352 pixel wide picture are 32 pixels each.
// Frames that touch have no gaps, but the frames of an animation look alike: the picture moved one
// frame along looks much like itself, while moved half a frame it does not.
// Failing both, a strip as many times wider as it is high is taken as square frames.
public static class SheetGuess
{
    public static SpritesheetConfig Guess(Bitmap sheet)
    {
        int w = sheet.PixelSize.Width, h = sheet.PixelSize.Height;
        var px = new byte[w * h * 4];
        var handle = GCHandle.Alloc(px, GCHandleType.Pinned);
        try { sheet.CopyPixels(new PixelRect(0, 0, w, h), handle.AddrOfPinnedObject(), px.Length, w * 4); }
        finally { handle.Free(); }

        // Alpha is the fourth byte in both RGBA and BGRA; magenta is the engine's transparent colour
        var solid = new bool[w * h];
        for (int i = 0; i < solid.Length; i++)
        {
            int o = i * 4;
            bool magenta = px[o] == 255 && px[o + 1] == 0 && px[o + 2] == 255;
            solid[i] = px[o + 3] > 16 && !magenta;
        }
        return Guess(solid, px, w, h);
    }

    public static SpritesheetConfig Guess(bool[] solid, byte[] px, int w, int h)
    {
        var colHas = new bool[w];
        var rowHas = new bool[h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (solid[y * w + x]) { colHas[x] = true; rowHas[y] = true; }

        int fw = FromGaps(colHas);
        int fh = FromGaps(rowHas);
        if (fw == w) fw = FromRepeats(px, w, h, horizontal: true, w);
        // Rows are looked at in the first column only, which is filled even when the last row is not
        if (fh == h && fw < w)
        {
            var firstColumn = new bool[h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < fw && !firstColumn[y]; x++)
                    firstColumn[y] = solid[y * w + x];
            fh = FromGaps(firstColumn);
        }
        // (a picture about as high as a frame is wide is one row already)
        if (fh == h && h > fw * 3 / 2) fh = FromRepeats(px, w, h, horizontal: false, fw);

        // Only one direction found: square frames (tiles) when they fit evenly
        if (fw == w && fh < h && w % fh == 0) fw = fh;
        else if (fh == h && fw < w && h % fw == 0) fh = fw;
        // Nothing found: square frames along a strip, or the whole picture as one frame
        else if (fw == w && fh == h)
        {
            if (w > h && w % h == 0) fw = h;
            else if (h > w && h % w == 0) fh = w;
        }

        // The count runs to the last frame that has anything in it
        int cols = w / fw, rows = h / fh, count = 1;
        for (int cell = 0; cell < cols * rows; cell++)
        {
            int cx = cell % cols * fw, cy = cell / cols * fh;
            for (int y = cy; y < cy + fh && count <= cell; y++)
                for (int x = cx; x < cx + fw; x++)
                    if (solid[y * w + x]) { count = cell + 1; break; }
        }
        return new SpritesheetConfig { FrameWidth = fw, FrameHeight = fh, FrameCount = count };
    }

    // The frame size along one direction from how alike the picture is to itself moved one frame:
    // the size whose shift differs least compared with a shift of half of it. Moving two frames
    // scores badly, since moving one is just as alike, so the real size wins over its multiples.
    private static int FromRepeats(byte[] px, int w, int h, bool horizontal, int width)
    {
        int len = horizontal ? w : h;
        int best = len;
        double bestRatio = 0.85;   // must be more alike than the half shift
        for (int size = 8; size <= len / 2; size++)
        {
            if (len % size != 0) continue;
            double whole = Difference(px, w, h, horizontal, size, width);
            double half = Difference(px, w, h, horizontal, size / 2, width);
            if (half <= 0) continue;
            double ratio = whole / half;
            if (ratio < bestRatio) { bestRatio = ratio; best = size; }
        }
        return best;
    }

    // Mean difference (colour and alpha) between the picture and itself moved by shift pixels,
    // sampled on a grid so that big sheets stay quick
    private static double Difference(byte[] px, int w, int h, bool horizontal, int shift, int width)
    {
        int dx = horizontal ? shift : 0, dy = horizontal ? 0 : shift;
        int step = Math.Max(1, (int)Math.Sqrt((double)w * h / 250_000));
        long sum = 0, n = 0;
        for (int y = 0; y + dy < h; y += step)
            for (int x = 0; x + dx < width; x += step)
            {
                int a = (y * w + x) * 4, b = ((y + dy) * w + x + dx) * 4;
                sum += Math.Abs(px[a] - px[b]) + Math.Abs(px[a + 1] - px[b + 1]) +
                       Math.Abs(px[a + 2] - px[b + 2]) + Math.Abs(px[a + 3] - px[b + 3]);
                n++;
            }
        return n == 0 ? 0 : (double)sum / n;
    }

    // The frame size along one direction: the length divided by the number of runs with content,
    // when that divides evenly (and the runs fit, one in each part)
    private static int FromGaps(bool[] has)
    {
        int len = has.Length, runs = 0;
        for (int i = 0; i < len; i++)
            if (has[i] && (i == 0 || !has[i - 1])) runs++;
        if (runs < 2) return len;

        // Figures with a gap inside them give extra runs, so also try fewer parts
        for (int parts = runs; parts >= 2; parts--)
        {
            if (len % parts != 0) continue;
            int size = len / parts;
            // Every run must stay inside one part: no content may cross a boundary
            bool fits = true;
            for (int b = size; b < len && fits; b += size)
                if (has[b - 1] && has[b]) fits = false;
            if (fits) return size;
        }
        return len;
    }
}
