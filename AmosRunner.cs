using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using System.Linq;
using System.Net.Security;
using System.Reflection.Metadata.Ecma335;
using System.Text;

//using AmoslikeBasic;
using Avalonia.Interactivity;

namespace AmosLikeBasic;

public static class AmosRunner
{
    // Lägg till i din klass
    private static Dictionary<int, FileChannel> _openFiles = new Dictionary<int, FileChannel>();
    private const int MaxChannels = 15;
    
    //public static MainWindow _mainWindow;
    
    private static Color ParseColorFlexible(string s)
    {
        s = (s ?? "").Trim();
    
        if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) 
            && n >= 0 && n <= 15)
            return PaperValueToColor(n);

        var parts = s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length is 3 or 4)
        {
            if (byte.TryParse(parts[0], out var r) &&
                byte.TryParse(parts[1], out var g) &&
                byte.TryParse(parts[2], out var b))
            {
                byte a = parts.Length == 4 && byte.TryParse(parts[3], out var aa) ? aa : (byte)255;
                return Color.FromArgb(a, r, g, b);
            }
        }
        

        try { return Color.Parse(s); }
        catch (Exception ex) { throw new FormatException($"Ogiltigt färgvärde: '{s}'", ex); }
    }
    
    private static Color PaperValueToColor(object v)
    {
        if (v is string s)
            return ParseColorFlexible(s);
        
        int n = 0;
        try {
            n = (int)Math.Round(Convert.ToDouble(v, CultureInfo.InvariantCulture));
        } catch { /* Ignorera fel och använd 0 (svart) */ }

        return n switch
        {
            0  => Color.FromRgb(0x00, 0x00, 0x00), // Black
            1  => Color.FromRgb(0xFF, 0xFF, 0xFF), // White
            2  => Color.FromRgb(0x88, 0x00, 0x00), // Red
            3  => Color.FromRgb(0xAA, 0xFF, 0xEE), // Cyan
            4  => Color.FromRgb(0xCC, 0x44, 0xCC), // Purple
            5  => Color.FromRgb(0x00, 0xCC, 0x55), // Green
            6  => Color.FromRgb(0x00, 0x00, 0xAA), // Blue
            7  => Color.FromRgb(0xEE, 0xEE, 0x77), // Yellow
            8  => Color.FromRgb(0xDD, 0x88, 0x55), // Orange
            9  => Color.FromRgb(0x66, 0x44, 0x00), // Brown
            10 => Color.FromRgb(0xFF, 0x77, 0x77), // Light red
            11 => Color.FromRgb(0x33, 0x33, 0x33), // Dark grey
            12 => Color.FromRgb(0x77, 0x77, 0x77), // Grey
            13 => Color.FromRgb(0xAA, 0xFF, 0x66), // Light green
            14 => Color.FromRgb(0x00, 0x88, 0xFF), // Light blue
            15 => Color.FromRgb(0xBB, 0xBB, 0xBB), // Light grey
            _  => Color.FromRgb(0x00, 0x00, 0x00)
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
    
    private sealed class FunctionDefinition
    {
        public required string Name;
        public required List<string> Parameters;
        public required int StartPc;
        public required int EndPc;
    }

    private sealed class FunctionCallFrame
    {
        public required int ReturnPc;
        public required Dictionary<string, object> SavedVariables;
    }
    
    private sealed class ProcDefinition
    {
        public required string Name;
        public required List<string> Parameters;
        public required int StartPc;  // raden efter PROC-deklarationen
        public required int EndPc;    // raden med END PROC
    }

    private sealed class ProcCallFrame
    {
        public required int ReturnPc;                          // dit vi hoppar tillbaka
        public required Dictionary<string, object> SavedVars; // parametrar att återställa
    }

    
    
    private static readonly Random _rng = new();
    private static IntPtr _currentXmpContext = IntPtr.Zero;

    private static System.Diagnostics.Process? _currentMusicProcess; // Musik-kanalen
    
    // ✅ NYTT: Hjälpfunktion för att extrahera #X prefix
    private static (int? tempScreen, string cleanArg) ExtractScreenPrefix(string arg)
    {
        if (string.IsNullOrWhiteSpace(arg))
            return (null, arg);

        arg = arg.Trim();

        bool inString = false;

        for (int i = 0; i < arg.Length; i++)
        {
            char c = arg[i];

            // Växla strängläge
            if (c == '"')
            {
                inString = !inString;
                continue;
            }

            // Vi bryr oss bara om # utanför strängar
            if (!inString && c == '#')
            {
                int start = i;
                int j = i + 1;

                // Måste följas av minst en siffra
                while (j < arg.Length && char.IsDigit(arg[j]))
                    j++;

                if (j > i + 1) // vi hittade siffror
                {
                    string numberStr = arg.Substring(i + 1, j - (i + 1));

                    if (int.TryParse(numberStr, out int screenNum))
                    {
                        // Hoppa över eventuell whitespace
                        while (j < arg.Length && char.IsWhiteSpace(arg[j]))
                            j++;

                        // Hoppa över eventuell komma
                        if (j < arg.Length && arg[j] == ',')
                            j++;

                        // Ta bort prefixet
                        string cleanArg = arg.Remove(start, j - start).Trim();

                        return (screenNum, cleanArg);
                    }
                }
            }
        }

        return (null, arg);
    }
    
    private sealed class FileChannel
    {
        public int Channel { get; set; }
        public string FilePath { get; set; }
        public FileMode Mode { get; set; }  // Input, Output, Append
        public StreamReader? Reader { get; set; }
        public StreamWriter? Writer { get; set; }
        public bool IsOpen { get; set; }
    
        public FileChannel(int channel, string path, FileMode mode)
        {
            Channel = channel;
            FilePath = path;
            Mode = mode;
            IsOpen = true;
        }
    }

    public enum FileMode
    {
        Input,   // Läsning
        Output,  // Skrivning (skapar ny/skriver över)
        Append   // Lägg till i slutet
    }

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
        var animationManager = new AnimationManager();
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
        
        var functions = new Dictionary<string, FunctionDefinition>(StringComparer.OrdinalIgnoreCase);
        var functionCallStack = new Stack<FunctionCallFrame>();
        var functionScanStack = new Stack<int>();
        
        var procs = new Dictionary<string, ProcDefinition>(StringComparer.OrdinalIgnoreCase);
        var procScanStack = new Stack<int>();
        var procCallStack = new Stack<ProcCallFrame>();
        
        var lines = programText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        _lastFrameTime = DateTime.MinValue;
        
        GamepadManager.Start();
        token.Register(() =>
        {
            GamepadManager.Stop();
            ChiptuneSynth.StopMusic();
        });
        
        // === EXPANDERA INLINE-IF TILL MULTI-RAD ===
        // "IF X THEN CMD1 : CMD2"  →  "IF X THEN\nCMD1\nCMD2\nENDIF"
        lines = ExpandInlineIf(lines);

        // Hjälpfunktion (placeras som static method i klassen):
        static string[] ExpandInlineIf(string[] input)
        {
            var result = new List<string>();
            foreach (var raw in input)
            {
                var stripped = StripLeadingLineNumber(StripComments(raw)).Trim();
                var upper = stripped.ToUpperInvariant();

                // Är det en inline-IF? (dvs. har THEN följt av faktiska kommandon)
                if (upper.StartsWith("IF "))
                {
                    int thenIdx = IndexOfWord(upper, "THEN");
                    if (thenIdx >= 0)
                    {
                        var afterThen = stripped[(thenIdx + 4)..].Trim();
                        // Om det finns något efter THEN → inline-IF
                        if (!string.IsNullOrEmpty(afterThen))
                        {
                            // Extrahera villkorsdelen
                            var condition = stripped[..thenIdx].Trim(); // "IF X > 5"
                    
                            // Kolla om det finns ELSE
                            var thenCmds = afterThen;
                            string? elseCmds = null;
                    
                            int elseIdx = IndexOfWordOutsideQuotes(afterThen, "ELSE");
                            if (elseIdx >= 0)
                            {
                                thenCmds = afterThen[..elseIdx].Trim();
                                elseCmds = afterThen[(elseIdx + 4)..].Trim();
                            }

                            // Bygg expanded IF-block
                            result.Add(condition + " THEN"); // "IF X > 5 THEN"
                            foreach (var cmd in SplitMultipleCommands(thenCmds))
                                if (!string.IsNullOrWhiteSpace(cmd))
                                    result.Add(cmd.Trim());
                    
                            if (elseCmds != null)
                            {
                                result.Add("ELSE");
                                foreach (var cmd in SplitMultipleCommands(elseCmds))
                                    if (!string.IsNullOrWhiteSpace(cmd))
                                        result.Add(cmd.Trim());
                            }
                    
                            result.Add("ENDIF");
                            continue;
                        }
                    }
                }
        
                result.Add(raw); // Alla andra rader oförändrade
            }
            return result.ToArray();
        }
        
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

        // --- FELHANTERING STATE ---
        // 0 = Break (Default), 1 = Resume Next, 2 = Goto Label
        int errorMode = 0; 
        int errorGotoPc = 0;
        
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
        var dataAreas = new Dictionary<string, List<object>>(StringComparer.OrdinalIgnoreCase);
        
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
                    var indicesStr = leftSide[(openParen + 1)..closeParen];

                    if (vars.TryGetValue(arrayName, out var aVal) && aVal is IAmosArray array)
                    {
                        var indexParts = SplitTopLevelCsv(indicesStr);
                        var indices = new int[indexParts.Count];
            
                        for (int i = 0; i < indexParts.Count; i++)
                        {
                            var rawIdx = EvalValue(indexParts[i].Trim(), vars, ln, getInkey, isKeyDown, graphics);
                            indices[i] = (int)Math.Round(Convert.ToDouble(rawIdx, CultureInfo.InvariantCulture));
                        }

                        array.Set(value, indices);
                        return;
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
            

            var scanLine = StripLeadingLineNumber(StripComments(rawLine)).Trim();
            var upperScan = scanLine.ToUpperInvariant();

            // FUNCTION scan
            if (upperScan.StartsWith("FUNCTION "))
            {
                functionScanStack.Push(i);
    
                var funcDecl = scanLine.Substring(9).Trim();
                var parenIdx = funcDecl.IndexOf('(');
    
                string funcName;
                var parameters = new List<string>();
    
                if (parenIdx > 0)
                {
                    funcName = funcDecl[..parenIdx].Trim();
                    var closeParenIdx = funcDecl.IndexOf(')');
                    if (closeParenIdx > parenIdx)
                    {
                        var paramStr = funcDecl[(parenIdx + 1)..closeParenIdx].Trim();
                        if (!string.IsNullOrWhiteSpace(paramStr))
                        {
                            parameters.AddRange(
                                paramStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            );
                        }
                    }
                }
                else
                {
                    funcName = funcDecl.Trim();
                }
    
                functions[funcName] = new FunctionDefinition
                {
                    Name = funcName,
                    Parameters = parameters,
                    StartPc = i + 1,
                    EndPc = -1
                };
            }
            else if (upperScan == "END FUNCTION" || upperScan == "ENDFUNCTION")
            {
                if (functionScanStack.Count == 0)
                    throw new Exception($"END FUNCTION without FUNCTION at line {i + 1}");
    
                var funcPc = functionScanStack.Pop();
                var funcDef = functions.Values.FirstOrDefault(f => f.StartPc == funcPc + 1);
                if (funcDef != null)
                {
                    funcDef.EndPc = i;
                }
            }
            
            // PROC scan
            if (upperScan.StartsWith("PROC "))
            {
                procScanStack.Push(i);

                var procDecl = scanLine[5..].Trim(); // efter "PROC "
                var parenIdx = procDecl.IndexOf('(');

                string procName;
                var parameters = new List<string>();

                if (parenIdx > 0)
                {
                    procName = procDecl[..parenIdx].Trim();
                    var closeIdx = procDecl.IndexOf(')');
                    if (closeIdx > parenIdx)
                    {
                        var paramStr = procDecl[(parenIdx + 1)..closeIdx].Trim();
                        if (!string.IsNullOrWhiteSpace(paramStr))
                            parameters.AddRange(
                                paramStr.Split(',',
                                    StringSplitOptions.RemoveEmptyEntries |
                                    StringSplitOptions.TrimEntries));
                    }
                }
                else
                {
                    procName = procDecl.Trim();
                }

                procs[procName] = new ProcDefinition
                {
                    Name      = procName,
                    Parameters = parameters,
                    StartPc   = i + 1,
                    EndPc     = -1
                };
            }
            else if (upperScan == "END PROC" || upperScan == "ENDPROC")
            {
                if (procScanStack.Count == 0)
                    throw new Exception($"END PROC without PROC at line {i + 1}");

                var procPc = procScanStack.Pop();
                var procDef = procs.Values.FirstOrDefault(p => p.StartPc == procPc + 1);
                if (procDef != null)
                    procDef.EndPc = i;
            }
            
            // WHILE/WEND scan
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
            else if (upperScan == "ENDIF" || upperScan == "END IF")
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
        
        // Registrera functions som tillgängliga för ParseFactor
        vars["__functions__"] = functions;
        vars["__callFunction__"] = (Func<string, List<object>, int, object>)((funcName, args, line) => CallFunction(funcName, args, line));
            
        // Helper för synkront funktionsanrop
        // Ersätter hela den gamla CallFunction-metoden.
        // Kör funktionskroppen via samma switch som main-loopen 
        // genom att temporärt "flytta" PC till funktionen.
        // Anropas fortfarande synkront från ParseFactor via __callFunction__.
        // Returvärdet läggs i vars["__return_value__"] och returneras.
        object CallFunction(string funcName, List<object> argValues, int callerLine)
        {
            if (!functions.TryGetValue(funcName, out var funcDef))
                throw new Exception($"Unknown function: {funcName} at line {callerLine}");

            if (argValues.Count != funcDef.Parameters.Count)
                throw new Exception(
                    $"Function {funcName} expects {funcDef.Parameters.Count} " +
                    $"parameters, got {argValues.Count} at line {callerLine}");

            // Spara undan alla variabler som parametrarna skriver över
            var savedVars = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var param in funcDef.Parameters)
                if (vars.TryGetValue(param, out var old))
                    savedVars[param] = old;

            // Sätt parametrar
            for (int i = 0; i < funcDef.Parameters.Count; i++)
                setVar(funcDef.Parameters[i], argValues[i]);

            // Spara undan __return_value__ om den råkar finnas
            object? savedReturn = vars.TryGetValue("__return_value__", out var rv) ? rv : null;
            vars["__return_value__"] = 0.0;

            // === Kör funktionskroppen via en inbäddad loop som delar
            //     samma vars, labels, ifJumps, whileMap osv. ===
            int funcPc = funcDef.StartPc;

            while (funcPc <= funcDef.EndPc && funcPc < lines.Length)
            {
                var rawFuncLine = StripComments((lines[funcPc] ?? "").Trim());

                // Hoppa över tomma rader och labels
                if (string.IsNullOrWhiteSpace(rawFuncLine) || rawFuncLine.EndsWith(':'))
                {
                    funcPc++;
                    continue;
                }

                rawFuncLine = StripLeadingLineNumber(rawFuncLine);
                var funcCmds = SplitMultipleCommands(rawFuncLine);
                bool funcJump = false;

                foreach (var fc in funcCmds)
                {
                    var trimFc = fc.Trim();
                    if (string.IsNullOrEmpty(trimFc)) continue;
                    var (fcmd, farg) = SplitCommand(trimFc);

                    // RETURN [värde] — avsluta funktionen
                    if (fcmd == "RETURN")
                    {
                        if (!string.IsNullOrWhiteSpace(farg))
                            vars["__return_value__"] =
                                EvalValue(farg, vars, funcPc + 1, getInkey, isKeyDown, graphics);
                        goto func_done;
                    }

                    // IF i funktionskroppen — använd ifJumps-kartan som byggdes i pre-scan
                    if (fcmd == "IF")
                    {
                        int tIdx = IndexOfWord(farg, "THEN");
                        string cond = tIdx >= 0 ? farg[..tIdx].Trim() : farg;
                        bool condResult = EvalCondition(cond, vars, funcPc + 1, getInkey, isKeyDown, graphics);
                        if (!condResult)
                        {
                            if (ifJumps.TryGetValue(funcPc, out var skipTo))
                            {
                                funcPc = skipTo;
                                funcJump = true;
                            }
                        }
                        if (funcJump) break;
                        continue;
                    }

                    // ELSE / ENDIF — hoppa förbi resten av IF-blocket
                    if (fcmd == "ELSE")
                    {
                        if (elseJumps.TryGetValue(funcPc, out var skipTo))
                        {
                            funcPc = skipTo;
                            funcJump = true;
                        }
                        break;
                    }
                    if (fcmd == "ENDIF") { /* bara markör */ continue; }

                    // WHILE / WEND i funktionen
                    if (fcmd == "WHILE")
                    {
                        bool wCond = EvalCondition(farg, vars, funcPc + 1, getInkey, isKeyDown, graphics);
                        if (!wCond)
                        {
                            if (whileMap.TryGetValue(funcPc, out var wendPc))
                            {
                                funcPc = wendPc + 1;
                                funcJump = true;
                            }
                        }
                        else
                        {
                            whileStack.Push(new WhileFrame
                            {
                                WhilePc = funcPc,
                                WendPc = whileMap.TryGetValue(funcPc, out var wp) ? wp : 0,
                                Line = funcPc + 1,
                                Condition = farg
                            });
                        }
                        if (funcJump) break;
                        continue;
                    }

                    if (fcmd == "WEND")
                    {
                        if (whileStack.Count > 0)
                        {
                            var wf = whileStack.Peek();
                            var wLine = StripLeadingLineNumber(StripComments(lines[wf.WhilePc]));
                            var wCond = wLine.Substring(5).Trim();
                            if (EvalCondition(wCond, vars, funcPc + 1, getInkey, isKeyDown, graphics))
                            {
                                funcPc = wf.WhilePc;
                                funcJump = true;
                            }
                            else whileStack.Pop();
                        }
                        if (funcJump) break;
                        continue;
                    }

                    // FOR / NEXT i funktionen
                    if (fcmd == "FOR")
                    {
                        var eqI = farg.IndexOf('=');
                        var fV = farg[..eqI].Trim();
                        var rhs = farg[(eqI + 1)..].Trim();
                        var toI = IndexOfWord(rhs, "TO");
                        var startV = EvalInt(rhs[..toI].Trim(), vars, funcPc + 1, getInkey, isKeyDown, graphics);
                        var restV = rhs[(toI + 2)..].Trim();
                        var stI = IndexOfWord(restV, "STEP");
                        var endV = EvalInt(stI < 0 ? restV : restV[..stI].Trim(), vars, funcPc + 1, getInkey, isKeyDown, graphics);
                        var stepV = stI < 0 ? 1 : EvalInt(restV[(stI + 4)..].Trim(), vars, funcPc + 1, getInkey, isKeyDown, graphics);
                        setVar(fV, startV);
                        forStack.Push(new ForFrame { VarName = fV, EndValue = endV, StepValue = stepV, LineAfterForPc = funcPc + 1, ForLineNumber = funcPc + 1 });
                        continue;
                    }

                    if (fcmd == "NEXT")
                    {
                        if (forStack.Count > 0)
                        {
                            var ff = forStack.Peek();
                            var cur = GetDoubleVar(ff.VarName, vars, funcPc + 1) + ff.StepValue;
                            setVar(ff.VarName, cur);
                            bool done = ff.StepValue > 0 ? cur > ff.EndValue + 0.000001 : cur < ff.EndValue - 0.000001;
                            if (!done) { funcPc = ff.LineAfterForPc; funcJump = true; }
                            else forStack.Pop();
                        }
                        if (funcJump) break;
                        continue;
                    }

                    // INC / DEC
                    if (fcmd == "INC") { setVar(farg, GetDoubleVar(farg, vars, funcPc + 1) + 1.0); continue; }
                    if (fcmd == "DEC") { setVar(farg, GetDoubleVar(farg, vars, funcPc + 1) - 1.0); continue; }

                    // Tilldelning (X = ...) och allt annat — skicka via EvalValue
                    if (trimFc.Contains('='))
                    {
                        var (leftSide, varValue) = SplitAssignment(trimFc);
                        setVar(leftSide, EvalValue(varValue, vars, funcPc + 1, getInkey, isKeyDown, graphics));
                        continue;
                    }

                    // Okänt kommando i funktion — kasta fel
                    throw new Exception($"Unsupported command '{fcmd}' inside FUNCTION at line {funcPc + 1}. " +
                                        $"Use subroutines (GOSUB) for complex side-effects.");
                }

                if (!funcJump) funcPc++;
            }

            func_done:
            // Återställ parametrar
            foreach (var param in funcDef.Parameters)
            {
                if (savedVars.TryGetValue(param, out var old))
                    vars[param] = old;
                else
                    vars.Remove(param);
            }

            // Hämta returvärde och återställ ev. sparat __return_value__
            var result = vars.TryGetValue("__return_value__", out var retVal) ? retVal : 0.0;
            if (savedReturn != null)
                vars["__return_value__"] = savedReturn;
            else
                vars.Remove("__return_value__");

            return result;
        }
        
        int pc = 0;
        while (pc < lines.Length) {
            token.ThrowIfCancellationRequested();
            
            // Hoppa över tomma rader vid stepping
            while (pc < lines.Length && IsEmptyLine(lines[pc]))
            {
                pc++;
            }
                
            if (pc >= lines.Length) break;
            
            var line = StripComments((lines[pc] ?? "").Trim());
            if (string.IsNullOrWhiteSpace(line) || line.EndsWith(':')) { pc++; continue; }
            line = StripLeadingLineNumber(line).Trim();

            // Jump over comments
            var upperCheck = line.ToUpperInvariant();
            if (upperCheck == "REM" || upperCheck.StartsWith("REM ")) { pc++; continue; }

            await waitForStep(pc);          // stannar aldrig på REM
            var ln = pc + 1;
            
            var commands = SplitMultipleCommands(line);
            bool jumpHappened = false;

            foreach (var fullCmd in commands) {
                try
                {
                    var trimmedCmd = fullCmd.Trim();
                    if (string.IsNullOrEmpty(trimmedCmd)) continue;
                    var (cmd, arg) = SplitCommand(trimmedCmd);

                    switch (cmd)
                    {
                        case "RETURN":
                            if (functionCallStack.Count > 0)
                            {
                                var frame = functionCallStack.Pop();
                                foreach (var kvp in frame.SavedVariables)
                                    vars[kvp.Key] = kvp.Value;
                                pc = frame.ReturnPc;
                                jumpHappened = true;
                            }
                            else if (gosubStack.Count > 0)
                            {
                                pc = gosubStack.Pop();
                                jumpHappened = true;
                            }
                            else
                            {
                                throw new Exception($"RETURN without GOSUB or FUNCTION at line {ln}");
                            }

                            break;

                        case "FUNCTION":
                            // Hoppa över funktionsdefinitioner
                        {
                            var funcDef = functions.Values.FirstOrDefault(f => f.StartPc == pc + 1);
                            if (funcDef != null && funcDef.EndPc > 0)
                            {
                                pc = funcDef.EndPc + 1;
                                jumpHappened = true;
                            }
                        }
                            break;

                        case "ENDFUNCTION":
                            // Implicit return
                            if (functionCallStack.Count > 0)
                            {
                                var frame = functionCallStack.Pop();
                                foreach (var kvp in frame.SavedVariables)
                                    vars[kvp.Key] = kvp.Value;
                                pc = frame.ReturnPc;
                                jumpHappened = true;
                            }

                            break;

                        case "PROC":
                        {
                            // Hoppa över proc-definitionen vid körning
                            var procName = arg.Split('(')[0].Trim();
                            if (procs.TryGetValue(procName, out var pd) && pd.EndPc > 0)
                            {
                                pc = pd.EndPc + 1;
                                jumpHappened = true;
                            }

                            break;
                        }

                        case "ENDPROC":
                        case "END PROC": // hanteras via "END"-casen nedan, se steg 4
                        {
                            // Implicit END PROC — återvänd till anroparen
                            if (procCallStack.Count > 0)
                            {
                                var frame = procCallStack.Pop();
                                // Återställ parametrar
                                foreach (var kvp in frame.SavedVars)
                                    vars[kvp.Key] = kvp.Value;
                                pc = frame.ReturnPc;
                                jumpHappened = true;
                            }

                            break;
                        }

                        case "ON":
                            var onArg = arg.ToUpperInvariant();
                            if (onArg.StartsWith("ERROR GOTO "))
                            {
                                var labelName = arg[11..].Trim(); // "ERROR GOTO ".Length == 11
                                // ÄNDRAT: targetPc -> errorTargetPc för att undvika namnkonflikt med GOTO
                                if (labels.TryGetValue(labelName, out var errorTargetPc))
                                {
                                    errorMode = 2; // Goto
                                    errorGotoPc = errorTargetPc;
                                }
                                else
                                {
                                    throw new Exception($"Label '{labelName}' not found for ON ERROR GOTO");
                                }
                            }
                            else if (onArg == "ERROR RESUME NEXT")
                            {
                                errorMode = 1; // Resume Next
                            }
                            else if (onArg == "ERROR BREAK" || onArg == "ERROR END")
                            {
                                errorMode = 0; // Break/Crash
                            }

                            break;

                        case "CLS":
                            await appendLineAsync("@@CLS");
                            await clearAsync();
                            break;

                        case "CLSG2":
                            // Rensa både grafik och text-cursor
                            await clearAsync(); // Om du vill rensa loggen/text-boxen också, annars ta bort
                            graphics.Clear(graphics.PaperColor); // Använd paper color som bakgrund
                            graphics.Locate(0, 0);
                            break;

                        case "CLSG":
                        {
                            var (tempScreen, cleanArg) = ExtractScreenPrefix(arg);
                            int? savedScreen = null;
                                
                            if (tempScreen.HasValue)
                            {
                                savedScreen = graphics.GetActiveScreenNumber();
                                graphics.SetDrawingScreen(tempScreen.Value);
                                graphics.Clear(Colors.Transparent);
                                graphics.SetDrawingScreen(savedScreen.Value);
                            }
                            else if (!string.IsNullOrWhiteSpace(cleanArg))
                            {
                                // Gammalt beteende: CLSG 1 (utan #)
                                savedScreen = graphics.GetActiveScreenNumber();
                                graphics.SetDrawingScreen(EvalInt(cleanArg, vars, ln, getInkey, isKeyDown, graphics));
                                graphics.Clear(Colors.Transparent);
                                graphics.SetDrawingScreen(savedScreen.Value);
                            }
                            else
                            {
                                // Ingen parameter: rensa aktivt lager
                                graphics.Clear(Colors.Transparent);
                            }
                                
                            graphics.Locate(0, 0);
                            onGraphicsChanged();
                            break;
                        }
                        case "CLSGA":
                            var x2 = graphics.GetActiveScreenNumber();
                            if (!string.IsNullOrWhiteSpace(arg))
                            {
                                // Om ett argument skickades med, välj det lagret först
                                graphics.SetDrawingScreen(EvalInt(arg, vars, ln, getInkey, isKeyDown, graphics));
                            }

                            graphics.ClearAll(Colors.Transparent);
                            graphics.SetDrawingScreen(x2);
                            graphics.Locate(0, 0);
                            onGraphicsChanged();
                            break;
                        case "PRINT":
                        {
                            var printArg = arg.Trim();

                            // Kolla om det är PRINT #channel (fil-output)
                            if (printArg.StartsWith("#"))
                            {
                                // PRINT #channel, data
                                int commaIdx2 = printArg.IndexOf(',');
                                if (commaIdx2 < 0)
                                    throw new Exception("PRINT #: Syntax är PRINT #channel, data");

                                string channelExpr = printArg[1..commaIdx2].Trim(); // Ta bort # och läs kanalnummer
                                int channel = EvalInt(channelExpr, vars, ln, getInkey, isKeyDown, graphics);

                                if (!_openFiles.ContainsKey(channel))
                                    throw new Exception($"PRINT #: Kanal {channel} är inte öppen");

                                var fileChannel = _openFiles[channel];

                                if (fileChannel.Mode == FileMode.Input)
                                    throw new Exception(
                                        $"PRINT #: Kanal {channel} är öppnad för läsning, inte skrivning");

                                if (fileChannel.Writer == null)
                                    throw new Exception($"PRINT #: Kanal {channel} har ingen writer");

                                // Läs data att skriva
                                string dataExpr = printArg[(commaIdx2 + 1)..].Trim();

                                // Kolla om det slutar med semikolon (ingen nyrad)
                                bool addNewLine = true;
                                if (dataExpr.EndsWith(";"))
                                {
                                    addNewLine = false;
                                    dataExpr = dataExpr[..^1].Trim();
                                }

                                if (!string.IsNullOrWhiteSpace(dataExpr))
                                {
                                    var valToPrint = EvalValue(dataExpr, vars, ln, getInkey, isKeyDown, graphics);
                                    string output = ValueToString(valToPrint);

                                    if (addNewLine)
                                        fileChannel.Writer.WriteLine(output);
                                    else
                                        fileChannel.Writer.Write(output);

                                    fileChannel.Writer.Flush(); // Säkerställ att data skrivs direkt
                                }
                                else if (addNewLine)
                                {
                                    // Tom PRINT #channel för nyrad
                                    fileChannel.Writer.WriteLine();
                                    fileChannel.Writer.Flush();
                                }
                            }
                            // Vanlig PRINT till skärm
                            else if (printArg.StartsWith("AT ", StringComparison.OrdinalIgnoreCase))
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

                        case "PRINTG":
                        {
                            var (tempScreen, cleanArg) = ExtractScreenPrefix(arg);
                            int? savedScreen = null;
                                
                            if (tempScreen.HasValue)
                            {
                                savedScreen = graphics.GetActiveScreenNumber();
                                graphics.SetDrawingScreen(tempScreen.Value);
                            }
                            
                            var printArg = cleanArg.Trim();

                            // Hantera PRINT AT x,y, "text"
                            if (printArg.StartsWith("AT ", StringComparison.OrdinalIgnoreCase))
                            {
                                var at = ParsePrintAtArguments(printArg);
                                int row = EvalInt(at.RowExpr, vars, ln, getInkey, isKeyDown, graphics);
                                int col = EvalInt(at.ColExpr, vars, ln, getInkey, isKeyDown, graphics);
                                graphics.Locate(row,
                                    col); // Notera: x=row? AMOS kör ofta Y,X i Locate men X,Y i text. Dubbelkolla ordningen.
                                // LOCATE X,Y brukar vara Kolumn, Rad.

                                if (!string.IsNullOrWhiteSpace(at.RestExpr))
                                {
                                    var valToPrint = EvalValue(at.RestExpr, vars, ln, getInkey, isKeyDown, graphics);
                                    graphics.ConsolePrint(ValueToString(valToPrint));
                                }
                            }
                            else
                            {
                                // Vanlig PRINT
                                // Kolla om det slutar med , för att undvika nyrad
                                bool newLine = true;
                                if (printArg.EndsWith(","))
                                {
                                    newLine = false;
                                    printArg = printArg[..^1];
                                }

                                var valToPrint = EvalValue(printArg, vars, ln, getInkey, isKeyDown, graphics);
                                graphics.ConsolePrint(ValueToString(valToPrint), newLine);
                            }
                            
                            if (savedScreen.HasValue)
                                graphics.SetDrawingScreen(savedScreen.Value);
                            
                            // Trigga uppdatering av fönstret
                            onGraphicsChanged();
                        }
                            break;
                        case "INPUT":
                        {
                            string inputArg = arg.Trim();

                            // Kolla om det är INPUT #channel (fil-input)
                            if (inputArg.StartsWith("#"))
                            {
                                // INPUT #channel, variable
                                int commaIdx = inputArg.IndexOf(',');
                                if (commaIdx < 0)
                                    throw new Exception("INPUT #: Syntax är INPUT #channel, variabel");

                                string channelExpr = inputArg[1..commaIdx].Trim();
                                int channel = EvalInt(channelExpr, vars, ln, getInkey, isKeyDown, graphics);

                                if (!_openFiles.ContainsKey(channel))
                                    throw new Exception($"INPUT #: Kanal {channel} är inte öppen");

                                var fileChannel = _openFiles[channel];

                                if (fileChannel.Mode != FileMode.Input)
                                    throw new Exception($"INPUT #: Kanal {channel} är inte öppnad för läsning");

                                if (fileChannel.Reader == null)
                                    throw new Exception($"INPUT #: Kanal {channel} har ingen reader");

                                string varName = inputArg[(commaIdx + 1)..].Trim();

                                // Läs en rad från filen
                                string? line2 = fileChannel.Reader.ReadLine();

                                if (line2 == null)
                                {
                                    // End of file - sätt tom sträng eller 0
                                    if (varName.EndsWith("$"))
                                        setVar(varName, "");
                                    else
                                        setVar(varName, 0);
                                }
                                else
                                {
                                    // Försök konvertera till nummer om det är numerisk variabel
                                    if (varName.EndsWith("$"))
                                    {
                                        setVar(varName, line2);
                                    }
                                    else
                                    {
                                        // Numerisk variabel
                                        if (double.TryParse(line2.Trim(), NumberStyles.Any,
                                                CultureInfo.InvariantCulture, out double numValue))
                                            setVar(varName, numValue);
                                        else
                                            setVar(varName, 0); // Default till 0 om konvertering misslyckas
                                    }
                                }
                            }
                            // Vanlig INPUT från användare
                            else
                            {
                                string promptInput = "";
                                string varNameInput = "A$";

                                // Hitta kommat som INTE är inuti citattecken
                                int commaIdx = -1;
                                bool inQuotes = false;
                                for (int i = 0; i < inputArg.Length; i++)
                                {
                                    if (inputArg[i] == '\"') inQuotes = !inQuotes;
                                    if (!inQuotes && inputArg[i] == ',')
                                    {
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
                            }
                        }
                            break;

                        case "INPUTG":
                        {
                            string iArg = arg.Trim();
                            string pPrompt = "? ";
                            string vName = "";

                            // 1. Parsa argument
                            int cIdx = -1;
                            bool inQ = false;
                            for (int i = 0; i < iArg.Length; i++)
                            {
                                if (iArg[i] == '\"') inQ = !inQ;
                                if (!inQ && iArg[i] == ',')
                                {
                                    cIdx = i;
                                    break;
                                }
                            }

                            if (cIdx >= 0)
                            {
                                pPrompt = Unquote(iArg[..cIdx].Trim());
                                vName = iArg[(cIdx + 1)..].Trim();
                            }
                            else
                            {
                                vName = iArg;
                            }

                            // 2. Skriv prompt och tvinga cursor-uppdatering
                            if (!string.IsNullOrEmpty(pPrompt))
                            {
                                graphics.ConsolePrint(pPrompt, newLine: false);
                                onGraphicsChanged();
                            }

                            var inputBuffer = new System.Text.StringBuilder();
                            while (!string.IsNullOrEmpty(getInkey()))
                            {
                            }

                            // Variabler för repeat-logik
                            string lastKey = null;
                            DateTime lastKeyTime = DateTime.MinValue;
                            int currentDelay = 500;

                            bool done = false;
                            while (!done)
                            {
                                token.ThrowIfCancellationRequested();
                                string key = getInkey();

                                // --- Repeat ---
                                if (string.IsNullOrEmpty(key))
                                {
                                    lastKey = null;
                                    await Task.Delay(5, token);
                                    continue;
                                }

                                if (key == lastKey)
                                {
                                    if ((DateTime.Now - lastKeyTime).TotalMilliseconds < currentDelay)
                                    {
                                        await Task.Delay(5, token);
                                        continue;
                                    }

                                    currentDelay = 50;
                                }
                                else
                                {
                                    currentDelay = 500;
                                }

                                lastKey = key;
                                lastKeyTime = DateTime.Now;
                                // --------------

                                string uKey = key.ToUpperInvariant();

                                // ENTER
                                if (uKey == "RETURN" || uKey == "ENTER" || key == "\r" || key == "\n")
                                {
                                    done = true;
                                    graphics.ConsolePrint("");
                                    onGraphicsChanged();
                                }
                                // BACKSPACE
                                else if (uKey == "BACK" || uKey == "BACKSPACE" || key == "\b" ||
                                         (key.Length > 0 && key[0] == 8))
                                {
                                    if (inputBuffer.Length > 0)
                                    {
                                        inputBuffer.Length--;
                                        int cx = graphics.CursorX;
                                        int cy = graphics.CursorY;
                                        if (cx > 0)
                                        {
                                            // Sudda med bakgrundsfärg (Bar)
                                            int cw = graphics.CharWidth;
                                            int ch = graphics.CharHeight;
                                            graphics.Bar((cx - 1) * cw, cy * ch, cx * cw - 1, cy * ch + ch - 1,
                                                graphics.PaperColor);
                                            graphics.Locate(cx - 1, cy);
                                            onGraphicsChanged();
                                        }
                                    }
                                }
                                // SPACE
                                else if (uKey == "SPACE" || key == " ")
                                {
                                    inputBuffer.Append(' ');
                                    graphics.ConsolePrint(" ", newLine: false);
                                    onGraphicsChanged();
                                }
                                // TECKEN (A-Z, 0-9)
                                else if (key.Length == 1 && !char.IsControl(key[0]))
                                {
                                    // Kolla Shift status via isKeyDown
                                    bool isShift = isKeyDown("LeftShift") || isKeyDown("RightShift") ||
                                                   isKeyDown("Shift");

                                    string finalChar = key;

                                    if (char.IsLetter(key[0]))
                                    {
                                        // Om bokstav: Shift = Stor, Ingen Shift = Liten
                                        finalChar = isShift ? key.ToUpperInvariant() : key.ToLowerInvariant();
                                    }
                                    else if (char.IsDigit(key[0]) && isShift)
                                    {
                                        // Enkel mapping för siffror + shift (kan behöva justeras för SE/US layout)
                                        finalChar = key[0] switch
                                        {
                                            '1' => "!", '2' => "\"", '3' => "#", '4' => "¤", '5' => "%",
                                            '6' => "&", '7' => "/", '8' => "(", '9' => ")", '0' => "=",
                                            _ => key
                                        };
                                    }
                                    // Hantera punkt och kommma om de kommer in som råa tecken
                                    else if (key == "." && isShift) finalChar = ":";
                                    else if (key == "," && isShift) finalChar = ";";

                                    inputBuffer.Append(finalChar);
                                    graphics.ConsolePrint(finalChar, newLine: false);
                                    onGraphicsChanged();
                                }
                            }

                            while (!string.IsNullOrEmpty(getInkey()))
                            {
                                await Task.Delay(10);
                            }

                            setVar(vName, inputBuffer.ToString());
                        }
                            break;

                        case "PAPER":
                        {
                            Color c2;
                            try
                            {
                                c2 = ParseColorFlexible(Unquote(arg));
                            }
                            catch
                            {
                                c2 = PaperValueToColor(EvalValue(arg, vars, ln, getInkey, isKeyDown, graphics));
                            }

                            // Skicka till UI-tråden via console-pipeline (AppendConsoleLineAsync)
                            await appendLineAsync("@@PAPER " + c2.ToString());
                            break;
                        }

                        case "PAPERG":
                        {
                            Color c;
                            // 1. Försök tolka som direkt färg/siffra först (t.ex. Red, #FF0000, 1)
                            try
                            {
                                c = ParseColorFlexible(Unquote(arg));
                            }
                            // 2. Om det misslyckas, utvärdera som variabel/uttryck (t.ex. I, A$, 10+5)
                            catch
                            {
                                c = PaperValueToColor(EvalValue(arg, vars, ln, getInkey, isKeyDown, graphics));
                            }

                            graphics.PaperColor = c;
                            break;
                        }
                        case "INK":
                        {
                            Color c;
                            try
                            {
                                c = ParseColorFlexible(Unquote(arg));
                            }
                            catch
                            {
                                c = PaperValueToColor(EvalValue(arg, vars, ln, getInkey, isKeyDown, graphics));
                            }


                            await appendLineAsync("@@INK " + c.ToString());
                            break;
                        }
                        case "INKG":
                        {
                            Color c;
                            try
                            {
                                c = ParseColorFlexible(Unquote(arg));
                            }
                            catch
                            {
                                c = PaperValueToColor(EvalValue(arg, vars, ln, getInkey, isKeyDown, graphics));
                            }

                            graphics.Ink = c;
                            //graphics.SetInkColor(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f);
                            break;
                        }
                        case "LOCATE":
                            var lp2 = SplitCsvOrSpaces(arg);
                            await appendLineAsync(
                                $"@@LOCATE {EvalInt(lp2[0], vars, ln, getInkey, isKeyDown, graphics)} {EvalInt(lp2[1], vars, ln, getInkey, isKeyDown, graphics)}");
                            break;

                        case "LOCATEG":
                        {
                            var (tempScreen, cleanArg) = ExtractScreenPrefix(arg);
                            int? savedScreen = null;
                                
                            if (tempScreen.HasValue)
                            {
                                savedScreen = graphics.GetActiveScreenNumber();
                                graphics.SetDrawingScreen(tempScreen.Value);
                            }
                                
                            var lp = SplitCsvOrSpaces(cleanArg);
                            graphics.Locate(
                                EvalInt(lp[0], vars, ln, getInkey, isKeyDown, graphics),
                                EvalInt(lp[1], vars, ln, getInkey, isKeyDown, graphics)
                            );
                                
                            if (savedScreen.HasValue)
                                graphics.SetDrawingScreen(savedScreen.Value);
                            break;
                        }


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
                                    if (val is string s && double.TryParse(s, NumberStyles.Float,
                                            CultureInfo.InvariantCulture, out var d))
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
                            if (selectOwnerMarker.TryGetValue(pc, out var selPc) &&
                                selectMap.TryGetValue(selPc, out var sel))
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
                            if (arg.Equals("SELECT", StringComparison.OrdinalIgnoreCase))
                            {
                                // befintlig SELECT-logik...
                                break;
                            }

                            if (arg.Equals("PROC", StringComparison.OrdinalIgnoreCase))
                            {
                                if (procCallStack.Count > 0)
                                {
                                    var frame = procCallStack.Pop();
                                    foreach (var kvp in frame.SavedVars)
                                        vars[kvp.Key] = kvp.Value;
                                    pc = frame.ReturnPc;
                                    jumpHappened = true;
                                }

                                break;
                            }

                            if (arg.Equals("FUNCTION", StringComparison.OrdinalIgnoreCase))
                            {
                                // befintlig ENDFUNCTION-logik...
                                break;
                            }

                            if (arg.Equals("IF", StringComparison.OrdinalIgnoreCase))
                            {
                                break; // bara en markör, precis som ENDIF
                            }

                            return; // END utan argument avslutar programmet
                        }
                        case "REM": goto next_line;
                        case "GOTO":
                            if (labels.TryGetValue(arg, out var targetPc))
                            {
                                pc = targetPc;
                                jumpHappened = true;
                            }
                            else throw new Exception($"Label {arg} not found at line {ln}");

                            break;
                        case "GOSUB":
                            gosubStack.Push(pc + 1);
                            if (labels.TryGetValue(arg, out var subPc))
                            {
                                pc = subPc;
                                jumpHappened = true;
                            }
                            else throw new Exception($"Label {arg} not found at line {ln}");

                            break;
                        case "RETURN2":
                            if (gosubStack.Count > 0)
                            {
                                pc = gosubStack.Pop();
                                jumpHappened = true;
                            }
                            else throw new Exception($"RETURN without GOSUB at line {ln}");

                            break;
                        case "LET":
                            var (n, vt) = SplitAssignment(arg);
                            setVar(n, EvalValue(vt, vars, ln, getInkey, isKeyDown, graphics));
                            break;
                        case "DIM":
                        {
                            var eqIdx = arg.IndexOf('=');
                            string dimDecl = eqIdx >= 0 ? arg[..eqIdx].Trim() : arg;
                            string? initValues = eqIdx >= 0 ? arg[(eqIdx + 1)..].Trim() : null;

                            // Parse array namn och dimensioner: A(10,20,5)
                            int openParen = dimDecl.IndexOf('(');
                            int closeParen = dimDecl.LastIndexOf(')');

                            if (openParen == -1 || closeParen == -1)
                                throw new Exception($"DIM Syntax Error at line {ln}: Missing parentheses");

                            var arrName = dimDecl[..openParen].Trim();
                            var dimensionsStr = dimDecl[(openParen + 1)..closeParen].Trim();

                            // Split på komma och utvärdera varje dimension
                            var dimParts = SplitTopLevelCsv(dimensionsStr);
                            var dimensions = new int[dimParts.Count];

                            for (int i = 0; i < dimParts.Count; i++)
                            {
                                dimensions[i] = EvalInt(dimParts[i].Trim(), vars, ln, getInkey, isKeyDown, graphics);

                                if (dimensions[i] < 0)
                                    throw new Exception($"DIM Error at line {ln}: Dimension {i} cannot be negative");
                            }

                            // Skapa array baserat på typ ($ = sträng)
                            IAmosArray array;
                            if (arrName.EndsWith("$", StringComparison.Ordinal))
                                array = new AmosStringArray(dimensions);
                            else
                                array = new AmosNumericArray(dimensions);

                            vars[arrName] = array;

                            // Initialisering med värden om de finns
                            if (initValues != null)
                            {
                                InitializeArrayFromString(array, initValues, dimensions, vars, ln, getInkey, isKeyDown,
                                    graphics);
                            }
                            break;
                        }
                        case "WAIT":
                            if (arg.ToUpperInvariant() == "VBL")
                            {
                                // NYTT: Stega sprite-fades innan frame presenteras
                                graphics.TickSpriteFades();
                                graphics.TickLayerFades();
                                
                                // ✅ NYTT: Auto-uppdatera WAVE Time parameter
                                graphics.TickSpriteEffects(0.016f);
                                
                                // 1. Säkerställ att all ritning är klar
                                graphics.EndFrame();

                                // 2. Uppdatera timers för shader-animationer i den AKTIVA (synliga) framen
                                lock (graphics.LockObject)
                                {
                                    foreach (var layer in graphics.ActiveFrame)
                                    {
                                        layer.Timer += 0.016f;
                                    }
                                }
                                // 2. Animations
                                animationManager.Tick(graphics); 
                                
                                // 3. Vänta tills nästa frame är redo (timing)
                                await WaitNextFrameAsync(token);

                                // 4. Signalera att UI ska uppdateras
                                onGraphicsChanged();

                                // 5. Börja rita på nya inactive frame
                                graphics.BeginFrame();
                            }
                            else if (arg.ToUpperInvariant() == "MUSIC")
                            {
                                // Vänta tills alla musikkanaler är klara
                                while (ChiptuneSynth.IsAnyPlaying())
                                    await Task.Delay(10, token);
                            }
                            else if (arg.ToUpperInvariant().StartsWith("MUSIC "))
                            {
                                // WAIT MUSIC 1 — vänta på specifik kanal
                                int ch = EvalInt(arg[6..], vars, ln, getInkey, isKeyDown, graphics);
                                while (ChiptuneSynth.IsPlaying(ch))
                                    await Task.Delay(10, token);
                            }
                            else
                            {
                                int ms = Math.Max(0, EvalInt(arg, vars, ln, getInkey, isKeyDown, graphics));
                                await Task.Delay(ms, token);
                            }
                            break;
                        case "ANIM":
                        {
                            var animUpper = arg.TrimStart().ToUpperInvariant();

                            // --------------------------------------------------
                            //  ANIM DEF 1, "idle", (0,8)(1,8)(2,8), LOOP
                            //  ANIM DEF 1, "jump", (8,5)(9,5)(10,12), ONCE, "idle"
                            // --------------------------------------------------
                            if (animUpper.StartsWith("DEF "))
                            {
                                string defRest = arg.Substring(4).Trim();

                                // Hämta sprite-id (första token före första kommat)
                                int firstComma = defRest.IndexOf(',');
                                if (firstComma < 0) break;
                                int spriteId = EvalInt(defRest[..firstComma].Trim(), vars, ln, getInkey, isKeyDown, graphics);

                                // Hämta state-namn (andra token, citatskyddad)
                                string afterId = defRest[(firstComma + 1)..].Trim();
                                int secondComma = -1;
                                bool inQ = false;
                                for (int ci = 0; ci < afterId.Length; ci++)
                                {
                                    if (afterId[ci] == '"') inQ = !inQ;
                                    if (!inQ && afterId[ci] == ',') { secondComma = ci; break; }
                                }
                                if (secondComma < 0) break;

                                string stateName = afterId[..secondComma].Trim().Trim('"').ToLowerInvariant();
                                string seqAndFlags = afterId[(secondComma + 1)..].Trim();

                                // Parsa (frame,delay)-par
                                var frames = AnimationManager.ParseFrameSequence(seqAndFlags);
                                if (frames.Count == 0) break;

                                // LOOP eller ONCE
                                bool loop = seqAndFlags.ToUpperInvariant().Contains("LOOP");

                                // Valfritt: "idle" i slutet = OnCompleteGoTo
                                string? onComplete = null;
                                var goMatch = System.Text.RegularExpressions.Regex.Match(
                                    seqAndFlags, "\"([\\w]+)\"\\s*$");
                                if (goMatch.Success)
                                    onComplete = goMatch.Groups[1].Value.ToLowerInvariant();

                                animationManager.Define(spriteId, stateName, frames, loop, onComplete);
                            }

                            // --------------------------------------------------
                            //  ANIM SET 1, "run"
                            // --------------------------------------------------
                            else if (animUpper.StartsWith("SET "))
                            {
                                string setRest = arg.Substring(4).Trim();
                                int commaIdx = setRest.IndexOf(',');
                                if (commaIdx < 0) break;

                                int spriteId = EvalInt(setRest[..commaIdx].Trim(), vars, ln, getInkey, isKeyDown, graphics);
                                string stateName = setRest[(commaIdx + 1)..].Trim().Trim('"').ToLowerInvariant();

                                animationManager.SetState(spriteId, stateName);
                            }

                            // --------------------------------------------------
                            //  ANIM STOP 1
                            // --------------------------------------------------
                            else if (animUpper.StartsWith("STOP "))
                            {
                                int spriteId = EvalInt(arg.Substring(5).Trim(), vars, ln, getInkey, isKeyDown, graphics);
                                animationManager.Stop(spriteId);
                            }

                            // --------------------------------------------------
                            //  ANIM GROUP "player", 1, 2, 3
                            //  ANIM GROUP SET "player", "run"
                            // --------------------------------------------------
                            else if (animUpper.StartsWith("GROUP "))
                            {
                                string groupRest = arg.Substring(6).Trim();
                                string groupUpper = groupRest.ToUpperInvariant();

                                if (groupUpper.StartsWith("SET "))
                                {
                                    // ANIM GROUP SET "player", "run"
                                    string groupSetRest = groupRest.Substring(4).Trim();
                                    int commaIdx = groupSetRest.IndexOf(',');
                                    if (commaIdx < 0) break;

                                    string groupName = groupSetRest[..commaIdx].Trim().Trim('"');
                                    string stateName = groupSetRest[(commaIdx + 1)..].Trim().Trim('"').ToLowerInvariant();

                                    animationManager.SetGroupState(groupName, stateName);
                                }
                                else
                                {
                                    // ANIM GROUP "player", 1, 2, 3
                                    // Parsa gruppnamn + sprite-ids
                                    int firstComma = groupRest.IndexOf(',');
                                    if (firstComma < 0) break;

                                    string groupName = groupRest[..firstComma].Trim().Trim('"');
                                    string idsRest = groupRest[(firstComma + 1)..].Trim();

                                    // Dela upp på komman och utvärdera varje id
                                    var idParts = idsRest.Split(',',
                                        System.StringSplitOptions.RemoveEmptyEntries |
                                        System.StringSplitOptions.TrimEntries);

                                    var idList = new List<int>();
                                    foreach (var part in idParts)
                                        idList.Add(EvalInt(part, vars, ln, getInkey, isKeyDown, graphics));

                                    animationManager.DefineGroup(groupName, idList.ToArray());
                                }
                            }
                            break;
                        }
                        
                        case "IF":
                        {
                            int tIdx = IndexOfWord(arg, "THEN");
                            string condition = tIdx >= 0 ? arg[..tIdx].Trim() : arg;

                            bool cond = EvalCondition(condition, vars, ln, getInkey, isKeyDown, graphics);

                            if (!cond)
                            {
                                if (!ifJumps.TryGetValue(pc, out var target))
                                    throw new Exception($"ENDIF not found for IF at line {ln}");
                                pc = target;
                                jumpHappened = true;
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
                            var end = EvalInt(stIdx < 0 ? rest : rest[..stIdx].Trim(), vars, ln, getInkey, isKeyDown,
                                graphics);
                            var step = stIdx < 0
                                ? 1
                                : EvalInt(rest[(stIdx + 4)..].Trim(), vars, ln, getInkey, isKeyDown, graphics);
                            setVar(fV, (double)start);
                            forStack.Push(new ForFrame
                            {
                                VarName = fV, EndValue = end, StepValue = step, LineAfterForPc = pc + 1,
                                ForLineNumber = ln
                            });
                            break;
                        case "NEXT":
                            if (forStack.Count == 0) break;
                            var f = forStack.Peek();
                            var cur = GetDoubleVar(f.VarName, vars, ln) + f.StepValue;
                            setVar(f.VarName, cur);

                            // Lägg till en liten marginal (0.000001) för att undvika att flyttalsfel kör loopen en gång för mycket
                            bool loopDone = f.StepValue > 0
                                ? cur > (f.EndValue + 0.000001)
                                : cur < (f.EndValue - 0.000001);

                            if (!loopDone)
                            {
                                pc = f.LineAfterForPc;
                                jumpHappened = true;
                            }
                            else
                            {
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
                                        var lSearch = StripComments(StripLeadingLineNumber(lines[searchPc])).Trim()
                                            .ToUpperInvariant();
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
                            if (screenArgs.Count > 0)
                            {
                                var subCmd = screenArgs[0].ToUpperInvariant();

                                if (subCmd == "SELECT" && screenArgs.Count >= 2)
                                {
                                    graphics.SetDrawingScreen(EvalInt(screenArgs[1], vars, ln, getInkey, isKeyDown,
                                        graphics));
                                }
                                else if (subCmd == "ALPHA" && screenArgs.Count >= 3)
                                {
                                    // SCREEN ALPHA lager, 0-255
                                    int layerId = EvalInt(screenArgs[1], vars, ln, getInkey, isKeyDown, graphics);
                                    int val     = EvalInt(screenArgs[2], vars, ln, getInkey, isKeyDown, graphics);
                                    graphics.ScreenAlpha(layerId, val);
                                }
                                else if (subCmd == "FADE" && screenArgs.Count >= 4)
                                {
                                    // SCREEN FADE lager, targetAlpha(0-255), frames
                                    int layerId = EvalInt(screenArgs[1], vars, ln, getInkey, isKeyDown, graphics);
                                    int target  = EvalInt(screenArgs[2], vars, ln, getInkey, isKeyDown, graphics);
                                    int frames  = EvalInt(screenArgs[3], vars, ln, getInkey, isKeyDown, graphics);
                                    graphics.StartScreenFade(layerId, target, frames);
                                }
                                else if (subCmd == "ON" && screenArgs.Count >= 2)
                                {
                                    graphics.SetScreenVisible(
                                        EvalInt(screenArgs[1], vars, ln, getInkey, isKeyDown, graphics), true);
                                    onGraphicsChanged();
                                }
                                else if (subCmd == "OFF" && screenArgs.Count >= 2)
                                {
                                    graphics.SetScreenVisible(
                                        EvalInt(screenArgs[1], vars, ln, getInkey, isKeyDown, graphics), false);
                                    onGraphicsChanged();
                                }
                                else if (screenArgs.Count >= 2)
                                {
                                    // Standard: SCREEN width, height
                                    graphics.Screen(EvalInt(screenArgs[0], vars, ln, getInkey, isKeyDown, graphics),
                                        EvalInt(screenArgs[1], vars, ln, getInkey, isKeyDown, graphics));
                                }
                            }
                            break;
                        
                        case "SCROLL":
                            var sc = SplitCsvOrSpaces(arg);
                            if (sc.Count >= 3)
                                graphics.Scroll(EvalInt(sc[0], vars, ln, getInkey, isKeyDown, graphics),
                                    EvalInt(sc[1], vars, ln, getInkey, isKeyDown, graphics),
                                    EvalInt(sc[2], vars, ln, getInkey, isKeyDown, graphics));
                            else
                            {
                                var parts = arg.Split(',');
                                if (parts.Length >= 2)
                                    graphics.Scroll(0, EvalInt(parts[0], vars, ln, getInkey, isKeyDown, graphics),
                                        EvalInt(parts[1], vars, ln, getInkey, isKeyDown, graphics));
                            }

                            break;

                        case "LOAD":
                        {
                            // Parsa första parametern
                            int commaIndex = arg.IndexOf(',');
    
                            if (commaIndex > -1)
                            {
                                // Vi har ett komma → kolla vad som står FÖRE kommat
                                string firstParam = arg[..commaIndex].Trim();
        
                                // Är det #X-prefix? (börjar med #)
                                if (firstParam.StartsWith("#"))
                                {
                                    // === LOAD #1, "background.png" ===
                                    var (tempScreen, cleanArg) = ExtractScreenPrefix(arg);
                                    int? savedScreen = null;
            
                                    if (tempScreen.HasValue)
                                    {
                                        savedScreen = graphics.GetActiveScreenNumber();
                                        graphics.SetDrawingScreen(tempScreen.Value);
                                    }
            
                                    // Resten efter #X, är filnamnet
                                    string path = ValueToString(EvalValue(cleanArg, vars, ln, getInkey, isKeyDown, graphics));
                                    graphics.LoadBackground(path);
            
                                    if (savedScreen.HasValue)
                                        graphics.SetDrawingScreen(savedScreen.Value);
                                }
                            }
                            else
                            {
                                // Inget komma → enkel background-load till aktivt lager
                                // === LOAD "background.png" ===
                                string path = ValueToString(EvalValue(arg, vars, ln, getInkey, isKeyDown, graphics));
                                graphics.LoadBackground(path);
                            }
    
                            onGraphicsChanged();
                            break;
                        }

                        case "INC":
                            setVar(arg, GetDoubleVar(arg, vars, ln) + 1.0);
                            break;
                        case "DEC":
                            setVar(arg, GetDoubleVar(arg, vars, ln) - 1.0);
                            break;
                        
                        case "PLOT":
                        {
                            var (tempScreen, cleanArg) = ExtractScreenPrefix(arg);
                            int? savedScreen = null;
                                
                            if (tempScreen.HasValue)
                            {
                                savedScreen = graphics.GetActiveScreenNumber();
                                graphics.SetDrawingScreen(tempScreen.Value);
                            }
                                
                            var pP = SplitCsvOrSpaces(cleanArg);
                            graphics.Plot(EvalInt(pP[0], vars, ln, getInkey, isKeyDown, graphics),
                                EvalInt(pP[1], vars, ln, getInkey, isKeyDown, graphics));
                                
                            if (savedScreen.HasValue)
                                graphics.SetDrawingScreen(savedScreen.Value);
                                    
                            onGraphicsChanged();
                            break;
                        }
                            
                        case "LINE":
                        {
                            var (tempScreen, cleanArg) = ExtractScreenPrefix(arg);
                            int? savedScreen = null;
                                
                            if (tempScreen.HasValue)
                            {
                                savedScreen = graphics.GetActiveScreenNumber();
                                graphics.SetDrawingScreen(tempScreen.Value);
                            }
                                
                            var lL = SplitCsvOrSpaces(cleanArg);
                            graphics.Line(EvalInt(lL[0], vars, ln, getInkey, isKeyDown, graphics),
                                EvalInt(lL[1], vars, ln, getInkey, isKeyDown, graphics),
                                EvalInt(lL[2], vars, ln, getInkey, isKeyDown, graphics),
                                EvalInt(lL[3], vars, ln, getInkey, isKeyDown, graphics));
                                
                            if (savedScreen.HasValue)
                                graphics.SetDrawingScreen(savedScreen.Value);
                                    
                            onGraphicsChanged();
                            break;
                        }
                            
                        case "BOX":
                        {
                            var (tempScreen, cleanArg) = ExtractScreenPrefix(arg);
                            int? savedScreen = null;
                                
                            if (tempScreen.HasValue)
                            {
                                savedScreen = graphics.GetActiveScreenNumber();
                                graphics.SetDrawingScreen(tempScreen.Value);
                            }
                                
                            var bB = SplitCsvOrSpaces(cleanArg);
                            graphics.Box(EvalInt(bB[0], vars, ln, getInkey, isKeyDown, graphics),
                                EvalInt(bB[1], vars, ln, getInkey, isKeyDown, graphics),
                                EvalInt(bB[2], vars, ln, getInkey, isKeyDown, graphics),
                                EvalInt(bB[3], vars, ln, getInkey, isKeyDown, graphics));
                                
                            if (savedScreen.HasValue)
                                graphics.SetDrawingScreen(savedScreen.Value);
                                    
                            onGraphicsChanged();
                            break;
                        }
                            
                        case "BAR":
                        {
                            var (tempScreen, cleanArg) = ExtractScreenPrefix(arg);
                            int? savedScreen = null;
                                
                            if (tempScreen.HasValue)
                            {
                                savedScreen = graphics.GetActiveScreenNumber();
                                graphics.SetDrawingScreen(tempScreen.Value);
                            }
                                
                            var rR = SplitCsvOrSpaces(cleanArg);
                            graphics.Bar(EvalInt(rR[0], vars, ln, getInkey, isKeyDown, graphics),
                                EvalInt(rR[1], vars, ln, getInkey, isKeyDown, graphics),
                                EvalInt(rR[2], vars, ln, getInkey, isKeyDown, graphics),
                                EvalInt(rR[3], vars, ln, getInkey, isKeyDown, graphics));
                                
                            if (savedScreen.HasValue)
                                graphics.SetDrawingScreen(savedScreen.Value);
                                    
                            onGraphicsChanged();
                            break;
                        }
                            
                        case "CIRCLE":
                        {
                            var (tempScreen, cleanArg) = ExtractScreenPrefix(arg);
                            int? savedScreen = null;
                                
                            if (tempScreen.HasValue)
                            {
                                savedScreen = graphics.GetActiveScreenNumber();
                                graphics.SetDrawingScreen(tempScreen.Value);
                            }
                                
                            var rC = SplitCsvOrSpaces(cleanArg);
                            graphics.Circle(EvalInt(rC[0], vars, ln, getInkey, isKeyDown, graphics),
                                EvalInt(rC[1], vars, ln, getInkey, isKeyDown, graphics),
                                EvalInt(rC[2], vars, ln, getInkey, isKeyDown, graphics));
                                
                            if (savedScreen.HasValue)
                                graphics.SetDrawingScreen(savedScreen.Value);
                                    
                            onGraphicsChanged();
                            break;
                        }
                            
                        case "CIRCLEF":
                        {
                            var (tempScreen, cleanArg) = ExtractScreenPrefix(arg);
                            int? savedScreen = null;
                                
                            if (tempScreen.HasValue)
                            {
                                savedScreen = graphics.GetActiveScreenNumber();
                                graphics.SetDrawingScreen(tempScreen.Value);
                            }
                                
                            var rF = SplitCsvOrSpaces(cleanArg);
                            graphics.CircleF(EvalInt(rF[0], vars, ln, getInkey, isKeyDown, graphics),
                                EvalInt(rF[1], vars, ln, getInkey, isKeyDown, graphics),
                                EvalInt(rF[2], vars, ln, getInkey, isKeyDown, graphics),
                                EvalInt(rF[3], vars, ln, getInkey, isKeyDown, graphics));
                                
                            if (savedScreen.HasValue)
                                graphics.SetDrawingScreen(savedScreen.Value);
                                    
                            onGraphicsChanged();
                            break;
                        }
                            
                        case "ELLIPSE":
                        {
                            var (tempScreen, cleanArg) = ExtractScreenPrefix(arg);
                            int? savedScreen = null;
                                
                            if (tempScreen.HasValue)
                            {
                                savedScreen = graphics.GetActiveScreenNumber();
                                graphics.SetDrawingScreen(tempScreen.Value);
                            }
                                
                            var rE = SplitCsvOrSpaces(cleanArg);
                            graphics.Ellipse(EvalInt(rE[0], vars, ln, getInkey, isKeyDown, graphics),
                                EvalInt(rE[1], vars, ln, getInkey, isKeyDown, graphics),
                                EvalInt(rE[2], vars, ln, getInkey, isKeyDown, graphics),
                                EvalInt(rE[3], vars, ln, getInkey, isKeyDown, graphics));
                                
                            if (savedScreen.HasValue)
                                graphics.SetDrawingScreen(savedScreen.Value);
                                    
                            onGraphicsChanged();
                            break;
                        }
                            
                        case "FILL":
                        {
                            var (tempScreen, cleanArg) = ExtractScreenPrefix(arg);
                            int? savedScreen = null;
                                
                            if (tempScreen.HasValue)
                            {
                                savedScreen = graphics.GetActiveScreenNumber();
                                graphics.SetDrawingScreen(tempScreen.Value);
                            }
                                
                            var rI = SplitCsvOrSpaces(cleanArg);
                            graphics.Fill(EvalInt(rI[0], vars, ln, getInkey, isKeyDown, graphics),
                                EvalInt(rI[1], vars, ln, getInkey, isKeyDown, graphics));
                                
                            if (savedScreen.HasValue)
                                graphics.SetDrawingScreen(savedScreen.Value);
                                    
                            onGraphicsChanged();
                            break;
                        }
                            
                        case "TEXT":
                        {
                            var (tempScreen, cleanArg) = ExtractScreenPrefix(arg);
                            int? savedScreen = null;
                                
                            if (tempScreen.HasValue)
                            {
                                savedScreen = graphics.GetActiveScreenNumber();
                                graphics.SetDrawingScreen(tempScreen.Value);
                            }
                                
                            var tP = SplitCsvOrSpaces(cleanArg);
                            if (tP.Count >= 3)
                                graphics.DrawText(EvalInt(tP[0], vars, ln, getInkey, isKeyDown, graphics),
                                    EvalInt(tP[1], vars, ln, getInkey, isKeyDown, graphics),
                                    ValueToString(EvalValue(string.Join(" ", tP.Skip(2)), vars, ln, getInkey, isKeyDown,
                                        graphics)));
                                
                            if (savedScreen.HasValue)
                                graphics.SetDrawingScreen(savedScreen.Value);
                                    
                            onGraphicsChanged();
                            break;
                        }
                        case "IMAGE":
                        {
                            if (arg.StartsWith("LOAD "))
                            {
                                string loadArg = arg.Substring(5).Trim();
                                var parts = loadArg.Split(',').Select(p => p.Trim()).ToList();
                                int imgId = EvalInt(parts[0], vars, ln, getInkey, isKeyDown, graphics);
                                string file = ValueToString(EvalValue(parts[1], vars, ln, getInkey, isKeyDown, graphics));
                                if (parts.Count >= 5)
                                {
                                    int fw = EvalInt(parts[2], vars, ln, getInkey, isKeyDown, graphics);
                                    int fh = EvalInt(parts[3], vars, ln, getInkey, isKeyDown, graphics);
                                    int fc = EvalInt(parts[4], vars, ln, getInkey, isKeyDown, graphics);
                                    graphics.LoadImageBankSheet(imgId, file, fw, fh, fc);
                                }
                                else
                                    graphics.LoadImageBank(imgId, file);
                            }
                            break;
                        }
                        
                        case "SPRITE":
                        {
                            var (tempScreen, cleanArg) = ExtractScreenPrefix(arg);
                            int currentLayer = graphics.GetActiveScreenNumber();
                            int? savedScreen = null;
                            int? id = null;

                            if (tempScreen.HasValue)
                            {
                                savedScreen = currentLayer;
                                graphics.SetDrawingScreen(tempScreen.Value);
                                currentLayer = tempScreen.Value;
                            }

                            var ss = SplitCsvOrSpaces(cleanArg);
                            if (ss.Count == 0) break;

                            if (!int.TryParse(ss[0], out var sid))
                            {
                                var sub = ss[0].ToUpperInvariant();

                                if (sub == "LAYER" && ss.Count >= 3)
                                {
                                    id = EvalInt(ss[1], vars, ln, getInkey, isKeyDown, graphics);
                                    int layerId = EvalInt(ss[2], vars, ln, getInkey, isKeyDown, graphics);
                                    graphics.SpriteLayer(id.Value, layerId);
                                }

                                else if (sub == "POS"){
                                    id = EvalInt(ss[1], vars, ln, getInkey, isKeyDown, graphics);
                                    graphics.SpritePos(id.Value,
                                        EvalInt(ss[2], vars, ln, getInkey, isKeyDown, graphics),
                                        EvalInt(ss[3], vars, ln, getInkey, isKeyDown, graphics));
                                }
                                else if (sub == "LOAD")
                                {
                                    id = EvalInt(ss[1], vars, ln, getInkey, isKeyDown, graphics);
                                    string path = Unquote(ss[2]);

                                    // Om vi har mer än 2 argument (id + path), då är det ett sheet
                                    // SPRITE LOAD 1, "sheet.png", 32, 32, 7
                                    if (ss.Count >= 6) // "LOAD", id, path, w, h, count = 6 delar
                                    {
                                        int fw = EvalInt(ss[3], vars, ln, getInkey, isKeyDown, graphics);
                                        int fh = EvalInt(ss[4], vars, ln, getInkey, isKeyDown, graphics);
                                        int count = EvalInt(ss[5], vars, ln, getInkey, isKeyDown, graphics);
                                        graphics.LoadSpriteSheet(id.Value, path, fw, fh, count);
                                    }
                                    else if
                                        (ss.Count == 5) // "LOAD", id, path, w, h = 5 delar (frameCount från filnamn)
                                    {
                                        int fw = EvalInt(ss[3], vars, ln, getInkey, isKeyDown, graphics);
                                        int fh = EvalInt(ss[4], vars, ln, getInkey, isKeyDown, graphics);
                                        graphics.LoadSpriteSheetAuto(id.Value, path, fw, fh, null);
                                    }
                                    else if
                                        (ss.Count == 4) // "LOAD", id, path, count = 4 delar (width/height från filnamn)
                                    {
                                        int count = EvalInt(ss[3], vars, ln, getInkey, isKeyDown, graphics);
                                        graphics.LoadSpriteSheetAuto(id.Value, path, null, null, count);
                                    }
                                    else if (ss.Count == 3) // "LOAD", id, path = 3 delar
                                    {
                                        // Kolla om filnamnet innehåller _W, _H eller _B/_F (dvs. är det ett spritesheet?)
                                        string fileName = System.IO.Path.GetFileNameWithoutExtension(path);
                                        bool isSpriteSheet =
                                            fileName.Contains("_W", System.StringComparison.OrdinalIgnoreCase) ||
                                            fileName.Contains("_H", System.StringComparison.OrdinalIgnoreCase) ||
                                            fileName.Contains("_B", System.StringComparison.OrdinalIgnoreCase) ||
                                            fileName.Contains("_F", System.StringComparison.OrdinalIgnoreCase);

                                        if (isSpriteSheet)
                                        {
                                            // Auto-parse allt från filnamnet
                                            graphics.LoadSpriteSheetAuto(id.Value, path);
                                        }
                                        else
                                        {
                                            // Vanlig single-frame sprite
                                            graphics.LoadSprite(id.Value, path);
                                        }
                                    }
                                    else
                                    {
                                        // Fallback: Vanlig laddning av en bild
                                        graphics.LoadSprite(id.Value, path);
                                    }
                                }
                                else if (sub == "IMAGE" && ss.Count >= 3)
                                {
                                    id = EvalInt(ss[1], vars, ln, getInkey, isKeyDown, graphics);
                                    int imgId = EvalInt(ss[2], vars, ln, getInkey, isKeyDown, graphics);
                                    int frameId = ss.Count >= 4
                                        ? EvalInt(ss[3], vars, ln, getInkey, isKeyDown, graphics)
                                        : 1;
                                    graphics.SpriteImage(id.Value, imgId, frameId);
                                }
                                else if (sub == "ADDFRAME")
                                {
                                    id = EvalInt(ss[1], vars, ln, getInkey, isKeyDown, graphics);
                                    graphics.AddFrame(id.Value,
                                        Unquote(ss[2]));
                                }
                                else if (sub == "FRAME")
                                {
                                    id = EvalInt(ss[1], vars, ln, getInkey, isKeyDown, graphics);
                                    graphics.SetSpriteFrame(id.Value,
                                        EvalInt(ss[2], vars, ln, getInkey, isKeyDown, graphics));
                                }
                                else if (sub == "HANDLE")
                                {
                                    id = EvalInt(ss[1], vars, ln, getInkey, isKeyDown, graphics);
                                    graphics.SpriteHandle(id.Value,
                                        EvalInt(ss[2], vars, ln, getInkey, isKeyDown, graphics),
                                        EvalInt(ss[3], vars, ln, getInkey, isKeyDown, graphics));
                                }
                                else if (sub == "MOVE" && ss.Count >= 3)
                                {
                                    id = EvalInt(ss[1], vars, ln, getInkey, isKeyDown, graphics);
                                    int x1 = graphics.GetSprite(id.Value).X;
                                    int y1 = graphics.GetSprite(id.Value).Y;
                                    x1 = x1 + EvalInt(ss[2], vars, ln, getInkey, isKeyDown, graphics);
                                    y1 = y1 + EvalInt(ss[3], vars, ln, getInkey, isKeyDown, graphics);
                                    graphics.SpritePos(id.Value, x1, y1);
                                }
                                else if (sub == "ROTATE")
                                {
                                    id = EvalInt(ss[1], vars, ln, getInkey, isKeyDown, graphics);
                                    graphics.SpriteRotate(id.Value,
                                        EvalInt(ss[2], vars, ln, getInkey, isKeyDown, graphics));
                                }
                                else if (sub == "ZOOM")
                                {
                                    id = EvalInt(ss[1], vars, ln, getInkey, isKeyDown, graphics);
                                    double zx = EvalInt(ss[2], vars, ln, getInkey, isKeyDown, graphics) / 100.0;
                                    double zy = (ss.Count >= 4)
                                        ? EvalInt(ss[3], vars, ln, getInkey, isKeyDown, graphics) / 100.0
                                        : zx;
                                    graphics.SpriteZoom(id.Value, zx, zy);
                                }
                                else if (sub == "ALPHA" && ss.Count >= 3)
                                {
                                    // SPRITE ALPHA id, 0-255
                                    id = EvalInt(ss[1], vars, ln, getInkey, isKeyDown, graphics);
                                    int val = EvalInt(ss[2], vars, ln, getInkey, isKeyDown, graphics);
                                    graphics.SpriteAlpha(id.Value, val);
                                }
                                else if (sub == "FADE" && ss.Count >= 4)
                                {
                                    // SPRITE FADE id, targetAlpha(0-255), frames
                                    id = EvalInt(ss[1], vars, ln, getInkey, isKeyDown, graphics);
                                    int target = EvalInt(ss[2], vars, ln, getInkey, isKeyDown, graphics);
                                    int frames = EvalInt(ss[3], vars, ln, getInkey, isKeyDown, graphics);
                                    graphics.StartSpriteFade(id.Value, target, frames);
                                }
                                else if (sub == "GROUP")
                                {
                                    id = EvalInt(ss[1], vars, ln, getInkey, isKeyDown, graphics);
                                    string group = Unquote(ss[2]);
                                    graphics.SpriteAddGroup(id.Value, group);
                                }
                                else if (sub == "UNGROUP")
                                {
                                    id = EvalInt(ss[1], vars, ln, getInkey, isKeyDown, graphics);
                                    string group = Unquote(ss[2]);
                                    graphics.SpriteRemoveGroup(id.Value, group);
                                }
                                else if (sub == "CLEARGROUP")
                                {
                                    string group = Unquote(ss[1]);
                                    graphics.SpriteClearGroup(group);
                                }
                                else if (sub == "ON")
                                {
                                    id = EvalInt(ss[1], vars, ln, getInkey, isKeyDown, graphics);
                                    graphics.SpriteOn(id.Value);
                                }
                                else if (sub == "OFF")
                                {
                                    id = EvalInt(ss[1], vars, ln, getInkey, isKeyDown, graphics);
                                    graphics.SpriteOff(id.Value);
                                }
                                else if (sub == "EFFECT")
                                {
                                    // SPRITE EFFECT ADD/CLEAR/UPDATE id, ...
                                    if (ss.Count < 2) break;
                                    
                                    string effectSub = ss[1].ToUpperInvariant();
                                    
                                    if (effectSub == "ADD" && ss.Count >= 4)
                                    {
                                        // SPRITE EFFECT ADD id, type, param1, param2, ...
                                        id = EvalInt(ss[2], vars, ln, getInkey, isKeyDown, graphics);
                                        string effectType = ss[3].ToUpperInvariant();
                                        var sprite = graphics.GetSprite(id.Value);
                                        
                                        switch (effectType)
                                        {
                                            case "BLUR":
                                                if (ss.Count >= 5)
                                                {
                                                    float radius = (float)EvalDouble(ss[4], vars, ln, getInkey, isKeyDown, graphics);
                                                    sprite.Effects.Add(new SpriteBlurEffect { Radius = radius });
                                                }
                                                break;
    
                                            case "WAVE":
                                                if (ss.Count >= 6)
                                                {
                                                    float amplitudeX = (float)EvalDouble(ss[4], vars, ln, getInkey, isKeyDown, graphics);
                                                    float frequencyX = (float)EvalDouble(ss[5], vars, ln, getInkey, isKeyDown, graphics);
        
                                                    float amplitudeY = ss.Count >= 7 
                                                        ? (float)EvalDouble(ss[6], vars, ln, getInkey, isKeyDown, graphics) 
                                                        : 0f;
                                                    float frequencyY = ss.Count >= 8 
                                                        ? (float)EvalDouble(ss[7], vars, ln, getInkey, isKeyDown, graphics) 
                                                        : 0f;
        
                                                    // ✅ Auto-sätt phaseY till pi/2 (90 grader) för organisk rörelse
                                                    float phaseY = amplitudeY > 0 ? 1.57f : 0f;  // pi/2 ≈ 1.57
        
                                                    sprite.Effects.Add(new WaveEffect { 
                                                        AmplitudeX = amplitudeX,
                                                        FrequencyX = frequencyX,
                                                        AmplitudeY = amplitudeY,
                                                        FrequencyY = frequencyY,
                                                        PhaseX = 0f,
                                                        PhaseY = phaseY  // ✅ Ge Y-wave en 90° fasförskjutning
                                                    });
                                                }
                                                break;
                                            case "COLOR":
                                                if (ss.Count >= 7)
                                                {
                                                    float brightness = (float)EvalDouble(ss[4], vars, ln, getInkey, isKeyDown, graphics);
                                                    float contrast = (float)EvalDouble(ss[5], vars, ln, getInkey, isKeyDown, graphics);
                                                    float saturation = (float)EvalDouble(ss[6], vars, ln, getInkey, isKeyDown, graphics);
                                                    sprite.Effects.Add(new ColorGradeEffect {
                                                        Brightness = brightness,
                                                        Contrast = contrast,
                                                        Saturation = saturation
                                                    });
                                                }
                                                break;
    

                                        }
                                        sprite.InvalidateEffectCache();
                                    }
                                    else if (effectSub == "CLEAR" && ss.Count >= 3)
                                    {
                                        // SPRITE EFFECT CLEAR id
                                        id = EvalInt(ss[2], vars, ln, getInkey, isKeyDown, graphics);
                                        var sprite = graphics.GetSprite(id.Value);
                                        sprite.Effects.Clear();
                                        sprite.InvalidateEffectCache();
                                    }
                                    else if (effectSub == "UPDATE" && ss.Count >= 6)
                                    {
                                        id = EvalInt(ss[2], vars, ln, getInkey, isKeyDown, graphics);
                                        int idx = EvalInt(ss[3], vars, ln, getInkey, isKeyDown, graphics);
                                        string param = ss[4].ToUpperInvariant();
                                        float val = (float)EvalDouble(ss[5], vars, ln, getInkey, isKeyDown, graphics);
    
                                        var sprite = graphics.GetSprite(id.Value);
                                        if (idx >= 0 && idx < sprite.Effects.Count)
                                        {
                                            var effect = sprite.Effects[idx];
        
                                            // ✅ Flagga för om vi ska invalidera cache
                                            bool shouldInvalidateCache = true;
        
                                            switch (effect)
                                            {
                                                case SpriteBlurEffect blur when param == "RADIUS":
                                                    blur.Radius = val;
                                                    break;
                                                case WaveEffect wave when param == "AMPLITUDEX":
                                                    wave.AmplitudeX = val;
                                                    break;
                                                case WaveEffect wave when param == "FREQUENCYX":
                                                    wave.FrequencyX = val;
                                                    break;
                                                case WaveEffect wave when param == "AMPLITUDEY":
                                                    wave.AmplitudeY = val;
                                                    break;
                                                case WaveEffect wave when param == "FREQUENCYY":
                                                    wave.FrequencyY = val;
                                                    break;
                                                case WaveEffect wave when param == "PHASEX":
                                                    wave.PhaseX = val;
                                                    shouldInvalidateCache = false; // ✅ Animerad parameter
                                                    break;
                                                case WaveEffect wave when param == "PHASEY":
                                                    wave.PhaseY = val;
                                                    shouldInvalidateCache = false; // ✅ Animerad parameter
                                                    break;
                                                case WaveEffect wave when param == "TIME":
                                                    wave.Time = val/10;
                                                    shouldInvalidateCache = false; // ✅ Animerad parameter
                                                    break;
                                                case ColorGradeEffect color when param == "BRIGHTNESS":
                                                    color.Brightness = val;
                                                    break;
                                                case ColorGradeEffect color when param == "CONTRAST":
                                                    color.Contrast = val;
                                                    break;
                                                case ColorGradeEffect color when param == "SATURATION":
                                                    color.Saturation = val;
                                                    break;
                                            }
                                            // ✅ DEBUG: Visa vad som händer
                                           System.Diagnostics.Debug.WriteLine($"🔍 UPDATE: sprite={id.Value}, idx={idx}, param={param}, val={val}");
                                            System.Diagnostics.Debug.WriteLine($"   Effect type: {effect.GetType().Name}");
                                            System.Diagnostics.Debug.WriteLine($"   shouldInvalidateCache: {shouldInvalidateCache}");
 
                                            // ✅ Invalidera bara för statiska parametrar (RADIUS, AMPLITUDE etc)
                                            if (shouldInvalidateCache)
                                            {
                                                sprite.InvalidateEffectCache();
                                            }
                                        }
                                    }
                                }
                                else
                                {
                                    id = EvalInt(ss[1], vars, ln, getInkey, isKeyDown, graphics);
                                    graphics.CreateSprite(sid, id.Value,
                                        EvalInt(ss[2], vars, ln, getInkey, isKeyDown, graphics));
                                }
                                if (savedScreen.HasValue)
                                    graphics.SetDrawingScreen(savedScreen.Value);
                                if (tempScreen.HasValue && id.HasValue)
                                    graphics.SpriteLayer(id.Value, currentLayer);
                            }
                            else
                            {
                                // ============================================================
                                // DIREKT SYNTAX: SPRITE id, x, y, img
                                // Ex: SPRITE #0,1,0,0,1 (lager 0, sprite 1, x=0, y=0, image 1)
                                // ============================================================
                                if (ss.Count >= 4)
                                {
                                    id = sid;
                                    int x1  = EvalInt(ss[1], vars, ln, getInkey, isKeyDown, graphics);
                                    int y1  = EvalInt(ss[2], vars, ln, getInkey, isKeyDown, graphics);
                                    int img = EvalInt(ss[3], vars, ln, getInkey, isKeyDown, graphics);
                                    
                                    // Skapa spriten med rätt storlek från bilden
                                    graphics.SpriteImage(id.Value, img, 1);
                                    // Sätt position
                                    graphics.SpritePos(id.Value, x1, y1);
                                    // Gör spriten synlig
                                    graphics.SpriteOn(id.Value);
                                    
                                    if (savedScreen.HasValue)
                                        graphics.SetDrawingScreen(savedScreen.Value);
                                    if (tempScreen.HasValue)
                                        graphics.SpriteLayer(id.Value, currentLayer);
                                }
                            }
                            break;
                        }
                        case "SAM":
                            var samArgs = SplitCsvOrSpaces(arg);
                            if (samArgs.Count >= 2 && samArgs[0].ToUpperInvariant() == "PLAY")
                            {
                                PlayEffect(Unquote(samArgs[1]), audioEngine, false);
                            }

                            if (samArgs.Count >= 2 && samArgs[0].ToUpperInvariant() == "LOOP")
                            {
                                PlayEffect(Unquote(samArgs[1]), audioEngine, true);
                            }

                            if (samArgs.Count >= 2 && samArgs[0].ToUpperInvariant() == "STOP")
                            {
                                StopEffect(Unquote(samArgs[1]), audioEngine);
                            }

                            break;
                        
                        case "MUSIC":
                            var musArgs = SplitCsvOrSpaces(arg);
                            if (musArgs.Count >= 2 && musArgs[0].ToUpperInvariant() == "PLAY")
                            {
                                PlayMusic(Unquote(musArgs[1]), audioEngine);
                            }
                            else if (musArgs.Count >= 1 && musArgs[0].ToUpperInvariant() == "STOP")
                            {
                                StopMusic(audioEngine);
                            }

                            break;
                        
                        case "PLAY":
                        {
                            var parts = arg.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                            string sub = parts[0].ToUpperInvariant();
                            //string rest = parts.Length > 1 ? parts[1].Trim() : "";
                            var pS = SplitCsvOrSpaces(arg);
                            //var (_, rest2) = SplitFirstWord(arg);  
                            
                            switch (sub)
                            {
                                case "JUMP":      ChiptuneSynth.PlayJump();      break;
                                case "LASER":     ChiptuneSynth.PlayLaser();     break;
                                case "EXPLOSION": ChiptuneSynth.PlayExplosion(); break;
                                case "COIN":      ChiptuneSynth.PlayCoin();      break;
                                case "BLIP":      ChiptuneSynth.PlayBlip();      break;
                                case "HIT":       ChiptuneSynth.PlayHit();       break;
                                case "POWERUP":   ChiptuneSynth.PlayPowerUp();   break;

                                case "WAVE":
                                {
                                    int    ch   = EvalInt(pS[1], vars, ln, getInkey, isKeyDown, graphics);
                                    string wave = Unquote(pS[2]);
                                    ChiptuneSynth.SetWave(ch, wave);
                                    break;
                                }

                                case "VOLUME":
                                {
                                    int    ch  = EvalInt(pS[1], vars, ln, getInkey, isKeyDown, graphics);
                                    double vol = EvalDouble(pS[2], vars, ln, getInkey, isKeyDown, graphics);
                                    ChiptuneSynth.SetVolume(ch, vol);
                                    break;
                                }

                                case "BPM":
                                {
                                    int bpm = EvalInt(pS[1], vars, ln, getInkey, isKeyDown, graphics);
                                    ChiptuneSynth.SetBpm(bpm);
                                    break;
                                }

                                case "MUSIC":
                                {
                                    var (_, rest2) = SplitFirstWord(arg);
                                    var ms2       = SplitTopLevelCsv(rest2);
                                    int    ch     = EvalInt(ms2[0], vars, ln, getInkey, isKeyDown, graphics);
                                    string seq    = Unquote(ms2[1]);
                                    ChiptuneSynth.PlayMusic(ch, seq);
                                    break;
                                }

                                case "STOP":
                                    ChiptuneSynth.StopMusic();
                                    break;
                            }
                            break;
                        }
                        
                        case "RASTER":
                        {
                            var (tempScreen, cleanArg) = ExtractScreenPrefix(arg);
                            int currentLayer = graphics.GetActiveScreenNumber();
                            int? savedScreen = null;
                            int? id = null;

                            if (tempScreen.HasValue)
                            {
                                savedScreen = currentLayer;
                                graphics.SetDrawingScreen(tempScreen.Value);
                                currentLayer = tempScreen.Value;
                                arg = cleanArg;
                            }

                            // RASTER MOVE id, newY
                            if (arg.StartsWith("MOVE ", StringComparison.OrdinalIgnoreCase))
                            {
                                var moveParts = SplitCsvOrSpaces(arg.Substring(5).Trim());
                                int barId = (int)Math.Round(EvalDouble(moveParts[0], vars, ln, getInkey, isKeyDown, graphics));
                                float newY = (float)EvalDouble(moveParts[1], vars, ln, getInkey, isKeyDown, graphics);
                                graphics.MoveRasterBar(currentLayer, barId, newY);
                                break;
                            }

                            // RASTER DEL id
                            if (arg.StartsWith("DEL ", StringComparison.OrdinalIgnoreCase))
                            {
                                int barId = (int)Math.Round(EvalDouble(arg.Substring(4).Trim(), vars, ln, getInkey, isKeyDown, graphics));
                                graphics.DeleteRasterBar(currentLayer, barId);
                                break;
                            }

                            // RASTER CLEAR
                            if (arg.Equals("CLEAR", StringComparison.OrdinalIgnoreCase))
                            {
                                graphics.ClearAllRasterBars(currentLayer);
                                break;
                            }

                            // RASTER INFO
                            if (arg.Equals("INFO", StringComparison.OrdinalIgnoreCase))
                            {
                                Console.WriteLine(graphics.GetRasterBarDebugInfo());
                                break;
                            }
                            
                            if (arg.StartsWith("WRAP ", StringComparison.OrdinalIgnoreCase))
                            {
                                var moveParts = SplitCsvOrSpaces(arg.Substring(5).Trim());
                                int barId = (int)Math.Round(EvalDouble(moveParts[0], vars, ln, getInkey, isKeyDown, graphics));
                                bool Wrap = EvalDouble(moveParts[1], vars, ln, getInkey, isKeyDown, graphics) != 0.0;
                                graphics.SetRasterWrap(currentLayer, barId, Wrap);
                                break;
                            }
                            if (arg.StartsWith("GFXMODE ", StringComparison.OrdinalIgnoreCase))
                            {
                                var moveParts = SplitCsvOrSpaces(arg.Substring(8).Trim());
                                int layerId = (int)Math.Round(EvalDouble(moveParts[0], vars, ln, getInkey, isKeyDown, graphics));
                                bool RM = EvalDouble(moveParts[1], vars, ln, getInkey, isKeyDown, graphics) != 0.0;
                                graphics.SetRasterGfxMode(layerId, RM);
                                break;
                            }                        
                            if (arg.StartsWith("SPACE ", StringComparison.OrdinalIgnoreCase))
                            {
                                var moveParts = SplitCsvOrSpaces(arg.Substring(6).Trim());
                                int layerId = (int)Math.Round(EvalDouble(moveParts[0], vars, ln, getInkey, isKeyDown, graphics));
                                int RM = (int)Math.Round(EvalDouble(moveParts[1], vars, ln, getInkey, isKeyDown, graphics));
                                graphics.SetRasterSpaceMode(layerId, RM);
                                break;
                            }   
                            // RASTER STR(n) = "color1,color2"  →  SetShaderColors (legacy)
                            if (arg.StartsWith("STR(", StringComparison.OrdinalIgnoreCase))
                            {
                                int openParen  = arg.IndexOf('(');
                                int closeParen = arg.IndexOf(')');
                                string inner = arg.Substring(openParen + 1, closeParen - openParen - 1);
                                var val = EvalValue(inner, vars, ln, getInkey, isKeyDown, graphics);
                                int rbNum = Convert.ToInt32(val);

                                int eqIdx2 = arg.IndexOf('=');
                                if (eqIdx2 > 0)
                                {
                                    string colorStr = ValueToString(EvalValue(arg[(eqIdx2 + 1)..].Trim(), vars, ln, getInkey, isKeyDown, graphics));
                                    var colorParts = colorStr.Split(',').Select(c => c.Trim()).ToList();
                                    var c1 = ParseColor(colorParts[0]);
                                    var c2 = colorParts.Count > 1 ? ParseColor(colorParts[1]) : c1;
                                    graphics.SetShaderColors(currentLayer, rbNum, c1, c2);
                                }
                                break;
                            }

                            // RASTER id, x, y, height [, "colors"]
                            var rbArgs = SplitTopLevelCsv(arg.Trim());
                            if (rbArgs.Count >= 4)
                            {
                                int rbNum = Convert.ToInt32(EvalValue(rbArgs[0], vars, ln, getInkey, isKeyDown, graphics));
                                float x      = (float)EvalDouble(rbArgs[1], vars, ln, getInkey, isKeyDown, graphics);
                                float rbOff  = (float)EvalDouble(rbArgs[2], vars, ln, getInkey, isKeyDown, graphics);
                                float rbH    = (float)EvalDouble(rbArgs[3], vars, ln, getInkey, isKeyDown, graphics);
                                    
                                if (rbArgs.Count >= 5)
                                {
                                    // Color string provided → use SetRasterBar (new API)
                                    string colorPart = ValueToString(EvalValue(rbArgs[4].Trim(), vars, ln, getInkey, isKeyDown, graphics));
                                    graphics.SetRasterBar(currentLayer, rbNum, x, rbOff, rbH, colorPart);
                                }
                                else
                                {
                                    // No colors → legacy SetShaderParams
                                    graphics.SetShaderParams(currentLayer, rbNum, rbOff, rbH);
                                }
                            }
                            
                            if (savedScreen.HasValue)
                                graphics.SetDrawingScreen(savedScreen.Value);
                            break;
                        }
                        
                        case "PARTICLE":
                            var rainArgs = SplitCsvOrSpaces(arg);
                            if (rainArgs.Count >= 2)
                            {
                                int type = EvalInt(rainArgs[0], vars, ln, getInkey, isKeyDown, graphics);
                                float density = (float)EvalDouble(rainArgs[1], vars, ln, getInkey, isKeyDown, graphics);
                                int curL = graphics.GetActiveScreenNumber();
                                // Slot 0 = Typ, Slot 1 = Mängd
                                graphics.SetShadervalues(curL, 1, (float)type, density);
                            }

                            break;
                        
                        case "TILE":
                            var tArgs = SplitCsvOrSpaces(arg);
                            if (tArgs.Count > 0)
                            {
                                var tileSub = tArgs[0].ToUpperInvariant();
                                if (tileSub == "LOAD" && tArgs.Count >= 4)
                                    graphics.LoadTileBank(Unquote(tArgs[1]),
                                        EvalInt(tArgs[2], vars, ln, getInkey, isKeyDown, graphics),
                                        EvalInt(tArgs[3], vars, ln, getInkey, isKeyDown, graphics));
                                else if (tileSub == "MAP" && tArgs.Count >= 3)
                                    graphics.SetMapSize(EvalInt(tArgs[1], vars, ln, getInkey, isKeyDown, graphics),
                                        EvalInt(tArgs[2], vars, ln, getInkey, isKeyDown, graphics));
                                else if (tileSub == "SET" && tArgs.Count >= 4)
                                    graphics.SetMapTile(EvalInt(tArgs[1], vars, ln, getInkey, isKeyDown, graphics),
                                        EvalInt(tArgs[2], vars, ln, getInkey, isKeyDown, graphics),
                                        EvalInt(tArgs[3], vars, ln, getInkey, isKeyDown, graphics));
                                else if (tileSub == "DRAW" && tArgs.Count >= 3)
                                {
                                    // TILE DRAW kan nu ha #X-prefix: TILE DRAW #1, offsetX, offsetY
                                    string drawArgs = string.Join(",", tArgs.Skip(1));
                                    var (tempScreen, cleanArg) = ExtractScreenPrefix(drawArgs);
                                    int? savedScreen = null;
                                        
                                    if (tempScreen.HasValue)
                                    {
                                        savedScreen = graphics.GetActiveScreenNumber();
                                        graphics.SetDrawingScreen(tempScreen.Value);
                                    }
                                        
                                    var coords = SplitCsvOrSpaces(cleanArg);
                                    graphics.DrawMap(
                                        EvalInt(coords[0], vars, ln, getInkey, isKeyDown, graphics),
                                        EvalInt(coords[1], vars, ln, getInkey, isKeyDown, graphics)
                                    );
                                        
                                    if (savedScreen.HasValue)
                                        graphics.SetDrawingScreen(savedScreen.Value);
                                }
                            }

                            break;
                        
                        case "FONT":
                        {
                            var (tempScreen, cleanArg) = ExtractScreenPrefix(arg);
                            int? savedScreen = null;

                            if (tempScreen.HasValue)
                            {
                                savedScreen = graphics.GetActiveScreenNumber();
                                graphics.SetDrawingScreen(tempScreen.Value);
                            }

                            var fArgs = SplitCsvOrSpaces(cleanArg);
                            if (fArgs.Count > 0)
                            {
                                var fSub = fArgs[0].ToUpperInvariant();

                                if (fSub == "LOAD" && fArgs.Count >= 5)
                                    graphics.FontLoad(
                                        EvalInt(fArgs[1], vars, ln, getInkey, isKeyDown, graphics),
                                        Unquote(fArgs[2]),
                                        EvalInt(fArgs[3], vars, ln, getInkey, isKeyDown, graphics),
                                        EvalInt(fArgs[4], vars, ln, getInkey, isKeyDown, graphics));

                                else if (fSub == "MAP" && fArgs.Count >= 3)
                                    graphics.FontMap(
                                        EvalInt(fArgs[1], vars, ln, getInkey, isKeyDown, graphics),
                                        Unquote(string.Join(" ", fArgs.Skip(2))));

                                else if (fSub == "PRINT" && fArgs.Count >= 4)
                                {
                                    // Bygg ihop argumenten efter "PRINT"
                                    string printRest = string.Join(",", fArgs.Skip(1));
    
                                    // Nu körs ExtractScreenPrefix på "#3, 134, 0, 0, \"HELLO AMOS\""
                                    var (tempScreen2, cleanPrint) = ExtractScreenPrefix(printRest);
                                    int? savedScreen2 = null;
    
                                    if (tempScreen2.HasValue)
                                    {
                                        savedScreen2 = graphics.GetActiveScreenNumber();
                                        graphics.SetDrawingScreen(tempScreen2.Value);
                                    }
    
                                    var parts = SplitTopLevelCsv(cleanPrint);
                                    graphics.FontPrint(
                                        EvalInt(parts[0], vars, ln, getInkey, isKeyDown, graphics),
                                        EvalInt(parts[1], vars, ln, getInkey, isKeyDown, graphics),
                                        EvalInt(parts[2], vars, ln, getInkey, isKeyDown, graphics),
                                        ValueToString(EvalValue(parts[3], vars, ln, getInkey, isKeyDown, graphics)));
    
                                    if (savedScreen2.HasValue)
                                        graphics.SetDrawingScreen(savedScreen2.Value);
                                }
                                else if (fSub == "CHAR" && fArgs.Count >= 5)
                                {
                                    var parts = SplitCsvOrSpaces(string.Join(",", fArgs.Skip(1)));
                                    graphics.FontChar(
                                        EvalInt(parts[0], vars, ln, getInkey, isKeyDown, graphics),
                                        EvalInt(parts[1], vars, ln, getInkey, isKeyDown, graphics),
                                        EvalInt(parts[2], vars, ln, getInkey, isKeyDown, graphics),
                                        ValueToString(EvalValue(parts[3], vars, ln, getInkey, isKeyDown, graphics)));
                                }

                                else if (fSub == "ROTATE" && fArgs.Count >= 3)
                                    graphics.FontRotate(
                                        EvalInt(fArgs[1], vars, ln, getInkey, isKeyDown, graphics),
                                        EvalInt(fArgs[2], vars, ln, getInkey, isKeyDown, graphics));

                                else if (fSub == "ZOOM" && fArgs.Count >= 3)
                                {
                                    int fid = EvalInt(fArgs[1], vars, ln, getInkey, isKeyDown, graphics);
                                    double fzx = EvalInt(fArgs[2], vars, ln, getInkey, isKeyDown, graphics) / 100.0;
                                    double fzy = (fArgs.Count >= 4)
                                        ? EvalInt(fArgs[3], vars, ln, getInkey, isKeyDown, graphics) / 100.0
                                        : fzx;
                                    graphics.FontZoom(fid, fzx, fzy);
                                }

                                else if (fSub == "SET" && fArgs.Count >= 3)
                                {
                                    var fArgs2 = SplitTopLevelCsv(cleanArg.Substring(4).Trim());
                                    if (fArgs2.Count >= 3)
                                    {
                                        int width  = EvalInt(fArgs2[0], vars, ln, getInkey, isKeyDown, graphics);
                                        int height = EvalInt(fArgs2[1], vars, ln, getInkey, isKeyDown, graphics);
                                        string fnt = ValueToString(EvalValue(fArgs2[2], vars, ln, getInkey, isKeyDown, graphics));
                                        graphics.ConfigureText(width, height, fnt);
                                    }
                                }

                                else if (fSub == "STYLE" && fArgs.Count >= 2)
                                {
                                    var fArgs2 = SplitTopLevelCsv(cleanArg.Substring(4).Trim());
                                    graphics.FontTextStyle(fArgs2[0]);
                                }

                                else if (fSub == "CLEAR")
                                    graphics.FontClear();
                                
                            }

                            if (savedScreen.HasValue)
                                graphics.SetDrawingScreen(savedScreen.Value);

                            onGraphicsChanged();
                            break;
                        }
                        
                        case "MAP":
                            var mArgs = SplitCsvOrSpaces(arg);
                            if (mArgs.Count >= 2 && mArgs[0].ToUpperInvariant() == "LOAD")
                            {
                                var path = Unquote(mArgs[1]);
                                if (System.IO.File.Exists(path))
                                {
                                    try
                                    {
                                        using var stream = System.IO.File.OpenRead(path);
                                        var dto = await System.Text.Json.JsonSerializer
                                            .DeserializeAsync<MapDto>(stream);
                                        if (dto != null)
                                        {
                                            graphics.SetMapSize(dto.Width, dto.Height);
                                            int idx = 0;
                                            for (int y = 0; y < dto.Height; y++)
                                            {
                                                for (int z = 0; z < dto.Width; z++)
                                                {
                                                    graphics.SetMapTile(z, y, dto.Data[idx++]);
                                                }
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        await appendLineAsync("MAP LOAD ERROR: " + ex.Message);
                                    }
                                }
                            }

                            break;
                        
                        case "OPEN":
                        {
                            // Syntax: OPEN IN/OUT/APPEND channel, "filename"
                            string openArg = arg.Trim();

                            // Läs mode (IN/OUT/APPEND)
                            string[] parts = openArg.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length < 2)
                                throw new Exception("OPEN: Syntax är OPEN IN/OUT/APPEND channel, \"filename\"");

                            string modeStr = parts[0].ToUpperInvariant();
                            FileMode mode;

                            if (modeStr == "IN" || modeStr == "INPUT")
                                mode = FileMode.Input;
                            else if (modeStr == "OUT" || modeStr == "OUTPUT")
                                mode = FileMode.Output;
                            else if (modeStr == "APPEND")
                                mode = FileMode.Append;
                            else
                                throw new Exception($"OPEN: Okänd mode '{modeStr}'. Använd IN, OUT eller APPEND");

                            // Hitta kommat mellan channel och filename
                            string restArg = parts[1];
                            int commaIdx2 = restArg.IndexOf(',');
                            if (commaIdx2 < 0)
                                throw new Exception("OPEN: Syntax är OPEN mode channel, \"filename\"");

                            // Läs kanalnummer
                            string channelExpr = restArg[..commaIdx2].Trim();
                            int channel = EvalInt(channelExpr, vars, ln, getInkey, isKeyDown, graphics);

                            if (channel < 1 || channel > MaxChannels)
                                throw new Exception(
                                    $"OPEN: Kanal {channel} utanför giltigt intervall (1-{MaxChannels})");

                            if (_openFiles.ContainsKey(channel))
                                throw new Exception($"OPEN: Kanal {channel} är redan öppen");

                            // Läs filnamn
                            string fileExpr = restArg[(commaIdx2 + 1)..].Trim();
                            object fileObj = EvalValue(fileExpr, vars, ln, getInkey, isKeyDown, graphics);
                            string filePath = ValueToString(fileObj);

                            // Konvertera relativ sökväg till absolut
                            if (!Path.IsPathRooted(filePath))
                            {
                                filePath = Path.Combine(Environment.CurrentDirectory, filePath);
                            }

                            // Öppna filen
                            try
                            {
                                var fileChannel = new FileChannel(channel, filePath, mode);

                                switch (mode)
                                {
                                    case FileMode.Input:
                                        if (!File.Exists(filePath))
                                            throw new FileNotFoundException($"Filen '{filePath}' hittades inte");
                                        fileChannel.Reader = new StreamReader(filePath, Encoding.UTF8);
                                        break;

                                    case FileMode.Output:
                                        // Skapa katalog om den inte finns
                                        var dir = Path.GetDirectoryName(filePath);
                                        if (!string.IsNullOrEmpty(dir))
                                            Directory.CreateDirectory(dir);
                                        fileChannel.Writer = new StreamWriter(filePath, false, Encoding.UTF8);
                                        break;

                                    case FileMode.Append:
                                        var dirAppend = Path.GetDirectoryName(filePath);
                                        if (!string.IsNullOrEmpty(dirAppend))
                                            Directory.CreateDirectory(dirAppend);
                                        fileChannel.Writer = new StreamWriter(filePath, true, Encoding.UTF8);
                                        break;
                                }

                                _openFiles[channel] = fileChannel;

                                //await appendLineAsync($"@@PRINT File opened on channel {channel}");
                            }
                            catch (Exception ex)
                            {
                                throw new Exception($"OPEN: Kunde inte öppna fil '{filePath}': {ex.Message}");
                            }
                        }
                            break;
                        case "CLOSE":
                        {
                            // Syntax: CLOSE channel
                            int channel = EvalInt(arg, vars, ln, getInkey, isKeyDown, graphics);

                            if (!_openFiles.ContainsKey(channel))
                            {
                                // Ignorera om kanalen inte är öppen (som klassisk BASIC)
                                break;
                            }

                            var fileChannel = _openFiles[channel];

                            try
                            {
                                fileChannel.Reader?.Close();
                                fileChannel.Reader?.Dispose();
                                fileChannel.Writer?.Flush();
                                fileChannel.Writer?.Close();
                                fileChannel.Writer?.Dispose();
                                fileChannel.IsOpen = false;

                                _openFiles.Remove(channel);

                                //await appendLineAsync($"@@PRINT File closed on channel {channel}");
                            }
                            catch (Exception ex)
                            {
                                throw new Exception($"CLOSE: Fel vid stängning av kanal {channel}: {ex.Message}");
                            }
                        }
                            break;
                        default:
                        {
                            if (!string.IsNullOrWhiteSpace(cmd))
                            {
                                // --- PROC-anrop ---
                                if (procs.TryGetValue(cmd, out var procDef))
                                {
                                    // Parsa argument
                                    var argValues = new List<object>();
                                    if (!string.IsNullOrWhiteSpace(arg))
                                    {
                                        foreach (var a in SplitTopLevelCsv(arg))
                                            argValues.Add(EvalValue(a.Trim(), vars, ln,
                                                getInkey, isKeyDown, graphics));
                                    }

                                    if (argValues.Count != procDef.Parameters.Count)
                                        throw new Exception(
                                            $"PROC {procDef.Name} expects {procDef.Parameters.Count} " +
                                            $"parameters, got {argValues.Count} at line {ln}");

                                    // Spara undan parametrar som skrivs över
                                    var savedVars = new Dictionary<string, object>(
                                        StringComparer.OrdinalIgnoreCase);
                                    foreach (var param in procDef.Parameters)
                                        if (vars.TryGetValue(param, out var old))
                                            savedVars[param] = old;

                                    // Sätt parametrar
                                    for (int i = 0; i < procDef.Parameters.Count; i++)
                                        setVar(procDef.Parameters[i], argValues[i]);

                                    // Pusha return-frame och hoppa
                                    procCallStack.Push(new ProcCallFrame
                                    {
                                        ReturnPc = pc + 1,
                                        SavedVars = savedVars
                                    });

                                    pc = procDef.StartPc;
                                    jumpHappened = true;
                                    break;
                                }

                                // --- Tilldelning (X = ...) ---
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
                                            var indicesStr = leftSide[(openParen + 1)..closeParen];

                                            if (vars.TryGetValue(arrayName, out var aVal) && aVal is IAmosArray array)
                                            {
                                                // Parse alla index (kan vara flera för multidim)
                                                var indexParts = SplitTopLevelCsv(indicesStr);
                                                var indices = new int[indexParts.Count];

                                                for (int i = 0; i < indexParts.Count; i++)
                                                {
                                                    var rawIdx = EvalValue(indexParts[i].Trim(), vars, ln, getInkey,
                                                        isKeyDown, graphics);
                                                    indices[i] = (int)Math.Round(Convert.ToDouble(rawIdx,
                                                        CultureInfo.InvariantCulture));
                                                }

                                                // Sätt värde på rätt plats i multidim-arrayen
                                                array.Set(rightValue, indices);
                                            }
                                            else
                                            {
                                                throw new Exception($"Unknown array '{arrayName}' at line {ln}");
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
                }
                catch (Exception ex)
                {
                    // Spara felinformation i BASIC-variabler så man kan kolla vad som hände
                    setVar("ERR$", ex.Message);
                    setVar("ERL", ln);

                    if (errorMode == 1)
                    {
                        // RESUME NEXT: Ignorera felet och kör vidare till nästa kommando/rad.
                        // Vi gör ingenting här, loopen fortsätter till nästa kommando i 'commands'
                        // eller nästa rad om commands är slut.
                    }
                    else if (errorMode == 2)
                    {
                        // GOTO Label
                        pc = errorGotoPc;
                        jumpHappened = true;
                        break; // Bryt command-loopen för att utföra hoppet
                    }
                    else
                    {
                        // BREAK (Standard): Rapportera och avsluta
                        if (ex.Message == "A task was canceled.") {return;}
                        await appendLineAsync($"Runtime Error at line {ln}: {ex.Message}");
                        // Kasta vidare eller returnera för att stoppa helt
                        return;
                    }
                }
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

    // NYTT: Hjälpmetod för att kolla om rad är tom eller bara kommentar
    private static bool IsEmptyLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return true;
            
        var stripped = StripComments(line.Trim());
        stripped = StripLeadingLineNumber(stripped).Trim();
            
        // Rad är tom om den bara innehåller label eller är tom efter stripping
        return string.IsNullOrWhiteSpace(stripped) || stripped.EndsWith(":");
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
                        
                // ÄNDRAT: Kasta fel om nämnaren är noll (eller extremt nära noll)
                if (Math.Abs(div) < 0.000000001)
                    throw new DivideByZeroException("Division by zero");

                res = Convert.ToDouble(res, CultureInfo.InvariantCulture) / div;
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
                if (v.ContainsKey("__functions__"))
                {
                    var funcs = (Dictionary<string, FunctionDefinition>)v["__functions__"];
                    if (funcs.ContainsKey(id))
                    {
                        var argExprs = new List<object>();
                        bool first = true;
                        while (!t.TryConsume(')'))
                        {
                            if (!first) t.TryConsume(',');
                            argExprs.Add(ParseExpr(ref t, v, ln, gk, ikd, g));
                            first = false;
                        }

                        var callFunc = (Func<string, List<object>, int, object>)v["__callFunction__"];
                        return callFunc(id, argExprs, ln);
                    }
                }
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
                if (id.Equals("ABS", StringComparison.OrdinalIgnoreCase)) {
                    double a = Convert.ToDouble(ParseExpr(ref t, v, ln, gk, ikd, g), CultureInfo.InvariantCulture); 
                    t.TryConsume(')'); return Math.Abs(a);
                }
                if (id.Equals("SGN", StringComparison.OrdinalIgnoreCase)) {
                    double a = Convert.ToDouble(ParseExpr(ref t, v, ln, gk, ikd, g), CultureInfo.InvariantCulture); 
                    t.TryConsume(')'); return Math.Sign(a);
                }
                if (id.Equals("SQR", StringComparison.OrdinalIgnoreCase)) {
                    double a = Convert.ToDouble(ParseExpr(ref t, v, ln, gk, ikd, g), CultureInfo.InvariantCulture); 
                    t.TryConsume(')'); return Math.Sqrt(a);
                }
                if (id.Equals("LOG", StringComparison.OrdinalIgnoreCase)) {
                    double a = Convert.ToDouble(ParseExpr(ref t, v, ln, gk, ikd, g), CultureInfo.InvariantCulture); 
                    t.TryConsume(')'); return Math.Log(a);
                }
                if (id.Equals("LOG2", StringComparison.OrdinalIgnoreCase)) {
                    double a = Convert.ToDouble(ParseExpr(ref t, v, ln, gk, ikd, g), CultureInfo.InvariantCulture); 
                    t.TryConsume(')'); return Math.Log2(a);
                }
                if (id.Equals("LOG10", StringComparison.OrdinalIgnoreCase)) {
                    double a = Convert.ToDouble(ParseExpr(ref t, v, ln, gk, ikd, g), CultureInfo.InvariantCulture); 
                    t.TryConsume(')'); return Math.Log10(a);
                }
                if (id.Equals("EXP", StringComparison.OrdinalIgnoreCase)) {
                    double a = Convert.ToDouble(ParseExpr(ref t, v, ln, gk, ikd, g), CultureInfo.InvariantCulture); 
                    t.TryConsume(')'); return Math.Exp(a);
                }
                if (id.Equals("TAN", StringComparison.OrdinalIgnoreCase)) {
                    double a = Convert.ToDouble(ParseExpr(ref t, v, ln, gk, ikd, g), CultureInfo.InvariantCulture); 
                    t.TryConsume(')'); return Math.Tan(a * Math.PI / 180.0);
                }
                if (id.Equals("ATN", StringComparison.OrdinalIgnoreCase)) {
                    double a = Convert.ToDouble(ParseExpr(ref t, v, ln, gk, ikd, g), CultureInfo.InvariantCulture); 
                    t.TryConsume(')'); return Math.Atan(a) * 180.0 / Math.PI;
                }
                if (id.Equals("MIN", StringComparison.OrdinalIgnoreCase)) {
                    double a = Convert.ToDouble(ParseExpr(ref t, v, ln, gk, ikd, g), CultureInfo.InvariantCulture); 
                    t.TryConsume(',');
                    double b = Convert.ToDouble(ParseExpr(ref t, v, ln, gk, ikd, g), CultureInfo.InvariantCulture); 
                    t.TryConsume(')'); return Math.Min(a,b);
                }
                if (id.Equals("MAX", StringComparison.OrdinalIgnoreCase)) {
                    double a = Convert.ToDouble(ParseExpr(ref t, v, ln, gk, ikd, g), CultureInfo.InvariantCulture); 
                    t.TryConsume(',');
                    double b = Convert.ToDouble(ParseExpr(ref t, v, ln, gk, ikd, g), CultureInfo.InvariantCulture); 
                    t.TryConsume(')'); return Math.Max(a,b);
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
                if (id.Equals("HIT", StringComparison.OrdinalIgnoreCase))
                {
                    t.TryConsume('(');
                    int id1 = (int)Math.Round(Convert.ToDouble(ParseExpr(ref t, v, ln, gk, ikd, g), CultureInfo.InvariantCulture));
                    t.TryConsume(',');
                    int id2 = (int)Math.Round(Convert.ToDouble(ParseExpr(ref t, v, ln, gk, ikd, g), CultureInfo.InvariantCulture));
                    t.TryConsume(')');
                    return g.SpriteHit(id1, id2) ? 1.0 : 0.0;
                }

                if (id.Equals("HITBOX", StringComparison.OrdinalIgnoreCase))
                {
                    t.TryConsume('(');
                    int id1 = (int)Math.Round(Convert.ToDouble(ParseExpr(ref t, v, ln, gk, ikd, g), CultureInfo.InvariantCulture));
                    t.TryConsume(',');
                    int id2 = (int)Math.Round(Convert.ToDouble(ParseExpr(ref t, v, ln, gk, ikd, g), CultureInfo.InvariantCulture));
                    t.TryConsume(')');
                    return g.SpriteHitBox(id1, id2) ? 1.0 : 0.0;
                }

                if (id.Equals("HITCIRCLE", StringComparison.OrdinalIgnoreCase))
                {
                    t.TryConsume('(');
                    int id1 = (int)Math.Round(Convert.ToDouble(ParseExpr(ref t, v, ln, gk, ikd, g), CultureInfo.InvariantCulture));
                    t.TryConsume(',');
                    int id2 = (int)Math.Round(Convert.ToDouble(ParseExpr(ref t, v, ln, gk, ikd, g), CultureInfo.InvariantCulture));
                    t.TryConsume(')');
                    return g.SpriteHitCircle(id1, id2) ? 1.0 : 0.0;
                }

                if (id.Equals("HITGROUP", StringComparison.OrdinalIgnoreCase))
                {
                    t.TryConsume('(');
                    int    id1   = (int)Math.Round(Convert.ToDouble(ParseExpr(ref t, v, ln, gk, ikd, g), CultureInfo.InvariantCulture));
                    t.TryConsume(',');
                    string group = ValueToString(ParseExpr(ref t, v, ln, gk, ikd, g));
                    t.TryConsume(')');
                    return (double)g.SpriteHitGroup(id1, group);
                }

                if (id.Equals("HITBOXGROUP", StringComparison.OrdinalIgnoreCase))
                {
                    t.TryConsume('(');
                    int    id1   = (int)Math.Round(Convert.ToDouble(ParseExpr(ref t, v, ln, gk, ikd, g), CultureInfo.InvariantCulture));
                    t.TryConsume(',');
                    string group = ValueToString(ParseExpr(ref t, v, ln, gk, ikd, g));
                    t.TryConsume(')');
                    return (double)g.SpriteHitBoxGroup(id1, group);
                }

                if (id.Equals("HITCIRCLEGROUP", StringComparison.OrdinalIgnoreCase))
                {
                    t.TryConsume('(');
                    int    id1   = (int)Math.Round(Convert.ToDouble(ParseExpr(ref t, v, ln, gk, ikd, g), CultureInfo.InvariantCulture));
                    t.TryConsume(',');
                    string group = ValueToString(ParseExpr(ref t, v, ln, gk, ikd, g));
                    t.TryConsume(')');
                    return (double)g.SpriteHitCircleGroup(id1, group);
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
                if (id.Equals("JOYSTATE", StringComparison.OrdinalIgnoreCase))
                {
                    object padObj = ParseExpr(ref t, v, ln, gk, ikd, g);
                    t.TryConsume(',');
                    object btnObj = ParseExpr(ref t, v, ln, gk, ikd, g);
                    t.TryConsume(')');

                    int pad = (int)Math.Round(Convert.ToDouble(padObj, CultureInfo.InvariantCulture));
                    string btn = ValueToString(btnObj);
                    return GamepadManager.IsButtonDown(pad, btn) ? 1.0 : 0.0;
                }
                if (id.Equals("JOYAXIS", StringComparison.OrdinalIgnoreCase))
                {
                    t.TryConsume('(');
                    object padObj = ParseExpr(ref t, v, ln, gk, ikd, g);
                    t.TryConsume(',');
                    object axisObj = ParseExpr(ref t, v, ln, gk, ikd, g);
                    t.TryConsume(')');
                    int    pad  = (int)Math.Round(Convert.ToDouble(padObj, CultureInfo.InvariantCulture));
                    string axis = ValueToString(axisObj);
                    return (double)GamepadManager.GetAxis(pad, axis);
                }
                if (id.Equals("PLAYING", StringComparison.OrdinalIgnoreCase))
                {
                    t.TryConsume('(');
                    object chObj = ParseExpr(ref t, v, ln, gk, ikd, g);
                    t.TryConsume(')');
                    int ch = (int)Math.Round(Convert.ToDouble(chObj, CultureInfo.InvariantCulture));
                    return ChiptuneSynth.IsPlaying(ch) ? 1.0 : 0.0;
                }
                if (id.Equals("ANYPLAYING", StringComparison.OrdinalIgnoreCase))
                {
                    t.TryConsume('(');
                    t.TryConsume(')');
                    return ChiptuneSynth.IsAnyPlaying() ? 1.0 : 0.0;
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
                    
                if (id.Equals("UPPER$", StringComparison.OrdinalIgnoreCase))
                {
                    object val = ParseExpr(ref t, v, ln, gk, ikd, g);
                    t.TryConsume(')');
                    return ValueToString(val).ToUpperInvariant();
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
                    
                if (id.Equals("JOIN$", StringComparison.OrdinalIgnoreCase))
                {
                    // Första argumentet är array-variabeln
                    object arrayObj = ParseExpr(ref t, v, ln, gk, ikd, g);
                    t.TryConsume(',');
                    object separatorObj = ParseExpr(ref t, v, ln, gk, ikd, g);
                    t.TryConsume(')');

                    string separator = ValueToString(separatorObj);
    
                    // Hantera både AmosStringArray och AmosNumericArray
                    if (arrayObj is AmosStringArray strArray)
                    {
                        // Filtrera bort null-värden och sista elementet om det är tomt
                        var validParts = strArray.Data.Where(s => s != null).ToArray();
                        return string.Join(separator, validParts);
                    }
                    else if (arrayObj is AmosNumericArray numArray)
                    {
                        // Konvertera numerisk array till strängar
                        var parts = numArray.Data.Select(d => d.ToString(CultureInfo.InvariantCulture)).ToArray();
                        return string.Join(separator, parts);
                    }
                    else if (arrayObj is IAmosArray genericArray)
                    {
                        // Fallback för andra IAmosArray-implementationer
                        var parts = new string[genericArray.Length];
                        for (int i = 0; i < genericArray.Length; i++)
                            parts[i] = ValueToString(genericArray.Get(i));
                        return string.Join(separator, parts);
                    }
    
                    return ""; // Ingen array hittades
                }                     
                        
                if (id.Equals("SPLIT$", StringComparison.OrdinalIgnoreCase))
                {
                    object strObj = ParseExpr(ref t, v, ln, gk, ikd, g);
                    t.TryConsume(',');
                    object delimiterObj = ParseExpr(ref t, v, ln, gk, ikd, g);
                    t.TryConsume(')');

                    string input = ValueToString(strObj);
                    string delimiter = ValueToString(delimiterObj);
    
                    if (string.IsNullOrEmpty(delimiter))
                    {
                        // Tom delimiter = dela i enskilda tecken
                        var chars = input.Select(c => c.ToString()).ToArray();
                        var result = new AmosStringArray(chars.Length - 1);
                        for (int i = 0; i < chars.Length; i++)
                            result.Data[i] = chars[i];
                        return result;
                    }
    
                    string[] parts = input.Split(new[] { delimiter }, StringSplitOptions.None);
                    var strArray = new AmosStringArray(parts.Length);
                    for (int i = 0; i < parts.Length; i++)
                        strArray.Data[i] = parts[i];
    
                    return strArray;
                }
                        
                if (id.Equals("FIND", StringComparison.OrdinalIgnoreCase))
                {
                    // Första argumentet: sträng att söka i
                    object textObj = ParseExpr(ref t, v, ln, gk, ikd, g);
                    t.TryConsume(',');
    
                    // Andra argumentet: söksträng
                    object searchObj = ParseExpr(ref t, v, ln, gk, ikd, g);
    
                    // Tredje argumentet (valfritt): startposition
                    int startPos = 0;
                    if (t.TryConsume(','))
                    {
                        object startObj = ParseExpr(ref t, v, ln, gk, ikd, g);
                        startPos = (int)Math.Round(Convert.ToDouble(startObj, CultureInfo.InvariantCulture));
                    }
    
                    t.TryConsume(')');

                    string text = ValueToString(textObj);
                    string search = ValueToString(searchObj);
    
                    if (startPos < 0 || startPos >= text.Length) return -1;
    
                    return text.IndexOf(search, startPos);
                }

                if (id.Equals("FINDLAST", StringComparison.OrdinalIgnoreCase))
                {
                    object textObj = ParseExpr(ref t, v, ln, gk, ikd, g);
                    t.TryConsume(',');
                    object searchObj = ParseExpr(ref t, v, ln, gk, ikd, g);
                    t.TryConsume(')');

                    string text = ValueToString(textObj);
                    string search = ValueToString(searchObj);
    
                    return text.LastIndexOf(search);
                }
                        
                if (id.Equals("REPEAT$", StringComparison.OrdinalIgnoreCase))
                {
                    // Första argumentet: sträng att upprepa
                    object strObj = ParseExpr(ref t, v, ln, gk, ikd, g);
                    t.TryConsume(',');
    
                    // Andra argumentet: antal gånger
                    object countObj = ParseExpr(ref t, v, ln, gk, ikd, g);
                    t.TryConsume(')');

                    string str = ValueToString(strObj);
                    int count = (int)Math.Round(Convert.ToDouble(countObj, CultureInfo.InvariantCulture));
    
                    if (count <= 0) return "";
    
                    // Optimera för enstaka tecken
                    if (str.Length == 1)
                    {
                        return new string(str[0], count);
                    }
    
                    return string.Concat(Enumerable.Repeat(str, count));
                }

                if (id.Equals("SPRITE#X", StringComparison.OrdinalIgnoreCase))
                {
                    int val = Convert.ToInt32(ParseExpr(ref t, v, ln, gk, ikd, g), CultureInfo.InvariantCulture); 
                    t.TryConsume(')');
                    return (double)g.GetSprite(val).X;     
                }
                if (id.Equals("SPRITE#Y", StringComparison.OrdinalIgnoreCase))
                {
                    int val = Convert.ToInt32(ParseExpr(ref t, v, ln, gk, ikd, g), CultureInfo.InvariantCulture); 
                    t.TryConsume(')');
                    return (double)g.GetSprite(val).Y;     
                }
                if (id.Equals("SPRITE#FRAME", StringComparison.OrdinalIgnoreCase))
                {
                    int val = Convert.ToInt32(ParseExpr(ref t, v, ln, gk, ikd, g), CultureInfo.InvariantCulture); 
                    t.TryConsume(')');
                    return (double)g.GetSprite(val).CurrentFrame;     
                }
                if (id.Equals("SPRITE#FRAMECOUNT", StringComparison.OrdinalIgnoreCase))
                {
                    int val = Convert.ToInt32(ParseExpr(ref t, v, ln, gk, ikd, g), CultureInfo.InvariantCulture); 
                    t.TryConsume(')');
                    return (double)g.GetSprite(val).Frames.Count;      
                }                
                if (id.Equals("SPRITE#WIDTH", StringComparison.OrdinalIgnoreCase))
                {
                    int val = Convert.ToInt32(ParseExpr(ref t, v, ln, gk, ikd, g), CultureInfo.InvariantCulture); 
                    t.TryConsume(')');
                    return (double)g.GetSprite(val).Width;      
                }  
                if (id.Equals("SPRITE#HEIGHT", StringComparison.OrdinalIgnoreCase))
                {
                    int val = Convert.ToInt32(ParseExpr(ref t, v, ln, gk, ikd, g), CultureInfo.InvariantCulture); 
                    t.TryConsume(')');
                    return (double)g.GetSprite(val).Height;      
                } 
                if (id.Equals("SPRITE#ZOOMX", StringComparison.OrdinalIgnoreCase))
                {
                    int val = Convert.ToInt32(ParseExpr(ref t, v, ln, gk, ikd, g), CultureInfo.InvariantCulture); 
                    t.TryConsume(')');
                    return (double)g.GetSprite(val).ZoomX;      
                }
                if (id.Equals("SPRITE#ZOOMY", StringComparison.OrdinalIgnoreCase))
                {
                    int val = Convert.ToInt32(ParseExpr(ref t, v, ln, gk, ikd, g), CultureInfo.InvariantCulture); 
                    t.TryConsume(')');
                    return (double)g.GetSprite(val).ZoomY;      
                } 
                if (id.Equals("SPRITE#ANGLE", StringComparison.OrdinalIgnoreCase))
                {
                    int val = Convert.ToInt32(ParseExpr(ref t, v, ln, gk, ikd, g), CultureInfo.InvariantCulture); 
                    t.TryConsume(')');
                    return (double)g.GetSprite(val).Angle;      
                } 
                if (id.Equals("SPRITE#VISIBLE", StringComparison.OrdinalIgnoreCase))
                {
                    int val = Convert.ToInt32(ParseExpr(ref t, v, ln, gk, ikd, g), CultureInfo.InvariantCulture); 
                    t.TryConsume(')');
                    return g.GetSprite(val).Visible ? 1.0 : 0.0; 
                } 
                // FADING(id) — returnerar 1.0 om sprite har pågående fade, annars 0.0
                if (id.Equals("FADING", StringComparison.OrdinalIgnoreCase))
                {
                    int sprId = Convert.ToInt32(ParseExpr(ref t, v, ln, gk, ikd, g), CultureInfo.InvariantCulture);
                    t.TryConsume(')');
                    return g.IsSpriteAFading(sprId) ? 1.0 : 0.0;
                }
                if (id.Equals("LAYERFADING", StringComparison.OrdinalIgnoreCase))
                {
                    int layerId = Convert.ToInt32(ParseExpr(ref t, v, ln, gk, ikd, g), CultureInfo.InvariantCulture);
                    t.TryConsume(')');
                    return g.IsLayerFading(layerId) ? 1.0 : 0.0;
                }
                if (id.Equals("EOF", StringComparison.OrdinalIgnoreCase))
                {
                    // Syntax: EOF(channel)
                    object channelObj = ParseExpr(ref t, v, ln, gk, ikd, g);
                    t.TryConsume(')');
    
                    int channel = (int)Math.Round(Convert.ToDouble(channelObj, CultureInfo.InvariantCulture));
    
                    // Kolla om kanalen finns
                    if (!_openFiles.ContainsKey(channel))
                    {
                        // Om kanalen inte är öppen, returnera 1 (true/EOF)
                        // Alternativt: throw new Exception($"EOF: Kanal {channel} är inte öppen");
                        return 1;
                    }
    
                    var fileChannel = _openFiles[channel];
    
                    // Kolla att det är en läskanal
                    if (fileChannel.Mode != FileMode.Input)
                    {
                        throw new Exception($"EOF: Kanal {channel} är inte öppnad för läsning");
                    }
    
                    if (fileChannel.Reader == null)
                    {
                        return 1; // Ingen reader = EOF
                    }
    
                    // Returnera 1 om EOF, annars 0
                    return fileChannel.Reader.EndOfStream ? 1 : 0;
                }
                        
                if (id.Equals("DIM", StringComparison.OrdinalIgnoreCase))
                {
                    // DIM(A) eller DIM(A()) returnerar arrayens storlek
                    // Vi läser namnet direkt utan att parsa som uttryck
                    t.SkipWs();
                    if (!t.TryReadIdentifier(out var arrName))
                        throw new Exception($"DIM() expects array name at line {ln}");
                            
                    // Skippa eventuella tomma parenteser: A()
                    t.SkipWs();
                    if (t.TryConsume('('))
                    {
                        t.SkipWs();
                        t.TryConsume(')');
                    }
                            
                    // Stäng DIM-funktionens parentes
                    t.SkipWs();
                    t.TryConsume(')');
                            
                    if (v.TryGetValue(arrName, out var arrObj2) && arrObj2 is IAmosArray arr2)
                    {
                        // Returnera storleken (längd - 1 eftersom AMOS indexerar 0-baserat men DIM A(10) ger 11 element)
                        return (double)(arr2.Length);
                    }
                            
                    throw new Exception($"Array '{arrName}' not found at line {ln}");
                }

                
                // Om vi kommer hit och det inte var en funktion, kolla om det är en array
                if (v.TryGetValue(id, out var arrObj) && arrObj is IAmosArray array)
                {
                    var indices = new List<int>();

                    // Första uttrycket (vi är redan efter '(' när vi kommer hit)
                    while (true)
                    {
                        // Parse ett index-uttryck (kan vara siffra, variabel, expr, funktion etc)
                        object exprVal = ParseExpr(ref t, v, ln, gk, ikd, g);

                        // Konvertera till int-index
                        var d = Convert.ToDouble(exprVal, CultureInfo.InvariantCulture);
                        indices.Add((int)Math.Round(d));

                        // Om komma → fortsätt läsa fler dimensioner
                        if (t.TryConsume(','))
                            continue;

                        // Om ) → klart
                        if (t.TryConsume(')'))
                            break;

                        // Annars syntaxfel
                        throw new Exception("Syntax error in array index expression");
                    }

                    return array.Get(indices.ToArray());
                }
                
                throw new Exception($"Unknown function: {id} at line {ln}");

                // ... existing code ...
            }
            if (v.TryGetValue(id, out var valVar)) return valVar;
            return 0.0;
        }
        return 0.0;
    }
    
    public static string ValueToString(object? v)
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
    
    private static void PlayEffect(string file, AudioEngine? engine, bool loop) {
        if (engine == null) return;
        engine.PlaySample(file, loop);
    }
    
    private static void StopEffect(string file, AudioEngine? engine) {
        if (engine == null) return;
        // Om filsträngen är tom, stoppa allt!
        if (string.IsNullOrEmpty(file))
        {
            engine.StopAllSamples();
        }
        else
        {
            engine.StopSample(file);
        }
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
    
    static int IndexOfWordOutsideQuotes(string s, string word)
    {
        bool inQ = false;
        for (int i = 0; i <= s.Length - word.Length; i++)
        {
            if (s[i] == '"') { inQ = !inQ; continue; }
            if (!inQ && string.Compare(s, i, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) == 0)
            {
                // Kontrollera att det är ett helt ord (omgivet av mellanslag/radslut)
                bool prevOk = i == 0 || !char.IsLetterOrDigit(s[i - 1]);
                bool nextOk = i + word.Length >= s.Length || !char.IsLetterOrDigit(s[i + word.Length]);
                if (prevOk && nextOk) return i;
            }
        }
        return -1;
    }
    
    private static string StripLeadingLineNumber(string l) { var i = 0; while (i < l.Length && char.IsDigit(l[i])) i++; return l[i..].Trim(); }
    private static List<string> SplitCsvOrSpaces(string a) {
        if (string.IsNullOrWhiteSpace(a)) return new List<string>();
        return a.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }
    private static List<string> SplitCsv(string a) {
        if (string.IsNullOrWhiteSpace(a)) return new List<string>();
        return a.Split(new[] { ','}, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }
    
    static (string cmd, string rest) SplitFirstWord(string s)
    {
        s = s.Trim();
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == ' ')
                return (s[..i].Trim(), s[(i + 1)..].Trim());
        }
        return (s, "");
    }
    
    private static List<string> SplitArgsRespectQuotes(string input)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(input)) return result;

        bool inQuotes = false;
        var current = new StringBuilder();

        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];

            if (c == '"')
            {
                inQuotes = !inQuotes;
                continue; // ta bort citattecken
            }

            if (!inQuotes && (c == ',' || char.IsWhiteSpace(c)))
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0)
            result.Add(current.ToString());

        return result;
    }
    //private static Color ParseColor(string t) { try { return Color.Parse(t); } catch { return Colors.Transparent; } }
    private static Color ParseColor(string t) { try { return ParseColorFlexible(t); } catch { return Colors.Transparent; } }

    private static int IndexOfWord(string t, string w) => t.ToUpperInvariant().IndexOf(w.ToUpperInvariant());

    private ref struct Tokenizer {
        private readonly string _s; private int _i;
        public Tokenizer(string s) { _s = s; _i = 0; }
        public void SkipWs() { while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++; }
        public bool TryConsume(char c) { SkipWs(); if (_i < _s.Length && _s[_i] == c) { _i++; return true; } return false; }
        public bool TryReadInt(out int v) { SkipWs(); var s = _i; while (_i < _s.Length && (char.IsDigit(_s[_i]) || (_i == s && _s[_i] == '-'))) _i++; return int.TryParse(_s[s.._i], out v); }
        public bool TryReadDouble(out double v) { 
            SkipWs(); 
            
            // ✅ NYTT: Kolla om det är TRUE/FALSE/ON/OFF
            var bookmark = _i;
            if (TryReadIdentifier(out string possibleKeyword))
            {
                var upper = possibleKeyword.ToUpperInvariant();
                if (upper == "TRUE" || upper == "ON")
                {
                    v = 1.0;
                    return true;
                }
                if (upper == "FALSE" || upper == "OFF")
                {
                    v = 0.0;
                    return true;
                }
                // Om det inte var ett keyword, backa och fortsätt med normal parsing
                _i = bookmark;
            }
            
            var s = _i; 
            while (_i < _s.Length && (char.IsDigit(_s[_i]) || _s[_i] == '.' || (_i == s && _s[_i] == '-'))) _i++; 
            return double.TryParse(_s[s.._i], CultureInfo.InvariantCulture, out v); 
        }
        public bool TryReadIdentifier(out string n) { SkipWs(); var s = _i; while (_i < _s.Length && (char.IsLetterOrDigit(_s[_i]) || _s[_i] == '$' || _s[_i] == '_' || _s[_i] == '#')) _i++; n = _s[s.._i]; return n.Length > 0; }
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

    /// <summary>
    /// Initialisera en multidimensionell array från en sträng med nested brackets
    /// Exempel: [[1,2,3],[4,5,6]] för 2D array
    /// </summary>
    private static void InitializeArrayFromString(
        IAmosArray array, 
        string initValues, 
        int[] dimensions,
        Dictionary<string, object> vars,
        int ln,
        Func<string> getInkey,
        Func<string, bool> isKeyDown,
        AmosGraphics graphics)
    {
        initValues = initValues.Trim();
    
        // 1D array: [1,2,3,4,5]
        if (dimensions.Length == 1)
        {
            if (initValues.StartsWith("[")) initValues = initValues[1..];
            if (initValues.EndsWith("]")) initValues = initValues[..^1];
        
            var values = SplitTopLevelCsv(initValues);
            for (int i = 0; i < values.Count && i <= dimensions[0]; i++)
            {
                var val = EvalValue(values[i].Trim(), vars, ln, getInkey, isKeyDown, graphics);
                array.Set(val, i);
            }
        }
        // 2D array: [[1,2,3],[4,5,6],[7,8,9]]
        else if (dimensions.Length == 2)
        {
            var rows = ParseNestedArray(initValues);
            for (int y = 0; y < rows.Count && y <= dimensions[0]; y++)
            {
                var cols = SplitTopLevelCsv(rows[y]);
                for (int x = 0; x < cols.Count && x <= dimensions[1]; x++)
                {
                    var val = EvalValue(cols[x].Trim(), vars, ln, getInkey, isKeyDown, graphics);
                    array.Set(val, y, x);
                }
            }
        }
        // 3D array: [[[1,2],[3,4]],[[5,6],[7,8]]]
        else if (dimensions.Length == 3)
        {
            var planes = ParseNestedArray(initValues);
            for (int z = 0; z < planes.Count && z <= dimensions[0]; z++)
            {
                var rows = ParseNestedArray(planes[z]);
                for (int y = 0; y < rows.Count && y <= dimensions[1]; y++)
                {
                    var cols = SplitTopLevelCsv(rows[y]);
                    for (int x = 0; x < cols.Count && x <= dimensions[2]; x++)
                    {
                        var val = EvalValue(cols[x].Trim(), vars, ln, getInkey, isKeyDown, graphics);
                        array.Set(val, z, y, x);
                    }
                }
            }
        }
        else
        {
            throw new Exception($"Array initialization for {dimensions.Length}D arrays not yet supported");
        }
    }

    /// <summary>
    /// Parsa nested brackets: [[1,2],[3,4]] -> ["1,2", "3,4"]
    /// </summary>
    private static List<string> ParseNestedArray(string str)
    {
        str = str.Trim();
        if (str.StartsWith("[")) str = str[1..];
        if (str.EndsWith("]")) str = str[..^1];
    
        var result = new List<string>();
        int depth = 0;
        int start = 0;
    
        for (int i = 0; i < str.Length; i++)
        {
            if (str[i] == '[') depth++;
            else if (str[i] == ']') depth--;
            else if (str[i] == ',' && depth == 0)
            {
                result.Add(str[start..i].Trim());
                start = i + 1;
            }
        }
    
        if (start < str.Length)
            result.Add(str[start..].Trim());
    
        // Ta bort yttre brackets från varje element
        for (int i = 0; i < result.Count; i++)
        {
            var elem = result[i].Trim();
            if (elem.StartsWith("[") && elem.EndsWith("]"))
                result[i] = elem[1..^1];
        }
    
        return result;
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