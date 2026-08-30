using System;
using System.Collections.Generic;
using System.Linq;
using TelemetryDashboard.Core.Analytics;
using TelemetryDashboard.Core.Ingest;

namespace TelemetryDashboard.Core.Streaming;

/// <summary>
/// The taxonomy half of <c>/api/inputs</c>: what each channel is, and how much of that is known.
/// </summary>
/// <remarks>
/// ROADMAP W1's rule decides the shape of this. A kind may never travel without the confidence that
/// qualifies it, because a consumer reading <c>kind</c> alone would go on to pick an axis and an
/// alarm band from a guess. So <c>proposal</c> is a field rather than something a client computes
/// from a comparison it can get the wrong way round, and <c>why</c> travels with every row —
/// including the unclassified ones, where it says what would classify the channel.
/// <para>
/// Computed on query rather than stored on the inventory. The inventory's job is to record what
/// arrived; deciding what it means is a separate one, and a classification cached at ingest would
/// be answered from the first sample a channel ever sent — before there was a range to check it
/// against, which is the only input here that can change its mind.
/// </para>
/// <para>
/// <b>Accepting a proposal is not built, and the reason is a product decision rather than an
/// omission.</b> ROADMAP W1 asks for proposals an operator accepts in one action; what is here is
/// the half that can be decided on evidence. The other half needs somebody to choose where an
/// accepted proposal is written, and the two answers behave differently: writing it into the
/// routing rules file makes the operator's decision the same kind of artefact as the rest of the
/// rig's configuration, reviewable and diffable, and edits a file they may also hand-maintain;
/// keeping a separate overrides store leaves their file alone and creates a second place a
/// channel's unit can come from, which is how two sources of truth start. Nothing here should pick
/// one on their behalf. Note also that the case the roadmap names — a rig of positional
/// <c>fieldN</c> channels — produces no proposals at all by design: there is nothing to accept for
/// a channel with no unit and no recognisable name, and manufacturing one would be the defect.
/// </para>
/// </remarks>
public static partial class InputsEndpoint
{
    /// <summary>One channel's classification, as it goes on the wire.</summary>
    private static object Describe(InputChannel channel)
    {
        ChannelClassification verdict = ChannelClassifier.Classify(
            channel.Channel, channel.Unit, channel.ObservedMin, channel.ObservedMax);

        return new
        {
            kind = verdict.KindName,

            // Named for its system rather than called "unit", because the row already carries the
            // unit the device declared and these are not the same thing: one is what arrived, the
            // other is what this host thinks it is in a vocabulary it can name. OPC-UA carries a
            // namespaceUri beside its unit id for the same reason.
            ucumUnit = verdict.Unit,
            subsystem = verdict.Subsystem,

            confidence = verdict.ConfidenceName,

            // A field and not an inference. "Below high" is a comparison a client can invert once,
            // and the consequence of inverting it is a guess rendered as a fact.
            proposal = verdict.IsProposal,

            // Separate from proposal, because "weakly supported" and "two sources actively
            // disagree" want different things from an operator.
            disputed = verdict.HasConflict,

            evidence = verdict.EvidenceNames,
            why = verdict.Why
        };
    }

    /// <summary>How much of the rig is identified, and how much of that is only proposed.</summary>
    /// <remarks>
    /// Four counts rather than a percentage. A single "83% classified" figure hides which way the
    /// remainder falls, and the difference between seventeen channels nobody has identified and
    /// seventeen whose identification is disputed is the difference between writing a rules file and
    /// going to look at a device.
    /// </remarks>
    private static object Summarise(IReadOnlyList<InputChannel> channels)
    {
        ChannelClassification[] verdicts = channels
            .Select(c => ChannelClassifier.Classify(c.Channel, c.Unit, c.ObservedMin, c.ObservedMax))
            .ToArray();

        return new
        {
            classified = verdicts.Count(v => !v.IsProposal),
            proposed = verdicts.Count(v => v.IsProposal && v.Kind != QuantityKind.Unclassified),
            unclassified = verdicts.Count(v => v.Kind == QuantityKind.Unclassified),
            disputed = verdicts.Count(v => v.HasConflict),

            // The groups the rig's own names declare. Absent rather than invented: a rig whose
            // channels carry no hierarchy gets an empty list, not one bucket holding everything.
            subsystems = verdicts
                .Select(v => v.Subsystem)
                .OfType<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }
}
