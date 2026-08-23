using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using FluentAssertions;
using TelemetryDashboard.UI.Services;
using Xunit;

namespace TelemetryDashboard.Tests.Desktop;

/// <summary>
/// Reading the ribbon so the palette knows about all of it.
/// </summary>
/// <remarks>
/// The palette held five commands while the ribbon carried thirty, and a command on an unselected
/// tab cannot be seen at all — so the palette was a shortcut to the handful of things an operator
/// was least likely to be hunting for.
/// <para>
/// The load-bearing case is <see cref="CommandsAreFoundOnTabsThatWereNeverSelected"/>. A TabControl
/// builds no visual tree for an unselected tab, so the obvious walk finds only whichever tab
/// happens to be showing — and would have passed every other test here while harvesting one tab in
/// ten. The walk is over the logical tree, which XAML parsing populates in full.
/// </para>
/// <para>
/// Driven on the running window as well: Ctrl+Shift+P, typing 헤더, and Enter opened the firmware
/// dialog without the 도구 tab ever being selected. Querying each ribbon tab name in turn listed 30
/// distinct commands.
/// </para>
/// </remarks>
public class RibbonCommandHarvestTests
{
    private static TabItem Tab(string header, params Button[] buttons)
    {
        var panel = new WrapPanel();
        foreach (Button button in buttons) panel.Children.Add(button);
        return new TabItem { Header = header, Content = panel };
    }

    private static TabControl Ribbon(params TabItem[] tabs)
    {
        var ribbon = new TabControl();
        foreach (TabItem tab in tabs) ribbon.Items.Add(tab);
        return ribbon;
    }

    [WpfFact]
    [Trait("Category", "Palette")]
    public void CommandsAreFoundOnTabsThatWereNeverSelected()
    {
        // Only the first tab is ever selected, and it is the one with nothing interesting on it.
        TabControl ribbon = Ribbon(
            Tab("첫 번째", new Button { Content = "Front" }),
            Tab("도구", new Button { Content = "C 헤더 생성" }),
            Tab("설정", new Button { Content = "화면 잠금" }));
        ribbon.SelectedIndex = 0;

        IReadOnlyList<CommandItem> found = RibbonCommandHarvest.From(ribbon);

        found.Select(c => c.Name).Should().BeEquivalentTo("Front", "C 헤더 생성", "화면 잠금");
    }

    [WpfFact]
    [Trait("Category", "Palette")]
    public void TheCategoryIsTheTabTheCommandSitsOn()
    {
        TabControl ribbon = Ribbon(Tab("도구", new Button { Content = "수식 계산기" }));

        RibbonCommandHarvest.From(ribbon).Single().Category.Should().Be("도구");
    }

    [WpfFact]
    [Trait("Category", "Palette")]
    public void AButtonLabelledWithAnIconAndTextIsNamedByTheText()
    {
        // Several ribbon buttons carry a Segoe MDL2 glyph beside their caption. Joining the glyph
        // into the name produces an entry nobody can type.
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = "" });
        content.Children.Add(new TextBlock { Text = "WebSocket 주소 복사" });
        TabControl ribbon = Ribbon(Tab("웹 콘솔", new Button { Content = content }));

        RibbonCommandHarvest.From(ribbon).Single().Name.Should().Be("WebSocket 주소 복사");
    }

    [WpfFact]
    [Trait("Category", "Palette")]
    public void RunningAHarvestedCommandClicksTheButtonItCameFrom()
    {
        int clicks = 0;
        var button = new Button { Content = "연결" };
        button.Click += (_, _) => clicks++;

        RibbonCommandHarvest.From(Ribbon(Tab("연결", button))).Single().Action!.Invoke();

        clicks.Should().Be(1, "the palette runs the ribbon's own handler rather than a copy of it");
    }

    [WpfFact]
    [Trait("Category", "Palette")]
    public void ACaptionSeenTwiceIsRegisteredOnce()
    {
        // Registration is by name, so a duplicate would silently replace the first button's action
        // with the second's. One entry, and it is the one it looks like.
        TabControl ribbon = Ribbon(
            Tab("도구", new Button { Content = "설정" }),
            Tab("설정", new Button { Content = "설정" }));

        RibbonCommandHarvest.From(ribbon).Should().ContainSingle().Which.Category.Should().Be("도구");
    }

    [WpfFact]
    [Trait("Category", "Palette")]
    public void AButtonWithNothingToCallItIsLeftOut()
    {
        var iconOnly = new StackPanel();
        iconOnly.Children.Add(new TextBlock { Text = "" });
        TabControl ribbon = Ribbon(Tab("도구", new Button { Content = iconOnly }, new Button()));

        RibbonCommandHarvest.From(ribbon).Should().BeEmpty();
    }

    [WpfFact]
    [Trait("Category", "Palette")]
    public void ThereIsNothingToHarvestWithoutARibbon()
    {
        RibbonCommandHarvest.From(null).Should().BeEmpty();
    }
}
