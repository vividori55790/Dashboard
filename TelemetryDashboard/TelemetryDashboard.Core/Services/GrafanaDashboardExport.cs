using System;
using System.Collections.Generic;
using System.Linq;
using TelemetryDashboard.Core.Ingest;

namespace TelemetryDashboard.Core.Services;

/// <summary>
/// A Grafana dashboard built from the channels this host has actually heard from.
/// </summary>
/// <remarks>
/// ROADMAP W3. A scrape endpoint alone does not meet "쉽게 설정할 수 있게": it leaves a human
/// building panels by hand for channels the hub already knows the name, unit and origin of.
/// <para>
/// <b>The rule this file exists to keep.</b> A panel is drawn for a channel that has reported and
/// for no other. An auto-generated dashboard full of empty graphs is how an operator learns to
/// distrust the generator, and — ARCHITECTURE's opening — an empty graph is indistinguishable from
/// a quiet one, which is the failure this whole product is organised against. That is why the
/// source here is <see cref="InputInventory"/>, which only ever holds a channel because a frame
/// carrying it arrived, and deliberately <em>not</em> the declared sets this server also has to
/// hand: <c>Computed</c> lists expressions that may never have produced a value and <c>Limits</c>
/// lists rules for channels that may never have existed. Generating from a declaration is the
/// obvious implementation and it is the wrong one.
/// </para>
/// <para>
/// <b>Three states, three answers.</b> No inventory means nobody is counting; an empty one means
/// somebody is and nothing has arrived; only the third is a rig with channels. The first two do not
/// produce a zero-panel dashboard — an operator importing one sees a blank Grafana page and cannot
/// tell it from a generator that failed — but one carrying a text panel saying which it was. That
/// is /api/inputs' <c>tracking</c> distinction, moved into the artefact so it survives this process.
/// </para>
/// <para>
/// <b>What was read to build this.</b> Field names from Grafana's "Dashboard JSON model" reference;
/// unit identifiers from <c>grafana-data/src/valueFormats/categories.ts</c> in grafana/grafana (see
/// the Units partial); the portability shape — a <c>datasource</c>-typed template variable rather
/// than an <c>__inputs</c> block — from node-exporter-full (grafana.com dashboard 1860), the
/// most-installed Prometheus dashboard there is, whose panels refer to
/// <c>{"type":"prometheus","uid":"${ds_prometheus}"}</c>. <c>__inputs</c> was the alternative and
/// was rejected: file provisioning does not resolve it, so a dashboard written that way imports
/// through the UI and breaks when dropped into a provisioning directory, and this endpoint cannot
/// know which of the two an operator will use.
/// </para>
/// </remarks>
public static partial class GrafanaDashboardExport
{
    /// <summary>Grafana's dashboard schema version, as dashboard 1860 ships it.</summary>
    /// <remarks>
    /// Grafana migrates older schema versions forward on import and cannot migrate a newer one
    /// backwards, so this tracks a version proven to import rather than the newest documented one.
    /// </remarks>
    public const int SchemaVersion = 41;

    /// <summary>Stable across exports, so re-importing replaces rather than duplicates.</summary>
    /// <remarks>
    /// A generated uid would hand an operator a fresh copy on every re-export, which is how a
    /// Grafana ends up with nine of them and no way to tell which is current.
    /// </remarks>
    public const string DashboardUid = "telemetry-hub-auto";

    /// <summary>The whole dashboard, ready to be serialised and imported.</summary>
    public static object Build(InputInventory? inventory, DateTimeOffset now)
    {
        IReadOnlyList<InputChannel> reporting = Reporting(inventory);

        return new
        {
            uid = DashboardUid,
            title = "TelemetryDashboard — 자동 생성",
            description = Describe(inventory, reporting, now),
            tags = new[] { "telemetrydashboard", "auto-generated" },
            timezone = "browser",
            editable = true,
            graphTooltip = 1,
            schemaVersion = SchemaVersion,

            // No id and no version: both belong to a dashboard Grafana has saved, and a generated
            // file is not one. Emitting version 0 was caught by schema validation, which requires
            // 1 or more -- zero claims "edited zero times" about a row that does not exist.
            refresh = "5s",
            time = new { from = "now-15m", to = "now" },
            templating = new { list = new[] { DatasourceVariable() } },
            annotations = new { list = Array.Empty<object>() },
            panels = reporting.Count == 0
                ? new object[] { NothingHasReportedPanel(inventory is not null) }
                : Layout(reporting)
        };
    }

    /// <summary>The channels a panel may be drawn for, and no others.</summary>
    /// <remarks>
    /// The <c>Samples &gt; 0</c> filter is redundant against today's inventory, which only creates
    /// an entry when it observes a reading. It is written anyway because this file's central rule
    /// must be enforced here rather than inherited from a collaborator's current behaviour: an
    /// inventory that later pre-registered declared channels at zero samples would otherwise
    /// silently start filling this dashboard with empty graphs.
    /// </remarks>
    private static IReadOnlyList<InputChannel> Reporting(InputInventory? inventory) =>
        inventory is null
            ? Array.Empty<InputChannel>()
            : inventory.Channels().Where(c => c.Samples > 0).ToArray();

    /// <summary>What replaces a blank dashboard, and which of the two silences it was.</summary>
    /// <remarks>
    /// A Grafana <c>text</c> panel, the type the JSON model reference uses in its own minimal
    /// example. It exists because the alternative — an empty panel list — opens on a page inviting
    /// the operator to add their first visualisation, which is what a broken generator produces too.
    /// </remarks>
    private static object NothingHasReportedPanel(bool tracking) => new
    {
        id = 1,
        type = "text",
        title = "그릴 채널이 없습니다",
        gridPos = new { h = 6, w = 24, x = 0, y = 0 },
        options = new { mode = "markdown", content = NothingHasReportedText(tracking) }
    };

    private static string NothingHasReportedText(bool tracking) => tracking
        ? "이 대시보드를 만들 때, 이 호스트는 입력을 집계하고 있었지만 아직 어떤 채널도 보고하지 "
        + "않았습니다. **채널이 조용한 것이 아니라, 아직 하나도 도착하지 않았습니다.** 장비가 "
        + "데이터를 보내기 시작한 뒤 다시 내려받으십시오.\n\n"
        + "_Generated while the host was tracking inputs and no channel had reported yet._"
        : "이 호스트는 입력 목록을 유지하지 않아, 어떤 채널이 보고했는지 말할 수 없습니다. "
        + "**채널이 없다는 뜻이 아니라, 아무도 세고 있지 않다는 뜻입니다.**\n\n"
        + "_This host keeps no input inventory, so it cannot say which channels reported._";

    private static string Describe(
        InputInventory? inventory, IReadOnlyList<InputChannel> reporting, DateTimeOffset now)
    {
        string when = now.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss") + " UTC";

        if (inventory is null)
        {
            return $"Generated {when}. This host keeps no input inventory, so it cannot say which "
                 + "channels have reported and has drawn no panels rather than guessing.";
        }

        // The omission is stated rather than left to be discovered. A computed channel has no port
        // and so is not in the inventory, which means an operator who declared one and does not
        // find it here would otherwise have to guess whether it was excluded or had failed.
        return $"Generated {when} from {reporting.Count} channel(s) that had reported by then, out "
             + $"of {inventory.Count} tracked. Panels are drawn only for channels this host has "
             + "actually heard from on a port, so a computed channel is not here and a channel "
             + "that starts reporting later needs a re-export. Queries expect the Prometheus "
             + $"exposition at /metrics, series `{MetricName}` labelled by node and channel."
             + (inventory.Evictions > 0
                 ? $" WARNING: {inventory.Evictions} input(s) were dropped by the inventory's "
                 + "cardinality ceiling, so this is not every channel on the rig."
                 : string.Empty);
    }
}
