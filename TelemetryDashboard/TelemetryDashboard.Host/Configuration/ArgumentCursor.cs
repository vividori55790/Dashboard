namespace TelemetryDashboard.Host.Configuration;

/// <summary>
/// Reading and rejecting command-line values, kept apart from deciding what the options mean.
/// </summary>
/// <remarks>
/// Extracted so the parser's switch stays the one readable list of what this host accepts. Every
/// failure here produces a <see cref="HostOptions"/> carrying an <see cref="HostOptions.Error"/>
/// rather than an exception, because a mistyped command line is an expected outcome that deserves
/// a message and an exit code.
/// </remarks>
internal static class ArgumentCursor
{
    /// <summary>Takes the value following the current argument, advancing the index.</summary>
    public static bool TryValue(string[] args, ref int index, out string value)
    {
        if (index + 1 >= args.Length)
        {
            value = string.Empty;
            return false;
        }

        value = args[++index];
        return !string.IsNullOrWhiteSpace(value);
    }

    /// <summary>Splits <c>host</c> or <c>host:port</c>, defaulting the port.</summary>
    /// <remarks>
    /// An unparseable port is refused rather than silently defaulted: a typo in the port of a
    /// broker address would otherwise connect somewhere the operator did not ask for, or fail with
    /// an error naming a port they never typed.
    /// </remarks>
    public static bool TryHostAndPort(string raw, int defaultPort, out string host, out int port)
    {
        host = raw;
        port = defaultPort;

        int separator = raw.LastIndexOf(':');
        if (separator <= 0) return true;

        string tail = raw[(separator + 1)..];
        if (!int.TryParse(tail, out int parsed) || parsed < 1 || parsed > 65535) return false;

        host = raw[..separator];
        port = parsed;
        return host.Length > 0;
    }

    public static HostOptions MissingValue(string option) => Fail($"{option} requires a value.");

    public static HostOptions Fail(string message) => new() { Error = message };
}
