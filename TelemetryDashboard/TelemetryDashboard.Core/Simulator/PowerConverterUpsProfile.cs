using System.Collections.Generic;

namespace TelemetryDashboard.Core.Simulator;

/// <summary>
/// The bundled UPS example: mains feed, DAB battery converter, PSFB 48 V server rail.
/// </summary>
/// <remarks>
/// This is the setup the first ribbon tab used to be hardcoded to. Nothing was dropped in the move
/// — the same four setpoints and the same four situations — but it now arrives as data under a name
/// that says what it is, so it reads as one worked example rather than as the only system this
/// application knows how to watch.
/// </remarks>
internal static class PowerConverterUpsProfile
{
    internal static MonitoringProfile Instance { get; } = new()
    {
        Id = MonitoringProfileLibrary.PowerConverterId,
        DisplayName = "DAB/PSFB UPS 전력 변환기 (예제)",
        Summary = "상용 전력망 → DAB 배터리 컨버터 → PSFB 48 V 서버 급전 체인 예제입니다.",
        Nodes =
        [
            new ProfileNode
            {
                Id = "COM3",
                Label = "DAB 배터리 컨버터",
                Description = "COM3 에 연결된 양방향 DAB 컨버터 노드입니다."
            },
            new ProfileNode
            {
                Id = "COM4",
                Label = "PSFB 서버 레일",
                Description = "COM4 에 연결된 PSFB 48 V 급전 노드입니다."
            }
        ],
        Channels =
        [
            new ProfileChannel
            {
                Id = SimulatorChannelIds.GridVoltage,
                Label = "계통 전압", Unit = "V",
                Minimum = 0, Maximum = 440, Nominal = 380, Decimals = 0
            },
            new ProfileChannel
            {
                Id = SimulatorChannelIds.DabBusVoltage,
                Label = "DAB 버스 전압", Unit = "V",
                Minimum = 350, Maximum = 450, Nominal = 400, Decimals = 0
            },
            new ProfileChannel
            {
                Id = SimulatorChannelIds.PsfbOutputVoltage,
                Label = "PSFB 출력 전압", Unit = "V",
                Minimum = 38, Maximum = 54, Nominal = 48.05, Decimals = 2
            },
            new ProfileChannel
            {
                Id = SimulatorChannelIds.ServerLoad,
                Label = "서버 부하", Unit = "%",
                Minimum = 10, Maximum = 100, Nominal = 82.4, Decimals = 1
            },
            new ProfileChannel
            {
                Id = SimulatorChannelIds.DabInputCurrent,
                Label = "DAB 입력 전류", Unit = "A",
                Minimum = 0, Maximum = 40, Nominal = 25, Decimals = 2
            },
            new ProfileChannel
            {
                Id = SimulatorChannelIds.PsfbOutputCurrent,
                Label = "PSFB 출력 전류", Unit = "A",
                Minimum = 0, Maximum = 260, Nominal = 190, Decimals = 1
            },

            // The battery branch, hanging off the same DC bus as everything above it. A 200 Ah
            // 48 V lithium bank: 51.2 V nominal, 44 V empty, 58 V on charge.
            new ProfileChannel
            {
                Id = SimulatorChannelIds.UpsBatteryVoltage,
                Label = "UPS 배터리 전압", Unit = "V",
                Minimum = 42, Maximum = 58, Nominal = 51.2, Decimals = 2
            },

            // Signed: positive is charge going into the bank, negative is the bank holding the bus
            // up. One channel carries direction and magnitude, so nothing downstream has to hold a
            // separate "mode" that could disagree with the current.
            //
            // The declared range is wide because the quantity is, and the drift model takes 8 % of
            // the range as its amplitude -- about +/-22 A here. The 24 A nominal is far enough
            // above that for the sign to stay put while the rig is idle, which matters because the
            // two sides of this converter are wandered independently: at a nominal near zero they
            // would disagree about which way the power was going, and the diagram would draw one
            // arrow charging and the other discharging in the same picture.
            new ProfileChannel
            {
                Id = SimulatorChannelIds.UpsBatteryCurrent,
                Label = "UPS 배터리 전류", Unit = "A",
                Minimum = -220, Maximum = 60, Nominal = 24, Decimals = 1
            },

            // The DC-link side of the same converter, positive into the bus. 24 A into a 51.2 V
            // bank is 1.23 kW, which off a 400 V bus is about 3.1 A -- so the idle nominal is
            // -3.1, the converter drawing from the bus to charge. Under the outage setpoint it is
            // +23 A, the 9.2 kW the server rail needs, arriving from the battery instead.
            new ProfileChannel
            {
                Id = SimulatorChannelIds.UpsBusCurrent,
                Label = "UPS 버스측 전류", Unit = "A",
                Minimum = -12, Maximum = 28, Nominal = -3.1, Decimals = 2
            },

            // Declared after the current it accumulates, so each tick's charge uses that tick's
            // current rather than the one before it. 100 / (200 Ah * 3600 s) = 1.3889e-4 %/(A*s):
            // the outage's 180 A empties 1.5 % per minute, so a 92 % bank carries this rig for
            // about an hour and the number visibly moves while an operator watches it.
            new ProfileChannel
            {
                Id = SimulatorChannelIds.UpsStateOfCharge,
                Label = "UPS 배터리 충전율", Unit = "%",
                Minimum = 0, Maximum = 100, Nominal = 92, Decimals = 1,
                Integrates = new ChannelIntegration
                {
                    Source = SimulatorChannelIds.UpsBatteryCurrent,
                    PerSecond = 100.0 / (200.0 * 3600.0)
                }
            }
        ],
        // 400 V x 25 A in, 48 V x 190 A out: a 10 kW battery converter feeding a 9 kW server rail.
        //
        // Efficiency is deliberately NOT declared here, and it is the number this chain is judged
        // by. It would be wrong on this profile specifically: the simulator wanders every channel
        // independently, on purpose, so that it never invents a correlation nobody put there --
        // and efficiency is a claim about the relationship between the two sides of a converter,
        // which is exactly the relationship the simulator refuses to model. Declared here, it
        // measured 116.1% on a live run, correctly computed from inputs that do not constrain each
        // other. Narrowing the current ranges until the quotient looked plausible would have fixed
        // the appearance and not the meaning.
        //
        // On hardware the inputs are correlated by physics, and the declaration belongs on the
        // command line where the operator states it about their own rig:
        //   --computed "psfb.efficiency[%] = 100 * psfb.output_voltage * psfb.output_current
        //                                    / (dab.bus_voltage * dab.input_current)"
        //
        // What is declared below is safe on any inputs, because each name states an operation
        // rather than a physical relationship: a product of two channels is that product whatever
        // the channels are doing.
        // What the machine may safely do, which is not what the sliders above may be set to. The
        // bus slider reaches 450 V and the ceiling here is 420: that gap is how an over-voltage is
        // injected on purpose, and a profile that used one pair of numbers for both would alarm on
        // every deliberate test and on nothing else.
        //
        // Units are stated so a rule cannot be applied to a channel reporting something else. A
        // limit in kV against a bus in volts never fires, and a limit that never fires has no
        // symptom at all -- it looks exactly like a healthy machine.
        Limits =
        [
            "grid.voltage[V] in 320..430",
            "dab.bus_voltage[V] in 370..420",
            "psfb.output_voltage[V] in 45..51",
            "dab.input_current[A] < 36",
            "psfb.output_current[A] < 240",

            // The two the battery branch is judged by. The charge floor is the one an operator acts
            // on -- below it the bank is nearly out and the load has to go somewhere -- and it is
            // reachable in about 50 minutes of simulated outage, or in seconds by commanding the
            // charge down to 21 % and letting the discharge current carry it across.
            "ups.battery_voltage[V] in 44..58",
            "ups.state_of_charge[%] > 20"
        ],
        Computed =
        [
            "dab.p_in[W] = dab.bus_voltage * dab.input_current",
            "psfb.p_out[W] = psfb.output_voltage * psfb.output_current",
            "psfb.conversion_ratio = psfb.output_voltage / dab.bus_voltage",

            // Signed like the current it is built from: positive is power into the bank. Safe on
            // any inputs for the same reason as the two products above -- it states an operation,
            // not a claim about how the two sides of a converter are related.
            //
            // Runtime remaining is deliberately not here, and it is the number a UPS is bought for.
            // It needs the bank's capacity, which is a property of the site rather than of this
            // example, and it is only meaningful while the current is negative: declared as a plain
            // expression it reads as a large negative number of minutes whenever the bank charges.
            // An operator states it about their own bank, where the capacity is known:
            //   --computed "ups.runtime_min[min] = 120 * ups.state_of_charge
            //                                      / (0 - ups.battery_current)"
            // with 120 = 60 min/h * 200 Ah / 100 %. Division by zero yields no value rather than an
            // infinity, so the channel goes quiet at the moment the current crosses zero.
            "ups.p_batt[W] = ups.battery_voltage * ups.battery_current",

            // The bus side of the same converter, positive into the bus. Deliberately a separate
            // channel rather than p_batt drawn twice: the two differ by the conversion loss, and
            // reusing one figure for both sides would be asserting a loss of zero that nobody
            // measured. On this profile the two are wandered independently and will not agree to
            // the watt -- that disagreement is the simulator's nature, and on hardware it is the
            // measurement an engineer wants, because it is the loss.
            "ups.p_bus[W] = dab.bus_voltage * ups.bus_current"
        ],
        // Every scenario below states its effect in Setpoints, because Setpoints is the only thing
        // the headless host acts on. Fault is read by the WPF shell alone -- it feeds a fault model
        // that lives in a class the host never constructs -- so the two scenarios that carried a
        // Fault and no setpoints were complete no-ops there: POST /api/control?cmd=scenario
        // resolved them, looped over zero setpoints and answered "Success". A control that reports
        // success and moves nothing is worse than a missing one, and this was the shape of it that
        // is hardest to catch, because the reply was correct about everything it actually did.
        //
        // The values are chosen to cross the limits declared above, so pressing a button produces
        // the alarm its caption promises: 38 A against a 36 A ceiling, 42 V against a 45 V floor.
        Scenarios =
        [
            new ProfileScenario
            {
                Id = "grid-online",
                Label = "계통 정상",
                Description = "상용 전력망 380 V 급전으로 되돌리고, 배터리를 부동 충전으로 되돌립니다.",
                Fault = nameof(PowerScenario.Normal),
                Setpoints = new Dictionary<string, double>
                {
                    [SimulatorChannelIds.GridVoltage] = 380,
                    [SimulatorChannelIds.DabInputCurrent] = 25,
                    [SimulatorChannelIds.PsfbOutputVoltage] = 48.05,
                    [SimulatorChannelIds.UpsBatteryCurrent] = 24,
                    [SimulatorChannelIds.UpsBusCurrent] = -3.1
                }
            },
            new ProfileScenario
            {
                Id = "grid-outage",
                Label = "정전 (UPS 방전)",
                Description = "계통 전압을 0 V로 떨어뜨리고 배터리를 180 A 방전으로 전환합니다. 충전율이 분당 1.5 %씩 내려갑니다.",
                Fault = nameof(PowerScenario.GridOutage),
                Setpoints = new Dictionary<string, double>
                {
                    [SimulatorChannelIds.GridVoltage] = 0,

                    // The load does not go away because the mains did -- that is the point of a
                    // UPS -- so the bank takes it: 180 A at 51.2 V is 9.2 kW, which is what the
                    // server rail draws at its nominal 48 V x 190 A. The same 9.2 kW arrives at
                    // the 400 V bus as 23 A, and it is that channel the diagram draws flowing up
                    // into the chain -- the one segment whose direction reverses in an outage.
                    [SimulatorChannelIds.UpsBatteryCurrent] = -180,
                    [SimulatorChannelIds.UpsBusCurrent] = 23
                }
            },
            new ProfileScenario
            {
                Id = "dab-overcurrent",
                Label = "DAB 과전류",
                Description = "DAB 입력 전류를 36 A 한계 위인 38 A로 밀어 넣습니다.",
                Fault = nameof(PowerScenario.DabOvercurrent),
                Setpoints = new Dictionary<string, double>
                {
                    [SimulatorChannelIds.DabInputCurrent] = 38
                }
            },
            new ProfileScenario
            {
                Id = "psfb-undervoltage",
                Label = "PSFB 저전압",
                Description = "PSFB 48 V 레일을 45 V 한계 아래인 42 V로 강하시킵니다.",
                Fault = nameof(PowerScenario.PsfbUnderVoltage),
                Setpoints = new Dictionary<string, double>
                {
                    [SimulatorChannelIds.PsfbOutputVoltage] = 42
                }
            }
        ]
    };
}
