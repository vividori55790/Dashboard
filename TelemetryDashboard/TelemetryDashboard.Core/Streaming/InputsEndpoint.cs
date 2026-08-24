using System;
using System.Collections.Generic;
using System.Linq;
using TelemetryDashboard.Core.Ingest;

namespace TelemetryDashboard.Core.Streaming;

/// <summary>
/// <c>/api/inputs</c> — what each port is delivering, grouped by the thing you can unplug.
/// </summary>
/// <remarks>
/// Every other endpoint here is organised by channel, which answers a question only somebody who
/// already knows the channel names can ask. A rig being commissioned is exactly the case where
/// nobody does: the operator has a cable, a device and a suspicion, and needs to see what is
/// arriving before any chart means anything.
/// <para>
/// Separate from <c>/api/status</c> deliberately. Status is polled continuously and has to stay
/// small; this grows with the rig, and a view that asks for it is asking on purpose.
/// </para>
/// </remarks>
public static class InputsEndpoint
{
    /// <summary>The reply, or an explicit statement that nothing is tracking inputs.</summary>
    /// <remarks>
    /// Null inventory answers <c>tracking: false</c> rather than an empty list, for the reason this
    /// project restates everywhere: "no inputs" and "nobody is looking" are different facts, and a
    /// view that renders an empty table for both tells an operator their rig is silent when the
    /// truth is that nothing asked.
    /// </remarks>
    public static object Query(InputInventory? inventory, DateTimeOffset now)
    {
        if (inventory is null)
        {
            return new
            {
                tracking = false,
                reason = "this host is not keeping an input inventory",
                ports = Array.Empty<object>()
            };
        }

        IReadOnlyList<InputChannel> channels = inventory.Channels();

        return new
        {
            tracking = true,
            distinctInputs = inventory.Count,

            // Surfaced rather than kept. A capped list that does not say it is capped reads as the
            // whole rig, which is the coverage failure this product's architecture opens with.
            evicted = inventory.Evictions,

            ports = channels
                .GroupBy(c => c.Port, StringComparer.OrdinalIgnoreCase)
                .Select(group => new
                {
                    port = group.Key,
                    channels = group.Select(c => new
                    {
                        node = c.NodeId,
                        channel = c.Channel,
                        unit = c.Unit,
                        lastValue = c.LastValue,
                        samples = c.Samples,
                        silenceSec = c.Silence(now).TotalSeconds,

                        // Null while a channel has reported once. A cadence nobody could have
                        // measured is not reported as a number, here or anywhere else.
                        meanIntervalSec = c.MeanInterval?.TotalSeconds
                    }).ToArray()
                })
                .ToArray()
        };
    }
}
