using Avalonia.Headless.XUnit;

namespace AmosLikeBasic.Tests;

// Changing variables from the variable watch while the program is paused
public class DebugVariablesTests
{
    [AvaloniaFact]
    public void A_changed_variable_is_used_by_the_program()
    {
        // At line 2 (as if paused there) A becomes 41, N$ "WORLD", and an element of the array 7
        var r = BasicRunner.Run("""
            A = 1 : N$ = "HI" : DIM B(3)
            PRINT A + 1 & " " & N$ & " " & B(2)
            """, 5, onStep: (pc, debug) =>
            {
                if (pc != 1) return;
                Assert.Null(debug.Set("A", "41"));
                Assert.Null(debug.Set("N$", "\"WORLD\""));
                Assert.Null(DebugVariables.SetElement((IAmosArray)debug.Get("B")!, new[] { 2 }, "7"));
            });
        Assert.Empty(r.Errors);
        Assert.Equal(new[] { "42 WORLD 7" }, r.Prints);
    }

    [AvaloniaFact]
    public void Constants_and_whole_numbers_follow_the_rules()
    {
        string? constError = null;
        var r = BasicRunner.Run("""
            CONST MAX = 10
            C% = 1
            PRINT MAX & " " & C%
            """, 5, onStep: (pc, debug) =>
            {
                if (pc != 2) return;
                constError = debug.Set("MAX", "99");
                Assert.Null(debug.Set("C%", "7.9"));
            });
        Assert.NotNull(constError);
        Assert.Equal(new[] { "10 7" }, r.Prints);
    }

    [Theory]
    [InlineData("12", 12.0)]
    [InlineData("-3.5", -3.5)]
    [InlineData("$FF", 255.0)]
    [InlineData("&H10", 16.0)]
    [InlineData("%1010", 10.0)]
    public void Numbers_are_read_as_in_BASIC(string text, double expected) =>
        Assert.Equal(expected, DebugVariables.Parse(text, isString: false).value);

    [Fact]
    public void Not_a_number_gives_an_error() =>
        Assert.NotNull(DebugVariables.Parse("ABC", isString: false).error);
}
