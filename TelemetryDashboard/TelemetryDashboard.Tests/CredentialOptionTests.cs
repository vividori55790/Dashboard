using TelemetryDashboard.Core.Security;
using TelemetryDashboard.Host.Configuration;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Refusing to serve openly when a credential was asked for.
/// </summary>
/// <remarks>
/// <c>CredentialFile.Load</c> answers null for a file it cannot read, which is the right answer for
/// the screen lock — it lets an operator enrol a new password instead of being locked out of their
/// own machine. Here the identical answer would mean serving with no credential at all, so the same
/// null has to be read the opposite way. The failure mode of a lock is that it opens.
/// </remarks>
public class CredentialOptionTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "td-cred-" + Guid.NewGuid().ToString("N"));

    public CredentialOptionTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private string WriteCredential(string password)
    {
        string path = Path.Combine(_directory, "console.cred");
        CredentialFile.Save(path, PasswordCredential.Create(password)).Should().BeNull();
        return path;
    }

    private static HostOptions Parse(params string[] args) =>
        CommandLineParser.Parse(args, new HostOptions());

    [Fact]
    [Trait("Category", "Tier1")]
    public void AUsableCredentialFileIsAccepted()
    {
        HostOptions options = Parse("--simulate", "--credential", WriteCredential("plant-floor-42"));

        options.Error.Should().BeNull();
        options.CredentialPath.Should().NotBeNull();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void AMissingCredentialFileStopsTheRunRatherThanServingOpenly()
    {
        HostOptions options = Parse(
            "--simulate", "--credential", Path.Combine(_directory, "not-here.cred"));

        options.Error.Should().NotBeNull().And.Subject.As<string>()
            .Should().Contain("does not exist");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void AMalformedCredentialFileStopsTheRunToo()
    {
        // The case the screen lock deliberately tolerates and this must not: a truncated or
        // hand-edited file reads as "no password configured", and defaulting to that here would
        // open the console of an operator who explicitly asked for it to be closed.
        string path = Path.Combine(_directory, "broken.cred");
        File.WriteAllText(path, "v1$210000$not-base64$also-not-base64");

        Parse("--simulate", "--credential", path).Error
            .Should().NotBeNull().And.Subject.As<string>()
            .Should().Contain("could not be read as one");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void AnEmptyCredentialFileIsNotACredential()
    {
        string path = Path.Combine(_directory, "empty.cred");
        File.WriteAllText(path, string.Empty);

        Parse("--simulate", "--credential", path).Error.Should().NotBeNull();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void WithoutTheFlagTheConsoleKeepsTodaysBehaviour()
    {
        // The default has to stay what it is. A loopback console is reachable only by somebody
        // already at the machine, and turning this on for everyone would break every existing run
        // to protect against a threat that is already inside the room.
        HostOptions options = Parse("--simulate");

        options.Error.Should().BeNull();
        options.CredentialPath.Should().BeNull();
    }
}
