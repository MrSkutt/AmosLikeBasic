using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Media.Imaging;

namespace AmosLikeBasic;

public sealed class AmosGraphics
{
    private WriteableBitmap? _bmp;
    private WriteableBitmap? _backbuffer;
    private int _scrollX = 0;
    private int _scrollY = 0;

    private sealed class Sprite
    {
        public Sprite(int width, int height, WriteableBitmap firstFrame)
        {
            Width = width;
            Height = height;
            Frames.Add(firstFrame);
            Ink = Colors.White;
            TransparentKey = Colors.Magenta;
            Visible = false;
        }

        public int Width { get; }
        public int Height { get; }
        public List<WriteableBitmap> Frames { get; } = new();
        public int CurrentFrame { get; set; } = 0;
        public WriteableBitmap Bitmap => Frames[CurrentFrame];

        public int X { get; set; }
        public int Y { get; set; }
        public int HandleX { get; set; }
        public int HandleY { get; set; }
        public bool Visible { get; set; }
        public Color Ink { get; set; }
        public Color TransparentKey { get; set; }
    }

    private readonly Dictionary<int, Sprite> _sprites = new();

    public int Width { get; private set; }
    public int Height { get; private set; }
    public Color Ink { get; set; } = Colors.White;
    public WriteableBitmap? Bitmap => _bmp;

    // ---------------- Project DTOs (save/load) ----------------

    public sealed record ProjectFile(int Version, string ProgramText, int ScreenWidth, int ScreenHeight, List<SpriteFile> Sprites);
    public sealed record SpriteFile(int Id, int Width, int Height, int X, int Y, int HandleX, int HandleY, int CurrentFrame, bool Visible, string TransparentKey, List<string> FramesBase64);

    public ProjectFile ExportProject(string programText)
    {
        EnsureScreen();
        var sprites = new List<SpriteFile>();
        foreach (var (id, s) in _sprites.OrderBy(k => k.Key))
        {
            var framesData = new List<string>();
            foreach (var frameBmp in s.Frames)
            {
                using var fb = frameBmp.Lock();
                var size = fb.RowBytes * s.Height;
                var bytes = new byte[size];
                System.Runtime.InteropServices.Marshal.Copy(fb.Address, bytes, 0, size);
                framesData.Add(Convert.ToBase64String(bytes));
            }
            sprites.Add(new SpriteFile(id, s.Width, s.Height, s.X, s.Y, s.HandleX, s.HandleY, s.CurrentFrame, s.Visible, s.TransparentKey.ToString(), framesData));
        }
        return new ProjectFile(1, programText ?? "", Width, Height, sprites);
    }

    public void ImportProject(ProjectFile project)
    {
        if (project is null) throw new ArgumentNullException(nameof(project));
        Screen(project.ScreenWidth <= 0 ? 640 : project.ScreenWidth, project.ScreenHeight <= 0 ? 480 : project.ScreenHeight);
        _sprites.Clear();
        foreach (var sf in project.Sprites ?? new())
        {
            var firstFrame = CreateEmptyBitmap(sf.Width, sf.Height);
            var s = new Sprite(sf.Width, sf.Height, firstFrame);
            s.X = sf.X; s.Y = sf.Y; s.HandleX = sf.HandleX; s.HandleY = sf.HandleY;
            s.CurrentFrame = sf.CurrentFrame; s.Visible = sf.Visible;
            s.TransparentKey = Color.Parse(sf.TransparentKey);
            s.Frames.Clear();
            foreach (var base64 in sf.FramesBase64)
            {
                var frameBmp = CreateEmptyBitmap(sf.Width, sf.Height);
                var bytes = Convert.FromBase64String(base64);
                using (var fb = frameBmp.Lock()) System.Runtime.InteropServices.Marshal.Copy(bytes, 0, fb.Address, bytes.Length);
                s.Frames.Add(frameBmp);
            }
            _sprites[sf.Id] = s;
        }
    }

    // ---------------- Screen & Core Methods ----------------

    public void Screen(int width, int height)
    {
        Width = width; Height = height;
        _bmp = CreateEmptyBitmap(width, height);
        _backbuffer = CreateEmptyBitmap(width, height);
    }

    private void EnsureScreen() { if (_bmp is null) Screen(640, 480); }

    private WriteableBitmap CreateEmptyBitmap(int w, int h) => 
        new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);

    public void Clear(Color color)
    {
        EnsureScreen();
        ClearBitmap(_bmp!, color);
        ClearBitmap(_backbuffer!, color);
    }

    private void ClearBitmap(WriteableBitmap bmp, Color color)
    {
        using var fb = bmp.Lock();
        unsafe {
            var ptr = (byte*)fb.Address;
            for (var y = 0; y < bmp.PixelSize.Height; y++) {
                var row = ptr + y * fb.RowBytes;
                for (var x = 0; x < bmp.PixelSize.Width; x++) {
                    var i = x * 4; row[i+0]=color.B; row[i+1]=color.G; row[i+2]=color.R; row[i+3]=color.A;
                }
            }
        }
    }

    public void Refresh()
    {
        EnsureScreen();
        using (var dst = _bmp!.Lock())
        using (var src = _backbuffer!.Lock())
        {
            unsafe {
                var dp = (byte*)dst.Address; var sp = (byte*)src.Address; var rb = src.RowBytes;
                for (var y = 0; y < Height; y++) {
                    var sy = (y + _scrollY) % Height; if (sy < 0) sy += Height;
                    var dr = dp + y * rb; var sr = sp + sy * rb;
                    for (var x = 0; x < Width; x++) {
                        var sx = (x + _scrollX) % Width; if (sx < 0) sx += Width;
                        var di = x * 4; var si = sx * 4;
                        dr[di+0]=sr[si+0]; dr[di+1]=sr[si+1]; dr[di+2]=sr[si+2]; dr[di+3]=sr[si+3];
                    }
                }
            }
        }
        foreach (var kv in _sprites) { if (kv.Value.Visible) SpriteShow(kv.Key, kv.Value.X, kv.Value.Y); }
    }

    public void Scroll(int x, int y) { _scrollX = x; _scrollY = y; }

    // ---------------- Primitive Drawing ----------------

    public void Plot(int x, int y) => Plot(x, y, Ink);
    public void Plot(int x, int y, Color color)
    {
        EnsureScreen(); if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return;
        using var fb = _backbuffer!.Lock();
        unsafe { var r = (byte*)fb.Address + y * fb.RowBytes; var i = x * 4; r[i+0]=color.B; r[i+1]=color.G; r[i+2]=color.R; r[i+3]=color.A; }
    }

    public void Line(int x0, int y0, int x1, int y1) => Line(x0, y0, x1, y1, Ink);
    public void Line(int x0, int y0, int x1, int y1, Color color)
    {
        EnsureScreen();
        int dx = Math.Abs(x1-x0), sx = x0<x1 ? 1 : -1, dy = -Math.Abs(y1-y0), sy = y0<y1 ? 1 : -1, err = dx+dy;
        while(true) { Plot(x0, y0, color); if (x0==x1 && y0==y1) break; int e2 = 2*err; if (e2>=dy) { err+=dy; x0+=sx; } if (e2<=dx) { err+=dx; y0+=sy; } }
    }

    public void Box(int x1, int y1, int x2, int y2) { Normalize(ref x1, ref y1, ref x2, ref y2); Line(x1,y1,x2,y1); Line(x2,y1,x2,y2); Line(x2,y2,x1,y2); Line(x1,y2,x1,y1); }
    public void Bar(int x1, int y1, int x2, int y2)
    {
        EnsureScreen(); Normalize(ref x1, ref y1, ref x2, ref y2);
        x1 = Math.Clamp(x1,0,Width-1); x2 = Math.Clamp(x2,0,Width-1); y1 = Math.Clamp(y1,0,Height-1); y2 = Math.Clamp(y2,0,Height-1);
        using var fb = _backbuffer!.Lock();
        unsafe {
            var p = (byte*)fb.Address;
            for (var y = y1; y <= y2; y++) {
                var r = p + y * fb.RowBytes;
                for (var x = x1; x <= x2; x++) { var i = x * 4; r[i+0]=Ink.B; r[i+1]=Ink.G; r[i+2]=Ink.R; r[i+3]=Ink.A; }
            }
        }
    }

    public void DrawText(int x, int y, string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        Avalonia.Threading.Dispatcher.UIThread.Post(() => {
            EnsureScreen();
            var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Arial"), 20, new SolidColorBrush(Ink));
            var ps = new PixelSize((int)Math.Max(1, Math.Ceiling(ft.Width)), (int)Math.Max(1, Math.Ceiling(ft.Height)));
            using var rtb = new RenderTargetBitmap(ps);
            using (var ctx = rtb.CreateDrawingContext()) { ctx.DrawRectangle(Brushes.Transparent, null, new Rect(0,0,ps.Width,ps.Height)); ctx.DrawText(ft, new Point(0,0)); }
            var buf = new byte[ps.Width * ps.Height * 4];
            unsafe { fixed(byte* p = buf) rtb.CopyPixels(new PixelRect(ps), (nint)p, buf.Length, ps.Width*4); }
            using (var dst = _backbuffer!.Lock()) unsafe {
                var dp = (byte*)dst.Address;
                for (int r = 0; r < ps.Height; r++) {
                    int ty = y + r; if (ty < 0 || ty >= Height) continue;
                    var dr = dp + ty * dst.RowBytes;
                    for (int c = 0; c < ps.Width; c++) {
                        int tx = x + c; if (tx < 0 || tx >= Width) continue;
                        int si = (r * ps.Width + c) * 4; int di = tx * 4;
                        if (buf[si+3] > 0) { dr[di+0]=buf[si+0]; dr[di+1]=buf[si+1]; dr[di+2]=buf[si+2]; dr[di+3]=buf[si+3]; }
                    }
                }
            }
            Refresh();
        });
    }

    public void LoadBackground(string fileName)
    {
        try {
            using var b = new Bitmap(fileName); EnsureScreen();
            int cw = Math.Min(Width, (int)b.Size.Width), ch = Math.Min(Height, (int)b.Size.Height);
            using (var fb = _backbuffer!.Lock()) {
                b.CopyPixels(new PixelRect(0,0,cw,ch), fb.Address, fb.RowBytes * Height, fb.RowBytes);
                unsafe { var p = (byte*)fb.Address; for (int i = 0; i < Width*Height; i++) { byte temp = p[i*4+0]; p[i*4+0]=p[i*4+2]; p[i*4+2]=temp; p[i*4+3]=255; } }
            }
            Refresh();
        } catch {}
    }

    // ---------------- Sprite Methods ----------------

    public void CreateSprite(int id, int w, int h) { var f = CreateEmptyBitmap(w, h); _sprites[id] = new Sprite(w, h, f); SpriteClear(id, Colors.Magenta); }
    public bool HasSprite(int id) => _sprites.ContainsKey(id);
    public (int w, int h) GetSpriteSize(int id) { var s = GetSprite(id); return (s.Width, s.Height); }
    public WriteableBitmap GetSpriteBitmap(int id) => GetSprite(id).Bitmap;
    public List<int> GetSpriteIds() => _sprites.Keys.OrderBy(id => id).ToList();

    public void LoadSprite(int id, string fileName)
    {
        try {
            using var b = new Bitmap(fileName); int w = (int)b.Size.Width, h = (int)b.Size.Height;
            CreateSprite(id, w, h); var s = GetSprite(id);
            using (var fb = s.Bitmap.Lock()) {
                b.CopyPixels(new PixelRect(0,0,w,h), fb.Address, fb.RowBytes * h, fb.RowBytes);
                unsafe { var p = (byte*)fb.Address; for (int i = 0; i < w*h; i++) { byte temp = p[i*4+0]; p[i*4+0]=p[i*4+2]; p[i*4+2]=temp; } s.TransparentKey = Color.FromArgb(p[3], p[2], p[1], p[0]); }
            }
        } catch {}
    }

    public void AddFrame(int id, string file)
    {
        var s = GetSprite(id); 
        using var b = new Bitmap(file); 
        var f = CreateEmptyBitmap(s.Width, s.Height);
        
        // Istället för CreateDrawingContext, använd CopyPixels för att flytta bilden till framen
        using (var fb = f.Lock())
        {
            b.CopyPixels(new PixelRect(0, 0, (int)b.Size.Width, (int)b.Size.Height), fb.Address, fb.RowBytes * s.Height, fb.RowBytes);
            
            // Fixa färgerna (RGB -> BGR swap) för att det ska se rätt ut på din Mac
            unsafe {
                byte* p = (byte*)fb.Address;
                for (int i = 0; i < s.Width * s.Height; i++) {
                    byte temp = p[i*4+0]; 
                    p[i*4+0] = p[i*4+2]; 
                    p[i*4+2] = temp;
                }
            }
        }
        s.Frames.Add(f);
    }

    public void SetSpriteFrame(int id, int idx) { var s = GetSprite(id); if (idx >= 0 && idx < s.Frames.Count) s.CurrentFrame = idx; }
    public void SpriteHandle(int id, int hx, int hy) { var s = GetSprite(id); s.HandleX = hx; s.HandleY = hy; }
    public void SpritePos(int id, int x, int y) { var s = GetSprite(id); s.X = x; s.Y = y; }
    public void SpriteOn(int id) => GetSprite(id).Visible = true;
    public void SpriteOff(int id) => GetSprite(id).Visible = false;

    public void SpriteSetPixel(int id, int x, int y, Color c)
    {
        var s = GetSprite(id); if ((uint)x >= (uint)s.Width || (uint)y >= (uint)s.Height) return;
        using var fb = s.Bitmap.Lock();
        unsafe { var r = (byte*)fb.Address + y * fb.RowBytes; var i = x * 4; r[i+0]=c.B; r[i+1]=c.G; r[i+2]=c.R; r[i+3]=c.A; }
    }

    public void SpriteClear(int id, Color c)
    {
        var s = GetSprite(id); using var fb = s.Bitmap.Lock();
        unsafe {
            var p = (byte*)fb.Address;
            for (var y = 0; y < s.Height; y++) {
                var r = p + y * fb.RowBytes;
                for (var x = 0; x < s.Width; x++) { var i = x * 4; r[i+0]=c.B; r[i+1]=c.G; r[i+2]=c.R; r[i+3]=c.A; }
            }
        }
    }

    public void SpriteShow(int id, int dstX, int dstY)
    {
        EnsureScreen(); var s = GetSprite(id); int rx = dstX - s.HandleX, ry = dstY - s.HandleY;
        using var dst = _bmp!.Lock(); using var src = s.Bitmap.Lock();
        unsafe {
            var dp = (byte*)dst.Address; var sp = (byte*)src.Address; var k = s.TransparentKey;
            for (var y = 0; y < s.Height; y++) {
                int ty = ry + y; if ((uint)ty >= (uint)Height) continue;
                var dr = dp + ty * dst.RowBytes; var sr = sp + y * src.RowBytes;
                for (var x = 0; x < s.Width; x++) {
                    int tx = rx + x; if ((uint)tx >= (uint)Width) continue;
                    int si = x * 4; byte b = sr[si+0], g = sr[si+1], r = sr[si+2], a = sr[si+3];
                    if (r == k.R && g == k.G && b == k.B) continue;
                    int di = tx * 4; dr[di+0]=b; dr[di+1]=g; dr[di+2]=r; dr[di+3]=a;
                }
            }
        }
    }

    public void SpriteInk(int id, Color c) => GetSprite(id).Ink = c;
    public void SpritePlot(int id, int x, int y) { var s = GetSprite(id); SpriteSetPixel(id, x, y, s.Ink); }
    public void SpriteBar(int id, int x1, int y1, int x2, int y2)
    {
        var s = GetSprite(id); Normalize(ref x1, ref y1, ref x2, ref y2);
        x1 = Math.Clamp(x1,0,s.Width-1); x2 = Math.Clamp(x2,0,s.Width-1); y1 = Math.Clamp(y1,0,s.Height-1); y2 = Math.Clamp(y2,0,s.Height-1);
        using var fb = s.Bitmap.Lock();
        unsafe {
            var p = (byte*)fb.Address;
            for (var y = y1; y <= y2; y++) {
                var r = p + y * fb.RowBytes;
                for (var x = x1; x <= x2; x++) { var i = x * 4; r[i+0]=s.Ink.B; r[i+1]=s.Ink.G; r[i+2]=s.Ink.R; r[i+3]=s.Ink.A; }
            }
        }
    }

    private Sprite GetSprite(int id) => _sprites.TryGetValue(id, out var s) ? s : throw new InvalidOperationException($"Sprite {id} not defined.");
    private void Normalize(ref int x1, ref int y1, ref int x2, ref int y2) { if (x2 < x1) (x1, x2) = (x2, x1); if (y2 < y1) (y1, y2) = (y2, y1); }
}