namespace TelemetryDashboard.Core.Plugins;

using System.Collections.Concurrent;

public abstract class AstNode
{
    public abstract double Evaluate(Func<string, string, double> variableResolver);
}

public sealed class NumberNode : AstNode
{
    private readonly double _value;
    public NumberNode(double value) => _value = value;
    public override double Evaluate(Func<string, string, double> variableResolver) => _value;
}

public sealed class VariableNode : AstNode
{
    private readonly string _nodeId;
    private readonly string _varName;
    public VariableNode(string nodeId, string varName) { _nodeId = nodeId; _varName = varName; }
    public override double Evaluate(Func<string, string, double> variableResolver) => variableResolver != null ? variableResolver(_nodeId, _varName) : 0.0;
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
}

public sealed class FunctionCallNode : AstNode
{
    private readonly string _funcName;
    private readonly List<AstNode> _arguments;
    public FunctionCallNode(string funcName, List<AstNode> arguments) { _funcName = funcName; _arguments = arguments; }

    [ThreadStatic]
    private static int s_functionDepth;

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
