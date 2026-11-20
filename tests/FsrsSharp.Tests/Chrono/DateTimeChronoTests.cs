using AwesomeAssertions;
using FsrsSharp.Chrono;
using FsrsSharp.Kit;
using FsrsSharp.Tests.Kit;
using Xunit;

namespace FsrsSharp.Tests.ChronoTests;

/// <summary>Port of srs-kit <c>chrono/presets/date/chrono.spec.ts</c> (type-level assertions skipped).</summary>
public class DateTimeChronoTests
{
    private static readonly DateTime Now = new(2026, 6, 20, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Later = new(2026, 6, 21, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Epoch = DateTime.UnixEpoch;

    private static IChronoCore<DateTime> Create(bool? fractional = null)
    {
        var config = new FieldBag.Builder();
        if (fractional is { } f) config.Set(KitSchemas.FractionalDays, f);
        return DateTimeChrono.Instance.Create(config.ToFieldBag());
    }

    private static DateTime D(string iso) => DateTime.Parse(iso, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal);

    [Fact]
    public void FractionalDaysConfig_DefaultsToFalse()
    {
        KitSchemas.FractionalDays.Name.Should().Be("fractionalDays");
        KitSchemas.FractionalDays.HasDefault.Should().BeTrue();
        KitSchemas.FractionalDays.Default.Should().BeFalse();
        DateTimeChrono.Instance.ConfigKeys.Should().ContainSingle().Which.Should().BeSameAs(KitSchemas.FractionalDays);
        KitSchemas.FractionalDays.Validate(true).Should().BeNull();
        KitSchemas.FractionalDays.Validate("true").Should().NotBeNull();
        KitSchemas.FractionalDays.Validate(null).Should().NotBeNull();
    }

    [Fact]
    public void Chrono_CanPreserveElapsedFractionalDaysWithoutChangingTheDefault()
    {
        var from = D("2026-06-20T23:59:00Z");
        var to = D("2026-06-21T00:01:00Z");
        var fractional = Create(true);
        fractional.Difference(from, to).Should().Be(2.0 / 1440);
        fractional.Difference(to, from).Should().Be(-2.0 / 1440);
        fractional.Difference(from, from).Should().Be(0);
        fractional.Add(from, 2.0 / 1440).Should().Be(to);
        Create(false).Difference(from, to).Should().Be(1);
        Create().Difference(from, to).Should().Be(1);
    }

    [Fact]
    public void Chrono_DifferenceAndCompareAndAdd()
    {
        var core = Create();
        core.Now().Kind.Should().Be(DateTimeKind.Utc);
        core.Difference(Now, Later).Should().Be(1);
        core.Difference(D("2026-06-20T23:59:00Z"), D("2026-06-21T00:01:00Z")).Should().Be(1);
        core.Difference(D("2026-06-20T00:01:00Z"), D("2026-06-20T23:59:00Z")).Should().Be(0);
        core.Difference(Later, Later).Should().Be(0);
        core.Difference(Later, Now).Should().Be(-1);
        core.Compare(Now, Later).Should().Be(-1);
        core.Compare(Later, Now).Should().Be(1);
        core.Compare(Later, Later).Should().Be(0);
        core.Compare(D("2026-06-20T00:01:00Z"), D("2026-06-20T23:59:00Z")).Should().Be(-1);
        core.Add(Now, 2.25).Should().Be(D("2026-06-22T06:00:00Z"));
    }

    [Fact]
    public void ComparesOnlyUtcCalendarDates()
    {
        DateTimeChrono.DateDiffInDays(D("2026-06-20T23:59:00Z"), D("2026-06-21T00:01:00Z")).Should().Be(1);
        DateTimeChrono.DateDiffInDays(D("2026-06-20T00:01:00Z"), D("2026-06-20T23:59:00Z")).Should().Be(0);
        DateTimeChrono.DateDiffInDays(D("2026-06-21T00:01:00Z"), D("2026-06-20T23:59:00Z")).Should().Be(-1);
    }

    [Fact]
    public void Add_TruncatesTheMillisecondSumLikeJsDate()
    {
        var core = Create();
        // 0.5 ms forward: the JS Date drops the fraction.
        core.Add(Now, 0.5 / 86_400_000).Should().Be(Now);
        // 1.9 ms forward truncates to 1 ms.
        core.Add(Now, 1.9 / 86_400_000).Should().Be(Now.AddMilliseconds(1));
        // Sub-millisecond ticks of the input are dropped first.
        core.Add(Now.AddTicks(9_999), 0).Should().Be(Now);
        // The result is always UTC and Local inputs are converted.
        core.Add(Now.ToLocalTime(), 1).Kind.Should().Be(DateTimeKind.Utc);
        core.Add(Now.ToLocalTime(), 1).Should().Be(Now.AddDays(1));
        core.Add(DateTime.SpecifyKind(Now, DateTimeKind.Unspecified), 1).Should().Be(Now.AddDays(1));
        var act = () => core.Add(Now, double.NaN);
        act.Should().Throw<FsrsValidationException>().WithMessage("Expected valid Date fields");
        var huge = () => core.Add(Now, 1e12);
        huge.Should().Throw<FsrsValidationException>();
    }

    [Fact]
    public void Keys_ValidateDatesAfterTheEpoch()
    {
        DateTimeChrono.DueAt.Name.Should().Be("dueAt");
        DateTimeChrono.LastReviewAt.Name.Should().Be("lastReviewAt");
        DateTimeChrono.ReviewTime.Name.Should().Be("reviewTime");
        DateTimeChrono.DueAt.Validate(Now).Should().BeNull();
        DateTimeChrono.DueAt.Validate(Epoch).Should().Be("Expected valid Date fields");
        DateTimeChrono.DueAt.Validate(null).Should().NotBeNull();
        DateTimeChrono.DueAt.Validate("never").Should().NotBeNull();
        DateTimeChrono.ReviewTime.Validate(Epoch).Should().Be("Expected valid Date fields");
        DateTimeChrono.LastReviewAt.HasDefault.Should().BeTrue();
        DateTimeChrono.LastReviewAt.Default.Should().BeNull();
        DateTimeChrono.LastReviewAt.Validate(null).Should().BeNull();
        DateTimeChrono.LastReviewAt.Validate(Now).Should().BeNull();
        DateTimeChrono.LastReviewAt.Validate(Epoch).Should().Be("Expected valid Date fields");
        DateTimeChrono.LastReviewAt.Validate("never").Should().NotBeNull();

        DateTimeChrono.Instance.CardFields.Should().Equal(DateTimeChrono.DueAt, DateTimeChrono.LastReviewAt);
        DateTimeChrono.Instance.RevlogFields.Should().Equal(DateTimeChrono.DueAt, DateTimeChrono.LastReviewAt, DateTimeChrono.ReviewTime);
    }

    private static FieldBag Card(DateTime? dueAt, DateTime? lastReviewAt = null, bool withLast = true)
    {
        var b = new FieldBag.Builder();
        if (dueAt is { } d) b.Set(DateTimeChrono.DueAt, d);
        if (withLast) b.Set(DateTimeChrono.LastReviewAt, lastReviewAt);
        return b.ToFieldBag();
    }

    [Fact]
    public void NormalizeReview_ReadsPreviousAndCurrent()
    {
        var chrono = DateTimeChrono.Instance;
        chrono.NormalizeReview(Card(Later, Now), new DateTime(2026, 6, 25, 0, 0, 0, DateTimeKind.Utc))
            .Should().Be(new ChronoTimes<DateTime>(Now, Later));
        chrono.NormalizeReview(Card(Now, null), Later).Should().Be(new ChronoTimes<DateTime>(null, Now));
        chrono.NormalizeReview(Card(Now, withLast: false), Later).Should().Be(new ChronoTimes<DateTime>(null, Now));

        var noDue = () => chrono.NormalizeReview(Card(null), Later);
        noDue.Should().Throw<FsrsValidationException>().WithMessage("Expected valid Date fields");
        var epochDue = () => chrono.NormalizeReview(Card(Epoch), Later);
        epochDue.Should().Throw<FsrsValidationException>().WithMessage("Expected valid Date fields");
        var badLast = () => chrono.NormalizeReview(Card(Now, Epoch), Later);
        badLast.Should().Throw<FsrsValidationException>().WithMessage("Expected valid Date fields");
        var badTime = () => chrono.NormalizeReview(Card(Now, Now), Epoch);
        badTime.Should().Throw<FsrsValidationException>().WithMessage("Expected valid Date");
    }

    [Fact]
    public void NormalizeRollback_ReadsPreviousAndCurrent()
    {
        var revlog = new FieldBag.Builder()
            .Set(DateTimeChrono.DueAt, Later)
            .Set(DateTimeChrono.LastReviewAt, Now)
            .Set(DateTimeChrono.ReviewTime, new DateTime(2026, 6, 25, 0, 0, 0, DateTimeKind.Utc))
            .ToFieldBag();
        DateTimeChrono.Instance.NormalizeRollback(revlog).Should().Be(new ChronoTimes<DateTime>(Now, Later));

        var missingReviewTime = new FieldBag.Builder().Set(DateTimeChrono.DueAt, Now).ToFieldBag();
        var act = () => DateTimeChrono.Instance.NormalizeRollback(missingReviewTime);
        act.Should().Throw<FsrsValidationException>().WithMessage("Expected valid Date fields");
    }

    [Fact]
    public void WriteDefaults_FillCardAndRevlogFields()
    {
        var chrono = DateTimeChrono.Instance;
        var config = FieldBag.Empty;

        var card = new FieldBag.Builder();
        chrono.WriteCardDefaults(card, config, Now, Later);
        card.ToFieldBag().ShouldBeBag(new FieldBag.Builder().Set(DateTimeChrono.DueAt, Now).Set(DateTimeChrono.LastReviewAt, Later).ToFieldBag());

        var fresh = new FieldBag.Builder();
        chrono.WriteCardDefaults(fresh, config, Now, null);
        fresh.ToFieldBag().ShouldBeBag(Card(Now, null));

        var dueAt = new DateTime(2026, 6, 19, 0, 0, 0, DateTimeKind.Utc);
        var revlog = new FieldBag.Builder();
        chrono.WriteRevlogDefaults(revlog, config, dueAt, new ChronoTimes<DateTime>(Now, Later));
        revlog.ToFieldBag().ShouldBeBag(new FieldBag.Builder()
            .Set(DateTimeChrono.DueAt, dueAt)
            .Set(DateTimeChrono.LastReviewAt, Now)
            .Set(DateTimeChrono.ReviewTime, Later)
            .ToFieldBag());
    }

    [Fact]
    public void ExtensionProperties_ReadTypedFields()
    {
        var core = SchedulerDefinition.Define(Tests.Kit.Sm2Model.Instance, DateTimeChrono.Instance).Create(Tests.Kit.Sm2Model.DefaultConfig);
        var card = core.NewCard(Now);
        card.DueAt.Should().Be(Now);
        card.LastReviewAt.Should().BeNull();
        var result = core.Review(card, Rating.Good, Later);
        result.Revlog.DueAt.Should().Be(Now);
        result.Revlog.LastReviewAt.Should().BeNull();
        result.Revlog.ReviewTime.Should().Be(Later);
        result.Card.LastReviewAt.Should().Be(Later);
    }

    [Fact]
    public void FractionalDays_ChangesElapsedDaysSeenByTheScheduler()
    {
        var elapsed = new List<double>();
        var middleware = new Tests.Kit.LambdaMiddleware<DateTime>("elapsed")
        {
            OnReview = (ctx, next) => { elapsed.Add(ctx.ElapsedDays); next(); },
        };
        foreach (var fractional in new[] { false, true })
        {
            var config = Tests.Kit.KitFixtures.ConfigBuilder().Set(KitSchemas.FractionalDays, fractional);
            var core = Tests.Kit.KitFixtures.DateDefinition().Use(middleware).Create(config);
            var first = core.Review(core.NewCard(D("2026-01-01T00:00:00Z")), Rating.Good, D("2026-01-01T23:00:00Z"));
            core.Review(first.Card, Rating.Good, D("2026-01-02T01:00:00Z"));
        }

        // calendar: 0 (new) then 1 day; fractional: 0 then 2h.
        elapsed.Should().Equal(0, 1, 0, 2.0 / 24);
    }
}
