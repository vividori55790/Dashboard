using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using TelemetryDashboard.Core.Analytics;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Simulator;

namespace TelemetryDashboard.UI;

/// <summary>
/// The simulation tab: which system is being modelled, and running the model.
/// </summary>
/// <remarks>
/// This file used to hold one handler per control on a hardcoded ribbon — a click for that
/// customer's grid, a slider for their DC bus, a fault for their converter — so supporting a second
/// installation meant editing XAML and C# together. The controls are now generated from the
/// selected <see cref="MonitoringProfile"/>, and everything below is about applying a profile
/// rather than about any particular piece of hardware.
/// </remarks>
public partial class MainWindow
{
    /// <summary>Profiles offered in the picker: the built-in ones plus anything on disk.</summary>
    public ObservableCollection<MonitoringProfile> Profiles { get; } = [];

    /// <summary>One slider row per channel of the selected profile.</summary>
    public ObservableCollection<ChannelSetpoint> ProfileChannels { get; } = [];

    /// <summary>One button per scenario of the selected profile.</summary>
    public ObservableCollection<ScenarioAction> ProfileScenarios { get; } = [];

    private MonitoringProfile? _activeProfile;

    /// <summary>
    /// Loads the profile set and selects the neutral built-in one.
    /// </summary>
    /// <remarks>
    /// The loader's account of itself goes straight into the event log, including the ordinary case
    /// of there being no file. An operator who put a profile on disk and does not see it needs to
    /// read why, and a silent fallback to a different profile is the one outcome worth refusing.
    /// </remarks>
    private void InitializeProfiles()
    {
        MonitoringProfileSet set = MonitoringProfileStore.Load(AppDomain.CurrentDomain.BaseDirectory);

        foreach (MonitoringProfile profile in set.Profiles)
        {
            Profiles.Add(profile);
        }

        ControlPanel.LogMessage(
            set.Status == ProfileSourceStatus.Invalid ? "ERROR" : "PROFILE", set.Message);

        CboProfile.SelectedItem = MonitoringProfileSet.Default;
        if (_activeProfile is null) ApplyProfile(MonitoringProfileSet.Default);
    }

    private void CboProfile_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CboProfile.SelectedItem is MonitoringProfile profile) ApplyProfile(profile);
    }

    /// <summary>Rebuilds the tab's controls from a profile and points the simulator at it.</summary>
    private void ApplyProfile(MonitoringProfile profile)
    {
        if (ReferenceEquals(profile, _activeProfile)) return;
        _activeProfile = profile;

        // Clearing first means a channel the previous profile drove stops being driven, rather
        // than lingering as a setpoint nothing on screen can see or reset.
        _simulator.Reset();
        ProfileChannels.Clear();
        ProfileScenarios.Clear();

        foreach (ProfileChannel channel in profile.Channels)
        {
            _simulator.SetSetpoint(channel.Id, channel.Nominal);
            ProfileChannels.Add(new ChannelSetpoint(channel, _simulator.SetSetpoint));
        }

        foreach (ProfileScenario scenario in profile.Scenarios)
        {
            ProfileScenarios.Add(new ScenarioAction(scenario, RunScenario));
        }

        ActiveProfileText.Text = profile.DisplayName;
        ProfileSummaryText.Text = profile.Summary;
        ControlPanel.LogMessage("PROFILE", $"모니터링 프로파일 적용: {profile.DisplayName}");
    }

    /// <summary>
    /// Applies one scenario: its setpoints, then whatever fault it names.
    /// </summary>
    /// <remarks>
    /// A scenario perturbs the physical model and nothing else. It never states a sigma, a severity
    /// or an alarm — those come out of the analytics engine scoring the numbers that result, which
    /// is the same path real hardware takes.
    /// </remarks>
    private void RunScenario(ProfileScenario scenario)
    {
        foreach (KeyValuePair<string, double> setpoint in scenario.Setpoints)
        {
            _simulator.SetSetpoint(setpoint.Key, setpoint.Value);

            ChannelSetpoint? row = ProfileChannels.FirstOrDefault(
                c => string.Equals(c.Id, setpoint.Key, StringComparison.OrdinalIgnoreCase));
            row?.SetQuietly(setpoint.Value);
        }

        ApplyScenarioFault(scenario);
        ControlPanel.LogMessage("SCENARIO", $"시나리오 적용: {scenario.Label}");
    }

    /// <summary>Resolves a scenario's fault name against the simulator's fault model.</summary>
    private void ApplyScenarioFault(ProfileScenario scenario)
    {
        if (string.IsNullOrWhiteSpace(scenario.Fault)) return;

        if (Enum.TryParse(scenario.Fault, ignoreCase: true, out PowerScenario fault))
        {
            _simulator.Scenario = fault;
            return;
        }

        // A profile from a file can name a fault this build does not have. Saying so beats
        // pressing a button that quietly does half of what its caption claims.
        ControlPanel.LogMessage("WARN",
            $"시나리오 '{scenario.Label}' 의 fault '{scenario.Fault}' 를 알 수 없어 설정값만 적용했습니다.");
    }

    private void BtnToggleSimulator_Click(object sender, RoutedEventArgs e)
    {
        if (_isSimulating)
        {
            StopSimulator();
        }
        else
        {
            StartSimulator();
        }
    }

    private void StartSimulator()
    {
        if (_isConnected)
        {
            ControlPanel.LogMessage("SIMULATOR", "하드웨어 포트를 먼저 연결 해제한 뒤 시뮬레이터를 시작하세요.");
            return;
        }

        _isSimulating = true;
        _simTimer?.Start();

        // Consume the virtual MCU stream through the same parser -> router -> ML path the real
        // hardware uses. Previously the engine was started and its packets thrown away, so the
        // simulator exercised none of the ingest pipeline it exists to exercise.
        _simulatorEngine.StartSimulation();
        _simulatorReadCts = new CancellationTokenSource();
        _ = Task.Run(() => ConsumeSimulatedPacketsAsync(_simulatorReadCts.Token));

        BtnToggleSimulator.Content = "가상 MCU 스트림 정지";
        ControlPanel.LogMessage("SIMULATOR", "가상 MCU 스트림 시작 (COM3/COM4 수집 활성).");
    }

    private void StopSimulator()
    {
        _isSimulating = false;
        _simTimer?.Stop(); // the tick timer kept running and kept broadcasting simulated frames

        _simulatorReadCts?.Cancel();
        _simulatorReadCts = null;
        _simulatorEngine.StopSimulation();

        BtnToggleSimulator.Content = "가상 MCU 스트림 시작";
        ControlPanel.LogMessage("SIMULATOR", "가상 MCU 스트림 정지.");
    }

    /// <summary>
    /// Routes virtual-MCU packets through the production ingest path so parsing, routing and
    /// anomaly scoring are all genuinely exercised in simulation.
    /// </summary>
    private async Task ConsumeSimulatedPacketsAsync(CancellationToken token)
    {
        try
        {
            await foreach (RawPacket raw in _simulatorEngine.StreamSimulatedPackets(token))
            {
                foreach (TelemetryPacket packet in ResolvePackets(raw))
                {
                    AnomalyResult analysis = _mlEngine.AnalyzeChannel(
                        $"{packet.NodeId}.{packet.Variable}", packet.Value);

                    await Dispatcher.InvokeAsync(() =>
                    {
                        ScopeControl.PushChannel(packet.Variable, packet.Value);
                        ControlPanel.UpdateChannelStats(packet.NodeId, packet.Variable, packet.Value, analysis);
                    });

                    _streamingServer.BroadcastTelemetry(new
                    {
                        timestamp = packet.Timestamp.ToString("o"),
                        source = "VIRTUAL_MCU",
                        nodeId = packet.NodeId,
                        variable = packet.Variable,
                        value = packet.Value,
                        unit = packet.Unit,
                        anomalyScore = analysis.ZScore,
                        isAnomaly = analysis.IsAnomaly
                    });
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Simulator stopped.
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() =>
                ControlPanel.LogMessage("ERROR", $"가상 MCU 수집 실패: {ex.Message}"));
        }
    }
}
