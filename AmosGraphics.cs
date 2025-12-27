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
    private WriteableBitmap? _finalBuffer;
    private readonly List<WriteableBitmap> _screens = new();
    private readonly List<Point> _screenOffsets = new();
    private int _currentScreen = 0;

    public List<WriteableBitmap> GetTileBitmaps() => _tiles;
    
    private sealed class Font
    {
        public List<WriteableBitmap> CharBitmaps { get; } = new();
        public int CharWidth { get; set; }
        public int CharHeight { get; set; }
        public double Angle { get; set; } = 0;
        public double ZoomX { get; set; } = 1.0;
        public double ZoomY { get; set; } = 1.0;
        public string CharMap { get; set; } = ""; 
    }
    
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
        public double Angle { get; set; } = 0;
        public double ZoomX { get; set; } = 1.0;
        public double ZoomY { get; set; } = 1.0;
        public Color Ink { get; set; }
        public Color TransparentKey { get; set; }
    }

    private readonly Dictionary<int, Sprite> _sprites = new();
    private readonly Dictionary<int, Font> _fonts = new();
    
    private sealed class QueuedFontText
    {
        public int FontId { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public string Text { get; set; } = "";
    }
    private readonly List<QueuedFontText> _fontTexts = new(); 
    
    private readonly List<WriteableBitmap> _tiles = new();
    private int _tilesInWidth = 0;
    private int[,] _map = new int[0, 0];
    private int _tileWidth = 32;
    private int _tileHeight = 32;

    public int Width { get; private set; }
    public int Height { get; private set; }
    public Color Ink { get; set; } = Colors.White;
    public WriteableBitmap? Bitmap => _bmp;

    private WriteableBitmap GetActiveScreen()
    {
        if (_screens.Count <= _currentScreen) SetDrawingScreen(_currentScreen);
        return _screens[_currentScreen];
    }

    // ---------------- Project Export/Import ----------------

    public sealed record ProjectFile(
        int Version,
        string ProgramText,
        int ScreenWidth,
        int ScreenHeight,
        List<SpriteFile> Sprites,
        int MapWidth, // Lägg till dessa
        int MapHeight,
        List<int> MapData);

    public sealed record SpriteFile(
        int Id,
        int Width,
        int Height,
        int X,
        int Y,
        int HandleX,
        int HandleY,
        int CurrentFrame,
        bool Visible,
        string TransparentKey,
        List<string> FramesBase64);

    public ProjectFile ExportProject(string programText)
    {
        EnsureScreen();
        // 1. Exportera Sprites
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

            sprites.Add(new SpriteFile(id, s.Width, s.Height, s.X, s.Y, s.HandleX, s.HandleY, s.CurrentFrame, s.Visible,
                s.TransparentKey.ToString(), framesData));
        }

        // 2. Exportera Banan (NY LOGIK)
        var mapList = new List<int>();
        int mw = GetMapWidth();
        int mh = GetMapHeight();

        for (int y = 0; y < mh; y++)
        for (int x = 0; x < mw; x++)
            mapList.Add(GetMapTile(x, y));

        // 3. Skapa ProjectFile med ALLA 8 argument (inklusive de nya för Map)
        return new ProjectFile(
            Version: 1,
            ProgramText: programText ?? "",
            ScreenWidth: Width,
            ScreenHeight: Height,
            Sprites: sprites,
            MapWidth: mw,
            MapHeight: mh,
            MapData: mapList);
    }

    public void ImportProject(ProjectFile project)
    {
        if (project is null) return;
        int screenW = project.ScreenWidth <= 0 ? 640 : project.ScreenWidth;
        int screenH = project.ScreenHeight <= 0 ? 480 : project.ScreenHeight;

        Screen(project.ScreenWidth <= 0 ? 640 : project.ScreenWidth,
            project.ScreenHeight <= 0 ? 480 : project.ScreenHeight);
        _sprites.Clear();
        foreach (var sf in project.Sprites ?? new())
        {
            var firstFrame = CreateEmptyBitmap(sf.Width, sf.Height);
            var s = new Sprite(sf.Width, sf.Height, firstFrame);
            s.X = sf.X;
            s.Y = sf.Y;
            s.HandleX = sf.HandleX;
            s.HandleY = sf.HandleY;
            s.CurrentFrame = sf.CurrentFrame;
            s.Visible = sf.Visible;
            s.TransparentKey = Color.Parse(sf.TransparentKey);
            s.Frames.Clear();
            foreach (var b64 in sf.FramesBase64)
            {
                var f = CreateEmptyBitmap(sf.Width, sf.Height);
                var b = Convert.FromBase64String(b64);
                using (var fb = f.Lock()) System.Runtime.InteropServices.Marshal.Copy(b, 0, fb.Address, b.Length);
                s.Frames.Add(f);
            }

            _sprites[sf.Id] = s;
        }

        SetMapSize(project.MapWidth, project.MapHeight);
        int idx = 0;
        for (int y = 0; y < project.MapHeight; y++)
        for (int x = 0; x < project.MapWidth; x++)
            if (idx < project.MapData.Count)
                _map[x, y] = project.MapData[idx++];

    }

    // ---------------- Screen & Core ----------------

    public void Screen(int w, int h)
    {
        Width = w;
        Height = h;
        _bmp = CreateEmptyBitmap(w, h);
        _finalBuffer = CreateEmptyBitmap(w, h); // Skapa den dolda buffern
        _screens.Clear();
        _screenOffsets.Clear();
        _screens.Add(CreateEmptyBitmap(w, h));
        _screenOffsets.Add(new Point(0, 0));
        _currentScreen = 0;
    }

    public void SetDrawingScreen(int id)
    {
        while (_screens.Count <= id)
        {
            _screens.Add(CreateEmptyBitmap(Width, Height));
            _screenOffsets.Add(new Point(0, 0));
        }

        _currentScreen = id;
    }

    private void EnsureScreen()
    {
        if (_bmp is null) Screen(640, 480);
    }

    private WriteableBitmap CreateEmptyBitmap(int w, int h) => new WriteableBitmap(new PixelSize(w, h),
        new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);

    public void Clear(Color color)
    {
        EnsureScreen();
        ClearBitmap(_bmp!, color);
        foreach (var s in _screens) ClearBitmap(s, color);
        for (int i = 0; i < _screenOffsets.Count; i++) _screenOffsets[i] = new Point(0, 0);
        _fontTexts.Clear(); 
        Refresh();
    }

    private void ClearBitmap(WriteableBitmap bmp, Color c)
    {
        using var fb = bmp.Lock();
        unsafe
        {
            var p = (byte*)fb.Address;
            for (var i = 0; i < fb.RowBytes * bmp.PixelSize.Height; i += 4)
            {
                p[i + 0] = c.B;
                p[i + 1] = c.G;
                p[i + 2] = c.R;
                p[i + 3] = c.A;
            }
        }
    }


    public void Refresh()
    {
        EnsureScreen();
        if (_screens.Count == 0 || _bmp == null || _finalBuffer == null) return;

        // 1. Slå ihop allt i den dolda _finalBuffer först
        using (var dst = _finalBuffer.Lock())
        {
            unsafe
            {
                var dp = (byte*)dst.Address;
                var rb = dst.RowBytes;

                // Rensa till svart
                for (var i = 0; i < rb * Height; i++) dp[i] = 0;

                // Rita alla lager
                for (int sIdx = 0; sIdx < _screens.Count; sIdx++)
                {
                    var currentScreen = _screens[sIdx];
                    using (var src = currentScreen.Lock())
                    {
                        var sp = (byte*)src.Address;
                        var sWidth = currentScreen.PixelSize.Width; // LAGRETS FAKTISKA BREDD
                        var sHeight = currentScreen.PixelSize.Height;
                        var sRowBytes = src.RowBytes;

                        var off = _screenOffsets[sIdx];
                        int sX = (int)off.X, sY = (int)off.Y;

                        for (var y = 0; y < Height; y++)
                        {
                            // Beräkna rätt rad i käll-bitmappen (med wrap-around baserat på lagrets storlek)
                            int sy = (y + sY) % sHeight;
                            if (sy < 0) sy += sHeight;
                            var dr = dp + y * rb;
                            var sr = sp + sy * sRowBytes; // Använd lagrets egna RowBytes!

                            for (var x = 0; x < Width; x++)
                            {
                                // Beräkna rätt kolumn i käll-bitmappen
                                int sx = (x + sX) % sWidth;
                                if (sx < 0) sx += sWidth;
                                int di = x * 4, si = sx * 4;

                                if (sIdx == 0 || (sr[si + 0] > 0 || sr[si + 1] > 0 || sr[si + 2] > 0))
                                {
                                    dr[di + 0] = sr[si + 0];
                                    dr[di + 1] = sr[si + 1];
                                    dr[di + 2] = sr[si + 2];
                                    dr[di + 3] = 255;
                                }
                            }
                        }
                    }
                }

                // Rita sprites (fortfarande inuti _finalBuffer Lock!)
                //using (var dc = _finalBuffer.CreateDrawingContext())
                {
                    foreach (var kv in _sprites.OrderBy(x => x.Key))
                    {
                        var s = kv.Value;
                        if (!s.Visible || s.Frames.Count == 0) continue;
                        RenderSpriteInternal(dp, rb, s);
                    }
                    
                    foreach (var ft in _fontTexts)
                    {
                        RenderFontTextInternal(dp, rb, ft);
                    }
                    
                }
            }
        }

        // 2. Kopiera NU den färdiga bilden till den bitmapp som visas (Double Buffering)
        using (var screenDst = _bmp.Lock())
        using (var finalSrc = _finalBuffer.Lock())
        {
            unsafe
            {
                System.Buffer.MemoryCopy((void*)finalSrc.Address, (void*)screenDst.Address, finalSrc.RowBytes * Height,
                    finalSrc.RowBytes * Height);
            }
        }
    }

    public void Scroll(int sid, int x, int y)
    {
        if (sid < _screenOffsets.Count) _screenOffsets[sid] = new Point(x, y);
    }

    public Point GetScreenOffset(int sid)
    {
        if (sid >= 0 && sid < _screenOffsets.Count) return _screenOffsets[sid];
        return new Point(0, 0);
    }

    // ---------------- Drawing ----------------
    public void Plot(int x, int y) => Plot(x, y, Ink);

    public void Plot(int x, int y, Color c)
    {
        EnsureScreen();
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return;
        using var fb = GetActiveScreen().Lock();
        unsafe
        {
            var r = (byte*)fb.Address + y * fb.RowBytes;
            var i = x * 4;
            r[i + 0] = c.B;
            r[i + 1] = c.G;
            r[i + 2] = c.R;
            r[i + 3] = c.A;
        }
    }

    public void Line(int x0, int y0, int x1, int y1) => Line(x0, y0, x1, y1, Ink);

    public void Line(int x0, int y0, int x1, int y1, Color c)
    {
        EnsureScreen();
        int dx = Math.Abs(x1 - x0),
            sx = x0 < x1 ? 1 : -1,
            dy = -Math.Abs(y1 - y0),
            sy = y0 < y1 ? 1 : -1,
            err = dx + dy;
        while (true)
        {
            Plot(x0, y0, c);
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy)
            {
                err += dy;
                x0 += sx;
            }

            if (e2 <= dx)
            {
                err += dx;
                y0 += sy;
            }
        }
    }

    public void Box(int x1, int y1, int x2, int y2)
    {
        Normalize(ref x1, ref y1, ref x2, ref y2);
        Line(x1, y1, x2, y1, Ink);
        Line(x2, y1, x2, y2, Ink);
        Line(x2, y2, x1, y2, Ink);
        Line(x1, y2, x1, y1, Ink);
    }

    public void Bar(int x1, int y1, int x2, int y2)
    {
        EnsureScreen();
        Normalize(ref x1, ref y1, ref x2, ref y2);
        x1 = Math.Clamp(x1, 0, Width - 1);
        x2 = Math.Clamp(x2, 0, Width - 1);
        y1 = Math.Clamp(y1, 0, Height - 1);
        y2 = Math.Clamp(y2, 0, Height - 1);
        using var fb = GetActiveScreen().Lock();
        unsafe
        {
            var p = (byte*)fb.Address;
            for (var y = y1; y <= y2; y++)
            {
                var r = p + y * fb.RowBytes;
                for (var x = x1; x <= x2; x++)
                {
                    var i = x * 4;
                    r[i + 0] = Ink.B;
                    r[i + 1] = Ink.G;
                    r[i + 2] = Ink.R;
                    r[i + 3] = Ink.A;
                }
            }
        }
    }

    public void DrawText(int x, int y, string t)
    {
        if (string.IsNullOrEmpty(t)) return;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            EnsureScreen();
            var ft = new FormattedText(t, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Arial"),
                20, new SolidColorBrush(Ink));
            var ps = new PixelSize((int)Math.Max(1, ft.Width), (int)Math.Max(1, ft.Height));
            using var rtb = new RenderTargetBitmap(ps);
            using (var ctx = rtb.CreateDrawingContext())
            {
                ctx.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ps.Width, ps.Height));
                ctx.DrawText(ft, new Point(0, 0));
            }

            var b = new byte[ps.Width * ps.Height * 4];
            unsafe
            {
                fixed (byte* p = b) rtb.CopyPixels(new PixelRect(ps), (nint)p, b.Length, ps.Width * 4);
            }

            using (var dst = GetActiveScreen().Lock())
                unsafe
                {
                    var dp = (byte*)dst.Address;
                    for (int r = 0; r < ps.Height; r++)
                    {
                        int ty = y + r;
                        if (ty < 0 || ty >= Height) continue;
                        var dr = dp + ty * dst.RowBytes;
                        for (int c = 0; c < ps.Width; c++)
                        {
                            int tx = x + c;
                            if (tx < 0 || tx >= Width) continue;
                            int si = (r * ps.Width + c) * 4, di = tx * 4;
                            if (b[si + 3] > 0)
                            {
                                dr[di + 0] = b[si + 0];
                                dr[di + 1] = b[si + 1];
                                dr[di + 2] = b[si + 2];
                                dr[di + 3] = b[si + 3];
                            }
                        }
                    }
                }

            Refresh();
        });
    }

            public void FontLoad(int id, string file, int tw, int th)
        {
            try
            {
                using var b = new Bitmap(file);
                var font = new Font { CharWidth = tw, CharHeight = th };
                int cols = (int)b.Size.Width / tw;
                int rows = (int)b.Size.Height / th;

                for (int y = 0; y < rows; y++)
                {
                    for (int x = 0; x < cols; x++)
                    {
                        var t = CreateEmptyBitmap(tw, th);
                        using (var fb = t.Lock())
                        {
                            b.CopyPixels(new PixelRect(x * tw, y * th, tw, th), fb.Address, fb.RowBytes * th, fb.RowBytes);
                            unsafe
                            {
                                var p = (byte*)fb.Address;
                                for (int i = 0; i < tw * th; i++)
                                {
                                    byte temp = p[i * 4 + 0];
                                    p[i * 4 + 0] = p[i * 4 + 2];
                                    p[i * 4 + 2] = temp;
                                }
                            }
                        }
                        font.CharBitmaps.Add(t);
                    }
                }
                _fonts[id] = font;
            }
            catch { }
        }

        public void FontRotate(int id, double angle) { if (_fonts.TryGetValue(id, out var f)) f.Angle = angle; }
        public void FontZoom(int id, double zx, double zy) { if (_fonts.TryGetValue(id, out var f)) { f.ZoomX = zx; f.ZoomY = zy; } }
        public void FontMap(int id, string map) { if (_fonts.TryGetValue(id, out var f)) f.CharMap = map; }

        public void FontPrint(int id, int x, int y, string text)
        {
            if (!_fonts.TryGetValue(id, out var f)) return;
            // Istället för att rita direkt, sparar vi texten för att ritas vid Refresh
            _fontTexts.Add(new QueuedFontText { FontId = id, X = x, Y = y, Text = text });
        }

        public void FontClear() => _fontTexts.Clear();
        
                private unsafe void RenderFontTextInternal(byte* dp, int rb, QueuedFontText qt)
        {
            if (!_fonts.TryGetValue(qt.FontId, out var f)) return;
            int curX = qt.X;
            foreach (var c in qt.Text)
            {
                RenderFontCharInternal(dp, rb, f, curX, qt.Y, c);
                curX += (int)(f.CharWidth * f.ZoomX);
            }
        }

        private unsafe void RenderFontCharInternal(byte* dp, int rb, Font f, int x, int y, char c)
        {
            string map = string.IsNullOrEmpty(f.CharMap) ? "" : f.CharMap;
            int charIdx = !string.IsNullOrEmpty(map) ? map.IndexOf(char.ToUpper(c)) : c - 32;
            if (charIdx < 0 || charIdx >= f.CharBitmaps.Count) return;

            var charBmp = f.CharBitmaps[charIdx];
            using var src = charBmp.Lock();
            byte* sp = (byte*)src.Address;
            int srb = src.RowBytes;

            double angleRad = f.Angle * Math.PI / 180.0;
            double cosA = Math.Cos(angleRad), sinA = Math.Sin(angleRad);
            double invZx = 1.0 / f.ZoomX, invZy = 1.0 / f.ZoomY;
            int hx = f.CharWidth / 2, hy = f.CharHeight / 2;

            double radius = Math.Sqrt(f.CharWidth * f.CharWidth + f.CharHeight * f.CharHeight) * Math.Max(f.ZoomX, f.ZoomY);
            int minX = Math.Max(0, (int)(x - radius)), maxX = Math.Min(Width - 1, (int)(x + radius));
            int minY = Math.Max(0, (int)(y - radius)), maxY = Math.Min(Height - 1, (int)(y + radius));

            for (int py = minY; py <= maxY; py++)
            {
                byte* rowPtr = dp + py * rb;
                double dy = py - y;
                for (int px = minX; px <= maxX; px++)
                {
                    double dx = px - x;
                    double lx = (dx * cosA + dy * sinA) * invZx + hx;
                    double ly = (dy * cosA - dx * sinA) * invZy + hy;
                    int ilx = (int)lx, ily = (int)ly;

                    if (ilx >= 0 && ilx < f.CharWidth && ily >= 0 && ily < f.CharHeight)
                    {
                        byte* srcPx = sp + ily * srb + ilx * 4;
                        if (srcPx[3] == 0 || (srcPx[0] == 0 && srcPx[1] == 0 && srcPx[2] == 0)) continue;
                        int di = px * 4;
                        rowPtr[di + 0] = srcPx[0];
                        rowPtr[di + 1] = srcPx[1];
                        rowPtr[di + 2] = srcPx[2];
                        rowPtr[di + 3] = 255;
                    }
                }
            }
        }
        
        public void FontChar(int id, int x, int y, string c)
        {
            if (!_fonts.TryGetValue(id, out var f) || string.IsNullOrEmpty(c)) return;
            
            // Justering: Ditt ark verkar börja på 'A' (ASCII 65). 
            // Om vi vill ha siffror och tecken som i din bild behöver vi mappa rätt.
            int charIdx = -1;
            if (!string.IsNullOrEmpty(f.CharMap))
            {
                // Om vi har en karta, använd den
                charIdx = f.CharMap.IndexOf(char.ToUpper(c[0]));
            }
            else
            {
                // Annars kör vi standard ASCII-offset (börjar på space)
                charIdx = c[0] - 32;
            }
            
            if (charIdx < 0 || charIdx >= f.CharBitmaps.Count) return;

            var charBmp = f.CharBitmaps[charIdx];
            var target = GetActiveScreen();
            
            // Vi använder en förenklad RenderSprite-logik här för att stödja Zoom/Rotate
            using var dst = target.Lock();
            using var src = charBmp.Lock();
            unsafe
            {
                byte* dp = (byte*)dst.Address;
                byte* sp = (byte*)src.Address;
                int rb = dst.RowBytes;
                int srb = src.RowBytes;
                
                double angleRad = f.Angle * Math.PI / 180.0;
                double cosA = Math.Cos(angleRad), sinA = Math.Sin(angleRad);
                double invZx = 1.0 / f.ZoomX, invZy = 1.0 / f.ZoomY;
                int hx = f.CharWidth / 2, hy = f.CharHeight / 2;

                double radius = Math.Sqrt(f.CharWidth * f.CharWidth + f.CharHeight * f.CharHeight) * Math.Max(f.ZoomX, f.ZoomY);
                int minX = Math.Max(0, (int)(x - radius)), maxX = Math.Min(target.PixelSize.Width - 1, (int)(x + radius));
                int minY = Math.Max(0, (int)(y - radius)), maxY = Math.Min(target.PixelSize.Height - 1, (int)(y + radius));

                for (int py = minY; py <= maxY; py++)
                {
                    byte* rowPtr = dp + py * rb;
                    double dy = py - y;
                    for (int px = minX; px <= maxX; px++)
                    {
                        double dx = px - x;
                        double lx = (dx * cosA + dy * sinA) * invZx + hx;
                        double ly = (dy * cosA - dx * sinA) * invZy + hy;
                        int ilx = (int)lx, ily = (int)ly;

                        if (ilx >= 0 && ilx < f.CharWidth && ily >= 0 && ily < f.CharHeight)
                        {
                            byte* srcPx = sp + ily * srb + ilx * 4;
                            if (srcPx[3] == 0 || (srcPx[0] == 0 && srcPx[1] == 0 && srcPx[2] == 0)) continue; 
                            int di = px * 4;
                            rowPtr[di + 0] = srcPx[0];
                            rowPtr[di + 1] = srcPx[1];
                            rowPtr[di + 2] = srcPx[2];
                            rowPtr[di + 3] = 255;
                        }
                    }
                }
            }
        }
    
    public void LoadBackground(string f)
    {
        try
        {
            using var b = new Bitmap(f);
            EnsureScreen();
            int cw = Math.Min(Width, (int)b.Size.Width), ch = Math.Min(Height, (int)b.Size.Height);
            using (var fb = GetActiveScreen().Lock())
            {
                b.CopyPixels(new PixelRect(0, 0, cw, ch), fb.Address, fb.RowBytes * Height, fb.RowBytes);
                unsafe
                {
                    var p = (byte*)fb.Address;
                    for (int i = 0; i < Width * Height; i++)
                    {
                        byte temp = p[i * 4 + 0];
                        p[i * 4 + 0] = p[i * 4 + 2];
                        p[i * 4 + 2] = temp;
                        p[i * 4 + 3] = 255;
                    }
                }
            }

            Refresh();
        }
        catch
        {
        }
    }

    // ---------------- Tiles ----------------
    public int GetTilesInWidth() => _tilesInWidth; // NYTT: Getter

    public void LoadTileBank(string f, int tw, int th) {
        try {
            using var b = new Bitmap(f); 
            _tileWidth = tw; 
            _tileHeight = th; 
            _tiles.Clear();
            
            // Uppdatera klassvariabeln för att undvika division med noll i paletten
            _tilesInWidth = (int)b.Size.Width / tw;
            
            int cs = _tilesInWidth; 
            int rs = (int)b.Size.Height / th;

            for (int y = 0; y < rs; y++) {
                for (int x = 0; x < cs; x++) {
                    var t = CreateEmptyBitmap(tw, th);
                    using (var fb = t.Lock()) {
                        b.CopyPixels(new PixelRect(x * tw, y * th, tw, th), fb.Address, fb.RowBytes * th, fb.RowBytes);
                        unsafe {
                            var p = (byte*)fb.Address;
                            for (int i = 0; i < tw * th; i++) {
                                byte temp = p[i * 4 + 0];
                                p[i * 4 + 0] = p[i * 4 + 2];
                                p[i * 4 + 2] = temp;
                            }
                        }
                    }
                    _tiles.Add(t);
                }
            }
        }
        catch {
            // Logga gärna felet här om du vill, t.ex. Console.WriteLine(ex.Message);
        }
    }

    public void LoadTileBank(System.IO.Stream stream, int tw, int th)
    {
        try
        {
            using var b = new Bitmap(stream);
            _tileWidth = tw;
            _tileHeight = th;
            _tiles.Clear();

            // Deklarera tilesInWidth här och spara den i klassvariabeln _tilesInWidth
            int tilesInWidth = (int)b.Size.Width / tw;
            _tilesInWidth = tilesInWidth;

            int tilesInHeight = (int)b.Size.Height / th;

            for (int y = 0; y < tilesInHeight; y++)
            {
                for (int x = 0; x < tilesInWidth; x++)
                {
                    var t = CreateEmptyBitmap(tw, th);
                    using (var fb = t.Lock())
                    {
                        // Kopiera exakt den rutan från källbilden
                        b.CopyPixels(new PixelRect(x * tw, y * th, tw, th), fb.Address, fb.RowBytes * th, fb.RowBytes);
                        unsafe
                        {
                            var p = (byte*)fb.Address;
                            for (int i = 0; i < tw * th; i++)
                            {
                                byte temp = p[i * 4 + 0];
                                p[i * 4 + 0] = p[i * 4 + 2];
                                p[i * 4 + 2] = temp;
                            }
                        }
                    }

                    _tiles.Add(t);
                }
            }
        }
        catch
        {
        }
    }

    public void SetMapSize(int newW, int newH)
    {
        newW = Math.Max(1, newW);
        newH = Math.Max(1, newH);
        
        var oldMap = _map;
        _map = new int[newW, newH];
        for (int y = 0; y < newH; y++)
        for (int x = 0; x < newW; x++)
            _map[x, y] = -1;

        int pixelW = newW * _tileWidth;
        int pixelH = newH * _tileHeight;

        _screens[_currentScreen] = CreateEmptyBitmap(pixelW, pixelH);

        // Initiera banan med -1 (tomt) istället för 0 (första tilen)
        for (int y = 0; y < newH; y++)
        for (int x = 0; x < newW; x++)
            _map[x, y] = -1;

        // Kopiera över gamla datan om den fanns
        int copyW = Math.Min(newW, oldMap.GetLength(0));
        int copyH = Math.Min(newH, oldMap.GetLength(1));

        for (int y = 0; y < copyH; y++)
        for (int x = 0; x < copyW; x++)
            _map[x, y] = oldMap[x, y];
    }

    public void SetMapTile(int x, int y, int tileId)
    {
        if (x >= 0 && x < _map.GetLength(0) && y >= 0 && y < _map.GetLength(1))
        {
            _map[x, y] = tileId;
        }
    }

    public void ClearMap()
    {
        for (int y = 0; y < _map.GetLength(1); y++)
        for (int x = 0; x < _map.GetLength(0); x++)
            _map[x, y] = -1;
    }

    public void DrawMap(int ox, int oy)
    {
        if (_map.GetLength(0) == 0 || _tiles.Count == 0) return;

        // Hämta det lagret vi ska rita på
        var target = GetActiveScreen();
        int targetW = target.PixelSize.Width;
        int targetH = target.PixelSize.Height;

        for (int y = 0; y < _map.GetLength(1); y++)
        {
            for (int x = 0; x < _map.GetLength(0); x++)
            {
                int tid = _map[x, y];
                if (tid < 0 || tid >= _tiles.Count) continue;

                // Beräkna koordinaterna i det stora lagret
                int dx = x * _tileWidth - ox;
                int dy = y * _tileHeight - oy;

                // VIKTIGT: Rita bara om vi är inom lagrets gränser
                if (dx >= 0 && dx < targetW && dy >= 0 && dy < targetH)
                {
                    DrawTileToBackbuffer(_tiles[tid], dx, dy);
                }
            }
        }
    }

    private void DrawTileToBackbuffer(WriteableBitmap t, int dx, int dy)
    {
        var target = GetActiveScreen();
        using var dst = target.Lock();
        using var src = t.Lock();
        unsafe
        {
            var dp = (byte*)dst.Address;
            var sp = (byte*)src.Address;
            // VIKTIGT: Använd target.PixelSize istället för globala Width/Height
            int tw = target.PixelSize.Width;
            int th = target.PixelSize.Height;

            for (int y = 0; y < _tileHeight; y++)
            {
                int ty = dy + y;
                if (ty < 0 || ty >= th) continue;
                var dr = dp + ty * dst.RowBytes;
                var sr = sp + y * src.RowBytes;
                for (int x = 0; x < _tileWidth; x++)
                {
                    int tx = dx + x;
                    if (tx < 0 || tx >= tw) continue;
                    int si = x * 4, di = tx * 4;
                    dr[di + 0] = sr[si + 0];
                    dr[di + 1] = sr[si + 1];
                    dr[di + 2] = sr[si + 2];
                    dr[di + 3] = 255;
                }
            }
        }
    }

    public int GetMapWidth() => _map.GetLength(0);
    public int GetMapHeight() => _map.GetLength(1);

    public int GetMapTile(int x, int y)
    {
        if (x >= 0 && x < _map.GetLength(0) && y >= 0 && y < _map.GetLength(1))
            return _map[x, y];
        return -1;
    }



    // ---------------- Sprites ----------------

    public void CreateSprite(int id, int w, int h)
    {
        var f = CreateEmptyBitmap(w, h);
        _sprites[id] = new Sprite(w, h, f);
        SpriteClear(id, Colors.Magenta);
    }

    public bool HasSprite(int id) => _sprites.ContainsKey(id);

    public (int w, int h) GetSpriteSize(int id)
    {
        var s = GetSprite(id);
        return (s.Width, s.Height);
    }

    public WriteableBitmap GetSpriteBitmap(int id) => GetSprite(id).Bitmap;
    public List<int> GetSpriteIds() => _sprites.Keys.OrderBy(id => id).ToList();

    public void LoadSprite(int id, string fileName)
    {
        try
        {
            using var b = new Bitmap(fileName);
            int w = (int)b.Size.Width, h = (int)b.Size.Height;
            CreateSprite(id, w, h);
            var s = GetSprite(id);
            using (var fb = s.Bitmap.Lock())
            {
                b.CopyPixels(new PixelRect(0, 0, w, h), fb.Address, fb.RowBytes * h, fb.RowBytes);
                unsafe
                {
                    var p = (byte*)fb.Address;
                    for (int i = 0; i < w * h; i++)
                    {
                        byte temp = p[i * 4 + 0];
                        p[i * 4 + 0] = p[i * 4 + 2];
                        p[i * 4 + 2] = temp;
                    }

                    s.TransparentKey = Color.FromArgb(p[3], p[2], p[1], p[0]);
                }
            }
        }
        catch
        {
        }
    }

    public void AddFrame(int id, string file)
    {
        var s = GetSprite(id);
        using var b = new Bitmap(file);
        var f = CreateEmptyBitmap(s.Width, s.Height);
        using (var fb = f.Lock())
        {
            b.CopyPixels(new PixelRect(0, 0, (int)b.Size.Width, (int)b.Size.Height), fb.Address, fb.RowBytes * s.Height,
                fb.RowBytes);
            unsafe
            {
                var p = (byte*)fb.Address;
                for (int i = 0; i < s.Width * s.Height; i++)
                {
                    byte temp = p[i * 4 + 0];
                    p[i * 4 + 0] = p[i * 4 + 2];
                    p[i * 4 + 2] = temp;
                }
            }
        }

        s.Frames.Add(f);
    }

    public void SetSpriteFrame(int id, int idx)
    {
        var s = GetSprite(id);
        if (idx >= 0 && idx < s.Frames.Count) s.CurrentFrame = idx;
    }

    public void SpriteHandle(int id, int hx, int hy)
    {
        var s = GetSprite(id);
        s.HandleX = hx;
        s.HandleY = hy;
    }

    public void SpritePos(int id, int x, int y)
    {
        var s = GetSprite(id);
        s.X = x;
        s.Y = y;
    }

    public void SpriteRotate(int id, double angle) => GetSprite(id).Angle = angle;

    public void SpriteZoom(int id, double zx, double zy)
    {
        var s = GetSprite(id);
        s.ZoomX = zx;
        s.ZoomY = zy;
    }

    public void SpriteOn(int id) => GetSprite(id).Visible = true;
    public void SpriteOff(int id) => GetSprite(id).Visible = false;

    public void SpriteSetPixel(int id, int x, int y, Color c)
    {
        var s = GetSprite(id);
        if ((uint)x >= (uint)s.Width || (uint)y >= (uint)s.Height) return;
        using var fb = s.Bitmap.Lock();
        unsafe
        {
            var r = (byte*)fb.Address + y * fb.RowBytes;
            var i = x * 4;
            r[i + 0] = c.B;
            r[i + 1] = c.G;
            r[i + 2] = c.R;
            r[i + 3] = c.A;
        }
    }

    public void SpriteClear(int id, Color c)
    {
        var s = GetSprite(id);
        using var fb = s.Bitmap.Lock();
        unsafe
        {
            var p = (byte*)fb.Address;
            for (var i = 0; i < fb.RowBytes * s.Height; i += 4)
            {
                p[i + 0] = c.B;
                p[i + 1] = c.G;
                p[i + 2] = c.R;
                p[i + 3] = c.A;
            }
        }
    }

    public void SpriteInk(int id, Color c) => GetSprite(id).Ink = c;

    public void SpritePlot(int id, int x, int y)
    {
        var s = GetSprite(id);
        SpriteSetPixel(id, x, y, s.Ink);
    }

    public void SpriteBar(int id, int x1, int y1, int x2, int y2)
    {
        var s = GetSprite(id);
        Normalize(ref x1, ref y1, ref x2, ref y2);
        x1 = Math.Clamp(x1, 0, s.Width - 1);
        x2 = Math.Clamp(x2, 0, s.Width - 1);
        y1 = Math.Clamp(y1, 0, s.Height - 1);
        y2 = Math.Clamp(y2, 0, s.Height - 1);
        using var fb = s.Bitmap.Lock();
        unsafe
        {
            var p = (byte*)fb.Address;
            for (var y = y1; y <= y2; y++)
            {
                var r = p + y * fb.RowBytes;
                for (var x = x1; x <= x2; x++)
                {
                    var i = x * 4;
                    r[i + 0] = s.Ink.B;
                    r[i + 1] = s.Ink.G;
                    r[i + 2] = s.Ink.R;
                    r[i + 3] = s.Ink.A;
                }
            }
        }
    }

    public bool SpriteHit(int id1, int id2)
    {
        if (!_sprites.TryGetValue(id1, out var s1) || !_sprites.TryGetValue(id2, out var s2)) return false;
        if (!s1.Visible || !s2.Visible) return false;
        int x1 = s1.X - s1.HandleX, y1 = s1.Y - s1.HandleY, x2 = s2.X - s2.HandleX, y2 = s2.Y - s2.HandleY;
        return x1 < x2 + s2.Width && x1 + s1.Width > x2 && y1 < y2 + s2.Height && y1 + s1.Height > y2;
    }

    private Sprite GetSprite(int id)
    {
        if (!_sprites.TryGetValue(id, out var s))
        {
            CreateSprite(id, 32, 32);
            return _sprites[id];
        }

        return s;
    }

    private void Normalize(ref int x1, ref int y1, ref int x2, ref int y2)
    {
        if (x2 < x1) (x1, x2) = (x2, x1);
        if (y2 < y1) (y1, y2) = (y2, y1);
    }

    private unsafe void RenderSpriteInternal(byte* dp, int rb, Sprite s)
    {
        var bmp = s.Bitmap;
        int sw = bmp.PixelSize.Width, sh = bmp.PixelSize.Height;
        var k = s.TransparentKey;

        double angleRad = s.Angle * Math.PI / 180.0;
        double cosA = Math.Cos(angleRad), sinA = Math.Sin(angleRad);
        double invZoomX = 1.0 / s.ZoomX, invZoomY = 1.0 / s.ZoomY;

        // Enkel bounding box för att veta vilka pixlar på skärmen vi behöver kontrollera
        double radius = Math.Sqrt(sw * sw + sh * sh) * Math.Max(s.ZoomX, s.ZoomY);
        int minX = Math.Max(0, (int)(s.X - radius)), maxX = Math.Min(Width - 1, (int)(s.X + radius));
        int minY = Math.Max(0, (int)(s.Y - radius)), maxY = Math.Min(Height - 1, (int)(s.Y + radius));

        using var sLock = bmp.Lock();
        byte* sp = (byte*)sLock.Address;
        int srb = sLock.RowBytes;

        for (int y = minY; y <= maxY; y++)
        {
            byte* rowPtr = dp + y * rb;
            double dy = y - s.Y;
            for (int x = minX; x <= maxX; x++)
            {
                double dx = x - s.X;
                // Rotera och skala tillbaka till käll-spritens koordinater
                double lx = (dx * cosA + dy * sinA) * invZoomX + s.HandleX;
                double ly = (dy * cosA - dx * sinA) * invZoomY + s.HandleY;
                int ilx = (int)lx, ily = (int)ly;

                if (ilx >= 0 && ilx < sw && ily >= 0 && ily < sh)
                {
                    byte* srcPx = sp + ily * srb + ilx * 4;
                    if (srcPx[2] == k.R && srcPx[1] == k.G && srcPx[0] == k.B) continue;
                    int di = x * 4;
                    rowPtr[di + 0] = srcPx[0];
                    rowPtr[di + 1] = srcPx[1];
                    rowPtr[di + 2] = srcPx[2];
                    rowPtr[di + 3] = 255;
                }
            }
        }
    }
}