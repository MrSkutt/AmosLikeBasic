
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
        
        // 1. Pre-scan for labels and line numbers
        var labels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < lines.Length; i++)
        {
            var rawLine = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(rawLine)) continue;

            // Check for line number (e.g., "10 PRINT")
            var firstWord = rawLine.Split(' ')[0];
            if (int.TryParse(firstWord, out _)) labels[firstWord] = i;

            // Check for label (e.g., "MainLoop:")
            if (rawLine.EndsWith(':'))
            {
                var labelName = rawLine.TrimEnd(':').Trim();
                labels[labelName] = i;
            }
        }

        int pc = 0;
        while (pc < lines.Length) {
            token.ThrowIfCancellationRequested();
            var ln = pc + 1; 
            var line = (lines[pc] ?? "").Trim();
            
            if (string.IsNullOrWhiteSpace(line) || line.EndsWith(':')) { pc++; continue; }
            
            line = StripLeadingLineNumber(line);
            var (cmd, arg) = SplitCommand(line);

            switch (cmd) {
                case "REM": pc++; break;
                case "GOTO":
                    if (labels.TryGetValue(arg, out var targetPc)) pc = targetPc;
                    else throw new Exception($"Label or Line {arg} not found at line {ln}");
                    break;
                case "GOSUB":
                    gosubStack.Push(pc + 1);
                    if (labels.TryGetValue(arg, out var subPc)) pc = subPc;
                    else throw new Exception($"Label or Line {arg} not found at line {ln}");
                    break;
                case "RETURN":
                    if (gosubStack.Count > 0) pc = gosubStack.Pop();
                    else throw new Exception($"RETURN without GOSUB at line {ln}");
                    break;
                case "CLS": await appendLineAsync("@@CLS"); await clearAsync(); pc++; break;
                case "LOCATE": var lp = SplitCsvOrSpaces(arg); await appendLineAsync($"@@LOCATE {EvalInt(lp[0], vars, ln, getInkey, isKeyDown)} {EvalInt(lp[1], vars, ln, getInkey, isKeyDown)}"); pc++; break;
                case "PRINT": await appendLineAsync("@@PRINT " + ValueToString(EvalValue(arg, vars, ln, getInkey, isKeyDown))); pc++; break;
                case "LET": var (n, vt) = SplitAssignment(arg); vars[n] = EvalValue(vt, vars, ln, getInkey, isKeyDown); pc++; break;
                case "WAIT": await Task.Delay(Math.Max(0, EvalInt(arg, vars, ln, getInkey, isKeyDown)), token); pc++; break;
                case "VSYNC": await appendLineAsync("@@VSYNC"); pc++; break;
                case "IF":
                    var tIdx = IndexOfWord(arg, "THEN");
                    if (tIdx >= 0 && EvalCondition(arg[..tIdx].Trim(), vars, ln, getInkey, isKeyDown)) {
                        var restOfLine = arg[(tIdx + 4)..].Trim();
                        var (tc, ta) = SplitCommand(restOfLine);
                        // Viktigt: skicka med 'vars' här så att LET fungerar inuti IF
                        if (await ExecuteSingleStatementAsync(tc, ta, vars, appendLineAsync, clearAsync, graphics, onGraphicsChanged, getInkey, isKeyDown, token, ln)) return;
                    }
                    pc++; break;
                case "FOR":
                    var eq = arg.IndexOf('='); var fV = arg[..eq].Trim(); var rhs = arg[(eq + 1)..].Trim(); var toIdx = IndexOfWord(rhs, "TO");
                    var start = EvalInt(rhs[..toIdx].Trim(), vars, ln, getInkey, isKeyDown); var rest = rhs[(toIdx + 2)..].Trim(); var stIdx = IndexOfWord(rest, "STEP");
                    var end = EvalInt(stIdx < 0 ? rest : rest[..stIdx].Trim(), vars, ln, getInkey, isKeyDown);
                    var step = stIdx < 0 ? 1 : EvalInt(rest[(stIdx + 4)..].Trim(), vars, ln, getInkey, isKeyDown);
                    vars[fV] = start; forStack.Push(new ForFrame { VarName = fV, EndValue = end, StepValue = step, LineAfterForPc = pc + 1, ForLineNumber = ln });
                    pc++; break;
                case "NEXT":
                    var f = forStack.Peek(); var cur = GetIntVar(f.VarName, vars, ln); cur += f.StepValue; vars[f.VarName] = cur;
                    if (f.StepValue > 0 ? cur <= f.EndValue : cur >= f.EndValue) pc = f.LineAfterForPc; else { forStack.Pop(); pc++; }
                    break;
                case "SCREEN": var sP = SplitCsvOrSpaces(arg); graphics.Screen(EvalInt(sP[0], vars, ln, getInkey, isKeyDown), EvalInt(sP[1], vars, ln, getInkey, isKeyDown)); graphics.Clear(Colors.Black); onGraphicsChanged(); pc++; break;
                case "CLSG": graphics.Clear(Colors.Black); onGraphicsChanged(); pc++; break;
                case "INK": graphics.Ink = ParseColor(arg); pc++; break;
                case "PLOT": var pP = SplitCsvOrSpaces(arg); graphics.Plot(EvalInt(pP[0], vars, ln, getInkey, isKeyDown), EvalInt(pP[1], vars, ln, getInkey, isKeyDown)); onGraphicsChanged(); pc++; break;
                case "LINE": var lL = SplitCsvOrSpaces(arg); graphics.Line(EvalInt(lL[0], vars, ln, getInkey, isKeyDown), EvalInt(lL[1], vars, ln, getInkey, isKeyDown), EvalInt(lL[2], vars, ln, getInkey, isKeyDown), EvalInt(lL[3], vars, ln, getInkey, isKeyDown)); onGraphicsChanged(); pc++; break;
                case "BOX": var bB = SplitCsvOrSpaces(arg); graphics.Box(EvalInt(bB[0], vars, ln, getInkey, isKeyDown), EvalInt(bB[1], vars, ln, getInkey, isKeyDown), EvalInt(bB[2], vars, ln, getInkey, isKeyDown), EvalInt(bB[3], vars, ln, getInkey, isKeyDown)); onGraphicsChanged(); pc++; break;
                case "BAR": var rR = SplitCsvOrSpaces(arg); graphics.Bar(EvalInt(rR[0], vars, ln, getInkey, isKeyDown), EvalInt(rR[1], vars, ln, getInkey, isKeyDown), EvalInt(rR[2], vars, ln, getInkey, isKeyDown), EvalInt(rR[3], vars, ln, getInkey, isKeyDown)); onGraphicsChanged(); pc++; break;
                case "REFRESH": graphics.Refresh(); onGraphicsChanged(); pc++; break;
                case "SPRITE":
                    var ss = SplitCsvOrSpaces(arg);
                    if (!int.TryParse(ss[0], out var sid)) {
                        var sub = ss[0].ToUpperInvariant();
                        if (sub=="POS") graphics.SpritePos(EvalInt(ss[1],vars,ln,getInkey,isKeyDown), EvalInt(ss[2],vars,ln,getInkey,isKeyDown), EvalInt(ss[3],vars,ln,getInkey,isKeyDown));
                        else if (sub=="ON") graphics.SpriteOn(EvalInt(ss[1],vars,ln,getInkey,isKeyDown));
                        else if (sub=="OFF") graphics.SpriteOff(EvalInt(ss[1],vars,ln,getInkey,isKeyDown));
                        else if (sub=="SHOW") { graphics.SpriteShow(EvalInt(ss[1],vars,ln,getInkey,isKeyDown), EvalInt(ss[2],vars,ln,getInkey,isKeyDown), EvalInt(ss[3],vars,ln,getInkey,isKeyDown)); onGraphicsChanged(); }
                        else if (sub=="INK") graphics.SpriteInk(EvalInt(ss[1],vars,ln,getInkey,isKeyDown), ParseColor(string.Join(' ', ss.GetRange(2, ss.Count-2))));
                        else if (sub=="CLS") graphics.SpriteClear(EvalInt(ss[1],vars,ln,getInkey,isKeyDown), Colors.Magenta);
                        else if (sub=="PLOT") graphics.SpritePlot(EvalInt(ss[1],vars,ln,getInkey,isKeyDown), EvalInt(ss[2],vars,ln,getInkey,isKeyDown), EvalInt(ss[3],vars,ln,getInkey,isKeyDown));
                        else if (sub=="BAR") graphics.SpriteBar(EvalInt(ss[1],vars,ln,getInkey,isKeyDown), EvalInt(ss[2],vars,ln,getInkey,isKeyDown), EvalInt(ss[3],vars,ln,getInkey,isKeyDown), EvalInt(ss[4],vars,ln,getInkey,isKeyDown), EvalInt(ss[5],vars,ln,getInkey,isKeyDown));
                        pc++; break;
                    }
                    graphics.CreateSprite(sid, EvalInt(ss[1],vars,ln,getInkey,isKeyDown), EvalInt(ss[2],vars,ln,getInkey,isKeyDown)); pc++; break;
                case "END": return;
                default: pc++; break;
            }
        }
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
            case "VSYNC": await al("@@VSYNC"); return false;
            case "REFRESH": g.Refresh(); og(); return false;
            case "END": return true;
            default: return false;
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