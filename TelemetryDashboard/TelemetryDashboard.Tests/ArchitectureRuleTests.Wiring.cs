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
/// Reachability is judged per <em>file</em>, not per type: a type counts as reached when something
/// outside its declaring file names any type that file declares. Judging it per type -- which this
/// did until a full audit of the baseline -- calls a helper dead whenever it lives beside the entry
/// point that uses it, and that was sixteen of thirty-four entries.
///
/// What that costs is the reverse case: a genuinely dead type sharing a file with a live one is
/// now invisible here. That trade is deliberate. A rule whose list is half false positives stops
/// being read, and the entries it was hiding behind them are the ones worth acting on.
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
        // Eighteen entries left this baseline at once when reachability moved from per-type to
        // per-file, and none of them by being wired: AppTheme, BinaryOpNode, CommandItem,
        // DropResult, DvrFrameEventArgs, EventLogEntry, FunctionCallNode, NodeStatus, NumberNode,
        // PeerHubDisplayModel, PluginItemModel, ReplayRowItem, NodePowerToggle, RelayCommand,
        // SamplingChannelRow, VariableNode, Win32Native, WindowBackdropType.
        //
        // They were never dead. Each lives beside the entry point that uses it -- an AST node built
        // by the parser in the same file, a row model built by its service, an enum owned by the
        // service that reads it -- and the old rule asked whether some *other file* named the type.
        // Half of this list was that, which made the metric misleading in the direction that costs
        // the most: it hid the entries worth acting on behind noise, and it did so for months.
        //
        // Two of them, NodePowerToggle and RelayCommand, carried a comment blaming XAML binding.
        // That diagnosis was wrong -- they are plain C# in the same file as their user -- and a
        // wrong explanation in a governance list is worse than none, because it stops the next
        // reader from looking.
        //
        // Three of them are not honestly alive either: Win32Native's WndProc is called from tests
        // and from nothing else (there is no HwndSource.AddHook anywhere, so the Win32 hot-plug
        // path has never run and PortPresencePoller is what actually works); DvrFrameEventArgs is
        // raised only by a push API no production caller uses; NodeStatus types a property nothing
        // reads or writes. The corrected rule cannot see any of that, which is the blind spot it
        // trades for honesty, and they are recorded here rather than left to be rediscovered.
        // Surfaced only once StripComments was added: each of these had been counted as wired
        // because a sibling file mentioned it in a <see cref="..."/>. The audit that produced this
        // baseline undercounted for exactly that reason.
        "AlertUXService",
        "AnomalyEngine",              // superseded by TelemetryMlAnalyticsEngine on the live path
        // AstNode retired from this baseline: ComputedChannel holds one and asks it what
        // channels it reads, which is what /api/computed needs before it can align them. The
        // expression engine had been reachable only from a WPF dialog and from DataRouter's
        // formula rules, so the headless product -- the cross-platform half -- could not
        // compute anything at all. Wiring it meant giving the tree a way to say "no value":
        // its resolver returned double, so a channel that had never reported and a channel
        // reading zero volts were the same answer, and an unknown function name was 0.0.
        // AutoReconnectEngine retired from this baseline: SerialTelemetrySource starts it when a
        // port opens, so a dropped cable is now retried instead of ending the run. Making it work
        // needed a real fix first -- the read loop swallowed the failure, leaving the port marked
        // Connected forever and reconnection impossible.
        // DashboardExporter retired from this baseline: TelemetryDashboard.Host writes a standalone
        // HTML console behind --export-dashboard, built from the profile in force. Feature 6 was
        // marked Built since M2 while nothing constructed it, so the page had never been opened --
        // which is how it kept a connection chip reading "WS CONNECTED" that no code updated, and a
        // widget that filled a missing field in from the temperature and then from zero.
        // DeltaCursorService retired from this baseline: ScopeViewControl holds one, places its
        // cursors from real clicks on the plot and draws the delta over the trace. The scope could
        // show a transient and offered no way to say how long it lasted or how far the rail
        // dropped, which is the first question anyone asks of a waveform. Wiring it needed one
        // addition -- HasAnyCursor, so the drawing code can tell "one cursor down" from "none"
        // without inferring it from coordinates and getting a cursor at the origin wrong.
        "DerivedNumericProjection",
        // EmergencyMcuController retired from this baseline: EmergencyInterlockRelay constructs it
        // behind --emergency-stop, so the one feature that acts on the machine rather than watching
        // it can now be reached from a running program. It stays off by default and is refused
        // without --serial, because the controller ships a rule that auto-executes against a port
        // literally named COM3 -- a host that armed itself would be writing to whatever happened to
        // be there.
        // ExtensionRegistry retired from this baseline: installing exists now, so the host has
        // something to register. ExtensionLoader fills it from the extension store and asks it
        // GetCompatibleExtensions(hostApiVersion) to decide what may load, which is the one place
        // that rule is applied — the entry has stopped exempting anything.
        // FailureSnapshotExtractor retired from this baseline: IncidentEndpoint puts it behind
        // /api/incident, over the durable archive, so an alert's timestamp turns into the run-up to
        // it across every channel. It lived in Infrastructure/Storage and depends on nothing but
        // TelemetryPacket, which is why nothing could reach it -- Core must not reference
        // Infrastructure, so the endpoint layer where an incident window is asked for could not
        // have it.
        // FftAnalyzerService retired from this baseline: it moved from UI/ViewModels to
        // Core/Analytics and SpectrumEndpoint puts it behind /api/spectrum, so any browser on any
        // platform can ask for a channel's frequency content. Its address was why nothing could
        // reach it -- the headless host must never reference the WPF project, so the one place a
        // spectrum is useful to every client could not have it. Its first run against live data
        // found a defect in the series store, which was writing every channel into one series.
        "HeatmapInterpolationService",
        // KestrelWebServer left this baseline by being deleted. It started a WebApplication
        // serving exactly one route -- /health -- and no telemetry, no assets and no client;
        // TelemetryStreamingServer has served /ws, /stream and eleven /api routes the whole
        // time. It was also the solution's only ASP.NET Core consumer, and the sole reason
        // Infrastructure carried a FrameworkReference that flowed on to Host and UI -- so a
        // framework-dependent deployment needed the ASP.NET Core runtime installed for one
        // route nothing called. Both are gone.
        // ManifestIndexMarketplace retired from this baseline: TelemetryDashboard.Host's
        // ExtensionCatalogueReport now constructs it behind --extensions, so the catalogue is
        // reachable from a running host and the entry has stopped exempting anything.
        // MatFileWriter retired from this baseline: MatlabArchiveExporter now drives it from the
        // shell's "MATLAB .mat 내보내기" button, reading the durable archive through IDataLogger, so
        // the entry has stopped exempting anything.
        // MockSerialPort retired from this baseline: LoopbackSerialManager builds ports out of
        // it behind --serial loopback, so a run has a serial path with no device on it. That
        // exists for one thing that could not otherwise be checked -- the emergency interlock
        // is refused without --serial, so on a workstation with no MCU the furthest anyone
        // could get was "the relay reports itself armed". It now writes a command that can be
        // watched leaving the host, and the first live run showed 91 of them in twenty seconds
        // from a five-second cooldown the limit path had bypassed.
        // Publishing a report needs a trigger the headless host does not have yet: unlike an alert
        // or a stream sample, there is no moment in a run that obviously means "publish now".
        "NotionClient",
        "PortablePackageChecker",
        // Redundant with PythonScriptEngine, which is what the sandbox actually loads. Both embed
        // IronPython; keeping two entry points to one interpreter is the thing to fix, not to wire.
        // PythonNetAdapter left this baseline by being deleted, but only after the one thing
        // it had that the shipping engine lacked was moved across. It was a timeout wrapper
        // around EmbeddedPythonRuntime, and it held the only cancellation machinery in the
        // codebase -- while PythonScriptEngine, which is what actually loads a .py plugin,
        // ran files and hooks with no budget at all. That mechanism now guards both the load
        // and the per-packet invoke; the wrapper was a second entry point to one interpreter.
        "SampleTelemetryPlugin",
        "ScopeViewModel",
        // SessionReplayPlayer retired from this baseline: ReplayTelemetrySource attaches it as a
        // source behind --replay, so a recording can be played back through the pipeline that wrote
        // it and the whole stack -- routing, analytics, console, spectrum, alignment, DVR -- works
        // on recorded data. Wiring it found that the row parser dropped the NodeId column, so two
        // devices reporting the same channel name collapsed into one series on replay.
        // SignalGeneratorService retired from this baseline: --signal drives a channel with a
        // declared waveform through InjectedSignal, so the analysis half of the product can be
        // checked instead of trusted. The simulator emits one shape per channel at a period
        // derived from a hash, so /api/spectrum had never had a ground truth to be measured
        // against -- the evidence that its peaks were right was that they looked plausible.
        // Its first run found the reference itself wrong: a declared 2 Hz came back at 1.888,
        // six bins out, because the generator advanced phase by the interval it asked for
        // rather than the time that passed, and the simulator ticks at 9.5 Hz against a
        // nominal 10. The analyser was right. Fixed at the cause, the same signal reads
        // 2.0103 Hz -- 0.55 of a bin.
        // SlackClient, MqttPublisher and GitHubUpdater retired from this baseline: the headless
        // host now relays anomalies to a webhook (--slack-webhook), republishes every scored sample
        // to a broker (--mqtt) and checks a release feed at start-up (--check-updates). Before
        // this the host had no outbound path at all, so the retry-on-429 work done on these
        // clients was correctness applied to code no user could reach.
        // SqliteIndexRepository left this baseline by being deleted, which is the other way an
        // unreachable type stops being one. It described itself as a fast lookup of "which file
        // and offset holds a channel at a given moment" and could not answer that: byte_offset
        // was declared in its schema and never written, and the archive column it did write had
        // no method that reads it. What it actually did was store a second, narrower copy of
        // every sample with no way to read the rows back -- SqliteDataLogger with the query
        // removed. The need it named is already met by wiring that exists: --replay <csv>
        // --archive <db> plays a recording through the pipeline and into the durable store,
        // measured at 990 CSV rows in and 990 samples queryable out.
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
        // WorkspaceLayoutState deleted rather than wired. It held a preset name and two lists of
        // panel names, with a hardcoded switch mapping "ScopeMode" to the string "ScopeView" -- a
        // model of the dock that was never connected to the dock. LayoutManager does the same job
        // against the real DockingManager. The only thing that ever exercised it was a Tier1 test
        // that declared its own copy of the class at the bottom of the test file, so five green
        // assertions covered a class the product could have lost without noticing.
        // WorkspaceManager retired from this baseline: WorkspaceStore uses it to keep the
        // operator's panel arrangement between sessions. Every piece of that feature already
        // existed -- a serialiser that could write the dock and read it back, a file store, and a
        // profile type with a LayoutXml field to carry it -- and nothing called any of it, so the
        // window came back the way it shipped at every launch. Joining the two ends needed one
        // real fix first: AvalonDock's deserialiser restores the shape of the panes and not their
        // contents, so without answering its LayoutSerializationCallback the arrangement came back
        // correct and every pane in it empty.
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

        // A file is reached when something outside it names one of the types it declares.
        var reachedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach ((string name, string file) in declared)
        {
            if (FrameworkEntryPoints.Contains(name)
                || mentions[name].Any(f => !string.Equals(f, file, StringComparison.OrdinalIgnoreCase)))
            {
                reachedFiles.Add(file);
            }
        }

        // Then a type is unreachable only when its declaring file is unreachable too. Asking
        // whether the type itself is named from outside -- which is what this did -- reported
        // every helper that lives beside its own entry point as dead: an AST node built by the
        // parser in the same file, a row model built by its service, an enum owned by the service
        // that reads it. Sixteen of the thirty-four entries on the baseline were that, so half the
        // list was noise and the metric it produced was misleading in the direction that matters.
        return declared
            .Where(d => !FrameworkEntryPoints.Contains(d.Key))
            .Where(d => !reachedFiles.Contains(d.Value))
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
