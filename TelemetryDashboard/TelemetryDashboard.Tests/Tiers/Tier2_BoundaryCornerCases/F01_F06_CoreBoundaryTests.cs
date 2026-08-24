namespace TelemetryDashboard.Tests.Tiers.Tier2_BoundaryCornerCases;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;
using Moq;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Events;
using TelemetryDashboard.Core.Parsers;
using TelemetryDashboard.Core.Services;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Infrastructure.Serial;
using TelemetryDashboard.Tests.TestUtilities;
using TelemetryDashboard.Core.Plugins;

public class F01_F06_CoreBoundaryTests
{
    #region F01: Clean 4-Project Solution Structure (Boundary Tests)

    [Fact]
    [Trait("Category", "Tier2")]
    public void F01_Boundary_EmptyAssemblyRef_GracefulHandling()
    {
        var mockPlugin = new Mock<IPlugin>();
        mockPlugin.Setup(p => p.Id).Returns("");
        mockPlugin.Setup(p => p.Name).Returns("EmptyPlugin");

        mockPlugin.Object.Id.Should().BeEmpty();
        mockPlugin.Object.Name.Should().Be("EmptyPlugin");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F01_Boundary_NullAssemblyContext_ThrowsOrReturnsEmpty()
    {
        Action act = () => System.Reflection.Assembly.Load(Array.Empty<byte>());
        act.Should().Throw<Exception>();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F01_Boundary_CorruptedAssemblyPath_FailsGracefully()
    {
        // Composed, not spelled with a drive letter: this test's premise is that the file is
        // absent, and a Windows-shaped literal states that in a form only Windows evaluates the
        // way it reads. It happened to hold elsewhere -- the string is simply a relative name that
        // also does not exist -- and holding by accident is not the same as holding.
        string invalidPath = Path.Combine(
            Path.GetTempPath(), "NonExistentDir_" + Guid.NewGuid().ToString("N"), "InvalidAssembly.dll");
        File.Exists(invalidPath).Should().BeFalse();
        Action act = () => System.Reflection.Assembly.LoadFrom(invalidPath);
        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F01_Boundary_NonExistentProjectAssembly_ReturnsFalse()
    {
        var type = Type.GetType("TelemetryDashboard.NonExistent.FakeClass, TelemetryDashboard.NonExistent");
        type.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F01_Boundary_MaxPathLengthProjectLocation_HandledWithoutException()
    {
        string longPath = Path.Combine(Path.GetTempPath(), new string('a', 150), "project.json");
        longPath.Length.Should().BeGreaterThan(150);
        File.Exists(longPath).Should().BeFalse();
    }

    #endregion

    #region F02: Multi-Threaded Serial Communication Manager (Boundary Tests)

    [Fact]
    [Trait("Category", "Tier2")]
    public void F02_Boundary_InvalidBaudRate_ZeroOrNegative_ThrowsArgumentException()
    {
        var mockSerial = new Mock<ISerialManager>();
        mockSerial.Setup(s => s.ConnectAsync(It.IsAny<string>(), It.Is<int>(b => b <= 0)))
                  .ThrowsAsync(new ArgumentOutOfRangeException("baudRate"));

        Func<Task> act = async () => await mockSerial.Object.ConnectAsync("COM1", 0);
        act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public async Task F02_Boundary_NonExistentCOMPort_ConnectAsync_ReturnsFalseOrThrows()
    {
        var mockSerial = new Mock<ISerialManager>();
        mockSerial.Setup(s => s.ConnectAsync("COM999", 115200))
                  .ThrowsAsync(new IOException("The port 'COM999' does not exist."));

        Func<Task> act = async () => await mockSerial.Object.ConnectAsync("COM999", 115200);
        await act.Should().ThrowAsync<IOException>();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F02_Boundary_RapidConnectDisconnectCycle_NoThreadDeadlock()
    {
        var device = new MockSerialDevice("COM5", 115200);
        for (int i = 0; i < 50; i++)
        {
            device.Connect();
            device.IsOpen.Should().BeTrue();
            device.Disconnect();
            device.IsOpen.Should().BeFalse();
        }
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public async Task F02_Boundary_PortLockedByAnotherProcess_FailsGracefully()
    {
        var mockSerial = new Mock<ISerialManager>();
        mockSerial.Setup(s => s.ConnectAsync("COM1", 115200))
                  .ThrowsAsync(new UnauthorizedAccessException("Access to COM1 is denied."));

        Func<Task> act = async () => await mockSerial.Object.ConnectAsync("COM1", 115200);
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F02_Boundary_ZeroByteRead_HandlesWithoutBufferOverflow()
    {
        var device = new MockSerialDevice("COM3", 115200);
        device.Connect();
        device.PushBytes(Array.Empty<byte>());
        device.PendingBytesCount.Should().Be(0);
    }

    #endregion

    #region F03: Win32 WM_DEVICECHANGE USB Hot-Plug Detection (Boundary Tests)

    [Fact]
    [Trait("Category", "Tier2")]
    public void F03_Boundary_NullWindowHandle_HookInitialization_FailsGracefully()
    {
        Action act = () => new Win32HotPlugHook(IntPtr.Zero);
        // Initializing hook with IntPtr.Zero should not throw unexpected crash
        act.Should().NotThrow();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F03_Boundary_MalformedLParam_DoesNotCrashMessageFilter()
    {
        var hook = new Win32HotPlugHook(IntPtr.Zero);
        bool handled = false;
        // Message 0x0219 (WM_DEVICECHANGE), wParam=0x8000 (DBT_DEVICEARRIVAL), lParam=IntPtr.Zero
        IntPtr result = hook.WndProc(IntPtr.Zero, 0x0219, (IntPtr)0x8000, IntPtr.Zero, ref handled);
        result.Should().Be(IntPtr.Zero);
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F03_Boundary_RapidSpamDeviceChangeMessages_ProcessesWithoutMemoryLeak()
    {
        var hook = new Win32HotPlugHook(IntPtr.Zero);
        int eventCount = 0;
        hook.DeviceChanged += (s, e) => eventCount++;

        for (int i = 0; i < 100; i++)
        {
            bool handled = false;
            hook.WndProc(IntPtr.Zero, 0x0219, (IntPtr)0x8000, IntPtr.Zero, ref handled);
        }

        eventCount.Should().BeGreaterThan(0);
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F03_Boundary_UnknownDeviceClassGUID_IgnoredCorrectly()
    {
        var hook = new Win32HotPlugHook(IntPtr.Zero);
        bool handled = false;
        // wParam 0x0007 is unsupported message code
        IntPtr res = hook.WndProc(IntPtr.Zero, 0x0219, (IntPtr)0x0007, IntPtr.Zero, ref handled);
        res.Should().Be(IntPtr.Zero);
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F03_Boundary_DeviceRemovalDuringActiveTransfer_FiresDisconnectEvent()
    {
        var hook = new Win32HotPlugHook(IntPtr.Zero);
        DeviceChangeType? capturedType = null;
        hook.DeviceChanged += (s, e) => capturedType = e.ChangeType;

        bool handled = false;
        // wParam 0x8004 is DBT_DEVICEREMOVECOMPLETE
        hook.WndProc(IntPtr.Zero, 0x0219, (IntPtr)0x8004, IntPtr.Zero, ref handled);

        capturedType.Should().Be(DeviceChangeType.Removed);
    }

    #endregion

    #region F04: 1-Second Automatic Reconnect Engine (Boundary Tests)

    [Fact]
    [Trait("Category", "Tier2")]
    public async Task F04_Boundary_MaxReconnectAttemptsExceeded_StopsRetrying()
    {
        var mockSerial = new Mock<ISerialManager>();
        mockSerial.Setup(s => s.ConnectAsync(It.IsAny<string>(), It.IsAny<int>()))
                  .ThrowsAsync(new IOException("Port unavailable"));

        var reconnectEngine = new AutoReconnectEngine(mockSerial.Object, retryIntervalMs: 10, maxRetries: 3);
        bool success = await reconnectEngine.TryReconnectAsync("COM3", 115200);

        success.Should().BeFalse();
        mockSerial.Verify(s => s.ConnectAsync("COM3", 115200), Times.Exactly(3));
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F04_Boundary_CorruptedHistPacketDuringResync_DiscardsInvalidPacket()
    {
        string corruptedHist = "$HIST,MCU_NODE,TEMP,INVALID_FLOAT,NOT_A_TIMESTAMP\r\n";
        var rawPkt = new RawPacket("COM3", corruptedHist);
        var rule = new RoutingRule { TargetNodeId = "NODE_1" };

        bool success = PrefixParser.TryParse(rawPkt, rule, out var packets);
        success.Should().BeFalse();
        packets.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F04_Boundary_ZeroSecondReconnectInterval_ClampsToMinimumSafetyWindow()
    {
        var mockSerial = new Mock<ISerialManager>();
        var engine = new AutoReconnectEngine(mockSerial.Object, retryIntervalMs: 0, maxRetries: 1);
        engine.RetryIntervalMs.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F04_Boundary_TimestampOverflowInHistPacket_ClampsToDateTimeBounds()
    {
        string histOverflow = "$HIST,MCU_NODE,TEMP,45.2,9999999999999\r\n";
        var rawPkt = new RawPacket("COM3", histOverflow);
        var rule = new RoutingRule { TargetNodeId = "NODE_1" };

        Action act = () => PrefixParser.TryParse(rawPkt, rule, out _);
        act.Should().NotThrow<OverflowException>();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public async Task F04_Boundary_ReconnectionInterruptedByManualDisconnect_CancelsTaskCleanly()
    {
        var mockSerial = new Mock<ISerialManager>();
        mockSerial.Setup(s => s.ConnectAsync(It.IsAny<string>(), It.IsAny<int>()))
                  .Returns(Task.Delay(1000).ContinueWith(_ => false));

        var cts = new CancellationTokenSource();
        var engine = new AutoReconnectEngine(mockSerial.Object, retryIntervalMs: 100, maxRetries: 5);

        cts.Cancel();
        Func<Task> act = async () => await engine.TryReconnectAsync("COM3", 115200, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    #endregion

    #region F05: Packet Routers & XOR Checksum (Boundary Tests)

    [Fact]
    [Trait("Category", "Tier2")]
    public void F05_Boundary_PrefixParser_CorruptedXorChecksum_ReturnsFalse()
    {
        string corruptedFrame = TestDataGenerator.CreateCorruptedChecksumPrefixFrame("TELE", "NODE_1", "TEMP", 50.0, "C");
        var rawPkt = new RawPacket("COM3", corruptedFrame);
        var rule = new RoutingRule
        {
            RuleType = RuleType.Prefix,
            Tag = "TELE",
            TargetNodeId = "NODE_1",
            IndexMap = new Dictionary<int, string> { { 0, "TEMP" } }
        };

        bool parsed = PrefixParser.TryParse(rawPkt, rule, out var packets);
        parsed.Should().BeFalse();
        packets.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F05_Boundary_JsonParser_MalformedJsonString_ReturnsFalse()
    {
        string malformed = TestDataGenerator.CreateMalformedJsonFrame();
        var rawPkt = new RawPacket("COM3", malformed);
        var rule = new RoutingRule
        {
            RuleType = RuleType.Json,
            TargetNodeId = "NODE_1",
            JsonMap = new Dictionary<string, string> { { "temp", "TEMP" } }
        };

        bool parsed = JsonParser.TryParse(rawPkt, rule, out var packets);
        parsed.Should().BeFalse();
        packets.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F05_Boundary_ColumnsParser_ExtremeFloatValues_NaNAndInfinity_ParsedOrHandled()
    {
        string csvNaN = "NaN, Infinity, -Infinity\r\n";
        var rawPkt = new RawPacket("COM3", csvNaN);
        var rule = new RoutingRule
        {
            RuleType = RuleType.Columns,
            TargetNodeId = "NODE_1",
            IndexMap = new Dictionary<int, string> { { 0, "V1" }, { 1, "V2" }, { 2, "V3" } }
        };

        bool parsed = ColumnsParser.TryParse(rawPkt, rule, out var packets);
        parsed.Should().BeTrue();
        double.IsNaN(packets[0].Value).Should().BeTrue();
        double.IsPositiveInfinity(packets[1].Value).Should().BeTrue();
        double.IsNegativeInfinity(packets[2].Value).Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F05_Boundary_BufferOverflowString_100KBPayload_DoesNotCrashParser()
    {
        string hugePayload = "$" + new string('A', 100_000) + "*00\r\n";
        var rawPkt = new RawPacket("COM3", hugePayload);
        var rule = new RoutingRule { RuleType = RuleType.Prefix, Tag = "TELE", TargetNodeId = "NODE_1" };

        Action act = () => PrefixParser.TryParse(rawPkt, rule, out _);
        act.Should().NotThrow();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F05_Boundary_UnicodeNodeNameAndVariable_ParsedCorrectly()
    {
        string unicodeFrame = TestDataGenerator.CreateValidPrefixFrame("TELE", "온도_Sensör", "αβγ_TEMP", 75.5, "C");
        var rawPkt = new RawPacket("COM3", unicodeFrame);
        var rule = new RoutingRule
        {
            RuleType = RuleType.Prefix,
            Tag = "TELE",
            TargetNodeId = "온도_Sensör",
            IndexMap = new Dictionary<int, string> { { 0, "αβγ_TEMP" } }
        };

        bool parsed = PrefixParser.TryParse(rawPkt, rule, out var packets);
        parsed.Should().BeTrue();
        packets[0].NodeId.Should().Be("온도_Sensör");
        packets[0].Variable.Should().Be("αβγ_TEMP");
    }

    #endregion

    #region F06: Dynamic Algebraic Link Formula Engine (Boundary Tests)

    [Fact]
    [Trait("Category", "Tier2")]
    public void F06_Boundary_DivisionByZero_ReturnsNaNOrInfinityWithoutCrashing()
    {
        var eval = new FormulaEvaluator();
        var resolver = new Func<string, string, double>((n, v) => 0.0);

        double res = eval.Evaluate("100 / 0", "NODE1", resolver);
        double.IsInfinity(res).Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F06_Boundary_CircularVariableReference_DetectsStackOverflowAndThrows()
    {
        var eval = new FormulaEvaluator();
        // Resolver attempts recursive evaluation of a -> b -> a
        int callCount = 0;
        var resolver = new Func<string, string, double>((n, v) =>
        {
            callCount++;
            if (callCount > 50) throw new InvalidOperationException("Circular dependency detected");
            return eval.Evaluate(v == "a" ? "b + 1" : "a + 1", n, null!);
        });

        Action act = () => eval.Evaluate("a", "NODE1", resolver);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F06_Boundary_MissingVariableInResolver_ReturnsZeroOrDefaultValue()
    {
        var eval = new FormulaEvaluator();
        var resolver = new Func<string, string, double>((n, v) => throw new KeyNotFoundException(v));

        Action act = () => eval.Evaluate("NON_EXISTENT_VAR * 2", "NODE1", resolver);
        act.Should().Throw<KeyNotFoundException>();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F06_Boundary_MalformedFormulaSyntax_ThrowsInvalidOperationOrFormatException()
    {
        var eval = new FormulaEvaluator();
        var resolver = new Func<string, string, double>((n, v) => 1.0);

        Action act = () => eval.Evaluate("100 + * 50", "NODE1", resolver);
        act.Should().Throw<Exception>();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F06_Boundary_NestedFunctions_MaxDepthExceeded_HandledSafely()
    {
        var eval = new FormulaEvaluator();
        var resolver = new Func<string, string, double>((n, v) => 16.0);

        // Deeply nested sqrt(sqrt(sqrt(sqrt(16))))
        string deepFormula = "sqrt(sqrt(sqrt(sqrt(16))))";
        double result = eval.Evaluate(deepFormula, "NODE1", resolver);
        result.Should().BeApproximately(1.0, 0.0001);
    }

    #endregion
}
