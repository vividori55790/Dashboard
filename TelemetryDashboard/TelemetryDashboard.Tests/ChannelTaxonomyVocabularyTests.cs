using System.Collections.Generic;
using System.Linq;
using TelemetryDashboard.Core.Analytics;

namespace TelemetryDashboard.Tests;

/// <summary>
/// The three pieces the classifier composes: the unit table, the name words, the subsystem prefix.
/// </summary>
/// <remarks>
/// Separated from <see cref="ChannelClassifierTests"/> because these are about the vocabulary being
/// the industry's rather than one somebody invented here, and a failure in one of them is a
/// different investigation from a failure in how the three are weighed against each other.
/// </remarks>
public class ChannelTaxonomyVocabularyTests
{
    [Fact]
    [Trait("Category", "Tier2")]
    public void APrefixedUnitIsTheSameQuantityWithoutASecondPrefixTable()
    {
        // UnitScale already refuses to read 'min' as milli-inches, and borrowing it is what keeps
        // that refusal from having to be written twice and drifting once.
        UnitVocabulary.Read("mV").Kind.Should().Be(QuantityKind.ElectricPotential);
        UnitVocabulary.Read("kW").Kind.Should().Be(QuantityKind.Power);
        UnitVocabulary.Read("kWh").Kind.Should().Be(QuantityKind.Energy);
        UnitVocabulary.Read("mbar").Kind.Should().Be(QuantityKind.Pressure);
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void TheSpellingsFirmwareEmitsMapOntoTheSpellingUcumUses()
    {
        // The canonical code is published beside the declared text rather than instead of it, so
        // this mapping is auditable rather than something an operator has to trust.
        foreach (string spelling in new[] { "°C", "degC", "celsius", "Cel" })
        {
            UnitReading reading = UnitVocabulary.Read(spelling);
            reading.Kind.Should().Be(QuantityKind.Temperature);
            reading.Ucum.Should().Be("Cel", $"'{spelling}' is UCUM's Cel");
        }

        UnitVocabulary.Read("RPM").Ucum.Should().Be("/min", "UCUM has no rpm symbol; it is per minute");
        UnitVocabulary.Read("%").Kind.Should().Be(QuantityKind.Ratio);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void NoWordShorterThanThreeLettersCanEverMatch()
    {
        // The single-letter case is the one the governing rule is written about. Asserted over the
        // whole alphabet rather than over 't', because the next classifier to grow a shortcut will
        // grow it for some other letter.
        foreach (char c in "abcdefghijklmnopqrstuvwxyz")
        {
            ChannelNameHints.Read(c.ToString()).Should().BeEmpty();
            ChannelNameHints.Read($"{c}1").Should().BeEmpty();
        }

        ChannelNameHints.MinimumWordLength.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void MatchingIsAgainstWholeWordsRatherThanSubstrings()
    {
        // 'current_state' is not an electric current, and it is the kind of name a status channel
        // actually has. A substring matcher would classify it and then pick an ampere axis for it.
        ChannelNameHints.Read("current_state").Select(h => h.Kind)
            .Should().Contain(QuantityKind.ElectricCurrent,
                "'current' is a whole word here, so this one is a genuine collision the unit resolves");

        ChannelNameHints.Read("concurrent_jobs").Should().BeEmpty(
            "'concurrent' contains 'current' and is not one");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void AnAcronymSurvivesTokenisationInsteadOfBecomingThreeDiscardedLetters()
    {
        // The bug this was written after: breaking on every uppercase letter splits RPM into R, P
        // and M, the minimum length then drops all three, and a name saying exactly what the
        // channel is reads as saying nothing.
        ChannelNameHints.Read("motorRPM").Select(h => h.Kind)
            .Should().Equal(QuantityKind.RotationalFrequency);

        ChannelNameHints.Read("busVoltage").Select(h => h.Kind)
            .Should().Equal(QuantityKind.ElectricPotential);
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void ASubsystemIsReadFromADeclaredHierarchyAndNeverInventedFromAWordSeparator()
    {
        SubsystemName.From("dab.bus_voltage").Should().Be("dab");
        SubsystemName.From("Inputs/0").Should().Be("Inputs", "Sparkplug B names folders with a slash");

        // The decision worth arguing with, asserted so that changing it is deliberate: an
        // underscore is how a name spells a space, and splitting on it would make 'output' a
        // subsystem of every rig that writes output_voltage.
        SubsystemName.From("output_voltage").Should().BeNull();

        SubsystemName.From("field1").Should().BeNull();
        SubsystemName.From("1.temperature").Should().BeNull("an index is not a group");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void AKindAndItsConfidenceAreNeverPresentWithoutEachOther()
    {
        // Either half alone is a lie: a kind at no confidence renders as a kind, and a confidence
        // with no kind is a claim about nothing.
        var corpus = new List<ChannelClassification>();
        foreach (string name in new[] { "field1", "t", "dab.bus_voltage", "ch3", "power_ratio", "motor.rpm" })
        {
            foreach (string unit in new[] { "", "V", "A", "g", "Cel", "%", "mH", "rpm" })
            {
                corpus.Add(ChannelClassifier.Classify(name, unit, 0, 100));
            }
        }

        foreach (ChannelClassification verdict in corpus)
        {
            (verdict.Kind == QuantityKind.Unclassified)
                .Should().Be(verdict.Confidence == ClassificationConfidence.None,
                    $"'{verdict.KindName}' at '{verdict.ConfidenceName}' pairs a kind with no "
                    + "confidence or a confidence with no kind");
        }
    }
}
