using System;
using System.Collections.Generic;
using System.Linq;
using TelemetryDashboard.Core.Plugins;

namespace TelemetryDashboard.Core.Analytics;

/// <summary>
/// A channel whose value is an expression over other channels, evaluated at one instant.
/// </summary>
/// <remarks>
/// Every quantity this product shows is something a device reported. That is a real limitation for
/// the system it was built to watch: the figure of merit of a DC-DC converter is its efficiency,
/// and no converter has an efficiency pin. It is <c>Pout / Pin</c>, which is four measurements
/// multiplied and divided — arriving from two different MCUs, at two different rates, and never at
/// the same moment.
/// <para>
/// So a computed channel is not sugar over the raw stream. It is the only way to see the quantity
/// the operator actually cares about, and it is unsafe to build casually: reading the latest of
/// each input and multiplying is wrong by exactly the interval between them, and wrong in a way
/// that looks like a working number. <see cref="Streaming.AlignedEndpoint"/> exists to answer
/// "what were these channels at the same instant", and this is what wanted the answer.
/// </para>
/// <para>
/// A computed value is never a measurement and is always marked, because the mistake this invites
/// is an operator reading a derived efficiency as though an instrument had reported it.
/// </para>
/// </remarks>
public sealed class ComputedChannel
{
    private readonly AstNode _ast;

    private ComputedChannel(string id, string unit, string expression, AstNode ast, IReadOnlyList<string> inputs)
    {
        Id = id;
        Unit = unit;
        Expression = expression;
        _ast = ast;
        Inputs = inputs;
    }

    /// <summary>Channel id this publishes under, e.g. <c>psfb.efficiency</c>.</summary>
    public string Id { get; }

    /// <summary>Engineering unit of the result. The expression cannot infer it; a person states it.</summary>
    public string Unit { get; }

    /// <summary>The expression as written, kept so an answer can show its own derivation.</summary>
    public string Expression { get; }

    /// <summary>Channel ids this reads, in the order first mentioned.</summary>
    public IReadOnlyList<string> Inputs { get; }

    /// <summary>
    /// Parses <c>id[unit] = expression</c>, or <c>id = expression</c> for a dimensionless result.
    /// </summary>
    /// <exception cref="FormatException">
    /// The declaration is malformed, names no expression, or the expression does not parse.
    /// Thrown at declaration time on purpose: a host that cannot compute a channel should fail to
    /// start rather than serve that channel as permanently unavailable.
    /// </exception>
    public static ComputedChannel Parse(string declaration)
    {
        if (string.IsNullOrWhiteSpace(declaration))
        {
            throw new FormatException("A computed channel needs a declaration of the form id[unit] = expression.");
        }

        // Only a missing '=' means there is no expression. An '=' at position 0 is a declaration
        // with an empty left side, which is a different mistake and gets its own message below --
        // telling someone their expression is missing when they can see it there is worse than
        // saying nothing.
        int split = declaration.IndexOf('=');
        if (split < 0)
        {
            throw new FormatException(
                $"'{declaration}' names no expression. Write it as id[unit] = expression, " +
                "for example psfb.efficiency[%] = 100 * psfb.p_out / dab.p_in");
        }

        string left = declaration[..split].Trim();
        string expression = declaration[(split + 1)..].Trim();

        if (expression.Length == 0)
        {
            throw new FormatException($"'{declaration}' has nothing on the right of the '='.");
        }

        string unit = string.Empty;
        int bracket = left.IndexOf('[');
        if (bracket >= 0)
        {
            int close = left.IndexOf(']', bracket);
            if (close < 0) throw new FormatException($"'{left}' opens a unit with '[' and never closes it.");
            unit = left[(bracket + 1)..close].Trim();
            left = left[..bracket].Trim();
        }

        if (left.Length == 0)
        {
            throw new FormatException($"'{declaration}' has nothing on the left of the '=' to name the channel.");
        }

        AstNode ast = FormulaEvaluator.Parse(expression);

        var refs = new List<VariableRef>();
        ast.CollectVariables(refs);

        // Distinct and ordered by first mention, so a formula that uses a channel twice does not
        // subscribe to it twice or report it twice.
        List<string> inputs = refs.Select(r => r.ToString()).Distinct(StringComparer.Ordinal).ToList();

        if (inputs.Count == 0)
        {
            throw new FormatException(
                $"'{expression}' reads no channel, so it is a constant rather than a computed channel.");
        }

        if (inputs.Contains(left, StringComparer.Ordinal))
        {
            throw new FormatException(
                $"'{left}' is defined in terms of itself, which has no value at any instant.");
        }

        return new ComputedChannel(left, unit, expression, ast, inputs);
    }

    /// <summary>
    /// Evaluates against a lookup that may answer "no value".
    /// </summary>
    /// <returns>The result, or null when any input was unavailable or the arithmetic has no value.</returns>
    /// <remarks>
    /// Null is the whole point of the signature. Substituting zero for an input that never arrived
    /// makes a product exactly zero and a quotient infinite, and both of those are printable.
    /// </remarks>
    public double? Evaluate(Func<string, double?> channelValue)
    {
        ArgumentNullException.ThrowIfNull(channelValue);
        return _ast.TryEvaluate((nodeId, name) => channelValue(new VariableRef(nodeId, name).ToString()));
    }
}
