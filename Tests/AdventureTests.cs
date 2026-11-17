using Avalonia.Headless.XUnit;

namespace AmosLikeBasic.Tests;

// The Text Adventure example, played with typed commands
public class AdventureTests
{
    private static RunResult Play(params string[] commands) =>
        BasicRunner.Run(BasicRunner.ProgramOf(Repo.PathOf("Projects/adventure.amosproj")), 4, input: commands);

    private static string Text(RunResult r) => string.Join("\n", r.Prints);

    [AvaloniaFact]
    public void The_game_can_be_won()
    {
        var r = Play("w", "take lamp", "e", "u", "turn on lamp", "u", "read letter", "take letter", "d",
                     "e", "examine book", "take key", "w", "d", "unlock door", "open door", "n", "d",
                     "take music box", "u", "s");
        Assert.True(r.Errors.Count == 0, r.Describe());
        var t = Text(r);
        Assert.Contains("*** YOU HAVE WON ***", t);
        Assert.Contains("You scored 65 of 65 points", t);
        Assert.True(r.Completed, "the program ends after the win");
    }

    [AvaloniaFact]
    public void Dark_rooms_need_the_lamp_and_doors_need_the_key()
    {
        var r = Play("u", "take letter", "d", "n", "open door", "unlock door", "dance", "quit");
        Assert.True(r.Errors.Count == 0, r.Describe());
        var t = Text(r);
        Assert.Contains("pitch dark", t);
        Assert.Contains("too dark to find anything", t);
        Assert.Contains("The heavy door is locked.", t);
        Assert.Contains("It is locked.", t);
        Assert.Contains("You need a key for that.", t);
        Assert.Contains("I don't know how to dance.", t);
    }

    // With PICTURE on, the game window shows the room's picture, and nothing while it is too dark
    [AvaloniaFact]
    public void Pictures_follow_the_room_and_the_dark()
    {
        static RunResult Shot(params string[] commands) =>
            BasicRunner.Run(BasicRunner.ProgramOf(Repo.PathOf("Projects/adventure.amosproj")), 2, input: commands, capture: true);
        static bool Picture(RunResult r)
        {
            // Count how many sample points differ from the empty screen's colour in the top left corner
            var bg = r.Frame!.GetPixel(2, 2);
            int differ = 0;
            for (int y = 40; y < 440; y += 40)
            for (int x = 40; x < 600; x += 40)
                if (r.Frame.GetPixel(x, y) != bg) differ++;
            return differ > 20;
        }

        Assert.True(Picture(Shot("picture")), "the hall's picture is shown");
        Assert.False(Picture(Shot("picture", "w", "take lamp", "e", "u")), "the landing is dark");
        Assert.True(Picture(Shot("picture", "w", "take lamp", "e", "u", "turn on lamp")), "the lamp lights it up");
        Assert.False(Picture(Shot("w")), "pictures are off until PICTURE");
    }
}
