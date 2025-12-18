using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Media.Imaging;

namespace AmosLikeBasic;

public sealed class AmosGraphics
{
    private WriteableBitmap? _bmp;
    private WriteableBitmap? _backbuffer;

    private sealed class Sprite
    {
        public Sprite(int width, int height)
        {
            Width = width;
            Height = height;

            Bitmap = new WriteableBitmap(
                new PixelSize(width, height),
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Premul);

            Ink = Colors.White;
            TransparentKey = Colors.Magenta;

            this.X = 0;
            this.Y = 0;
            this.Visible = false;
        }

        public int Width { get; }
        public int Height { get; }
        public WriteableBitmap Bitmap { get; }
        public Color Ink { get; set; }
        public Color TransparentKey { get; set; }

        public int X { get; set; }
        public int Y { get; set; }
        public bool Visible { get; set; }
    }

    private readonly Dictionary<int, Sprite> _sprites = new();

    public int Width { get; private set; }
    public int Height { get; private set; }

    public Color Ink { get; set; } = Colors.White;

    public WriteableBitmap? Bitmap => _bmp;

    // ---------------- Project DTOs (save/load) ----------------

    public sealed record ProjectFile(
        int Version,
        string ProgramText,
        int ScreenWidth,
        int ScreenHeight,
        List<SpriteFile> Sprites);

    public sealed record SpriteFile(
        int Id,
        int Width, int Height,
        int X, int Y,
        bool Visible,
        string TransparentKey,
        int Stride,
        string PixelDataBase64
    );

    public ProjectFile ExportProject(string programText)
    {
        EnsureScreen();
        var sprites = new List<SpriteFile>();
        foreach (var (id, s) in _sprites.OrderBy(k => k.Key))
        {
            using var fb = s.Bitmap.Lock();
            var stride = fb.RowBytes;
            var bytes = new byte[stride * s.Height];
            unsafe
            {
                var src = (byte*)fb.Address;
                for (var y = 0; y < s.Height; y++)
                {
                    System.Runtime.InteropServices.Marshal.Copy((nint)(src + y * stride), bytes, y * stride, stride);
                }
            }

            sprites.Add(new SpriteFile(
                id, s.Width, s.Height, s.X, s.Y, s.Visible, s.TransparentKey.ToString(), stride, Convert.ToBase64String(bytes)
            ));
        }

        return new ProjectFile(1, programText ?? "", Width, Height, sprites);
    }

    public void ImportProject(ProjectFile project)
    {
        if (project is null) throw new ArgumentNullException(nameof(project));
        Screen(project.ScreenWidth <= 0 ? 640 : project.ScreenWidth, project.ScreenHeight <= 0 ? 480 : project.ScreenHeight);
        Clear(Colors.Black);
        _sprites.Clear();

        foreach (var sf in project.Sprites ?? new())
        {
            CreateSprite(sf.Id, sf.Width, sf.Height);
            var s = GetSprite(sf.Id);
            s.X = sf.X; s.Y = sf.Y; s.Visible = sf.Visible;
            s.TransparentKey = Color.Parse(sf.TransparentKey);

            var bytes = Convert.FromBase64String(sf.PixelDataBase64);
            using var fb = s.Bitmap.Lock();
            unsafe
            {
                var dst = (byte*)fb.Address;
                for (var y = 0; y < s.Height; y++)
                {
                    System.Runtime.InteropServices.Marshal.Copy(bytes, y * fb.RowBytes, (nint)(dst + y * fb.RowBytes), fb.RowBytes);
                }
            }
        }
    }

    // ---------------- Graphics Methods ----------------

    public void Screen(int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        Width = width;
        Height = height;

        _bmp = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        _backbuffer = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
    }

    public void Clear(Color color)
    {
        EnsureScreen();
        ClearBitmap(_bmp!, color);
        ClearBitmap(_backbuffer!, color);
    }

    private void ClearBitmap(WriteableBitmap bmp, Color color)
    {
        using var fb = bmp.Lock();
        unsafe
        {
            var ptr = (byte*)fb.Address;
            for (var y = 0; y < bmp.PixelSize.Height; y++)
            {
                var row = ptr + y * fb.RowBytes;
                for (var x = 0; x < bmp.PixelSize.Width; x++)
                {
                    var i = x * 4;
                    row[i + 0] = color.B; row[i + 1] = color.G; row[i + 2] = color.R; row[i + 3] = color.A;
                }
            }
        }
    }

    public void Plot(int x, int y) => Plot(x, y, Ink);
    public void Plot(int x, int y, Color color)
    {
        EnsureScreen();
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return;
        using var fb = _backbuffer!.Lock();
        unsafe
        {
            var row = (byte*)fb.Address + y * fb.RowBytes;
            var i = x * 4;
            row[i + 0] = color.B; row[i + 1] = color.G; row[i + 2] = color.R; row[i + 3] = color.A;
        }
    }

    public void Line(int x0, int y0, int x1, int y1) => Line(x0, y0, x1, y1, Ink);
    public void Line(int x0, int y0, int x1, int y1, Color color)
    {
        EnsureScreen();
        var dx = Math.Abs(x1 - x0); var sx = x0 < x1 ? 1 : -1;
        var dy = -Math.Abs(y1 - y0); var sy = y0 < y1 ? 1 : -1;
        var err = dx + dy;
        while (true)
        {
            Plot(x0, y0, color);
            if (x0 == x1 && y0 == y1) break;
            var e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    public void Box(int x1, int y1, int x2, int y2) => Box(x1, y1, x2, y2, Ink);
    public void Box(int x1, int y1, int x2, int y2, Color color)
    {
        NormalizeRect(ref x1, ref y1, ref x2, ref y2);
        Line(x1, y1, x2, y1, color); Line(x2, y1, x2, y2, color);
        Line(x2, y2, x1, y2, color); Line(x1, y2, x1, y1, color);
    }

    public void Bar(int x1, int y1, int x2, int y2) => Bar(x1, y1, x2, y2, Ink);
    public void Bar(int x1, int y1, int x2, int y2, Color color)
    {
        EnsureScreen();
        NormalizeRect(ref x1, ref y1, ref x2, ref y2);
        x1 = Math.Clamp(x1, 0, Width - 1); x2 = Math.Clamp(x2, 0, Width - 1);
        y1 = Math.Clamp(y1, 0, Height - 1); y2 = Math.Clamp(y2, 0, Height - 1);
        using var fb = _backbuffer!.Lock();
        unsafe
        {
            var ptr = (byte*)fb.Address;
            for (var y = y1; y <= y2; y++)
            {
                var row = ptr + y * fb.RowBytes;
                for (var x = x1; x <= x2; x++)
                {
                    var i = x * 4;
                    row[i + 0] = color.B; row[i + 1] = color.G; row[i + 2] = color.R; row[i + 3] = color.A;
                }
            }
        }
    }

    // ---------------- Sprites ----------------

    public void CreateSprite(int id, int width, int height)
    {
        _sprites[id] = new Sprite(width, height);
        SpriteClear(id, _sprites[id].TransparentKey);
    }

    public bool HasSprite(int id) => _sprites.ContainsKey(id);
    public (int w, int h) GetSpriteSize(int id) { var s = GetSprite(id); return (s.Width, s.Height); }
    public WriteableBitmap GetSpriteBitmap(int id) => GetSprite(id).Bitmap;
    public void SpriteSetPixel(int id, int x, int y, Color color)
    {
        var s = GetSprite(id); if ((uint)x >= (uint)s.Width || (uint)y >= (uint)s.Height) return;
        using var fb = s.Bitmap.Lock();
        unsafe { var r = (byte*)fb.Address + y * fb.RowBytes; var i = x * 4; r[i+0]=color.B; r[i+1]=color.G; r[i+2]=color.R; r[i+3]=color.A; }
    }

    public void SpriteClear(int id, Color color)
    {
        var s = GetSprite(id); using var fb = s.Bitmap.Lock();
        unsafe {
            var p = (byte*)fb.Address;
            for (var y = 0; y < s.Height; y++) {
                var r = p + y * fb.RowBytes;
                for (var x = 0; x < s.Width; x++) { var i = x * 4; r[i+0]=color.B; r[i+1]=color.G; r[i+2]=color.R; r[i+3]=color.A; }
            }
        }
    }

    public void SpriteShow(int id, int dstX, int dstY)
    {
        EnsureScreen(); var s = GetSprite(id);
        using var dst = _bmp!.Lock(); using var src = s.Bitmap.Lock();
        unsafe {
            var dp = (byte*)dst.Address; var sp = (byte*)src.Address; var k = s.TransparentKey;
            for (var y = 0; y < s.Height; y++) {
                var ty = dstY + y; if ((uint)ty >= (uint)Height) continue;
                var dr = dp + ty * dst.RowBytes; var sr = sp + y * src.RowBytes;
                for (var x = 0; x < s.Width; x++) {
                    var tx = dstX + x; if ((uint)tx >= (uint)Width) continue;
                    var si = x * 4; var b = sr[si+0]; var g = sr[si+1]; var r = sr[si+2]; var a = sr[si+3];
                    if (r == k.R && g == k.G && b == k.B) continue;
                    var di = tx * 4; dr[di+0]=b; dr[di+1]=g; dr[di+2]=r; dr[di+3]=a;
                }
            }
        }
    }

    public void SpritePos(int id, int x, int y) { var s = GetSprite(id); s.X = x; s.Y = y; }
    public void SpriteOn(int id) { GetSprite(id).Visible = true; }
    public void SpriteOff(int id) { GetSprite(id).Visible = false; }

    public void Refresh()
    {
        EnsureScreen();
        using (var dst = _bmp!.Lock())
        using (var src = _backbuffer!.Lock())
        {
            unsafe
            {
                var size = src.RowBytes * Height;
                Buffer.MemoryCopy((void*)src.Address, (void*)dst.Address, size, size);
            }
        }
        foreach (var kv in _sprites) { if (kv.Value.Visible) SpriteShow(kv.Key, kv.Value.X, kv.Value.Y); }
    }

    private Sprite GetSprite(int id) => _sprites.TryGetValue(id, out var s) ? s : throw new InvalidOperationException($"Sprite {id} not defined.");
    private void EnsureScreen() { if (_bmp is null) Screen(640, 480); }
    private static void NormalizeRect(ref int x1, ref int y1, ref int x2, ref int y2) { if (x2 < x1) (x1, x2) = (x2, x1); if (y2 < y1) (y1, y2) = (y2, y1); }
    
    public void SpriteInk(int id, Color color)
    {
        var s = GetSprite(id);
        s.Ink = color;
    }

    public void SpritePlot(int id, int x, int y)
    {
        var s = GetSprite(id);
        SpriteSetPixel(id, x, y, s.Ink);
    }

    public void SpriteBar(int id, int x1, int y1, int x2, int y2)
    {
        var s = GetSprite(id);
        NormalizeRect(ref x1, ref y1, ref x2, ref y2);

        x1 = Math.Clamp(x1, 0, s.Width - 1);
        x2 = Math.Clamp(x2, 0, s.Width - 1);
        y1 = Math.Clamp(y1, 0, s.Height - 1);
        y2 = Math.Clamp(y2, 0, s.Height - 1);

        using var fb = s.Bitmap.Lock();
        unsafe
        {
            var ptr = (byte*)fb.Address;
            for (var y = y1; y <= y2; y++)
            {
                var row = ptr + y * fb.RowBytes;
                for (var x = x1; x <= x2; x++)
                {
                    var i = x * 4;
                    row[i + 0] = s.Ink.B;
                    row[i + 1] = s.Ink.G;
                    row[i + 2] = s.Ink.R;
                    row[i + 3] = s.Ink.A;
                }
            }
        }
    }

}