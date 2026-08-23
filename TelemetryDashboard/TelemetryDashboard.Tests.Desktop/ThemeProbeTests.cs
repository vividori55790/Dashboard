using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FluentAssertions;
using TelemetryDashboard.UI.Themes;
using Xunit;

namespace TelemetryDashboard.Tests.Desktop;

/// <summary>
/// The measurement the theme feature is finally judged by.
/// </summary>
/// <remarks>
/// Everything else about a theme switch can pass while the window stays dark, and twice it did.
/// The service reporting success, the dictionary holding the new colour, a test dictionary
/// repainting — none of those is the screen. This walks the realised tree and asks each element
/// what brush it is actually holding, which is the one check the broken versions would have failed.
/// <para>
/// Screen capture on the machine this is developed on returns a blank image, so this is also the
/// only evidence available about the running window. That is why the shell prints its result into
/// the event log: it can then be read back through UI Automation from outside the process.
/// </para>
/// </remarks>
public class ThemeProbeTests
{
    // No colour appears in both, which the two real palettes cannot promise -- white on the accent
    // is white in either theme, and the scrims are dark in both. The probe resolves that by asking
    // the active palette first; these fixtures keep the two apart so each test says one thing.
    private static readonly Color ActiveCanvas = Color.FromRgb(0xFF, 0xFF, 0xFF);
    private static readonly Color StaleCanvas = Color.FromRgb(0x10, 0x10, 0x10);

    private static readonly IReadOnlyDictionary<string, Color> Active =
        new Dictionary<string, Color>
        {
            ["CanvasColor"] = ActiveCanvas,
            ["TextColor"] = Color.FromRgb(0x22, 0x22, 0x22)
        };

    private static readonly IReadOnlyDictionary<string, Color> Other =
        new Dictionary<string, Color>
        {
            ["CanvasColor"] = StaleCanvas,
            ["TextColor"] = Color.FromRgb(0xEE, 0xEE, 0xEE)
        };

    private static SolidColorBrush Painted(Color colour) => new(colour);

    /// <summary>Lays the tree out, because the probe only ever sees a tree WPF has realised.</summary>
    private static Grid Realised(Grid root)
    {
        root.Measure(new Size(200, 200));
        root.Arrange(new Rect(0, 0, 200, 200));
        root.UpdateLayout();
        return root;
    }

    [WpfFact]
    [Trait("Category", "Tier1")]
    public void AnElementLeftOnTheOldPaletteIsCountedAndNamed()
    {
        // The failure this exists to catch: a control that kept the colour it was built with.
        var root = new Grid { Background = Painted(ActiveCanvas) };
        root.Children.Add(new Border { Background = Painted(StaleCanvas) });

        ThemeProbeResult result = ThemeProbe.Sample(Realised(root), Active, Other);

        result.Stale.Should().Be(1);
        result.Active.Should().Be(1);
        result.Stragglers.Should().ContainSingle().Which.Should().Contain("Border.Background #101010");
    }

    [WpfFact]
    [Trait("Category", "Tier1")]
    public void ManyElementsStuckTheSameWayAreOneLineWithACount()
    {
        // Five lines repeating the first of eighty-seven identical faults is not a report anybody
        // can act on, and it hides whatever the second fault was.
        var root = new Grid();
        for (int i = 0; i < 4; i++) root.Children.Add(new Border { Background = Painted(StaleCanvas) });

        ThemeProbeResult result = ThemeProbe.Sample(Realised(root), Active, Other);

        result.Stale.Should().Be(4);
        result.Stragglers.Should().ContainSingle().Which.Should().EndWith("x4");
    }

    [WpfFact]
    [Trait("Category", "Tier1")]
    public void AColourNeitherPaletteDeclaresIsNotAFailure()
    {
        // A chart trace or a vendor's chrome is not something the theme was ever going to reach,
        // and counting it as stuck would bury the ones that are.
        var root = new Grid();
        root.Children.Add(new Border { Background = Painted(Colors.HotPink) });

        ThemeProbeResult result = ThemeProbe.Sample(Realised(root), Active, Other);

        result.Custom.Should().Be(1);
        result.Stale.Should().Be(0);
        result.Describe().Should().NotContain("Stuck");
    }

    [WpfFact]
    [Trait("Category", "Tier1")]
    public void APropertyATemplateNeverPaintsIsNotMeasured()
    {
        // A templated control's own BorderBrush is only on the screen if its template binds it, and
        // this application's templates frequently do not -- they paint a Border of their own and
        // leave the control's property at whatever WPF defaulted it to. Counting those made an
        // earlier version of this report claim 37 unthemed brushes on a window rendering none of
        // them, which would have sent somebody hunting a fault that was not there.
        var painted = new FrameworkElementFactory(typeof(Border));
        painted.SetValue(Border.BackgroundProperty, Painted(ActiveCanvas));

        var root = new Grid();
        root.Children.Add(new Button
        {
            // The stale colour, on a property this template ignores completely.
            BorderBrush = Painted(StaleCanvas),
            Template = new ControlTemplate(typeof(Button)) { VisualTree = painted }
        });

        ThemeProbeResult result = ThemeProbe.Sample(Realised(root), Active, Other);

        result.Active.Should().Be(1, "the Border the template paints is on the screen");
        result.Stale.Should().Be(0, "the control's own property is not");
    }

    [WpfFact]
    [Trait("Category", "Tier2")]
    public void AFullyTransparentBrushIsNotAColourAnybodySees()
    {
        var root = new Grid();
        root.Children.Add(new Border { Background = Painted(Color.FromArgb(0, 0, 0, 0)) });

        ThemeProbe.Sample(Realised(root), Active, Other).Painted.Should().Be(0);
    }

    [WpfFact]
    [Trait("Category", "Tier2")]
    public void NoTreeAtAllIsAnEmptyReportRatherThanACrash()
    {
        // It runs from a theme change, and a theme can be applied before there is a window.
        ThemeProbeResult result = ThemeProbe.Sample(null, Active, Other);

        result.Painted.Should().Be(0);
        result.Describe().Should().Contain("0 painted brushes");
    }
}
