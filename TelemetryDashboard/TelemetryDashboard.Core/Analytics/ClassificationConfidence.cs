using System;

namespace TelemetryDashboard.Core.Analytics;

/// <summary>How well a channel's quantity kind is actually known.</summary>
/// <remarks>
/// Ordinal on purpose, so a contradiction can lower a verdict without anything having to know which
/// verdict it was lowering. The only level that means "fact" is <see cref="High"/>; everything else
/// is a proposal an operator accepts, and <see cref="ChannelClassification.IsProposal"/> is what a
/// view reads rather than re-deriving the comparison and getting it the wrong way round once.
/// </remarks>
public enum ClassificationConfidence
{
    /// <summary>Nothing was established. Pairs with <see cref="QuantityKind.Unclassified"/>.</summary>
    None = 0,

    /// <summary>
    /// One weak support and nothing corroborating it — a recognised word in the channel name, or a
    /// verdict something else contradicted. A guess, presented as one.
    /// </summary>
    Low,

    /// <summary>
    /// Two things agree but neither is decisive: an ambiguous unit resolved by the name, say.
    /// Still a proposal.
    /// </summary>
    Medium,

    /// <summary>
    /// A declared unit maps to exactly one kind and nothing contradicts it. This is a derivation
    /// rather than an inference — a reading in volts <em>is</em> an electric potential — and it is
    /// the only route to this level. See <see cref="ChannelClassifier"/>.
    /// </summary>
    High
}

/// <summary>What a classification was actually reached from.</summary>
/// <remarks>
/// ROADMAP W1 requires every classification to carry how it was reached, and a sentence alone is
/// not enough: a view has to be able to grey a row, and a rule has to be able to assert that
/// nothing climbed to <see cref="ClassificationConfidence.High"/> without
/// <see cref="DeclaredUnit"/>. Flags rather than a single "method", because the interesting cases
/// are the ones where two sources were consulted and disagreed.
/// <para>
/// The absence of <see cref="ObservedValues"/> is itself information. It says the values were never
/// checked — because no rule exists for that kind — rather than that they were checked and passed,
/// which is the same distinction between silence and health this product is built on.
/// </para>
/// </remarks>
[Flags]
public enum ClassificationEvidence
{
    /// <summary>Nothing was consulted, or nothing that was consulted said anything.</summary>
    None = 0,

    /// <summary>A unit arrived with the channel and it named a kind.</summary>
    DeclaredUnit = 1,

    /// <summary>A word in the channel name is in the vocabulary. Never decisive on its own.</summary>
    ChannelName = 2,

    /// <summary>The observed range was checked against this kind. Says nothing about the outcome.</summary>
    ObservedValues = 4,

    /// <summary>
    /// The name proposes one kind and the unit another. Both are human-authored and this host
    /// cannot tell which is wrong, so it says so rather than choosing.
    /// </summary>
    NameDisagreesWithUnit = 8,

    /// <summary>The observed values are impossible for this kind. See <see cref="ValueRangeCheck"/>.</summary>
    ValuesContradictKind = 16,

    /// <summary>
    /// The declared unit names more than one kind — <c>g</c> is gram or standard gravity, <c>C</c>
    /// is coulomb or celsius — so it cannot decide on its own.
    /// </summary>
    UnitIsAmbiguous = 32,

    /// <summary>The name proposes several kinds at once, so it proposes none.</summary>
    NameProposesSeveralKinds = 64
}
