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
    private sealed class ForFrame { public required string VarName; public required int EndValue; public required int StepValue; public required int LineAfterForPc; public required int ForLineNumber; }
    private static readonly Random _rng = new();
    private static System.Diagnostics.Process? _currentMusicProcess; // Musik-kanalen

    
    public static async Task ExecuteAsync(string programText, Func<string, Task> appendLineAsync, Func<Task> clearAsync, AmosGraphics graphics, Action onGraphicsChanged, Func<string> getInkey, Func<string, bool> isKeyDown, CancellationToken token)
    {
        var vars = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        var forStack = new Stack<ForFrame>();
        var gosubStack = new Stack<int>();
        var lines = programText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        
        var labels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var ifJumps = new Dictionary<int, int>(); // PC -> PC (Vart IF hoppar om falskt)
        var elseJumps = new Dictionary<int, int>(); // PC -> PC (Vart ELSE hoppar för att skippa till ENDIF)
        var controlStack = new Stack<int>();

        // Pre-scan: Labels OCH IF/ELSE/ENDIF logik
        for (int i = 0; i < lines.Length; i++) {
            var rawLine = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(rawLine)) continue;
                
            // Labels
            var firstWord = rawLine.Split(' ')[0];
            if (int.TryParse(firstWord, out _)) labels[firstWord] = i;
            if (rawLine.EndsWith(':')) labels[rawLine.TrimEnd(':').Trim()] = i;

            // IF/ELSE/ENDIF Mapping
            var lineForScan = StripLeadingLineNumber(StripComments(rawLine)).ToUpperInvariant();
            if (lineForScan.StartsWith("IF ") && !lineForScan.Contains("THEN")) {
                controlStack.Push(i);
            } else if (lineForScan == "ELSE") {
                if (controlStack.Count > 0) {
                    int ifPc = controlStack.Pop();
                    ifJumps[ifPc] = i + 1; // IF hoppar till raden EFTER ELSE om falskt
                    controlStack.Push(i);  // ELSE väntar på ENDIF
                }
            } else if (lineForScan == "ENDIF") {
                if (controlStack.Count > 0) {
                    int sourcePc = controlStack.Pop();
                    var sourceLine = StripLeadingLineNumber(StripComments(lines[sourcePc].Trim())).ToUpperInvariant();
                    if (sourceLine.StartsWith("IF ")) {
                        ifJumps[sourcePc] = i + 1; // IF utan ELSE hoppar till raden EFTER ENDIF
                    } else {
                        elseJumps[sourcePc] = i + 1; // ELSE hoppar till raden EFTER ENDIF
                    }
                }
            }
        }

        int pc = 0;
        while (pc < lines.Length) {
            token.ThrowIfCancellationRequested();
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
                        vars[n] = EvalValue(vt, vars, ln, getInkey, isKeyDown, graphics); 
                        break;
                    case "WAIT": 
                        if (arg.ToUpperInvariant() == "VBL") {
                            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Render);
                            await Task.Delay(16, token);
                        } else await Task.Delay(Math.Max(0, EvalInt(arg, vars, ln, getInkey, isKeyDown, graphics)), token);
                        break;
                        case "IF":
                            var tIdx = IndexOfWord(arg, "THEN");
                            string condition;
                            string? remainingCmds = null;

                            if (tIdx >= 0) {
                                condition = arg[..tIdx].Trim();
                                var thenContent = arg[(tIdx + 4)..].Trim();
                                if (!string.IsNullOrEmpty(thenContent)) remainingCmds = thenContent;
                            } else {
                                // Om det inte finns THEN, kollar vi om det finns kommandon direkt efter villkoret
                                condition = arg;
                                // EvalCondition kommer att försöka tolka så mycket den kan.
                            }

                            if (EvalCondition(condition, vars, ln, getInkey, isKeyDown, graphics)) {
                                if (remainingCmds != null) {
                                    var thenCmds = SplitMultipleCommands(remainingCmds);
                                    foreach (var tc in thenCmds) {
                                        var (tc_cmd, tc_arg) = SplitCommand(tc);
                                        if (await ExecuteSingleStatementAsync(tc_cmd, tc_arg, vars, appendLineAsync, clearAsync, graphics, onGraphicsChanged, getInkey, isKeyDown, token, ln)) { 
                                            jumpHappened = true; break; 
                                        }
                                    }
                                    // Om vi har ett tillhörande ELSE/ENDIF block, hoppa över det
                                    if (ifJumps.TryGetValue(pc, out var target)) {
                                        pc = target; 
                                        jumpHappened = true;
                                    } else {
                                        goto next_line;
                                    }
                                }
                            } else {
                                if (ifJumps.TryGetValue(pc, out var target)) {
                                    pc = target;
                                    jumpHappened = true;
                                } else {
                                    goto next_line;
                                }
                            }
                            break;
                    case "ELSE":
                        if (elseJumps.TryGetValue(pc, out var eTarget)) {
                            pc = eTarget;
                            jumpHappened = true;
                        }
                        break;
                    case "ENDIF":
                        // Bara en markör, gå till nästa rad
                        break;
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
                        vars[fV] = start; 
                        forStack.Push(new ForFrame { VarName = fV, EndValue = end, StepValue = step, LineAfterForPc = pc + 1, ForLineNumber = ln });
                        break;
                    case "NEXT":
                        if (forStack.Count == 0) break;
                        var f = forStack.Peek(); var cur = GetIntVar(f.VarName, vars, ln) + f.StepValue; vars[f.VarName] = cur;
                        if (f.StepValue > 0 ? cur <= f.EndValue : cur >= f.EndValue) { pc = f.LineAfterForPc; jumpHappened = true; } else forStack.Pop();
                        break;
                    case "SCREEN":
                        var screenArgs = SplitCsvOrSpaces(arg);
                        if (screenArgs.Count > 0 && screenArgs[0].Equals("SELECT", StringComparison.OrdinalIgnoreCase)) {
                            if (screenArgs.Count >= 2) graphics.SetDrawingScreen(EvalInt(screenArgs[1], vars, ln, getInkey, isKeyDown, graphics));
                        } else if (screenArgs.Count >= 2) {
                            graphics.Screen(EvalInt(screenArgs[0], vars, ln, getInkey, isKeyDown, graphics), EvalInt(screenArgs[1], vars, ln, getInkey, isKeyDown, graphics));
                            graphics.Clear(Colors.Black);
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
                    case "CLSG": graphics.Clear(Colors.Black); onGraphicsChanged(); break;
                    case "LOAD": graphics.LoadBackground(Unquote(arg)); onGraphicsChanged(); break;
                    case "INK": graphics.Ink = ParseColor(arg); break;
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
                        if (!int.TryParse(ss[0], out var sid)) {
                            var sub = ss[0].ToUpperInvariant();
                            if (sub=="POS") graphics.SpritePos(EvalInt(ss[1],vars,ln,getInkey,isKeyDown, graphics), EvalInt(ss[2],vars,ln,getInkey,isKeyDown, graphics), EvalInt(ss[3],vars,ln,getInkey,isKeyDown, graphics));
                            else if (sub=="LOAD") graphics.LoadSprite(EvalInt(ss[1], vars, ln, getInkey, isKeyDown, graphics), Unquote(ss[2]));
                            else if (sub=="ADDFRAME") graphics.AddFrame(EvalInt(ss[1], vars, ln, getInkey, isKeyDown, graphics), Unquote(ss[2]));
                            else if (sub=="FRAME") graphics.SetSpriteFrame(EvalInt(ss[1], vars, ln, getInkey, isKeyDown, graphics), EvalInt(ss[2], vars, ln, getInkey, isKeyDown, graphics));
                            else if (sub=="HANDLE") graphics.SpriteHandle(EvalInt(ss[1],vars,ln,getInkey,isKeyDown, graphics), EvalInt(ss[2],vars,ln,getInkey,isKeyDown, graphics), EvalInt(ss[3],vars,ln,getInkey,isKeyDown, graphics));
                            else if (sub=="ON") graphics.SpriteOn(EvalInt(ss[1],vars,ln,getInkey,isKeyDown, graphics));
                            else if (sub=="OFF") graphics.SpriteOff(EvalInt(ss[1],vars,ln,getInkey,isKeyDown, graphics));
                        } else graphics.CreateSprite(sid, EvalInt(ss[1],vars,ln,getInkey,isKeyDown, graphics), EvalInt(ss[2],vars,ln,getInkey,isKeyDown, graphics)); 
                        break;
                    case "SAM":
                        var samArgs = SplitCsvOrSpaces(arg);
                        if (samArgs.Count >= 2 && samArgs[0].ToUpperInvariant() == "PLAY") {
                            PlayEffect(Unquote(samArgs[1])); // Ljudeffekt
                        }
                        break;
                    case "MUSIC":
                        var musArgs = SplitCsvOrSpaces(arg);
                        if (musArgs.Count >= 2 && musArgs[0].ToUpperInvariant() == "PLAY") {
                            PlayMusic(Unquote(musArgs[1])); // Bakgrundsmusik
                        } else if (musArgs.Count >= 1 && musArgs[0].ToUpperInvariant() == "STOP") {
                            StopMusic();
                        }
                        break;;
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
                    default: if (!string.IsNullOrWhiteSpace(cmd)) throw new Exception($"Syntax Error: '{cmd}' at line {ln}"); break;
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

    private static async Task<bool> ExecuteSingleStatementAsync(string cmd, string arg, Dictionary<string, object> vars, Func<string, Task> al, Func<Task> cl, AmosGraphics g, Action og, Func<string> gk, Func<string, bool> ikd, CancellationToken t, int ln)
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
            case "LOCATE": var p = SplitCsvOrSpaces(arg); await al($"@@LOCATE {EvalInt(p[0], vars, ln, gk, ikd, g)} {EvalInt(p[1], vars, ln, gk, ikd, g)}"); return false;
            case "LET": 
                var (n, vt) = SplitAssignment(arg); 
                vars[n] = EvalValue(vt, vars, ln, gk, ikd, g); 
                return false;
            case "PLOT": var pp = SplitCsvOrSpaces(arg); g.Plot(EvalInt(pp[0], vars, ln, gk, ikd, g), EvalInt(pp[1], vars, ln, gk, ikd, g)); og(); return false;
            case "SPRITE":
                var ss = SplitCsvOrSpaces(arg);
                if (ss.Count >= 2) {
                    var sub = ss[0].ToUpperInvariant();
                    if (sub == "POS") g.SpritePos(EvalInt(ss[1], vars, ln, gk, ikd, g), EvalInt(ss[2], vars, ln, gk, ikd, g), EvalInt(ss[3], vars, ln, gk, ikd, g));
                    else if (sub == "FRAME") g.SetSpriteFrame(EvalInt(ss[1], vars, ln, gk, ikd, g), EvalInt(ss[2], vars, ln, gk, ikd, g));
                    else if (sub == "ON") g.SpriteOn(EvalInt(ss[1], vars, ln, gk, ikd, g));
                    else if (sub == "OFF") g.SpriteOff(EvalInt(ss[1], vars, ln, gk, ikd, g));
                }
                return false;
            case "WAIT":
                if (arg.ToUpperInvariant() == "VBL") {
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Render);
                    await Task.Delay(16, t);
                } else await Task.Delay(Math.Max(0, EvalInt(arg, vars, ln, gk, ikd, g)), t);
                return false;
            case "VSYNC": await al("@@VSYNC"); return false;
            case "REFRESH": g.Refresh(); og(); return false;
            case "SAM":
                var sa = SplitCsvOrSpaces(arg);
                if (sa.Count >= 2 && sa[0].ToUpperInvariant() == "PLAY") {
                    PlayEffect(Unquote(sa[1]));
                }
                return false; // VIKTIGT: Lägg till denna rad!
            case "END": return true;
            default: if (!string.IsNullOrWhiteSpace(cmd)) throw new Exception($"Syntax Error in IF-THEN: Unknown command '{cmd}' at line {ln}"); return false;
        }
    }

    private static bool EvalCondition(string c, Dictionary<string, object> v, int ln, Func<string> gk, Func<string, bool> ikd, AmosGraphics g)
    {
        if (!c.Contains('=') && !c.Contains('<') && !c.Contains('>')) return Convert.ToInt32(EvalValue(c, v, ln, gk, ikd, g)) != 0;
        var ops = new[] { "<>", "<=", ">=", "=", "<", ">" };
        foreach (var op in ops) {
            var i = c.IndexOf(op); if (i < 0) continue;
            var lV = EvalValue(c[..i].Trim(), v, ln, gk, ikd, g); var rV = EvalValue(c[(i + op.Length)..].Trim(), v, ln, gk, ikd, g);
            if (lV is string || rV is string) { var ls = ValueToString(lV); var rs = ValueToString(rV); return op == "=" ? ls == rs : ls != rs; }
            var li = Convert.ToInt32(lV); var ri = Convert.ToInt32(rV);
            return op switch { "=" => li == ri, "<>" => li != ri, "<" => li < ri, ">" => li > ri, "<=" => li <= ri, ">=" => li >= ri, _ => false };
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
        return EvalInt(t, v, ln, gk, ikd, g);
    }

    private static int EvalInt(string val, Dictionary<string, object> v, int ln, Func<string> gk, Func<string, bool> ikd, AmosGraphics g) { 
        if (string.IsNullOrWhiteSpace(val)) return 0;
        var t = new Tokenizer(val); return ParseExpr(ref t, v, ln, gk, ikd, g); 
    }

    private static int ParseExpr(ref Tokenizer t, Dictionary<string, object> v, int ln, Func<string> gk, Func<string, bool> ikd, AmosGraphics g) {
        var res = ParseTerm(ref t, v, ln, gk, ikd, g);
        while (true) { if (t.TryConsume('+')) res += ParseTerm(ref t, v, ln, gk, ikd, g); else if (t.TryConsume('-')) res -= ParseTerm(ref t, v, ln, gk, ikd, g); else break; }
        return res;
    }

    private static int ParseTerm(ref Tokenizer t, Dictionary<string, object> v, int ln, Func<string> gk, Func<string, bool> ikd, AmosGraphics g) {
        var res = ParseFactor(ref t, v, ln, gk, ikd, g);
        while (true) { 
            if (t.TryConsume('*')) res *= ParseFactor(ref t, v, ln, gk, ikd, g); 
            else if (t.TryConsume('/')) {
                var d = ParseFactor(ref t, v, ln, gk, ikd, g);
                res = (d == 0) ? 0 : res / d;
            } else break; 
        }
        return res;
    }

    private static int ParseFactor(ref Tokenizer t, Dictionary<string, object> v, int ln, Func<string> gk, Func<string, bool> ikd, AmosGraphics g) {
        t.SkipWs();
        if (t.TryConsume('(')) { var res = ParseExpr(ref t, v, ln, gk, ikd, g); t.TryConsume(')'); return res; }
        if (t.TryReadInt(out var n)) return n;
        if (t.TryReadIdentifier(out var id)) {
            if (id.Equals("INKEY$", StringComparison.OrdinalIgnoreCase)) return gk().Length;
            
            // NYTT: Hämta banans storlek i pixlar
            if (id.Equals("MAP", StringComparison.OrdinalIgnoreCase)) {
                t.SkipWs();
                if (t.TryReadIdentifier(out var sub)) {
                    if (sub.Equals("WIDTH", StringComparison.OrdinalIgnoreCase)) return g.GetMapWidth() * 32; 
                    if (sub.Equals("HEIGHT", StringComparison.OrdinalIgnoreCase)) return g.GetMapHeight() * 32;
                }
            }
            if (id.Equals("HIT", StringComparison.OrdinalIgnoreCase)) {
                t.TryConsume('('); var id1 = ParseExpr(ref t, v, ln, gk, ikd, g); t.TryConsume(','); var id2 = ParseExpr(ref t, v, ln, gk, ikd, g); t.TryConsume(')');
                return g.SpriteHit(id1, id2) ? 1 : 0;
            }
            if (id.Equals("TILE", StringComparison.OrdinalIgnoreCase)) {
                t.TryConsume('(');
                var layer = ParseExpr(ref t, v, ln, gk, ikd, g); 
                t.TryConsume(',');
                var px = ParseExpr(ref t, v, ln, gk, ikd, g); 
                var py = 0;
                if (t.TryConsume(',')) {
                    py = ParseExpr(ref t, v, ln, gk, ikd, g);
                }
                t.TryConsume(')');
                    
                // VIKTIGT: Vi ska INTE använda offset (scroll) här. 
                // TILE ska returnera vad som finns på en specifik position i mappen.
                int tx = px / 32;
                int ty = py / 32;
                    
                return g.GetMapTile(tx, ty);
            }
            if (id.Equals("RND", StringComparison.OrdinalIgnoreCase)) {
                t.TryConsume('('); var m = ParseExpr(ref t, v, ln, gk, ikd, g); t.TryConsume(')'); return _rng.Next(m + 1); 
            }
            if (id.Equals("KEYSTATE", StringComparison.OrdinalIgnoreCase)) {
                t.TryConsume('('); var k = Unquote(t.ReadUntil(')')); t.TryConsume(')'); return ikd(k) ? 1 : 0;
            }
            return GetIntVar(id, v, ln);
        }
        return 0;
    }

    private static string ValueToString(object? v) => v?.ToString() ?? "";
    private static bool IsQuotedString(string t) => t.Length >= 2 && t.StartsWith("\"") && t.EndsWith("\"");
    private static string Unquote(string t) => t.Trim('\"');
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
    
    private static void PlayEffect(string file) {
        try {
            // Starta afplay snabbt för ljudeffekter
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
                FileName = "afplay",
                Arguments = $"\"{file}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            });
        } catch {}
    }

    private static void PlayMusic(string file) {
        try {
            StopMusic(); // Stoppa föregående låt innan ny startar
            _currentMusicProcess = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
                FileName = "xmp",
                Arguments = $"\"{file}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            });
        } catch {}
    }

    public static void StopMusic() {
        try {
            if (_currentMusicProcess != null && !_currentMusicProcess.HasExited) {
                _currentMusicProcess.Kill();
                _currentMusicProcess = null;
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
        public bool TryReadIdentifier(out string n) { SkipWs(); var s = _i; while (_i < _s.Length && (char.IsLetterOrDigit(_s[_i]) || _s[_i] == '$')) _i++; n = _s[s.._i]; return n.Length > 0; }
        public string ReadUntil(char c) { var s = _i; while (_i < _s.Length && _s[_i] != c) _i++; return _s[s.._i]; }
    }
}