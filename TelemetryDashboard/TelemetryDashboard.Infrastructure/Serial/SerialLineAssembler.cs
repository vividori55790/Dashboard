namespace TelemetryDashboard.Infrastructure.Serial;

using System.Text;

/// <summary>
/// Reassembles newline-terminated lines from a byte stream that arrives in arbitrary chunks.
/// </summary>
/// <remarks>
/// A serial read returns whatever bytes happen to be in the driver buffer, so a frame is routinely
/// split across two reads and two frames routinely share one. Keeping the partial line here rather
/// than inside the read loop makes that boundary behaviour testable on its own, which matters
/// because the failure it prevents — a frame silently cut in half and parsed as two — produces
/// plausible numbers rather than an error.
/// </remarks>
public sealed class SerialLineAssembler
{
    /// <summary>
    /// Longest line kept before the buffer is abandoned.
    /// </summary>
    /// <remarks>
    /// A device emitting binary, or one whose newline never arrives, would otherwise grow this
    /// without bound. Discarding is the honest response: half a frame is not a measurement, and
    /// the discard is counted so it is not invisible.
    /// </remarks>
    public const int MaxLineLength = 2048;

    private readonly StringBuilder _line = new(256);

    /// <summary>Partial lines abandoned for exceeding <see cref="MaxLineLength"/>.</summary>
    public long OverlongDiscards { get; private set; }

    /// <summary>Characters currently held in the incomplete line.</summary>
    public int Pending => _line.Length;

    /// <summary>Feeds bytes in and invokes <paramref name="onLine"/> for each complete line.</summary>
    public void Append(ReadOnlySpan<byte> bytes, Action<string> onLine)
    {
        ArgumentNullException.ThrowIfNull(onLine);

        foreach (byte b in bytes)
        {
            if (b == (byte)'\n')
            {
                string completed = _line.ToString().TrimEnd('\r');
                _line.Clear();

                if (completed.Length > 0) onLine(completed);
                continue;
            }

            _line.Append((char)b);

            if (_line.Length > MaxLineLength)
            {
                _line.Clear();
                OverlongDiscards++;
            }
        }
    }

    /// <summary>Drops any partial line, for a reconnect that must not splice two sessions together.</summary>
    public void Reset() => _line.Clear();
}
