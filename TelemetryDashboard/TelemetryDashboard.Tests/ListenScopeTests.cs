using TelemetryDashboard.Core.Security;
using TelemetryDashboard.Host.Configuration;

namespace TelemetryDashboard.Tests;

/// <summary>
/// <c>--listen network</c>, and the credential it cannot be asked for without.
/// </summary>
/// <remarks>
/// The socket refuses the same pair, so this is not the only thing standing between an operator
/// and an open console. It is the one that refuses before anything binds, with a message naming
/// the command that enrols the missing credential -- a host that starts, opens a port and then
/// throws has already been reachable, however briefly.
/// </remarks>
public class ListenScopeTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "td-listen-" + Guid.NewGuid().ToString("N"));

    public ListenScopeTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private string ACredential()
    {
        string path = Path.Combine(_directory, "console.cred");
        CredentialFile.Save(path, PasswordCredential.Create("bench-lan-password")).Should().BeNull();
        return path;
    }

    private static HostOptions Parse(params string[] args) =>
        CommandLineParser.Parse(args, new HostOptions());

    [Fact]
    [Trait("Category", "Tier1")]
    public void BindingWideWithoutACredentialIsRefusedBeforeAnythingBinds()
    {
        HostOptions options = Parse("--simulate", "--listen", "network");

        options.Error.Should().NotBeNull().And.Subject.As<string>()
            .Should().Contain("--credential").And.Contain("telemetry-host credential",
                "an operator told what is missing and not how to produce it will reach for the "
                + "flag that makes the message go away, and that flag is --listen loopback");
        options.ListenOnAllInterfaces.Should().BeFalse();
    }

    [Theory]
    [Trait("Category", "Tier1")]
    [InlineData(true)]
    [InlineData(false)]
    public void TheOrderTheTwoFlagsAreWrittenInDoesNotDecideIt(bool credentialFirst)
    {
        // The check is deliberately at the end of parsing rather than beside the flag. Inline it
        // would accept one of these two spellings of the same request and refuse the other, which
        // is the kind of rule an operator learns by hitting it.
        string cred = ACredential();

        HostOptions options = credentialFirst
            ? Parse("--simulate", "--credential", cred, "--listen", "network")
            : Parse("--simulate", "--listen", "network", "--credential", cred);

        options.Error.Should().BeNull();
        options.ListenOnAllInterfaces.Should().BeTrue();
        options.CredentialPath.Should().NotBeNull();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void LoopbackCanBeAskedForByNameRatherThanByOmission()
    {
        // Worth a name: in a script, the absence of a flag and a deliberate choice of the safe
        // value look identical, and only one of them survives somebody editing the file later.
        HostOptions options = Parse("--simulate", "--listen", "loopback");

        options.Error.Should().BeNull();
        options.ListenOnAllInterfaces.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void AnUnrecognisedScopeNamesTheTwoThatExist()
    {
        HostOptions options = Parse("--simulate", "--listen", "0.0.0.0");

        options.Error.Should().NotBeNull().And.Subject.As<string>()
            .Should().Contain("loopback").And.Contain("network");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void TheDefaultIsStillLoopback()
    {
        Parse("--simulate").ListenOnAllInterfaces.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void ACredentialOnItsOwnDoesNotOpenAnything()
    {
        // The two are coupled in one direction only. Locking a loopback console is a reasonable
        // thing to want on a shared machine, and it must not be read as a request to publish it.
        HostOptions options = Parse("--simulate", "--credential", ACredential());

        options.Error.Should().BeNull();
        options.ListenOnAllInterfaces.Should().BeFalse();
    }
}
