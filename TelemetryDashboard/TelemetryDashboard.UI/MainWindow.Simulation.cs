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
using TelemetryDashboard.UI.Controls;

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

    /// <summary>Channel ids named by a profile that the built-in model does not measure.</summary>
    /// <remarks>
    /// Kept so the explanation is written once per channel rather than twenty times a second. The
    /// channel is left off the chart either way; this only governs how often that is said.
    /// </remarks>
    private readonly HashSet<string> _unmeasuredChannels = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Loads the profile set, reports how that went, and selects the neutral built-in profile.
    /// </summary>
    /// <remarks>
    /// This existed and was never called, which is the whole of why the picker was empty: the
    /// collection behind it stayed at zero items, no selection could be made, and the subtitle kept
    /// the "preparing" placeholder it is given in XAML. Nothing about the load, the binding or the
    /// ordering was wrong — the entry point simply had no caller.
    /// <para>
    /// A file that could not be used is now also said on the tab itself, not only in the event log.
    /// An empty-looking picker and a picker whose file was rejected are the same picture, and this
    /// application exists on the premise that those two must never be the same picture.
    /// </para>
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

        ShowProfileLoadProblem(set);

        CboProfile.SelectedItem = MonitoringProfileSet.Default;
        if (_activeProfile is null) ApplyProfile(MonitoringProfileSet.Default);
    }

    /// <summary>
    /// Puts a rejected profile file in front of the operator, on the tab that lists profiles.
    /// </summary>
    /// <remarks>
    /// Only for <see cref="ProfileSourceStatus.Invalid"/>. Having no profile file is the ordinary
    /// state of a fresh installation and warning about it would teach the operator to ignore the
    /// banner, which costs the one time it means something.
    /// </remarks>
    private void ShowProfileLoadProblem(MonitoringProfileSet set)
    {
        if (set.Status != ProfileSourceStatus.Invalid)
        {
            ProfileLoadBanner.Visibility = Visibility.Collapsed;
            return;
        }

        ProfileLoadText.Text = set.Message;
        ProfileLoadBanner.Visibility = Visibility.Visible;
    }

    /// <summary>Applies the picked profile, and stops the pick from being mistaken for a tab change.</summary>
    /// <remarks>
    /// SelectionChanged is a routed event, and the picker sits inside the ribbon's TabControl —
    /// which is itself a Selector and answers the event as though one of its own tabs had been
    /// chosen, re-realising its content. Marking it handled keeps the profile pick inside the
    /// picker, where it belongs.
    /// </remarks>
    private void CboProfile_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        e.Handled = true;
        if (CboProfile.SelectedItem is MonitoringProfile profile) ApplyProfile(profile);
    }

    /// <summary>Rebuilds everything a profile decides, and points the simulator at it.</summary>
    /// <remarks>
    /// Four things follow the selection, and they are listed here rather than each finding out on
    /// their own: the simulator's setpoints, the setpoint sliders and scenario buttons on this tab,
    /// the node switches in the control panel, and which channels the scope is fed.
    /// </remarks>
    private void ApplyProfile(MonitoringProfile profile)
    {
        if (ReferenceEquals(profile, _activeProfile)) return;

        MonitoringProfile? previous = _activeProfile;
        _activeProfile = profile;
        _unmeasuredChannels.Clear();

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

        ControlPanel.ShowProfileNodes(profile.DisplayName, profile.Nodes);
        RetireScopeChannels(previous, profile);

        ActiveProfileText.Text = profile.DisplayName;
        ProfileSummaryText.Text = profile.Summary;
        ControlPanel.LogMessage("PROFILE", $"모니터링 프로파일 적용: {profile.DisplayName}");
    }

    /// <summary>
    /// Stops plotting the outgoing profile's channels, without touching anything else on the scope.
    /// </summary>
    /// <remarks>
    /// Only series the previous profile put there are retired. The scope also carries channels
    /// discovered from the incoming packet stream, and those belong to the hardware rather than to
    /// the selection — hiding them because the operator changed profile would blank out live traces
    /// that never stopped arriving.
    /// </remarks>
    private void RetireScopeChannels(MonitoringProfile? previous, MonitoringProfile current)
    {
        if (previous is null) return;

        foreach (ScopeChannelSeries series in ScopeControl.Channels)
        {
            bool wasProfileChannel = previous.Channels.Any(
                c => string.Equals(c.Label, series.Name, StringComparison.OrdinalIgnoreCase));
            bool stillWanted = current.Channels.Any(
                c => string.Equals(c.Label, series.Name, StringComparison.OrdinalIgnoreCase));

            if (!wasProfileChannel || stillWanted) continue;

            series.Clear();
            series.IsVisible = false;
        }
    }

    /// <summary>
    /// Feeds the scope the channels the active profile declares, at their measured values.
    /// </summary>
    /// <remarks>
    /// This used to push a fixed four — temperature, humidity, vibration and a figure labelled RPM
    /// that was the model's rpm divided by a hundred — so the chart described the same four
    /// quantities whichever system had been selected, and one of the four was a number nothing had
    /// computed. A channel the built-in model does not measure is now left off the chart and named
    /// in the log, because an absent trace is honest and a substituted one is not.
    /// </remarks>
    private void PushProfileChannelsToScope(PowerPlantState state)
    {
        if (_activeProfile is null) return;

        foreach (ProfileChannel channel in _activeProfile.Channels)
        {
            if (SimulatorChannelReadings.TryRead(state, channel.Id, out double value))
            {
                ScopeControl.PushChannel(channel.Label, value);
                continue;
            }

            if (_unmeasuredChannels.Add(channel.Id))
            {
                ControlPanel.LogMessage("WARN",
                    $"채널 '{channel.Label}' ({channel.Id}) 은 내장 시뮬레이터가 계산하지 않아 스코프에 표시하지 않습니다.");
            }
        }
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

        // Every packet from here is synthetic, and has to say so where it is stored as well as
        // where it is displayed. Without this the durable archive held simulated readings under
        // ordinary node names with no flag set — a file that an operator, or the MATLAB export
        // reading it months later, has no way to tell from a record of the real machine.
        _dataRouter.SourceIsSimulated = true;

        // Consume the virtual MCU stream through the same parser -> router -> ML path the real
        // hardware uses. Previously the engine was started and its packets thrown away, so the
        // simulator exercised none of the ingest pipeline it exists to exercise.
        // Built here rather than at construction so it follows the profile actually selected.
        _simulatorEngine?.Dispose();
        _simulatorEngine = new ProfileSimulatorEngine(_activeProfile ?? MonitoringProfileSet.Default);
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
        _dataRouter.SourceIsSimulated = false;

        _simulatorReadCts?.Cancel();
        _simulatorReadCts = null;
        _simulatorEngine?.StopSimulation();

        BtnToggleSimulator.Content = "가상 MCU 스트림 시작";
        ControlPanel.LogMessage("SIMULATOR", "가상 MCU 스트림 정지.");
    }

    /// <summary>Channels currently reading anomalous, so a run of them logs once rather than 40 times a second.</summary>
    private readonly HashSet<string> _channelsInAnomaly = new(StringComparer.Ordinal);

    /// <summary>
    /// Logs a channel entering or leaving its anomalous state, and nothing in between.
    /// </summary>
    /// <remarks>
    /// The event this replaces named DAB_CONVERTER or PSFB_CONVERTER regardless of the selected
    /// profile, so the log attributed every alarm to one customer's converters. It now reports the
    /// channel the reading actually came from.
    /// <para>
    /// Edges only. A channel that stays out of range for a minute is one event, not two thousand
    /// four hundred, and the recovery is worth a line for the same reason the onset is: a log that
    /// shows an alarm and never shows it clearing reads like an alarm that never cleared.
    /// </para>
    /// </remarks>
    private void LogAnomalyTransition(TelemetryPacket packet, AnomalyResult analysis)
    {
        string key = $"{packet.NodeId}.{packet.Variable}";

        if (analysis.IsAnomaly)
        {
            lock (_channelsInAnomaly)
            {
                if (!_channelsInAnomaly.Add(key)) return;
            }

            ControlPanel.LogTelemetryEvent(packet.NodeId, packet.Variable,
                packet.Value, analysis.ZScore,
                $"{packet.Value:F3}{packet.Unit} — 검출기가 이상으로 판정 (z={analysis.ZScore:F2})");
            return;
        }

        lock (_channelsInAnomaly)
        {
            if (!_channelsInAnomaly.Remove(key)) return;
        }

        ControlPanel.LogTelemetryEvent(packet.NodeId, packet.Variable,
            packet.Value, analysis.ZScore,
            $"{packet.Value:F3}{packet.Unit} — 정상 범위로 복귀");
    }

    /// <summary>
    /// Routes virtual-MCU packets through the production ingest path so parsing, routing and
    /// anomaly scoring are all genuinely exercised in simulation.
    /// </summary>
    private async Task ConsumeSimulatedPacketsAsync(CancellationToken token)
    {
        try
        {
            ProfileSimulatorEngine? engine = _simulatorEngine;
            if (engine is null) return;

            await foreach (RawPacket raw in engine.StreamSimulatedPackets(token))
            {
                foreach (TelemetryPacket packet in ResolvePackets(raw))
                {
                    AnomalyResult analysis = _mlEngine.AnalyzeChannel(
                        $"{packet.NodeId}.{packet.Variable}", packet.Value);

                    // Recorded here rather than on the display tick, so what lands in the CSV and
                    // the archive is whatever the selected profile actually produced. Both stores
                    // are safe to write from this thread: the recorder enqueues and the archive is
                    // a bounded channel.
                    PersistSample(packet, analysis);
                    LogAnomalyTransition(packet, analysis);

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
