namespace TelemetryDashboard.Tests.Tiers.Tier1_FeatureCoverage;

public class F19_DeltaCursorTests
{
    [Fact]
    [Trait("Category", "Tier1")]
    public void DeltaCursor_SetPositions_UpdatesCursorAAndB()
    {
        var cursor = new DeltaCursorState();
        cursor.SetCursorA(1.0, 12.0);
        cursor.SetCursorB(3.0, 18.0);

        cursor.CursorA.Should().Be((1.0, 12.0));
        cursor.CursorB.Should().Be((3.0, 18.0));
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void DeltaCursor_CalculateDeltaV_ComputesVoltageDifference()
    {
        var cursor = new DeltaCursorState();
        cursor.SetCursorA(1.0, 12.0);
        cursor.SetCursorB(3.0, 18.0);

        cursor.DeltaV.Should().Be(6.0);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void DeltaCursor_CalculateDeltaT_ComputesTimeDifference()
    {
        var cursor = new DeltaCursorState();
        cursor.SetCursorA(1.0, 12.0);
        cursor.SetCursorB(3.5, 18.0);

        cursor.DeltaT.Should().Be(2.5);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void DeltaCursor_CalculateFrequency_ComputesReciprocalOfDeltaT()
    {
        var cursor = new DeltaCursorState();
        cursor.SetCursorA(1.0, 0);
        cursor.SetCursorB(1.05, 0); // DeltaT = 0.05 s -> 20 Hz

        cursor.Frequency.Should().BeApproximately(20.0, 0.001);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void DeltaCursor_HudOverlay_UpdatesDisplayStrings()
    {
        var cursor = new DeltaCursorState();
        cursor.SetCursorA(1.0, 5.0);
        cursor.SetCursorB(2.0, 15.0);

        string hudText = cursor.GetHudText();
        hudText.Should().Contain("ΔV = 10.00");
        hudText.Should().Contain("ΔT = 1.00s");
    }
}

public class DeltaCursorState
{
    public (double t, double v) CursorA { get; private set; }
    public (double t, double v) CursorB { get; private set; }

    public double DeltaV => CursorB.v - CursorA.v;
    public double DeltaT => Math.Abs(CursorB.t - CursorA.t);
    public double Frequency => DeltaT > 0 ? 1.0 / DeltaT : 0.0;

    public void SetCursorA(double t, double v) => CursorA = (t, v);
    public void SetCursorB(double t, double v) => CursorB = (t, v);

    public string GetHudText()
    {
        return $"ΔV = {DeltaV:F2}, ΔT = {DeltaT:F2}s, f = {Frequency:F1}Hz";
    }
}
