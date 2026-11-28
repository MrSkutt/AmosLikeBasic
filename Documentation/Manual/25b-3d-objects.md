# 3D objects

The Amiga and the Atari ST were famous for their turning **vector cubes**: shapes made of flat sides, worked out in 3D and drawn on the screen many times a second. AMOS Like Basic has the same with the `OBJECT` commands. You make a shape, give it a texture or a colour, and place and turn it in 3D. The graphics card draws it.

## A first cube

```basic
OBJECT CUBE 1, 120
OBJECT TEXTURE 1, "Graphics/Textures/256_002.png"
OBJECT POS 1, 0, 0, 400
T = 0
Loop:
  T = T + 1
  OBJECT ROTATE 1, T, T * 1.5, 0
  WAIT VBL
GOTO Loop
```

`OBJECT CUBE 1, 120` makes object number 1, a cube with sides of 120. The picture goes on each of its six sides. `OBJECT POS` puts it 400 in front of the camera, and `OBJECT ROTATE` turns it a little more each frame.

## Where things are: x, y and z

A 3D object has three numbers for where it is:

- **x** goes to the right, as on the screen.
- **y** goes down, as on the screen.
- **z** goes into the screen, away from you.

The camera starts at 0, 0, 0 and looks along z. So `OBJECT POS 1, 0, 0, 400` is straight ahead, and an object with a larger z is farther away and looks smaller. A negative y is up. An object with a z below 0 is behind the camera and is not seen.

The objects are drawn in the layer that was selected when they were made, over what is drawn in that layer and under its texts and sprites. `OBJECT LAYER` moves an object to another layer.

## The shapes

| Command | Makes |
|---|---|
| `OBJECT CUBE no, size [, height, depth]` | a cube, or a box when height and depth are given |
| `OBJECT SPHERE no, radius [, detail]` | a ball; detail is how many pieces it has round it (24) |
| `OBJECT CYLINDER no, radius, height [, detail]` | a cylinder standing up |
| `OBJECT CONE no, radius, height [, sides]` | a cone; with 4 sides a pyramid |
| `OBJECT TORUS no, radius, thickness [, detail]` | a ring like a doughnut, lying flat |
| `OBJECT PLANE no, width, depth [, tilesX, tilesZ]` | a flat floor, the texture repeated tilesX × tilesZ times |
| `OBJECT LOAD no, "file.obj"` | a model from a 3D program, see below |

The shapes are built round their middle, so `OBJECT POS` places the middle. A plane lies flat like a floor. Turned 90 degrees round x (`OBJECT ROTATE no, 90, 0, 0`) it stands up like a wall.

Making a shape again with the same number changes the shape but keeps its texture, colour, place and angle.

## Textures and colours

`OBJECT TEXTURE no, "picture"` wraps a picture round the object: once on each side of a cube, and once round a ball like a map round a globe. A fourth value of 1 makes the pixels sharp, as on old computers; 0 is smooth. Two more values say how many times the picture repeats across the shape:

```basic nocheck
OBJECT SPHERE 1, 100
OBJECT TEXTURE 1, "Graphics/Textures/256_009.png", 0, 2, 1   ; the checks twice round, once down
```

`OBJECT COLOUR no, "colour"` gives the object a colour, with the same colour names as `INKG`. With a texture, the colour tints it; white leaves it as it is. `OBJECT TEXTURE no, OFF` takes the texture away again. `OBJECT ALPHA no, value` makes the object see-through, from 0 (invisible) to 255.

## Moving, turning and zooming

| Command | Does |
|---|---|
| `OBJECT POS no, x, y, z` | where the object's middle is |
| `OBJECT ROTATE no, ax, ay, az` | turns it round x, y and z, in degrees, in that order |
| `OBJECT ZOOM no, percent [, percentY, percentZ]` | its size; 100 is as it was made |
| `OBJECT ON` / `OFF no` | shows or hides it |
| `OBJECT DEL no` | removes it |
| `OBJECT CLEAR` | removes all objects |

Turning round y spins an object like a top. Turning round x tips it towards you, and round z turns it like a wheel facing you.

## The camera

```basic nocheck
CAMERA POS x, y, z      ; where the camera is
CAMERA LOOK x, y, z     ; the point it looks at
CAMERA FOV degrees      ; how wide it sees, 60 to begin with
```

A camera that moves round in a circle and always looks at the middle flies round the objects:

```basic
OBJECT CUBE 1, 150
OBJECT COLOUR 1, "Orange"
CAMERA LOOK 0, 0, 0
T = 0
Loop:
  T = T + 1
  CAMERA POS SIN(T) * 500, -200, COS(T) * 500
  WAIT VBL
GOTO Loop
```

## Light

The objects are lit by a lamp far away, so the sides that face it are light and the ones away from it darker. `LIGHT x, y, z` says which way the light comes from. The standard is from the top left, a little in front: `LIGHT -1, -1.2, -1`. A fourth value is how light the dark sides are, in percent (30). `LIGHT OFF` shows everything in its own colours without shading.

## Models from 3D programs

`OBJECT LOAD no, "file.obj"` loads a model saved as Wavefront .obj, a format that Blender and most 3D programs can save. The model's corners, faces, normals and texture places are read; give it a texture with `OBJECT TEXTURE`. 3D programs usually have y going up; it is turned the right way as it is loaded. Models are often small, so `OBJECT ZOOM` may be needed to see them.

A spaceship comes with AMOS Like Basic, in `Graphics/Models`. `Starfighter.png` is the picture its faces are painted with:

```basic
OBJECT LOAD 1, "Graphics/Models/Starfighter.obj"
OBJECT TEXTURE 1, "Graphics/Models/Starfighter.png"
OBJECT ZOOM 1, 145
OBJECT POS 1, 0, 0, 420
T = 0
Loop:
  T = T + 1
  OBJECT ROTATE 1, SIN(T) * 20, T, 0
  WAIT VBL
GOTO Loop
```

A model has one texture for all of it. The model knows which part of the picture goes on which face, so one picture can hold the grey hull, the blue glass and the red stripes side by side. In Blender this is called *UV mapping*; save the .obj with UV coordinates and paint the picture it gives. Materials (.mtl files) are not read.

The example *Starfighter* lets the ship turn among rocks in space. The space there is made with `RASTER SPACE` in layer 0, and the objects are in layer 1: a raster background covers everything in its own layer, objects too.

## Good to know

- All the triangles of a layer are sorted and drawn from the back to the front. Objects that go through each other can come out wrong where they cross. Keep them apart, or put them in different layers.
- A few thousand triangles are no problem. A sphere with detail 24 has about 550, a cube 192.
- An object never moves by itself. Change its place and angle before each `WAIT VBL`.

> **Try it:** Put an `OBJECT PLANE` under a cube as a floor, and make the cube jump with `ABS(SIN(T))`.

> **Try it:** Open the examples *Vector Cubes*, *Boing 3D*, *3D Shapes* and *Starfighter* and change the textures.
