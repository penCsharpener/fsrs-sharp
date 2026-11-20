using Xunit;
using AwesomeAssertions;
using FsrsSharp.Kit;
using FsrsSharp.Middlewares;

namespace FsrsSharp.Tests.Middlewares.LearningSteps;

/// <summary>Port of ts-fsrs <c>middlewares/learning-steps/schema.spec.ts</c> (config parsing runs through <c>SchedulerDefinition.Create</c>).</summary>
public class LearningStepsSchemaTests
{
    private static FieldBag.Builder Base(Action<FieldBag.Builder>? configure = null)
    {
        var builder = MiddlewareTestSchedulers.Fsrs6Config(enableShortTerm: true);
        builder.SetRaw("learningSteps", new string[0]);
        builder.SetRaw("relearningSteps", new string[0]);
        configure?.Invoke(builder);
        return builder;
    }

    private static FieldBag Parse(Action<FieldBag.Builder>? configure = null) =>
        MiddlewareTestSchedulers.Fsrs6(LearningStepsMiddleware.Instance).Create(Base(configure)).Config;

    private static void ShouldReject(Action<FieldBag.Builder> configure, string message) =>
        FluentActions.Invoking(() => Parse(configure)).Should().Throw<FsrsValidationException>().WithMessage(message);

    [Fact]
    public void AcceptsLearningAndRelearningStepUnits()
    {
        var config = Parse(b =>
        {
            b.SetRaw("learningSteps", new[] { "1m", "1.5h", "0.25d" });
            b.SetRaw("relearningSteps", new[] { ".5m", "1e2m" });
        });

        config.Get(LearningStepsMiddleware.EnableShortTerm).Should().BeTrue();
        config.Get(LearningStepsMiddleware.LearningSteps).Should().Equal("1m", "1.5h", "0.25d");
        config.Get(LearningStepsMiddleware.RelearningSteps).Should().Equal(".5m", "1e2m");
        config.Get(LearningStepsMiddleware.GraduatingInterval).Should().Be(1);
        config.Get(LearningStepsMiddleware.AllowModelOverride).Should().BeFalse();
    }

    [Fact]
    public void ExposesUpstreamDefaults()
    {
        LearningStepsMiddleware.DefaultLearningSteps.Should().Equal("1m", "10m");
        LearningStepsMiddleware.DefaultRelearningSteps.Should().Equal("10m");
        LearningStepsMiddleware.DefaultGraduatingInterval.Should().Be(1);
    }

    [Fact]
    public void AcceptsEmptyStepLists()
    {
        var config = Parse(b => b.Add(LearningStepsMiddleware.EnableShortTerm, false));

        config.Get(LearningStepsMiddleware.EnableShortTerm).Should().BeFalse();
        config.Get(LearningStepsMiddleware.LearningSteps).Should().BeEmpty();
        config.Get(LearningStepsMiddleware.RelearningSteps).Should().BeEmpty();
        config.Get(LearningStepsMiddleware.GraduatingInterval).Should().Be(1);
        config.Get(LearningStepsMiddleware.AllowModelOverride).Should().BeFalse();
    }

    [Fact]
    public void AcceptsAPositiveGraduatingIntervalInDays() =>
        Parse(b => b.SetRaw("graduatingInterval", 0.5)).Get(LearningStepsMiddleware.GraduatingInterval).Should().Be(0.5);

    [Fact]
    public void RejectsANonBooleanAllowModelOverride() =>
        FluentActions.Invoking(() => Parse(b => b.SetRaw("allowModelOverride", 1))).Should().Throw<FsrsValidationException>();

    public static TheoryData<object> BadGraduatingIntervals =>
        [0.0, -1.0, double.PositiveInfinity, double.NaN, "1"];

    [Theory]
    [MemberData(nameof(BadGraduatingIntervals))]
    public void RejectsInvalidGraduatingInterval(object graduatingInterval) =>
        ShouldReject(b => b.SetRaw("graduatingInterval", graduatingInterval), graduatingInterval is double
            ? "Expected positive graduatingInterval days"
            : "*graduatingInterval*");

    [Fact]
    public void DoesNotCapAFiniteStepAtTheModelMaximumInterval() =>
        Parse(b => b.SetRaw("learningSteps", new[] { "1000000d" })).Get(LearningStepsMiddleware.LearningSteps)
            .Should().Equal("1000000d");

    [Fact]
    public void RejectsAMissingOrInvalidEnableShortTermFlag()
    {
        FluentActions.Invoking(() => Parse(b => b.Remove(LearningStepsMiddleware.EnableShortTerm))).Should().Throw<FsrsValidationException>();
        FluentActions.Invoking(() => Parse(b => b.SetRaw("enableShortTerm", "true"))).Should().Throw<FsrsValidationException>();
    }

    public static TheoryData<object?> BadLearningSteps =>
    [
        "1m",
        new[] { "1x" },
        new[] { "1e999m" },
        new[] { "1e308d" },
        new[] { "1e308m" },
        new[] { "1e304m" },
        new string?[] { null },
        new[] { "m" },
    ];

    [Theory]
    [MemberData(nameof(BadLearningSteps))]
    public void RejectsInvalidLearningStepLists(object? learningSteps) =>
        // A non-list (upstream: '1m') is rejected by the typed key before the step validator runs.
        ShouldReject(b => b.SetRaw("learningSteps", learningSteps), learningSteps is string ? "*learningSteps*" : "*valid learningSteps array*");

    public static TheoryData<object?> BadRelearningSteps =>
    [
        "1m",
        new[] { "-1m" },
        new[] { "1e308d" },
        new[] { "1e308m" },
        new string?[] { null },
    ];

    [Theory]
    [MemberData(nameof(BadRelearningSteps))]
    public void RejectsInvalidRelearningStepLists(object? relearningSteps) =>
        ShouldReject(b => b.SetRaw("relearningSteps", relearningSteps), relearningSteps is string ? "*relearningSteps*" : "*valid relearningSteps array*");

    [Fact]
    public void RejectsNonObjectConfig() =>
        FluentActions.Invoking(() => MiddlewareTestSchedulers.Fsrs6(LearningStepsMiddleware.Instance).Create((FieldBag)null!))
            .Should().Throw<FsrsValidationException>();

    // learningStepFieldsSchema

    [Fact]
    public void DefaultsAMissingLearningStepToZero()
    {
        var scheduler = MiddlewareTestSchedulers.Fsrs6(LearningStepsMiddleware.Instance).Create(Base());
        var card = scheduler.NewCard(MiddlewareTestSchedulers.Now);
        card.LearningStep.Should().Be(0);
        card.With(LearningStepsMiddleware.LearningStepField, 0);
        var withoutStep = card with { Fields = card.Fields.Without(LearningStepsMiddleware.LearningStepField) };
        scheduler.Review(withoutStep, Rating.Again, MiddlewareTestSchedulers.Now).Card.LearningStep.Should().Be(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void AcceptsANonNegativeIntegerLearningStep(int learningStep)
    {
        var scheduler = MiddlewareTestSchedulers.Fsrs6(LearningStepsMiddleware.Instance).Create(Base());
        var card = scheduler.NewCard(MiddlewareTestSchedulers.Now).With(LearningStepsMiddleware.LearningStepField, learningStep);
        var result = scheduler.Review(card, Rating.Easy, MiddlewareTestSchedulers.Now);
        result.Revlog.LearningStep.Should().Be(learningStep);
    }

    [Fact]
    public void RejectsInvalidLearningStepValues()
    {
        var scheduler = MiddlewareTestSchedulers.Fsrs6(LearningStepsMiddleware.Instance).Create(Base());
        var card = scheduler.NewCard(MiddlewareTestSchedulers.Now);
        foreach (var bad in new object[] { "0", double.NaN, 0.5, -1 })
        {
            var broken = card with { Fields = card.Fields.ToBuilder().AssignFrom([new("learningStep", bad)]).ToFieldBag() };
            FluentActions.Invoking(() => scheduler.Review(broken, Rating.Again, MiddlewareTestSchedulers.Now))
                .Should().Throw<FsrsValidationException>($"learningStep {bad} is invalid");
        }
    }

    [Fact]
    public void LearningStepKeyValidatesNonNegativeIntegers()
    {
        LearningStepsMiddleware.LearningStepField.Validate(0).Should().BeNull();
        LearningStepsMiddleware.LearningStepField.Validate(-1).Should().Be("Expected non-negative integer learningStep");
    }
}
