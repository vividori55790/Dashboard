using System.Collections.Generic;

namespace TelemetryDashboard.Core.Simulator;

/// <summary>
/// The default profile: temperature, humidity, vibration and speed.
/// </summary>
/// <remarks>
/// Nothing here names a product, a converter topology or a customer. It is what a machine has when
/// nobody has told the application what the machine is, and every channel drives a quantity the
/// built-in simulator genuinely models, so the sliders move real numbers rather than decorating the
/// tab with controls that do nothing.
/// </remarks>
internal static class GenericMachineProfile
{
    internal static MonitoringProfile Instance { get; } = new()
    {
        Id = MonitoringProfileLibrary.GenericId,
        DisplayName = "일반 장비 (기본)",
        Summary = "온도·습도·진동·회전수 네 채널을 가진 기본 프로파일입니다.",
        Channels =
        [
            new ProfileChannel
            {
                Id = SimulatorChannelIds.AmbientTemperature,
                Label = "온도", Unit = "°C",
                Minimum = 0, Maximum = 80, Nominal = 25, Decimals = 1
            },
            new ProfileChannel
            {
                Id = SimulatorChannelIds.AmbientHumidity,
                Label = "습도", Unit = "%",
                Minimum = 0, Maximum = 100, Nominal = 50, Decimals = 0
            },
            new ProfileChannel
            {
                Id = SimulatorChannelIds.MachineVibration,
                Label = "진동", Unit = "g",
                Minimum = 0, Maximum = 2, Nominal = 0.2, Decimals = 2
            },
            new ProfileChannel
            {
                Id = SimulatorChannelIds.MachineSpeed,
                Label = "회전수", Unit = "rpm",
                Minimum = 0, Maximum = 3000, Nominal = 1200, Decimals = 0
            }
        ],
        Scenarios =
        [
            new ProfileScenario
            {
                Id = "nominal",
                Label = "정상 운전",
                Description = "네 채널을 모두 기준값으로 되돌리고 주입된 고장을 해제합니다.",
                Fault = nameof(PowerScenario.Normal),
                Setpoints = new Dictionary<string, double>
                {
                    [SimulatorChannelIds.AmbientTemperature] = 25,
                    [SimulatorChannelIds.AmbientHumidity] = 50,
                    [SimulatorChannelIds.MachineVibration] = 0.2,
                    [SimulatorChannelIds.MachineSpeed] = 1200
                }
            },
            new ProfileScenario
            {
                Id = "overheating",
                Label = "과열",
                Description = "온도 기준값을 65 °C로 올립니다. 이상 점수는 검출기가 계산합니다.",
                Setpoints = new Dictionary<string, double>
                {
                    [SimulatorChannelIds.AmbientTemperature] = 65
                }
            },
            new ProfileScenario
            {
                Id = "rough-running",
                Label = "진동 과대",
                Description = "진동을 1.2 g로, 회전수를 2400 rpm으로 올립니다.",
                Setpoints = new Dictionary<string, double>
                {
                    [SimulatorChannelIds.MachineVibration] = 1.2,
                    [SimulatorChannelIds.MachineSpeed] = 2400
                }
            }
        ]
    };
}
