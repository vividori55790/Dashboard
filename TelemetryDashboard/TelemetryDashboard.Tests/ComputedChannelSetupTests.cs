using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using TelemetryDashboard.Core.Analytics;
using TelemetryDashboard.Core.Simulator;
using TelemetryDashboard.Host.Configuration;
using TelemetryDashboard.Host.Startup;
using Xunit;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Which derived channels a run serves, from a profile and a command line that both have opinions.
/// </summary>
public class ComputedChannelSetupTests
{
    private static MonitoringProfile ProfileDeclaring(params string[] computed) => new()
    {
        Id = "rig",
        DisplayName = "rig",
        Channels = [new ProfileChannel { Id = "a.v", Label = "v" }, new ProfileChannel { Id = "a.i", Label = "i" }],
        Computed = computed
    };

    private static HostOptions OptionsDeclaring(params string[] computed) => new() { Computed = computed };

    [Fact]
    public void AProfileWithNoDeclarationsAndNoFlagsServesNothing()
    {
        ComputedChannelSetup.Result resolved =
            ComputedChannelSetup.Resolve(new HostOptions(), profile: null);

        resolved.Channels.Should().BeEmpty();
        resolved.Warnings.Should().BeEmpty();
        ComputedChannelSetup.BannerLines(resolved).Should().BeEmpty(
            "a host with nothing to say about computed channels says nothing");
    }

    [Fact]
    public void ProfileAndCommandLineAreBothServed()
    {
        ComputedChannelSetup.Result resolved = ComputedChannelSetup.Resolve(
            OptionsDeclaring("b = a.v * 3"),
            ProfileDeclaring("p[W] = a.v * a.i"));

        resolved.Channels.Select(c => c.Id).Should().Equal("p", "b");
    }

    [Fact]
    public void ACommandLineDeclarationOverridesTheProfileButKeepsItsPlace()
    {
        // Position comes from where the id first appeared, so overriding one entry does not
        // reshuffle the list an operator has learned to read.
        ComputedChannelSetup.Result resolved = ComputedChannelSetup.Resolve(
            OptionsDeclaring("p[W] = a.v * 999"),
            ProfileDeclaring("p[W] = a.v * a.i", "q = a.v / a.i"));

        resolved.Channels.Select(c => c.Id).Should().Equal("p", "q");
        resolved.Channels.First().Expression.Should().Be("a.v * 999");
    }

    [Fact]
    public void ADeclarationThatDoesNotParseIsSkippedAndReported()
    {
        ComputedChannelSetup.Result resolved = ComputedChannelSetup.Resolve(
            OptionsDeclaring("broken = power(a.v, 2)"),
            ProfileDeclaring("p[W] = a.v * a.i"));

        resolved.Channels.Select(c => c.Id).Should().Equal("p");
        resolved.Warnings.Should().ContainSingle().Which.Should().Contain("Unknown function 'power'");
        ComputedChannelSetup.BannerLines(resolved).Should().Contain(l => l.Contains("power"),
            "a skipped declaration that is not printed is a channel the operator thinks is running");
    }

    [Fact]
    public void TheBannerNamesEveryChannelAndItsExpression()
    {
        ComputedChannelSetup.Result resolved =
            ComputedChannelSetup.Resolve(new HostOptions(), ProfileDeclaring("p[W] = a.v * a.i"));

        string banner = string.Join("\n", ComputedChannelSetup.BannerLines(resolved));

        banner.Should().Contain("/api/computed").And.Contain("p [W] = a.v * a.i");
    }

    [Fact]
    public void AProfileDeclarationOverAChannelTheProfileDoesNotHaveIsRejectedWhenTheProfileIsRead()
    {
        // Caught at load rather than at the first request, because the endpoint's answer for a
        // misspelled input -- "that channel has reported nothing" -- is exactly what it says about
        // a sensor that has genuinely gone quiet, and there is no later moment anyone finds out.
        var problems = new List<string>();

        MonitoringProfile? read = ReadProfile(
            channels: ["a.v", "a.i"],
            computed: ["p[W] = a.v * a.typo"],
            problems);

        read.Should().NotBeNull();
        read!.Computed.Should().BeEmpty();
        problems.Should().ContainSingle().Which.Should().Contain("a.typo");
    }

    [Fact]
    public void AProfileDeclarationOverChannelsItDoesHaveIsKept()
    {
        var problems = new List<string>();

        MonitoringProfile? read = ReadProfile(
            channels: ["a.v", "a.i"],
            computed: ["p[W] = a.v * a.i"],
            problems);

        read!.Computed.Should().Equal("p[W] = a.v * a.i");

        // The reader reports what it loaded whether or not anything was wrong, so the assertion
        // is that nothing was said about a computed channel -- not that nothing was said at all.
        problems.Should().NotContain(p => p.Contains("계산 채널", StringComparison.Ordinal));
    }

    /// <summary>Round-trips a profile through the JSON reader, which is where validation lives.</summary>
    private static MonitoringProfile? ReadProfile(string[] channels, string[] computed, List<string> problems)
    {
        string json = System.Text.Json.JsonSerializer.Serialize(new
        {
            profiles = new[]
            {
                new
                {
                    id = "rig",
                    displayName = "rig",
                    channels = channels.Select(c => new { id = c, label = c, minimum = 0, maximum = 10 }).ToArray(),
                    computed
                }
            }
        });

        string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tdcomp_" + Guid.NewGuid().ToString("N")[..8]);
        System.IO.Directory.CreateDirectory(dir);
        try
        {
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "profiles.json"), json);
            MonitoringProfileSet set = MonitoringProfileStore.Load(dir);

            if (set.Message is { Length: > 0 }) problems.Add(set.Message);
            return set.Profiles.FirstOrDefault(p => p.Id == "rig");
        }
        finally
        {
            System.IO.Directory.Delete(dir, recursive: true);
        }
    }
}
