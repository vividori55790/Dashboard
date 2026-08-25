using System.Net;
using System.Text.Json;
using TelemetryDashboard.Core.Security;
using TelemetryDashboard.Core.Streaming;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Binding wide and demanding a credential cannot be asked for separately.
/// </summary>
/// <remarks>
/// The rule this replaces recorded that nothing in the product could bind beyond loopback, and
/// said the moment somebody wired it would be the moment somebody had to decide about
/// authentication. That decision is made: <c>--listen network</c> exists and requires
/// <c>--credential</c>.
/// <para>
/// The parser refuses the pair too, earlier and with a better message, but a check living only in
/// an argument parser protects only the callers that go through one. This is the check at the
/// socket, which is why it is tested here against the server object rather than through the host.
/// The desktop shell, a test, and whatever calls this next all get it.
/// </para>
/// <para>
/// What none of this supplies is confidentiality. Basic is base64 and the link is plain HTTP, so
/// the password crosses the segment readable; <c>IsLinkEncrypted</c> exists to say so from the
/// binding rather than from a document, and <see cref="TheStatusPayloadSaysWhoCanReachItAndWhatProtectsThem"/>
/// pins that an operator can look it up.
/// </para>
/// </remarks>
public class ConsoleBindingTests
{
    private static ConsoleAccessGate SomeGate() =>
        new(PasswordCredential.Create("bench-lan-password"));

    [Fact]
    [Trait("Category", "Tier1")]
    public void BindingEveryInterfaceWithNoCredentialIsRefused()
    {
        // No port is bound and none is needed: the refusal has to come before the prefixes are
        // touched, or a rejected configuration still opens the listener for the instant it takes
        // to throw.
        var server = new TelemetryStreamingServer(port: 0, acceptRemoteConnections: true);

        Action start = () => server.Start(string.Empty);

        start.Should().Throw<InvalidOperationException>()
            .WithMessage("*no credential*",
                "the operator has to be told which of the two halves is missing")
            .And.Message.Should().Contain("WebSocket",
                "and why it matters here rather than in general -- this endpoint takes commands");

        server.IsRunning.Should().BeFalse("nothing may have been bound on the way to refusing");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public async Task BindingEveryInterfaceWithACredentialIsNotRefusedByThisProduct()
    {
        // The complement, and the one that keeps the test above from passing for the wrong reason:
        // if Start refused every wide binding, the rule would look enforced while the feature was
        // simply absent.
        var server = new TelemetryStreamingServer(port: 18131, acceptRemoteConnections: true)
        {
            Access = SomeGate()
        };

        await using (server)
        {
            Exception? thrown = Record.Exception(() => server.Start(string.Empty));

            if (thrown is not null)
            {
                // Windows reserves wildcard prefixes, so an unelevated run cannot bind one and
                // CI's Linux and macOS jobs can. Both outcomes prove what this test is about --
                // the product's own check passed and the OS got as far as the socket. What is
                // not allowed is the refusal above, which would mean the gate went unnoticed.
                thrown.Should().BeOfType<HttpListenerException>(
                    "the only permitted failure here is the operating system declining the "
                    + "prefix; a refusal from this product would mean the credential was not seen");
            }

            server.BoundPrefixes.Should().ContainSingle()
                .Which.Should().Contain("+:18131",
                    "a wide binding must actually ask for every interface, not quietly stay on "
                    + "loopback while reporting that it did not");
        }
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public async Task LoopbackWithNoCredentialStaysExactlyWhatItWas()
    {
        // The default carries every existing run. Coupling the credential to the wide binding must
        // not have coupled it to binding at all.
        await using var server = new TelemetryStreamingServer(port: 18132);

        server.Start(string.Empty);

        server.IsRunning.Should().BeTrue();
        server.IsNetworkReachable.Should().BeFalse();
        server.BoundPrefixes.Should().HaveCount(2).And
            .OnlyContain(prefix => prefix.Contains("localhost") || prefix.Contains("127.0.0.1"));
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void NothingThisProductBindsIsEncrypted()
    {
        // Read off the scheme rather than asserted, so this becomes a real measurement the day a
        // https prefix exists. Until then it answers false, which is the fact an operator needs
        // before deciding a segment is safe enough for --listen network.
        new TelemetryStreamingServer(port: 0).IsLinkEncrypted.Should().BeFalse();
        new TelemetryStreamingServer(port: 0, acceptRemoteConnections: true)
            .IsLinkEncrypted.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public async Task TheStatusPayloadSaysWhoCanReachItAndWhatProtectsThem()
    {
        await using var server = new TelemetryStreamingServer(port: 18133) { Access = SomeGate() };
        server.Start(string.Empty);

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var request = new HttpRequestMessage(HttpMethod.Get, $"http://localhost:18133/api/status");
        request.Headers.Add("Authorization",
            "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("ignored:bench-lan-password")));

        using HttpResponseMessage response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement reach = document.RootElement.GetProperty("reachability");

        reach.GetProperty("scope").GetString().Should().Be("loopback");
        reach.GetProperty("authenticated").GetBoolean().Should().BeTrue();
        reach.GetProperty("encrypted").GetBoolean().Should().BeFalse(
            "an operator reading 'authenticated' without this beside it would reasonably assume "
            + "the credential is private on the wire, and it is not");
        reach.GetProperty("prefixes").EnumerateArray().Should().NotBeEmpty();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void TheBannerAndTheStatusEndpointReadOneEndpointList()
    {
        // They were two lists for a while: the banner fetched /api/status over HTTP, and when a
        // credential made that request 401 the fallback was a hardcoded copy. A second copy drifts
        // silently, so there is one, and it is this.
        TelemetryStreamingServer.AdvertisedEndpoints.Should()
            .Contain(["/ws", "/stream", "/api/status", "/api/inputs", "/api/control"])
            .And.OnlyHaveUniqueItems()
            .And.OnlyContain(path => path.StartsWith('/'));
    }
}
