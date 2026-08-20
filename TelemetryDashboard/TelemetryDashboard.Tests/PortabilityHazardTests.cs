using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using FluentAssertions;
using TelemetryDashboard.Core.Ingest;
using TelemetryDashboard.Core.Models;
using Xunit;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Hunts, on Windows, for the bugs that only appear on Linux and macOS.
/// </summary>
/// <remarks>
/// This repository publishes a Linux and a macOS binary and has never run either, because there is
/// no machine here to run them on. The documentation says so rather than implying coverage, which
/// is honest but does not make the risk smaller.
///
/// So instead of waiting for a machine, this hunts the specific ways a Windows-developed .NET
/// program fails elsewhere. They are a short and well-known list — case-sensitive file systems,
/// path separators, and a culture that formats numbers differently — and every one of them can be
/// detected from here. That is not the same as running it, and nothing here should be read as
/// proof that it runs. It is a way of turning "we have no idea" into "these particular things are
/// ruled out", which is the difference between an unknown risk and a bounded one.
/// </remarks>
public class PortabilityHazardTests
{
    private static string SolutionRoot()
    {
        DirectoryInfo? here = new(AppContext.BaseDirectory);
        while (here is not null && !File.Exists(Path.Combine(here.FullName, "TelemetryDashboard.sln")))
        {
            here = here.Parent;
        }

        return here?.FullName ?? AppContext.BaseDirectory;
    }

    /// <summary>Projects that must run off Windows. The WPF shell is Windows-only by construction.</summary>
    private static readonly string[] PortableProjects =
    {
        "TelemetryDashboard.Core",
        "TelemetryDashboard.Infrastructure",
        "TelemetryDashboard.Plugins",
        "TelemetryDashboard.Host"
    };

    private static IEnumerable<string> PortableSources() =>
        PortableProjects
            .Select(p => Path.Combine(SolutionRoot(), p))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories))
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    private static string Relative(string path) => Path.GetRelativePath(SolutionRoot(), path);

    [Fact]
    [Trait("Category", "Portability")]
    public void NoPathIsBuiltByGluingStringsWithABackslash()
    {
        // A literal backslash in a path is a file that does not exist on Linux, and the failure is
        // usually a silent "not found" rather than an error naming the separator.
        var hazard = new Regex(@"""[^""\n]*[A-Za-z0-9_)\]]\\\\[A-Za-z0-9_(\[][^""\n]*""");

        string[] offenders = PortableSources()
            .SelectMany(file => hazard.Matches(File.ReadAllText(file))
                .Select(m => $"{Relative(file)}: {m.Value}"))
            .Where(hit => !hit.Contains("\\\\n") && !hit.Contains("\\\\r") && !hit.Contains("\\\\t"))
            .ToArray();

        offenders.Should().BeEmpty(
            "a backslash-separated path resolves on Windows and silently does not exist elsewhere:\n"
            + string.Join("\n", offenders));
    }

    [Fact]
    [Trait("Category", "Portability")]
    public void NumbersAreParsedAndFormattedWithAFixedCulture()
    {
        // The classic: on a machine whose locale uses a decimal comma, double.Parse("41.9") either
        // throws or returns 419. Telemetry parsed on a German or Korean-with-comma host would be
        // wrong by a factor of ten and never say so.
        // Scanning a fixed window after the call rather than trying to capture its arguments. A
        // regex that stops at the first ')' is fooled by a nested call such as
        // double.TryParse(element.GetString(), ...) and reported a correct line as a defect — which
        // it did, on this test's first run. A check that cries wolf gets switched off, so the check
        // itself has to be right before its findings mean anything.
        var parseCall = new Regex(@"double\.(?:Try)?Parse\s*\(", RegexOptions.Singleline);
        const int windowChars = 220;

        var offenders = new List<string>();

        foreach (string file in PortableSources())
        {
            string text = File.ReadAllText(file);
            foreach (System.Text.RegularExpressions.Match match in parseCall.Matches(text))
            {
                string window = text[match.Index..Math.Min(text.Length, match.Index + windowChars)];
                if (window.Contains("InvariantCulture") || window.Contains("CultureInfo")) continue;

                offenders.Add($"{Relative(file)}: {window.Split('\n')[0].Trim()}");
            }
        }

        offenders.Should().BeEmpty(
            "a decimal-comma locale turns 41.9 into 419 or an exception, and neither says why:\n"
            + string.Join("\n", offenders));
    }

    [Fact]
    [Trait("Category", "Portability")]
    public void TheJsonChannelMapReadsTheSameNumbersUnderADecimalCommaLocale()
    {
        // The static check above proves the calls name a culture. This proves the behaviour, by
        // running the real projection under a locale that would break a careless one.
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");

            var map = new JsonChannelMap("locale", new[] { new JsonChannel("temp", "temp") });
            IReadOnlyList<TelemetryPacket> packets = map.Project(
                """{"temp":"41.9"}""", new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc));

            packets.Should().ContainSingle().Which.Value.Should().BeApproximately(41.9, 1e-9,
                "a German locale must not read 41.9 as 419");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
            Thread.CurrentThread.CurrentCulture = original;
        }
    }

    [Fact]
    [Trait("Category", "Portability")]
    public void ChannelNamesSurviveTheTurkishDottedI()
    {
        // Turkish lowercases 'I' to 'ı', not 'i'. A case-insensitive comparison done through the
        // current culture therefore stops matching channel names containing an I -- and this
        // codebase is full of identifiers like SIM:, HIST and VIB. The failure is that a channel
        // silently stops resolving on one operator's machine.
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            Thread.CurrentThread.CurrentCulture = new CultureInfo("tr-TR");

            SimulatedNodeMarker.Apply("MCU_1").Should().Be("SIM:MCU_1");
            SimulatedNodeMarker.IsMarked("SIM:MCU_1").Should().BeTrue();

            // The ordinal comparison the marker uses is culture-independent by construction; this
            // asserts that it stayed that way.
            SimulatedNodeMarker.Apply("SIM:MCU_1").Should().Be("SIM:MCU_1",
                "double-marking under a Turkish locale would split one series into two");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
            Thread.CurrentThread.CurrentCulture = original;
        }
    }

    [Fact]
    [Trait("Category", "Portability")]
    public void NoPortableCodeReachesForAWindowsOnlyApiWithoutSayingSo()
    {
        // Each of these throws PlatformNotSupportedException off Windows. Reaching one behind an
        // OperatingSystem.IsWindows() guard is fine; reaching one unguarded is a crash that cannot
        // happen on the machine that wrote it.
        string[] windowsOnly = { "Microsoft.Win32.Registry", "System.Management", "EventLog(" };

        var offenders = new List<string>();

        foreach (string file in PortableSources())
        {
            string text = File.ReadAllText(file);
            if (text.Contains("OperatingSystem.IsWindows()") || text.Contains("SupportedOSPlatform")) continue;

            offenders.AddRange(windowsOnly
                .Where(text.Contains)
                .Select(api => $"{Relative(file)}: {api} with no platform guard"));
        }

        offenders.Should().BeEmpty(
            "these throw PlatformNotSupportedException off Windows:\n" + string.Join("\n", offenders));
    }

    [Fact]
    [Trait("Category", "Portability")]
    public void EveryProjectReferenceMatchesTheFileNameExactly()
    {
        // Linux file systems are case-sensitive and Windows ones are not, so a reference written as
        // "telemetrydashboard.core.csproj" builds here and fails there. It is the single commonest
        // way a Windows-built solution refuses to build on a Linux CI machine, and it is invisible
        // until you try.
        var reference = new Regex(@"(?:ProjectReference|Compile|Content|Resource|None)\s+Include=""([^""]+)""");
        var offenders = new List<string>();

        foreach (string project in PortableProjects)
        {
            string directory = Path.Combine(SolutionRoot(), project);
            if (!Directory.Exists(directory)) continue;

            foreach (string csproj in Directory.EnumerateFiles(directory, "*.csproj"))
            {
                foreach (System.Text.RegularExpressions.Match match in reference.Matches(File.ReadAllText(csproj)))
                {
                    string include = match.Groups[1].Value;
                    if (include.Contains('*') || include.Contains("$(")) continue;

                    string full = Path.GetFullPath(Path.Combine(directory, include.Replace('\\', Path.DirectorySeparatorChar)));
                    if (!File.Exists(full)) continue;

                    string onDisk = Path.GetFileName(new FileInfo(full).Directory!
                        .GetFiles(Path.GetFileName(full))[0].FullName);

                    if (!string.Equals(onDisk, Path.GetFileName(full), StringComparison.Ordinal))
                    {
                        offenders.Add($"{Path.GetFileName(csproj)} includes '{include}' but the file is '{onDisk}'");
                    }
                }
            }
        }

        offenders.Should().BeEmpty(
            "a case-mismatched reference builds on Windows and fails on Linux:\n" + string.Join("\n", offenders));
    }
}
