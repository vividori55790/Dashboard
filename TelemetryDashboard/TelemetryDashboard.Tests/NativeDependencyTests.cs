using System.Runtime.InteropServices;
using TelemetryDashboard.Host.Startup;
using TelemetryDashboard.Infrastructure.Updater;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Finding the native libraries an optional feature needs, before the feature is promised.
/// </summary>
/// <remarks>
/// Measured on a build with <c>runtimes/win-x64/native/e_sqlite3.dll</c> deleted — which is what a
/// trimmed publish or a half-extracted portable package leaves behind. The host printed its whole
/// start-up banner, bound its port, advertised all thirteen endpoints, and then died with an
/// unhandled <c>TypeInitializationException</c> out of <c>SqliteConnection</c>. Not a degraded
/// archive: a dead process, after every sign of a healthy start.
/// <para>
/// After this change the same build exits 64 with one sentence and no stack trace, and still starts
/// normally without <c>--archive</c>. Both were run.
/// </para>
/// </remarks>
public class NativeDependencyTests
{
    [Fact]
    [Trait("Category", "Tier1")]
    public void ALibraryUnderRuntimesIsFoundWhereDotnetActuallyPutsIt()
    {
        // The correction that matters. The search used to cover only "beside the executable" and
        // PATH, which is right for a self-contained single-file publish and wrong for every
        // framework-dependent build -- where natives sit under runtimes/<rid>/native/ and nothing
        // copies them up. So the check answered "missing" for a perfectly working install, and a
        // start-up check that cries wolf is worse than no check at all.
        string? located = NativeLibraryProbe.Locate(NativeDependencyCheck.SqliteLibrary);

        located.Should().NotBeNull(
            "this test assembly's own output carries runtimes/<rid>/native/, and SQLite works here");
        File.Exists(located!).Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void TheCheckerAgreesWithTheProbeAboutALibraryThatIsPresent()
    {
        // VerifyNativeDll returned false on this very build before the search was corrected, which
        // is the false alarm being fixed.
        new PortablePackageChecker()
            .VerifyNativeDll(NativeLibraryProbe.PlatformFileName(NativeDependencyCheck.SqliteLibrary))
            .Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void TheArchiveReportsItselfAvailableOnAMachineThatCanRunIt()
    {
        NativeDependencyCheck.ArchiveUnavailable().Should().BeNull(
            "SQLite loads here, so --archive must not be refused");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ALibraryNobodyShippedIsNotFoundAnywhere()
    {
        NativeLibraryProbe.Locate("no-such-native-library-9f3a").Should().BeNull();
        new PortablePackageChecker().VerifyNativeDll("no-such-native-library-9f3a.dll").Should().BeFalse();
    }

    [Theory]
    [Trait("Category", "Tier2")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyNameIsAnAnswerRatherThanAnException(string? name)
    {
        NativeLibraryProbe.Locate(name!).Should().BeNull();
        new PortablePackageChecker().VerifyNativeDll(name!).Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void TheFileNameFollowsThePlatformRatherThanTheCaller()
    {
        // The same dependency is e_sqlite3.dll, libe_sqlite3.so and libe_sqlite3.dylib depending on
        // where the host runs. A caller made to spell that out gets it right on the machine it was
        // written on and wrong on the two it was not.
        string name = NativeLibraryProbe.PlatformFileName("e_sqlite3");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) name.Should().Be("e_sqlite3.dll");
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) name.Should().Be("libe_sqlite3.dylib");
        else name.Should().Be("libe_sqlite3.so");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void TheCheckerAcceptsEitherSpellingOfTheSameLibrary()
    {
        // VerifyNativeDll takes a file name and Locate takes a bare one; a caller holding either
        // should get the same answer rather than having to know which un-decorating this does.
        var checker = new PortablePackageChecker();

        checker.VerifyNativeDll("e_sqlite3.dll").Should().Be(checker.VerifyNativeDll("libe_sqlite3.so"),
            "both name the one library this build either has or has not");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void TheRefusalNamesTheFileWhereItLookedAndWhatToDoNext()
    {
        // The sentence a build really was refused with: exit 64, this text, and zero lines of stack
        // trace where there used to be a type initializer failure through four layers of provider.
        // A refusal that named only the missing file would send somebody to a search engine.
        string refusal = NativeDependencyCheck.Refusal(NativeDependencyCheck.SqliteLibrary);

        refusal.Should().Contain(NativeLibraryProbe.PlatformFileName("e_sqlite3"), "which file");
        refusal.Should().Contain("runtimes/", "where it looked");
        refusal.Should().Contain("without --archive", "what to do instead");
    }
}
