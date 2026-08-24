using TelemetryDashboard.Core.Cluster;

namespace TelemetryDashboard.Tests;

/// <summary>
/// The identity outliving the directory it was installed into.
/// </summary>
/// <remarks>
/// It used to be written beside the executable, which is the one directory an update replaces.
/// Nothing in this product performs an update yet, so the defect had never fired — and the identity
/// is precisely the thing ARCHITECTURE.md §2 says must never quietly change. The first in-place
/// update would have changed it silently: the same rig publishing under a new id, every coverage
/// entry for the old one going to silence, and no error anywhere.
/// </remarks>
public class NodeIdentityStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "td-node-store-" + Guid.NewGuid().ToString("N"));

    private string Install(string name)
    {
        string path = Path.Combine(_root, "installs", name);
        Directory.CreateDirectory(path);
        return path;
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* a temp directory that outlives the run is not a failed test */ }
        GC.SuppressFinalize(this);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void TheSameInstallGetsTheSameIdentityTwice()
    {
        string install = Install("a");

        NodeIdentity first = NodeIdentityStore.LoadOrCreate(install, _root);
        NodeIdentity second = NodeIdentityStore.LoadOrCreate(install, _root);

        second.Id.Should().Be(first.Id);
        first.WasCreated.Should().BeTrue("the first run had nothing to read");
        second.WasCreated.Should().BeFalse("the second read what the first wrote");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void TheIdentityIsNotWrittenInsideTheInstallDirectory()
    {
        // The whole point. Anything under the install is what an update replaces.
        string install = Install("a");
        NodeIdentityStore.LoadOrCreate(install, _root);

        Directory.EnumerateFiles(install, "*", SearchOption.AllDirectories).Should().BeEmpty();
        NodeIdentityStore.PathFor(install, _root).Should().StartWith(_root);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void TwoInstallsOnOneMachineStayTwoNodes()
    {
        // The property the old location had by accident and this one has to keep on purpose: two
        // hosts run from two directories watch two rigs, and one identity between them would
        // interleave their channels into a series that looks like noise.
        NodeIdentity a = NodeIdentityStore.LoadOrCreate(Install("a"), _root);
        NodeIdentity b = NodeIdentityStore.LoadOrCreate(Install("b"), _root);

        b.Id.Should().NotBe(a.Id);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void AnIdentityWrittenBesideTheExecutableIsCarriedForwardUnchanged()
    {
        // An installation that already has an identity must keep it. Changing it here would be the
        // same silent history split this move exists to prevent, arriving on the day of the fix.
        string install = Install("legacy");
        const string existing = "abcdef0123456789abcdef0123456789";
        File.WriteAllText(Path.Combine(install, NodeIdentity.FileName), existing + Environment.NewLine);

        NodeIdentity migrated = NodeIdentityStore.LoadOrCreate(install, _root);

        migrated.Id.Should().Be(existing);
        migrated.WasCreated.Should().BeFalse("it was found, not invented");
        File.Exists(NodeIdentityStore.PathFor(install, _root)).Should().BeTrue("it has to survive the next update");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void OnceMigratedTheOldFileIsNoLongerConsulted()
    {
        string install = Install("legacy");
        string legacyPath = Path.Combine(install, NodeIdentity.FileName);
        File.WriteAllText(legacyPath, "abcdef0123456789abcdef0123456789");

        string migrated = NodeIdentityStore.LoadOrCreate(install, _root).Id;

        // Somebody edits the leftover file, or an old backup restores it. The live identity must
        // not follow it: two files free to disagree is the failure mode of leaving both readable.
        File.WriteAllText(legacyPath, "99999999999999999999999999999999");

        NodeIdentityStore.LoadOrCreate(install, _root).Id.Should().Be(migrated);
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void AStoredValueThatIsNotAnIdIsReplacedRatherThanTrusted()
    {
        // Carrying on with a value that is not a valid identifier puts a broken id into every
        // record this node ever emits, and those records outlive the process that wrote them.
        string install = Install("corrupt");
        string durable = NodeIdentityStore.PathFor(install, _root);
        Directory.CreateDirectory(Path.GetDirectoryName(durable)!);
        File.WriteAllText(durable, "not a valid id -- spaces and punctuation!");

        NodeIdentity replaced = NodeIdentityStore.LoadOrCreate(install, _root);

        NodeIdentity.IsValidId(replaced.Id).Should().BeTrue();
        replaced.WasCreated.Should().BeTrue("a replacement is a new identity and has to say so");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void TheLegacyFileIsReportableSoTheBannerCanMentionIt()
    {
        string install = Install("legacy");
        NodeIdentityStore.HasLegacyFile(install).Should().BeFalse();

        File.WriteAllText(Path.Combine(install, NodeIdentity.FileName), "abcdef0123456789abcdef0123456789");
        NodeIdentityStore.HasLegacyFile(install).Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Tier3")]
    public void ATrailingSeparatorIsTheSameInstall()
    {
        // Two ways of typing one directory must not become two nodes. This is the reason the key is
        // a normalised path rather than the string the caller happened to pass.
        string install = Install("a");

        NodeIdentityStore.PathFor(install + Path.DirectorySeparatorChar, _root)
            .Should().Be(NodeIdentityStore.PathFor(install, _root));
    }
}
