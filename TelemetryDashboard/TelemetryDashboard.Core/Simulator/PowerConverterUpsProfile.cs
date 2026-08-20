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
            }
        ],
        Scenarios =
        [
            new ProfileScenario
            {
                Id = "grid-online",
                Label = "계통 정상",
                Description = "상용 전력망 380 V 급전으로 되돌리고 주입된 고장을 해제합니다.",
                Fault = nameof(PowerScenario.Normal),
                Setpoints = new Dictionary<string, double>
                {
                    [SimulatorChannelIds.GridVoltage] = 380
                }
            },
            new ProfileScenario
            {
                Id = "grid-outage",
                Label = "정전 (UPS 방전)",
                Description = "계통 전압을 0 V로 떨어뜨리고 DAB를 배터리 방전 모드로 전환합니다.",
                Fault = nameof(PowerScenario.GridOutage),
                Setpoints = new Dictionary<string, double>
                {
                    [SimulatorChannelIds.GridVoltage] = 0
                }
            },
            new ProfileScenario
            {
                Id = "dab-overcurrent",
                Label = "DAB 과전류",
                Description = "DAB 배터리 전류를 과전류 영역으로 밀어 넣습니다. 이상 점수는 검출기가 계산합니다.",
                Fault = nameof(PowerScenario.DabOvercurrent)
            },
            new ProfileScenario
            {
                Id = "psfb-undervoltage",
                Label = "PSFB 저전압",
                Description = "PSFB 48 V 레일을 강하시킵니다. 이상 점수는 검출기가 계산합니다.",
                Fault = nameof(PowerScenario.PsfbUnderVoltage)
            }
        ]
    };
}
