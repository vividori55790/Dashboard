using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Parsers;
using TelemetryDashboard.Infrastructure.Replay;

namespace TelemetryDashboard.Host.Ingest;

/// <summary>
/// Plays a recorded CSV back through the live pipeline.
/// </summary>
/// <remarks>
/// <see cref="SessionReplayPlayer"/> could load a recording since M2 and was constructed by nothing,
/// so a recording could be written and never read by the program that wrote it. Attaching it as a
/// source means the whole stack works on recorded data: routing, the analytics engine, the console,
/// the spectrum, the alignment endpoint and the DVR all behave exactly as they do live, because from
/// their side nothing is different.
/// <para>
/// Frames are re-emitted in this repository's own <c>$TELE</c> format rather than pushed in as
/// packets, for the same reason the simulator does: the parser and the routing rules are then
/// exercised by the replay too. It also means the recorded z-score is deliberately dropped. The
/// analytics engine recomputes a verdict from the values, which is what this project argues for
/// everywhere else — a score stored beside the value it came from is a second copy that disagrees
/// with the engine after any change to the detector.
/// </para>
/// <para>
/// <see cref="Origin"/> is <c>REPLAY</c> so every frame says what it is. A recording played back is
/// not a live reading, and a console that could not tell them apart would show an incident from last
/// week as though it were happening now.
/// </para>
/// </remarks>
public sealed class ReplayTelemetrySource : ITelemetrySource
{
    /// <summary>Longest gap honoured between two frames.</summary>
    /// <remarks>
    /// A recording that was paused overnight has an eight-hour gap in it. Sleeping through that
    /// faithfully would be a replay nobody watches, so the gap is compressed and the compression is
    /// reported rather than done silently.
    /// </remarks>
    public const double MaximumGapSec = 5.0;

    private readonly SessionReplayPlayer _player = new();
    private readonly string _path;
    private readonly double _speed;

    public ReplayTelemetrySource(string path, double speed = 1.0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!double.IsFinite(speed) || speed <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(speed), speed, "Replay speed must be positive.");
        }

        _path = path;
        _speed = speed;
    }

    /// <summary>Frames the recording contained.</summary>
    public int FrameCount => _player.Frames.Count;

    /// <summary>Length of the recording in seconds.</summary>
    public double DurationSec => _player.TotalDurationSeconds;

    /// <summary>Gaps that were longer than <see cref="MaximumGapSec"/> and were compressed.</summary>
    public int CompressedGaps { get; private set; }

    public string Origin => "REPLAY";

    /// <summary>
    /// False: a replay is not synthetic.
    /// </summary>
    /// <remarks>
    /// What it is instead is <em>not live</em>, which <see cref="Origin"/> carries. Marking a replay
    /// as simulated would say the data was invented, and a recording of real hardware was not. If
    /// the recording was itself of a simulator run, its node ids still carry the <c>SIM:</c> prefix
    /// they were written with, so that fact survives the round trip on its own.
    /// </remarks>
    public bool IsSimulated => false;

    public string Description =>
        $"{System.IO.Path.GetFileName(_path)} — {FrameCount:N0} frame(s) over {DurationSec:F1}s at {_speed:0.##}x";

    /// <summary>Loads the recording. Returns false when it holds nothing playable.</summary>
    public bool Load()
    {
        _player.LoadSession(_path);
        return _player.Frames.Count > 0;
    }

    public async IAsyncEnumerable<RawPacket> ReadAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        IReadOnlyList<DvrFrame> frames = _player.Frames;
        double previous = frames.Count > 0 ? frames[0].TimestampSec : 0.0;

        foreach (DvrFrame frame in frames)
        {
            if (cancellationToken.IsCancellationRequested) yield break;

            double gap = frame.TimestampSec - previous;
            previous = frame.TimestampSec;

            if (gap > MaximumGapSec)
            {
                CompressedGaps++;
                gap = MaximumGapSec;
            }

            if (gap > 0)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(gap / _speed), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    yield break;
                }
            }

            yield return new RawPacket(Origin, Frame(frame), DateTime.UtcNow);
        }
    }

    /// <summary>Re-encodes one recorded frame as a <c>$TELE</c> line.</summary>
    /// <remarks>
    /// The recorded channel name is <c>node.variable</c>, and the frame format wants them apart. The
    /// split is on the first dot, because a variable may contain dots — <c>ambient.temperature</c>
    /// does — while a node id written by the recorder does not.
    /// </remarks>
    private static string Frame(DvrFrame recorded)
    {
        string channel = recorded.ChannelName ?? string.Empty;
        int dot = channel.IndexOf('.');

        string node = dot > 0 ? channel[..dot] : "REPLAY";
        string variable = dot > 0 ? channel[(dot + 1)..] : channel;

        string body = string.Create(CultureInfo.InvariantCulture,
            $"TELE,{Sanitise(node)},{Sanitise(variable)},{recorded.Value},");

        byte checksum = XorChecksum.Calculate(Encoding.UTF8.GetBytes(body));
        return $"${body}*{checksum:X2}";
    }

    /// <summary>Strips the delimiters the frame format uses, so a recorded name cannot break it.</summary>
    private static string Sanitise(string value)
    {
        string trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Length == 0) return "unnamed";

        var builder = new StringBuilder(trimmed.Length);
        foreach (char c in trimmed)
        {
            builder.Append(c is ',' or '*' or '$' or '\r' or '\n' ? '_' : c);
        }

        return builder.ToString();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
