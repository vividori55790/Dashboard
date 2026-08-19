using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TelemetryDashboard.Core.Recording;

/// <summary>
/// Real-Time Telemetry CSV Disk Persistence Recorder.
/// Automatically creates real CSV files on disk with buffered flushing,
/// thread-safe queuing, and recording statistics (packet count, file size, duration).
/// </summary>
public class TelemetryCsvRecorder : IDisposable
{
    private readonly ConcurrentQueue<string> _lineQueue = new();
    private StreamWriter? _writer;
    private FileStream? _fileStream;
    private CancellationTokenSource? _cts;
    private Task? _flushTask;

    public bool IsRecording { get; private set; } = false;
    public string CurrentFilePath { get; private set; } = string.Empty;
    public long RecordedPacketCount { get; private set; } = 0;
    public DateTime RecordingStartTime { get; private set; } = DateTime.MinValue;

    public long FileSizeBytes
    {
        get
        {
            if (string.IsNullOrEmpty(CurrentFilePath) || !File.Exists(CurrentFilePath)) return 0;
            try { return new FileInfo(CurrentFilePath).Length; } catch { return 0; }
        }
    }

    public TimeSpan RecordingDuration => IsRecording ? (DateTime.UtcNow - RecordingStartTime) : TimeSpan.Zero;

    public string StartRecording(string? outputDirectory = null, string? customFileName = null)
    {
        if (IsRecording) return CurrentFilePath;

        string targetDir = outputDirectory ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        if (!Directory.Exists(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        string fileName = customFileName ?? $"telemetry_log_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
        CurrentFilePath = Path.Combine(targetDir, fileName);

        _fileStream = new FileStream(CurrentFilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        _writer = new StreamWriter(_fileStream, Encoding.UTF8) { AutoFlush = false };

        // Write CSV Header
        _writer.WriteLine("Timestamp_ISO,Timestamp_Sec,NodeId,Channel,Value,ZScore,IsAnomaly,Predicted_60s,Status");
        _writer.Flush();

        RecordedPacketCount = 0;
        RecordingStartTime = DateTime.UtcNow;
        IsRecording = true;

        _cts = new CancellationTokenSource();
        _flushTask = Task.Run(() => FlushLoopAsync(_cts.Token), _cts.Token);

        return CurrentFilePath;
    }

    public void RecordSample(string nodeId, string channel, double value, double zScore, bool isAnomaly, double predicted60s, string status = "OK")
    {
        if (!IsRecording) return;

        double nowSec = DateTime.UtcNow.Ticks / 10_000_000.0;
        string isoTime = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

        string line = string.Format(CultureInfo.InvariantCulture,
            "{0},{1:F3},{2},{3},{4:F4},{5:F2},{6},{7:F4},{8}",
            isoTime, nowSec, nodeId, channel, value, zScore, isAnomaly ? "TRUE" : "FALSE", predicted60s, status);

        _lineQueue.Enqueue(line);
        Interlocked.Increment(ref _totalQueued);
    }

    private long _totalQueued = 0;

    private async Task FlushLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(100, token);
                FlushQueue();
            }
            catch (OperationCanceledException) { break; }
            catch { }
        }
        FlushQueue();
    }

    private void FlushQueue()
    {
        if (_writer == null) return;

        int flushed = 0;
        while (_lineQueue.TryDequeue(out string? line))
        {
            _writer.WriteLine(line);
            flushed++;
            RecordedPacketCount++;
        }

        if (flushed > 0)
        {
            _writer.Flush();
        }
    }

    public string StopRecording()
    {
        if (!IsRecording) return CurrentFilePath;

        IsRecording = false;
        _cts?.Cancel();

        try
        {
            _flushTask?.Wait(500);
        }
        catch { }

        FlushQueue();

        _writer?.Dispose();
        _writer = null;

        _fileStream?.Dispose();
        _fileStream = null;

        return CurrentFilePath;
    }

    public void Dispose()
    {
        StopRecording();
    }
}
