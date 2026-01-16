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
        Func<int, Task> waitForStep) // <-- NY PARAMETER: skickar PC, väntar på signal
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

        var gosubStack = new Stack<int>();
        var lines = programText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        
        var labels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var ifJumps = new Dictionary<int, int>(); // PC -> PC (Vart IF hoppar om falskt)
        var elseJumps = new Dictionary<int, int>(); // PC -> PC (Vart ELSE hoppar för att skippa till ENDIF)
            
        var whileMap = new Dictionary<int, int>(); // WHILE pc -> WEND pc
        var wendMap  = new Dictionary<int, int>(); // WEND pc  -> WHILE pc
        var whileScanStack = new Stack<int>();
        var ifStack = new Stack<int>();
        
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
                        var lp = SplitCsvOrSpaces(arg); 
                        await appendLineAsync($"@@LOCATE {EvalInt(lp[0], vars, ln, getInkey, isKeyDown, graphics)} {EvalInt(lp[1], vars, ln, getInkey, isKeyDown, graphics)}"); 
                        break;
                    case "PRINT":
                        var printArg = arg.Trim();
                        if (printArg.ToUpperInvariant().StartsWith("AT ")) {
                            var parts = SplitCsvOrSpaces(printArg.Substring(3));
                            if (parts.Count >= 3) {
                                await appendLineAsync($"@@LOCATE {EvalInt(parts[0], vars, ln, getInkey, isKeyDown, graphics)} {EvalInt(parts[1], vars, ln, getInkey, isKeyDown, graphics)}");
                                await appendLineAsync("@@PRINT " + ValueToString(EvalValue(string.Join(" ", parts.Skip(2)), vars, ln, getInkey, isKeyDown, graphics)));
                            }
                        } else await appendLineAsync("@@PRINT " + ValueToString(EvalValue(arg, vars, ln, getInkey, isKeyDown, graphics)));
                        break;
                    case "LET": 
                        var (n, vt) = SplitAssignment(arg); 
                        setVar(n, EvalValue(vt, vars, ln, getInkey, isKeyDown, graphics)); 
                        break;
                    case "DIM":
                        // Enkel parsing av DIM A(10)
                        var dimParts = arg.Split('(', ')');
                        if (dimParts.Length >= 2)
                        {
                            var arrName = dimParts[0].Trim();
                            var size = EvalInt(dimParts[1], vars, ln, getInkey, isKeyDown, graphics);
                            vars[arrName] = new AmosArray(size);
                        }
                        break;
                    case "WAIT": 
                        if (arg.ToUpperInvariant() == "VBL")
                        {
                            // Stega upp timern för alla lager innan swap
                            lock(graphics.LockObject) {
                                foreach(var layer in graphics.InactiveFrame) {
                                    layer.Timer += 0.05f; // Justera hastigheten här
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
                    case "INK": graphics.Ink = ParseColor(arg); break;
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
                        // Hämta det nuvarande aktiva lagret istället för hårdkodat 0
                        int currentLayer = graphics.GetActiveScreenNumber();

                        if (arg.ToUpperInvariant().StartsWith("STR("))
                        {
                            int closeParen = arg.IndexOf(')');
                            int rbNum = EvalInt(arg.Substring(4, closeParen - 4), vars, ln, getInkey, isKeyDown, graphics);
                            int eqIdx = arg.IndexOf('=');
                            if (eqIdx > 0) {
                                string colorStr = Unquote(arg[(eqIdx + 1)..].Trim());
                                var parts = colorStr.Split(',').Select(c => c.Trim()).ToList();
                                var c1 = ParseColor(parts[0]);
                                var c2 = parts.Count > 1 ? ParseColor(parts[1]) : c1;
            
                                // Använd currentLayer!
                                graphics.SetShaderColors(currentLayer, rbNum, c1, c2);
                            }
                        }
                        else {
                            var rbArgs = SplitCsvOrSpaces(arg);
                            if (rbArgs.Count >= 4) {
                                int rbNum = EvalInt(rbArgs[0], vars, ln, getInkey, isKeyDown, graphics);
                                int rbOff = EvalInt(rbArgs[2], vars, ln, getInkey, isKeyDown, graphics);
                                int rbH = EvalInt(rbArgs[3], vars, ln, getInkey, isKeyDown, graphics);
            
                                // Använd currentLayer!
                                graphics.SetShaderParams(currentLayer, rbNum, (float)rbOff, (float)rbH);
                            }
                        }
                        break;
                    case "RAIN": // Format: RAIN typ, densitet
                        var rainArgs = SplitCsvOrSpaces(arg);
                        if (rainArgs.Count >= 2) {
                            int type = EvalInt(rainArgs[0], vars, ln, getInkey, isKeyDown, graphics);
                            float density = (float)EvalDouble(rainArgs[1], vars, ln, getInkey, isKeyDown, graphics);
                            int curL = graphics.GetActiveScreenNumber();
                            graphics.SetShaderParams(curL, 22, (float)type, 0); // Typ i slot 22
                            graphics.SetShaderParams(curL, 23, density, 0);     // Densitet i slot 23
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
                            // Vi läser filen asynkront
                            var path = Unquote(mArgs[1]);
                            using var fs = System.IO.File.OpenRead(path);
                            // Vi kan återanvända samma logik som i editorn här för att fylla _gfx
                            // Men för att hålla det enkelt just nu, kan du ladda banan via projektet.
                        }
                        break;
                    case "END": return;
                        default: 
                            if (!string.IsNullOrWhiteSpace(cmd)) {
                                if (fullCmd.Contains('=')) {
                                    // Vi använder SplitAssignment för att dela upp på '='
                                    var (leftSide, varValue) = SplitAssignment(fullCmd);
                                    var rightValue = EvalDouble(varValue, vars, ln, getInkey, isKeyDown, graphics);

                                    if (leftSide.Contains('(')) {
                                        // Hantera array-tilldelning: A(5) = 10
                                        int openParen = leftSide.IndexOf('(');
                                        int closeParen = leftSide.LastIndexOf(')');
                                        if (openParen != -1 && closeParen != -1) {
                                            var aName = leftSide[..openParen].Trim();
                                            var idxStr = leftSide[(openParen + 1)..closeParen];
                                            
                                            if (vars.TryGetValue(aName, out var aVal) && aVal is AmosArray array) {
                                                var aIdx = (int)Math.Round(EvalDouble(idxStr, vars, ln, getInkey, isKeyDown, graphics));
                                                if (aIdx >= 0 && aIdx < array.Data.Length) {
                                                    array.Data[aIdx] = rightValue;
                                                }
                                            }
                                        }
                                    } else {
                                        // Vanlig variabel
                                        setVar(leftSide, rightValue);
                                    }
                                } else {
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
            case "PRINT": await al("@@PRINT " + ValueToString(EvalValue(arg, vars, ln, gk, ikd, g))); return false;
            case "INC": vars[arg] = GetDoubleVar(arg, vars, ln) + 1.0; return false;
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
        t = t.Trim();
        if (t.Equals("INKEY$", StringComparison.OrdinalIgnoreCase)) return gk();
        if (t.StartsWith("KEYSTATE(", StringComparison.OrdinalIgnoreCase)) return ikd(t.Substring(9).TrimEnd(')').Trim('\"')) ? 1 : 0;
        if (IsQuotedString(t)) return Unquote(t);
        return EvalDouble(t, v, ln, gk, ikd, g);
    }

    private static int EvalInt(string val, Dictionary<string, object> v, int ln, Func<string> gk, Func<string, bool> ikd, AmosGraphics g) { 
        // Använd din nya uträkningslogik som returnerar double och runda av
        return (int)Math.Round(EvalDouble(val, v, ln, gk, ikd, g));
    }
    
    private static double EvalDouble(string val, Dictionary<string, object> v, int ln, Func<string> gk, Func<string, bool> ikd, AmosGraphics g) { 
        if (string.IsNullOrWhiteSpace(val)) return 0;
        var t = new Tokenizer(val); return ParseExpr(ref t, v, ln, gk, ikd, g); 
    }


    private static double ParseExpr(ref Tokenizer t, Dictionary<string, object> v, int ln, Func<string> gk, Func<string, bool> ikd, AmosGraphics g) {
        var res = ParseTerm(ref t, v, ln, gk, ikd, g);
        while (true) { if (t.TryConsume('+')) res += ParseTerm(ref t, v, ln, gk, ikd, g); else if (t.TryConsume('-')) res -= ParseTerm(ref t, v, ln, gk, ikd, g); else break; }
        return res;
    }

    private static double ParseTerm(ref Tokenizer t, Dictionary<string, object> v, int ln, Func<string> gk, Func<string, bool> ikd, AmosGraphics g) {
        var res = ParseFactor(ref t, v, ln, gk, ikd, g);
        while (true) { 
            if (t.TryConsume('*')) res *= ParseFactor(ref t, v, ln, gk, ikd, g); 
            else if (t.TryConsume('/')) {
                var d = ParseFactor(ref t, v, ln, gk, ikd, g);
                res = (d == 0) ? 0 : res / d; // Double hanterar division med noll, men vi behåller 0 för säkerhets skull
            } else break; 
        }
        return res;
    }

// ... befintlig kod ...
        private static double ParseFactor(ref Tokenizer t, Dictionary<string, object> v, int ln, Func<string> gk, Func<string, bool> ikd, AmosGraphics g) {
            t.SkipWs();
            if (t.TryConsume('(')) { var res = ParseExpr(ref t, v, ln, gk, ikd, g); t.TryConsume(')'); return res; }
            if (t.TryReadDouble(out var n)) return n; 
            if (t.TryReadIdentifier(out var id)) {
                t.SkipWs();
                
                // 1. Hantera specialfall UTAN parenteser
                if (id.Equals("INKEY$", StringComparison.OrdinalIgnoreCase)) return gk().Length;
                if (id.Equals("MAP", StringComparison.OrdinalIgnoreCase)) {
                    t.SkipWs();
                    if (t.TryReadIdentifier(out var sub)) {
                        if (sub.Equals("WIDTH", StringComparison.OrdinalIgnoreCase)) return g.GetMapWidth() * 32; 
                        if (sub.Equals("HEIGHT", StringComparison.OrdinalIgnoreCase)) return g.GetMapHeight() * 32;
                    }
                }

                // 2. Om det följer en parentese, är det antingen en FUNKTION eller en ARRAY
                if (t.TryConsume('(')) {
                    if (id.Equals("SIN", StringComparison.OrdinalIgnoreCase)) {
                        double a = ParseExpr(ref t, v, ln, gk, ikd, g); t.TryConsume(')'); return Math.Sin(a * Math.PI / 180.0);
                    }
                    if (id.Equals("COS", StringComparison.OrdinalIgnoreCase)) {
                        double a = ParseExpr(ref t, v, ln, gk, ikd, g); t.TryConsume(')'); return Math.Cos(a * Math.PI / 180.0);
                    }
                    if (id.Equals("RND", StringComparison.OrdinalIgnoreCase)) {
                        double m = ParseExpr(ref t, v, ln, gk, ikd, g); t.TryConsume(')'); return _rng.NextDouble() * m;
                    }
                    if (id.Equals("INT", StringComparison.OrdinalIgnoreCase)) {
                        double val = ParseExpr(ref t, v, ln, gk, ikd, g); t.TryConsume(')'); return Math.Floor(val + 0.000001);
                    }
                    if (id.Equals("INC", StringComparison.OrdinalIgnoreCase)) {
                        double val = ParseExpr(ref t, v, ln, gk, ikd, g); t.TryConsume(')'); return val + 1; 
                    }   
                    if (id.Equals("DEC", StringComparison.OrdinalIgnoreCase)) {
                        double val = ParseExpr(ref t, v, ln, gk, ikd, g); t.TryConsume(')'); return val - 1; 
                    }
                    if (id.Equals("HIT", StringComparison.OrdinalIgnoreCase)) {
                        var id1 = (int)Math.Round(ParseExpr(ref t, v, ln, gk, ikd, g)); t.TryConsume(',');
                        var id2 = (int)Math.Round(ParseExpr(ref t, v, ln, gk, ikd, g)); t.TryConsume(')');
                        return g.SpriteHit(id1, id2) ? 1.0 : 0.0;
                    }
                    if (id.Equals("TILE", StringComparison.OrdinalIgnoreCase)) {
                        // 1. Läs layer
                        var layer = (int)Math.Round(ParseExpr(ref t, v, ln, gk, ikd, g));
                        t.TryConsume(',');
                        
                        // 2. Läs X-koordinat
                        var px = (int)Math.Round(ParseExpr(ref t, v, ln, gk, ikd, g));
                        
                        // 3. Kolla om det finns en Y-koordinat (valfritt)
                        var py = 0;
                        if (t.TryConsume(',')) {
                            py = (int)Math.Round(ParseExpr(ref t, v, ln, gk, ikd, g));
                        }
                        
                        t.TryConsume(')');
                        
                        // Returnera tilen på den pixel-positionen (dividerat med tile-storlek 32)
                        return g.GetMapTile(px / 32, py / 32);
                    }
                    if (id.Equals("KEYSTATE", StringComparison.OrdinalIgnoreCase)) {
                        var k = Unquote(t.ReadUntil(')')); t.TryConsume(')'); return ikd(k) ? 1.0 : 0.0;
                    }

                    // ARRAY-KOLL (Om det inte var en funktion ovan)
                    if (v.TryGetValue(id, out var arrayObj) && arrayObj is AmosArray arr) {
                        double rawIdx = ParseExpr(ref t, v, ln, gk, ikd, g);
                        t.TryConsume(')');
                        int aIdx = (int)Math.Floor(rawIdx + 0.000001);
                        if (aIdx >= 0 && aIdx < arr.Data.Length) return arr.Data[aIdx];
                        throw new Exception($"Array index out of bounds: {id}({aIdx}) at line {ln}");
                    }
                    
                    throw new Exception($"Unknown function or array: {id} at line {ln}");
                }
                
                // 3. VANLIG VARIABEL (Ingen parentes alls)
                return GetDoubleVar(id, v, ln);
            }
            return 0.0;
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
            // Om det råkar vara en array vi försöker läsa som ett tal, returnera 0 eller kasta fel
            if (val is AmosArray) return 0.0; 

            // Om variabeln lagrats som en int, float eller double, konvertera säkert
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
}