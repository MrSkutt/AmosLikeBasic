using Avalonia.Headless.XUnit;

namespace AmosLikeBasic.Tests;

// The language follows the usual BASIC rules. Each case is a small program and what it must print.
public class LanguageTests
{
    public static IEnumerable<object[]> Programs() => new[]
    {
        // IF with ELSE IF / ELSEIF, block and inline
        Case("ELSE IF", "X=2\nIF X=1 THEN\n PRINT \"one\"\nELSE IF X=2 THEN\n PRINT \"two\"\nELSE\n PRINT \"other\"\nEND IF", "two"),
        Case("ELSEIF", "X=3\nIF X=1 THEN\n PRINT \"one\"\nELSEIF X=2 THEN\n PRINT \"two\"\nELSEIF X=3 THEN\n PRINT \"three\"\nELSE\n PRINT \"other\"\nENDIF", "three"),
        Case("ELSEIF falls to ELSE", "X=9\nIF X=1 THEN\n PRINT \"one\"\nELSEIF X=2 THEN\n PRINT \"two\"\nELSE\n PRINT \"other\"\nEND IF", "other"),
        Case("inline ELSE IF", "X=2\nIF X=1 THEN PRINT \"one\" ELSE IF X=2 THEN PRINT \"two\" ELSE PRINT \"other\"", "two"),
        Case("nested ELSEIF", "A=1 : B=2\nIF A=1 THEN\n IF B=1 THEN\n  PRINT \"b1\"\n ELSEIF B=2 THEN\n  PRINT \"b2\"\n END IF\nELSEIF A=2 THEN\n PRINT \"a2\"\nEND IF", "b2"),

        // FOR
        Case("FOR skips an empty range", "FOR I=1 TO 0\n PRINT \"lap\"\nNEXT I\nPRINT \"after\"", "after"),
        Case("FOR ends one past the end", "FOR I=1 TO 3\nNEXT I\nPRINT I", "4"),
        Case("FOR with a decimal step", "FOR I=0 TO 1 STEP 0.5\n PRINT I\nNEXT I", "0", "0.5", "1"),
        Case("FOR counting down", "FOR I=3 TO 1 STEP -1\n PRINT I\nNEXT I", "3", "2", "1"),
        Case("EXIT FOR", "FOR I=1 TO 10\n IF I=3 THEN EXIT FOR\nNEXT I\nPRINT I", "3"),
        Case("EXIT FOR in nested loops", "FOR A=1 TO 2\n FOR B=1 TO 5\n  IF B=2 THEN EXIT FOR\n NEXT B\n PRINT A & B\nNEXT A", "12", "22"),
        Case("FOR in a FUNCTION skips an empty range", "PRINT S(0)\nEND\nFUNCTION S(N)\n T=0\n FOR I=1 TO N\n  T=T+I\n NEXT I\n RETURN T\nEND FUNCTION", "0"),

        // DO and LOOP
        Case("DO WHILE", "I=0\nDO WHILE I<3\n I=I+1\nLOOP\nPRINT I", "3"),
        Case("DO UNTIL", "I=0\nDO UNTIL I=3\n I=I+1\nLOOP\nPRINT I", "3"),
        Case("LOOP UNTIL", "I=0\nDO\n I=I+1\nLOOP UNTIL I=3\nPRINT I", "3"),
        Case("LOOP WHILE runs once", "I=10\nDO\n I=I+1\nLOOP WHILE I<3\nPRINT I", "11"),
        Case("DO WHILE that is false", "DO WHILE 0\n PRINT \"never\"\nLOOP\nPRINT \"done\"", "done"),
        Case("DONE is a variable, not DO", "DONE=0\nI=0\nWHILE DONE=0\n I=I+1\n IF I=2 THEN DONE=1\nWEND\nPRINT I", "2"),

        // SELECT CASE
        Case("CASE TO", "X=5\nSELECT CASE X\nCASE 1 TO 4\n PRINT \"low\"\nCASE 5 TO 9\n PRINT \"mid\"\nCASE ELSE\n PRINT \"else\"\nEND SELECT", "mid"),
        Case("CASE list", "X=3\nSELECT CASE X\nCASE 1, 3\n PRINT \"odd\"\nCASE ELSE\n PRINT \"else\"\nEND SELECT", "odd"),
        Case("CASE IS", "X=50\nSELECT CASE X\nCASE IS < 10\n PRINT \"small\"\nCASE IS >= 10\n PRINT \"big\"\nEND SELECT", "big"),
        Case("CASE ELSE", "X=7\nSELECT CASE X\nCASE 1\n PRINT \"one\"\nCASE ELSE\n PRINT \"else\"\nEND SELECT", "else"),
        Case("CASE with text", "A$=\"B\"\nSELECT CASE A$\nCASE \"A\", \"B\"\n PRINT \"ab\"\nEND SELECT", "ab"),

        // ON GOTO / ON GOSUB
        Case("ON GOSUB", "N=2\nON N GOSUB A1,A2\nPRINT \"back\"\nEND\nA1:\nPRINT \"a1\"\nRETURN\nA2:\nPRINT \"a2\"\nRETURN", "a2", "back"),
        Case("ON GOTO", "N=1\nON N GOTO A1,A2\nEND\nA1:\nPRINT \"a1\"\nEND\nA2:\nPRINT \"a2\"", "a1"),
        Case("ON outside the list", "N=5\nON N GOTO A1\nPRINT \"on\"\nEND\nA1:\nPRINT \"a1\"", "on"),

        // PRINT with ; and ,
        Case("PRINT ;", "PRINT \"A\";\nPRINT \"B\"", "AB"),
        Case("PRINT ; between values", "PRINT \"X=\"; 5; \"!\"", "X=5!"),
        Case("PRINT ,", "PRINT 1, 2", "1             2"),
        Case("PRINT with a comma in a function", "PRINT MID$(\"HELLO\", 2, 3)", "ELL"),

        // Operators and values
        Case("integer division", "PRINT 7 \\ 2", "3"),
        Case("XOR", "PRINT (1 XOR 0) & (1 XOR 1) & (0 XOR 0)", "100"),
        Case("PRINT ; after THEN", "IF 1 THEN PRINT \"A\"; \"B\"", "AB"),
        Case("PRINT ; and a comment", "PRINT \"A\";\nPRINT \"B\"   ; prints B", "AB"),
        Case("XOR in IF", "A=1 : B=0\nIF A=1 XOR B=1 THEN PRINT \"one\"", "one"),
        Case("hex with $", "PRINT $FF", "255"),
        Case("hex with &H", "PRINT &H10 + 1", "17"),
        Case("binary with %", "PRINT %1010", "10"),
        Case("whole-number variable", "A%=7/2\nPRINT A%", "3"),
        Case("CONST", "CONST MAXV=5\nPRINT MAXV * 2", "10"),
        Case("STRING$", "PRINT STRING$(3, \"*\") & STRING$(2, 65)", "***AA"),
        Case("SPACE$", "PRINT \"[\" & SPACE$(2) & \"]\"", "[  ]"),

        // DIM gives the highest index
        Case("DIM A(10) has A(10)", "DIM A(10)\nA(10)=5\nPRINT A(10) & \" \" & DIM(A)", "5 11"),
        Case("DIM with starting values", "DIM T(2) = [10, 20, 30]\nPRINT JOIN$(T, \",\")", "10,20,30"),
    };

    private static object[] Case(string name, string program, params string[] prints) => new object[] { name, program, prints };

    [AvaloniaTheory]
    [MemberData(nameof(Programs))]
    public void Prints(string name, string program, string[] expected)
    {
        var r = BasicRunner.Run(program, 2);
        Assert.True(r.Errors.Count == 0, $"{name}: {r.Describe()}");
        Assert.Equal(expected, r.Prints);
    }

    public static IEnumerable<object[]> Mistakes() => new[]
    {
        Err("a misspelt sub-command", "SPRITE MOOVE 1, 10, 10", "SPRITE does not know MOOVE"),
        Err("an unknown EXIT", "EXIT LOOPS", "EXIT LOOPS?"),
        Err("changing a constant", "CONST A=1\nA=2", "constant"),
        Err("STEP 0", "FOR I=1 TO 2 STEP 0\nNEXT I", "STEP 0"),
        Err("an index past the end", "DIM A(10)\nA(11)=1", "out of range"),
        Err("an unknown statement", "CONTINUE", "CONTINUE"),
    };

    private static object[] Err(string name, string program, string message) => new object[] { name, program, message };

    [AvaloniaTheory]
    [MemberData(nameof(Mistakes))]
    public void Gives_an_error_for(string name, string program, string message)
    {
        var r = BasicRunner.Run(program, 2);
        Assert.True(r.Errors.Any(e => e.Contains(message, StringComparison.OrdinalIgnoreCase)), $"{name}: {r.Describe()}");
    }

    [AvaloniaFact]
    public void A_variable_as_the_first_value_is_still_fine()
    {
        var r = BasicRunner.Run("SCREEN 640,480\nP=1\nSPRITE P, 10, 10\nPRINT \"ok\"", 2);
        Assert.True(r.Errors.Count == 0, r.Describe());
    }
}
