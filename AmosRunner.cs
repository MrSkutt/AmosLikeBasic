using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using System.Linq;

namespace AmosLikeBasic;

public static class AmosRunner
{
    
    public static MainWindow _mainWindow;
    
    private static Color ParseColorFlexible(string s)
    {
        s = (s ?? "").Trim();

        // "r,g,b" eller "r,g,b,a"
        var parts = s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 3 || parts.Length == 4)
        {
            if (byte.TryParse(parts[0], out var r) &&
                byte.TryParse(parts[1], out var g) &&
                byte.TryParse(parts[2], out var b))
            {
                byte a = 255;
                if (parts.Length == 4 && byte.TryParse(parts[3], out var aa))
                    a = aa;

                return Color.FromArgb(a, r, g, b);
            }
        }

        // "Blue", "#RRGGBB", "#AARRGGBB", osv
        return Color.Parse(s);
    }
    
    private static Color PaperValueToColor(object v)
    {
        if (v is string s)
            return ParseColorFlexible(s);

        var n = (int)Math.Round(Convert.ToDouble(v, CultureInfo.InvariantCulture));
        return n switch
        {
            0 => Colors.Black,
            1 => Colors.White,
            2 => Colors.Red,
            3 => Colors.Green,
            4 => Colors.Blue,
            5 => Colors.Yellow,
            6 => Colors.Magenta,
            7 => Colors.Cyan,
            _ => Colors.Black
        };
    }
    
    private static string UnescapeBasicString(string s)
    {
        if (string.IsNullOrEmpty(s) || !s.Contains('\\'))
            return s;

        var sb = new System.Text.StringBuilder(s.Length);

        for (int i = 0; i < s.Length; i++)
        {
            char ch = s[i];
            if (ch == '\\' && i + 1 < s.Length)
            {
                char n = s[i + 1];
                switch (n)
                {
                    case 'r': sb.Append('\r'); i++; continue;
                    case 'n': sb.Append('\n'); i++; continue;
                    case 't': sb.Append('\t'); i++; continue;
                    case '\\': sb.Append('\\'); i++; continue;
                    case '"': sb.Append('\"'); i++; continue;
                }
            }

            sb.Append(ch);
        }

        return sb.ToString();
    }
    
    private static async Task EmitPrintAsync(Func<string, Task> appendLineAsync, string text)
    {
        // Normalisera newlines och skriv som flera @@PRINT-rader
        text = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
        var parts = text.Split('\n');

        foreach (var part in parts)
            await appendLineAsync("@@PRINT " + part);
    }
    
    private sealed class ForFrame
    {
        public required string VarName; 
        public required int EndValue; 
        public required int StepValue; 
        public required int LineAfterForPc; 
        public required int ForLineNumber;
    }
    private sealed class WhileFrame
    {
        public required int WhilePc;     // Program counter för WHILE-raden
        public int WendPc;                // Program counter för WEND (fylls senare)
        public required int Line;         // Källrad (för felmeddelanden)
        public required string Condition; // WHILE-villkoret
    }

    private sealed class RepeatFrame
    {
        public int RepeatPc;
        public int UntilPc;   // <-- ny
        public int RepeatLine;
    }

    private sealed class SelectInfo
    {
        public int SelectPc;
        public int EndSelectPc;
        public int? DefaultPc;
        public List<(string CaseExpr, int CasePc)> Cases { get; } = new();
    }
    
    private sealed class SelectRuntimeFrame
    {
        public required int EndSelectPc;
    }
    
    private static readonly Random _rng = new();
    private static IntPtr _currentXmpContext = IntPtr.Zero;

    private static System.Diagnostics.Process? _currentMusicProcess; // Musik-kanalen

    private sealed class AmosArray
    {
        public double[] Data { get; }
        public AmosArray(int size) { Data = new double[size + 1]; } // +1 för att AMOS ofta tillåter index upp till storleken
        public override string ToString()
        {
            var content = string.Join(", ", Data.Take(5).Select(d => d.ToString("G5")));
            if (Data.Length > 5) content += "...";
            return $"Array({Data.Length - 1}) [{content}]";
        }
    }
    
    private interface IAmosArray
    {
        int Length { get; }
        object Get(int index);
        void Set(int index, object value);
    }

    private sealed class AmosNumericArray : IAmosArray
    {
        public double[] Data { get; }
        public AmosNumericArray(int size) { Data = new double[size + 1]; }
        public int Length => Data.Length;

        public object Get(int index) => Data[index];

        public void Set(int index, object value)
        {
            Data[index] = Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }

        public override string ToString()
        {
            var content = string.Join(", ", Data.Take(5).Select(d => d.ToString("G5", CultureInfo.InvariantCulture)));
            if (Data.Length > 5) content += "...";
            return $"Array({Data.Length - 1}) [{content}]";
        }
    }

    private sealed class AmosStringArray : IAmosArray
    {
        public string[] Data { get; }
        public AmosStringArray(int size) { Data = new string[size + 1]; }
        public int Length => Data.Length;

        public object Get(int index) => Data[index] ?? "";

        public void Set(int index, object value)
        {
            Data[index] = ValueToString(value);
        }

        public override string ToString()
        {
            var content = string.Join(", ", Data.Take(5).Select(s => s ?? ""));
            if (Data.Length > 5) content += "...";
            return $"Array$({Data.Length - 1}) [{content}]";
        }
    }
    
    public static async Task ExecuteAsync(
        string programText, 
        Func<string, Task> appendLineAsync, 
        Func<Task> clearAsync, 
        AmosGraphics graphics, 
        Action onGraphicsChanged, 
        Func<string> getInkey, 
        Func<string, bool> isKeyDown, 
        AudioEngine? audioEngine, 
        CancellationToken token,
        Action<Dictionary<string, object>> onVariablesChanged,
        Func<int, Task> waitForStep, 
        Func<Task<string>> getConsoleInputAsync) 
    {
        var vars = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        var lastVarUpdateTime = DateTime.MinValue;
        var updateInterval = TimeSpan.FromMilliseconds(500);
        // En lokal hjälpfunktion för att uppdatera variabler och trigga UI
        var setVar = (string name, object value) => {
            vars[name] = value;
                
            var now = DateTime.Now;
            if (now - lastVarUpdateTime > updateInterval)
            {
                onVariablesChanged(new Dictionary<string, object>(vars)); // Skicka en kopia
                lastVarUpdateTime = now;
            }
        };
        var forStack = new Stack<ForFrame>();
        var whileStack = new Stack<WhileFrame>();
        var repeatStack = new Stack<RepeatFrame>();
        var selectRuntimeStack = new Stack<SelectRuntimeFrame>();

        var gosubStack = new Stack<int>();
        var lines = programText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        
        var labels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var ifJumps = new Dictionary<int, int>(); // PC -> PC (Vart IF hoppar om falskt)
        var elseJumps = new Dictionary<int, int>(); // PC -> PC (Vart ELSE hoppar för att skippa till ENDIF)
            
        var whileMap = new Dictionary<int, int>(); // WHILE pc -> WEND pc
        var wendMap  = new Dictionary<int, int>(); // WEND pc  -> WHILE pc
        var whileScanStack = new Stack<int>();
        var ifStack = new Stack<int>();
        
        var selectMap = new Dictionary<int, SelectInfo>();
        var selectScanStack = new Stack<int>();
        var selectOwnerMarker = new Dictionary<int, int>(); // markerPc (CASE/DEFAULT/ENDSELECT) -> selectPc

        static bool IsEndSelectLine(string upperScan)
            => upperScan == "ENDSELECT" || upperScan == "END SELECT";
        
        static bool SelectValueEquals(object a, object b)
        {
            // AMOS-likt: sträng jämförs exakt, tal jämförs med liten tolerans.
            if (a is string || b is string)
                return ValueToString(a) == ValueToString(b);

            var da = Convert.ToDouble(a, CultureInfo.InvariantCulture);
            var db = Convert.ToDouble(b, CultureInfo.InvariantCulture);
            return Math.Abs(da - db) < 0.000001;
        }

        void PopSelectUntilEndPc(int endPc)
        {
            while (selectRuntimeStack.Count > 0 && selectRuntimeStack.Peek().EndSelectPc != endPc)
                selectRuntimeStack.Pop();

            if (selectRuntimeStack.Count > 0 && selectRuntimeStack.Peek().EndSelectPc == endPc)
                selectRuntimeStack.Pop();
        }       
        
         // --- DATA/READ/RESTORE (AMOS-like) ------------------------------------
        // DATA definieras efter en label:
        //   Room:
        //   DATA "Hall",2,0
        //   DATA "Kök",1,0
        //
        // RESTORE Room  -> ställ läspekare till början av Room
        // READ A$, B    -> läs nästa värden från aktuell label
        var dataAreas = new Dictionary<string, List<object>>(StringComparer.OrdinalIgnoreCase);

        static List<string> SplitTopLevelCsv(string s)
        {
            var res = new List<string>();
            if (string.IsNullOrWhiteSpace(s)) return res;

            bool inQuotes = false;
            int parenDepth = 0;
            int start = 0;

            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];

                if (ch == '"')
                {
                    inQuotes = !inQuotes;
                    continue;
                }

                if (!inQuotes)
                {
                    if (ch == '(') { parenDepth++; continue; }
                    if (ch == ')') { if (parenDepth > 0) parenDepth--; continue; }

                    if (parenDepth == 0 && ch == ',')
                    {
                        res.Add(s[start..i].Trim());
                        start = i + 1;
                    }
                }
            }

            res.Add(s[start..].Trim());
            return res;
        }

// ... existing code ...
        static List<object> ParseDataValues(string dataArg)
        {
            // DATA "Hall",2,0,0
            var parts = SplitTopLevelCsv(dataArg);
            var vals = new List<object>(parts.Count);

            foreach (var p0 in parts)
            {
                var p = p0.Trim();
                if (p.Length == 0) continue;

                // String literal
                if (p.Length >= 2 && p.StartsWith("\"", StringComparison.Ordinal) && p.EndsWith("\"", StringComparison.Ordinal))
                {
                    var raw = p[1..^1];
                    vals.Add(UnescapeBasicString(raw));
                    continue;
                }

                // Number literal (AMOS-style uses dot)
                if (double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                {
                    vals.Add(d);
                    continue;
                }

                // Fallback: treat as raw text (keeps it forgiving)
                vals.Add(p);
            }

            return vals;
        }

        static string ToBasicLiteral(object v)
        {
            return v switch
            {
                string s => "\"" + s.Replace("\"", "\"\"") + "\"",
                double d => d.ToString("G10", CultureInfo.InvariantCulture),
                _ => "\"" + (v?.ToString() ?? "").Replace("\"", "\"\"") + "\""
            };
        }

        void AssignValueToTarget(string target, object value, int ln)
        {
            // Vi återanvänder din existerande tilldelningslogik (inkl. arrayer)
            var leftSide = target.Trim();
            if (string.IsNullOrWhiteSpace(leftSide))
                throw new Exception($"Syntax Error in READ at line {ln}: empty target");

            if (leftSide.Contains('('))
            {
                int openParen = leftSide.IndexOf('(');
                int closeParen = leftSide.LastIndexOf(')');
                if (openParen != -1 && closeParen != -1)
                {
                    var arrayName = leftSide[..openParen].Trim();
                    var idxStr = leftSide[(openParen + 1)..closeParen];

                    if (vars.TryGetValue(arrayName, out var aVal) && aVal is IAmosArray array)
                    {
                        var rawIdx = EvalValue(idxStr, vars, ln, getInkey, isKeyDown, graphics);
                        int aIdx = (int)Math.Round(Convert.ToDouble(rawIdx, CultureInfo.InvariantCulture));

                        if (aIdx >= 0 && aIdx < array.Length)
                        {
                            array.Set(aIdx, value);
                            return;
                        }

                        throw new Exception($"Array index out of range in READ at line {ln}: {leftSide}");
                    }

                    throw new Exception($"Unknown array in READ at line {ln}: {arrayName}");
                }
            }

            setVar(leftSide, value);
        }

        // Bygg DATA-areas: label -> lista av värden (i ordning)
        string? currentDataLabel = null;
        for (int i = 0; i < lines.Length; i++)
        {
            var rawLine = (lines[i] ?? "").Trim();
            if (string.IsNullOrWhiteSpace(rawLine)) continue;

            // ta bort kommentarer + ev. radnummer
            var scan = StripLeadingLineNumber(StripComments(rawLine)).Trim();
            if (string.IsNullOrWhiteSpace(scan)) continue;

            // Label (t.ex. Room:)
            if (scan.EndsWith(':'))
            {
                currentDataLabel = scan.TrimEnd(':').Trim();
                if (!string.IsNullOrWhiteSpace(currentDataLabel) && !dataAreas.ContainsKey(currentDataLabel))
                    dataAreas[currentDataLabel] = new List<object>();
                continue;
            }

            // DATA ... (endast giltigt om vi är "inne" i en label)
            var upper = scan.ToUpperInvariant();
            if (upper.StartsWith("DATA "))
            {
                if (string.IsNullOrWhiteSpace(currentDataLabel))
                    continue; // enligt din önskan: DATA ska vara efter label, så vi ignorerar "orphan DATA"

                var dataArg = scan[5..].Trim();
                var values = ParseDataValues(dataArg);
                dataAreas[currentDataLabel].AddRange(values);
            }
        }

        string? currentReadLabel = null;
        int currentReadIndex = 0;

        object NextDataValue(int ln)
        {
            if (string.IsNullOrWhiteSpace(currentReadLabel))
                throw new Exception($"READ without RESTORE at line {ln}");

            if (!dataAreas.TryGetValue(currentReadLabel, out var list))
                throw new Exception($"Unknown DATA label '{currentReadLabel}' at line {ln}");

            if (currentReadIndex >= list.Count)
                throw new Exception($"Out of DATA in '{currentReadLabel}' at line {ln}");

            return list[currentReadIndex++];
        }
        // ---------------------------------------------------------------------       
        
        
            // Pre-scan: Labels, WHILE/WEND OCH IF/ELSE/ENDIF logik
            for (int i = 0; i < lines.Length; i++) {
                var rawLine = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(rawLine)) continue;
                
                // Labels
                var firstWord = rawLine.Split(' ')[0];
                if (int.TryParse(firstWord, out _)) labels[firstWord] = i;
                if (rawLine.EndsWith(':')) labels[rawLine.TrimEnd(':').Trim()] = i;
            
                // WHILE/WEND scan
                var scanLine = StripLeadingLineNumber(StripComments(rawLine)).Trim();
                var upperScan = scanLine.ToUpperInvariant();

                if (upperScan.StartsWith("WHILE "))
                {
                    whileScanStack.Push(i);
                }
                else if (upperScan == "WEND")
                {
                    if (whileScanStack.Count == 0)
                        throw new Exception($"WEND without WHILE at line {i + 1}");

                    var whilePc = whileScanStack.Pop();
                    whileMap[whilePc] = i;
                    wendMap[i] = whilePc;
                }
            
                // SELECT/CASE
                if (upperScan.StartsWith("SELECT "))
                {
                    selectScanStack.Push(i);
                    selectMap[i] = new SelectInfo { SelectPc = i, EndSelectPc = -1 };
                }
                else if (upperScan.StartsWith("CASE "))
                {
                    if (selectScanStack.Count == 0)
                        throw new Exception($"CASE without SELECT at line {i + 1}");

                    var selPc = selectScanStack.Peek();
                    var expr = scanLine[4..].Trim(); // efter "CASE"
                    selectMap[selPc].Cases.Add((expr, i));
                    selectOwnerMarker[i] = selPc;
                }
                else if (upperScan == "DEFAULT")
                {
                    if (selectScanStack.Count == 0)
                        throw new Exception($"DEFAULT without SELECT at line {i + 1}");

                    var selPc = selectScanStack.Peek();
                    selectMap[selPc].DefaultPc = i;
                    selectOwnerMarker[i] = selPc;
                }
                else if (IsEndSelectLine(upperScan))
                {
                    if (selectScanStack.Count == 0)
                        throw new Exception($"ENDSELECT without SELECT at line {i + 1}");

                    var selPc = selectScanStack.Pop();
                    selectMap[selPc].EndSelectPc = i;
                    selectOwnerMarker[i] = selPc;
                }
                
                // IF/ELSE/ENDIF Mapping
                if (upperScan.StartsWith("IF "))
                {
                    int thenIdx = upperScan.IndexOf("THEN");
                    if (thenIdx >= 0)
                    {
                        var afterThen = scanLine[(thenIdx + 4)..].Trim();
                        if (!string.IsNullOrEmpty(afterThen)) continue; // Inline-IF, ignorera i stacken
                    }
                    ifStack.Push(i);
                }
                else if (upperScan == "ELSE")
                {
                    if (ifStack.Count == 0) throw new Exception($"ELSE without IF at line {i + 1}");
                    var ifPc = ifStack.Pop();
                    ifJumps[ifPc] = i + 1; 
                    ifStack.Push(i); 
                }
                else if (upperScan == "ENDIF")
                {
                    if (ifStack.Count == 0) throw new Exception($"ENDIF without IF at line {i + 1}");
                    var sourcePc = ifStack.Pop();
                    if (StripLeadingLineNumber(StripComments(lines[sourcePc])).Trim().ToUpperInvariant() == "ELSE")
                        elseJumps[sourcePc] = i + 1;
                    else
                        ifJumps[sourcePc] = i + 1;
                }
            }

            if (ifStack.Count > 0)
                throw new Exception("IF without ENDIF detected at end of program");

            int pc = 0;
        while (pc < lines.Length) {
            token.ThrowIfCancellationRequested();
            await waitForStep(pc);
            var ln = pc + 1; 
            var line = StripComments((lines[pc] ?? "").Trim());
            if (string.IsNullOrWhiteSpace(line) || line.EndsWith(':')) { pc++; continue; }
            line = StripLeadingLineNumber(line);

            var commands = SplitMultipleCommands(line);
            bool jumpHappened = false;

            foreach (var fullCmd in commands) {
                var trimmedCmd = fullCmd.Trim();
                if (string.IsNullOrEmpty(trimmedCmd)) continue;
                var (cmd, arg) = SplitCommand(trimmedCmd);

                switch (cmd) {
                         case "CLSG2": 
                            // Rensa både grafik och text-cursor
                            await clearAsync(); // Om du vill rensa loggen/text-boxen också, annars ta bort
                            graphics.Clear(graphics.PaperColor); // Använd paper color som bakgrund
                            graphics.Locate(0, 0); 
                            break;

                        case "PAPERG2":
                        {
                            // Sätter grafikens bakgrundsfärg för text
                            var color = Colors.Black;
                            if (!string.IsNullOrWhiteSpace(arg))
                            {
                                var v = EvalValue(arg, vars, ln, getInkey, isKeyDown, graphics);
                                color = PaperValueToColor(v);
                            }
                            graphics.PaperColor = color;
                            // Vi behöver inte anropa MainWindow längre för detta
                            break;
                        }

                        case "INKG2":
                        {
                            var c = ParseColor(arg);
                            graphics.Ink = c;
                            // Ingen @@INK behövs, graphics.Ink används av ConsolePrint
                            break;
                        }

                        case "LOCATEG2": 
                            var lp = SplitCsvOrSpaces(arg); 
                            graphics.Locate(
                                EvalInt(lp[0], vars, ln, getInkey, isKeyDown, graphics), 
                                EvalInt(lp[1], vars, ln, getInkey, isKeyDown, graphics)
                            );
                            break;

                        case "PRINTG2":
                        {
                            var printArg = arg.Trim();
                            
                            // Hantera PRINT AT x,y, "text"
                            if (printArg.StartsWith("AT ", StringComparison.OrdinalIgnoreCase))
                            {
                                var at = ParsePrintAtArguments(printArg);
                                int row = EvalInt(at.RowExpr, vars, ln, getInkey, isKeyDown, graphics);
                                int col = EvalInt(at.ColExpr, vars, ln, getInkey, isKeyDown, graphics);
                                graphics.Locate(row, col); // Notera: x=row? AMOS kör ofta Y,X i Locate men X,Y i text. Dubbelkolla ordningen.
                                // LOCATE X,Y brukar vara Kolumn, Rad.
                                
                                if (!string.IsNullOrWhiteSpace(at.RestExpr))
                                {
                                    var valToPrint = EvalValue(at.RestExpr, vars, ln, getInkey, isKeyDown, graphics);
                                    graphics.ConsolePrint(ValueToString(valToPrint));
                                    graphics.DoubleBuffer();
                                }
                            }
                            else
                            {
                                // Vanlig PRINT
                                // Kolla om det slutar med ; för att undvika nyrad
                                bool newLine = true;
                                if (printArg.EndsWith(";")) 
                                {
                                    newLine = false;
                                    printArg = printArg[..^1];
                                }

                                var valToPrint = EvalValue(printArg, vars, ln, getInkey, isKeyDown, graphics);
                                graphics.ConsolePrint(ValueToString(valToPrint), newLine);
                                graphics.DoubleBuffer();
                            }
                            
                            // Trigga uppdatering av fönstret
                            onGraphicsChanged();
                        }
                        break;                   
                    case "DATA":
                        // DATA exekveras inte (endast deklaration)
                        break;
                    case "RESTORE":
                    {
                        var labelName = (arg ?? "").Trim();
                        if (string.IsNullOrWhiteSpace(labelName))
                        {
                            // Om ingen label anges: välj första data-area om den finns
                            if (dataAreas.Count == 0)
                                throw new Exception($"RESTORE without label but no DATA exists at line {ln}");

                            currentReadLabel = dataAreas.Keys.First();
                            currentReadIndex = 0;
                        }
                        else
                        {
                            if (!dataAreas.ContainsKey(labelName))
                                throw new Exception($"Unknown DATA label '{labelName}' at line {ln}");

                            currentReadLabel = labelName;
                            currentReadIndex = 0;
                        }
                        break;
                    }
                    case "READ":
                    {
                        // READ A$, B, ARR(I)
                        var targets = SplitTopLevelCsv(arg);
                        if (targets.Count == 0)
                            throw new Exception($"Syntax Error in READ at line {ln}: missing targets");

                        foreach (var t in targets)
                        {
                            var val = NextDataValue(ln);

                            // Om target är en strängvariabel (slutar med $), konvertera val till string
                            var tt = (t ?? "").Trim();
                            if (tt.EndsWith("$", StringComparison.Ordinal))
                            {
                                AssignValueToTarget(tt, ValueToString(val), ln);
                            }
                            else
                            {
                                // Numeriskt target: om data råkar vara string försöker vi tolka som tal
                                if (val is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                                    AssignValueToTarget(tt, d, ln);
                                else
                                    AssignValueToTarget(tt, val, ln);
                            }
                        }
                        break;
                    }
                    case "SELECT":
                    {
                        if (!selectMap.TryGetValue(pc, out var sel))
                            throw new Exception($"SELECT without ENDSELECT at line {ln}");

                        var selectedValue = EvalValue(arg, vars, ln, getInkey, isKeyDown, graphics);

                        int? targetMarkerPc = null;

                        foreach (var (caseExpr, casePc) in sel.Cases)
                        {
                            var caseValue = EvalValue(caseExpr, vars, ln, getInkey, isKeyDown, graphics);
                            if (SelectValueEquals(selectedValue, caseValue))
                            {
                                targetMarkerPc = casePc;
                                break;
                            }
                        }

                        if (targetMarkerPc.HasValue)
                        {
                            // Hoppa in i matchande CASE-block (raden efter CASE)
                            selectRuntimeStack.Push(new SelectRuntimeFrame { EndSelectPc = sel.EndSelectPc });
                            pc = targetMarkerPc.Value + 1;
                            jumpHappened = true;
                        }
                        else if (sel.DefaultPc.HasValue)
                        {
                            // Hoppa in i DEFAULT-block (raden efter DEFAULT)
                            selectRuntimeStack.Push(new SelectRuntimeFrame { EndSelectPc = sel.EndSelectPc });
                            pc = sel.DefaultPc.Value + 1;
                            jumpHappened = true;
                        }
                        else
                        {
                            // Ingen match och ingen DEFAULT -> hoppa förbi hela SELECT
                            pc = sel.EndSelectPc + 1;
                            jumpHappened = true;
                        }
                        break;
                    }

                    case "CASE":
                    case "DEFAULT":
                    {
                        // Om vi når en CASE/DEFAULT under exekvering betyder det att vi är klara med valt block.
                        if (selectOwnerMarker.TryGetValue(pc, out var selPc) && selectMap.TryGetValue(selPc, out var sel))
                        {
                            PopSelectUntilEndPc(sel.EndSelectPc);
                            pc = sel.EndSelectPc + 1;
                            jumpHappened = true;
                        }
                        break;
                    }

                    case "ENDSELECT":
                    {
                        // Markör: om vi kommer hit naturligt så poppar vi ett SELECT (om vi är i ett)
                        if (selectRuntimeStack.Count > 0 && selectRuntimeStack.Peek().EndSelectPc == pc)
                            selectRuntimeStack.Pop();
                        break;
                    }

                    case "END":
                    {
                        // Viktigt: stöd "END SELECT" utan att avsluta programmet
                        if (arg.Equals("SELECT", StringComparison.OrdinalIgnoreCase))
                        {
                            if (selectRuntimeStack.Count > 0 && selectRuntimeStack.Peek().EndSelectPc == pc)
                                selectRuntimeStack.Pop();
                            break;
                        }

                        return;
                    }
                    case "REM": goto next_line;
                    case "GOTO":
                        if (labels.TryGetValue(arg, out var targetPc)) { pc = targetPc; jumpHappened = true; }
                        else throw new Exception($"Label {arg} not found at line {ln}");
                        break;
                    case "GOSUB":
                        gosubStack.Push(pc + 1);
                        if (labels.TryGetValue(arg, out var subPc)) { pc = subPc; jumpHappened = true; }
                        else throw new Exception($"Label {arg} not found at line {ln}");
                        break;
                    case "RETURN":
                        if (gosubStack.Count > 0) { pc = gosubStack.Pop(); jumpHappened = true; }
                        else throw new Exception($"RETURN without GOSUB at line {ln}");
                        break;
                    case "CLS": await appendLineAsync("@@CLS"); await clearAsync(); break;
                    case "LOCATE": 
                        var lp2 = SplitCsvOrSpaces(arg); 
                        await appendLineAsync($"@@LOCATE {EvalInt(lp2[0], vars, ln, getInkey, isKeyDown, graphics)} {EvalInt(lp2[1], vars, ln, getInkey, isKeyDown, graphics)}"); 
                        break;
                    case "PRINT":
                    {
                        var printArg = arg.Trim();

                        if (printArg.StartsWith("AT ", StringComparison.OrdinalIgnoreCase))
                        {
                            var at = ParsePrintAtArguments(printArg);

                            int row = EvalInt(at.RowExpr, vars, ln, getInkey, isKeyDown, graphics);
                            int col = EvalInt(at.ColExpr, vars, ln, getInkey, isKeyDown, graphics);

                            await appendLineAsync($"@@LOCATE {row} {col}");

                            if (!string.IsNullOrWhiteSpace(at.RestExpr))
                            {
                                var valToPrint = EvalValue(at.RestExpr, vars, ln, getInkey, isKeyDown, graphics);
                                await EmitPrintAsync(appendLineAsync, ValueToString(valToPrint));
                            }
                        }
                        else
                        {
                            var valToPrint = EvalValue(printArg, vars, ln, getInkey, isKeyDown, graphics);
                            await EmitPrintAsync(appendLineAsync, ValueToString(valToPrint));
                        }
                    }
                        break;
                    case "INPUT":
                        string inputArg = arg.Trim();
                        string promptInput = "";
                        string varNameInput = "A$";

                        // Hitta kommat som INTE är inuti citattecken
                        int commaIdx = -1;
                        bool inQuotes = false;
                        for (int i = 0; i < inputArg.Length; i++) {
                            if (inputArg[i] == '\"') inQuotes = !inQuotes;
                            if (!inQuotes && inputArg[i] == ',') {
                                commaIdx = i;
                                break;
                            }
                        }

                        if (commaIdx >= 0)
                        {
                            promptInput = Unquote(inputArg[..commaIdx].Trim());
                            varNameInput = inputArg[(commaIdx + 1)..].Trim();
                        }
                        else
                        {
                            varNameInput = inputArg;
                        }

                        // Skriv ut prompten
                        if (!string.IsNullOrEmpty(promptInput))
                        {
                            await appendLineAsync("@@PRINT " + promptInput);
                        }

                        // Vänta på användarens inmatning
                        string userInput = await getConsoleInputAsync();

                        // Spara resultatet
                        setVar(varNameInput, userInput.Trim());
                        break;
                    case "LET": 
                        var (n, vt) = SplitAssignment(arg); 
                        setVar(n, EvalValue(vt, vars, ln, getInkey, isKeyDown, graphics)); 
                        break;
                    case "DIM":
                        // DIM A(10) eller DIM A$(10)
                        var dimParts = arg.Split('(', ')');
                        if (dimParts.Length >= 2)
                        {
                            var arrName = dimParts[0].Trim();
                            var size = EvalInt(dimParts[1], vars, ln, getInkey, isKeyDown, graphics);

                            if (arrName.EndsWith("$", StringComparison.Ordinal))
                                vars[arrName] = new AmosStringArray(size);
                            else
                                vars[arrName] = new AmosNumericArray(size);
                        }
                        break;
                    case "WAIT": 
                        if (arg.ToUpperInvariant() == "VBL")
                        {
                            // Stega upp timern för alla lager innan swap
                            lock(graphics.LockObject) {
                                foreach(var layer in graphics.InactiveFrame) {
                                    layer.Timer += 0.016f; // Öka tiden (motsvarar ~60 FPS)
                                }
                            }
                            
                            graphics.EndFrame(); 
                            graphics.SwapBuffers();
                            onGraphicsChanged(); 
                            await WaitNextFrameAsync(token);
                            graphics.BeginFrame(); 
                        } else {
                            int ms = Math.Max(0, EvalInt(arg, vars, ln, getInkey, isKeyDown, graphics));
                            await Task.Delay(ms, token);
                        }
                        break;
                    case "DOUBLE":
                        if (arg.ToUpperInvariant() == "BUFFER") {
                            graphics.DoubleBuffer();
                        }
                        break;
                    case "IF":
                    {
                        int tIdx = IndexOfWord(arg, "THEN");
                        string condition;
                        string? inlineCmd = null;

                        if (tIdx >= 0)
                        {
                            condition = arg[..tIdx].Trim();
                            var thenContent = arg[(tIdx + 4)..].Trim();
                            if (!string.IsNullOrEmpty(thenContent)) inlineCmd = thenContent;
                        }
                        else condition = arg;

                        bool cond = EvalCondition(condition, vars, ln, getInkey, isKeyDown, graphics);

                        if (inlineCmd != null)
                        {
                            if (cond)
                            {
                                var cmds = SplitMultipleCommands(inlineCmd);
                                foreach (var c in cmds)
                                {
                                    var (cmd2, arg2) = SplitCommand(c);
                                    // Vi kollar om kommandot returnerar en ny PC (t.ex. vid EXIT)
                                    var (isJump, jumpPc) = await ExecuteInlineStatementAsync(cmd2, arg2, vars, appendLineAsync, clearAsync, graphics, onGraphicsChanged, getInkey, isKeyDown, audioEngine, token, ln, repeatStack, whileStack, lines);
                                    if (isJump)
                                    {
                                        pc = jumpPc;
                                        jumpHappened = true;
                                        break;
                                    }
                                }
                            }
                        }
                        else
                        {
                                if (!cond)
                                {
                                    if (!ifJumps.TryGetValue(pc, out var target))
                                        throw new Exception($"ENDIF not found for IF at line {ln}");
                                    pc = target;
                                    jumpHappened = true;
                                }
                            }
                            break;
                        }
                    case "ELSE":
                    {
                        if (!elseJumps.TryGetValue(pc, out var target))
                            throw new Exception($"ENDIF not found for ELSE at line {ln}");
                        pc = target;
                        jumpHappened = true;
                        break;
                    }
                    case "ENDIF":
                    {
                        // Bara markör, gå vidare
                        break;
                    }
                    case "FOR":
                        var eq = arg.IndexOf('='); 
                        if (eq < 0) throw new Exception($"Syntax Error in FOR: Missing '=' at line {ln}");
                        var fV = arg[..eq].Trim(); 
                        var rhs = arg[(eq + 1)..].Trim(); 
                        var toIdx = IndexOfWord(rhs, "TO");
                        if (toIdx < 0) throw new Exception($"Syntax Error in FOR: Missing 'TO' at line {ln}");
                        var start = EvalInt(rhs[..toIdx].Trim(), vars, ln, getInkey, isKeyDown, graphics); 
                        var rest = rhs[(toIdx + 2)..].Trim(); 
                        var stIdx = IndexOfWord(rest, "STEP");
                        var end = EvalInt(stIdx < 0 ? rest : rest[..stIdx].Trim(), vars, ln, getInkey, isKeyDown, graphics);
                        var step = stIdx < 0 ? 1 : EvalInt(rest[(stIdx + 4)..].Trim(), vars, ln, getInkey, isKeyDown, graphics);
                        setVar(fV, start); 
                        forStack.Push(new ForFrame { VarName = fV, EndValue = end, StepValue = step, LineAfterForPc = pc + 1, ForLineNumber = ln });
                        break;
                    case "NEXT":
                        if (forStack.Count == 0) break;
                        var f = forStack.Peek(); 
                        var cur = GetDoubleVar(f.VarName, vars, ln) + f.StepValue; 
                        setVar(f.VarName, cur);
                            
                        // Lägg till en liten marginal (0.000001) för att undvika att flyttalsfel kör loopen en gång för mycket
                        bool loopDone = f.StepValue > 0 ? cur > (f.EndValue + 0.000001) : cur < (f.EndValue - 0.000001);
                            
                        if (!loopDone) { 
                            pc = f.LineAfterForPc; 
                            jumpHappened = true; 
                        } else { 
                            forStack.Pop(); 
                        }
                        break;
                    case "WHILE":
                    {
                        bool conditionw = EvalCondition(arg, vars, ln, getInkey, isKeyDown, graphics);

                        if (!conditionw)
                        {
                            // Hoppa direkt till raden efter WEND
                            if (!whileMap.TryGetValue(pc, out var wendPc))
                                throw new Exception($"WHILE without WEND at line {ln}");

                            pc = wendPc + 1;
                            jumpHappened = true;
                        }
                        else
                        {
                            // Villkoret sant → fortsätt, men kom ihåg loopen
                            if (!whileMap.TryGetValue(pc, out var wpc))
                                throw new Exception($"WHILE without WEND at line {ln}");

                            whileStack.Push(new WhileFrame
                            {
                                WhilePc = pc,
                                WendPc = whileMap[pc],
                                Line = ln,
                                Condition = arg
                            });
                        }
                        break;
                    }
                    case "WEND":
                    {
                        if (whileStack.Count == 0)
                            throw new Exception($"WEND without WHILE at line {ln}");

                        var frame = whileStack.Peek();

                        // Utvärdera villkoret igen
                        var whileLine = StripLeadingLineNumber(
                            StripComments(lines[frame.WhilePc])
                        );

                        var conditionText = whileLine.Substring(5).Trim(); // efter "WHILE"

                        if (EvalCondition(conditionText, vars, ln, getInkey, isKeyDown, graphics))
                        {
                            pc = frame.WhilePc;
                            jumpHappened = true;
                        }
                        else
                        {
                            // Klart → lämna loopen
                            whileStack.Pop();
                        }
                        break;
                    }
                    case "REPEAT":
                    {
                        // Lägg till repeat frame utan UNTILPc
                        repeatStack.Push(new RepeatFrame
                        {
                            RepeatPc = pc,
                            RepeatLine = ln,
                            UntilPc = 0 // sätts senare när vi hittar UNTIL
                        });
                        break;
                    }

                    case "UNTIL":
                    {
                        if (repeatStack.Count == 0)
                            throw new Exception($"UNTIL without REPEAT at line {ln}");

                        var rf = repeatStack.Peek();

                        // Spara UNTILPc om det inte redan finns
                        if (rf.UntilPc == 0)
                            rf.UntilPc = pc;

                        if (EvalCondition(arg, vars, ln, getInkey, isKeyDown, graphics))
                        {
                            // Villkor sant → avsluta loop
                            repeatStack.Pop();
                        }
                        else
                        {
                            // Villkor falskt → hoppa tillbaka till REPEAT
                            pc = rf.RepeatPc;
                            jumpHappened = true;
                        }
                        break;
                    }

                    case "EXIT":
                    {
                        var what = arg.ToUpperInvariant();

                        if (what == "REPEAT")
                        {
                            if (repeatStack.Count == 0)
                                throw new Exception($"EXIT REPEAT without REPEAT at line {ln}");

                            var rf = repeatStack.Pop();

                            // Om UNTILPc inte är satt, skanna programmet framåt
                            if (rf.UntilPc == 0)
                            {
                                int searchPc = rf.RepeatPc + 1;
                                while (searchPc < lines.Length)
                                {
                                    var lSearch = StripComments(StripLeadingLineNumber(lines[searchPc])).Trim().ToUpperInvariant();
                                    if (lSearch.StartsWith("UNTIL "))
                                    {
                                        rf.UntilPc = searchPc;
                                        break;
                                    }
                                    searchPc++;
                                }
                                if (rf.UntilPc == 0)
                                    throw new Exception($"EXIT REPEAT before matching UNTIL at line {ln}");
                            }

                            pc = rf.UntilPc + 1;
                            jumpHappened = true;
                        }
                        else if (what == "WHILE")
                        {
                            // Befintlig WHILE-logik
                            if (whileStack.Count == 0)
                                throw new Exception($"EXIT WHILE without WHILE at line {ln}");
                            var wf = whileStack.Pop();
                            if (wf.WendPc == 0)
                                throw new Exception($"EXIT WHILE before matching WEND at line {ln}");
                            pc = wf.WendPc + 1;
                            jumpHappened = true;
                        }
                        break;
                    }

                    case "SCREEN":
                        var screenArgs = SplitCsvOrSpaces(arg);
                        if (screenArgs.Count > 0 && screenArgs[0].Equals("SELECT", StringComparison.OrdinalIgnoreCase)) {
                            if (screenArgs.Count >= 2) graphics.SetDrawingScreen(EvalInt(screenArgs[1], vars, ln, getInkey, isKeyDown, graphics));
                        } else if (screenArgs.Count >= 2) {
                            graphics.Screen(EvalInt(screenArgs[0], vars, ln, getInkey, isKeyDown, graphics), EvalInt(screenArgs[1], vars, ln, getInkey, isKeyDown, graphics));
                            //graphics.Clear(Colors.Transparent);
                        }
                        break;
                    case "SCROLL":
                        var sc = SplitCsvOrSpaces(arg);
                        if (sc.Count >= 3) graphics.Scroll(EvalInt(sc[0], vars, ln, getInkey, isKeyDown, graphics), EvalInt(sc[1], vars, ln, getInkey, isKeyDown, graphics), EvalInt(sc[2], vars, ln, getInkey, isKeyDown, graphics));
                        else {
                            var parts = arg.Split(',');
                            if (parts.Length >= 2) graphics.Scroll(0, EvalInt(parts[0], vars, ln, getInkey, isKeyDown, graphics), EvalInt(parts[1], vars, ln, getInkey, isKeyDown, graphics));
                        }
                        break;
                    case "CLSG": 
                        var x = graphics.GetActiveScreenNumber();
                        if (!string.IsNullOrWhiteSpace(arg))
                        {
                            // Om ett argument skickades med, välj det lagret först
                            graphics.SetDrawingScreen(EvalInt(arg, vars, ln, getInkey, isKeyDown, graphics));
                        }
                        graphics.Clear(Colors.Transparent); 
                        graphics.SetDrawingScreen(x);
                        onGraphicsChanged(); 
                        break;
                    case "LOAD": graphics.LoadBackground(Unquote(arg)); onGraphicsChanged(); break;
                    case "INK":
                    {
                        var c = ParseColor(arg);
                        
                        await appendLineAsync("@@INK " + c.ToString());
                        break;
                    }
                    case "INKG":
                    {
                        var c = ParseColor(arg);
                        graphics.Ink = c;
                        break;
                    }
                    case "PAPER":
                    {
                        var c2 = ParseColor(arg);

                        // Skicka till UI-tråden via console-pipeline (AppendConsoleLineAsync)
                        await appendLineAsync("@@PAPER " + c2.ToString());
                        break;
                    }
                    case "INC":
                        setVar(arg, GetDoubleVar(arg, vars, ln) + 1.0);
                        break;
                    case "DEC":
                        setVar(arg, GetDoubleVar(arg, vars, ln) - 1.0);
                        break;
                    case "PLOT": 
                        var pP = SplitCsvOrSpaces(arg); 
                        graphics.Plot(EvalInt(pP[0], vars, ln, getInkey, isKeyDown, graphics), EvalInt(pP[1], vars, ln, getInkey, isKeyDown, graphics)); 
                        onGraphicsChanged(); break;
                    case "LINE": 
                        var lL = SplitCsvOrSpaces(arg); 
                        graphics.Line(EvalInt(lL[0], vars, ln, getInkey, isKeyDown, graphics), EvalInt(lL[1], vars, ln, getInkey, isKeyDown, graphics), EvalInt(lL[2], vars, ln, getInkey, isKeyDown, graphics), EvalInt(lL[3], vars, ln, getInkey, isKeyDown, graphics)); 
                        onGraphicsChanged(); break;
                    case "BOX": 
                        var bB = SplitCsvOrSpaces(arg); 
                        graphics.Box(EvalInt(bB[0], vars, ln, getInkey, isKeyDown, graphics), EvalInt(bB[1], vars, ln, getInkey, isKeyDown, graphics), EvalInt(bB[2], vars, ln, getInkey, isKeyDown, graphics), EvalInt(bB[3], vars, ln, getInkey, isKeyDown, graphics)); 
                        onGraphicsChanged(); break;
                    case "BAR": 
                        var rR = SplitCsvOrSpaces(arg); 
                        graphics.Bar(EvalInt(rR[0], vars, ln, getInkey, isKeyDown, graphics), EvalInt(rR[1], vars, ln, getInkey, isKeyDown, graphics), EvalInt(rR[2], vars, ln, getInkey, isKeyDown, graphics), EvalInt(rR[3], vars, ln, getInkey, isKeyDown, graphics)); 
                        onGraphicsChanged(); break;
                    case "TEXT":
                        var tP = SplitCsvOrSpaces(arg);
                        if (tP.Count >= 3) graphics.DrawText(EvalInt(tP[0], vars, ln, getInkey, isKeyDown, graphics), EvalInt(tP[1], vars, ln, getInkey, isKeyDown, graphics), ValueToString(EvalValue(string.Join(" ", tP.Skip(2)), vars, ln, getInkey, isKeyDown, graphics)));
                        onGraphicsChanged(); break;
                    case "REFRESH": graphics.Refresh(); onGraphicsChanged(); break;
                        case "SPRITE":
                            var ss = SplitCsvOrSpaces(arg);
                            if (ss.Count == 0) break;
                            if (!int.TryParse(ss[0], out var sid)) {
                                var sub = ss[0].ToUpperInvariant();
                                if (sub=="POS") graphics.SpritePos(EvalInt(ss[1],vars,ln,getInkey,isKeyDown, graphics), EvalInt(ss[2],vars,ln,getInkey,isKeyDown, graphics), EvalInt(ss[3],vars,ln,getInkey,isKeyDown, graphics));
                                else if (sub=="LOAD") graphics.LoadSprite(EvalInt(ss[1], vars, ln, getInkey, isKeyDown, graphics), Unquote(ss[2]));
                                else if (sub=="ADDFRAME") graphics.AddFrame(EvalInt(ss[1], vars, ln, getInkey, isKeyDown, graphics), Unquote(ss[2]));
                                else if (sub=="FRAME") graphics.SetSpriteFrame(EvalInt(ss[1], vars, ln, getInkey, isKeyDown, graphics), EvalInt(ss[2], vars, ln, getInkey, isKeyDown, graphics));
                                else if (sub=="HANDLE") graphics.SpriteHandle(EvalInt(ss[1],vars,ln,getInkey,isKeyDown, graphics), EvalInt(ss[2],vars,ln,getInkey,isKeyDown, graphics), EvalInt(ss[3],vars,ln,getInkey,isKeyDown, graphics));
                                else if (sub=="ROTATE") graphics.SpriteRotate(EvalInt(ss[1], vars, ln, getInkey, isKeyDown, graphics), EvalInt(ss[2], vars, ln, getInkey, isKeyDown, graphics));
                                else if (sub=="ZOOM") {
                                    int id = EvalInt(ss[1], vars, ln, getInkey, isKeyDown, graphics);
                                    double zx = EvalInt(ss[2], vars, ln, getInkey, isKeyDown, graphics) / 100.0;
                                    double zy = (ss.Count >= 4) ? EvalInt(ss[3], vars, ln, getInkey, isKeyDown, graphics) / 100.0 : zx;
                                    graphics.SpriteZoom(id, zx, zy);
                                }
                                else if (sub=="ON") graphics.SpriteOn(EvalInt(ss[1],vars,ln,getInkey,isKeyDown, graphics));
                                else if (sub=="OFF") graphics.SpriteOff(EvalInt(ss[1],vars,ln,getInkey,isKeyDown, graphics));
                            } else graphics.CreateSprite(sid, EvalInt(ss[1],vars,ln,getInkey,isKeyDown, graphics), EvalInt(ss[2],vars,ln,getInkey,isKeyDown, graphics)); 
                            break;
                    case "SAM":
                        var samArgs = SplitCsvOrSpaces(arg);
                        if (samArgs.Count >= 2 && samArgs[0].ToUpperInvariant() == "PLAY") {
                            PlayEffect(Unquote(samArgs[1]), audioEngine); 
                        }
                        break;
                    case "MUSIC":
                        var musArgs = SplitCsvOrSpaces(arg);
                        if (musArgs.Count >= 2 && musArgs[0].ToUpperInvariant() == "PLAY") {
                            PlayMusic(Unquote(musArgs[1]), audioEngine); 
                        } else if (musArgs.Count >= 1 && musArgs[0].ToUpperInvariant() == "STOP") {
                            StopMusic(audioEngine);
                        }
                        break;
                    case "RAINBOW":
                            int currentLayer = graphics.GetActiveScreenNumber();

                            if (arg.ToUpperInvariant().StartsWith("STR("))
                            {
                                int openParen = arg.IndexOf('(');
                                int closeParen = arg.IndexOf(')');
                                // Hämta innehållet inuti parentesen (t.ex. "I" eller "5")
                                string inner = arg.Substring(openParen + 1, closeParen - openParen - 1);
                                // Utvärdera uttrycket ordentligt så att variabler som 'I' fungerar
                                var val = EvalValue(inner, vars, ln, getInkey, isKeyDown, graphics);
                                int rbNum = Convert.ToInt32(val);
                                
                                int eqIdx = arg.IndexOf('=');
                                if (eqIdx > 0) {
                                    string colorStr = ValueToString(EvalValue(arg[(eqIdx + 1)..].Trim(), vars, ln, getInkey, isKeyDown, graphics));
                                    var parts = colorStr.Split(',').Select(c => c.Trim()).ToList();
                                    var c1 = ParseColor(parts[0]);
                                    var c2 = parts.Count > 1 ? ParseColor(parts[1]) : c1;
                                    graphics.SetShaderColors(currentLayer, rbNum, c1, c2);
                                }
                            }
                            else {
                                var rbArgs = SplitCsvOrSpaces(arg.Trim());
                                if (rbArgs.Count >= 4) {
                                    // Använd EvalDouble på varje argument så att variabler slås upp korrekt
                                    var val = EvalValue(rbArgs[0], vars, ln, getInkey, isKeyDown, graphics);
                                    int rbNum = Convert.ToInt32(val);
                                    int rbOff = (int)Math.Round(EvalDouble(rbArgs[2], vars, ln, getInkey, isKeyDown, graphics));
                                    int rbH = (int)Math.Round(EvalDouble(rbArgs[3], vars, ln, getInkey, isKeyDown, graphics));
            
                                    graphics.SetShaderParams(currentLayer, rbNum, (float)rbOff, (float)rbH);
                                }
                            }
                            break;
                    case "RAIN": 
                        var rainArgs = SplitCsvOrSpaces(arg);
                        if (rainArgs.Count >= 2) {
                            int type = EvalInt(rainArgs[0], vars, ln, getInkey, isKeyDown, graphics);
                            float density = (float)EvalDouble(rainArgs[1], vars, ln, getInkey, isKeyDown, graphics);
                            int curL = graphics.GetActiveScreenNumber();
                            // Slot 0 = Typ, Slot 1 = Mängd
                            graphics.SetShadervalues(curL, 1, (float)type, density);
                        }
                        break;
                    case "TILE":
                        var tArgs = SplitCsvOrSpaces(arg);
                        if (tArgs.Count > 0) {
                            var tileSub = tArgs[0].ToUpperInvariant(); // Ändrat namn från sub till tileSub
                            if (tileSub == "LOAD" && tArgs.Count >= 4) 
                                graphics.LoadTileBank(Unquote(tArgs[1]), EvalInt(tArgs[2], vars, ln, getInkey, isKeyDown, graphics), EvalInt(tArgs[3], vars, ln, getInkey, isKeyDown, graphics));
                            else if (tileSub == "MAP" && tArgs.Count >= 3) 
                                graphics.SetMapSize(EvalInt(tArgs[1], vars, ln, getInkey, isKeyDown, graphics), EvalInt(tArgs[2], vars, ln, getInkey, isKeyDown, graphics));
                            else if (tileSub == "SET" && tArgs.Count >= 4) 
                                graphics.SetMapTile(EvalInt(tArgs[1], vars, ln, getInkey, isKeyDown, graphics), EvalInt(tArgs[2], vars, ln, getInkey, isKeyDown, graphics), EvalInt(tArgs[3], vars, ln, getInkey, isKeyDown, graphics));
                            else if (tileSub == "DRAW" && tArgs.Count >= 3) 
                                graphics.DrawMap(EvalInt(tArgs[1], vars, ln, getInkey, isKeyDown, graphics), EvalInt(tArgs[2], vars, ln, getInkey, isKeyDown, graphics));
                        }
                        break;
                    case "FONT":
                            var fArgs = SplitCsvOrSpaces(arg);
                            if (fArgs.Count > 0) {
                                var fSub = fArgs[0].ToUpperInvariant();
                                if (fSub == "LOAD" && fArgs.Count >= 5)
                                    graphics.FontLoad(EvalInt(fArgs[1], vars, ln, getInkey, isKeyDown, graphics), Unquote(fArgs[2]), EvalInt(fArgs[3], vars, ln, getInkey, isKeyDown, graphics), EvalInt(fArgs[4], vars, ln, getInkey, isKeyDown, graphics));
                                else if (fSub == "MAP" && fArgs.Count >= 3)
                                    graphics.FontMap(EvalInt(fArgs[1], vars, ln, getInkey, isKeyDown, graphics), Unquote(string.Join(" ", fArgs.Skip(2))));
                                else if (fSub == "PRINT" && fArgs.Count >= 4)
                                    graphics.FontPrint(EvalInt(fArgs[1], vars, ln, getInkey, isKeyDown, graphics), EvalInt(fArgs[2], vars, ln, getInkey, isKeyDown, graphics), EvalInt(fArgs[3], vars, ln, getInkey, isKeyDown, graphics), ValueToString(EvalValue(string.Join(" ", fArgs.Skip(4)), vars, ln, getInkey, isKeyDown, graphics)));
                                else if (fSub == "CHAR" && fArgs.Count >= 5)
                                    graphics.FontChar(EvalInt(fArgs[1], vars, ln, getInkey, isKeyDown, graphics), EvalInt(fArgs[2], vars, ln, getInkey, isKeyDown, graphics), EvalInt(fArgs[3], vars, ln, getInkey, isKeyDown, graphics), ValueToString(EvalValue(fArgs[4], vars, ln, getInkey, isKeyDown, graphics)));
                                else if (fSub == "ROTATE" && fArgs.Count >= 3)
                                    graphics.FontRotate(EvalInt(fArgs[1], vars, ln, getInkey, isKeyDown, graphics), EvalInt(fArgs[2], vars, ln, getInkey, isKeyDown, graphics));
                                else if (fSub == "ZOOM" && fArgs.Count >= 3) {
                                    int fid = EvalInt(fArgs[1], vars, ln, getInkey, isKeyDown, graphics);
                                    double fzx = EvalInt(fArgs[2], vars, ln, getInkey, isKeyDown, graphics) / 100.0;
                                    double fzy = (fArgs.Count >= 4) ? EvalInt(fArgs[3], vars, ln, getInkey, isKeyDown, graphics) / 100.0 : fzx;
                                    graphics.FontZoom(fid, fzx, fzy);
                                }
                                else if (fSub == "CLEAR")
                                {
                                    graphics.FontClear();
                                }
                            }
                            onGraphicsChanged();
                            break;
                    case "MAP":
                        var mArgs = SplitCsvOrSpaces(arg);
                        if (mArgs.Count >= 2 && mArgs[0].ToUpperInvariant() == "LOAD") {
                            var path = Unquote(mArgs[1]);
                            if (System.IO.File.Exists(path)) {
                                try {
                                    using var stream = System.IO.File.OpenRead(path);
                                    var dto = await System.Text.Json.JsonSerializer.DeserializeAsync<MapDto>(stream);
                                    if (dto != null) {
                                        graphics.SetMapSize(dto.Width, dto.Height);
                                        int idx = 0;
                                        for (int y = 0; y < dto.Height; y++) {
                                            for (int z = 0; z < dto.Width; z++) {
                                                graphics.SetMapTile(z, y, dto.Data[idx++]);
                                            }
                                        }
                                        // Rita ut banan på det stora lagret direkt efter laddning
                                        graphics.DrawMap(0, 0);
                                    }
                                } catch (Exception ex) {
                                    await appendLineAsync("MAP LOAD ERROR: " + ex.Message);
                                }
                            }
                        }
                        break;
                    default:
                        if (!string.IsNullOrWhiteSpace(cmd))
                        {
                            if (fullCmd.Contains('='))
                            {
                                var (leftSide, varValue) = SplitAssignment(fullCmd);

                                var rightValue = EvalValue(varValue, vars, ln, getInkey, isKeyDown, graphics);

                                if (leftSide.Contains('('))
                                {
                                    int openParen = leftSide.IndexOf('(');
                                    int closeParen = leftSide.LastIndexOf(')');
                                    if (openParen != -1 && closeParen != -1)
                                    {
                                        var arrayName = leftSide[..openParen].Trim();
                                        var idxStr = leftSide[(openParen + 1)..closeParen];

                                        if (vars.TryGetValue(arrayName, out var aVal) && aVal is IAmosArray array)
                                        {
                                            var rawIdx = EvalValue(idxStr, vars, ln, getInkey, isKeyDown, graphics);
                                            int aIdx = (int)Math.Round(Convert.ToDouble(rawIdx, CultureInfo.InvariantCulture));

                                            if (aIdx >= 0 && aIdx < array.Length)
                                            {
                                                array.Set(aIdx, rightValue);
                                            }
                                        }
                                    }
                                }
                                else
                                {
                                    setVar(leftSide, rightValue);
                                }
                            }
                            else
                            {
                                throw new Exception($"Syntax Error: '{cmd}' at line {ln}");
                            }
                        }
                        break;
                }
                if (jumpHappened) break;
            }
            if (!jumpHappened) pc++;
            continue;
            next_line: pc++;
        }
    }

    private static List<string> SplitMultipleCommands(string l) {
        var trimmed = l.TrimStart();
        if (trimmed.StartsWith("IF ", StringComparison.OrdinalIgnoreCase) &&
            trimmed.IndexOf("THEN", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return new List<string> { l.Trim() };
        }
        
        var res = new List<string>(); bool q = false; int s = 0;
        for (int i = 0; i < l.Length; i++) {
            if (l[i] == '\"') q = !q;
            if (!q && l[i] == ':') { res.Add(l[s..i].Trim()); s = i + 1; }
        }
        res.Add(l[s..].Trim()); return res;
    }
    private static async Task<(bool isJump, int jumpPc)> ExecuteInlineStatementAsync(string cmd, string arg, Dictionary<string, object> vars, Func<string, Task> al, Func<Task> cl, AmosGraphics g, Action og, Func<string> gk, Func<string, bool> ikd, AudioEngine? audioEngine, CancellationToken t, int ln, Stack<RepeatFrame> repeatStack, Stack<WhileFrame> whileStack, string[] lines)
    {
        if (cmd == "EXIT")
        {
            var what = arg.ToUpperInvariant();
            if (what == "REPEAT")
            {
                if (repeatStack.Count == 0) throw new Exception($"EXIT REPEAT without REPEAT at line {ln}");
                var rf = repeatStack.Pop();
                if (rf.UntilPc == 0)
                {
                    // Enkel sökning framåt efter UNTIL om den inte är mappad
                    for (int i = rf.RepeatPc; i < lines.Length; i++) {
                        if (StripComments(lines[i]).Trim().ToUpperInvariant().StartsWith("UNTIL ")) { rf.UntilPc = i; break; }
                    }
                }
                return (true, rf.UntilPc + 1);
            }
            if (what == "WHILE")
            {
                if (whileStack.Count == 0) throw new Exception($"EXIT WHILE without WHILE at line {ln}");
                var wf = whileStack.Pop();
                return (true, wf.WendPc + 1);
            }
        }

        // För alla andra kommandon, kör den gamla logiken
        await ExecuteSingleStatementAsync(cmd, arg, vars, al, cl, g, og, gk, ikd, audioEngine, t, ln);
        return (false, 0);
    }
    
    private static async Task<bool> ExecuteSingleStatementAsync(string cmd, string arg, Dictionary<string, object> vars, Func<string, Task> al, Func<Task> cl, AmosGraphics g, Action og, Func<string> gk, Func<string, bool> ikd, AudioEngine? audioEngine, CancellationToken t, int ln)
    {
        // Om kommandot innehåller ett '=', är det förmodligen en tilldelning (t.ex. SX = SX + 8)
        if (cmd.Contains('=') || arg.StartsWith("=")) {
            string assignment = cmd + " " + arg;
            var (n, vt) = SplitAssignment(assignment);
            vars[n] = EvalValue(vt, vars, ln, gk, ikd, g);
            return false;
        }
        switch (cmd) {
            case "PRINT":
                await EmitPrintAsync(al, ValueToString(EvalValue(arg, vars, ln, gk, ikd, g)));
                return false;            case "INC": vars[arg] = GetDoubleVar(arg, vars, ln) + 1.0; return false;
            case "DEC": vars[arg] = GetDoubleVar(arg, vars, ln) - 1.0; return false;
            case "LOCATE": var p = SplitCsvOrSpaces(arg); await al($"@@LOCATE {EvalInt(p[0], vars, ln, gk, ikd, g)} {EvalInt(p[1], vars, ln, gk, ikd, g)}"); return false;
            case "LET": 
                var (n, vt) = SplitAssignment(arg); 
                vars[n] = EvalValue(vt, vars, ln, gk, ikd, g); 
                return false;
            case "PLOT": var pp = SplitCsvOrSpaces(arg); g.Plot(EvalInt(pp[0], vars, ln, gk, ikd, g), EvalInt(pp[1], vars, ln, gk, ikd, g)); og(); return false;
            case "SPRITE":
                var ssa = SplitCsvOrSpaces(arg);
                if (ssa.Count >= 2) {
                    var sub = ssa[0].ToUpperInvariant();
                    if (sub == "POS") g.SpritePos(EvalInt(ssa[1], vars, ln, gk, ikd, g), EvalInt(ssa[2], vars, ln, gk, ikd, g), EvalInt(ssa[3], vars, ln, gk, ikd, g));
                    else if (sub == "FRAME") g.SetSpriteFrame(EvalInt(ssa[1], vars, ln, gk, ikd, g), EvalInt(ssa[2], vars, ln, gk, ikd, g));
                    else if (sub == "ROTATE") g.SpriteRotate(EvalInt(ssa[1], vars, ln, gk, ikd, g), EvalInt(ssa[2], vars, ln, gk, ikd, g));
                    else if (sub == "ZOOM") {
                        int id = EvalInt(ssa[1], vars, ln, gk, ikd, g);
                        double zx = EvalInt(ssa[2], vars, ln, gk, ikd, g) / 100.0;
                        double zy = (ssa.Count >= 4) ? EvalInt(ssa[3], vars, ln, gk, ikd, g) / 100.0 : zx;
                        g.SpriteZoom(id, zx, zy);
                    }
                    else if (sub == "ON") g.SpriteOn(EvalInt(ssa[1], vars, ln, gk, ikd, g));
                    else if (sub == "OFF") g.SpriteOff(EvalInt(ssa[1], vars, ln, gk, ikd, g));
                }
                return false;
            case "WAIT":
                if (arg.ToUpperInvariant() == "VBL")
                {
                    // Vänta på nästa GPU-frame på ett korrekt sätt
                    await WaitNextFrameAsync(t);
                }
                else
                {
                    int ms = Math.Max(0, EvalInt(arg, vars, ln, gk, ikd, g));
                    await Task.Delay(ms, t);
                }
                return false;
            case "VSYNC": await al("@@VSYNC"); return false;
            case "REFRESH": g.Refresh(); og(); return false;
            case "SAM":
                var sa = SplitCsvOrSpaces(arg);
                if (sa.Count >= 2 && sa[0].ToUpperInvariant() == "PLAY") {
                    PlayEffect(Unquote(sa[1]), audioEngine);
                }
                return false;
            case "END": return true;
            default: if (!string.IsNullOrWhiteSpace(cmd)) throw new Exception($"Syntax Error in IF-THEN: Unknown command '{cmd}' at line {ln}"); return false;
        }
    }
    
        private static bool EvalCondition(string c, Dictionary<string, object> v, int ln, Func<string> gk, Func<string, bool> ikd, AmosGraphics g)
        {
            c = c.Trim();

            // NOT (hög prioritet)
            if (c.StartsWith("NOT ", StringComparison.OrdinalIgnoreCase))
            {
                return !EvalCondition(c.Substring(4).Trim(), v, ln, gk, ikd, g);
            }
            
            // 1. Hantera OR (Lägst prioritet, kollas först)
            var orIdx = IndexOfWord(c, " OR ");
            if (orIdx >= 0) {
                return EvalCondition(c[..orIdx].Trim(), v, ln, gk, ikd, g) || 
                       EvalCondition(c[(orIdx + 4)..].Trim(), v, ln, gk, ikd, g);
            }

            // 2. Hantera AND
            var andIdx = IndexOfWord(c, " AND ");
            if (andIdx >= 0) {
                return EvalCondition(c[..andIdx].Trim(), v, ln, gk, ikd, g) && 
                       EvalCondition(c[(andIdx + 5)..].Trim(), v, ln, gk, ikd, g);
            }

            // 3. Befintlig logik för jämförelser (=, <, >, etc.)
            if (!c.Contains('=') && !c.Contains('<') && !c.Contains('>')) {
                // Konvertera till double: allt utom exakt 0.0 räknas som sant
                return Math.Abs(Convert.ToDouble(EvalValue(c, v, ln, gk, ikd, g))) > 0.000001;
            }

            var ops = new[] { "<>", "<=", ">=", "=", "<", ">" };
            foreach (var op in ops) {
                var i = c.IndexOf(op); if (i < 0) continue;
                var lV = EvalValue(c[..i].Trim(), v, ln, gk, ikd, g); 
                var rV = EvalValue(c[(i + op.Length)..].Trim(), v, ln, gk, ikd, g);
                
                if (lV is string || rV is string) { 
                    var ls = ValueToString(lV); 
                    var rs = ValueToString(rV); 
                    return op == "=" ? ls == rs : ls != rs; 
                }

                // Använd Double här för att stödja flyttalsjämförelser!
                var li = Convert.ToDouble(lV); 
                var ri = Convert.ToDouble(rV);

                return op switch { 
                    "=" => Math.Abs(li - ri) < 0.000001, // Säker jämförelse för flyttal
                    "<>" => Math.Abs(li - ri) > 0.000001, 
                    "<" => li < ri, 
                    ">" => li > ri, 
                    "<=" => li <= ri, 
                    ">=" => li >= ri, 
                    _ => false 
                };
            }
            return false;
        }

    private static string StripComments(string l) {
        bool q = false;
        for (int i = 0; i < l.Length; i++) {
            if (l[i] == '\"') q = !q;
            if (!q && l[i] == ';') return l[..i].Trim();
        }
        return l;
    }

    private static object EvalValue(string t, Dictionary<string, object> v, int ln, Func<string> gk, Func<string, bool> ikd, AmosGraphics g) {
        if (string.IsNullOrWhiteSpace(t)) return "";
        var tok = new Tokenizer(t);
        return ParseExpr(ref tok, v, ln, gk, ikd, g);
    }

    private static int EvalInt(string val, Dictionary<string, object> v, int ln, Func<string> gk, Func<string, bool> ikd, AmosGraphics g) { 
        // Använd din nya uträkningslogik som returnerar double och runda av
        return (int)Math.Round(EvalDouble(val, v, ln, gk, ikd, g));
    }
    
    private static double EvalDouble(string val, Dictionary<string, object> v, int ln, Func<string> gk, Func<string, bool> ikd, AmosGraphics g) { 
        var result = EvalValue(val, v, ln, gk, ikd, g);
        if (result is double d) return d;
        if (result is string s && double.TryParse(s, CultureInfo.InvariantCulture, out var parsed)) return parsed;
        return 0.0;
    }
    

        private static object ParseExpr(ref Tokenizer t, Dictionary<string, object> v, int ln, Func<string> gk, Func<string, bool> ikd, AmosGraphics g) {
            var res = ParseTerm(ref t, v, ln, gk, ikd, g);
            while (true) { 
                if (t.TryConsume('+')) {
                    var right = ParseTerm(ref t, v, ln, gk, ikd, g);
                    if (res is string || right is string) res = ValueToString(res) + ValueToString(right);
                    else res = Convert.ToDouble(res, CultureInfo.InvariantCulture) + Convert.ToDouble(right, CultureInfo.InvariantCulture);
                } 
                else if (t.TryConsume('-')) {
                    var right = ParseTerm(ref t, v, ln, gk, ikd, g);
                    res = Convert.ToDouble(res, CultureInfo.InvariantCulture) - Convert.ToDouble(right, CultureInfo.InvariantCulture);
                }
                else if (t.TryConsume('&')) {
                    var right = ParseTerm(ref t, v, ln, gk, ikd, g);
                    res = ValueToString(res) + ValueToString(right);
                }
                else break; 
            }
            return res;
        }

        private static object ParseTerm(ref Tokenizer t, Dictionary<string, object> v, int ln, Func<string> gk, Func<string, bool> ikd, AmosGraphics g) {
            var res = ParseFactor(ref t, v, ln, gk, ikd, g);
            while (true) { 
                if (t.TryConsume('*')) {
                    var right = ParseFactor(ref t, v, ln, gk, ikd, g);
                    res = Convert.ToDouble(res, CultureInfo.InvariantCulture) * Convert.ToDouble(right, CultureInfo.InvariantCulture);
                }
                else if (t.TryConsume('/')) {
                    var d = ParseFactor(ref t, v, ln, gk, ikd, g);
                    double div = Convert.ToDouble(d, CultureInfo.InvariantCulture);
                    res = (div == 0) ? 0.0 : Convert.ToDouble(res, CultureInfo.InvariantCulture) / div;
                } else break; 
            }
            return res;
        }

    
        private static object ParseFactor(ref Tokenizer t, Dictionary<string, object> v, int ln, Func<string> gk, Func<string, bool> ikd, AmosGraphics g) {
            t.SkipWs();
            if (t.TryReadString(out var s)) return s;
            if (t.TryConsume('(')) { var res = ParseExpr(ref t, v, ln, gk, ikd, g); t.TryConsume(')'); return res; }
            if (t.TryReadDouble(out var n)) return n; 
            if (t.TryReadIdentifier(out var id)) {
                t.SkipWs();
                if (id.Equals("INKEY$", StringComparison.OrdinalIgnoreCase)) return gk();
                if (t.TryConsume('(')) {
                    if (id.Equals("STR$", StringComparison.OrdinalIgnoreCase)) {
                        object val = ParseExpr(ref t, v, ln, gk, ikd, g); t.TryConsume(')'); return ValueToString(val);
                    }
                    if (id.Equals("CHR$", StringComparison.OrdinalIgnoreCase)) {
                        int ascii = (int)Math.Round(Convert.ToDouble(ParseExpr(ref t, v, ln, gk, ikd, g), CultureInfo.InvariantCulture));
                        t.TryConsume(')'); return ((char)Math.Clamp(ascii, 0, 255)).ToString();
                    }
                    if (id.Equals("ASC", StringComparison.OrdinalIgnoreCase)) {
                        object val = ParseExpr(ref t, v, ln, gk, ikd, g);
                        t.TryConsume(')'); string str = ValueToString(val);
                        return str.Length > 0 ? (double)str[0] : 0.0;
                    }
                    if (id.Equals("VAL", StringComparison.OrdinalIgnoreCase)) {
                        object val = ParseExpr(ref t, v, ln, gk, ikd, g); t.TryConsume(')');
                        double.TryParse(ValueToString(val), CultureInfo.InvariantCulture, out var dv); return dv;
                    }
                    if (id.Equals("SIN", StringComparison.OrdinalIgnoreCase)) {
                        double a = Convert.ToDouble(ParseExpr(ref t, v, ln, gk, ikd, g), CultureInfo.InvariantCulture); 
                        t.TryConsume(')'); return Math.Sin(a * Math.PI / 180.0);
                    }
                    if (id.Equals("COS", StringComparison.OrdinalIgnoreCase)) {
                        double a = Convert.ToDouble(ParseExpr(ref t, v, ln, gk, ikd, g), CultureInfo.InvariantCulture); 
                        t.TryConsume(')'); return Math.Cos(a * Math.PI / 180.0);
                    }
                    if (id.Equals("RND", StringComparison.OrdinalIgnoreCase)) {
                        double m = Convert.ToDouble(ParseExpr(ref t, v, ln, gk, ikd, g), CultureInfo.InvariantCulture); 
                        t.TryConsume(')'); return _rng.NextDouble() * m;
                    }
                    if (id.Equals("INT", StringComparison.OrdinalIgnoreCase)) {
                        double val = Convert.ToDouble(ParseExpr(ref t, v, ln, gk, ikd, g), CultureInfo.InvariantCulture); 
                        t.TryConsume(')'); return Math.Floor(val + 0.000001);
                    }
                    if (id.Equals("HEX", StringComparison.OrdinalIgnoreCase))
                    {
                        int val = Convert.ToInt32(ParseExpr(ref t, v, ln, gk, ikd, g)); 
                        t.TryConsume(')'); return val.ToString("X");
                    }
                    if (id.Equals("INC", StringComparison.OrdinalIgnoreCase)) {
                        double val = Convert.ToDouble(ParseExpr(ref t, v, ln, gk, ikd, g), CultureInfo.InvariantCulture); 
                        t.TryConsume(')'); return val + 1; 
                    }   
                    if (id.Equals("DEC", StringComparison.OrdinalIgnoreCase)) {
                        double val = Convert.ToDouble(ParseExpr(ref t, v, ln, gk, ikd, g), CultureInfo.InvariantCulture); 
                        t.TryConsume(')'); return val - 1; 
                    }
                    if (id.Equals("HIT", StringComparison.OrdinalIgnoreCase)) {
                        int id1 = (int)Math.Round(Convert.ToDouble(ParseExpr(ref t, v, ln, gk, ikd, g), CultureInfo.InvariantCulture)); t.TryConsume(',');
                        int id2 = (int)Math.Round(Convert.ToDouble(ParseExpr(ref t, v, ln, gk, ikd, g), CultureInfo.InvariantCulture)); t.TryConsume(')');
                        return g.SpriteHit(id1, id2) ? 1.0 : 0.0;
                    }
                    if (id.Equals("TILE", StringComparison.OrdinalIgnoreCase)) {
                        int layer = (int)Math.Round(Convert.ToDouble(ParseExpr(ref t, v, ln, gk, ikd, g), CultureInfo.InvariantCulture)); t.TryConsume(',');
                        int px = (int)Math.Round(Convert.ToDouble(ParseExpr(ref t, v, ln, gk, ikd, g), CultureInfo.InvariantCulture));
                        int py = 0;
                        if (t.TryConsume(',')) py = (int)Math.Round(Convert.ToDouble(ParseExpr(ref t, v, ln, gk, ikd, g), CultureInfo.InvariantCulture));
                        t.TryConsume(')');
                        return (double)g.GetMapTile(px / 32, py / 32);
                    }
                    if (id.Equals("KEYSTATE", StringComparison.OrdinalIgnoreCase)) {
                        var k = ValueToString(ParseExpr(ref t, v, ln, gk, ikd, g)); t.TryConsume(')'); return ikd(k) ? 1.0 : 0.0;
                    }

                        // --- String functions (AMOS-like) ---
                        if (id.Equals("LEN", StringComparison.OrdinalIgnoreCase))
                        {
                            object val = ParseExpr(ref t, v, ln, gk, ikd, g);
                            t.TryConsume(')');
                            string sVal = ValueToString(val);
                            return (double)sVal.Length;
                        }

                        if (id.Equals("TRIM$", StringComparison.OrdinalIgnoreCase))
                        {
                            object val = ParseExpr(ref t, v, ln, gk, ikd, g);
                            t.TryConsume(')');
                            return ValueToString(val).Trim();
                        }

                        if (id.Equals("LOWER$", StringComparison.OrdinalIgnoreCase))
                        {
                            object val = ParseExpr(ref t, v, ln, gk, ikd, g);
                            t.TryConsume(')');
                            return ValueToString(val).ToLowerInvariant();
                        }

                        if (id.Equals("LEFT$", StringComparison.OrdinalIgnoreCase))
                        {
                            object sObj = ParseExpr(ref t, v, ln, gk, ikd, g);
                            t.TryConsume(',');
                            object nObj = ParseExpr(ref t, v, ln, gk, ikd, g);
                            t.TryConsume(')');

                            string sVal = ValueToString(sObj);
                            int n1 = (int)Math.Round(Convert.ToDouble(nObj, CultureInfo.InvariantCulture));
                            if (n1 <= 0) return "";
                            if (n1 >= sVal.Length) return sVal;
                            return sVal.Substring(0, n1);
                        }

                        if (id.Equals("RIGHT$", StringComparison.OrdinalIgnoreCase))
                        {
                            object sObj = ParseExpr(ref t, v, ln, gk, ikd, g);
                            t.TryConsume(',');
                            object nObj = ParseExpr(ref t, v, ln, gk, ikd, g);
                            t.TryConsume(')');

                            string sVal = ValueToString(sObj);
                            int n2 = (int)Math.Round(Convert.ToDouble(nObj, CultureInfo.InvariantCulture));
                            if (n2 <= 0) return "";
                            if (n2 >= sVal.Length) return sVal;
                            return sVal.Substring(sVal.Length - n2, n2);
                        }

                        if (id.Equals("MID$", StringComparison.OrdinalIgnoreCase))
                        {
                            object sObj = ParseExpr(ref t, v, ln, gk, ikd, g);
                            t.TryConsume(',');
                            object startObj = ParseExpr(ref t, v, ln, gk, ikd, g);

                            int len = -1;
                            if (t.TryConsume(','))
                            {
                                object lenObj = ParseExpr(ref t, v, ln, gk, ikd, g);
                                len = (int)Math.Round(Convert.ToDouble(lenObj, CultureInfo.InvariantCulture));
                            }
                            t.TryConsume(')');

                            string sVal = ValueToString(sObj);
                            int start1 = (int)Math.Round(Convert.ToDouble(startObj, CultureInfo.InvariantCulture)); // 1-based
                            int start0 = Math.Max(0, start1 - 1);

                            if (start0 >= sVal.Length) return "";
                            if (len < 0) return sVal.Substring(start0);
                            if (len <= 0) return "";
                            int maxLen = Math.Min(len, sVal.Length - start0);
                            return sVal.Substring(start0, maxLen);
                        }

						if (id.Equals("REPLACE$", StringComparison.OrdinalIgnoreCase))
						{ 
                            // REPLACE$(source$, find$, replace$)

    						object srcObj = ParseExpr(ref t, v, ln, gk, ikd, g);
    						t.TryConsume(',');

						    object findObj = ParseExpr(ref t, v, ln, gk, ikd, g);
    						t.TryConsume(',');

    						object replObj = ParseExpr(ref t, v, ln, gk, ikd, g);
    						t.TryConsume(')');

    						string src = ValueToString(srcObj);
    						string find = ValueToString(findObj);
    						string repl = ValueToString(replObj);

    						// Skydd mot tom söksträng
    						if (string.IsNullOrEmpty(find))
        						return src;

						    return src.Replace(find, repl);
						}

                        if (id.Equals("INSTR", StringComparison.OrdinalIgnoreCase))
                        {
                            object sObj = ParseExpr(ref t, v, ln, gk, ikd, g);
                            t.TryConsume(',');
                            object findObj = ParseExpr(ref t, v, ln, gk, ikd, g);

                            int start1 = 1; // optional 1-based start position
                            if (t.TryConsume(','))
                            {
                                object startObj = ParseExpr(ref t, v, ln, gk, ikd, g);
                                start1 = (int)Math.Round(Convert.ToDouble(startObj, CultureInfo.InvariantCulture));
                            }
                            t.TryConsume(')');

                            string sVal = ValueToString(sObj);
                            string needle = ValueToString(findObj);

                            if (string.IsNullOrEmpty(needle)) return 0.0;

                            int start0 = Math.Max(0, start1 - 1);
                            if (start0 > sVal.Length) return 0.0;

                            int idx = sVal.IndexOf(needle, start0, StringComparison.Ordinal);
                            return idx >= 0 ? (double)(idx + 1) : 0.0; // 1-based, 0 if not found
                        }

                        if (id.Equals("WORD$", StringComparison.OrdinalIgnoreCase))
                        {
                            object sObj = ParseExpr(ref t, v, ln, gk, ikd, g);
                            t.TryConsume(',');
                            object nObj = ParseExpr(ref t, v, ln, gk, ikd, g);
                            t.TryConsume(')');

                            string sVal = ValueToString(sObj);
                            int n3 = (int)Math.Round(Convert.ToDouble(nObj, CultureInfo.InvariantCulture)); // 1-based
                            if (n3 <= 0) return "";

                            var parts = sVal
                                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                            if (n3 > parts.Length) return "";
                            return parts[n3 - 1];
                        }
                        
                    // Om vi kommer hit och det inte var en funktion, kolla om det är en array
                    if (v.TryGetValue(id, out var arrObj) && arrObj is IAmosArray arr)
                    {
                        double rawIdx = Convert.ToDouble(ParseExpr(ref t, v, ln, gk, ikd, g), CultureInfo.InvariantCulture);
                        t.TryConsume(')');
                        int aIdx = (int)Math.Floor(rawIdx + 0.000001);
                        if (aIdx >= 0 && aIdx < arr.Length) return arr.Get(aIdx);
                    }

                    throw new Exception($"Unknown function: {id} at line {ln}");

                    // ... existing code ...
                }
                if (v.TryGetValue(id, out var valVar)) return valVar;
                return 0.0;
            }
            return 0.0;
        }

static string InputCommand(string[] args)
{
    string prompt = "";
    string variableName = "";

    // Anta att args = ["\"Vad heter du?\"", "A$"]
    if (args.Length >= 2)
    {
        prompt = args[0].Trim('"');  // ta bort citationstecken
        variableName = args[1];
    }
    
    // Visa prompt i konsolen
    Console.Write(prompt + " ");

    // Läs hela raden
    string input = Console.ReadLine() ?? "";

    // Sätt variabeln i din AMOS-variabeltabell
    //SetStringVariable(variableName, input); 

    return ""; // INPUT returnerar inget
}

    private static string ValueToString(object? v)
    {
        if (v is double d) 
        {
            // "G10" betyder "General format" med 10 signifikanta siffror.
            // Det rensar bort de pyttesmå avrundningsfelen i slutet.
            return d.ToString("G10", CultureInfo.InvariantCulture);
        }
        return v?.ToString() ?? "";
    }
    private static bool IsQuotedString(string t) => t.Length >= 2 && t.StartsWith("\"") && t.EndsWith("\"");
    private static string Unquote(string t) => t.Trim('\"');
    private static double GetDoubleVar(string n, Dictionary<string, object> v, int ln)
    {
        if (v.TryGetValue(n, out var val))
        {
            if (val is IAmosArray) return 0.0;
            return Convert.ToDouble(val, CultureInfo.InvariantCulture);
        }
        return 0.0;
    }
    private static int GetIntVar(string n, Dictionary<string, object> v, int ln) => v.TryGetValue(n, out var val) ? Convert.ToInt32(val) : 0;
    private static (string n, string v) SplitAssignment(string t) { var i = t.IndexOf('='); return (t[..i].Trim(), t[(i + 1)..].Trim()); }
    private static (string c, string a) SplitCommand(string l) { 
        // 1. Trimma bort mellanslag i början och slutet direkt!
        l = l.Trim();
        if (string.IsNullOrWhiteSpace(l)) return ("", "");
        
        var i = l.IndexOf(' '); 
        if (i < 0) return (l.ToUpperInvariant(), "");
        
        // 2. Ta ut kommandot och argumentet, och trimma igen
        string cmd = l[..i].ToUpperInvariant().Trim();
        string arg = l[(i + 1)..].Trim();
        
        return (cmd, arg); 
    }
    
    private static void PlayEffect(string file, AudioEngine? engine) {
        if (engine == null) return;
        engine.PlaySample(file);
    }

    private static void PlayMusic(string file, AudioEngine? engine) {
        if (engine == null) return;
        try {
            StopMusic(engine); 

            IntPtr ctx = LibXmp.xmp_create_context();
            if (LibXmp.xmp_load_module(ctx, file) == 0)
            {
                LibXmp.xmp_start_player(ctx, 44100, 0);
                _currentXmpContext = ctx;
                engine.PlayMod(file);
            }
        } catch {}
    }

    public static void StopMusic(AudioEngine? engine = null) {
        try {
            if (_currentXmpContext != IntPtr.Zero) {
                // Vi låter AudioEngine sluta läsa först
                engine?.StopMod();
                LibXmp.xmp_release_module(_currentXmpContext);
                LibXmp.xmp_free_context(_currentXmpContext);
                _currentXmpContext = IntPtr.Zero;
            }
        } catch {}
    }

    public static void StopAllSounds() {
        StopMusic();
        // För att vara helt säkra vid STOP-knappen dödar vi alla afplay
        try { System.Diagnostics.Process.Start("killall", "afplay"); } catch {}
    }
    

    private static string StripLeadingLineNumber(string l) { var i = 0; while (i < l.Length && char.IsDigit(l[i])) i++; return l[i..].Trim(); }
    private static List<string> SplitCsvOrSpaces(string a) {
        if (string.IsNullOrWhiteSpace(a)) return new List<string>();
        return a.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }
    private static Color ParseColor(string t) { try { return Color.Parse(t); } catch { return Colors.White; } }
    private static int IndexOfWord(string t, string w) => t.ToUpperInvariant().IndexOf(w.ToUpperInvariant());

    private ref struct Tokenizer {
        private readonly string _s; private int _i;
        public Tokenizer(string s) { _s = s; _i = 0; }
        public void SkipWs() { while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++; }
        public bool TryConsume(char c) { SkipWs(); if (_i < _s.Length && _s[_i] == c) { _i++; return true; } return false; }
        public bool TryReadInt(out int v) { SkipWs(); var s = _i; while (_i < _s.Length && (char.IsDigit(_s[_i]) || (_i == s && _s[_i] == '-'))) _i++; return int.TryParse(_s[s.._i], out v); }
        public bool TryReadDouble(out double v) { 
            SkipWs(); 
            var s = _i; 
            while (_i < _s.Length && (char.IsDigit(_s[_i]) || _s[_i] == '.' || (_i == s && _s[_i] == '-'))) _i++; 
            return double.TryParse(_s[s.._i], CultureInfo.InvariantCulture, out v); 
        }
        public bool TryReadIdentifier(out string n) { SkipWs(); var s = _i; while (_i < _s.Length && (char.IsLetterOrDigit(_s[_i]) || _s[_i] == '$')) _i++; n = _s[s.._i]; return n.Length > 0; }
        public string ReadUntil(char c) { var s = _i; while (_i < _s.Length && _s[_i] != c) _i++; return _s[s.._i]; }
        public bool TryReadString(out string v)
        {
            SkipWs();
            v = "";
            if (_i < _s.Length && _s[_i] == '\"')
            {
                _i++; // Skippa första "
                var start = _i;
                while (_i < _s.Length && _s[_i] != '\"') _i++;
                v = _s[start.._i];

                // Tolka \r\n etc i stränglitteraler
                v = UnescapeBasicString(v);

                if (_i < _s.Length) _i++; // Skippa sista "
                return true;
            }
            return false;
        }
    }

    private readonly record struct PrintAtArgs(string RowExpr, string ColExpr, string RestExpr);

        /// <summary>
        /// Parses: "AT <rowExpr>[,|space]<colExpr>[,]<restExpr?>"
        /// Robust against commas/spaces inside quotes and parentheses.
        /// Examples:
        ///  PRINT AT 10,5,"HI"
        ///  PRINT AT X+1, Y*2, "HELLO"
        ///  PRINT AT 1 1, STR$(A)
        /// </summary>
        private static PrintAtArgs ParsePrintAtArguments(string printArg)
        {
            // remove leading "AT"
            var s = printArg.Trim();
            if (s.Length >= 2 && s.StartsWith("AT", StringComparison.OrdinalIgnoreCase))
                s = s[2..].TrimStart();

            int i = 0;

            string ReadExpr()
            {
                // Read until separator at top-level: comma or whitespace.
                bool inQuotes = false;
                int parenDepth = 0;

                int start = i;
                while (i < s.Length)
                {
                    char ch = s[i];

                    if (ch == '"')
                    {
                        inQuotes = !inQuotes;
                        i++;
                        continue;
                    }

                    if (!inQuotes)
                    {
                        if (ch == '(') { parenDepth++; i++; continue; }
                        if (ch == ')') { if (parenDepth > 0) parenDepth--; i++; continue; }

                        if (parenDepth == 0)
                        {
                            if (ch == ',' || char.IsWhiteSpace(ch))
                                break;
                        }
                    }

                    i++;
                }

                return s[start..i].Trim();
            }

            void SkipSeparators()
            {
                // Skip whitespace and at most one comma (plus surrounding whitespace)
                while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
                if (i < s.Length && s[i] == ',') i++;
                while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            }

            var rowExpr = ReadExpr();
            SkipSeparators();
            var colExpr = ReadExpr();
            SkipSeparators();

            var rest = (i < s.Length) ? s[i..].Trim() : "";
            if (rest.StartsWith(",", StringComparison.Ordinal))
                rest = rest[1..].TrimStart();

            if (string.IsNullOrWhiteSpace(rowExpr) || string.IsNullOrWhiteSpace(colExpr))
                throw new Exception("Syntax Error in PRINT AT: expected row and col");

            return new PrintAtArgs(rowExpr, colExpr, rest);
        }

    
    
    private static DateTime _lastFrameTime = DateTime.MinValue;

    private static async Task WaitNextFrameAsync(CancellationToken token)
    {
        // ~60 FPS -> 1000ms / 60 ≈ 16.6667ms
        const double targetMs = 1000.0 / 60.0;

        var now = DateTime.UtcNow;
        double elapsed = _lastFrameTime == DateTime.MinValue 
            ? targetMs 
            : (now - _lastFrameTime).TotalMilliseconds;

        double delay = Math.Max(0, targetMs - elapsed);

        _lastFrameTime = now.AddMilliseconds(delay);
        await Task.Delay(TimeSpan.FromMilliseconds(delay), token);
    }
    private record MapDto(int Width, int Height, List<int> Data);
}