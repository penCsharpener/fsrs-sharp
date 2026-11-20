using AwesomeAssertions;
using FsrsSharp.Chrono;
using FsrsSharp.Kit;
using Xunit;

namespace FsrsSharp.Tests.ChronoTests;

/// <summary>Port of srs-kit <c>chrono/presets/numeric/chrono.spec.ts</c>.</summary>
public class NumericChronoTests
{
    [Fact]
    public void ProvidesNumericChronoWithoutFields()
    {
        var chrono = NumericChrono.Instance;
        chrono.ConfigKeys.Should().BeEmpty();
        chrono.CardFields.Should().BeEmpty();
        chrono.RevlogFields.Should().BeEmpty();

        chrono.NormalizeReview(FieldBag.Empty, 4.5).Should().Be(new ChronoTimes<double>(0, 4.5));
        var act = () => chrono.NormalizeReview(FieldBag.Empty, double.NaN);
        act.Should().Throw<FsrsValidationException>().WithMessage("Expected finite number");

        var core = chrono.Create(FieldBag.Empty);
        core.Now().Should().Be(0);
        core.Difference(0, 4.5).Should().Be(4.5);
        core.Difference(1.25, 4.5).Should().Be(3.25);
        core.Add(4.5, 2.25).Should().Be(6.75);
        core.Compare(1, 2).Should().Be(-1);

        var card = new FieldBag.Builder();
        chrono.WriteCardDefaults(card, FieldBag.Empty, 3, 1);
        card.Count().Should().Be(0);
        var revlog = new FieldBag.Builder();
        chrono.WriteRevlogDefaults(revlog, FieldBag.Empty, 3, new ChronoTimes<double>(1, 2));
        revlog.Count().Should().Be(0);

        var normalized = chrono.NormalizeReview(FieldBag.Empty, 4.5);
        core.Difference(normalized.Previous ?? 0, normalized.Current).Should().Be(4.5);
    }

    [Fact]
    public void SchedulerRejectsNonFiniteTimes()
    {
        var core = Tests.Kit.KitFixtures.NumericScheduler();
        var act = () => core.NewCard(double.PositiveInfinity);
        act.Should().Throw<FsrsValidationException>().WithMessage("Expected finite number");
    }
}
