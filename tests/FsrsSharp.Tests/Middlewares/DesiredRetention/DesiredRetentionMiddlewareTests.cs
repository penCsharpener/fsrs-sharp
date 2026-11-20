using Xunit;
using AwesomeAssertions;
using FsrsSharp.Chrono;
using FsrsSharp.Kit;
using FsrsSharp.Middlewares;
using FsrsSharp.Models;
using FsrsSharp.Models.Fsrs7;

namespace FsrsSharp.Tests.Middlewares.DesiredRetention;

/// <summary>Port of ts-fsrs <c>middlewares/desired-retention/middleware.spec.ts</c>.</summary>
public class DesiredRetentionMiddlewareTests
{
    private static readonly Rating[] Grades = [Rating.Again, Rating.Hard, Rating.Good, Rating.Easy];
    private static readonly DateTime Now = new(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);

    private static FieldBag.Builder Fsrs7Config(object desiredRetention)
    {
        var builder = new FieldBag.Builder { { Fsrs7Model.Weights, Fsrs7Constants.DefaultWeights } };
        builder.AddRaw(DesiredRetentionMiddleware.DesiredRetention, desiredRetention);
        return builder;
    }

    private static SchedulerDefinition<Fsrs7State, DateTime> Fsrs7(IModel<Fsrs7State> model, params Middleware[] middlewares) =>
        SchedulerDefinition.Define(model, DateTimeChrono.Instance).Use(middlewares);

    private static Dictionary<Rating, double> PerGrade(double again, double hard, double good, double easy) =>
        new() { [Rating.Again] = again, [Rating.Hard] = hard, [Rating.Good] = good, [Rating.Easy] = easy };

    private static void ShouldEqual(DesiredRetentionByGrade actual, IReadOnlyDictionary<Rating, double> expected)
    {
        foreach (var grade in Grades) actual[grade].Should().Be(expected[grade]);
    }

    private static IReadOnlyDictionary<Rating, double> Uniform(double value) => PerGrade(value, value, value, value);

    [Fact]
    public void InjectsOncePerCandidateWithoutResettingDownstreamRetentionOnRepeatedPreview()
    {
        var overridden = Uniform(0.8);
        var seen = new List<DesiredRetentionByGrade>();
        var probe = new ProbeMiddleware<Fsrs7State, DateTime>("override-after-initial-retention")
        {
            OnNextInterval = (ctx, next) =>
            {
                if (ctx.Grade == Rating.Again)
                    foreach (var grade in Grades) ctx.Candidate.DesiredRetention[grade] = overridden[grade];
                seen.Add(ctx.Candidate.DesiredRetention);
                next();
            },
        };
        var core = Fsrs7(new SpyModel<Fsrs7State>(Fsrs7Model.Instance), DesiredRetentionMiddleware.Instance, probe)
            .Create(Fsrs7Config(0.9));
        var card = core.NewCard(Now);
        var preview = core.Preview(card, Now);
        var first = preview.ToList();
        preview.ToList().Should().Equal(first, (a, b) => ReferenceEquals(a, b));

        // Upstream's preview is not cached, so iterating twice runs the handlers eight times; here it runs once per grade.
        seen.Should().HaveCount(4);
        seen.Should().OnlyContain(retention => ReferenceEquals(retention, seen[0]));
        ShouldEqual(seen[0], overridden);
        ShouldEqual(DesiredRetentionMiddleware.Resolve(core.Config), Uniform(0.9));

        core.Review(card, Rating.Hard, Now);
        ShouldEqual(seen[^1], DesiredRetentionMiddleware.Resolve(core.Config).ToDictionary());
        seen[^1].Should().NotBeSameAs(DesiredRetentionMiddleware.Resolve(core.Config));
    }

    public static TheoryData<object?> InvalidRetentions()
    {
        var data = new TheoryData<object?>();
        foreach (var invalid in new[] { double.NaN, double.PositiveInfinity, 0, 1 })
        {
            data.Add(invalid);
            data.Add(PerGrade(0.9, 0.9, invalid, 0.9));
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(InvalidRetentions))]
    public void ReturnsIssuesForInvalidScalarAndPerGradeRetentions(object? desiredRetention) =>
        DesiredRetentionMiddleware.DesiredRetention.Validate(desiredRetention).Should().Be("Expected desiredRetention in (0, 1)");

    [Fact]
    public void UsesTheConfiguredRetentionForEachGradeRegardlessOfPropertyDeclarationOrder()
    {
        var maps = new HashSet<DesiredRetentionByGrade>(); // no value equality: reference identity
        var desiredRetention = new Dictionary<Rating, double>
        {
            [Rating.Easy] = 0.7,
            [Rating.Hard] = 0.85,
            [Rating.Again] = 0.95,
            [Rating.Good] = 0.8,
        };
        var spy = new SpyModel<Fsrs7State>(Fsrs7Model.Instance);
        var observe = new ProbeMiddleware<Fsrs7State, DateTime>("observe-desired-retention")
        {
            OnReview = (ctx, next) =>
            {
                maps.Add(ctx.Candidate.DesiredRetention);
                next();
            },
        };
        var core = Fsrs7(spy, DesiredRetentionMiddleware.Instance, observe).Create(Fsrs7Config(desiredRetention));
        var preview = core.Preview(core.NewCard(Now), Now);
        var first = preview.ToList();
        preview.ToList().Should().Equal(first, (a, b) => ReferenceEquals(a, b));

        foreach (var retentions in maps) ShouldEqual(retentions, desiredRetention);
        spy.Retentions.Should().Equal(Grades.Select(grade => desiredRetention[grade]));
        ShouldEqual(DesiredRetentionMiddleware.Resolve(core.Config), desiredRetention);
    }

    public static TheoryData<object?> InvalidPerGradeConfigs()
    {
        var all = PerGrade(0.9, 0.9, 0.9, 0.9);
        return new TheoryData<object?>
        {
            default(object),
            new Dictionary<Rating, double> { [Rating.Good] = 0.9 },
            new Dictionary<Rating, double> { [Rating.Manual] = 0.9, [Rating.Again] = 0.9, [Rating.Hard] = 0.9, [Rating.Good] = 0.9 },
            PerGrade(0.9, 0.9, 0.9, 1),
            all.ToDictionary(p => p.Key, _ => double.NaN),
        };
    }

    [Theory]
    [MemberData(nameof(InvalidPerGradeConfigs))]
    public void RejectsInvalidPerGradeConfig(object? value)
    {
        DesiredRetentionMiddleware.DesiredRetention.Validate(value).Should().Be("Expected desiredRetention in (0, 1)");

        var config = Fsrs7Config(0.9);
        config.SetRaw("desiredRetention", value);
        FluentActions.Invoking(() => Fsrs7(Fsrs7Model.Instance, DesiredRetentionMiddleware.Instance).Create(config))
            .Should().Throw<FsrsValidationException>().WithMessage("Expected desiredRetention in (0, 1)");
    }

    [Fact]
    public void RejectsAMissingDesiredRetention()
    {
        var config = new FieldBag.Builder { { Fsrs7Model.Weights, Fsrs7Constants.DefaultWeights } };
        FluentActions.Invoking(() => Fsrs7(Fsrs7Model.Instance, DesiredRetentionMiddleware.Instance).Create(config))
            .Should().Throw<FsrsValidationException>();
    }

    [Fact]
    public void InjectsEveryGradeWhilePreservingTheCandidateRecordIdentity()
    {
        var core = Fsrs7(Fsrs7Model.Instance, DesiredRetentionMiddleware.Instance).Create(Fsrs7Config(0.9));
        var card = core.NewCard(Now);
        var initialRetentions = new DesiredRetentionByGrade(0);
        var candidate = new ReviewCandidate<Fsrs7State>(initialRetentions, _ => card.MemoryState, _ => null, (_, _) => 0);
        var ctx = new NextIntervalContext<Fsrs7State, DateTime>(core, card, Rating.Good, null, 0, candidate);
        var ran = false;

        DesiredRetentionMiddleware.Instance.NextInterval(ctx, () =>
        {
            ran = true;
            ShouldEqual(ctx.Candidate.DesiredRetention, Uniform(0.9));
            ctx.Candidate.DesiredRetention.Should().BeSameAs(initialRetentions);
            ctx.Candidate.DesiredRetention.Should().NotBeSameAs(DesiredRetentionMiddleware.Resolve(core.Config));
        });

        ran.Should().BeTrue();
    }

    [Fact]
    public void KeepsAnExplicitRetentionOfAStandaloneIntervalQuery()
    {
        var core = Fsrs7(Fsrs7Model.Instance, DesiredRetentionMiddleware.Instance).Create(Fsrs7Config(0.9));
        var card = core.NewCard(Now);
        var candidate = new ReviewCandidate<Fsrs7State>(new DesiredRetentionByGrade(0.5), _ => card.MemoryState, _ => null, (_, _) => 0);
        var ctx = new NextIntervalContext<Fsrs7State, DateTime>(core, card, Rating.Good, 0.5, 0, candidate);

        DesiredRetentionMiddleware.Instance.NextInterval(ctx, () => { });

        ShouldEqual(candidate.DesiredRetention, Uniform(0.5));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.1)]
    [InlineData(1.0)]
    [InlineData(1.1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void RejectsDesiredRetentionOutsideZeroOne(double desiredRetention) =>
        DesiredRetentionMiddleware.DesiredRetention.Validate(desiredRetention).Should().Be("Expected desiredRetention in (0, 1)");

    [Theory]
    [InlineData(0.1)]
    [InlineData(0.9)]
    [InlineData(0.99)]
    [InlineData(0.9999999999999998)] // 1 - Number.EPSILON
    public void AcceptsDesiredRetention(double desiredRetention)
    {
        var core = Fsrs7(Fsrs7Model.Instance, DesiredRetentionMiddleware.Instance).Create(Fsrs7Config(desiredRetention));
        ShouldEqual(DesiredRetentionMiddleware.Resolve(core.Config), Uniform(desiredRetention));
    }

    [Fact]
    public void RejectsRetentionOneWhenCreatingFsrs6AndFsrs7Schedulers()
    {
        var fsrs7 = () => Fsrs7(Fsrs7Model.Instance, DesiredRetentionMiddleware.Instance).Create(Fsrs7Config(1.0));
        fsrs7.Should().Throw<FsrsValidationException>().WithMessage("Expected desiredRetention in (0, 1)");

        var config = MiddlewareTestSchedulers.Fsrs6Config();
        config.Add(DesiredRetentionMiddleware.DesiredRetention, 1.0);
        var fsrs6 = () => MiddlewareTestSchedulers.Fsrs6(DesiredRetentionMiddleware.Instance).Create(config);
        fsrs6.Should().Throw<FsrsValidationException>().WithMessage("Expected desiredRetention in (0, 1)");
    }

    [Theory(Skip = "needs DefaultScheduler")]
    [InlineData("FSRS-3")]
    [InlineData("FSRS-4")]
    [InlineData("FSRS-4.5")]
    [InlineData("FSRS-5")]
    [InlineData("FSRS-6")]
    [InlineData("FSRS-7")]
    public void RejectsRetentionOneWhenCreatingDefaultScheduler(string version)
    {
        // Upstream: DefaultScheduler({ version, desiredRetention: 1 }) rejects with 'Expected desiredRetention in (0, 1)'.
        _ = version;
    }
}

internal static class DesiredRetentionTestExtensions
{
    public static IReadOnlyDictionary<Rating, double> ToDictionary(this DesiredRetentionByGrade value) =>
        new[] { Rating.Again, Rating.Hard, Rating.Good, Rating.Easy }.ToDictionary(g => g, g => value[g]);
}
