using TelemetryDashboard.Core.Analytics;

namespace TelemetryDashboard.Tests;

/// <summary>
/// The channel taxonomy, and mostly the cases where it must refuse to answer.
/// </summary>
/// <remarks>
/// ROADMAP W1 states the rule these are written against: a channel confidently labelled a
/// temperature because its name contains a <c>t</c> and its values sit near 20 is the same defect
/// as a confident zero, and worse, because that label goes on to pick an axis and an alarm band.
/// So most of what follows asserts an absence, and the ones that assert a classification are
/// checking that it came from a derivation rather than from a resemblance.
/// </remarks>
public class ChannelClassifierTests
{
    [Fact]
    [Trait("Category", "Tier1")]
    public void AChannelCalledTWhoseValuesSitNearTwentyIsUnclassifiedRatherThanATemperature()
    {
        // The defect ROADMAP W1 names, written out literally. Both halves of the false evidence are
        // present: a name that starts with the right letter, and a range a room is at.
        ChannelClassification verdict = ChannelClassifier.Classify("t", declaredUnit: null, 19.4, 21.8);

        verdict.Kind.Should().Be(QuantityKind.Unclassified);
        verdict.Confidence.Should().Be(ClassificationConfidence.None);
        verdict.Unit.Should().BeNull("a kind nobody established cannot have picked a unit");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void NoChannelReachesHighConfidenceWithoutADeclaredUnit()
    {
        // The invariant the whole design rests on: a name may propose and values may veto, and
        // neither can promote. Swept rather than sampled, because the failure would arrive as one
        // new branch in the classifier that nobody thought to write a case for.
        string[] names =
        [
            "t", "temp", "temperature", "bus_voltage", "dab.bus_voltage", "field1", "field2",
            "power_ratio", "motorRPM", "vibration", "duty_cycle", "uptime", "psfb.output_current"
        ];

        foreach (string name in names)
        {
            foreach (double value in new[] { -400.0, 0.0, 0.5, 20.0, 48259.9 })
            {
                ChannelClassification verdict = ChannelClassifier.Classify(name, null, value, value);

                verdict.Confidence.Should().NotBe(ClassificationConfidence.High,
                    $"'{name}' at {value} has no declared unit, so nothing about it is a derivation");
                verdict.IsProposal.Should().BeTrue();
            }
        }
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void APositionallyNamedChannelIsUnclassifiedAndSaysWhatWouldClassifyIt()
    {
        // The state a rig is in before anybody writes a rules file. There is no proposal to make
        // here and inventing one would be the defect -- but a dead end and a next step read very
        // differently to the operator who has to act on it.
        ChannelClassification verdict = ChannelClassifier.Classify("field1", declaredUnit: "");

        verdict.Kind.Should().Be(QuantityKind.Unclassified);
        verdict.Evidence.Should().Be(ClassificationEvidence.None);
        verdict.Why.Should().Contain("no unit was declared");
        verdict.Why.Should().Contain("routing rule");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ADeclaredUnitIsADerivationAndIsCarriedAsAFactRatherThanAProposal()
    {
        ChannelClassification verdict = ChannelClassifier.Classify("dab.bus_voltage", "mV", 47_000, 49_000);

        verdict.Kind.Should().Be(QuantityKind.ElectricPotential);
        verdict.Confidence.Should().Be(ClassificationConfidence.High);
        verdict.IsProposal.Should().BeFalse();
        verdict.Evidence.Should().HaveFlag(ClassificationEvidence.DeclaredUnit);

        // The prefix walk is UnitScale's, not a second table that could drift from it.
        verdict.Unit.Should().Be("mV");
        verdict.Subsystem.Should().Be("dab");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ANameThatDisagreesWithTheUnitIsReportedAsDisputedRatherThanQuietlyResolved()
    {
        // The planted mislabel. Both halves are hand-written -- a rules file and a firmware string
        // -- and this host cannot tell which is the mistake, so it must not pick one silently.
        ChannelClassification verdict = ChannelClassifier.Classify("psfb.bus_voltage", "A", 0, 12);

        verdict.HasConflict.Should().BeTrue();
        verdict.IsProposal.Should().BeTrue("a disputed reading is not a fact");
        verdict.Evidence.Should().HaveFlag(ClassificationEvidence.NameDisagreesWithUnit);
        verdict.Why.Should().Contain("cannot tell which is wrong");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ValuesMayTakeAClassificationAwayAndMayNeverGiveOne()
    {
        // Veto that removes a verdict: a name proposed a ratio and the readings run to 100, so the
        // one weak thing holding the proposal up has been taken away.
        ChannelClassification removed = ChannelClassifier.Classify("pump.duty_cycle", null, 0, 100);
        removed.Kind.Should().Be(QuantityKind.Unclassified);
        removed.Evidence.Should().HaveFlag(ClassificationEvidence.ValuesContradictKind);

        // Veto that only lowers one: the device did declare celsius, and readings below absolute
        // zero make that disputed rather than untrue. Dropping the kind here would throw away the
        // one thing anybody actually said.
        ChannelClassification lowered = ChannelClassifier.Classify("intake_temperature", "Cel", -400, 20);
        lowered.Kind.Should().Be(QuantityKind.Temperature);
        lowered.Confidence.Should().Be(ClassificationConfidence.Low);
        lowered.HasConflict.Should().BeTrue();

        // No election: readings this temperature-shaped classify nothing on their own.
        ChannelClassifier.Classify("field7", null, 19.5, 22.0)
            .Kind.Should().Be(QuantityKind.Unclassified);
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void ValuesCannotBeCheckedAgainstAScaleNobodyDeclared()
    {
        // Found while writing the veto above, and kept because it is the honest answer rather than
        // a gap. Minus 400 is impossible in celsius and in kelvin, and is an ordinary winter
        // morning in fahrenheit -- so with no unit there is no floor to be below. The evidence
        // flags say the values were never checked, which is not the same as checked and passed.
        ChannelClassification verdict = ChannelClassifier.Classify("intake_temperature", null, -400, 20);

        verdict.Kind.Should().Be(QuantityKind.Temperature);
        verdict.Confidence.Should().Be(ClassificationConfidence.Low);
        verdict.Evidence.Should().NotHaveFlag(ClassificationEvidence.ObservedValues);
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void AUnitNamingTwoQuantitiesDecidesNeitherUntilSomethingElsePicksOne()
    {
        // UCUM brackets standard gravity as [g] precisely so it cannot collide with the gram. A
        // device writing a bare 'g' has discarded that, and reading it as mass would put a mass
        // axis on an accelerometer.
        ChannelClassification bare = ChannelClassifier.Classify("ch3", "g", 0.1, 3.2);
        bare.Kind.Should().Be(QuantityKind.Unclassified);
        bare.Evidence.Should().HaveFlag(ClassificationEvidence.UnitIsAmbiguous);

        ChannelClassification named = ChannelClassifier.Classify("rig.vibration", "g", 0.1, 3.2);
        named.Kind.Should().Be(QuantityKind.Acceleration);
        named.Unit.Should().Be("[g]");
        named.Confidence.Should().Be(ClassificationConfidence.Medium,
            "an ambiguous unit resolved by a name is still two weak things agreeing");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void ANameProposingTwoKindsProposesNeither()
    {
        ChannelClassification verdict = ChannelClassifier.Classify("power_ratio", declaredUnit: null);

        verdict.Kind.Should().Be(QuantityKind.Unclassified);
        verdict.Evidence.Should().HaveFlag(ClassificationEvidence.NameProposesSeveralKinds);
        verdict.Why.Should().Contain("Prometheus",
            "the shortcut not taken is worth naming, because somebody will propose it again");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void AKnownUnitWithNoKindInThisVocabularyIsUnrecognisedRatherThanTheNearestOne()
    {
        // UnitScale knows the henry. This taxonomy has no inductance kind, and mapping it onto the
        // closest available one would publish a quantity the device never reported.
        UnitVocabulary.Read("mH").Recognised.Should().BeFalse();
        ChannelClassifier.Classify("filter.choke", "mH").Kind.Should().Be(QuantityKind.Unclassified);
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void EveryVerdictCarriesASentenceAnOperatorCanDispute()
    {
        string[][] cases =
        [
            ["field1", ""], ["t", ""], ["dab.bus_voltage", "V"], ["ch3", "g"],
            ["power_ratio", ""], ["psfb.bus_voltage", "A"], ["motor.rpm", "rpm"]
        ];

        foreach (string[] pair in cases)
        {
            ChannelClassifier.Classify(pair[0], pair[1]).Why
                .Should().NotBeNullOrWhiteSpace($"'{pair[0]}' needs a reason a person can argue with");
        }
    }
}
