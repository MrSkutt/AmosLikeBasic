using System.Collections.Generic;

namespace AmosLikeBasic;

// The example projects in Projects/, as they are listed under Project → Examples.
// A test checks that this list and the files in Projects/ agree, so a new example
// must be added here too.
public static class Examples
{
    public sealed record Example(string File, string Title, string Description);

    public sealed record Group(string Name, IReadOnlyList<Example> Items);

    public static readonly IReadOnlyList<Group> Groups = new[]
    {
        new Group("Games", new[]
        {
            new Example("foresthop", "Forest Hop", "A whole platform game: parallax scrolling, a tile map, enemies and tracker music"),
            new Example("froghop", "Frog Hop", "Jump up through holes that slide sideways, floor by floor"),
            new Example("snake", "Snake", "The classic snake game, with tracker music"),
            new Example("galaga", "Alien Formation", "A formation of rotating space ships"),
            new Example("tile_platformer", "Tile Platformer", "A hero running on a level made with the map editor"),
            new Example("adventure", "Text Adventure", "Type commands to walk between rooms and pick things up"),
            new Example("guess_the_number", "Guess the Number", "INPUT, IF and loops in a few lines"),
        }),
        new Group("Graphics and effects", new[]
        {
            new Example("raster_bars", "Raster Bars", "Moving colour bars behind a picture, demo style"),
            new Example("tunnel", "Sine Tunnel", "An animated tunnel with particles and a scrolling text"),
            new Example("parallax_layers", "Parallax Layers", "Mountain ranges that scroll at different speeds; steer with left and right"),
            new Example("solar_system", "Solar System", "Planets on a raster sky, with C64-style music"),
            new Example("animated_sprites", "Animated Sprites", "Sprites with frames, zoom and rotation"),
            new Example("sprite_collisions", "Sprite Collisions", "Steer a hero and see when it touches the enemies"),
            new Example("fading_screen", "Fading Screen", "Fade a whole layer in and out"),
            new Example("fading_sprites", "Fading Sprites", "Fade a sprite in and out"),
            new Example("font_effects", "Bitmap Fonts", "Fonts drawn from images, zoomed and coloured"),
            new Example("shadow_text", "Shadow Text", "Text with a shadow on two layers"),
            new Example("phalanx", "Big Text", "Large text in a system font over a raster"),
            new Example("weather", "Snow and Rain", "The particle effects for snow and rain"),
            new Example("merry_christmas", "Merry Christmas", "A Christmas card with music, rasters and particles"),
            new Example("c64_colours", "C64 Colours", "The 16 colours of the palette in the C64 font"),
        }),
        new Group("Sound and music", new[]
        {
            new Example("trackerdemo", "Tracker Demo", "Plays a song made in the tracker"),
            new Example("forestsong", "Forest Song with PLAY", "The Forest Hop song written with PLAY commands"),
            new Example("synthdemo", "Synth Demo", "Four channels in C64 style: pulse width, vibrato and filters"),
            new Example("synth_song", "Synth Song", "A song for the four synth channels"),
        }),
    };
}
