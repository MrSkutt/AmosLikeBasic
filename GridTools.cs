using System;
using System.Collections.Generic;
using System.Linq;

namespace AmosLikeBasic;

public enum DrawTool { Pencil, Line, Rectangle, Ellipse, Fill, Picker }

// The drawing tools of the sprite and map editors, on a grid of numbers: colours (BGRA packed into
// a uint, as in the bitmaps) in the sprite editor, tile numbers in the map editor
public static class GridTools
{
    // Calls plot for every cell of a line, without gaps (Bresenham)
    public static void Line(int x0, int y0, int x1, int y1, Action<int, int> plot)
    {
        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;
        while (true)
        {
            plot(x0, y0);
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    public static void Rectangle(int x0, int y0, int x1, int y1, bool filled, Action<int, int> plot)
    {
        (x0, x1) = (Math.Min(x0, x1), Math.Max(x0, x1));
        (y0, y1) = (Math.Min(y0, y1), Math.Max(y0, y1));
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                if (filled || x == x0 || x == x1 || y == y0 || y == y1) plot(x, y);
    }

    // The ellipse that fits the box from (x0,y0) to (x1,y1); the outline is the cells inside that
    // have a neighbour outside, so it is one cell thick without gaps
    public static void Ellipse(int x0, int y0, int x1, int y1, bool filled, Action<int, int> plot)
    {
        (x0, x1) = (Math.Min(x0, x1), Math.Max(x0, x1));
        (y0, y1) = (Math.Min(y0, y1), Math.Max(y0, y1));
        double cx = (x0 + x1) / 2.0, cy = (y0 + y1) / 2.0;
        double rx = (x1 - x0) / 2.0 + 0.5, ry = (y1 - y0) / 2.0 + 0.5;
        bool Inside(int x, int y)
        {
            double nx = (x - cx) / rx, ny = (y - cy) / ry;
            return nx * nx + ny * ny <= 1.0;
        }
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                if (Inside(x, y) && (filled || !Inside(x - 1, y) || !Inside(x + 1, y) || !Inside(x, y - 1) || !Inside(x, y + 1)))
                    plot(x, y);
    }

    // Fills the area of equal cells around (x,y) with value
    public static void Fill<T>(T[] cells, int w, int h, int x, int y, T value) where T : IEquatable<T>
    {
        if (x < 0 || y < 0 || x >= w || y >= h) return;
        var target = cells[y * w + x];
        if (target.Equals(value)) return;
        var stack = new Stack<(int x, int y)>();
        stack.Push((x, y));
        while (stack.Count > 0)
        {
            var (px, py) = stack.Pop();
            if (px < 0 || py < 0 || px >= w || py >= h || !cells[py * w + px].Equals(target)) continue;
            cells[py * w + px] = value;
            stack.Push((px + 1, py)); stack.Push((px - 1, py));
            stack.Push((px, py + 1)); stack.Push((px, py - 1));
        }
    }

    public static void FlipHorizontal<T>(T[] cells, int w, int h)
    {
        for (int y = 0; y < h; y++) Array.Reverse(cells, y * w, w);
    }

    public static void FlipVertical<T>(T[] cells, int w, int h)
    {
        var row = new T[w];
        for (int y = 0; y < h / 2; y++)
        {
            Array.Copy(cells, y * w, row, 0, w);
            Array.Copy(cells, (h - 1 - y) * w, cells, y * w, w);
            Array.Copy(row, 0, cells, (h - 1 - y) * w, w);
        }
    }

    // Moves everything one step (with wrap-around), for nudging a figure into place
    public static void Shift<T>(T[] cells, int w, int h, int dx, int dy)
    {
        var copy = (T[])cells.Clone();
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                cells[((y + dy) % h + h) % h * w + ((x + dx) % w + w) % w] = copy[y * w + x];
    }

    // The colours used most, most common first, leaving out the transparent ones
    public static List<uint> CommonColours(IEnumerable<uint[]> frames, int max, Func<uint, bool> transparent) =>
        frames.SelectMany(f => f)
              .Where(c => !transparent(c))
              .GroupBy(c => c)
              .OrderByDescending(g => g.Count())
              .Take(max)
              .Select(g => g.Key)
              .ToList();
}
