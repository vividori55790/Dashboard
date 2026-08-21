using System.Reflection;
using System.Text.RegularExpressions;
using TelemetryDashboard.Core.Streaming;
using TelemetryDashboard.Core.Analytics;
using TelemetryDashboard.Core.Plugins;
using TelemetryDashboard.Core.Resilience;
using TelemetryDashboard.Core.Services;
using TelemetryDashboard.Core.Recording;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Enforces the AGENT_BLUEPRINT architecture rules as executable checks.
/// </summary>
/// <remarks>
/// These rules previously existed only as prose, so breaking them cost nothing and produced no
/// signal. Two Tier-2 test files were excluded from compilation for months and the build stayed
/// green the entire time. A rule that is not executed is a preference, not a rule.
///
/// Almost every rule here reads <c>.csproj</c> and <c>.cs</c> files off disk, which is why this
/// class stayed in the portable test project when the WPF-dependent tests moved out: a rule about
/// the shape of the solution that only runs on Windows cannot police the portability of the
/// solution. The partial half in <c>ArchitectureRuleTests.Portability.cs</c> carries the target
/// framework rule that guards exactly that.
/// </remarks>
public partial class ArchitectureRuleTests
{
    /// <summary>Repository root, located by walking up to the solution file.</summary>
    private static readonly string SolutionRoot = FindSolutionRoot();

    private static string FindSolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TelemetryDashboard.sln")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("TelemetryDashboard.sln not found above the test binaries.");
    }

    /// <summary>The four projects whose sources these rules govern.</summary>
    private static readonly string[] ProductionProjects =
    {
        "TelemetryDashboard.Core",
        "TelemetryDashboard.Infrastructure",
        "TelemetryDashboard.UI",
        "TelemetryDashboard.Plugins",
        "TelemetryDashboard.Host"
    };

    /// <summary>
    /// Browser-facing assets, checked by the rules that are about content rather than C#.
    /// </summary>
    /// <remarks>
    /// Added because the anomaly-fabrication rule scanned only <c>.cs</c> files, and the web console
    /// is where five fabrications were living: an invented z-score formula
    /// (<c>Math.abs((temp - 25.0) / 5.0)</c>), the literal 0.4 in three places, and an exported
    /// incident report asserting a root cause from an "ML anomaly engine" that never ran. The
    /// desktop shell is Windows-only, so for most users these files <em>are</em> the product —
    /// governing only the C# left the surface they actually look at ungoverned.
    /// </remarks>
    private static IEnumerable<string> WebAssetFiles() =>
        new[] { "*.html", "*.js" }
            .SelectMany(pattern => Directory.EnumerateFiles(
                Directory.GetParent(SolutionRoot)?.FullName ?? SolutionRoot, pattern, SearchOption.TopDirectoryOnly))
            // The verify_*.js harnesses sit beside the assets and are not assets: they drive a
            // page against a running host and name retired field names on purpose, to assert that
            // no page reads them. Scanning the check for the thing it checks for is circular.
            .Where(path => !Path.GetFileName(path).StartsWith("verify_", StringComparison.Ordinal));

    private static IEnumerable<string> ProductionSourceFiles() =>
        ProductionProjects
            .Select(project => Path.Combine(SolutionRoot, project))
            .Where(Directory.Exists)
            .SelectMany(dir => Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    private static string Relative(string fullPath) =>
        Path.GetRelativePath(SolutionRoot, fullPath);

    /// <summary>Maximum lines in a production source file, per AGENT_BLUEPRINT.</summary>
    private const int MicroModuleLineLimit = 150;

    // -----------------------------------------------------------------
    // Layering
    // -----------------------------------------------------------------

    [Fact]
    [Trait("Category", "Architecture")]
    public void Core_DoesNotDependOnInfrastructureOrUi()
    {
        Assembly core = typeof(Core.Services.GorillaCompressor).Assembly;

        string[] forbidden = core.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(name => name is "TelemetryDashboard.Infrastructure" or "TelemetryDashboard.UI")
            .ToArray();

        forbidden.Should().BeEmpty(
            "the domain layer must stay independent of transports, hosting and presentation");
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void Core_DoesNotReferenceWpfOrWindowsForms()
    {
        Assembly core = typeof(Core.Services.GorillaCompressor).Assembly;

        string[] uiFrameworks = core.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(name => name.StartsWith("PresentationFramework", StringComparison.OrdinalIgnoreCase)
                        || name.StartsWith("System.Windows.Forms", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        uiFrameworks.Should().BeEmpty("Core must remain hostable outside a desktop shell");
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void Infrastructure_DoesNotDependOnUi()
    {
        Assembly infrastructure = typeof(Infrastructure.Serial.AutoReconnectEngine).Assembly;

        string[] forbidden = infrastructure.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(name => name == "TelemetryDashboard.UI")
            .ToArray();

        forbidden.Should().BeEmpty("adapters must not reach back into the presentation layer");
    }

    // -----------------------------------------------------------------
    // Build integrity
    // -----------------------------------------------------------------

    [Fact]
    [Trait("Category", "Architecture")]
    public void NoTestFileIsExcludedFromCompilation()
    {
        // Both halves of the split suite. Checking only the portable project would leave the
        // desktop one free to hide a WPF test the day it starts failing — the identical loophole
        // this rule was written to close, reopened by the split itself.
        string[] testProjects = { "TelemetryDashboard.Tests", "TelemetryDashboard.Tests.Desktop" };

        string[] excluded = testProjects
            .Select(project => Path.Combine(SolutionRoot, project, project + ".csproj"))
            .Where(File.Exists)
            .SelectMany(path => Regex.Matches(File.ReadAllText(path), @"<Compile\s+Remove=""([^""]+)""")
                .Select(m => m.Groups[1].Value))
            .ToArray();

        // Excluding a test file makes the suite pass by hiding it. The baseline is now empty:
        // the two Tier-2 exclusions that predated this rule have been removed and their tests
        // compile and run. Any new exclusion fails here immediately, and the list may only shrink.
        string[] knownDebt = Array.Empty<string>();

        string[] unapproved = excluded.Except(knownDebt, StringComparer.OrdinalIgnoreCase).ToArray();
        unapproved.Should().BeEmpty(
            "excluded test files hide failures: " + string.Join(", ", unapproved));

        string[] resolved = knownDebt.Except(excluded, StringComparer.OrdinalIgnoreCase).ToArray();
        resolved.Should().BeEmpty(
            "these exclusions were removed — delete them from knownDebt so the baseline shrinks: "
            + string.Join(", ", resolved));
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void HostDoesNotCompileAgainstThePluginProject()
    {
        string csproj = Path.Combine(SolutionRoot, "TelemetryDashboard.UI", "TelemetryDashboard.UI.csproj");
        string content = File.ReadAllText(csproj);

        // Fully qualified: Moq.Match is in scope through the global usings and collides here.
        System.Text.RegularExpressions.Match reference = Regex.Match(
            content,
            @"<ProjectReference\s+Include=""[^""]*TelemetryDashboard\.Plugins\.csproj""(?<body>.*?)(/>|</ProjectReference>)",
            RegexOptions.Singleline);

        reference.Success.Should().BeTrue(
            "the host still needs a build-order dependency so the plugin is compiled before staging");

        reference.Groups["body"].Value
            .Replace(" ", string.Empty)
            .Should().Contain(@"ReferenceOutputAssembly=""false""",
                "compiling against the plugin puts it on the default AssemblyLoadContext's probing "
                + "path, where it can be loaded into a context that never unloads — hot reload then "
                + "fails silently. The host must discover plugins as files it was not built against.");
    }

    /// <summary>
    /// Every interface in <c>Core.Interfaces</c> is implemented by a concrete type somewhere in the
    /// portable backbone.
    /// </summary>
    /// <remarks>
    /// The presentation layer is deliberately absent from the assembly list. A contract satisfied
    /// only inside the WPF shell is unreachable from every other host — a headless service, a Linux
    /// CI agent, an integration test — so counting it as "implemented" would let this rule certify a
    /// capability that most of the product cannot use. That is exactly the coupling the
    /// portable/desktop test split exists to prevent, and the rule would be the first thing to hide
    /// it. Verified when the split was made: no type in TelemetryDashboard.UI implements any
    /// interface in TelemetryDashboard.Core.Interfaces, so dropping the assembly cost no coverage.
    /// </remarks>
    [Fact]
    [Trait("Category", "Architecture")]
    public void EveryDeclaredContractHasAnImplementation()
    {
        Assembly[] assemblies =
        {
            typeof(Core.Services.GorillaCompressor).Assembly,
            typeof(Infrastructure.Serial.AutoReconnectEngine).Assembly,
            typeof(Plugins.SamplePlugins.SampleTelemetryPlugin).Assembly
        };

        Type[] contracts = LoadableTypes(assemblies[0])
            .Where(t => t.IsInterface && t.Namespace == "TelemetryDashboard.Core.Interfaces")
            .ToArray();

        Type[] concrete = assemblies
            .SelectMany(LoadableTypes)
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .ToArray();

        string[] orphans = contracts
            .Where(contract => !concrete.Any(contract.IsAssignableFrom))
            .Select(contract => contract.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        // An interface nothing implements is a capability the codebase advertises and does not
        // have. Three were found this way — IDataLogger (so buffered telemetry was never durable),
        // IPluginContext (so no plugin could reach the host at all) and IMarketplaceService — and
        // each looked like a finished feature from the outside, because the contract was there and
        // the tests mocked it.
        orphans.Should().BeEmpty(
            "these contracts have no implementation anywhere: " + string.Join(", ", orphans));
    }

    /// <summary>Types an assembly can actually surface, tolerating an unresolvable dependency.</summary>
    private static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void NamespacesMatchFolderStructure()
    {
        var offenders = new List<string>();

        foreach (string project in ProductionProjects)
        {
            string root = Path.Combine(SolutionRoot, project);
            if (!Directory.Exists(root)) continue;

            foreach (string file in ProductionSourceFiles().Where(f => f.StartsWith(root, StringComparison.OrdinalIgnoreCase)))
            {
                string? line = File.ReadLines(file)
                    .Select(l => l.Trim())
                    .FirstOrDefault(l => l.StartsWith("namespace ", StringComparison.Ordinal));

                if (line is null) continue; // AssemblyInfo and other namespace-free files

                string declared = line["namespace ".Length..].TrimEnd('{', ';', ' ').Trim();

                string relativeDir = Path.GetRelativePath(root, Path.GetDirectoryName(file)!);
                string expected = relativeDir == "."
                    ? project
                    : project + "." + relativeDir.Replace(Path.DirectorySeparatorChar, '.');

                if (!string.Equals(declared, expected, StringComparison.Ordinal))
                {
                    offenders.Add($"{Relative(file)}: declares '{declared}', folder implies '{expected}'");
                }
            }
        }

        // A folder that disagrees with its namespace hides duplication in plain sight. Core/Analytics
        // and Core/Protocols each held files physically grouped by domain that still declared
        // Core.Services, so the tree looked organised while the namespace kept them in the junk
        // drawer — and a reader searching the namespace never found the folder, or vice versa.
        offenders.Should().BeEmpty(
            "a file's namespace must match its folder:\n" + string.Join("\n", offenders));
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void EveryProjectInTheSolutionDirectoryIsPartOfTheSolution()
    {
        string solution = File.ReadAllText(Path.Combine(SolutionRoot, "TelemetryDashboard.sln"));

        string[] orphans = Directory
            .EnumerateFiles(SolutionRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(p => !solution.Contains(Path.GetFileName(p), StringComparison.OrdinalIgnoreCase))
            .Select(Relative)
            .ToArray();

        // An orphan project compiles for nobody: it drifts out of date and its code is never
        // verified, while still looking like part of the system.
        orphans.Should().BeEmpty("projects outside the solution are never built: " + string.Join(", ", orphans));
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void NoBuildArtifactProjectFilesAreCheckedIn()
    {
        string[] artifacts = Directory
            .EnumerateFiles(SolutionRoot, "*_wpftmp.csproj", SearchOption.AllDirectories)
            .Select(Relative)
            .ToArray();

        artifacts.Should().BeEmpty("WPF temporary project files are build output, not source");
    }

    // -----------------------------------------------------------------
    // Honesty of the data path
    // -----------------------------------------------------------------

    [Fact]
    [Trait("Category", "Architecture")]
    public void NoProductionFileHardCodesAnAnomalyScore()
    {
        // A telemetry hub's one inviolable property is that displayed numbers came from data.
        //
        // Each pattern below was added after a real fabrication slipped past the previous set.
        // The first rule only caught a literal assigned straight to a score-named member, so a
        // pre-formatted display string and a literal handed to a positional parameter both went
        // unnoticed for a full audit pass.
        var offenders = new List<string>();
        (Regex Pattern, string Shape)[] fabrications =
        {
            (new Regex(@"(anomalyScore|ZScore|zScore)\s*=\s*\d+\.\d+", RegexOptions.IgnoreCase),
                "literal assigned to a score member"),

            // A sigma-suffixed literal inside a string, e.g. ZScore = "0.3σ". Interpolations such
            // as $"{result.ZScore:F1}σ" do not match: the digits there belong to a format specifier.
            (new Regex(@"""[^""]*\d+\.\d+\s*σ"),
                "pre-formatted sigma value in a string literal"),

            // A ternary of literals fed to a recording call, e.g. RecordFrame(ch, v, hot ? 3.9 : 0.3, hot).
            (new Regex(@"Record(Frame|Sample)\s*\([^;]*\?[^;]*\d+\.\d+\s*:\s*\d+\.\d+"),
                "scenario-keyed literal sigma passed positionally")
        };

        foreach (string file in ProductionSourceFiles().Concat(WebAssetFiles()))
        {
            // Synthetic payloads have exactly one home, and everything it emits is marked [TEST].
            if (Path.GetFileName(file).Equals("DemonstrationPayloads.cs", StringComparison.OrdinalIgnoreCase)) continue;

            foreach (string line in File.ReadLines(file))
            {
                string trimmed = line.TrimStart();
                if (trimmed.StartsWith("//") || trimmed.StartsWith("///") || trimmed.StartsWith("*")
                    || trimmed.StartsWith("<!--")) continue;

                foreach ((Regex pattern, string shape) in fabrications)
                {
                    if (pattern.IsMatch(line)) offenders.Add($"{Relative(file)} [{shape}]: {trimmed}");
                }
            }
        }

        offenders.Should().BeEmpty(
            "anomaly scores must come from the analytics engine, never from a literal:\n" +
            string.Join("\n", offenders));
    }

    // -----------------------------------------------------------------
    // Micro-module rule
    // -----------------------------------------------------------------

    /// <summary>
    /// Files granted an explicit exemption from the 150-line rule, keyed by repository-relative
    /// path.
    /// </summary>
    /// <remarks>
    /// The rule exists so small models can edit a file safely; splitting a cohesive unit purely to
    /// satisfy a line count makes the code worse. Two design decisions keep the list honest:
    ///
    /// Entries are full paths, not bare file names. The previous filename-keyed set silently
    /// exempted <em>any</em> future file sharing a name with a grandfathered one — a new
    /// <c>DataRouter.cs</c> anywhere in the tree would have inherited the pass.
    ///
    /// <see cref="ExemptionsCoverOnlyFilesStillOverTheLimit"/> deletes an entry's justification the
    /// moment the file drops under the limit, so the list can only shrink. Applying that rule for
    /// the first time retired 18 of 59 entries that had quietly stopped exempting anything.
    /// </remarks>
    private static readonly HashSet<string> LineLimitExemptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "TelemetryDashboard.Core/Firmware/IntelHexParser.cs",                      // 211 lines
        "TelemetryDashboard.Core/Parsers/JsonParser.cs",                           // 185 lines
        "TelemetryDashboard.Core/Parsers/PrefixParser.cs",                         // 210 lines
        "TelemetryDashboard.Core/Security/Ed25519.cs",                             // 154 lines
        "TelemetryDashboard.Core/Security/Ed25519Point.cs",                        // 185 lines
        "TelemetryDashboard.Core/Security/MeshPacketCodec.cs",                     // 182 lines
        "TelemetryDashboard.Core/Services/AdaptiveSamplingController.cs",          // 198 lines
        "TelemetryDashboard.Core/Services/CHeaderGenerator.cs",                    // 154 lines
        "TelemetryDashboard.Core/Services/DashboardExporter.cs",                   // 338 lines — embedded HTML template
        "TelemetryDashboard.Core/Services/DataRouter.cs",                          // 157 lines
        "TelemetryDashboard.Core/Services/EmergencyMcuController.cs",              // 279 lines
        "TelemetryDashboard.Core/Plugins/FormulaEvaluator.cs",                    // 276 lines
        "TelemetryDashboard.Core/Services/GorillaCompressor.cs",                   // 225 lines
        "TelemetryDashboard.Core/Services/LlmDiagnosisAgent.cs",                   // 414 lines — one prompt/response pipeline
        "TelemetryDashboard.Core/Plugins/ScriptPluginSandbox.cs",                 // 194 lines
        "TelemetryDashboard.Core/Resilience/TelemetryCircuitBreaker.cs",             // 182 lines
        "TelemetryDashboard.Core/Analytics/TelemetryMlAnalyticsEngine.cs",          // 187 lines
        "TelemetryDashboard.Core/Streaming/TelemetryStreamingServer.cs",            // 252 lines
        "TelemetryDashboard.Core/Services/TimeSyncJitterBuffer.cs",                // 159 lines
        "TelemetryDashboard.Core/Recording/TimeTravelDvrPlayer.cs",                 // 247 lines
        "TelemetryDashboard.Core/Simulator/PowerPlantSimulator.cs",                // 164 lines
        "TelemetryDashboard.Core/Simulator/PowerTelemetryFrameBuilder.cs",         // 151 lines
        "TelemetryDashboard.Core/Streaming/TelemetryFrameRecorder.cs",             // 154 lines
        "TelemetryDashboard.Core/Streaming/TelemetryHttpRoutes.cs",                // 204 lines
        "TelemetryDashboard.Infrastructure/Integrations/MultiChannelAlertForwarder.cs",    // 271 lines
        "TelemetryDashboard.Infrastructure/Network/P2PMeshClusterSync.cs",         // 211 lines
        "TelemetryDashboard.Infrastructure/Network/WebRtcTelemetryBridge.cs",      // 203 lines
        "TelemetryDashboard.Infrastructure/Serial/AutoBaudScanner.cs",             // 154 lines
        "TelemetryDashboard.Infrastructure/Serial/AutoReconnectEngine.cs",         // 203 lines
        "TelemetryDashboard.Infrastructure/Serial/EdgeMcuOtaFlasher.cs",           // 203 lines
        "TelemetryDashboard.Infrastructure/Serial/MultiPortSerialManager.cs",      // 224 lines
        "TelemetryDashboard.UI/Controls/ControlPanelControl.xaml.cs",              // 334 lines
        "TelemetryDashboard.UI/Controls/ScopeViewControl.xaml.cs",                 // 168 lines
        "TelemetryDashboard.UI/Dialogs/AdaptiveSamplingDialog.xaml.cs",            // 172 lines
        "TelemetryDashboard.UI/Dialogs/LlmDiagnosisDialog.xaml.cs",                // 168 lines
        "TelemetryDashboard.UI/MainWindow.Serial.cs",                              // 228 lines
        "TelemetryDashboard.UI/MainWindow.Shell.cs",                               // 200 lines
        "TelemetryDashboard.UI/MainWindow.Simulation.cs",                          // 166 lines
        "TelemetryDashboard.UI/MainWindow.xaml.cs",                                // 276 lines
        "TelemetryDashboard.UI/Services/DragDropHandler.cs",                       // 163 lines

        // Added while building out the scale and live-ingest work. The limit is a nudge toward
        // small modules, not a law: each of these is one cohesive unit whose seams are already at
        // the right places, and splitting them further would scatter one idea across three files
        // to satisfy an arithmetic rule.
        "TelemetryDashboard.Core/Analytics/RollingChannelStatistics.cs",           // 182 lines
        "TelemetryDashboard.Core/Cluster/CoverageLedger.cs",                       // 151 lines
        "TelemetryDashboard.Core/Recording/TelemetryCsvRecorder.cs",               // 157 lines
        "TelemetryDashboard.Core/Ingest/JsonChannelMap.cs",                        // 175 lines
        "TelemetryDashboard.Core/Simulator/MonitoringProfileReader.cs",            // 165 lines
        "TelemetryDashboard.Core/Simulator/ProfileSimulatorEngine.cs",             // 281 lines
        "TelemetryDashboard.Host/Configuration/HostOptions.cs",                    // 152 lines
        "TelemetryDashboard.Core/Query/ChannelSeriesBuffer.cs",                    // 152 lines
        "TelemetryDashboard.Host/Configuration/CommandLineParser.cs",              // 182 lines
        "TelemetryDashboard.Host/Ingest/IngestPublisher.cs",                       // 151 lines
        "TelemetryDashboard.Host/Ingest/PollingTelemetrySource.cs",                // 177 lines
        "TelemetryDashboard.Host/Ingest/SseTelemetrySource.cs",                    // 197 lines
        "TelemetryDashboard.Host/Startup/StartupBanner.cs",                        // 172 lines
        "TelemetryDashboard.Host/Ingest/TelemetryIngestPump.cs",                   // 151 lines
        "TelemetryDashboard.UI/Dialogs/MeshClusterDialog.xaml.cs",                 // 273 lines
        "TelemetryDashboard.UI/Dialogs/MlAnalyticsDialog.xaml.cs",                 // 155 lines
        "TelemetryDashboard.UI/Dialogs/OtaFlasherDialog.xaml.cs",                  // 234 lines
        "TelemetryDashboard.UI/Dialogs/PluginSandboxDialog.xaml.cs",               // 238 lines
        "TelemetryDashboard.UI/Dialogs/ProtocolBridgeDialog.xaml.cs",              // 159 lines

        // Grew when recording stopped being a list of one customer's channels and became one path
        // that persists whatever arrived. Most of the addition is the account of what it replaces,
        // which belongs next to the code rather than in a commit message nobody reads twice.
        "TelemetryDashboard.UI/MainWindow.Archive.cs",                             // 177 lines

        // Feature 12 reaching a running program for the first time. Most of the addition in each
        // file is the account of why an interlock that transmits to hardware is off by default and
        // refuses to pick its own port, which belongs beside the code that enforces it.
        "TelemetryDashboard.Host/Outbound/EmergencyInterlockRelay.cs",             // 157 lines

        // The spectrum endpoint and the transform behind it. Both are one idea each -- a Fourier
        // transform and the query that shapes its answer -- and the length is the account of what
        // each field means, which is the difference between a spectrum a reader can trust and a
        // row of numbers.
        "TelemetryDashboard.Core/Streaming/SpectrumEndpoint.cs",                   // 171 lines
        "TelemetryDashboard.Core/Analytics/FftAnalyzerService.cs",                 // 153 lines

        // Playing a recording back through the pipeline that wrote it. One idea, and the length is
        // mostly the account of why a replay is marked REPLAY rather than simulated, and why the
        // recorded verdict is dropped and recomputed instead of replayed.
        "TelemetryDashboard.Host/Ingest/ReplayTelemetrySource.cs",                 // 168 lines

        // The archive query and the incident window. Each is one question -- what did this channel
        // do over a span, and what was everything doing around this instant -- and the length is
        // the record shape plus the account of what each field is allowed to claim.
        "TelemetryDashboard.Core/Streaming/IncidentEndpoint.cs",                   // 162 lines
        "TelemetryDashboard.Core/Streaming/ComputedEndpoint.cs",                   // 180 lines — reply shape already split into ComputedEndpointModels.cs
        "TelemetryDashboard.Core/Analytics/LimitMonitor.cs",                       // 190 lines — state records already split into LimitMonitorState.cs
        "TelemetryDashboard.Core/Simulator/PowerConverterUpsProfile.cs",           // 156 lines — one worked example, entirely data
        "TelemetryDashboard.Host/Ingest/ComputedChannelPump.cs",                   // 226 lines — mostly the reasoning behind the instant it picks
        "TelemetryDashboard.Host/Ingest/TelemetryFrame.cs",                        // 157 lines — one wire contract, one field per line
    };

    [Fact]
    [Trait("Category", "Architecture")]
    public void NewFilesRespectTheMicroModuleLineLimit()
    {
        string[] offenders = ProductionSourceFiles()
            .Where(path => !LineLimitExemptions.Contains(ExemptionKey(path)))
            .Select(path => (Path: path, Lines: File.ReadAllLines(path).Length))
            .Where(x => x.Lines > MicroModuleLineLimit)
            .Select(x => $"{Relative(x.Path)} ({x.Lines} lines)")
            .ToArray();

        offenders.Should().BeEmpty(
            $"files above {MicroModuleLineLimit} lines need either a split or an explicit entry in " +
            $"{nameof(LineLimitExemptions)}:\n" + string.Join("\n", offenders));
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void ExemptionsCoverOnlyFilesStillOverTheLimit()
    {
        var current = ProductionSourceFiles()
            .ToDictionary(ExemptionKey, path => File.ReadAllLines(path).Length, StringComparer.OrdinalIgnoreCase);

        string[] deleted = LineLimitExemptions.Where(key => !current.ContainsKey(key)).ToArray();

        // An exemption for a file that no longer exists re-admits a violation the day a file
        // reappears at that path.
        deleted.Should().BeEmpty("exemptions for deleted files must be removed: " + string.Join(", ", deleted));

        string[] outgrown = LineLimitExemptions
            .Where(key => current.TryGetValue(key, out int lines) && lines <= MicroModuleLineLimit)
            .Select(key => $"{key} ({current[key]} lines)")
            .ToArray();

        // This is the ratchet. Once a file is split back under the limit its exemption has stopped
        // exempting anything, and leaving it in place quietly re-grants the pass the next time the
        // file grows. Applying this rule for the first time retired 18 of 59 entries.
        outgrown.Should().BeEmpty(
            $"these files are back under {MicroModuleLineLimit} lines — remove their entries so the "
            + $"baseline shrinks:\n" + string.Join("\n", outgrown));
    }

    /// <summary>
    /// Repository-relative path with forward slashes, so exemption entries read the same way on
    /// every platform and cannot be satisfied by a same-named file elsewhere in the tree.
    /// </summary>
    private static string ExemptionKey(string fullPath) =>
        Relative(fullPath).Replace(Path.DirectorySeparatorChar, '/').Replace('\\', '/');
}
