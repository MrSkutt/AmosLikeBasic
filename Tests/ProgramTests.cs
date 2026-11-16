using System.Text.RegularExpressions;
using Avalonia.Headless.XUnit;

namespace AmosLikeBasic.Tests;

// Every code example in the manual and every example project must run without errors
public class ProgramTests
{
    // ```basic blocks in the manual. Blocks marked "basic nocheck" are fragments and are skipped.
    public static IEnumerable<object[]> ManualExamples()
    {
        var block = new Regex(@"```basic\n(.*?)```", RegexOptions.Singleline);
        foreach (var file in Directory.GetFiles(Repo.PathOf("Documentation/Manual"), "*.md").OrderBy(f => f))
        {
            string text = File.ReadAllText(file).Replace("\r\n", "\n");
            int i = 0;
            foreach (Match m in block.Matches(text))
                yield return new object[] { $"{Path.GetFileNameWithoutExtension(file)} #{++i}", m.Groups[1].Value };
        }
    }

    [AvaloniaTheory]
    [MemberData(nameof(ManualExamples))]
    public void ManualExampleRuns(string name, string program)
    {
        var result = BasicRunner.Run(program, maxSeconds: 1.0);
        Assert.True(result.Errors.Count == 0, $"{name}: {result.Describe()}");
    }

    public static IEnumerable<object[]> Projects() =>
        Directory.GetFiles(Repo.PathOf("Projects"), "*.amosproj")
            .Select(Path.GetFileNameWithoutExtension)
            .OrderBy(n => n)
            .Select(n => new object[] { n! });

    [AvaloniaTheory]
    [MemberData(nameof(Projects))]
    public void ExampleProjectRuns(string name)
    {
        string program = BasicRunner.ProgramOf(Repo.PathOf($"Projects/{name}.amosproj"));
        var result = BasicRunner.Run(program, maxSeconds: 2.0);
        Assert.True(result.Errors.Count == 0, $"{name}: {result.Describe()}");
    }

    // A few examples whose output the manual states
    [AvaloniaFact]
    public void ComparisonsGiveOneOrZero()
    {
        var r = BasicRunner.Run("PRINT 10 > 3\nPRINT 10 < 3\nPRINT \"CAT\" < \"DOG\"\nPRINT -2 ^ 2\nPRINT 2 ^ 3 ^ 2", 2);
        Assert.Equal(new[] { "1", "0", "1", "-4", "512" }, r.Prints);
    }

    [AvaloniaFact]
    public void StringFunctionsMatchTheManual()
    {
        var r = BasicRunner.Run(
            "PRINT INSTR(\"Pinecone lives in the forest\", \"lives\")\n" +
            "PRINT FIND(\"PINECONE\", \"E\") & \" \" & FINDLAST(\"PINECONE\", \"E\")\n" +
            "PRINT MID$(\"PINECONE\", 2, 3)\n" +
            "PRINT WORD$(\"one two three\", 2)", 2);
        Assert.Equal(new[] { "10", "3 7", "INE", "two" }, r.Prints);
    }

    [AvaloniaFact]
    public void ForLoopRunsAtLeastOnce()
    {
        var r = BasicRunner.Run("N = 0\nFOR K = 0 TO N - 1\n  PRINT \"LAP \" & K\nNEXT K", 2);
        Assert.Equal(new[] { "LAP 0" }, r.Prints);
    }

    [AvaloniaFact]
    public void SwapWorksForVariablesAndArrays()
    {
        var r = BasicRunner.Run("A = 1 : B = 2\nSWAP A, B\nPRINT A & \" \" & B\nDIM T(3) = [10, 20, 30]\nSWAP T(0), T(2)\nPRINT JOIN$(T, \",\")", 2);
        Assert.Equal(new[] { "2 1", "30,20,10" }, r.Prints);
    }

    [AvaloniaFact]
    public void RuntimeErrorsAreReported()
    {
        var r = BasicRunner.Run("DIM A(3)\nA(5) = 1", 2);
        Assert.NotEmpty(r.Errors);
    }
}
