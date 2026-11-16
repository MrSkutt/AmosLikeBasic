using Avalonia.Headless.XUnit;

namespace AmosLikeBasic.Tests;

// LOCAL in procedures, and that parameters and locals are put back at END PROC and EXIT PROC
public class LocalTests
{
    private static List<string> Prints(string program)
    {
        var r = BasicRunner.Run(program, 2);
        Assert.True(r.Errors.Count == 0, r.Describe());
        return r.Prints;
    }

    [AvaloniaFact]
    public void A_local_loop_counter_leaves_the_callers_alone()
    {
        var p = Prints("""
            FOR I = 1 TO 3
              Stars I
            NEXT I
            END
            PROC Stars(N)
              LOCAL I, ROW$
              FOR I = 1 TO N
                ROW$ = ROW$ & "*"
              NEXT I
              PRINT ROW$
            END PROC
            """);
        Assert.Equal(new[] { "*", "**", "***" }, p);
    }

    [AvaloniaFact]
    public void Locals_start_at_zero_and_empty_text_and_come_back_afterwards()
    {
        var p = Prints("""
            X = 5 : NAME$ = "Pinecone"
            Test
            PRINT X & " " & NAME$
            END
            PROC Test
              LOCAL X, NAME$
              PRINT X & "[" & NAME$ & "]"
              X = 99 : NAME$ = "Beetle"
            END PROC
            """);
        Assert.Equal(new[] { "0[]", "5 Pinecone" }, p);
    }

    [AvaloniaFact]
    public void A_local_that_did_not_exist_before_is_gone_afterwards()
    {
        // Afterwards TEMP reads like a variable that was never set
        var p = Prints("""
            Test
            PRINT TEMP
            PRINT NEVERSET
            END
            PROC Test
              LOCAL TEMP
              TEMP = 7
            END PROC
            """);
        Assert.Equal(2, p.Count);
        Assert.NotEqual("7", p[0]);
        Assert.Equal(p[1], p[0]);
    }

    [AvaloniaFact]
    public void Exit_proc_also_gives_the_values_back()
    {
        var p = Prints("""
            A = 1
            Test
            PRINT A
            END
            PROC Test
              LOCAL A
              A = 2
              EXIT PROC
              A = 3
            END PROC
            """);
        Assert.Equal(new[] { "1" }, p);
    }

    [AvaloniaFact]
    public void Recursion_keeps_a_local_per_call()
    {
        var p = Prints("""
            Count 3
            END
            PROC Count(N)
              LOCAL HERE
              HERE = N * 10
              IF N > 1 THEN Count N - 1
              PRINT HERE
            END PROC
            """);
        Assert.Equal(new[] { "10", "20", "30" }, p);
    }

    [AvaloniaFact]
    public void Shared_variables_still_work_next_to_locals()
    {
        var p = Prints("""
            SCORE = 0
            Add 5
            Add 7
            PRINT SCORE
            END
            PROC Add(P)
              LOCAL I
              FOR I = 1 TO P
                SCORE = SCORE + 1
              NEXT I
            END PROC
            """);
        Assert.Equal(new[] { "12" }, p);
    }

    [AvaloniaFact]
    public void Local_outside_a_procedure_is_an_error()
    {
        var r = BasicRunner.Run("LOCAL X\nPRINT \"after\"", 2);
        Assert.Contains(r.Errors, e => e.Contains("LOCAL outside PROC"));
    }
}
