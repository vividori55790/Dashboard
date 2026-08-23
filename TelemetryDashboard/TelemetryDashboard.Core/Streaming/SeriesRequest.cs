using System;

namespace TelemetryDashboard.Core.Streaming;

/// <summary>
/// Reading which channels a series query asked for, and refusing one that asked for none.
/// </summary>
/// <remarks>
/// The endpoint used to answer a request naming nothing with a well-formed frame holding zero
/// points and no complaint. That reads as "this host has no data", which is the sentence a caller
/// who wrote <c>?channel=</c> instead of <c>?channels=</c> will believe — and both spellings look
/// equally reasonable from outside.
/// <para>
/// Found exactly that way. A query for a channel the computed endpoint was aligning 292 samples
/// from at that same moment came back empty, and the store was suspected before the request was.
/// <see cref="AlignedEndpoint"/> already refused the same mistake by name; these two are siblings
/// and now answer it the same way.
/// </para>
/// </remarks>
public static class SeriesRequest
{
    /// <summary>What a caller is told when they name no channel.</summary>
    public const string NoChannelsNamed =
        "no channels named; pass ?channels=a,b,c (plural). A request naming none is answered with "
        + "nothing, which is not the same thing as a host holding nothing.";

    /// <summary>Splits the <c>channels</c> parameter, or explains why it cannot be used.</summary>
    public static bool TryChannels(string? raw, out string[] channels, out string? refusal)
    {
        channels = (raw ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        refusal = channels.Length == 0 ? NoChannelsNamed : null;
        return refusal is null;
    }
}
