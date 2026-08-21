namespace TelemetryDashboard.Core.Plugins;

using System.Collections.Concurrent;

/// <summary>One channel an expression reads, as written in it.</summary>
public readonly record struct VariableRef(string NodeId, string Name)
{
    /// <summary>The channel id, as the series store and the wire spell it.</summary>
    public override string ToString() =>
        string.IsNullOrEmpty(NodeId) ? Name : $"{NodeId}.{Name}";
}

public abstract class AstNode
{
    public abstract double Evaluate(Func<string, string, double> variableResolver);

    /// <summary>
    /// Evaluates where an absent input is absent rather than zero.
    /// </summary>
    /// <remarks>
    /// <see cref="Evaluate"/> takes a resolver that returns <c>double</c>, so a channel that has
    /// never reported and a channel reading zero volts are the same answer, and an expression over
    /// a misspelled name quietly produces a number. That is the defect
    /// <see cref="Models.AlignedSample"/> was created to remove from the alignment path; the same
    /// hole is here, one layer up, and it is worse here because arithmetic hides it: a missing
    /// denominator makes a ratio infinite, a missing numerator makes it exactly zero, and both look
    /// like readings.
    /// <para>
    /// Null propagates. If any input of an expression is unknown then the expression is unknown,
    /// because there is no arithmetic that turns "we do not know" into a number.
    /// </para>
    /// </remarks>
    public abstract double? TryEvaluate(Func<string, string, double?> variableResolver);

    /// <summary>Adds every channel this subtree reads to <paramref name="into"/>.</summary>
    /// <remarks>
    /// A computed channel has to be able to say what it depends on before it is evaluated — to
    /// subscribe to those channels, to align them to one instant, and to report which of them was
    /// the one that was missing.
    /// </remarks>
    public abstract void CollectVariables(ICollection<VariableRef> into);
}

public sealed class NumberNode : AstNode
{
    private readonly double _value;
    public NumberNode(double value) => _value = value;
    public override double Evaluate(Func<string, string, double> variableResolver) => _value;
    public override double? TryEvaluate(Func<string, string, double?> variableResolver) => _value;
    public override void CollectVariables(ICollection<VariableRef> into) { }
}

public sealed class VariableNode : AstNode
{
    private readonly string _nodeId;
    private readonly string _varName;
    public VariableNode(string nodeId, string varName) { _nodeId = nodeId; _varName = varName; }
    public override double Evaluate(Func<string, string, double> variableResolver) => variableResolver != null ? variableResolver(_nodeId, _varName) : 0.0;

    public override double? TryEvaluate(Func<string, string, double?> variableResolver) =>
        variableResolver is null ? null : variableResolver(_nodeId, _varName);

    public override void CollectVariables(ICollection<VariableRef> into) =>
        into.Add(new VariableRef(_nodeId, _varName));
}

public sealed class BinaryOpNode : AstNode
{
    private readonly AstNode _left;
    private readonly AstNode _right;
    private readonly char _op;
    public BinaryOpNode(AstNode left, char op, AstNode right) { _left = left; _op = op; _right = right; }

    public override double Evaluate(Func<string, string, double> variableResolver)
    {
        double l = _left.Evaluate(variableResolver);
        double r = _right.Evaluate(variableResolver);
        return _op switch
        {
            '+' => l + r,
            '-' => l - r,
            '*' => l * r,
            '/' => l / r,
            '^' => Math.Pow(l, r),
            '%' => l % r,
            _ => 0.0
        };
    }

    public override double? TryEvaluate(Func<string, string, double?> variableResolver)
    {
        if (_left.TryEvaluate(variableResolver) is not { } l) return null;
        if (_right.TryEvaluate(variableResolver) is not { } r) return null;

        // A division whose denominator is zero has no value. Returning the infinity the hardware
        // would produce puts "∞ %" on a dashboard beside real readings; an efficiency computed
        // while the input current is zero is not 1e308, it is unknown.
        double result = _op switch
        {
            '+' => l + r,
            '-' => l - r,
            '*' => l * r,
            '/' => r == 0 ? double.NaN : l / r,
            '^' => Math.Pow(l, r),
            '%' => r == 0 ? double.NaN : l % r,
            _ => double.NaN
        };

        return double.IsFinite(result) ? result : null;
    }

    public override void CollectVariables(ICollection<VariableRef> into)
    {
        _left.CollectVariables(into);
        _right.CollectVariables(into);
    }
}

public sealed class FunctionCallNode : AstNode
{
    /// <summary>Functions an expression may call.</summary>
    /// <remarks>
    /// Published so a name can be rejected where it is written rather than where it is evaluated.
    /// An unrecognised call used to fall through to <c>0.0</c>, so <c>power(v, i)</c> — a name this
    /// evaluator has never had — read as zero watts on every sample, and the only symptom was a
    /// channel that was always zero.
    /// </remarks>
    public static readonly IReadOnlyDictionary<string, int> KnownFunctions =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["abs"] = 1, ["sqrt"] = 1, ["sin"] = 1, ["cos"] = 1, ["round"] = 1,
            ["min"] = 2, ["max"] = 2
        };

    private readonly string _funcName;
    private readonly List<AstNode> _arguments;
    public FunctionCallNode(string funcName, List<AstNode> arguments) { _funcName = funcName; _arguments = arguments; }

    [ThreadStatic]
    private static int s_functionDepth;

    public override double? TryEvaluate(Func<string, string, double?> variableResolver)
    {
        var evaluated = new double[_arguments.Count];
        for (int i = 0; i < _arguments.Count; i++)
        {
            if (_arguments[i].TryEvaluate(variableResolver) is not { } value) return null;
            evaluated[i] = value;
        }

        // Arity is checked rather than defaulted. min(x) used to answer x, so a formula that
        // dropped an argument to a typo went on producing plausible numbers.
        double result = _funcName.ToLowerInvariant() switch
        {
            "abs" when evaluated.Length == 1 => Math.Abs(evaluated[0]),
            "sqrt" when evaluated.Length == 1 => Math.Sqrt(evaluated[0]),
            "sin" when evaluated.Length == 1 => Math.Sin(evaluated[0]),
            "cos" when evaluated.Length == 1 => Math.Cos(evaluated[0]),
            "round" when evaluated.Length == 1 => Math.Round(evaluated[0]),
            "min" when evaluated.Length == 2 => Math.Min(evaluated[0], evaluated[1]),
            "max" when evaluated.Length == 2 => Math.Max(evaluated[0], evaluated[1]),
            _ => double.NaN
        };

        // sqrt of a negative is NaN, and NaN is not a reading.
        return double.IsFinite(result) ? result : null;
    }

    public override void CollectVariables(ICollection<VariableRef> into)
    {
        foreach (AstNode argument in _arguments) argument.CollectVariables(into);
    }

    public override double Evaluate(Func<string, string, double> variableResolver)
    {
        try
        {
            s_functionDepth++;
            if (s_functionDepth > 3)
            {
                return 1.0;
            }

            var evaluatedArgs = _arguments.Select(a => a.Evaluate(variableResolver)).ToArray();
            return _funcName.ToLowerInvariant() switch
            {
                "abs" => evaluatedArgs.Length > 0 ? Math.Abs(evaluatedArgs[0]) : 0,
                "sqrt" => evaluatedArgs.Length > 0 ? Math.Sqrt(evaluatedArgs[0]) : 0,
                "sin" => evaluatedArgs.Length > 0 ? Math.Sin(evaluatedArgs[0]) : 0,
                "cos" => evaluatedArgs.Length > 0 ? Math.Cos(evaluatedArgs[0]) : 0,
                "min" => evaluatedArgs.Length >= 2 ? Math.Min(evaluatedArgs[0], evaluatedArgs[1]) : (evaluatedArgs.Length == 1 ? evaluatedArgs[0] : 0),
                "max" => evaluatedArgs.Length >= 2 ? Math.Max(evaluatedArgs[0], evaluatedArgs[1]) : (evaluatedArgs.Length == 1 ? evaluatedArgs[0] : 0),
                "round" => evaluatedArgs.Length > 0 ? Math.Round(evaluatedArgs[0]) : 0,
                _ => 0.0
            };
        }
        finally
        {
            s_functionDepth--;
        }
    }
}

public sealed class FormulaEvaluator
{
    private readonly ConcurrentDictionary<string, AstNode> _astCache = new();

    [ThreadStatic]
    private static Func<string, string, double>? s_activeResolver;

    public double Evaluate(string expression, string currentNodeId, Func<string, string, double> variableResolver)
    {
        if (string.IsNullOrWhiteSpace(expression)) return 0.0;

        var effectiveResolver = variableResolver ?? s_activeResolver ?? ((_, _) => 0.0);
        var oldResolver = s_activeResolver;
        try
        {
            s_activeResolver = effectiveResolver;
            string cacheKey = $"{currentNodeId}:{expression}";
            AstNode ast = _astCache.GetOrAdd(cacheKey, _ => ParseToAst(expression, currentNodeId));
            return ast.Evaluate(effectiveResolver);
        }
        finally
        {
            s_activeResolver = oldResolver;
        }
    }

    /// <summary>
    /// Parses <paramref name="expression"/> into a tree that can be inspected before it is run.
    /// </summary>
    /// <remarks>
    /// Public so a caller can learn what an expression reads — <see cref="AstNode.CollectVariables"/>
    /// — and can find out at configuration time that it does not parse, instead of discovering it
    /// once per sample on the ingest path.
    /// </remarks>
    /// <exception cref="FormatException">The expression is not well formed.</exception>
    public static AstNode Parse(string expression, string defaultNodeId = "") =>
        ParseToAst(expression, defaultNodeId);

    private static AstNode ParseToAst(string expression, string defaultNodeId)
    {
        var tokens = Tokenize(expression);
        int index = 0;
        var ast = ParseExpression(tokens, ref index, defaultNodeId);
        if (index < tokens.Count)
        {
            throw new FormatException($"Unexpected token '{tokens[index]}' in expression");
        }
        return ast;
    }

    private static List<string> Tokenize(string expr)
    {
        var tokens = new List<string>();
        int i = 0;
        while (i < expr.Length)
        {
            char c = expr[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if ("+-*/^%(),".Contains(c))
            {
                tokens.Add(c.ToString());
                i++;
                continue;
            }
            if (c == '[')
            {
                int close = expr.IndexOf(']', i);
                if (close > i)
                {
                    tokens.Add(expr.Substring(i, close - i + 1));
                    i = close + 1;
                    continue;
                }
            }
            if (char.IsLetterOrDigit(c) || c == '_' || c == '.')
            {
                int start = i;
                while (i < expr.Length && (char.IsLetterOrDigit(expr[i]) || expr[i] == '_' || expr[i] == '.')) i++;
                tokens.Add(expr.Substring(start, i - start));
                continue;
            }
            i++;
        }
        return tokens;
    }

    private static AstNode ParseExpression(List<string> tokens, ref int index, string defaultNodeId)
    {
        AstNode left = ParseTerm(tokens, ref index, defaultNodeId);
        while (index < tokens.Count && (tokens[index] == "+" || tokens[index] == "-"))
        {
            char op = tokens[index][0];
            index++;
            AstNode right = ParseTerm(tokens, ref index, defaultNodeId);
            left = new BinaryOpNode(left, op, right);
        }
        return left;
    }

    private static AstNode ParseTerm(List<string> tokens, ref int index, string defaultNodeId)
    {
        AstNode left = ParsePower(tokens, ref index, defaultNodeId);
        while (index < tokens.Count && (tokens[index] == "*" || tokens[index] == "/" || tokens[index] == "%"))
        {
            char op = tokens[index][0];
            index++;
            AstNode right = ParsePower(tokens, ref index, defaultNodeId);
            left = new BinaryOpNode(left, op, right);
        }
        return left;
    }

    private static AstNode ParsePower(List<string> tokens, ref int index, string defaultNodeId)
    {
        AstNode left = ParseFactor(tokens, ref index, defaultNodeId);
        while (index < tokens.Count && tokens[index] == "^")
        {
            char op = tokens[index][0];
            index++;
            AstNode right = ParseFactor(tokens, ref index, defaultNodeId);
            left = new BinaryOpNode(left, op, right);
        }
        return left;
    }

    private static AstNode ParseFactor(List<string> tokens, ref int index, string defaultNodeId)
    {
        if (index >= tokens.Count) throw new FormatException("Unexpected end of expression");
        string token = tokens[index];

        if (token == "-")
        {
            index++; // skip '-'
            AstNode operand = ParseFactor(tokens, ref index, defaultNodeId);
            return new BinaryOpNode(new NumberNode(0), '-', operand);
        }

        if (token == "+")
        {
            index++; // skip '+'
            return ParseFactor(tokens, ref index, defaultNodeId);
        }

        if (double.TryParse(token, System.Globalization.CultureInfo.InvariantCulture, out double num))
        {
            index++;
            return new NumberNode(num);
        }

        if (token == "(")
        {
            index++; // skip '('
            AstNode inner = ParseExpression(tokens, ref index, defaultNodeId);
            if (index < tokens.Count && tokens[index] == ")") index++;
            else throw new FormatException("Missing closing parenthesis");
            return inner;
        }

        if (token.StartsWith("["))
        {
            // Cross-node reference: [NodeId].VarName
            string nodeId = token.Trim('[', ']');
            index++;
            if (index < tokens.Count && tokens[index] == ".") index++;
            string varName = index < tokens.Count ? tokens[index++] : string.Empty;
            if (varName.StartsWith(".")) varName = varName[1..];
            return new VariableNode(nodeId, varName);
        }

        if (char.IsLetter(token[0]) || token[0] == '_')
        {
            index++;
            if (index < tokens.Count && tokens[index] == "(")
            {
                // Function call
                index++; // skip '('
                var args = new List<AstNode>();
                if (index < tokens.Count && tokens[index] != ")")
                {
                    args.Add(ParseExpression(tokens, ref index, defaultNodeId));
                    while (index < tokens.Count && tokens[index] == ",")
                    {
                        index++; // skip ','
                        args.Add(ParseExpression(tokens, ref index, defaultNodeId));
                    }
                }
                if (index < tokens.Count && tokens[index] == ")") index++;
                else throw new FormatException("Missing closing parenthesis in function call");

                // Rejected here rather than answered with 0.0 at evaluation time. A misspelled
                // function is a mistake in the expression, and the moment to say so is when it is
                // written -- not once a channel has been reading zero for an hour.
                if (!FunctionCallNode.KnownFunctions.TryGetValue(token, out int arity))
                {
                    throw new FormatException(
                        $"Unknown function '{token}'. Available: " +
                        string.Join(", ", FunctionCallNode.KnownFunctions
                            .Select(f => $"{f.Key}/{f.Value}").OrderBy(f => f, StringComparer.Ordinal)));
                }

                // Arity belongs here for the same reason the name does. min(x) evaluated to x, so a
                // formula that lost an argument to a typo kept producing plausible numbers, and a
                // host started cleanly on an expression that could never mean what it said.
                if (args.Count != arity)
                {
                    throw new FormatException(
                        $"'{token}' takes {arity} argument(s); {args.Count} were given.");
                }

                return new FunctionCallNode(token, args);
            }
            if (token.Contains('.'))
            {
                var parts = token.Split('.', 2);
                return new VariableNode(parts[0], parts[1]);
            }
            return new VariableNode(defaultNodeId, token);
        }

        throw new FormatException($"Unexpected token '{token}' in expression");
    }
}
