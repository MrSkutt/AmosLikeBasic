using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AmosLikeBasic;

// Precompiling expressions and conditions.
//
// Before, every expression was parsed from text every time it ran. Now every
// expression string is translated once into a tree of nodes, which is cached and then only evaluated.
// The compiler follows exactly the same grammar as the old interpreter (ParseExpr/ParseTerm/
// ParseFactor) and uses the same Tokenizer, so the results are the same.
//
// The most used built-in functions (SIN, MID$, KEYSTATE ...) are evaluated directly by the
// compiled nodes (NativeBuiltins); the rest are run by the old interpreter, but only for the call
// itself. The old expression interpreter (EvalValueInterpreted) is kept as a fallback for
// expressions the compiler does not understand, so the two must follow the same grammar.
public static partial class AmosRunner
{
    // What a node needs in order to be evaluated
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

    // Split command arguments, per argument text
    private static readonly ConcurrentDictionary<string, string[]> CsvOrSpacesCache = new();
    private static readonly ConcurrentDictionary<string, string[]> TopLevelCsvCache = new();
    private static readonly ConcurrentDictionary<string, (int?, string)> ScreenPrefixCache = new();

    // Cleared when a new program starts, so that the cache does not grow between runs
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

    // Names that ParseFactor treats as built-in functions. They take precedence over arrays with the same name.
    // For the code editor: the built-in functions and the words after each command
    internal static IReadOnlyCollection<string> FunctionNames => BuiltinFunctions;
    internal static IReadOnlyDictionary<string, HashSet<string>> SubCommandWords => SubCommands;

    private static readonly HashSet<string> BuiltinFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "ABS", "ANYPLAYING", "ASC", "ATN", "CHR$", "COS", "DEC", "DIM", "EOF", "EXP", "FADING", "FIND",
        "FINDLAST", "HEX", "HIT", "HITBOX", "HITBOXGROUP", "HITCIRCLE", "HITCIRCLEGROUP", "HITGROUP",
        "INC", "INKEY$", "INSTR", "INT", "JOIN$", "JOYAXIS", "JOYSTATE", "KEYSTATE", "LAYERFADING",
        "LEFT$", "LEN", "LOG", "LOG10", "LOG2", "LOWER$", "MAX", "MICROS", "MID$", "MILLIS", "MIN",
        "PETSCII$", "PLAYING", "REPEAT$", "REPLACE$", "RIGHT$", "RND", "SGN", "SIN", "SPACE$", "SPLIT$", "SQR", "STR$", "STRING$", "TAN",
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
            // Something the compiler does not understand: interpret it as before every time
            return new InterpretedExpr(text);
        }
    }

    // Expr := And { (OR | XOR) And }
    private static ExprNode CompileExpr(ref Tokenizer t)
    {
        var res = CompileAnd(ref t);
        while (true)
        {
            if (t.TryConsumeWord("OR")) res = new OrNode(res, CompileAnd(ref t));
            else if (t.TryConsumeWord("XOR")) res = new XorNode(res, CompileAnd(ref t));
            else break;
        }
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

    // Compare := Sum { ('=' | '<>' | '<' | '>' | '<=' | '>=') Sum }, gives 1 or 0
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

    // Term := Power { ('*' | '/' | MOD | DIV | '\\') Power }
    private static ExprNode CompileTerm(ref Tokenizer t)
    {
        var res = CompilePower(ref t);
        while (true)
        {
            if (t.TryConsume('*')) res = new MulNode(res, CompilePower(ref t));
            else if (t.TryConsume('/')) res = new DivNode(res, CompilePower(ref t));
            else if (t.TryConsumeWord("MOD")) res = new ModNode(res, CompilePower(ref t));
            else if (t.TryConsumeWord("DIV") || t.TryConsume('\\')) res = new IntDivNode(res, CompilePower(ref t));
            else break;
        }
        return res;
    }

    // Power := Factor [ '^' Power ], right-associative: 2^3^2 = 2^9
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
        // Minus before the number, so that -2^2 becomes -(2^2) as in maths
        if (t.TryConsume('-')) return new NegNode(CompilePower(ref t));
        if (t.TryReadDouble(out var n)) return new ConstNode(n);
        if (t.TryConsume('+')) return CompilePower(ref t);

        int start = t.Pos;
        if (t.TryReadIdentifier(out var id))
        {
            t.SkipWs();
            if (id.Equals("INKEY$", StringComparison.OrdinalIgnoreCase)) return new InkeyNode();
            // SCREEN WIDTH and SCREEN HEIGHT: the game's resolution
            if (id.Equals("SCREEN", StringComparison.OrdinalIgnoreCase))
            {
                int after = t.Pos;
                if (t.TryReadIdentifier(out var which) && (which.Equals("WIDTH", StringComparison.OrdinalIgnoreCase)
                                                          || which.Equals("HEIGHT", StringComparison.OrdinalIgnoreCase)))
                    return new ScreenSizeNode(which.Equals("HEIGHT", StringComparison.OrdinalIgnoreCase));
                t.Pos = after;
            }
            // TICKS, MILLIS and MICROS work with or without ()
            if (id.Equals("TICKS", StringComparison.OrdinalIgnoreCase) || id.Equals("MILLIS", StringComparison.OrdinalIgnoreCase))
            {
                t.TryConsume('(');
                t.TryConsume(')');
                return new TicksNode();
            }
            if (id.Equals("MICROS", StringComparison.OrdinalIgnoreCase))
            {
                t.TryConsume('(');
                t.TryConsume(')');
                return new TicksNode(1000.0);
            }

            if (t.TryConsume('('))
            {
                int openPos = t.Pos - 1;
                var args = TryCompileArgs(ref t);
                if (args == null)
                {
                    // Arguments that are not an ordinary expression list: let the old interpreter take the whole call
                    t.Pos = FindClosingParen(t.Source, openPos);
                    return new InterpretedFactorNode(t.Source[start..t.Pos]);
                }
                return new CallNode(id, args, t.Source[start..t.Pos]);
            }

            return new VarNode(id);
        }

        // Nothing known: the same as the old interpreter
        return new ConstNode(0.0);
    }

    // Reads "a, b, c)" after an opening bracket. null if it cannot.
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

    // The position after the bracket that matches the one at 'openPos' (skips strings)
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

    private sealed class ScreenSizeNode : ExprNode
    {
        private readonly bool _height;
        public ScreenSizeNode(bool height) => _height = height;
        public override object Eval(in EvalContext c) => (double)(_height ? c.Graphics.Height : c.Graphics.Width);
    }

    private sealed class TicksNode : ExprNode
    {
        private readonly double _scale;
        public TicksNode(double scale = 1.0) => _scale = scale;   // 1 for milliseconds, 1000 for microseconds
        public override object Eval(in EvalContext c)
        {
            if (_programStartTime == DateTime.MinValue)
                _programStartTime = DateTime.UtcNow;
            return (DateTime.UtcNow - _programStartTime).TotalMilliseconds * _scale;
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

    // XOR: true when exactly one side is true
    private sealed class XorNode : ExprNode
    {
        private readonly ExprNode _l, _r;
        public XorNode(ExprNode l, ExprNode r) { _l = l; _r = r; }
        public override object Eval(in EvalContext c) => IsTrue(_l.Eval(c)) != IsTrue(_r.Eval(c)) ? One : Zero;
    }

    private sealed class NotNode : ExprNode
    {
        private readonly ExprNode _inner;
        public NotNode(ExprNode inner) => _inner = inner;
        public override object Eval(in EvalContext c) => IsTrue(_inner.Eval(c)) ? Zero : One;
    }

    // Everything except 0 (and empty text) is true, as in conditions
    private static bool IsTrue(object o) => o is string str
        ? str.Length > 0
        : Math.Abs(Num(o)) > 0.000001;

    // Shared comparison for conditions and expressions. Text is compared character by character.
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

    // NAME(arguments): own FUNCTION, built-in function or array, in the same order as ParseFactor
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
            // A compiled variant only when the number of arguments is what the function expects;
            // otherwise the old interpreter handles it, with the same behaviour as before
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

            // Built-in function (or an unknown name, which gives the same error as before)
            return InterpretFactor(_text, c);
        }
    }

    // ── Built-in functions that are evaluated directly ─────────────────────
    // The same calculation as the matching code in ParseFactor (the arguments are evaluated in the same order,
    // the same rounding and the same return types). Other built-in functions are run by ParseFactor.

    private sealed record NativeBuiltin(int MinArgs, int MaxArgs, Func<object[], EvalContext, object> Fn);

    private static int Round(object o) => (int)Math.Round(Num(o));
    private static double Degrees(object o) => Num(o) * Math.PI / 180.0;

    private static readonly Dictionary<string, NativeBuiltin> NativeBuiltins = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ABS"] = new(1, 1, (a, _) => Math.Abs(Num(a[0]))),
        ["STRING$"] = new(2, 2, (a, _) => StringOf(a[0], a[1])),
        ["SPACE$"] = new(1, 1, (a, _) => new string(' ', Math.Max(0, Round(a[0])))),
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
        ["CHR$"] = new(1, 1, (a, _) => CharOf(Num(a[0]))),
        ["PETSCII$"] = new(1, 1, (a, _) => PetsciiOf(Num(a[0]))),
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
            Round(a[0]); // the layer is not used, but is converted to a number first as before
            int px = Round(a[1]);
            int py = a.Length == 3 ? Round(a[2]) : 0;
            return (double)c.Graphics.TileAtPoint(px, py);
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

    // Conditions are split into OR, then AND, then NOT, then comparison
    private static CondNode CompileCondition(string c)
    {
        c = StripOuterParens(c.Trim());

        var orIdx = TopLevelIndexOf(c, "OR", isWord: true);
        if (orIdx >= 0)
            return new OrCond(CompileCondition(c[..orIdx]), CompileCondition(c[(orIdx + 2)..]));

        var xorIdx = TopLevelIndexOf(c, "XOR", isWord: true);
        if (xorIdx >= 0)
            return new XorCond(CompileCondition(c[..xorIdx]), CompileCondition(c[(xorIdx + 3)..]));

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

    private sealed class XorCond : CondNode
    {
        private readonly CondNode _l, _r;
        public XorCond(CondNode l, CondNode r) { _l = l; _r = r; }
        public override bool Eval(in EvalContext c) => _l.Eval(c) != _r.Eval(c);
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

    // An expression as a condition: everything except 0 is true
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

    // A program line prepared once: empty/comment/label is skipped, otherwise it is already
    // split into statements with command and arguments.
    private sealed class PreparedLine
    {
        public bool Skip;
        public string Text = "";
        public PreparedCommand[] Commands = Array.Empty<PreparedCommand>();
        public int SourceLine;          // 1-based line number in the editor
        public bool FirstOfSourceLine;  // The debugger only stops once per source line
    }

    private sealed class PreparedCommand
    {
        public string Full = "";
        public string Cmd = "";
        public string Arg = "";

        // Filled in the first time the statement runs
        public string? IfCondition;
        public PreparedAssignment? Assignment;
        public bool SubCommandChecked;
    }

    // ── Commands with sub-commands ─────────────────────────────────────
    // The words each of these commands understands after its name. A misspelt one (SPRITE MOOVE 1, 2, 3)
    // used to be ignored without a word; now it gives an error. A variable is still fine as the first
    // value (SPRITE P, X, Y), since it is followed by a comma or an operator, not by more values.
    private static HashSet<string> Words(string list) => new(list.Split(' '), StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, HashSet<string>> SubCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SPRITE"] = Words("ADD ADDFRAME ALPHA AMPLITUDEX AMPLITUDEY BLUR BRIGHTNESS CLEAR CLEARGROUP COLOR CONTRAST EFFECT FADE FRAME FREQUENCYX FREQUENCYY GROUP HANDLE IMAGE LAYER LOAD MOVE OFF ON PHASEX PHASEY POS RADIUS ROTATE SATURATION TIME UNGROUP UPDATE WAVE ZOOM"),
        ["RASTER"] = Words("CLEAR COLOR COLOUR DEL GFXMODE INFO MOVE SPACE STR WRAP"),
        ["PLAY"] = Words("ADSR ARP BEND BLIP BPM COIN ECHO EXPLOSION FILTER FILTERENV GATE GLIDE HIT JUMP LASER LFO LOOP MUSIC OSC2 PAN POWERUP PULSE RING STOP SYNC VIBRATO VOLUME WAVE"),
        ["SCREEN"] = Words("ALPHA CRT FADE HEIGHT OFF ON SELECT WIDTH"),
        ["FONT"] = Words("ADD AMPLITUDEX AMPLITUDEY BLUR BRIGHTNESS CHAR CLEAR COLOR CONTRAST EFFECT FREQUENCYX FREQUENCYY LOAD MAP PHASEX PHASEY PRINT RADIUS ROTATE SATURATION SET STYLE TIME UPDATE WAVE ZOOM"),
        ["TILE"] = Words("DRAW LOAD MAP SET"),
        ["MAP"] = Words("LOAD MAP OBJECTS"),
        ["SAM"] = Words("LOOP PLAY STOP"),
        ["MUSIC"] = Words("PLAY STOP"),
        ["ANIM"] = Words("DEF GROUP LOOP SET STOP"),
        ["IMAGE"] = Words("LOAD"),
        ["WAIT"] = Words("MUSIC VBL"),
        ["PROFILE"] = Words("END START TIME"),
    };

    private static void CheckSubCommand(string cmd, string arg, int line)
    {
        if (!SubCommands.TryGetValue(cmd, out var known)) return;
        var a = arg.TrimStart();
        if (a.Length == 0 || !char.IsLetter(a[0])) return;   // a number, a string, #layer ...
        int i = 0;
        while (i < a.Length && (char.IsLetterOrDigit(a[i]) || a[i] == '_' || a[i] == '$')) i++;
        var word = a[..i];
        if (known.Contains(word)) return;
        int j = i;
        while (j < a.Length && a[j] == ' ') j++;
        // "WORD" alone, or "WORD, ..." / "WORD + 1": a variable, not a sub-command
        if (j == i || j >= a.Length) return;
        char next = a[j];
        if (char.IsLetterOrDigit(next) || next is '"' or '(' or '#' or '[')
            throw new Exception($"{cmd} does not know {word.ToUpperInvariant()}, at line {line}. " +
                                $"After {cmd} can come: {string.Join(", ", known.OrderBy(w => w))}");
    }

    // "X = expression" or "A(i, j) = expression", split once
    private sealed class PreparedAssignment
    {
        public string Target = "";
        public string Value = "";
        public bool IsArray;             // The left side contains '('
        public string? ArrayName;        // null if the brackets cannot be parsed
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

    // The condition in "IF condition THEN", which the IF command used to work out every time
    private static string IfConditionText(string arg)
    {
        int tIdx = IndexOfWord(arg, "THEN");
        return tIdx >= 0 ? arg[..tIdx].Trim() : arg;
    }

    // The same steps the main loop used to do on every line every time it ran
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

        // A source line that has been split up (single-line IF) makes the debugger stop on the first real line of
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
