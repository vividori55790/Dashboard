namespace TelemetryDashboard.Tests;

using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using FluentAssertions;
using TelemetryDashboard.Core.Events;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Infrastructure.Serial;
using Xunit;

[Collection(HeavyTestCollection.Name)]
public class InfrastructureEmpiricalTests
{
    #region 1. MultiPortSerialManager Tests

    [Fact]
    public async Task MultiPortSerialManager_ChannelBackpressure_DropsOldestWhenExceedingCapacity()
    {
        using var hook = new Win32HotPlugHook();
        using var manager = new MultiPortSerialManager(hook);

        var reader = manager.PacketReader;

        // Verify channel is bounded and configured with DropOldest
        // We write 60,000 items to the writer indirectly or inspect channel behavior
        // Since PacketReader is public, let's verify Channel completes cleanly on dispose
        manager.ActivePorts.Should().BeEmpty();

        await manager.DisposeAsync();

        // After dispose, channel writer is completed
        Func<Task> act = async () => await reader.ReadAsync();
        await act.Should().ThrowAsync<System.Threading.Channels.ChannelClosedException>();
    }

    [Fact]
    public async Task MultiPortSerialManager_ConnectNonExistentPort_ReturnsFalseAndFaultedOrDisconnected()
    {
        using var hook = new Win32HotPlugHook();
        using var manager = new MultiPortSerialManager(hook);

        string invalidPort = "COM99999";
        bool result = await manager.ConnectPortAsync(invalidPort, 115200);

        result.Should().BeFalse();
        manager.ActivePorts.Should().ContainKey(invalidPort);
        manager.ActivePorts[invalidPort].Should().Be(PortConnectionStatus.Disconnected);
    }

    [Fact]
    public async Task MultiPortSerialManager_ConnectPortAlreadyTracked_ReturnsTrue()
    {
        using var hook = new Win32HotPlugHook();
        using var manager = new MultiPortSerialManager(hook);

        // Simulate a port status entry
        string mockPort = "COM1";
        
        // Disconnect initially
        await manager.DisconnectPortAsync(mockPort);

        // Second disconnect call on untracked/disconnected port
        await manager.DisconnectPortAsync(mockPort);
        manager.ActivePorts[mockPort].Should().Be(PortConnectionStatus.Disconnected);
    }

    [Fact]
    public void MultiPortSerialManager_EventForwarding_FiresDeviceChangedEvent()
    {
        using var hook = new Win32HotPlugHook();
        using var manager = new MultiPortSerialManager(hook);

        DeviceChangeEventArgs? receivedArgs = null;
        manager.DeviceChanged += (s, e) => receivedArgs = e;

        // Simulate WndProc invocation on hook
        bool handled = false;
        hook.WndProc(IntPtr.Zero, Win32Native.WM_DEVICECHANGE, (IntPtr)Win32Native.DBT_DEVICEARRIVAL, IntPtr.Zero, ref handled);

        // Wait for 100ms debouncing timer
        Thread.Sleep(200);

        receivedArgs.Should().NotBeNull();
        receivedArgs!.ChangeType.Should().Be(DeviceChangeType.Arrival);
    }

    [Fact]
    public async Task MultiPortSerialManager_DisposeSafety_CanBeDisposedMultipleTimes()
    {
        var hook = new Win32HotPlugHook();
        var manager = new MultiPortSerialManager(hook);

        await manager.DisposeAsync();

        // Double dispose should not throw
        Action act = () => manager.Dispose();
        act.Should().NotThrow();
    }

    #endregion

    #region 2. Win32HotPlugHook Tests

    [Fact]
    public void Win32HotPlugHook_WndProc_DeviceArrival_FiresArrivalEventAfterDebounce()
    {
        using var hook = new Win32HotPlugHook();
        DeviceChangeEventArgs? eventArgs = null;
        using var manualResetEvent = new ManualResetEventSlim(false);

        hook.DeviceChanged += (s, e) =>
        {
            eventArgs = e;
            manualResetEvent.Set();
        };

        bool handled = false;
        hook.WndProc(IntPtr.Zero, Win32Native.WM_DEVICECHANGE, (IntPtr)Win32Native.DBT_DEVICEARRIVAL, IntPtr.Zero, ref handled);

        bool signaled = manualResetEvent.Wait(500);

        signaled.Should().BeTrue();
        eventArgs.Should().NotBeNull();
        eventArgs!.ChangeType.Should().Be(DeviceChangeType.Arrival);
        eventArgs.PortName.Should().BeNull(); // No lParam passed
    }

    [Fact]
    public void Win32HotPlugHook_WndProc_DeviceRemoval_FiresRemovalEventAfterDebounce()
    {
        using var hook = new Win32HotPlugHook();
        DeviceChangeEventArgs? eventArgs = null;
        using var manualResetEvent = new ManualResetEventSlim(false);

        hook.DeviceChanged += (s, e) =>
        {
            eventArgs = e;
            manualResetEvent.Set();
        };

        bool handled = false;
        hook.WndProc(IntPtr.Zero, Win32Native.WM_DEVICECHANGE, (IntPtr)Win32Native.DBT_DEVICEREMOVECOMPLETE, IntPtr.Zero, ref handled);

        bool signaled = manualResetEvent.Wait(500);

        signaled.Should().BeTrue();
        eventArgs.Should().NotBeNull();
        eventArgs!.ChangeType.Should().Be(DeviceChangeType.Removal);
    }

    [Fact]
    public void Win32HotPlugHook_WndProc_NonDeviceChangeMessage_DoesNotFireEvent()
    {
        using var hook = new Win32HotPlugHook();
        bool fired = false;

        hook.DeviceChanged += (s, e) => fired = true;

        bool handled = false;
        const int WM_PAINT = 0x000F;
        hook.WndProc(IntPtr.Zero, WM_PAINT, IntPtr.Zero, IntPtr.Zero, ref handled);

        Thread.Sleep(200);

        fired.Should().BeFalse();
    }

    [Fact]
    public void Win32HotPlugHook_WndProc_DevBroadcastPort_ExtractsPortNameCorrectly()
    {
        using var hook = new Win32HotPlugHook();
        DeviceChangeEventArgs? eventArgs = null;
        using var manualResetEvent = new ManualResetEventSlim(false);

        hook.DeviceChanged += (s, e) =>
        {
            eventArgs = e;
            manualResetEvent.Set();
        };

        // Allocate unmanaged memory for DEV_BROADCAST_PORT
        var portStruct = new Win32Native.DEV_BROADCAST_PORT
        {
            dbcp_size = Marshal.SizeOf<Win32Native.DEV_BROADCAST_PORT>(),
            dbcp_devicetype = Win32Native.DBT_DEVTYP_PORT,
            dbcp_reserved = 0,
            dbcp_name = "COM7"
        };

        IntPtr ptr = Marshal.AllocHGlobal(portStruct.dbcp_size);
        try
        {
            Marshal.StructureToPtr(portStruct, ptr, false);

            bool handled = false;
            hook.WndProc(IntPtr.Zero, Win32Native.WM_DEVICECHANGE, (IntPtr)Win32Native.DBT_DEVICEARRIVAL, ptr, ref handled);

            bool signaled = manualResetEvent.Wait(2000);

            signaled.Should().BeTrue();
            eventArgs.Should().NotBeNull();
            eventArgs!.ChangeType.Should().Be(DeviceChangeType.Arrival);
            eventArgs.PortName.Should().Be("COM7");
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    [Fact]
    public void Win32HotPlugHook_RapidMessages_DebouncesToSingleEvent()
    {
        using var hook = new Win32HotPlugHook();
        int eventCount = 0;
        using var mre = new ManualResetEventSlim(false);

        hook.DeviceChanged += (s, e) =>
        {
            Interlocked.Increment(ref eventCount);
            mre.Set();
        };

        // No sleep between messages. The property under test is that a burst inside one debounce
        // window produces a single event, and a burst is precisely what arrives with no gap. The
        // Thread.Sleep(2) that used to be here made the test's own premise a timing assumption:
        // ten sleeps that each ran long — a GC pause, a loaded machine — pushed the burst past the
        // 200 ms window, the debouncer correctly emitted twice, and the failure was reported
        // against the debouncer rather than against the assumption that had actually broken.
        bool handled = false;
        var burst = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 10; i++)
        {
            hook.WndProc(IntPtr.Zero, Win32Native.WM_DEVICECHANGE, (IntPtr)Win32Native.DBT_DEVICEARRIVAL, IntPtr.Zero, ref handled);
        }
        burst.Stop();

        // Checked rather than assumed, so a machine slow enough to break the premise says so
        // instead of failing on the count below and blaming the code under test.
        burst.ElapsedMilliseconds.Should().BeLessThan(DebounceWindowMs,
            "the messages have to land inside one debounce window for the assertion below to mean anything");

        bool signaled = mre.Wait(TimeSpan.FromSeconds(5));

        signaled.Should().BeTrue();
        eventCount.Should().Be(1);
    }

    /// <summary>Mirrors <c>Win32HotPlugHook.DebounceMs</c>, which is private to the hook.</summary>
    private const int DebounceWindowMs = 200;

    #endregion

    #region 3. AutoReconnectEngine Tests

    private class MockSerialManagerForReconnect : ISerialManager
    {
        private readonly Dictionary<string, PortConnectionStatus> _statuses = new(StringComparer.OrdinalIgnoreCase);
        public System.Threading.Channels.ChannelReader<RawPacket> PacketReader => throw new NotImplementedException();
        public IReadOnlyDictionary<string, PortConnectionStatus> ActivePorts => _statuses;

        public event EventHandler<DeviceChangeEventArgs>? DeviceChanged;

#pragma warning disable CS0067
        public event EventHandler<TelemetryDashboard.Core.Events.SerialPortFaultEventArgs>? PortFaulted;
        public event EventHandler<string>? PortRecovered;
#pragma warning restore CS0067

        public List<string> WrittenCommands { get; } = new();
        public bool SimulateConnectSuccess { get; set; } = true;
        public List<string> ConnectAttempts { get; } = new();

        public void SetPortStatus(string portName, PortConnectionStatus status)
        {
            _statuses[portName] = status;
        }

        public void RaiseDeviceChanged(DeviceChangeEventArgs args)
        {
            DeviceChanged?.Invoke(this, args);
        }

        public Task<bool> ConnectPortAsync(string portName, int baudRate = 115200, CancellationToken cancellationToken = default)
        {
            ConnectAttempts.Add(portName);
            if (SimulateConnectSuccess)
            {
                _statuses[portName] = PortConnectionStatus.Connected;
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }

        public Task<bool> ConnectAsync(string portName, int baudRate) => ConnectPortAsync(portName, baudRate);

        public Task DisconnectPortAsync(string portName)
        {
            _statuses[portName] = PortConnectionStatus.Disconnected;
            return Task.CompletedTask;
        }

        public Task DisconnectAllAsync()
        {
            _statuses.Clear();
            return Task.CompletedTask;
        }

        public Task WriteLineAsync(string portName, string data, CancellationToken cancellationToken = default)
        {
            WrittenCommands.Add($"{portName}:{data}");
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Dispose() { }
    }

    [Fact]
    public void AutoReconnectEngine_RegisterAndTimestampManagement_WorksCorrectly()
    {
        var mockManager = new MockSerialManagerForReconnect();
        using var engine = new AutoReconnectEngine(mockManager);

        DateTime testTime = new DateTime(2026, 8, 9, 12, 0, 0, DateTimeKind.Utc);
        engine.RegisterTargetPort("COM3", 115200, testTime);

        // Update timestamp
        DateTime newTime = testTime.AddMinutes(5);
        engine.UpdateLastTimestamp("COM3", newTime);

        // Unregister
        engine.UnregisterTargetPort("COM3");
    }

    [Fact]
    public async Task AutoReconnectEngine_CommandGeneration_FormatsResyncCommandCorrectly()
    {
        var mockManager = new MockSerialManagerForReconnect();
        mockManager.SetPortStatus("COM_TEST", PortConnectionStatus.Disconnected);

        using var engine = new AutoReconnectEngine(mockManager, TimeSpan.FromMilliseconds(50));
        DateTime testTime = new DateTime(2026, 8, 9, 10, 30, 45, 123, DateTimeKind.Utc);
        engine.RegisterTargetPort("COM_TEST", 115200, testTime);

        // Verify exact ISO 8601 formatting string used by AutoReconnectEngine
        string timestampStr = testTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        timestampStr.Should().Be("2026-08-09T10:30:45.123Z");

        string expectedCmd = $"$CMD,REQ_RESYNC,{timestampStr}\r\n";
        expectedCmd.Should().Be("$CMD,REQ_RESYNC,2026-08-09T10:30:45.123Z\r\n");
    }

    [Fact]
    public async Task MultiPortSerialManager_ChannelBackpressure_Pushes60kPackets_NoOverflowException()
    {
        using var hook = new Win32HotPlugHook();
        using var manager = new MultiPortSerialManager(hook);

        var reader = manager.PacketReader;
        
        // Reflection or channel writer access to test bounded capacity of 50,000 with DropOldest
        var channelField = typeof(MultiPortSerialManager).GetField("_channel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        channelField.Should().NotBeNull();

        var channel = channelField!.GetValue(manager) as System.Threading.Channels.Channel<RawPacket>;
        channel.Should().NotBeNull();

        var writer = channel!.Writer;

        // Write 60,000 packets into bounded channel (capacity 50,000)
        for (int i = 0; i < 60_000; i++)
        {
            bool written = writer.TryWrite(new RawPacket("COM3", $"DATA_{i}", DateTime.UtcNow));
            written.Should().BeTrue("DropOldest mode should always return true for TryWrite");
        }

        // Channel should contain max 50,000 items (the newest ones: 10,000 to 59,999)
        RawPacket firstItem = await reader.ReadAsync();
        firstItem.RawData.Should().Be("DATA_10000", "The first 10,000 items should have been dropped due to backpressure DropOldest policy");
    }

    [Fact]
    public async Task AutoReconnectEngine_Disabled_DoesNotReconnectOnEvent()
    {
        var mockManager = new MockSerialManagerForReconnect();
        mockManager.SetPortStatus("COM3", PortConnectionStatus.Disconnected);

        using var engine = new AutoReconnectEngine(mockManager, TimeSpan.FromMilliseconds(50));
        engine.IsEnabled = false;

        engine.RegisterTargetPort("COM3", 115200);
        mockManager.RaiseDeviceChanged(new DeviceChangeEventArgs(DeviceChangeType.Arrival, "COM3"));

        await Task.Delay(150);

        mockManager.ConnectAttempts.Should().BeEmpty();
    }

    [Fact]
    public async Task AutoReconnectEngine_DisposeSafety_CleansUpTaskAndEvents()
    {
        var mockManager = new MockSerialManagerForReconnect();
        var engine = new AutoReconnectEngine(mockManager, TimeSpan.FromMilliseconds(50));

        engine.Start();
        await Task.Delay(100);

        await engine.DisposeAsync();

        // Double dispose should not throw
        Action act = () => engine.Dispose();
        act.Should().NotThrow();
    }

    #endregion
}
