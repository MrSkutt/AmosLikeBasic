
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

    public static async Task ExecuteAsync(string programText, Func<string, Task> appendLineAsync, Func<Task> clearAsync, AmosGraphics graphics, Action onGraphicsChanged, Func<string> getInkey, Func<string, bool> isKeyDown, CancellationToken token)
    {
        var vars = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        var forStack = new Stack<ForFrame>();
        var gosubStack = new Stack<int>();
        var lines = programText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        
        // 1. Pre-scan for labels (oförändrad)
        var labels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < lines.Length; i++) {
            var rawLine = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(rawLine)) continue;
            var firstWord = rawLine.Split(' ')[0];
            if (int.TryParse(firstWord, out _)) labels[firstWord] = i;
            if (rawLine.EndsWith(':')) labels[rawLine.TrimEnd(':').Trim()] = i;
        }

        int pc = 0;
        while (pc < lines.Length) {
            token.ThrowIfCancellationRequested();
            var ln = pc + 1; 
            var line = StripComments((lines[pc] ?? "").Trim());
            
            if (string.IsNullOrWhiteSpace(line) || line.EndsWith(':')) { pc++; continue; }
            line = StripLeadingLineNumber(line);

            // NYTT: Dela upp raden i flera kommandon (:)
            var commands = SplitMultipleCommands(line);
            bool jumpHappened = false;

            foreach (var fullCmd in commands) {
                if (string.IsNullOrWhiteSpace(fullCmd)) continue;
                var (cmd, arg) = SplitCommand(fullCmd);

                switch (cmd) {
                    case "REM": goto next_line; // REM hoppar över resten av raden
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
                    case "LOCATE": var lp = SplitCsvOrSpaces(arg); await appendLineAsync($"@@LOCATE {EvalInt(lp[0], vars, ln, getInkey, isKeyDown)} {EvalInt(lp[1], vars, ln, getInkey, isKeyDown)}"); break;
                    case "PRINT":
                        var printArg = arg.Trim();
                        if (printArg.ToUpperInvariant().StartsWith("AT ")) {
                            var parts = SplitCsvOrSpaces(printArg.Substring(3));
                            if (parts.Count >= 3) {
                                await appendLineAsync($"@@LOCATE {EvalInt(parts[0], vars, ln, getInkey, isKeyDown)} {EvalInt(parts[1], vars, ln, getInkey, isKeyDown)}");
                                await appendLineAsync("@@PRINT " + ValueToString(EvalValue(string.Join(" ", parts.Skip(2)), vars, ln, getInkey, isKeyDown)));
                            }
                        } else await appendLineAsync("@@PRINT " + ValueToString(EvalValue(arg, vars, ln, getInkey, isKeyDown)));
                        break;
                    case "LET": var (n, vt) = SplitAssignment(arg); vars[n] = EvalValue(vt, vars, ln, getInkey, isKeyDown); break;
                    case "WAIT": 
                        if (arg.ToUpperInvariant() == "VBL") await appendLineAsync("@@VSYNC");
                        else await Task.Delay(Math.Max(0, EvalInt(arg, vars, ln, getInkey, isKeyDown)), token);
                        break;
                    case "IF":
                        var tIdx = IndexOfWord(arg, "THEN");
                        if (tIdx >= 0 && EvalCondition(arg[..tIdx].Trim(), vars, ln, getInkey, isKeyDown)) {
                            var restOfLine = arg[(tIdx + 4)..].Trim();
                            // Kör resten av raden som nya kommandon
                            var thenCmds = SplitMultipleCommands(restOfLine);
                            foreach (var tc in thenCmds) {
                                var (tc_cmd, tc_arg) = SplitCommand(tc);
                                if (await ExecuteSingleStatementAsync(tc_cmd, tc_arg, vars, appendLineAsync, clearAsync, graphics, onGraphicsChanged, getInkey, isKeyDown, token, ln)) { jumpHappened = true; break; }
                            }
                        }
                        goto next_line; // IF avbryter alltid resten av originalraden för att inte köra kommandon efter THEN av misstag
                    case "FOR":
                        var eq = arg.IndexOf('='); var fV = arg[..eq].Trim(); var rhs = arg[(eq + 1)..].Trim(); var toIdx = IndexOfWord(rhs, "TO");
                        var start = EvalInt(rhs[..toIdx].Trim(), vars, ln, getInkey, isKeyDown); var rest = rhs[(toIdx + 2)..].Trim(); var stIdx = IndexOfWord(rest, "STEP");
                        var end = EvalInt(stIdx < 0 ? rest : rest[..stIdx].Trim(), vars, ln, getInkey, isKeyDown);
                        var step = stIdx < 0 ? 1 : EvalInt(rest[(stIdx + 4)..].Trim(), vars, ln, getInkey, isKeyDown);
                        vars[fV] = start; forStack.Push(new ForFrame { VarName = fV, EndValue = end, StepValue = step, LineAfterForPc = pc + 1, ForLineNumber = ln });
                        break;
                    case "NEXT":
                        if (forStack.Count == 0) break;
                        var f = forStack.Peek(); var cur = GetIntVar(f.VarName, vars, ln) + f.StepValue; vars[f.VarName] = cur;
                        if (f.StepValue > 0 ? cur <= f.EndValue : cur >= f.EndValue) { pc = f.LineAfterForPc; jumpHappened = true; } else forStack.Pop();
                        break;
                    case "SCREEN": var sP = SplitCsvOrSpaces(arg); graphics.Screen(EvalInt(sP[0], vars, ln, getInkey, isKeyDown), EvalInt(sP[1], vars, ln, getInkey, isKeyDown)); graphics.Clear(Colors.Black); onGraphicsChanged(); break;
                    case "CLSG": graphics.Clear(Colors.Black); onGraphicsChanged(); break;
                    case "LOAD": graphics.LoadBackground(Unquote(arg)); onGraphicsChanged(); break;
                    case "INK": graphics.Ink = ParseColor(arg); break;
                    case "PLOT": var pP = SplitCsvOrSpaces(arg); graphics.Plot(EvalInt(pP[0], vars, ln, getInkey, isKeyDown), EvalInt(pP[1], vars, ln, getInkey, isKeyDown)); onGraphicsChanged(); break;
                    case "LINE": var lL = SplitCsvOrSpaces(arg); graphics.Line(EvalInt(lL[0], vars, ln, getInkey, isKeyDown), EvalInt(lL[1], vars, ln, getInkey, isKeyDown), EvalInt(lL[2], vars, ln, getInkey, isKeyDown), EvalInt(lL[3], vars, ln, getInkey, isKeyDown)); onGraphicsChanged(); break;
                    case "BOX": var bB = SplitCsvOrSpaces(arg); graphics.Box(EvalInt(bB[0], vars, ln, getInkey, isKeyDown), EvalInt(bB[1], vars, ln, getInkey, isKeyDown), EvalInt(bB[2], vars, ln, getInkey, isKeyDown), EvalInt(bB[3], vars, ln, getInkey, isKeyDown)); onGraphicsChanged(); break;
                    case "BAR": var rR = SplitCsvOrSpaces(arg); graphics.Bar(EvalInt(rR[0], vars, ln, getInkey, isKeyDown), EvalInt(rR[1], vars, ln, getInkey, isKeyDown), EvalInt(rR[2], vars, ln, getInkey, isKeyDown), EvalInt(rR[3], vars, ln, getInkey, isKeyDown)); onGraphicsChanged(); break;
                    case "SCROLL": var sc = SplitCsvOrSpaces(arg); graphics.Scroll(EvalInt(sc[0], vars, ln, getInkey, isKeyDown), EvalInt(sc[1], vars, ln, getInkey, isKeyDown)); break;
                    case "TEXT":
                        var tP = SplitCsvOrSpaces(arg);
                        if (tP.Count >= 3) graphics.DrawText(EvalInt(tP[0], vars, ln, getInkey, isKeyDown), EvalInt(tP[1], vars, ln, getInkey, isKeyDown), ValueToString(EvalValue(string.Join(" ", tP.Skip(2)), vars, ln, getInkey, isKeyDown)));
                        onGraphicsChanged(); break;
                    case "REFRESH": graphics.Refresh(); onGraphicsChanged(); break;
                    case "SPRITE":
                        var ss = SplitCsvOrSpaces(arg);
                        if (!int.TryParse(ss[0], out var sid)) {
                            var sub = ss[0].ToUpperInvariant();
                            if (sub=="POS") graphics.SpritePos(EvalInt(ss[1],vars,ln,getInkey,isKeyDown), EvalInt(ss[2],vars,ln,getInkey,isKeyDown), EvalInt(ss[3],vars,ln,getInkey,isKeyDown));
                            else if (sub=="LOAD") graphics.LoadSprite(EvalInt(ss[1], vars, ln, getInkey, isKeyDown), Unquote(ss[2]));
                            else if (sub=="ADDFRAME") graphics.AddFrame(EvalInt(ss[1], vars, ln, getInkey, isKeyDown), Unquote(ss[2]));
                            else if (sub=="FRAME") graphics.SetSpriteFrame(EvalInt(ss[1], vars, ln, getInkey, isKeyDown), EvalInt(ss[2], vars, ln, getInkey, isKeyDown));
                            else if (sub=="HANDLE") graphics.SpriteHandle(EvalInt(ss[1],vars,ln,getInkey,isKeyDown), EvalInt(ss[2],vars,ln,getInkey,isKeyDown), EvalInt(ss[3],vars,ln,getInkey,isKeyDown));
                            else if (sub=="ON") graphics.SpriteOn(EvalInt(ss[1],vars,ln,getInkey,isKeyDown));
                            else if (sub=="OFF") graphics.SpriteOff(EvalInt(ss[1],vars,ln,getInkey,isKeyDown));
                        } else graphics.CreateSprite(sid, EvalInt(ss[1],vars,ln,getInkey,isKeyDown), EvalInt(ss[2],vars,ln,getInkey,isKeyDown)); 
                        break;
                    case "END": return;
                    default: if (!string.IsNullOrWhiteSpace(cmd)) throw new Exception($"Syntax Error: '{cmd}' at line {ln}"); break;
                }
                if (jumpHappened) break;
            }
            
            // Om inget hopp skedde (GOTO/NEXT etc), gå till nästa rad
            if (!jumpHappened) pc++;
            
            continue; // Hoppa över next_line labeln om vi körde normalt

            next_line:
            pc++; // VIKTIGT: Se till att vi faktiskt stegar framåt även om vi använde 'goto next_line'
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
        switch (cmd) {
            case "PRINT": await al("@@PRINT " + ValueToString(EvalValue(arg, vars, ln, gk, ikd))); return false;
            case "LOCATE": var p = SplitCsvOrSpaces(arg); await al($"@@LOCATE {EvalInt(p[0], vars, ln, gk, ikd)} {EvalInt(p[1], vars, ln, gk, ikd)}"); return false;
            case "LET": 
                var (n, vt) = SplitAssignment(arg); 
                vars[n] = EvalValue(vt, vars, ln, gk, ikd); 
                return false;
            case "PLOT": var pp = SplitCsvOrSpaces(arg); g.Plot(EvalInt(pp[0], vars, ln, gk, ikd), EvalInt(pp[1], vars, ln, gk, ikd)); og(); return false;
            case "SPRITE":
                var ss = SplitCsvOrSpaces(arg);
                if (ss.Count >= 2) {
                    var sub = ss[0].ToUpperInvariant();
                    if (sub == "POS") g.SpritePos(EvalInt(ss[1], vars, ln, gk, ikd), EvalInt(ss[2], vars, ln, gk, ikd), EvalInt(ss[3], vars, ln, gk, ikd));
                    else if (sub == "FRAME") g.SetSpriteFrame(EvalInt(ss[1], vars, ln, gk, ikd), EvalInt(ss[2], vars, ln, gk, ikd));
                    else if (sub == "ON") g.SpriteOn(EvalInt(ss[1], vars, ln, gk, ikd));
                    else if (sub == "OFF") g.SpriteOff(EvalInt(ss[1], vars, ln, gk, ikd));
                }
                return false;
            case "WAIT":
                if (arg.ToUpperInvariant() == "VBL") await al("@@VSYNC");
                else await Task.Delay(Math.Max(0, EvalInt(arg, vars, ln, gk, ikd)), t);
                return false;
            case "VSYNC": await al("@@VSYNC"); return false;
            case "REFRESH": g.Refresh(); og(); return false;
            case "END": return true;
            default: 
                if (!string.IsNullOrWhiteSpace(cmd))
                {
                    throw new Exception($"Syntax Error in IF-THEN: Unknown command '{cmd}' at line {ln}");
                }
                return false;
        }
    }

    private static bool EvalCondition(string c, Dictionary<string, object> v, int ln, Func<string> gk, Func<string, bool> ikd)
    {
        // Om villkoret bara är en funktion som returnerar 0 eller 1 (som KEYSTATE)
        if (!c.Contains('=') && !c.Contains('<') && !c.Contains('>'))
        {
            var val = EvalValue(c, v, ln, gk, ikd);
            return Convert.ToInt32(val) != 0;
        }

        var ops = new[] { "<>", "<=", ">=", "=", "<", ">" };
        foreach (var op in ops) {
            var i = c.IndexOf(op); if (i < 0) continue;
            var lV = EvalValue(c[..i].Trim(), v, ln, gk, ikd); var rV = EvalValue(c[(i + op.Length)..].Trim(), v, ln, gk, ikd);
            if (lV is string || rV is string) { var ls = ValueToString(lV); var rs = ValueToString(rV); return op == "=" ? ls == rs : ls != rs; }
            var li = Convert.ToInt32(lV); var ri = Convert.ToInt32(rV);
            return op switch { "=" => li == ri, "<>" => li != ri, "<" => li < ri, ">" => li > ri, "<=" => li <= ri, ">=" => li >= ri, _ => false };
        }
        
        return false;
    }
    

    private static string StripComments(string l)
    {
        bool inQuotes = false;
        for (int i = 0; i < l.Length; i++)
        {
            if (l[i] == '\"') inQuotes = !inQuotes;
            if (!inQuotes && l[i] == ';') return l[..i].Trim();
        }
        return l;
    }
    private static object EvalValue(string t, Dictionary<string, object> v, int ln, Func<string> gk, Func<string, bool> ikd)
    {
        t = t.Trim();
        if (t.Equals("INKEY$", StringComparison.OrdinalIgnoreCase)) return gk();
        
        if (t.StartsWith("KEYSTATE(", StringComparison.OrdinalIgnoreCase)) {
            var k = t.Substring(9).TrimEnd(')').Trim('"');
            // Anropa ikd (isKeyDown) med tangentnamnet
            return ikd(k) ? 1 : 0;
        }
        if (IsQuotedString(t)) return Unquote(t);
        return EvalInt(t, v, ln, gk, ikd);
    }

    private static int EvalInt(string val, Dictionary<string, object> v, int ln, Func<string> gk, Func<string, bool> ikd) { var t = new Tokenizer(val); return ParseExpr(ref t, v, ln, gk, ikd); }
    private static int ParseExpr(ref Tokenizer t, Dictionary<string, object> v, int ln, Func<string> gk, Func<string, bool> ikd) {
        var res = ParseTerm(ref t, v, ln, gk, ikd);
        while (true) { if (t.TryConsume('+')) res += ParseTerm(ref t, v, ln, gk, ikd); else if (t.TryConsume('-')) res -= ParseTerm(ref t, v, ln, gk, ikd); else break; }
        return res;
    }
    private static int ParseTerm(ref Tokenizer t, Dictionary<string, object> v, int ln, Func<string> gk, Func<string, bool> ikd) {
        var res = ParseFactor(ref t, v, ln, gk, ikd);
        while (true) { if (t.TryConsume('*')) res *= ParseFactor(ref t, v, ln, gk, ikd); else if (t.TryConsume('/')) res /= ParseFactor(ref t, v, ln, gk, ikd); else break; }
        return res;
    }
    private static int ParseFactor(ref Tokenizer t, Dictionary<string, object> v, int ln, Func<string> gk, Func<string, bool> ikd) {
        t.SkipWs();
        if (t.TryConsume('(')) { var res = ParseExpr(ref t, v, ln, gk, ikd); t.TryConsume(')'); return res; }
        if (t.TryReadInt(out var n)) return n;
        if (t.TryReadIdentifier(out var id)) {
            if (id.Equals("INKEY$", StringComparison.OrdinalIgnoreCase)) return gk().Length;
            if (id.Equals("RND", StringComparison.OrdinalIgnoreCase)) {
                t.TryConsume('('); 
                var max = ParseExpr(ref t, v, ln, gk, ikd); 
                t.TryConsume(')'); 
                return _rng.Next(max + 1); 
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
    private static string Unquote(string t) => t.Trim('"');
    private static int GetIntVar(string n, Dictionary<string, object> v, int ln) => v.TryGetValue(n, out var val) ? Convert.ToInt32(val) : 0;
    private static (string n, string v) SplitAssignment(string t) { var i = t.IndexOf('='); return (t[..i].Trim(), t[(i + 1)..].Trim()); }
    private static (string c, string a) SplitCommand(string l) { var i = l.IndexOf(' '); return i < 0 ? (l.ToUpperInvariant(), "") : (l[..i].ToUpperInvariant(), l[(i + 1)..].Trim()); }
    private static string StripLeadingLineNumber(string l) { var i = 0; while (i < l.Length && char.IsDigit(l[i])) i++; return l[i..].Trim(); }
    private static List<string> SplitCsvOrSpaces(string a) => new List<string>(a.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries));
    private static Color ParseColor(string t) { try { return Color.Parse(t); } catch { return Colors.White; } }
    private static int IndexOfWord(string t, string w) { var i = t.ToUpperInvariant().IndexOf(w.ToUpperInvariant()); return i; }

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