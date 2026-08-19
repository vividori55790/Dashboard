using TelemetryDashboard.Core.Services;
using TelemetryDashboard.Core.Plugins;

namespace TelemetryDashboard.Tests.Tiers.Tier1_FeatureCoverage;

public class F06_FormulaEngineTests
{
    private readonly FormulaEvaluator _evaluator = new();

    [Fact]
    [Trait("Category", "Tier1")]
    public void FormulaEvaluator_BasicArithmetic_EvaluatesCorrectly()
    {
        double result = _evaluator.Evaluate("10 + 20 * 3 - 5", "NODE1", (_, _) => 0.0);
        result.Should().Be(65.0);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void FormulaEvaluator_VariableReferences_ResolvesValues()
    {
        Func<string, string, double> resolver = (node, varName) =>
        {
            if (node == "MCU_1" && varName == "TEMP") return 40.0;
            if (node == "MCU_1" && varName == "GAIN") return 1.5;
            return 0.0;
        };

        double result = _evaluator.Evaluate("TEMP * GAIN + 5.0", "MCU_1", resolver);
        result.Should().Be(65.0);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void FormulaEvaluator_MathFunctions_EvaluatesFunctions()
    {
        Func<string, string, double> resolver = (_, _) => 0.0;

        double absVal = _evaluator.Evaluate("abs(-15.5)", "NODE1", resolver);
        double sqrtVal = _evaluator.Evaluate("sqrt(16.0)", "NODE1", resolver);
        double maxVal = _evaluator.Evaluate("max(10.0, 25.0)", "NODE1", resolver);

        absVal.Should().Be(15.5);
        sqrtVal.Should().Be(4.0);
        maxVal.Should().Be(25.0);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void FormulaEvaluator_NestedPrecedence_EvaluatesWithPrecedence()
    {
        double result = _evaluator.Evaluate("(2 + 3) * (10 - 4)", "NODE1", (_, _) => 0.0);
        result.Should().Be(30.0);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void FormulaEvaluator_CrossNodeReferences_ResolvesExternalNodeVariable()
    {
        Func<string, string, double> resolver = (node, varName) =>
        {
            if (node == "NODE_A" && varName == "VOLT") return 12.0;
            if (node == "NODE_B" && varName == "CURR") return 2.5;
            return 0.0;
        };

        double power = _evaluator.Evaluate("[NODE_A].VOLT * [NODE_B].CURR", "NODE_A", resolver);
        power.Should().Be(30.0);
    }
}
