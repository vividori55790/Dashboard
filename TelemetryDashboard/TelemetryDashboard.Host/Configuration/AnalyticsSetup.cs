using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using TelemetryDashboard.Core.Analytics.Detectors;
using TelemetryDashboard.Infrastructure.Analytics;

namespace TelemetryDashboard.Host.Configuration;

/// <summary>
/// Turns an analytics configuration file into the detectors a run will actually use.
/// </summary>
/// <remarks>
/// The file is the point. Which detectors watch which channels, and which model is consulted, are
/// deployment decisions — a different plant wants a different set, and an operator who has to
/// rebuild to add one will not add one. This is the same argument <c>JsonChannelMap</c> makes about
/// ingest, applied to judgement.
/// <para>Assembled in the host rather than in Core because only the host may reach both: the
/// in-process detectors are portable arithmetic, and the model client is a transport. Core cannot
/// see Infrastructure, and that separation is worth more than the convenience of one factory.</para>
/// </remarks>
public sealed class AnalyticsSetup : IAsyncDisposable
{
    /// <summary>Environment variable naming the configuration file, overriding the default location.</summary>
    /// <remarks>
    /// Declared here rather than in <see cref="EnvironmentVariables"/> because it does not flow
    /// through <see cref="HostOptions"/>: it is consumed where the detectors are built, not where
    /// the socket is bound.
    /// </remarks>
    public const string PathVariable = "TELEMETRY_HOST_DETECTORS";

    private readonly IDisposable? _endpoint;

    private AnalyticsSetup(DetectorPanel panel, RemoteInferenceDetector? inference, IDisposable? endpoint, IReadOnlyList<string> report)
    {
        Panel = panel;
        Inference = inference;
        Report = report;
        _endpoint = endpoint;
    }

    /// <summary>The detectors, ready to be asked about samples.</summary>
    public DetectorPanel Panel { get; }

    /// <summary>The external-model detector, or null when no model was configured.</summary>
    public RemoteInferenceDetector? Inference { get; }

    /// <summary>Lines describing what was configured, for a startup banner or a log.</summary>
    public IReadOnlyList<string> Report { get; }

    /// <summary>
    /// The configuration this process is running under, loaded once.
    /// </summary>
    /// <remarks>
    /// Process-wide, unlike the per-publisher analytics engine, because a model connection is a
    /// process resource: one bounded queue, one HTTP client, one set of counters. Per-channel state
    /// inside each detector is keyed by the fully qualified channel name, which already carries the
    /// node, so two sources cannot collide unless they are publishing the same channel — in which
    /// case they are the same channel and sharing the baseline is correct. A malformed file throws
    /// here and the exception is cached, so every later caller gets the same refusal rather than a
    /// host that half-started.
    /// </remarks>
    public static AnalyticsSetup Shared => Lazy.Value;

    private static readonly Lazy<AnalyticsSetup> Lazy = new(() => Load());

    /// <summary>Resolved path of the configuration file, whether or not it exists.</summary>
    public static string ResolvePath(string? explicitPath = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath)) return explicitPath!;

        string? fromEnvironment = Environment.GetEnvironmentVariable(PathVariable);
        return string.IsNullOrWhiteSpace(fromEnvironment)
            ? Path.Combine(AppContext.BaseDirectory, DetectorConfigurationReader.DefaultFileName)
            : fromEnvironment.Trim();
    }

    /// <summary>Loads from a file, or builds an empty setup when there is no file to load.</summary>
    /// <exception cref="InvalidDataException">The file exists but describes something unusable.</exception>
    public static AnalyticsSetup Load(string? explicitPath = null)
    {
        string path = ResolvePath(explicitPath);
        return Build(DetectorConfigurationReader.LoadOrNone(path), path);
    }

    /// <summary>Builds a setup from a configuration already in hand, so the wiring is testable.</summary>
    public static AnalyticsSetup Build(DetectorConfiguration configuration, string source = "(in memory)")
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var detectors = new List<IChannelDetector>(DetectorFactory.Create(configuration));
        var report = new List<string>();

        RemoteInferenceDetector? inference = null;
        IDisposable? endpoint = null;

        if (configuration.Inference is { Runtime: not InferenceRuntime.None } spec)
        {
            HttpInferenceEndpoint http = CreateEndpoint(spec);
            endpoint = http;
            inference = new RemoteInferenceDetector(http, spec);
            detectors.Add(inference);
        }

        var panel = new DetectorPanel(detectors);

        report.Add(panel.IsEmpty
            ? $"analytics: no detectors configured ({source})"
            : $"analytics: {panel.Detectors.Count} detector(s) from {source}");

        foreach (IChannelDetector detector in panel.Detectors) report.Add("  detector " + detector.DetectorId);

        return new AnalyticsSetup(panel, inference, endpoint, report);
    }

    /// <summary>
    /// Builds the client for the configured model, or refuses when this build cannot host it.
    /// </summary>
    /// <remarks>
    /// The refusal for an in-process runtime is deliberate and is the honest form of "not
    /// implemented". Accepting the setting and quietly running no model would produce a host that
    /// reports the model as configured, never scores anything with it, and is indistinguishable
    /// from one whose model happens to be finding nothing wrong.
    /// </remarks>
    private static HttpInferenceEndpoint CreateEndpoint(InferenceSpec spec)
    {
        if (spec.Runtime == InferenceRuntime.InProcess)
        {
            throw new InvalidDataException(
                $"An in-process model was configured ('{spec.ModelPath}') but this build carries no "
                + "in-process inference runtime. IInferenceEndpoint is the seam it would implement; "
                + "until one is present, use an 'http' runtime or remove the inference section "
                + "rather than run a host that reports a model it never consults.");
        }

        if (!Uri.TryCreate(spec.Endpoint, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidDataException($"Inference endpoint '{spec.Endpoint}' is not an absolute http(s) URL.");
        }

        return new HttpInferenceEndpoint(uri, TimeSpan.FromMilliseconds(spec.TimeoutMs), spec.ModelId);
    }

    public async ValueTask DisposeAsync()
    {
        if (Inference is not null) await Inference.DisposeAsync().ConfigureAwait(false);
        _endpoint?.Dispose();
    }
}
