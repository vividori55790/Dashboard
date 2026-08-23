using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using TelemetryDashboard.UI.Services;
using Xunit;

namespace TelemetryDashboard.Tests.Desktop;

/// <summary>
/// Switching the language, which used to report success and change nothing.
/// </summary>
/// <remarks>
/// The ribbon's captions were literals in MainWindow.xaml. Pressing 언어 전환 changed the culture,
/// raised a <c>LanguageChanged</c> event with no subscribers, logged "Language switched to en-US"
/// and left every word on screen exactly as it was.
/// <para>
/// Driven on the running window: Ctrl+Shift+P, 언어, Enter — the seven ribbon tabs became
/// "Simulation profile | Connection | Analysis &amp; replay | Alarms &amp; sampling | Web console |
/// Tools | Settings" without a restart, the one realised ribbon button followed, and the palette
/// re-harvested: searching "Generate" found "Generate C header" and the old Korean caption had no
/// entry left.
/// </para>
/// </remarks>
public class UiStringsTests
{
    [Fact]
    [Trait("Category", "Language")]
    public void TheDictionaryIsNamedByAnAbsolutePackUri()
    {
        // The defect this pins, measured on the running application: a relative uri resolves
        // against the markup that declared it, and a ResourceDictionary built in code has none.
        // The swap reported success and not one caption changed.
        Uri source = UiStrings.SourceFor("en-US");

        source.IsAbsoluteUri.Should().BeTrue();
        source.Scheme.Should().Be("pack");
        source.OriginalString.Should().EndWith("Resources/Strings.en-US.xaml");
    }

    [Fact]
    [Trait("Category", "Language")]
    public void WithoutAnApplicationTheSwapIsRefusedRatherThanThrowing()
    {
        // The headless host and this test both run without one. A language that cannot be applied
        // is not a reason to fail.
        UiStrings.Apply("en-US").Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Language")]
    public void ALanguageNothingShipsForIsRefused()
    {
        UiStrings.Apply("fr-FR").Should().BeFalse();
        UiStrings.Supported.Should().Contain("ko-KR").And.Contain("en-US");
    }

    [Fact]
    [Trait("Category", "Language")]
    public void TheApplicationStartsInTheLanguageItsMarkupMerges()
    {
        // These disagreed: the service reported en-US while every caption on screen was Korean, so
        // the first toggle asked to switch to the language it believed it was already in.
        string appXaml = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "TelemetryDashboard.UI", "App.xaml"));

        appXaml.Should().Contain($"Resources/Strings.{LanguageService.DefaultCulture}.xaml");
        new LanguageService().CurrentCultureName.Should().Be(LanguageService.DefaultCulture);
    }

    [Fact]
    [Trait("Category", "Language")]
    public void SwitchingRaisesTheEventThatRebuildsWhatFollowsTheCaptions()
    {
        var service = new LanguageService();
        int raised = 0;
        service.LanguageChanged += (_, _) => raised++;

        service.SetLanguage("en-US");

        raised.Should().Be(1, "the command palette rebuilds itself from this");
        service.CurrentCultureName.Should().Be("en-US");
    }

    [Fact]
    [Trait("Category", "Language")]
    public void ALanguageNobodyShipsFallsBackRatherThanLeavingTheUiUndefined()
    {
        var service = new LanguageService();

        service.SetLanguage("fr-FR");

        service.CurrentCultureName.Should().Be("en-US");
    }

    [Fact]
    [Trait("Category", "Language")]
    public void AnUnknownKeyReadsAsItselfRatherThanAsNothing()
    {
        // On screen an untranslated caption is findable; a blank button is not.
        new LanguageService().GetString("Ui_Cmd_NoSuchThing").Should().Be("Ui_Cmd_NoSuchThing");
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TelemetryDashboard.sln")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("the tests run from inside the solution");
        return dir!.FullName;
    }
}
