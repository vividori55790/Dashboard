using System.Text;

namespace TelemetryDashboard.Core.Streaming;

/// <summary>
/// How an exposition document is assembled: families, headers and sample lines.
/// </summary>
/// <remarks>
/// Split from the half that decides what to say, because the two fail differently. A mistake here
/// is a document a scraper rejects outright, which is loud; a mistake there is a document a scraper
/// accepts and misreads, which is silent.
/// <para>
/// The tokens themselves -- what may appear inside a label value, how a double is spelled -- live
/// in <c>MetricsEndpoint.Escaping.cs</c> beside the sentences of the specification they came from.
/// </para>
/// </remarks>
public static partial class MetricsEndpoint
{
    /// <summary>The document under construction. Nothing else here appends to it.</summary>
    private sealed class Document
    {
        private readonly StringBuilder _text = new(16 * 1024);

        /// <summary>
        /// Begins a metric family. Its header lines are written by the first sample, or never.
        /// </summary>
        /// <remarks>
        /// This is where this endpoint's governing rule is enforced by construction rather than by
        /// discipline. A caller that finds nothing to report simply does not call
        /// <see cref="Family.Sample(double)"/>, and the family then contributes no bytes at all --
        /// no header advertising a metric with no series, and above all no zero standing in for a
        /// reading nobody took. The alternative shape, writing the header eagerly and hoping every
        /// caller remembers to skip the sample, puts the rule in twenty places instead of one.
        /// <para>
        /// Every name is prefixed here, so no call site can spell the namespace differently.
        /// </para>
        /// </remarks>
        public Family Open(string name, string type, string help) =>
            new(_text, Prefix + name, type, help);

        public override string ToString() => _text.ToString();
    }

    /// <summary>One metric family and the samples in it.</summary>
    /// <remarks>
    /// The exposition format requires that all lines for a given metric be provided as one single
    /// group with the HELP and TYPE lines first, so a family is opened, filled and finished before
    /// the next one begins. Nothing enforces that ordering at runtime; it is a property of the
    /// composition in the other partials, which write one family at a time.
    /// </remarks>
    private sealed class Family
    {
        private readonly StringBuilder _text;
        private readonly string _name;
        private readonly string _type;
        private readonly string _help;
        private bool _headed;

        internal Family(StringBuilder text, string name, string type, string help)
        {
            _text = text;
            _name = name;
            _type = type;
            _help = help;
        }

        public void Sample(double value) => Write(value, null, null, null, null);

        public void Sample(double value, string label, string labelValue) =>
            Write(value, label, labelValue, null, null);

        public void Sample(double value, string label, string labelValue, string label2, string labelValue2) =>
            Write(value, label, labelValue, label2, labelValue2);

        private void Write(double value, string? label, string? labelValue, string? label2, string? labelValue2)
        {
            if (!_headed)
            {
                _headed = true;
                _text.Append("# HELP ").Append(_name).Append(' ').Append(EscapeHelp(_help)).Append('\n');
                _text.Append("# TYPE ").Append(_name).Append(' ').Append(_type).Append('\n');
            }

            _text.Append(_name);

            if (label is not null)
            {
                _text.Append('{');
                AppendLabel(_text, label, labelValue!);
                if (label2 is not null)
                {
                    _text.Append(',');
                    AppendLabel(_text, label2, labelValue2!);
                }
                _text.Append('}');
            }

            _text.Append(' ');
            AppendNumber(_text, value);
            _text.Append('\n');
        }

        private static void AppendLabel(StringBuilder text, string name, string value)
        {
            text.Append(name).Append('=').Append('"');
            AppendEscaped(text, value);
            text.Append('"');
        }
    }
}
