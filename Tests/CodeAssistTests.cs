namespace AmosLikeBasic.Tests;

// What the code editor knows about the language, read from the manual
public class CodeAssistTests
{
    [Fact]
    public void A_command_has_its_syntax_and_description()
    {
        var e = BasicHelp.Find("BAR")!;
        Assert.StartsWith("BAR [#layer,] x1, y1, x2, y2", e.Syntax);
        Assert.Contains("filled rectangle", e.Text);
        Assert.False(e.IsFunction);
    }

    [Fact]
    public void Two_word_commands_are_found_by_both_words()
    {
        Assert.Equal("MAP OBJECTS", BasicHelp.Find("MAP", "OBJECTS")!.Name);
        Assert.Equal("MAP LOAD", BasicHelp.Find("MAP", "LOAD")!.Name);
        // A word of a heading like "DO ... LOOP" finds that section too
        Assert.Equal("DO ... LOOP", BasicHelp.Find("LOOP")!.Name);
    }

    [Fact]
    public void Functions_come_from_the_functions_table()
    {
        var e = BasicHelp.Find("PETSCII$")!;
        Assert.True(e.IsFunction);
        Assert.Equal("PETSCII$(code)", e.Syntax);
        Assert.Contains("Example:", e.Text);
    }

    [Fact]
    public void The_words_for_colours_and_completion()
    {
        Assert.Contains("PRINT", BasicHelp.Keywords);
        Assert.Contains("ENDIF", BasicHelp.Keywords);
        Assert.Contains("SIN", BasicHelp.Functions);
        Assert.Contains("POS", BasicHelp.SubCommandsOf("SPRITE"));
        Assert.Empty(BasicHelp.SubCommandsOf("PRINT"));
    }

    [Fact]
    public void Markdown_becomes_plain_text()
    {
        Assert.Equal("Draws x in bold", BasicHelp.Plain("Draws `x` in **bold**"));
        Assert.Equal("a  –  b", BasicHelp.Plain("| a | b |\n|---|---|"));
        Assert.Equal("see the manual", BasicHelp.Plain("see [the manual](x.md)"));
    }

    [Fact]
    public void Comments_start_as_the_interpreter_reads_them()
    {
        Assert.True(BasicColorizer.StartsComment("; a comment", 0));
        Assert.True(BasicColorizer.StartsComment("PRINT A ; note", 8));
        Assert.False(BasicColorizer.StartsComment("PRINT A;B", 7));   // joins the values
    }
}
