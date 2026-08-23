using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TelemetryDashboard.Core.Streaming;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Keeps the console's commissioning panel honest about the one thing it can change.
/// </summary>
/// <remarks>
/// <c>/api/control</c> is the only write path this product offers a browser, and it had no caller
/// anywhere. What that cost is commissioning: an engineer has to prove the alarm fires before
/// trusting it, and with no way to put a channel at a chosen value the only proof available was
/// over-volting real hardware.
/// <para>
/// Driven live in a browser against a running host: setting psfb.output_voltage to 40 V reported
/// "40.0 V 적용됨" and the safety-band panel above it went to "위반 1" with
/// "41.31 is below the 45 floor". Asking for 999 reported "999.0 요청 → 54.0 적용". A 9 Hz signal
/// on a 10 Hz source was refused with the host's Nyquist explanation. Against a host reading a
/// real device every control was disabled and the host's refusal shown word for word.
/// </para>
/// </remarks>
public partial class ArchitectureRuleTests
{
    [Fact]
    [Trait("Category", "Architecture")]
    public void TheConsoleChangesASetpointWithPostAndNeverWithGet()
    {
        // The route answers a GET with a description on purpose, and its own comment says why: a
        // setpoint that moved on a GET could be moved by a link somebody shared, by a browser
        // prefetching it, or by the back button. The first version of this panel used GET and the
        // host answered with a description instead of applying anything -- the refusal working.
        string page = ConsolePage();

        var offenders = new List<string>();
        foreach (System.Text.RegularExpressions.Match call in Regex.Matches(page, @"fetch\('/api/control\?[^)]*\)"))
        {
            if (!call.Value.Contains("method: 'POST'", StringComparison.Ordinal))
            {
                offenders.Add(call.Value);
            }
        }

        offenders.Should().BeEmpty(
            "a command sent by GET is a command a link or a prefetch can send");
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void EveryCommandTheConsoleSendsIsOneTheEndpointAccepts()
    {
        // A command the endpoint does not know is refused with a sentence naming the ones it does,
        // which the panel then displays -- so this fails visibly rather than silently. It is
        // pinned anyway because the refusal arrives only when somebody presses the button, and on
        // a commissioning run that is the moment with the least patience for it.
        string page = ConsolePage();

        string[] accepted = ControlEndpoint.Commands
            .Select(c => c.Split('&')[0])
            .ToArray();

        string[] sent = Regex.Matches(page, @"'cmd=([a-z-]+)")
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToArray();

        sent.Should().NotBeEmpty("the panel has to send something");
        sent.Should().BeSubsetOf(accepted);
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void ClampingIsShownRatherThanFoldedIntoSuccess()
    {
        // The endpoint's own remark: a caller who asks for 999 V, gets 450 and is told "Success"
        // will believe the bus is at 999 -- and on a commissioning run that belief is the
        // difference between "the alarm did not fire" and "the alarm was never given the chance".
        string page = ConsolePage();

        page.Should().Contain("d.Clamped",
            "the panel has to branch on it rather than print the applied value as if it were asked for");
        page.Should().Contain("d.Requested",
            "and it has to show what was asked for beside what was applied");
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void AHostThatMayNotBeCommandedDisablesThePanelAndSaysWhy()
    {
        // A disabled button with no explanation reads as a broken page. The host has a sentence
        // for this and it names the two flags that would make control available, so it is shown
        // as it is rather than replaced with "unavailable".
        string page = ConsolePage();

        page.Should().Contain("controlDisable(true)");
        page.Should().MatchRegex(@"controlDisable\(true\);\s*\n\s*stat\.textContent = d\.Reason",
            "the host's own words, not ours");
    }
}
