using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using SkiaSharp;

namespace AmosLikeBasic;

// The 3D objects, the camera and the light (OBJECT, CAMERA, LIGHT); drawn by Renderer3D in Scene3D.cs
public sealed partial class AmosGraphics
{
    private readonly Dictionary<int, Object3D> _objects = new();
    private Camera3D _camera = Camera3D.Default;
    private Light3D _light = Light3D.Default;
    private Scene3DState? _presentedScene, _shownScene;
    private readonly Dictionary<string, SKImage> _textures = new();   // each picture read once

    public Camera3D Camera { get { lock (LockObject) return _camera; } }

    private Object3D GetObject(int id) =>
        _objects.TryGetValue(id, out var o) ? o : throw new Exception($"There is no object {id}; make it with OBJECT CUBE, SPHERE ...");

    // OBJECT CUBE / SPHERE ...: a new shape for the object, in the layer drawn in now; an object
    // that is already there keeps its texture, colour, place, angle and zoom
    public void SetObjectMesh(int id, Mesh3D mesh)
    {
        lock (LockObject)
        {
            if (_objects.TryGetValue(id, out var o)) o.Mesh = mesh;
            else _objects[id] = new Object3D(mesh, GetActiveScreenNumber());
        }
    }

    public void ObjectPos(int id, float x, float y, float z) { lock (LockObject) GetObject(id).Pos = new Vector3(x, y, z); }
    public void ObjectRotate(int id, float ax, float ay, float az) { lock (LockObject) GetObject(id).Angle = new Vector3(ax, ay, az); }
    public void ObjectZoom(int id, float zx, float zy, float zz) { lock (LockObject) GetObject(id).Zoom = new Vector3(zx, zy, zz); }
    public void ObjectColour(int id, SKColor c) { lock (LockObject) GetObject(id).Colour = c; }
    public void ObjectAlpha(int id, float a) { lock (LockObject) GetObject(id).Alpha = Math.Clamp(a, 0, 1); }
    public void ObjectVisible(int id, bool on) { lock (LockObject) GetObject(id).Visible = on; }
    public void ObjectLayer(int id, int layer) { lock (LockObject) GetObject(id).Layer = layer; }
    public void ObjectDelete(int id) { lock (LockObject) _objects.Remove(id); }
    public void ObjectClear() { lock (LockObject) _objects.Clear(); }

    // OBJECT TEXTURE: a picture file wrapped round the object; null takes it away (the colour only)
    public void ObjectTexture(int id, string? file, bool sharp, float timesX = 1, float timesY = 1)
    {
        SKImage? image = null;
        if (file != null)
        {
            lock (LockObject) _textures.TryGetValue(file, out image);
            if (image == null)
            {
                using var bitmap = SKBitmap.Decode(ResourceLoader.GetPath(file))
                                   ?? throw new Exception($"Could not read the picture '{file}'");
                image = SKImage.FromBitmap(bitmap);
                lock (LockObject) _textures[file] = image;
            }
        }
        lock (LockObject)
        {
            var o = GetObject(id);
            o.Texture = image;
            o.SharpTexture = sharp;
            o.TextureTimes = new System.Numerics.Vector2(timesX, timesY);
        }
    }

    public void SetCamera(Vector3? pos = null, Vector3? look = null, float? fov = null)
    {
        lock (LockObject)
            _camera = _camera with { Pos = pos ?? _camera.Pos, Look = look ?? _camera.Look, Fov = fov ?? _camera.Fov };
    }

    public void SetLight(Light3D light) { lock (LockObject) _light = light; }

    // The caller must hold LockObject
    private Scene3DState BuildSceneState() => new(
        _objects.OrderBy(kv => kv.Key).Select(kv => kv.Value).Where(o => o.Visible)
            .Select(o => new Object3DState(o.Mesh, o.Texture, o.SharpTexture, o.TextureTimes, o.Colour, o.Alpha, o.World, o.Layer)).ToList(),
        _camera, _light);

    public Scene3DState GetSceneState()
    {
        lock (LockObject) return _shownScene ?? _presentedScene ?? BuildSceneState();
    }

    private void PresentScene() => _presentedScene = BuildSceneState();
    private void LatchScene() => _shownScene = _presentedScene;
    private void ClearShownScene() => _shownScene = null;

    private void ClearScene()
    {
        _objects.Clear();
        _textures.Clear();
        _camera = Camera3D.Default;
        _light = Light3D.Default;
        _presentedScene = null;
        _shownScene = null;
    }
}
