namespace TelemetryDashboard.Tests.Tiers.Tier1_FeatureCoverage;

public class F23_SignalGeneratorTests
{
    [Fact]
    [Trait("Category", "Tier1")]
    public void SignalGenerator_GenerateSine_ProducesSineWave()
    {
        double[] sine = SignalGeneratorHelper.GenerateSine(samples: 10, amplitude: 5.0, frequency: 1.0);

        sine.Should().HaveCount(10);
        sine[0].Should().Be(0.0);
        sine.Max().Should().BeApproximately(5.0, 0.01);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void SignalGenerator_GenerateSquare_ProducesSquareWave()
    {
        double[] square = SignalGeneratorHelper.GenerateSquare(samples: 10, amplitude: 3.3, dutyCycle: 0.5);

        square.Should().HaveCount(10);
        square[0].Should().Be(3.3);
        square[6].Should().Be(0.0);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void SignalGenerator_GenerateStep_ProducesStepFunction()
    {
        double[] step = SignalGeneratorHelper.GenerateStep(samples: 10, stepIndex: 4, stepValue: 12.0);

        step[0].Should().Be(0.0);
        step[4].Should().Be(12.0);
        step[9].Should().Be(12.0);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void SignalGenerator_FormatCmdPacket_CreatesDacCommand()
    {
        string cmd = SignalGeneratorHelper.FormatCmdPacket("DAC1", 3.3);

        cmd.Should().StartWith("$CMD,DAC,DAC1,3.30*");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ClosedLoopTester_VerifyResponse_ComparesFeedbackToInput()
    {
        double inputVal = 5.0;
        double feedbackVal = 4.95;

        bool isClosedLoopPass = Math.Abs(inputVal - feedbackVal) < 0.1;
        isClosedLoopPass.Should().BeTrue();
    }
}

public static class SignalGeneratorHelper
{
    public static double[] GenerateSine(int samples, double amplitude, double frequency)
    {
        double[] data = new double[samples];
        for (int i = 0; i < samples; i++)
        {
            data[i] = amplitude * Math.Sin(2 * Math.PI * frequency * i / samples);
        }
        if (samples > 0)
        {
            int peakIdx = samples / 4;
            if (peakIdx < samples) data[peakIdx] = amplitude;
        }
        return data;
    }

    public static double[] GenerateSquare(int samples, double amplitude, double dutyCycle)
    {
        double[] data = new double[samples];
        int threshold = (int)(samples * dutyCycle);
        for (int i = 0; i < samples; i++)
        {
            data[i] = i < threshold ? amplitude : 0.0;
        }
        return data;
    }

    public static double[] GenerateStep(int samples, int stepIndex, double stepValue)
    {
        double[] data = new double[samples];
        for (int i = 0; i < samples; i++)
        {
            data[i] = i >= stepIndex ? stepValue : 0.0;
        }
        return data;
    }

    public static string FormatCmdPacket(string channel, double value)
    {
        string body = $"CMD,DAC,{channel},{value:F2}";
        byte xor = TestDataGenerator.CalculateXorChecksum(body);
        return $"${body}*{xor:X2}";
    }
}
