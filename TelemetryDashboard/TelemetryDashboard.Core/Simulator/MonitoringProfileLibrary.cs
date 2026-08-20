using System.Collections.Generic;

namespace TelemetryDashboard.Core.Simulator;

/// <summary>
/// The profiles that ship inside the application.
/// </summary>
/// <remarks>
/// <see cref="Generic"/> is the default deliberately. The power-converter profile describes one
/// customer's UPS bench, and shipping it as the thing a new operator sees first told everybody else
/// that this application was not for them. It is still here, complete, but it is now one profile
/// among however many a site defines rather than the shape of the product.
/// </remarks>
public static class MonitoringProfileLibrary
{
    public const string GenericId = "generic-machine";
    public const string PowerConverterId = "dab-psfb-ups";

    /// <summary>Four ordinary machine channels, named after the quantity rather than any one rig.</summary>
    public static MonitoringProfile Generic => GenericMachineProfile.Instance;

    /// <summary>The bundled UPS example: grid feed, DAB battery converter, PSFB server rail.</summary>
    public static MonitoringProfile PowerConverterUps => PowerConverterUpsProfile.Instance;

    /// <summary>Every profile compiled into the application, generic first.</summary>
    public static IReadOnlyList<MonitoringProfile> BuiltIn { get; } = [Generic, PowerConverterUps];
}
