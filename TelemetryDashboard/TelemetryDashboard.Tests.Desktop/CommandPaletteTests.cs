using System.Collections.Generic;
using FluentAssertions;
using TelemetryDashboard.UI.Services;
using Xunit;

namespace TelemetryDashboard.Tests.Desktop;

/// <summary>
/// What the command palette does with a query and with the arrow keys.
/// </summary>
/// <remarks>
/// Reported as "the keys do nothing", and three separate things were true at once.
/// <list type="number">
/// <item>The list was built when the overlay was attached, which happened before any command had
/// been registered — so it was built from an empty dictionary and the palette opened showing
/// nothing. Typing a letter repopulated it, which is why it looked intermittent.</item>
/// <item>Nothing focused the search box when the palette appeared, so what the operator typed went
/// to whatever was behind it. That is a view concern and is fixed in the overlay.</item>
/// <item>Selection lived in the ListBox and filtering lived in the service, and the service's
/// navigation clamped to the <em>total</em> command count. With a query typed, the arrow keys
/// indexed past the end of what was on screen.</item>
/// </list>
/// The first and third are logic and are pinned here. Nothing in this file needs a window.
/// </remarks>
public class CommandPaletteTests
{
    private static CommandPaletteService Palette(List<string>? ran = null)
    {
        var service = new CommandPaletteService();
        foreach (string name in new[]
                 {
                     "Toggle Theme", "Scope Layout", "Start Dual-MCU Simulator",
                     "Stop Dual-MCU Simulator", "Open ML Analytics Modal"
                 })
        {
            string captured = name;
            service.RegisterCommand(captured, "Test", () => ran?.Add(captured));
        }

        return service;
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void OpeningThePaletteListsTheCommandsRegisteredSinceItWasAttached()
    {
        // The defect exactly: the overlay attached at start-up and the commands were registered
        // afterwards. Building the list on open is what makes the wiring order stop mattering.
        var service = new CommandPaletteService();
        service.Open().Should().BeEmpty("nothing is registered yet");

        service.RegisterCommand("Toggle Theme", "View", () => { });

        service.Open().Should().ContainSingle().Which.Should().Be("Toggle Theme");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void TypingSelectsTheFirstMatchSoEnterRunsIt()
    {
        var ran = new List<string>();
        CommandPaletteService service = Palette(ran);
        service.Open();

        service.ApplyQuery("simul");

        service.Filtered.Should().HaveCount(2);
        service.SelectedCommand.Should().Be("Start Dual-MCU Simulator");
        service.ExecuteSelected().Should().Be("Start Dual-MCU Simulator");
        ran.Should().Equal(new[] { "Start Dual-MCU Simulator" });
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void TheArrowKeysMoveWithinWhatIsOnScreenRatherThanTheWholeList()
    {
        CommandPaletteService service = Palette();
        service.Open();
        service.ApplyQuery("simulator");     // two of the five

        service.MoveNext();
        service.SelectedCommand.Should().Be("Stop Dual-MCU Simulator");

        // Wraps, rather than running off the end of a list it is not showing.
        service.MoveNext();
        service.SelectedCommand.Should().Be("Start Dual-MCU Simulator");

        service.MovePrevious();
        service.SelectedCommand.Should().Be("Stop Dual-MCU Simulator");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void AQueryMatchingNothingSelectsNothingAndRunsNothing()
    {
        var ran = new List<string>();
        CommandPaletteService service = Palette(ran);
        service.Open();

        service.ApplyQuery("no such command");

        service.Filtered.Should().BeEmpty();
        service.SelectedCommand.Should().BeNull();
        service.ExecuteSelected().Should().BeNull();
        service.MoveNext();
        service.SelectedCommand.Should().BeNull("moving within an empty list has nowhere to go");
        ran.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ClosingThePaletteLeavesItReadyToOpenAgainOnTheNextPress()
    {
        // Escape and Enter used to hide the control and leave IsVisible true, so the next
        // Ctrl+Shift+P toggled it to false and nothing appeared. It took two presses to reopen,
        // every time, after any use of it.
        CommandPaletteService service = Palette();

        service.ToggleVisibility();
        service.IsVisible.Should().BeTrue();

        service.Close();                       // what Escape and Enter do
        service.IsVisible.Should().BeFalse();

        service.ToggleVisibility();
        service.IsVisible.Should().BeTrue("one press, not two");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void MatchingIgnoresCaseAndDoesNotTreatTheQueryAsAPattern()
    {
        CommandPaletteService service = Palette();
        service.Open();

        service.ApplyQuery("THEME").Should().ContainSingle();
        // Was a regex over an escaped query, which is a substring test written to look like it
        // supports patterns. Stated plainly instead, so nobody expects the other thing.
        service.ApplyQuery("Dual.MCU").Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void EveryCommandTheWindowRegistersIsReachableByTyping()
    {
        CommandPaletteService service = Palette();
        service.Open();

        foreach (string name in service.Filtered)
        {
            service.ApplyQuery(name).Should().Contain(name,
                $"'{name}' has to be findable by its own name");
        }
    }

    [Fact]
    [Trait("Category", "Palette")]
    public void ACommandIsFoundByTheTabItSitsOnAsWellAsByItsName()
    {
        // The category is the ribbon tab, and that is often what somebody remembers: they know the
        // export is under 도구 without remembering the button's wording. Matching names alone left
        // the one piece of structure the palette inherited from the ribbon unsearchable.
        var service = new CommandPaletteService();
        service.RegisterCommand("MATLAB 파일로 내보내기", "도구", () => { });
        service.RegisterCommand("포트 다시 검색", "연결", () => { });

        service.ApplyQuery("도구").Should().ContainSingle().Which.Should().Be("MATLAB 파일로 내보내기");
        service.ApplyQuery("연결").Should().ContainSingle().Which.Should().Be("포트 다시 검색");
    }

    [Fact]
    [Trait("Category", "Palette")]
    public void AQueryMatchingNeitherNameNorCategoryFindsNothing()
    {
        var service = new CommandPaletteService();
        service.RegisterCommand("포트 다시 검색", "연결", () => { });

        service.ApplyQuery("디지털 트윈").Should().BeEmpty();
        service.SelectedCommand.Should().BeNull("Enter must not run something that did not match");
    }
}
