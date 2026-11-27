using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using SkiaSharp;

namespace AmosLikeBasic;

// ════════════════════════════════════════════════════════════════════
//  3D objects (OBJECT, CAMERA, LIGHT)
//
//  Simple shapes, and models from .obj files, with a texture or a colour and light on them.
//  The world has x to the right, y downwards (as on the screen) and z into the screen. The
//  corners are turned and projected here, and the graphics card draws the triangles, textured,
//  in the object's layer. All the triangles of a layer are drawn from the back to the front (the
//  painter's way, with no depth buffer), so objects that go through each other can come out wrong.
// ════════════════════════════════════════════════════════════════════

// A shape: a list of triangles, three corners each, with a normal (outwards) and a place in
// the texture (0 – 1, more repeats it) at every corner. Never changed once made.
public sealed class Mesh3D
{
    public readonly Vector3[] P;
    public readonly Vector3[] N;
    public readonly Vector2[] UV;
    public readonly bool TwoSided;   // a flat shape seen from both sides (PLANE)

    public Mesh3D(Vector3[] p, Vector3[] n, Vector2[] uv, bool twoSided = false)
    {
        P = p; N = n; UV = uv; TwoSided = twoSided;
    }

    public int Triangles => P.Length / 3;

    // Collects triangles and turns each so that its corners go round the right way: the cross
    // product of its edges points outwards, the same way as its normals (used to skip back faces)
    private sealed class Builder
    {
        public readonly List<Vector3> P = new();
        public readonly List<Vector3> N = new();
        public readonly List<Vector2> UV = new();

        public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 na, Vector3 nb, Vector3 nc, Vector2 ta, Vector2 tb, Vector2 tc)
        {
            var cross = Vector3.Cross(b - a, c - a);
            if (cross.LengthSquared() < 1e-12f) return;   // no area (at a pole)
            if (Vector3.Dot(cross, na + nb + nc) < 0) { (b, c) = (c, b); (nb, nc) = (nc, nb); (tb, tc) = (tc, tb); }
            P.Add(a); P.Add(b); P.Add(c);
            N.Add(na); N.Add(nb); N.Add(nc);
            UV.Add(ta); UV.Add(tb); UV.Add(tc);
        }

        // A grid of quads from the function f(u, v) → (place, normal), u and v from 0 to 1
        public void Grid(int su, int sv, Func<float, float, (Vector3 p, Vector3 n)> f, Vector2 uvScale, bool flipV = false)
        {
            for (int j = 0; j < sv; j++)
            for (int i = 0; i < su; i++)
            {
                float u0 = (float)i / su, u1 = (float)(i + 1) / su, v0 = (float)j / sv, v1 = (float)(j + 1) / sv;
                var (p00, n00) = f(u0, v0); var (p10, n10) = f(u1, v0);
                var (p11, n11) = f(u1, v1); var (p01, n01) = f(u0, v1);
                Vector2 T(float u, float v) => new(u * uvScale.X, (flipV ? 1 - v : v) * uvScale.Y);
                Tri(p00, p10, p11, n00, n10, n11, T(u0, v0), T(u1, v0), T(u1, v1));
                Tri(p00, p11, p01, n00, n11, n01, T(u0, v0), T(u1, v1), T(u0, v1));
            }
        }

        public Mesh3D Build(bool twoSided = false) => new(P.ToArray(), N.ToArray(), UV.ToArray(), twoSided);
    }

    // A box w x h x d round its middle; the whole texture on each side
    public static Mesh3D Box(float w, float h, float d, int seg = 4)
    {
        var b = new Builder();
        float x = w / 2, y = h / 2, z = d / 2;
        // Each side: its middle, the way u and v go across it, and its normal
        (Vector3 c, Vector3 du, Vector3 dv, Vector3 n)[] sides =
        {
            (new(0, 0, -z), new(w, 0, 0), new(0, h, 0), -Vector3.UnitZ),   // front (towards the camera)
            (new(0, 0, z), new(-w, 0, 0), new(0, h, 0), Vector3.UnitZ),    // back
            (new(-x, 0, 0), new(0, 0, -d), new(0, h, 0), -Vector3.UnitX),  // left
            (new(x, 0, 0), new(0, 0, d), new(0, h, 0), Vector3.UnitX),     // right
            (new(0, -y, 0), new(w, 0, 0), new(0, 0, d), -Vector3.UnitY),   // top
            (new(0, y, 0), new(w, 0, 0), new(0, 0, -d), Vector3.UnitY),    // bottom
        };
        foreach (var (c, du, dv, n) in sides)
            b.Grid(seg, seg, (u, v) => (c + du * (u - 0.5f) + dv * (v - 0.5f), n), Vector2.One);
        return b.Build();
    }

    // A ball; the texture is wrapped round it once, as a map round a globe
    public static Mesh3D Sphere(float r, int detail = 24)
    {
        detail = Math.Clamp(detail, 4, 128);
        var b = new Builder();
        b.Grid(detail, Math.Max(2, detail / 2), (u, v) =>
        {
            double lon = u * 2 * Math.PI, lat = v * Math.PI;
            var n = new Vector3((float)(Math.Sin(lat) * Math.Sin(lon)), (float)-Math.Cos(lat), (float)(-Math.Sin(lat) * Math.Cos(lon)));
            return (n * r, n);
        }, Vector2.One);
        return b.Build();
    }

    // A cylinder (rTop = rBottom), a cone (rTop = 0) or a pyramid (a cone with 3 or 4 sides), standing
    // on its middle; the texture round its side and once on each end
    public static Mesh3D Cylinder(float rBottom, float rTop, float h, int detail = 24)
    {
        detail = Math.Clamp(detail, 3, 128);
        bool smooth = detail > 8;
        var b = new Builder();
        float y0 = h / 2, y1 = -h / 2;
        float slope = (rBottom - rTop) / h;
        for (int i = 0; i < detail; i++)
        {
            double a0 = 2 * Math.PI * i / detail, a1 = 2 * Math.PI * (i + 1) / detail, am = (a0 + a1) / 2;
            Vector3 Ring(double a, float r, float y) => new((float)Math.Sin(a) * r, y, -(float)Math.Cos(a) * r);
            Vector3 Normal(double a) => Vector3.Normalize(new Vector3((float)Math.Sin(a), slope, -(float)Math.Cos(a)));
            var n0 = smooth ? Normal(a0) : Normal(am);
            var n1 = smooth ? Normal(a1) : Normal(am);
            float u0 = (float)i / detail, u1 = (float)(i + 1) / detail;
            var b0 = Ring(a0, rBottom, y0); var b1 = Ring(a1, rBottom, y0);
            var t0 = Ring(a0, rTop, y1); var t1 = Ring(a1, rTop, y1);
            b.Tri(b0, b1, t1, n0, n1, n1, new(u0, 1), new(u1, 1), new(u1, 0));
            b.Tri(b0, t1, t0, n0, n1, n0, new(u0, 1), new(u1, 0), new(u0, 0));
            Vector2 Cap(Vector3 p, float r) => new(0.5f + p.X / (2 * r), 0.5f + p.Z / (2 * r));
            var down = Vector3.UnitY;
            if (rBottom > 0) b.Tri(new(0, y0, 0), b0, b1, down, down, down, new(0.5f, 0.5f), Cap(b0, rBottom), Cap(b1, rBottom));
            if (rTop > 0) b.Tri(new(0, y1, 0), t0, t1, -down, -down, -down, new(0.5f, 0.5f), Cap(t0, rTop), Cap(t1, rTop));
        }
        return b.Build();
    }

    // A ring like a doughnut lying flat round the y axis: radius to the middle of the tube, and the tube's radius
    public static Mesh3D Torus(float radius, float tube, int detail = 32)
    {
        detail = Math.Clamp(detail, 6, 128);
        var b = new Builder();
        b.Grid(detail, Math.Max(3, detail / 2), (u, v) =>
        {
            double a = u * 2 * Math.PI, t = v * 2 * Math.PI;
            var dir = new Vector3((float)Math.Sin(a), 0, -(float)Math.Cos(a));
            var n = dir * (float)Math.Cos(t) + new Vector3(0, -(float)Math.Sin(t), 0);
            return (dir * radius + n * tube, n);
        }, new Vector2(4, 1));
        return b.Build();
    }

    // A flat floor w x d lying round its middle, seen from above and below; the texture repeated tx x tz times
    public static Mesh3D Plane(float w, float d, float tx = 1, float tz = 1)
    {
        var b = new Builder();
        // Small enough pieces that the texture does not bend where the floor comes close to the camera
        int su = Math.Clamp((int)Math.Ceiling(tx * 2), 8, 48), sv = Math.Clamp((int)Math.Ceiling(tz * 2), 8, 48);
        b.Grid(su, sv, (u, v) => (new Vector3((u - 0.5f) * w, 0, (0.5f - v) * d), -Vector3.UnitY), new Vector2(tx, tz));
        return b.Build(twoSided: true);
    }

    // A Wavefront .obj model (from Blender and most 3D programs): v, vt, vn and f lines. The file's y
    // goes up, so it is turned the right way for this world, where y goes down.
    public static Mesh3D LoadObj(string path)
    {
        var pos = new List<Vector3>();
        var tex = new List<Vector2>();
        var nor = new List<Vector3>();
        var b = new Builder();
        var inv = CultureInfo.InvariantCulture;
        float F(string s) => float.Parse(s, NumberStyles.Float, inv);

        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            switch (parts[0])
            {
                case "v": pos.Add(new Vector3(F(parts[1]), -F(parts[2]), F(parts[3]))); break;
                case "vt": tex.Add(new Vector2(F(parts[1]), 1 - (parts.Length > 2 ? F(parts[2]) : 0))); break;
                case "vn": nor.Add(Vector3.Normalize(new Vector3(F(parts[1]), -F(parts[2]), F(parts[3])))); break;
                case "f":
                {
                    var corners = new List<(Vector3 p, Vector2 t, Vector3? n)>();
                    for (int i = 1; i < parts.Length; i++)
                    {
                        var idx = parts[i].Split('/');
                        int Index(string s, int count) { int k = int.Parse(s, inv); return k < 0 ? count + k : k - 1; }
                        var p = pos[Index(idx[0], pos.Count)];
                        var t = idx.Length > 1 && idx[1].Length > 0 ? tex[Index(idx[1], tex.Count)] : Vector2.Zero;
                        Vector3? n = idx.Length > 2 && idx[2].Length > 0 ? nor[Index(idx[2], nor.Count)] : null;
                        corners.Add((p, t, n));
                    }
                    for (int i = 1; i + 1 < corners.Count; i++)
                    {
                        var (a, c, d) = (corners[0], corners[i], corners[i + 1]);
                        // The file goes round its faces anticlockwise; with y turned it is clockwise,
                        // so the face's normal is the cross product the other way round
                        var flat = Vector3.Cross(d.p - a.p, c.p - a.p);
                        if (flat.LengthSquared() < 1e-12f) continue;
                        flat = Vector3.Normalize(flat);
                        b.Tri(a.p, c.p, d.p, a.n ?? flat, c.n ?? flat, d.n ?? flat, a.t, c.t, d.t);
                    }
                    break;
                }
            }
        }
        if (b.P.Count == 0) throw new InvalidDataException("no faces in the file");
        return b.Build();
    }
}

// One 3D object as the program has set it up
public sealed class Object3D
{
    public Mesh3D Mesh;
    public SKImage? Texture;
    public bool SharpTexture;
    public Vector2 TextureTimes = Vector2.One;   // how many times the texture goes across the shape
    public SKColor Colour = SKColors.White;
    public Vector3 Pos;
    public Vector3 Angle;            // degrees round x, y and z
    public Vector3 Zoom = Vector3.One;
    public float Alpha = 1f;
    public bool Visible = true;
    public int Layer;

    public Object3D(Mesh3D mesh, int layer) { Mesh = mesh; Layer = layer; }

    public Matrix4x4 World =>
        Matrix4x4.CreateScale(Zoom) *
        Matrix4x4.CreateRotationX(Angle.X * MathF.PI / 180) *
        Matrix4x4.CreateRotationY(Angle.Y * MathF.PI / 180) *
        Matrix4x4.CreateRotationZ(Angle.Z * MathF.PI / 180) *
        Matrix4x4.CreateTranslation(Pos);
}

// Everything the screen needs to draw the objects, copied at WAIT VBL
public sealed record Object3DState(Mesh3D Mesh, SKImage? Texture, bool SharpTexture, Vector2 TextureTimes, SKColor Colour, float Alpha, Matrix4x4 World, int Layer);

public sealed record Camera3D(Vector3 Pos, Vector3 Look, float Fov)
{
    public static readonly Camera3D Default = new(Vector3.Zero, new Vector3(0, 0, 1), 60);

    // The camera's right, down and forward
    public (Vector3 r, Vector3 d, Vector3 f) Basis()
    {
        var f = Look - Pos;
        f = f.LengthSquared() < 1e-9f ? Vector3.UnitZ : Vector3.Normalize(f);
        var r = Vector3.Cross(Vector3.UnitY, f);
        r = r.LengthSquared() < 1e-9f ? Vector3.UnitX : Vector3.Normalize(r);
        return (r, Vector3.Cross(f, r), f);
    }

    // A point in the world on the screen w x h; null behind the camera
    public Vector2? Project(Vector3 p, int w, int h)
    {
        var (r, d, f) = Basis();
        var q = p - Pos;
        float z = Vector3.Dot(q, f);
        if (z < Renderer3D.Near) return null;
        float focal = h / 2f / MathF.Tan(Fov * MathF.PI / 360);
        return new Vector2(w / 2f + Vector3.Dot(q, r) * focal / z, h / 2f + Vector3.Dot(q, d) * focal / z);
    }
}

// The direction the light comes from, and how light the side away from it is (0 – 1). Off = no shading.
public sealed record Light3D(Vector3 From, float Ambient, bool On)
{
    public static readonly Light3D Default = new(new Vector3(-1, -1.2f, -1), 0.3f, true);
}

public sealed record Scene3DState(List<Object3DState> Objects, Camera3D Camera, Light3D Light);

public static class Renderer3D
{
    public const float Near = 1f;

    private struct Corner
    {
        public Vector3 C;      // in the camera's space
        public Vector2 UV;
        public float Light;
    }

    // Draws the objects of one layer, w x h, on the canvas. All their triangles are sorted together
    // from the back to the front, so that a floor and the shapes standing on it come out right; the
    // triangles that follow each other from the same object are drawn in one go.
    public static void Draw(SKCanvas canvas, IReadOnlyList<Object3DState> objects, Camera3D camera, Light3D light, int w, int h)
    {
        if (objects.Count == 0) return;
        var (r, d, f) = camera.Basis();
        float focal = h / 2f / MathF.Tan(Math.Clamp(camera.Fov, 1, 170) * MathF.PI / 360);
        var toLight = Vector3.Normalize(light.From.LengthSquared() < 1e-9f ? -Vector3.UnitZ : light.From);
        Vector3 ToCamera(Vector3 p) { var q = p - camera.Pos; return new(Vector3.Dot(q, r), Vector3.Dot(q, d), Vector3.Dot(q, f)); }

        var tris = new List<(float depth, int obj, Corner a, Corner b, Corner c)>();
        var poly = new List<Corner>(4);
        for (int oi = 0; oi < objects.Count; oi++)
        {
            var o = objects[oi];
            var mesh = o.Mesh;
            int n = mesh.P.Length;
            var corners = new Corner[n];
            Matrix4x4.Invert(o.World, out var inv);
            var normalMatrix = Matrix4x4.Transpose(inv);
            bool mirrored = o.World.GetDeterminant() < 0;
            for (int i = 0; i < n; i++)
            {
                var p = Vector3.Transform(mesh.P[i], o.World);
                float lit = 1f;
                if (light.On)
                {
                    var nw = Vector3.TransformNormal(mesh.N[i], normalMatrix);
                    if (nw.LengthSquared() > 1e-12f) nw = Vector3.Normalize(nw);
                    float diffuse = Vector3.Dot(nw, toLight);
                    if (mesh.TwoSided) diffuse = MathF.Abs(diffuse);
                    lit = light.Ambient + (1 - light.Ambient) * MathF.Max(0, diffuse);
                }
                corners[i] = new Corner { C = ToCamera(p), UV = mesh.UV[i], Light = lit };
            }

            // The triangles that face the camera, cut at the near plane
            for (int t = 0; t < n; t += 3)
            {
                Corner a = corners[t], b = corners[t + 1], c = corners[t + 2];
                if (!mesh.TwoSided)
                {
                    var facing = Vector3.Dot(Vector3.Cross(b.C - a.C, c.C - a.C), a.C);
                    if (mirrored ? facing <= 0 : facing >= 0) continue;
                }
                if (a.C.Z < Near || b.C.Z < Near || c.C.Z < Near)
                {
                    if (a.C.Z < Near && b.C.Z < Near && c.C.Z < Near) continue;
                    ClipNear(a, b, c, poly);
                    for (int k = 1; k + 1 < poly.Count; k++)
                        tris.Add((Depth(poly[0], poly[k], poly[k + 1]), oi, poly[0], poly[k], poly[k + 1]));
                }
                else tris.Add((Depth(a, b, c), oi, a, b, c));
            }
        }
        if (tris.Count == 0) return;
        // From the back to the front; the same depth keeps the objects' order
        var order = Enumerable.Range(0, tris.Count).ToArray();
        Array.Sort(order, (x, y) =>
        {
            int c = tris[y].depth.CompareTo(tris[x].depth);
            return c != 0 ? c : x.CompareTo(y);
        });

        int start = 0;
        while (start < order.Length)
        {
            int obj = tris[order[start]].obj, end = start;
            while (end < order.Length && tris[order[end]].obj == obj) end++;
            DrawRun(canvas, objects[obj], tris, order, start, end, focal, w, h);
            start = end;
        }
    }

    // The distance used to sort a triangle: its middle's, a little towards its farthest corner,
    // so that a long floor triangle is not counted as nearer than a shape standing on it
    private static float Depth(Corner a, Corner b, Corner c) =>
        (a.C.Z + b.C.Z + c.C.Z) / 3 * 0.75f + MathF.Max(a.C.Z, MathF.Max(b.C.Z, c.C.Z)) * 0.25f;

    private static void DrawRun(SKCanvas canvas, Object3DState o, List<(float depth, int obj, Corner a, Corner b, Corner c)> tris,
        int[] order, int start, int end, float focal, int w, int h)
    {
        int count = (end - start) * 3;
        var positions = new SKPoint[count];
        var texs = o.Texture != null ? new SKPoint[count] : null;
        var colours = new SKColor[count];
        float tw = (o.Texture?.Width ?? 1) * o.TextureTimes.X, th = (o.Texture?.Height ?? 1) * o.TextureTimes.Y;
        byte alpha = (byte)Math.Round(Math.Clamp(o.Alpha, 0, 1) * o.Colour.Alpha);
        int v = 0;
        void Add(in Corner corner)
        {
            positions[v] = new SKPoint(w / 2f + corner.C.X * focal / corner.C.Z, h / 2f + corner.C.Y * focal / corner.C.Z);
            if (texs != null) texs[v] = new SKPoint(corner.UV.X * tw, corner.UV.Y * th);
            float lit = Math.Clamp(corner.Light, 0, 1);
            colours[v] = new SKColor((byte)(o.Colour.Red * lit), (byte)(o.Colour.Green * lit), (byte)(o.Colour.Blue * lit), alpha);
            v++;
        }
        for (int i = start; i < end; i++)
        {
            var t = tris[order[i]];
            Add(t.a); Add(t.b); Add(t.c);
        }

        using var vertices = SKVertices.CreateCopy(SKVertexMode.Triangles, positions, texs, colours);
        // The vertex colours times the texture, or times white when there is none
        using var paint = new SKPaint { IsAntialias = false, Color = SKColors.White };
        if (o.Texture != null)
        {
            var sampling = o.SharpTexture ? new SKSamplingOptions(SKFilterMode.Nearest) : new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
            paint.Shader = o.Texture.ToShader(SKShaderTileMode.Repeat, SKShaderTileMode.Repeat, sampling);
            if (alpha < 255) paint.Color = SKColors.White.WithAlpha(alpha);
        }
        canvas.DrawVertices(vertices, SKBlendMode.Modulate, paint);
        paint.Shader?.Dispose();
    }

    // The part of the triangle in front of the near plane (Sutherland – Hodgman), 3 or 4 corners
    private static void ClipNear(Corner a, Corner b, Corner c, List<Corner> outList)
    {
        outList.Clear();
        var input = new[] { a, b, c };
        for (int i = 0; i < 3; i++)
        {
            var p = input[i];
            var q = input[(i + 1) % 3];
            bool pIn = p.C.Z >= Near, qIn = q.C.Z >= Near;
            if (pIn) outList.Add(p);
            if (pIn != qIn)
            {
                float t = (Near - p.C.Z) / (q.C.Z - p.C.Z);
                outList.Add(new Corner
                {
                    C = Vector3.Lerp(p.C, q.C, t),
                    UV = Vector2.Lerp(p.UV, q.UV, t),
                    Light = p.Light + (q.Light - p.Light) * t
                });
            }
        }
    }
}
