namespace TelemetryDashboard.Core.Streaming;

/// <summary>
/// This host's own counters: what it took in, what it turned away, and who is reading it.
/// </summary>
/// <remarks>
/// These are the ones an operator misses first. A hub that has been refusing every connection for
/// an hour looks, from a browser that will not load, exactly like a network fault, and the number
/// that settles it -- <c>stream_connections_refused_total</c> rising -- was until now readable only
/// by opening the console that is refusing to open.
/// </remarks>
public static partial class MetricsEndpoint
{
    private static void WriteHost(Document document, TelemetryStreamingServer server)
    {
        document.Open("stream_clients", "gauge",
            "Clients currently attached to the live feed over WebSocket or SSE.")
            .Sample(server.ConnectedClientCount);

        document.Open("stream_clients_limit", "gauge",
            "Most concurrent stream clients this host admits. Connections beyond it are refused "
            + "rather than admitted into a hub whose existing clients would pay for them.")
            .Sample(server.MaxStreamClients);

        document.Open("stream_subscribers", "gauge",
            "Clients being served a reduced series rather than the raw feed.")
            .Sample(server.SubscribedClientCount);

        document.Open("stream_connections_refused_total", "counter",
            "Connections turned away because the client limit was already reached. Rising means "
            + "somebody cannot connect for a reason that is this host's and not the network's.")
            .Sample(server.RefusedConnections);

        document.Open("frames_broadcast_total", "counter",
            "Whole telemetry frames fanned out to subscribers.")
            .Sample(server.TotalPacketsBroadcasted);

        document.Open("reduced_frames_sent_total", "counter",
            "Reduced series frames sent to subscribers on the display path.")
            .Sample(server.ReducedFramesSent);

        document.Open("reduced_points_sent_total", "counter",
            "Points inside those frames -- what the display path actually costs the wire.")
            .Sample(server.ReducedPointsSent);

        document.Open("channels", "gauge",
            "Channels the series store is currently holding history for.")
            .Sample(server.Series.ChannelCount);

        document.Open("channels_limit", "gauge",
            "Channels this host will admit before refusing new ones.")
            .Sample(server.Series.MaxChannels);

        document.Open("samples_accepted_total", "counter",
            "Samples written into a channel's history.")
            .Sample(server.Series.SamplesAccepted);

        document.Open("samples_refused_total", "counter",
            "Samples dropped because their channel could not be admitted past the channel ceiling. "
            + "Non-zero means some channel is not queryable at all, and a chart of it is blank for "
            + "a reason that has nothing to do with the sensor.")
            .Sample(server.Series.SamplesRefused);

        WriteReachability(document, server);
        WriteExchange(document, server);
    }

    /// <summary>
    /// What protects this listener, answered by the socket rather than by the documentation.
    /// </summary>
    /// <remarks>
    /// Booleans as 0/1 gauges so they can be alerted on, which is the only reason to export them at
    /// all: "reachable from the network and not encrypted" is a rule an operator can write once and
    /// have hold across a fleet, and it is not a question a person remembers to re-ask after a
    /// deployment changes a flag.
    /// <para>
    /// A zero here is measured, not missing. <c>encrypted</c> answers false on every binding this
    /// product can construct today, and dropping it on that ground would leave a consumer to assume
    /// the better of the two -- which is the failure this endpoint is organised against, wearing
    /// the opposite disguise.
    /// </para>
    /// </remarks>
    private static void WriteReachability(Document document, TelemetryStreamingServer server)
    {
        document.Open("listener_network_reachable", "gauge",
            "1 when this host accepts connections from other machines, 0 when it binds loopback.")
            .Sample(server.IsNetworkReachable ? 1.0 : 0.0);

        document.Open("listener_authenticated", "gauge",
            "1 when every path on this listener demands a credential.")
            .Sample(server.Access is not null ? 1.0 : 0.0);

        document.Open("listener_encrypted", "gauge",
            "1 when the link this listener binds encrypts what crosses it. A credential over a "
            + "cleartext link is only as private as the segment.")
            .Sample(server.IsLinkEncrypted ? 1.0 : 0.0);
    }

    /// <summary>
    /// Idempotent exchange, or nothing at all when nothing on this host is checking.
    /// </summary>
    /// <remarks>
    /// The whole block is absent when no filter is attached, exactly as <c>/api/status</c> sends
    /// <c>exchange: null</c> there. A host that deduplicates nothing and one that deduplicates and
    /// has found nothing are different facts, and <c>duplicates_refused_total 0</c> would state the
    /// second while meaning the first -- inside an alert rule that reads a flat counter as a clean
    /// link.
    /// <para>
    /// <c>unsequenced</c> is the field that keeps the other one readable, and it is the reason this
    /// block is worth exporting rather than summarising. A sender that stamps no sequence can never
    /// produce a duplicate, so its duplicate count is zero forever while nothing watches it; only
    /// the ratio of the two says whether the zero means anything.
    /// </para>
    /// </remarks>
    private static void WriteExchange(Document document, TelemetryStreamingServer server)
    {
        if (server.Duplicates is not { } filter) return;

        document.Open("exchange_samples_admitted_total", "counter",
            "Samples admitted because this host had not already taken them.")
            .Sample(filter.Admitted);

        document.Open("exchange_duplicates_refused_total", "counter",
            "Samples refused as already taken. Rising means a link is reconnecting and replaying, "
            + "and that it is no longer inflating this host's totals.")
            .Sample(filter.Duplicates);

        document.Open("exchange_unsequenced_samples_total", "counter",
            "Samples admitted without being checked, because their sender stamped no sequence. "
            + "Read the duplicate count above against this one: a link that is never checked "
            + "reports zero duplicates forever.")
            .Sample(filter.Unsequenced);

        document.Open("exchange_tracked_senders", "gauge",
            "Sender and epoch pairs whose recent sequence numbers are being remembered.")
            .Sample(filter.TrackedSenders);

        document.Open("exchange_sender_evictions_total", "counter",
            "Senders that fell off the end of that table. Past it a genuine duplicate is admitted "
            + "rather than refused, which is the safe direction but is no longer a guarantee.")
            .Sample(filter.SenderEvictions);
    }
}
