using AwesomeAssertions;
using FsrsSharp.Kit;
using Xunit;

namespace FsrsSharp.Tests.Kit;

/// <summary>
/// Ports of srs-kit <c>schema/field.spec.ts</c>, <c>fractional-days.spec.ts</c>, <c>middleware.spec.ts</c>
/// and <c>iterable.spec.ts</c> (lazy preview). The Standard-Schema, <c>utils</c> and type-inference specs
/// are TS-only.
/// </summary>
public class KitSchemasTests
{
    [Fact]
    public void DesiredRetention_MustBeStrictlyBetweenZeroAndOne()
    {
        KitSchemas.ParseDesiredRetention(0.9).Should().Be(0.9);
        foreach (var value in new[] { double.NaN, double.PositiveInfinity, -1, 0, 1 })
        {
            var act = () => KitSchemas.ParseDesiredRetention(value);
            act.Should().Throw<FsrsValidationException>().WithMessage("desiredRetention must be finite and in (0, 1)");
        }
    }

    [Fact]
    public void ElapsedDays_AcceptsZeroAndFractionsAndRejectsInvalidDurations()
    {
        foreach (var value in new[] { 0, 1.0 / 1440, 2 }) KitSchemas.ParseElapsedDays(value).Should().Be(value);
        foreach (var value in new[] { double.NaN, double.PositiveInfinity, -1 })
        {
            var act = () => KitSchemas.ParseElapsedDays(value);
            act.Should().Throw<FsrsValidationException>().WithMessage("elapsedDays must be finite and non-negative");
        }
    }

    [Fact]
    public void ScheduledDays_PreservesZeroAndFractions()
    {
        foreach (var value in new[] { 0, 1.0 / 1440, 2 }) KitSchemas.ParseScheduledDays(value).Should().Be(value);
        foreach (var value in new[] { double.NaN, double.PositiveInfinity, -1 })
        {
            var act = () => KitSchemas.ParseScheduledDays(value);
            act.Should().Throw<FsrsValidationException>().WithMessage("scheduledDays must be finite and non-negative");
        }
    }

    [Fact]
    public void FiniteNumberAndDate()
    {
        KitSchemas.ValidateFiniteNumber(1.25).Should().BeNull();
        KitSchemas.ValidateFiniteNumber(double.NaN).Should().Be("Expected finite number");
        var date = new DateTime(2026, 6, 20, 0, 0, 0, DateTimeKind.Utc);
        KitSchemas.ValidateDate(date).Should().BeNull();
        KitSchemas.ValidateDate(DateTime.UnixEpoch).Should().Be("Expected valid Date");
    }

    [Fact]
    public void FractionalDays_AcceptsAnOmittedFieldAndProducesABoolean()
    {
        var key = KitSchemas.FractionalDays;
        FieldBag.Empty.Get(key).Should().BeFalse();
        new FieldBag.Builder().Set(key, true).ToFieldBag().Get(key).Should().BeTrue();
        key.Validate(false).Should().BeNull();
        key.Validate(1).Should().NotBeNull();
        key.Validate("true").Should().NotBeNull();
        key.Validate(null).Should().NotBeNull();
    }

    // ---------------------------------------------------------------- MiddlewarePipeline (runMiddlewares)

    [Fact]
    public void Pipeline_RunsTheTerminalWithoutHandlers()
    {
        var trace = new List<string>();
        MiddlewarePipeline.Run<string>([], "ctx", _ => trace.Add("terminal"));
        trace.Should().Equal("terminal");
    }

    [Fact]
    public void Pipeline_RunsHandlersInOnionOrderAndSkipsMissingHandlers()
    {
        var trace = new List<string>();
        Action<string, Action> Make(string name) => (ctx, next) =>
        {
            trace.Add($"{name}:before:{ctx}");
            next();
            trace.Add($"{name}:after:{ctx}");
        };

        MiddlewarePipeline.Run<string>([Make("outer"), null, Make("inner")], "ctx", _ => trace.Add("terminal"));

        trace.Should().Equal("outer:before:ctx", "inner:before:ctx", "terminal", "inner:after:ctx", "outer:after:ctx");
    }

    [Fact]
    public void Pipeline_ThrowsWhenNextIsCalledMoreThanOnce()
    {
        var act = () => MiddlewarePipeline.Run<string>(
            [(_, next) => { next(); next(); }], "bad", _ => { });
        act.Should().Throw<FsrsOperationException>().WithMessage("Middleware next() called multiple times");
    }

    [Fact]
    public void Pipeline_AllowsAHandlerToSkipNextAndPropagatesExceptions()
    {
        var terminal = false;
        MiddlewarePipeline.Run<string>([(_, _) => { }], "ctx", _ => terminal = true);
        terminal.Should().BeFalse();
        var act = () => MiddlewarePipeline.Run<string>([(_, next) => next()], "ctx", _ => throw new InvalidOperationException("boom"));
        act.Should().Throw<InvalidOperationException>().WithMessage("boom");
    }

    // ---------------------------------------------------------------- lazy preview (iterable.spec.ts)

    [Fact]
    public void Preview_ComputesValuesLazilyInGradeOrderAndCachesThem()
    {
        var calls = new List<Rating>();
        var card = new Card<Sm2State>(new Sm2State(0, 2.5, 0), State.New, ScheduleStatus.New, FieldBag.Empty);
        var preview = new SchedulePreview<Sm2State>(grade =>
        {
            calls.Add(grade);
            return new PreviewItem<Sm2State>(grade, card, new ReviewLog<Sm2State>(card.MemoryState, grade, State.New, ScheduleStatus.New, FieldBag.Empty));
        });
        calls.Should().BeEmpty();

        using var enumerator = preview.GetEnumerator();
        enumerator.MoveNext().Should().BeTrue();
        calls.Should().Equal(Rating.Again);
        enumerator.Current.Grade.Should().Be(Rating.Again);

        preview.Select(p => p.Grade).Should().Equal(Grades.All);
        preview.Select(p => p.Grade).Should().Equal(Grades.All);
        calls.Should().Equal(Rating.Again, Rating.Hard, Rating.Good, Rating.Easy);
        preview.Count.Should().Be(4);
        var outOfRange = () => preview[4];
        outOfRange.Should().Throw<ArgumentOutOfRangeException>();
    }
}
