using System.Text.RegularExpressions;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Fails when a public production type is reachable from nothing.
/// </summary>
/// <remarks>
/// This rule exists because the same defect kept recurring and nothing looked for it. A script
/// engine, a retry policy, a plugin context and a marketplace client were each written, tested and
/// then connected to no execution path — every one of them passing its tests while doing nothing
/// in a running program. "Built" had come to mean "the tests pass" rather than "a user can reach
/// it", and only a manual audit ever caught the difference.
///
/// The baseline below is deliberately large: it is the honest state of the codebase the day the
/// rule was written, not an aspiration. <see cref="EveryUnwiredTypeInTheBaselineIsStillUnwired"/>
/// forces an entry out the moment its type gains a reference, so the list can only shrink.
/// </remarks>
public partial class ArchitectureRuleTests
{
    /// <summary>Entry points the runtime instantiates, which no source references by name.</summary>
    private static readonly HashSet<string> FrameworkEntryPoints = new(StringComparer.Ordinal)
    {
        "App",     // WPF application object, named in App.xaml
        "Program"  // host entry point, invoked by the runtime
    };

    /// <summary>
    /// Types known to be referenced by nothing, each one a feature that does not currently run.
    /// </summary>
    /// <remarks>
    /// Grouped roughly by why. Several are headline features whose implementation is complete and
    /// tested — the circuit breaker, the jitter buffer, the auto-reconnect engine — but which no
    /// host constructs, so the protection they describe is not in force anywhere.
    /// </remarks>
    private static readonly HashSet<string> UnwiredTypes = new(StringComparer.Ordinal)
    {
        // Surfaced only once StripComments was added: each of these had been counted as wired
        // because a sibling file mentioned it in a <see cref="..."/>. The audit that produced this
        // baseline undercounted for exactly that reason.
        "AlertUXService",
        "AnomalyEngine",              // superseded by TelemetryMlAnalyticsEngine on the live path
        "AppTheme",
        "AstNode",
        // AutoReconnectEngine retired from this baseline: SerialTelemetrySource starts it when a
        // port opens, so a dropped cable is now retried instead of ending the run. Making it work
        // needed a real fix first -- the read loop swallowed the failure, leaving the port marked
        // Connected forever and reconnection impossible.
        "BinaryOpNode",
        "CommandItem",
        // DashboardExporter retired from this baseline: TelemetryDashboard.Host writes a standalone
        // HTML console behind --export-dashboard, built from the profile in force. Feature 6 was
        // marked Built since M2 while nothing constructed it, so the page had never been opened --
        // which is how it kept a connection chip reading "WS CONNECTED" that no code updated, and a
        // widget that filled a missing field in from the temperature and then from zero.
        "DeltaCursorService",
        "DerivedNumericProjection",
        "DropResult",
        "DvrFrameEventArgs",
        // EmergencyMcuController retired from this baseline: EmergencyInterlockRelay constructs it
        // behind --emergency-stop, so the one feature that acts on the machine rather than watching
        // it can now be reached from a running program. It stays off by default and is refused
        // without --serial, because the controller ships a rule that auto-executes against a port
        // literally named COM3 -- a host that armed itself would be writing to whatever happened to
        // be there.
        "EventLogEntry",
        // ExtensionRegistry retired from this baseline: installing exists now, so the host has
        // something to register. ExtensionLoader fills it from the extension store and asks it
        // GetCompatibleExtensions(hostApiVersion) to decide what may load, which is the one place
        // that rule is applied — the entry has stopped exempting anything.
        "FailureSnapshotExtractor",
        // FftAnalyzerService retired from this baseline: it moved from UI/ViewModels to
        // Core/Analytics and SpectrumEndpoint puts it behind /api/spectrum, so any browser on any
        // platform can ask for a channel's frequency content. Its address was why nothing could
        // reach it -- the headless host must never reference the WPF project, so the one place a
        // spectrum is useful to every client could not have it. Its first run against live data
        // found a defect in the series store, which was writing every channel into one series.
        "FunctionCallNode",
        "HeatmapInterpolationService",
        "KestrelWebServer",
        // ManifestIndexMarketplace retired from this baseline: TelemetryDashboard.Host's
        // ExtensionCatalogueReport now constructs it behind --extensions, so the catalogue is
        // reachable from a running host and the entry has stopped exempting anything.
        // MatFileWriter retired from this baseline: MatlabArchiveExporter now drives it from the
        // shell's "MATLAB .mat 내보내기" button, reading the durable archive through IDataLogger, so
        // the entry has stopped exempting anything.
        "MockSerialPort",
        // Publishing a report needs a trigger the headless host does not have yet: unlike an alert
        // or a stream sample, there is no moment in a run that obviously means "publish now".
        "NotionClient",
        "NodeStatus",
        "NumberNode",
        "PeerHubDisplayModel",
        "PluginItemModel",
        "PortablePackageChecker",
        // Redundant with PythonScriptEngine, which is what the sandbox actually loads. Both embed
        // IronPython; keeping two entry points to one interpreter is the thing to fix, not to wire.
        "PythonNetAdapter",
        "ReplayRowItem",
        // Bound from XAML only, which this rule's tokeniser cannot see: a view model reached as
        // {Binding Caption} or {Binding IsOn} inside a DataTemplate appears nowhere as a type name.
        // Listed rather than "fixed", because the honest statement is that the rule is blind here,
        // not that these types are dead. Both are verifiably live: the node switches they back were
        // driven from a running window and produced real commands.
        "NodePowerToggle",
        "RelayCommand",
        "SampleTelemetryPlugin",
        "SamplingChannelRow",
        "ScopeViewModel",
        // SessionReplayPlayer retired from this baseline: ReplayTelemetrySource attaches it as a
        // source behind --replay, so a recording can be played back through the pipeline that wrote
        // it and the whole stack -- routing, analytics, console, spectrum, alignment, DVR -- works
        // on recorded data. Wiring it found that the row parser dropped the NodeId column, so two
        // devices reporting the same channel name collapsed into one series on replay.
        "SignalGeneratorService",
        // SlackClient, MqttPublisher and GitHubUpdater retired from this baseline: the headless
        // host now relays anomalies to a webhook (--slack-webhook), republishes every scored sample
        // to a broker (--mqtt) and checks a release feed at start-up (--check-updates). Before
        // this the host had no outbound path at all, so the retry-on-429 work done on these
        // clients was correctness applied to code no user could reach.
        "SqliteIndexRepository",      // no caller indexes archives yet; the DVR reads files directly
        "SseStreamHandler",
        // RecordPipeline, NumericPacketStage, StageActivity and TelemetryCircuitBreaker retired
        // from this baseline: TelemetryIngestPump now routes every arrival through the record path,
        // and IngestRateGuard puts the breaker in force. Before this the M6 record layer carried
        // nothing and the documented flood protection was not applied anywhere.
        // TimeSyncJitterBuffer retired from this baseline: AlignedEndpoint puts it behind
        // /api/aligned, so Feature 2 can be reached from a running program. Wiring it meant fixing
        // what it said first -- it returned 0.0 for a node that had sent nothing, which is also an
        // ordinary reading, and clamped silently to the nearest sample for any instant outside its
        // buffer. A test asserted the first of those as the required behaviour.
        "Twin3DService",              // the 3D viewport control it holds state for is not in the shell
        "VariableNode",
        "Win32Native",
        "WindowBackdropType",
        "WorkspaceLayoutState",
        "WorkspaceManager",
    };

    private static IEnumerable<string> ReferenceScanFiles() =>
        ProductionProjects
            .Select(p => Path.Combine(SolutionRoot, p))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.*", SearchOption.AllDirectories))
            .Where(p => p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                     || p.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)
                     || p.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    /// <summary>Public types declared in production source, mapped to their declaring file.</summary>
    private static Dictionary<string, string> DeclaredPublicTypes()
    {
        var pattern = new Regex(
            @"^public\s+(?:sealed\s+|static\s+|abstract\s+|partial\s+)*(?:class|record|interface|enum|struct)\s+([A-Za-z0-9_]+)",
            RegexOptions.Multiline);

        var declared = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string file in ProductionSourceFiles())
        {
            foreach (System.Text.RegularExpressions.Match match in pattern.Matches(File.ReadAllText(file)))
            {
                declared.TryAdd(match.Groups[1].Value, file);
            }
        }
        return declared;
    }

    /// <summary>Type names appearing in any production file, mapped to the files mentioning them.</summary>
    private static Dictionary<string, HashSet<string>> TypeMentions(IEnumerable<string> names)
    {
        var wanted = new HashSet<string>(names, StringComparer.Ordinal);
        var mentions = wanted.ToDictionary(n => n, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase), StringComparer.Ordinal);
        var token = new Regex(@"[A-Za-z_][A-Za-z0-9_]*");

        foreach (string file in ReferenceScanFiles())
        {
            foreach (System.Text.RegularExpressions.Match m in token.Matches(StripComments(File.ReadAllText(file))))
            {
                if (wanted.Contains(m.Value)) mentions[m.Value].Add(file);
            }
        }
        return mentions;
    }

    /// <summary>
    /// Removes commentary so a type named only in prose does not count as wired.
    /// </summary>
    /// <remarks>
    /// A <c>&lt;see cref="X"/&gt;</c> in a doc comment reads to a plain tokeniser exactly like a
    /// constructor call, so a dead type stayed invisible to this rule as long as some surviving
    /// file still talked about it — which is precisely the situation around abandoned code. That
    /// blind spot was found by a reviewer, not by the rule, which is the argument for closing it.
    ///
    /// The stripping is deliberately blunt: it also truncates a line at a <c>//</c> inside a string
    /// literal such as a URL. That direction of error is the safe one. Over-stripping can only
    /// report a wired type as unwired, which fails the test loudly and gets looked at;
    /// under-stripping hides a dead feature, which is the defect being hunted.
    /// </remarks>
    private static string StripComments(string source)
    {
        source = Regex.Replace(source, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        source = Regex.Replace(source, @"<!--.*?-->", " ", RegexOptions.Singleline);
        return Regex.Replace(source, @"//[^
]*", " ");
    }

    /// <summary>Types nobody references, excluding the framework entry points and the baseline.</summary>
    private static string[] UnreferencedTypes(out Dictionary<string, string> declared)
    {
        declared = DeclaredPublicTypes();
        Dictionary<string, HashSet<string>> mentions = TypeMentions(declared.Keys);

        return declared
            .Where(d => !FrameworkEntryPoints.Contains(d.Key))
            .Where(d => !mentions[d.Key].Any(f => !string.Equals(f, d.Value, StringComparison.OrdinalIgnoreCase)))
            .Select(d => d.Key)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void NoNewProductionTypeIsUnreachable()
    {
        string[] newlyUnwired = UnreferencedTypes(out Dictionary<string, string> declared)
            .Where(n => !UnwiredTypes.Contains(n))
            .Select(n => $"{n} ({Relative(declared[n])})")
            .ToArray();

        newlyUnwired.Should().BeEmpty(
            "a public type nothing references is a feature that does not run, however well it is "
            + "tested. Wire it into a host, or add it to " + nameof(UnwiredTypes) + " with a reason:\n"
            + string.Join("\n", newlyUnwired));
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void EveryUnwiredTypeInTheBaselineIsStillUnwired()
    {
        var stillUnwired = new HashSet<string>(UnreferencedTypes(out _), StringComparer.Ordinal);
        Dictionary<string, string> declared = DeclaredPublicTypes();

        string[] gone = UnwiredTypes.Where(n => !declared.ContainsKey(n)).ToArray();
        gone.Should().BeEmpty("baseline entries for deleted types must go: " + string.Join(", ", gone));

        string[] nowWired = UnwiredTypes.Where(n => declared.ContainsKey(n) && !stillUnwired.Contains(n)).ToArray();

        // The ratchet. Leaving a wired type on the list would quietly re-grant it the exemption the
        // day someone disconnected it again.
        nowWired.Should().BeEmpty(
            "these are reachable now — remove them from " + nameof(UnwiredTypes)
            + " so the baseline shrinks: " + string.Join(", ", nowWired));
    }
}
