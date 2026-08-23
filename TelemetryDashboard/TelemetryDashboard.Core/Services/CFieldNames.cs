using System;
using System.Collections.Generic;
using TelemetryDashboard.Core.Models;

namespace TelemetryDashboard.Core.Services;

/// <summary>One channel, and the C identifier the generated struct calls it.</summary>
public sealed record CField(VariableDefinition Variable, string Name)
{
    /// <summary>C type of the struct member.</summary>
    public string DataType =>
        string.IsNullOrWhiteSpace(Variable.DataType) ? "float" : Variable.DataType;
}

/// <summary>
/// The single place a channel becomes a C field name.
/// </summary>
/// <remarks>
/// The header and the driver are two files that have to agree about every identifier, and they had
/// no shared answer: the header wrote a struct from the configured channels while the driver
/// referenced <c>data-&gt;temperature</c> and <c>data-&gt;vibration</c> whatever the configuration
/// said. The pair did not compile together for any configuration that was not the example. Both
/// now read their names from here, so agreeing is not something either has to remember.
/// <para>
/// Uniqueness is enforced here as well. <see cref="CHeaderGenerator.SanitizeIdentifier"/> maps
/// every run of punctuation to one underscore, so <c>psfb.output_voltage</c> and
/// <c>psfb-output-voltage</c> both become <c>psfb_output_voltage</c> — two struct members with the
/// same name, which a C compiler rejects outright.
/// </para>
/// </remarks>
public static class CFieldNames
{
    /// <summary>Names every variable in <paramref name="config"/>, distinctly and in order.</summary>
    public static IReadOnlyList<CField> For(SensorNodeConfig? config)
    {
        var fields = new List<CField>();
        var taken = new HashSet<string>(StringComparer.Ordinal);

        foreach (VariableDefinition variable in config?.Variables ?? new List<VariableDefinition>())
        {
            if (variable is null) continue;

            string name = CHeaderGenerator.SanitizeIdentifier(variable.Name);
            string unique = name;
            for (int suffix = 2; !taken.Add(unique); suffix++) unique = $"{name}_{suffix}";

            fields.Add(new CField(variable, unique));
        }

        return fields;
    }
}
