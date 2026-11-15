using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AmosLikeBasic;

// Förkompilering av uttryck och villkor.
//
// Tidigare tolkades varje uttryck från text varje gång det kördes. Nu översätts varje
// uttryckssträng en gång till ett träd av noder, som cachas och sedan bara räknas ut.
// Kompilatorn följer exakt samma grammatik som den gamla tolken (ParseExpr/ParseTerm/
// ParseFactor) och använder samma Tokenizer, så resultaten blir desamma.
//
// Inbyggda funktioner (SIN, MID$, KEYSTATE ...) körs fortfarande av den gamla tolken,
// men bara på själva anropet. Den gamla uttryckstolken (EvalValueInterpreted) finns kvar
// som reserv för uttryck som kompilatorn inte förstår.
public static partial class AmosRunner
{
    // Det en nod behöver för att räknas ut
    private readonly struct EvalContext
    {
        public readonly Dictionary<string, object> Vars;
        public readonly int Line;
        public readonly Func<string> GetInkey;
        public readonly Func<string, bool> IsKeyDown;
        public readonly AmosGraphics Graphics;

        public EvalContext(Dictionary<string, object> vars, int line, Func<string> getInkey,
            Func<string, bool> isKeyDown, AmosGraphics graphics)
        {
            Vars = vars;
            Line = line;
            GetInkey = getInkey;
            IsKeyDown = isKeyDown;
            Graphics = graphics;
        }
    }

    private static readonly ConcurrentDictionary<string, ExprNode> ExprCache = new();
    private static readonly ConcurrentDictionary<string, CondNode> CondCache = new();

    // Uppdelade kommandoargument, per argumenttext
    private static readonly ConcurrentDictionary<string, string[]> CsvOrSpacesCache = new();
    private static readonly ConcurrentDictionary<string, string[]> TopLevelCsvCache = new();
    private static readonly ConcurrentDictionary<string, (int?, string)> ScreenPrefixCache = new();

    // Töms när ett nytt program startar, så att cachen inte växer mellan körningar
    private static void ClearCompiledCache()
    {
        ExprCache.Clear();
        CondCache.Clear();
        CsvOrSpacesCache.Clear();
        TopLevelCsvCache.Clear();
        ScreenPrefixCache.Clear();
    }

    private static object EvalValue(string t, Dictionary<string, object> v, int ln, Func<string> gk,
        Func<string, bool> ikd, AmosGraphics g)
    {
        if (string.IsNullOrWhiteSpace(t)) return "";
        var node = ExprCache.GetOrAdd(t, CompileExpression);
        return node.Eval(new EvalContext(v, ln, gk, ikd, g));
    }

    private static bool EvalCondition(string c, Dictionary<string, object> v, int ln, Func<string> gk,
        Func<string, bool> ikd, AmosGraphics g)
    {
        var node = CondCache.GetOrAdd(c, CompileCondition);
        return node.Eval(new EvalContext(v, ln, gk, ikd, g));
    }

    // ════════════════════════════════════════════════════════════════
    //  Uttryck
    // ════════════════════════════════════════════════════════════════

    private static readonly object Zero = 0.0;

    // Namn som ParseFactor hanterar som inbyggda funktioner. De går före arrayer med samma namn.
    private static readonly HashSet<string> BuiltinFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "ABS", "ANYPLAYING", "ASC", "ATN", "CHR$", "COS", "DEC", "DIM", "EOF", "EXP", "FADING", "FIND",
        "FINDLAST", "HEX", "HIT", "HITBOX", "HITBOXGROUP", "HITCIRCLE", "HITCIRCLEGROUP", "HITGROUP",
        "INC", "INKEY$", "INSTR", "INT", "JOIN$", "JOYAXIS", "JOYSTATE", "KEYSTATE", "LAYERFADING",
        "LEFT$", "LEN", "LOG", "LOG10", "LOG2", "LOWER$", "MAX", "MICROS", "MID$", "MILLIS", "MIN",
        "PLAYING", "REPEAT$", "REPLACE$", "RIGHT$", "RND", "SGN", "SIN", "SPLIT$", "SQR", "STR$", "TAN",
        "TICKS", "TILE", "TRACKPLAYING", "TRACKPOS", "TRACKROW", "TRIM$", "UPPER$", "VAL", "WORD$"
    };

    private abstract class ExprNode
    {
        public abstract object Eval(in EvalContext c);
    }

    private static ExprNode CompileExpression(string text)
    {
        try
        {
            var t = new Tokenizer(text);
            return CompileExpr(ref t);
        }
        catch
        {
            // Något kompilatorn inte förstår: tolka som förut varje gång
            return new InterpretedExpr(text);
        }
    }

    // Expr := And { OR And }
    private static ExprNode CompileExpr(ref Tokenizer t)
    {
        var res = CompileAnd(ref t);
        while (t.TryConsumeWord("OR")) res = new OrNode(res, CompileAnd(ref t));
        return res;
    }

    // And := Not { AND Not }
    private static ExprNode CompileAnd(ref Tokenizer t)
    {
        var res = CompileNot(ref t);
        while (t.TryConsumeWord("AND")) res = new AndNode(res, CompileNot(ref t));
        return res;
    }

    // Not := NOT Not | Compare
    private static ExprNode CompileNot(ref Tokenizer t) =>
        t.TryConsumeWord("NOT") ? new NotNode(CompileNot(ref t)) : CompileCompare(ref t);

    // Compare := Sum { ('=' | '<>' | '<' | '>' | '<=' | '>=') Sum }, ger 1 eller 0
    private static ExprNode CompileCompare(ref Tokenizer t)
    {
        var res = CompileSum(ref t);
        while (t.TryConsumeCompareOp(out var op)) res = new CompareNode(op, res, CompileSum(ref t));
        return res;
    }

    // Sum := Term { ('+' | '-' | '&') Term }
    private static ExprNode CompileSum(ref Tokenizer t)
    {
        var res = CompileTerm(ref t);
        while (true)
        {
            if (t.TryConsume('+')) res = new AddNode(res, CompileTerm(ref t));
            else if (t.TryConsume('-')) res = new SubNode(res, CompileTerm(ref t));
            else if (t.TryConsume('&')) res = new ConcatNode(res, CompileTerm(ref t));
            else break;
        }
        return res;
    }

    // Term := Power { ('*' | '/' | MOD | DIV) Power }
    private static ExprNode CompileTerm(ref Tokenizer t)
    {
        var res = CompilePower(ref t);
        while (true)
        {
            if (t.TryConsume('*')) res = new MulNode(res, CompilePower(ref t));
            else if (t.TryConsume('/')) res = new DivNode(res, CompilePower(ref t));
            else if (t.TryConsumeWord("MOD")) res = new ModNode(res, CompilePower(ref t));
            else if (t.TryConsumeWord("DIV")) res = new IntDivNode(res, CompilePower(ref t));
            else break;
        }
        return res;
    }

    // Power := Factor [ '^' Power ], högerassociativ: 2^3^2 = 2^9
    private static ExprNode CompilePower(ref Tokenizer t)
    {
        var res = CompileFactor(ref t);
        return t.TryConsume('^') ? new PowNode(res, CompilePower(ref t)) : res;
    }

    private static ExprNode CompileFactor(ref Tokenizer t)
    {
        t.SkipWs();
        if (t.TryReadString(out var s)) return new ConstNode(s);
        if (t.TryConsume('('))
        {
            var inner = CompileExpr(ref t);
            t.TryConsume(')');
            return inner;
        }
        // Minus före talet, så att -2^2 blir -(2^2) som i matematiken
        if (t.TryConsume('-')) return new NegNode(CompilePower(ref t));
        if (t.TryReadDouble(out var n)) return new ConstNode(n);
        if (t.TryConsume('+')) return CompilePower(ref t);

        int start = t.Pos;
        if (t.TryReadIdentifier(out var id))
        {
            t.SkipWs();
            if (id.Equals("INKEY$", StringComparison.OrdinalIgnoreCase)) return new InkeyNode();
            if (id.Equals("TICKS", StringComparison.OrdinalIgnoreCase))
            {
                t.TryConsume('(');
                t.TryConsume(')');
                return new TicksNode();
            }

            if (t.TryConsume('('))
            {
                int openPos = t.Pos - 1;
                var args = TryCompileArgs(ref t);
                if (args == null)
                {
                    // Argument som inte är en vanlig uttryckslista: låt den gamla tolken ta hela anropet
                    t.Pos = FindClosingParen(t.Source, openPos);
                    return new InterpretedFactorNode(t.Source[start..t.Pos]);
                }
                return new CallNode(id, args, t.Source[start..t.Pos]);
            }

            return new VarNode(id);
        }

        // Inget känt: samma som den gamla tolken
        return new ConstNode(0.0);
    }

    // Läser "a, b, c)" efter en öppningsparentes. null om det inte går.
    private static ExprNode[]? TryCompileArgs(ref Tokenizer t)
    {
        var args = new List<ExprNode>();
        if (t.TryConsume(')')) return args.ToArray();
        while (true)
        {
            args.Add(CompileExpr(ref t));
            if (t.TryConsume(',')) continue;
            if (t.TryConsume(')')) return args.ToArray();
            return null;
        }
    }

    // Positionen efter parentesen som matchar den på 'openPos' (hoppar över strängar)
    private static int FindClosingParen(string s, int openPos)
    {
        int depth = 0;
        bool q = false;
        for (int i = openPos; i < s.Length; i++)
        {
            if (s[i] == '"') q = !q;
            if (q) continue;
            if (s[i] == '(') depth++;
            else if (s[i] == ')' && --depth == 0) return i + 1;
        }
        return s.Length;
    }

    private static double Num(object o) => Convert.ToDouble(o, CultureInfo.InvariantCulture);

    private sealed class ConstNode : ExprNode
    {
        private readonly object _value;
        public ConstNode(object value) => _value = value;
        public override object Eval(in EvalContext c) => _value;
    }

    private sealed class VarNode : ExprNode
    {
        private readonly string _name;
        public VarNode(string name) => _name = name;
        public override object Eval(in EvalContext c) => c.Vars.TryGetValue(_name, out var v) ? v : Zero;
    }

    private sealed class InkeyNode : ExprNode
    {
        public override object Eval(in EvalContext c) => c.GetInkey();
    }

    private sealed class TicksNode : ExprNode
    {
        public override object Eval(in EvalContext c)
        {
            if (_programStartTime == DateTime.MinValue)
                _programStartTime = DateTime.UtcNow;
            return (DateTime.UtcNow - _programStartTime).TotalMilliseconds;
        }
    }

    private sealed class NegNode : ExprNode
    {
        private readonly ExprNode _inner;
        public NegNode(ExprNode inner) => _inner = inner;
        public override object Eval(in EvalContext c) => -Num(_inner.Eval(c));
    }

    private static readonly object One = 1.0;

    private sealed class PowNode : ExprNode
    {
        private readonly ExprNode _l, _r;
        public PowNode(ExprNode l, ExprNode r) { _l = l; _r = r; }
        public override object Eval(in EvalContext c)
        {
            var a = Num(_l.Eval(c));
            return Math.Pow(a, Num(_r.Eval(c)));
        }
    }

    private sealed class CompareNode : ExprNode
    {
        private readonly string _op;
        private readonly ExprNode _l, _r;
        public CompareNode(string op, ExprNode l, ExprNode r) { _op = op; _l = l; _r = r; }
        public override object Eval(in EvalContext c) =>
            CompareValues(_op, _l.Eval(c), _r.Eval(c)) ? One : Zero;
    }

    private sealed class AndNode : ExprNode
    {
        private readonly ExprNode _l, _r;
        public AndNode(ExprNode l, ExprNode r) { _l = l; _r = r; }
        public override object Eval(in EvalContext c) => IsTrue(_l.Eval(c)) && IsTrue(_r.Eval(c)) ? One : Zero;
    }

    private sealed class OrNode : ExprNode
    {
        private readonly ExprNode _l, _r;
        public OrNode(ExprNode l, ExprNode r) { _l = l; _r = r; }
        public override object Eval(in EvalContext c) => IsTrue(_l.Eval(c)) || IsTrue(_r.Eval(c)) ? One : Zero;
    }

    private sealed class NotNode : ExprNode
    {
        private readonly ExprNode _inner;
        public NotNode(ExprNode inner) => _inner = inner;
        public override object Eval(in EvalContext c) => IsTrue(_inner.Eval(c)) ? Zero : One;
    }

    // Allt utom 0 (och tom text) är sant, som i villkor
    private static bool IsTrue(object o) => o is string str
        ? str.Length > 0
        : Math.Abs(Num(o)) > 0.000001;

    // Gemensam jämförelse för villkor och uttryck. Text jämförs tecken för tecken.
    private static bool CompareValues(string op, object lV, object rV)
    {
        if (lV is string || rV is string)
        {
            int cmp = string.CompareOrdinal(ValueToString(lV), ValueToString(rV));
            return op switch
            {
                "=" => cmp == 0,
                "<>" => cmp != 0,
                "<" => cmp < 0,
                ">" => cmp > 0,
                "<=" => cmp <= 0,
                ">=" => cmp >= 0,
                _ => false
            };
        }

        var li = Num(lV);
        var ri = Num(rV);
        return op switch
        {
            "=" => Math.Abs(li - ri) < 0.000001,
            "<>" => Math.Abs(li - ri) > 0.000001,
            "<" => li < ri,
            ">" => li > ri,
            "<=" => li <= ri,
            ">=" => li >= ri,
            _ => false
        };
    }

    private sealed class AddNode : ExprNode
    {
        private readonly ExprNode _l, _r;
        public AddNode(ExprNode l, ExprNode r) { _l = l; _r = r; }
        public override object Eval(in EvalContext c)
        {
            var a = _l.Eval(c);
            var b = _r.Eval(c);
            if (a is string || b is string) return ValueToString(a) + ValueToString(b);
            return Num(a) + Num(b);
        }
    }

    private sealed class SubNode : ExprNode
    {
        private readonly ExprNode _l, _r;
        public SubNode(ExprNode l, ExprNode r) { _l = l; _r = r; }
        public override object Eval(in EvalContext c)
        {
            var a = _l.Eval(c);
            return Num(a) - Num(_r.Eval(c));
        }
    }

    private sealed class ConcatNode : ExprNode
    {
        private readonly ExprNode _l, _r;
        public ConcatNode(ExprNode l, ExprNode r) { _l = l; _r = r; }
        public override object Eval(in EvalContext c)
        {
            var a = _l.Eval(c);
            return ValueToString(a) + ValueToString(_r.Eval(c));
        }
    }

    private sealed class MulNode : ExprNode
    {
        private readonly ExprNode _l, _r;
        public MulNode(ExprNode l, ExprNode r) { _l = l; _r = r; }
        public override object Eval(in EvalContext c)
        {
            var a = _l.Eval(c);
            return Num(a) * Num(_r.Eval(c));
        }
    }

    private sealed class DivNode : ExprNode
    {
        private readonly ExprNode _l, _r;
        public DivNode(ExprNode l, ExprNode r) { _l = l; _r = r; }
        public override object Eval(in EvalContext c)
        {
            var a = _l.Eval(c);
            double div = Num(_r.Eval(c));
            if (Math.Abs(div) < 0.000000001)
                throw new DivideByZeroException("Division by zero");
            return Num(a) / div;
        }
    }

    private sealed class ModNode : ExprNode
    {
        private readonly ExprNode _l, _r;
        public ModNode(ExprNode l, ExprNode r) { _l = l; _r = r; }
        public override object Eval(in EvalContext c)
        {
            var a = Num(_l.Eval(c));
            var b = Num(_r.Eval(c));
            if (Math.Abs(b) < 0.000000001)
                throw new DivideByZeroException("MOD by zero");
            return a % b;
        }
    }

    private sealed class IntDivNode : ExprNode
    {
        private readonly ExprNode _l, _r;
        public IntDivNode(ExprNode l, ExprNode r) { _l = l; _r = r; }
        public override object Eval(in EvalContext c)
        {
            var a = Num(_l.Eval(c));
            var b = Num(_r.Eval(c));
            if (Math.Abs(b) < 0.000000001)
                throw new DivideByZeroException("DIV by zero");
            return Math.Floor(a / b);
        }
    }

    // NAMN(argument): egen FUNCTION, inbyggd funktion eller array, i samma ordning som ParseFactor
    private sealed class CallNode : ExprNode
    {
        private readonly string _id;
        private readonly ExprNode[] _args;
        private readonly string _text;
        private readonly bool _isBuiltin;
        private readonly NativeBuiltin? _native;

        public CallNode(string id, ExprNode[] args, string text)
        {
            _id = id;
            _args = args;
            _text = text;
            _isBuiltin = BuiltinFunctions.Contains(id);
            // Kompilerad variant bara när antalet argument är det funktionen förväntar sig;
            // annars tar den gamla tolken hand om det, med samma beteende som förut
            if (NativeBuiltins.TryGetValue(id, out var nb) && args.Length >= nb.MinArgs && args.Length <= nb.MaxArgs)
                _native = nb;
        }

        public override object Eval(in EvalContext c)
        {
            if (c.Vars.TryGetValue("__functions__", out var fo) &&
                ((Dictionary<string, FunctionDefinition>)fo).ContainsKey(_id))
            {
                var argValues = new List<object>(_args.Length);
                foreach (var a in _args) argValues.Add(a.Eval(c));
                var callFunc = (Func<string, List<object>, int, object>)c.Vars["__callFunction__"];
                return callFunc(_id, argValues, c.Line);
            }

            if (_native != null)
            {
                var values = new object[_args.Length];
                for (int i = 0; i < values.Length; i++) values[i] = _args[i].Eval(c);
                return _native.Fn(values, c);
            }

            if (!_isBuiltin && c.Vars.TryGetValue(_id, out var arrObj) && arrObj is IAmosArray array)
            {
                var indices = new int[_args.Length];
                for (int i = 0; i < _args.Length; i++)
                    indices[i] = (int)Math.Round(Num(_args[i].Eval(c)));
                return array.Get(indices);
            }

            // Inbyggd funktion (eller okänt namn, som ger samma fel som förut)
            return InterpretFactor(_text, c);
        }
    }

    // ── Inbyggda funktioner som räknas ut direkt ───────────────────────
    // Samma uträkning som motsvarande kod i ParseFactor (argumenten tolkas i samma ordning,
    // samma avrundning och samma returtyper). Övriga inbyggda funktioner körs av ParseFactor.

    private sealed record NativeBuiltin(int MinArgs, int MaxArgs, Func<object[], EvalContext, object> Fn);

    private static int Round(object o) => (int)Math.Round(Num(o));
    private static double Degrees(object o) => Num(o) * Math.PI / 180.0;

    private static readonly Dictionary<string, NativeBuiltin> NativeBuiltins = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ABS"] = new(1, 1, (a, _) => Math.Abs(Num(a[0]))),
        ["SGN"] = new(1, 1, (a, _) => Math.Sign(Num(a[0]))),
        ["SQR"] = new(1, 1, (a, _) => Math.Sqrt(Num(a[0]))),
        ["LOG"] = new(1, 1, (a, _) => Math.Log(Num(a[0]))),
        ["LOG2"] = new(1, 1, (a, _) => Math.Log2(Num(a[0]))),
        ["LOG10"] = new(1, 1, (a, _) => Math.Log10(Num(a[0]))),
        ["EXP"] = new(1, 1, (a, _) => Math.Exp(Num(a[0]))),
        ["SIN"] = new(1, 1, (a, _) => Math.Sin(Degrees(a[0]))),
        ["COS"] = new(1, 1, (a, _) => Math.Cos(Degrees(a[0]))),
        ["TAN"] = new(1, 1, (a, _) => Math.Tan(Degrees(a[0]))),
        ["ATN"] = new(1, 1, (a, _) => Math.Atan(Num(a[0])) * 180.0 / Math.PI),
        ["INT"] = new(1, 1, (a, _) => Math.Floor(Num(a[0]) + 0.000001)),
        ["RND"] = new(1, 1, (a, _) => _rng.NextDouble() * Num(a[0])),
        ["MIN"] = new(2, 2, (a, _) => Math.Min(Num(a[0]), Num(a[1]))),
        ["MAX"] = new(2, 2, (a, _) => Math.Max(Num(a[0]), Num(a[1]))),

        ["LEN"] = new(1, 1, (a, _) => (double)ValueToString(a[0]).Length),
        ["STR$"] = new(1, 1, (a, _) => ValueToString(a[0])),
        ["UPPER$"] = new(1, 1, (a, _) => ValueToString(a[0]).ToUpperInvariant()),
        ["LOWER$"] = new(1, 1, (a, _) => ValueToString(a[0]).ToLowerInvariant()),
        ["TRIM$"] = new(1, 1, (a, _) => ValueToString(a[0]).Trim()),
        ["CHR$"] = new(1, 1, (a, _) => ((char)Math.Clamp(Round(a[0]), 0, 255)).ToString()),
        ["ASC"] = new(1, 1, (a, _) => { var str = ValueToString(a[0]); return str.Length > 0 ? (double)str[0] : 0.0; }),
        ["VAL"] = new(1, 1, (a, _) =>
        {
            double.TryParse(ValueToString(a[0]), CultureInfo.InvariantCulture, out var dv);
            return dv;
        }),
        ["LEFT$"] = new(2, 2, (a, _) =>
        {
            string s = ValueToString(a[0]);
            int n = Round(a[1]);
            if (n <= 0) return "";
            if (n >= s.Length) return s;
            return s.Substring(0, n);
        }),
        ["RIGHT$"] = new(2, 2, (a, _) =>
        {
            string s = ValueToString(a[0]);
            int n = Round(a[1]);
            if (n <= 0) return "";
            if (n >= s.Length) return s;
            return s.Substring(s.Length - n, n);
        }),
        ["MID$"] = new(2, 3, (a, _) =>
        {
            int len = a.Length == 3 ? Round(a[2]) : -1;
            string s = ValueToString(a[0]);
            int start0 = Math.Max(0, Round(a[1]) - 1); // 1-baserat
            if (start0 >= s.Length) return "";
            if (len < 0) return s.Substring(start0);
            if (len <= 0) return "";
            return s.Substring(start0, Math.Min(len, s.Length - start0));
        }),
        ["INSTR"] = new(2, 3, (a, _) =>
        {
            int start1 = a.Length == 3 ? Round(a[2]) : 1;
            string s = ValueToString(a[0]);
            string needle = ValueToString(a[1]);
            if (string.IsNullOrEmpty(needle)) return 0.0;
            int start0 = Math.Max(0, start1 - 1);
            if (start0 > s.Length) return 0.0;
            int idx = s.IndexOf(needle, start0, StringComparison.Ordinal);
            return idx >= 0 ? (double)(idx + 1) : 0.0;
        }),

        ["KEYSTATE"] = new(1, 1, (a, c) => c.IsKeyDown(ValueToString(a[0])) ? 1.0 : 0.0),
        ["JOYSTATE"] = new(2, 2, (a, _) => GamepadManager.IsButtonDown(Round(a[0]), ValueToString(a[1])) ? 1.0 : 0.0),
        ["TILE"] = new(2, 3, (a, c) =>
        {
            Round(a[0]); // lagret används inte, men räknas om till tal först som förut
            int px = Round(a[1]);
            int py = a.Length == 3 ? Round(a[2]) : 0;
            return (double)c.Graphics.GetMapTile(px / 32, py / 32);
        }),
    };

    private sealed class InterpretedFactorNode : ExprNode
    {
        private readonly string _text;
        public InterpretedFactorNode(string text) => _text = text;
        public override object Eval(in EvalContext c) => InterpretFactor(_text, c);
    }

    private sealed class InterpretedExpr : ExprNode
    {
        private readonly string _text;
        public InterpretedExpr(string text) => _text = text;
        public override object Eval(in EvalContext c) =>
            EvalValueInterpreted(_text, c.Vars, c.Line, c.GetInkey, c.IsKeyDown, c.Graphics);
    }

    private static object InterpretFactor(string text, in EvalContext c)
    {
        var tok = new Tokenizer(text);
        return ParseFactor(ref tok, c.Vars, c.Line, c.GetInkey, c.IsKeyDown, c.Graphics);
    }

    // ════════════════════════════════════════════════════════════════
    //  Villkor (IF, WHILE, UNTIL ...)
    // ════════════════════════════════════════════════════════════════

    private abstract class CondNode
    {
        public abstract bool Eval(in EvalContext c);
    }

    private static readonly string[] CompareOps = { "<>", "<=", ">=", "=", "<", ">" };

    // Villkor delas upp i OR, sedan AND, sedan NOT, sedan jämförelse
    private static CondNode CompileCondition(string c)
    {
        c = StripOuterParens(c.Trim());

        var orIdx = TopLevelIndexOf(c, "OR", isWord: true);
        if (orIdx >= 0)
            return new OrCond(CompileCondition(c[..orIdx]), CompileCondition(c[(orIdx + 2)..]));

        var andIdx = TopLevelIndexOf(c, "AND", isWord: true);
        if (andIdx >= 0)
            return new AndCond(CompileCondition(c[..andIdx]), CompileCondition(c[(andIdx + 3)..]));

        if (c.StartsWith("NOT ", StringComparison.OrdinalIgnoreCase) ||
            c.StartsWith("NOT(", StringComparison.OrdinalIgnoreCase))
            return new NotCond(CompileCondition(c.Substring(3)));

        foreach (var op in CompareOps)
        {
            var i = TopLevelIndexOf(c, op, isWord: false);
            if (i < 0) continue;
            return new CompareCond(op, c[..i].Trim(), c[(i + op.Length)..].Trim());
        }

        return new TruthyCond(c);
    }

    private sealed class OrCond : CondNode
    {
        private readonly CondNode _l, _r;
        public OrCond(CondNode l, CondNode r) { _l = l; _r = r; }
        public override bool Eval(in EvalContext c) => _l.Eval(c) || _r.Eval(c);
    }

    private sealed class AndCond : CondNode
    {
        private readonly CondNode _l, _r;
        public AndCond(CondNode l, CondNode r) { _l = l; _r = r; }
        public override bool Eval(in EvalContext c) => _l.Eval(c) && _r.Eval(c);
    }

    private sealed class NotCond : CondNode
    {
        private readonly CondNode _inner;
        public NotCond(CondNode inner) => _inner = inner;
        public override bool Eval(in EvalContext c) => !_inner.Eval(c);
    }

    // Ett uttryck som villkor: allt utom 0 är sant
    private sealed class TruthyCond : CondNode
    {
        private readonly string _text;
        public TruthyCond(string text) => _text = text;
        public override bool Eval(in EvalContext c) =>
            Math.Abs(Convert.ToDouble(EvalValue(_text, c.Vars, c.Line, c.GetInkey, c.IsKeyDown, c.Graphics))) > 0.000001;
    }

    private sealed class CompareCond : CondNode
    {
        private readonly string _op, _left, _right;

        public CompareCond(string op, string left, string right)
        {
            _op = op;
            _left = left;
            _right = right;
        }

        public override bool Eval(in EvalContext c)
        {
            var lV = EvalValue(_left, c.Vars, c.Line, c.GetInkey, c.IsKeyDown, c.Graphics);
            var rV = EvalValue(_right, c.Vars, c.Line, c.GetInkey, c.IsKeyDown, c.Graphics);
            return CompareValues(_op, lV, rV);
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  Programrader
    // ════════════════════════════════════════════════════════════════

    // En programrad förberedd en gång: tom/kommentar/etikett hoppas över, annars färdigt
    // uppdelad i satser med kommando och argument.
    private sealed class PreparedLine
    {
        public bool Skip;
        public string Text = "";
        public PreparedCommand[] Commands = Array.Empty<PreparedCommand>();
        public int SourceLine;          // 1-baserat radnummer i editorn
        public bool FirstOfSourceLine;  // Debuggern stannar bara en gång per källrad
    }

    private sealed class PreparedCommand
    {
        public string Full = "";
        public string Cmd = "";
        public string Arg = "";

        // Fylls i första gången satsen körs
        public string? IfCondition;
        public PreparedAssignment? Assignment;
    }

    // "X = uttryck" eller "A(i, j) = uttryck", uppdelat en gång
    private sealed class PreparedAssignment
    {
        public string Target = "";
        public string Value = "";
        public bool IsArray;             // Vänstersidan innehåller '('
        public string? ArrayName;        // null om parenteserna inte går att tolka
        public string[]? IndexParts;
    }

    private static PreparedAssignment PrepareAssignment(string fullCmd)
    {
        var (leftSide, value) = SplitAssignment(fullCmd);
        var asg = new PreparedAssignment { Target = leftSide, Value = value, IsArray = leftSide.Contains('(') };
        if (asg.IsArray)
        {
            int openParen = leftSide.IndexOf('(');
            int closeParen = leftSide.LastIndexOf(')');
            if (openParen != -1 && closeParen != -1)
            {
                asg.ArrayName = leftSide[..openParen].Trim();
                asg.IndexParts = SplitTopLevelCsv(leftSide[(openParen + 1)..closeParen])
                    .Select(x => x.Trim()).ToArray();
            }
        }
        return asg;
    }

    // Villkoret i "IF villkor THEN", som IF-kommandot tidigare tog fram varje gång
    private static string IfConditionText(string arg)
    {
        int tIdx = IndexOfWord(arg, "THEN");
        return tIdx >= 0 ? arg[..tIdx].Trim() : arg;
    }

    // Samma steg som huvudloopen tidigare gjorde på varje rad varje gång den kördes
    private static PreparedLine[] PrepareLines(string[] lines, int[] sourceLine)
    {
        var prepared = new PreparedLine[lines.Length];
        for (int pc = 0; pc < lines.Length; pc++)
        {
            var p = new PreparedLine
            {
                SourceLine = sourceLine[pc],
                FirstOfSourceLine = pc == 0 || sourceLine[pc] != sourceLine[pc - 1]
            };
            prepared[pc] = p;

            if (IsEmptyLine(lines[pc])) { p.Skip = true; continue; }

            var line = StripComments((lines[pc] ?? "").Trim());
            if (string.IsNullOrWhiteSpace(line) || line.EndsWith(':')) { p.Skip = true; continue; }
            line = StripLeadingLineNumber(line).Trim();

            var upperCheck = line.ToUpperInvariant();
            if (upperCheck == "REM" || upperCheck.StartsWith("REM ")) { p.Skip = true; continue; }

            p.Text = line;
            p.Commands = SplitMultipleCommands(line)
                .Select(full => (full, trimmed: full.Trim()))
                .Where(x => x.trimmed.Length > 0)
                .Select(x =>
                {
                    var (cmd, arg) = SplitCommand(x.trimmed);
                    return new PreparedCommand { Full = x.full, Cmd = cmd, Arg = arg };
                })
                .ToArray();
        }

        // En källrad som delats upp (enradig IF) stannar debuggern på första riktiga raden av
        FixFirstOfSourceLine(prepared);
        return prepared;
    }

    private static void FixFirstOfSourceLine(PreparedLine[] prepared)
    {
        int lastSource = -1;
        foreach (var p in prepared)
        {
            p.FirstOfSourceLine = !p.Skip && p.SourceLine != lastSource;
            if (!p.Skip) lastSource = p.SourceLine;
        }
    }
}
