using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using TelemetryDashboard.Core.Simulator;

namespace TelemetryDashboard.UI;

/// <summary>
/// What the profile tab binds to: one row per channel the selected profile declares.
/// </summary>
/// <remarks>
/// The ribbon used to hold a slider and a readout per channel written out by hand, which is why it
/// could only ever describe one customer's converter. Binding a list of these instead means the tab
/// has no opinion about what is being measured — the profile decides, and a profile can arrive from
/// a file.
/// </remarks>
public sealed class ChannelSetpoint : INotifyPropertyChanged
{
    private readonly ProfileChannel _channel;
    private readonly Action<string, double> _onChanged;
    private double _value;

    public ChannelSetpoint(ProfileChannel channel, Action<string, double> onChanged)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(onChanged);

        _channel = channel;
        _onChanged = onChanged;
        _value = channel.Nominal;
    }

    public string Id => _channel.Id;
    public string Label => _channel.Label;
    public double Minimum => _channel.Minimum;
    public double Maximum => _channel.Maximum;

    /// <summary>Tooltip text: the limits the profile gives this channel.</summary>
    public string Range => $"{Format(_channel.Minimum)} – {Format(_channel.Maximum)}";

    public double Value
    {
        get => _value;
        set
        {
            double clamped = Math.Clamp(value, _channel.Minimum, _channel.Maximum);

            // The slider raises a change for every pixel of travel; comparing first keeps the
            // simulator from being written to at mouse-move rate for no change in the number.
            if (Math.Abs(clamped - _value) < double.Epsilon) return;

            _value = clamped;
            _onChanged(_channel.Id, clamped);
            Raise(nameof(Value));
            Raise(nameof(Display));
        }
    }

    /// <summary>The value with its unit, at the precision the profile asked for.</summary>
    public string Display => Format(_value);

    /// <summary>Applies a value from a scenario without echoing it back to the simulator twice.</summary>
    public void SetQuietly(double value)
    {
        _value = Math.Clamp(value, _channel.Minimum, _channel.Maximum);
        Raise(nameof(Value));
        Raise(nameof(Display));
    }

    private string Format(double value)
    {
        string number = value.ToString($"F{_channel.Decimals}", CultureInfo.CurrentCulture);
        return string.IsNullOrEmpty(_channel.Unit) ? number : $"{number} {_channel.Unit}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise(string property) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}

/// <summary>One generated scenario button.</summary>
public sealed class ScenarioAction
{
    public ScenarioAction(ProfileScenario scenario, Action<ProfileScenario> run)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(run);

        Label = scenario.Label;
        Description = scenario.Description;
        Apply = new RelayCommand(() => run(scenario));
    }

    public string Label { get; }

    /// <summary>Shown as a tooltip, so the button caption does not have to explain itself.</summary>
    public string Description { get; }

    public ICommand Apply { get; }
}

/// <summary>The one-line command the generated buttons need.</summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;

    public RelayCommand(Action execute)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
    }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => _execute();

    /// <summary>Never raised: these commands are always available while their tab is on screen.</summary>
    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }
}
