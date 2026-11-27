using System.IO;
using System.Numerics;
using Avalonia.Headless.XUnit;
using SkiaSharp;
using Xunit;

namespace AmosLikeBasic.Tests;

public class ThreeDTests
{
    private static RunResult Show(string program) => BasicRunner.Run(program + """

        Loop:
          WAIT VBL
        GOTO Loop
        """, 0.5, capture: true);

    private static bool IsRed(SKColor c) => c.Red > 120 && c.Green < 60 && c.Blue < 60;

    [AvaloniaFact]
    public void A_cube_in_front_of_the_camera_is_drawn_lit_in_the_middle_of_the_screen()
    {
        var r = Show("""
            OBJECT CUBE 1, 100
            OBJECT POS 1, 0, 0, 300
            OBJECT ROTATE 1, 30, 40, 0
            """);
        Assert.True(r.Errors.Count == 0, r.Describe());
        var f = r.Frame!;
        var background = f.GetPixel(5, 5);
        Assert.NotEqual(background, f.GetPixel(320, 240));
        // The light comes from above: the top of the cube is lighter than its front
        Assert.True(f.GetPixel(320, 185).Red > f.GetPixel(320, 270).Red,
            $"top {f.GetPixel(320, 185)}, front {f.GetPixel(320, 270)}");
    }

    [AvaloniaFact]
    public void An_object_behind_the_middle_of_a_floor_is_not_hidden_by_the_floor()
    {
        // The red ball is farther away than the middle of the big floor it stands on
        var r = Show("""
            OBJECT PLANE 1, 3000, 3000
            OBJECT COLOUR 1, "Blue"
            OBJECT POS 1, 0, 100, 500
            OBJECT SPHERE 2, 60
            OBJECT COLOUR 2, "Red"
            OBJECT POS 2, 0, 40, 1200
            LIGHT OFF
            CAMERA POS 0, -100, 0
            CAMERA LOOK 0, 40, 1200
            """);
        Assert.True(r.Errors.Count == 0, r.Describe());
        Assert.True(IsRed(r.Frame!.GetPixel(320, 240)), r.Frame!.GetPixel(320, 240).ToString());
    }

    [AvaloniaFact]
    public void A_floor_that_goes_under_the_camera_is_cut_at_the_camera_and_fills_the_bottom()
    {
        var r = Show("""
            OBJECT PLANE 1, 2000, 2000
            OBJECT COLOUR 1, "Red"
            OBJECT POS 1, 0, 100, 0
            LIGHT OFF
            """);
        Assert.True(r.Errors.Count == 0, r.Describe());
        Assert.True(IsRed(r.Frame!.GetPixel(320, 470)), r.Frame!.GetPixel(320, 470).ToString());
        Assert.False(IsRed(r.Frame!.GetPixel(320, 100)), "above the horizon");
    }

    [AvaloniaFact]
    public void A_textured_object_shows_its_texture()
    {
        var r = Show("""
            OBJECT CUBE 1, 200
            OBJECT TEXTURE 1, "Graphics/Textures/256_009.png", 1
            OBJECT POS 1, 0, 0, 300
            LIGHT OFF
            """);
        Assert.True(r.Errors.Count == 0, r.Describe());
        // Red and white checks on the front
        bool red = false, white = false;
        for (int x = 250; x < 390; x += 4)
        {
            var c = r.Frame!.GetPixel(x, 240);
            red |= IsRed(c);
            white |= c.Red > 200 && c.Green > 200 && c.Blue > 200;
        }
        Assert.True(red && white);
    }

    [AvaloniaFact]
    public void OBJECT_LOAD_reads_an_obj_file()
    {
        var file = Path.Combine(Path.GetTempPath(), "amos_tetra.obj");
        File.WriteAllText(file, """
            # a pyramid with three sides and a bottom
            v 0 1 0
            v -1 -1 1
            v 1 -1 1
            v 0 -1 -1
            f 1 2 3
            f 1 3 4
            f 1 4 2
            f 2 4 3
            """);
        try
        {
            var mesh = Mesh3D.LoadObj(file);
            Assert.Equal(4, mesh.Triangles);
            var r = Show($"""
                OBJECT LOAD 1, "{file}"
                OBJECT ZOOM 1, 8000
                OBJECT POS 1, 0, 0, 400
                """);
            Assert.True(r.Errors.Count == 0, r.Describe());
            Assert.NotEqual(r.Frame!.GetPixel(5, 5), r.Frame!.GetPixel(320, 240));
        }
        finally { File.Delete(file); }
    }

    [AvaloniaFact]
    public void Every_shape_can_be_made_and_moved()
    {
        var r = BasicRunner.Run("""
            OBJECT CUBE 1, 50, 60, 70
            OBJECT SPHERE 2, 40, 12
            OBJECT CYLINDER 3, 30, 80
            OBJECT CONE 4, 30, 80, 4
            OBJECT TORUS 5, 50, 15
            OBJECT PLANE 6, 100, 100, 2, 2
            FOR I = 1 TO 6
              OBJECT POS I, I * 10, 0, 300
              OBJECT ROTATE I, 10, 20, 30
              OBJECT ZOOM I, 120
              OBJECT COLOUR I, "Orange"
              OBJECT ALPHA I, 200
              OBJECT OFF I
              OBJECT ON I
            NEXT I
            OBJECT TEXTURE 1, OFF
            OBJECT LAYER 2, 1
            OBJECT DEL 3
            CAMERA FOV 70
            OBJECT CLEAR
            """, 2);
        Assert.True(r.Errors.Count == 0, r.Describe());
    }

    [AvaloniaFact]
    public void A_misspelt_or_missing_object_gives_an_error()
    {
        Assert.Contains("OBJECT does not know", BasicRunner.Run("OBJECT CUBE 1, 50 : OBJECT POSS 1, 0, 0, 0", 2).Describe());
        Assert.Contains("no object 7", BasicRunner.Run("OBJECT POS 7, 0, 0, 100", 2).Describe());
    }

    [AvaloniaFact]
    public void A_PROC_named_like_a_3D_command_is_still_called()
    {
        var r = BasicRunner.Run("""
            Light 1
            Camera "on"
            END
            PROC Light(N)
              PRINT "light "; N
            END PROC
            PROC Camera(S$)
              PRINT "camera "; S$
            END PROC
            """, 2);
        Assert.True(r.Errors.Count == 0, r.Describe());
        var printed = string.Join("|", r.Prints);
        Assert.Contains("light 1", printed);
        Assert.Contains("camera on", printed);
    }

    [AvaloniaFact]
    public void A_variable_named_like_a_3D_command_can_still_be_given_a_value()
    {
        var r = BasicRunner.Run("LIGHT = 3 : CAMERA = 4 : PRINT LIGHT + CAMERA", 2);
        Assert.True(r.Errors.Count == 0, r.Describe());
        Assert.Contains("7", string.Join("|", r.Prints));
    }

    [Fact]
    public void The_camera_projects_the_point_it_looks_at_to_the_middle()
    {
        var cam = new Camera3D(new Vector3(100, -50, 20), new Vector3(-30, 40, 500), 60);
        var p = cam.Project(new Vector3(-30, 40, 500), 640, 480);
        Assert.NotNull(p);
        Assert.Equal(320, p!.Value.X, 2);
        Assert.Equal(240, p.Value.Y, 2);
        Assert.Null(cam.Project(new Vector3(100, -50, -300), 640, 480));   // behind it
    }

    [Fact]
    public void The_shapes_turn_their_triangles_outwards()
    {
        foreach (var mesh in new[] { Mesh3D.Box(10, 20, 30), Mesh3D.Sphere(10), Mesh3D.Cylinder(5, 5, 10), Mesh3D.Torus(10, 3) })
            for (int i = 0; i < mesh.P.Length; i += 3)
            {
                var cross = Vector3.Cross(mesh.P[i + 1] - mesh.P[i], mesh.P[i + 2] - mesh.P[i]);
                Assert.True(Vector3.Dot(cross, mesh.N[i] + mesh.N[i + 1] + mesh.N[i + 2]) > 0);
            }
    }
}
