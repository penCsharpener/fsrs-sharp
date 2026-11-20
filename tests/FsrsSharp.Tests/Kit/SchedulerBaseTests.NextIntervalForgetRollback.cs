using AwesomeAssertions;
using FsrsSharp.Chrono;
using FsrsSharp.Kit;
using FsrsSharp.Middlewares;
using Xunit;
using static FsrsSharp.Tests.Kit.KitFixtures;

namespace FsrsSharp.Tests.Kit;

// base.spec.ts: SchedulerCore.nextInterval, forget, rollback, card schema validation.
public partial class SchedulerBaseTests
{
    // ---------------------------------------------------------------- nextInterval

    [Fact]
    public void NextInterval_UsesSuppliedStateAndEffectiveRetentionWithoutSteppingCurrentGrade()
    {
        var middleware = new LambdaMiddleware<double>("query-retention")
        {
            OnNextInterval = (ctx, next) =>
            {
                ctx.DesiredRetention.Should().Be(0.9);
                ctx.Candidate.DesiredRetention[ctx.Grade] = 0.8;
                next();
            },
        };
        var core = NumericScheduler(middleware);
        var card = core.NewCard(0);
        var state = new Sm2State(10, 2.5, 3);
        var expected = core.Model.NextInterval(state, 0.8);
        core.Core().StepCalls.Clear();
        core.Core().CurveCalls.Clear();
        core.Core().IntervalCalls.Clear();

        core.NextInterval(state, 0.9, new NextIntervalQuery<Sm2State>(card, Rating.Good, 0.5)).Should().Be(expected);

        core.Core().IntervalCalls.Should().Equal((state, 0.8));
        core.Core().StepCalls.Should().BeEmpty();
        core.Core().CurveCalls.Should().BeEmpty();
        card.MemoryState.ReviewStep.Should().Be(0);
    }

    [Fact]
    public void NextInterval_LazilyComputesSiblingGradesOnceAndCachesIntervalsIncludingZero()
    {
        var middleware = new LambdaMiddleware<double>("query-candidates")
        {
            OnNextInterval = (ctx, next) =>
            {
                var supplied = ctx.Candidate.Step(ctx.Grade);
                ctx.Candidate.FindGrade(supplied).Should().Be(Rating.Good);
                var sibling = ctx.Candidate.Step(Rating.Again);
                ctx.Candidate.Step(Rating.Again).Should().BeSameAs(sibling);
                ctx.Candidate.Step(Rating.Hard);
                ctx.Candidate.NextInterval(supplied, 0.9).Should().Be(0);
                ctx.Candidate.NextInterval(supplied, 0.9).Should().Be(0);
                ctx.Candidate.NextInterval(supplied, 0.8).Should().Be(0);
                next();
            },
        };
        var core = NumericScheduler(middleware);
        var card = core.NewCard(0);
        core.Core().StepCalls.Clear();
        core.Core().CurveCalls.Clear();
        core.Core().IntervalCalls.Clear();
        core.Core().NextIntervalOverride = (_, _) => 0;

        core.NextInterval(card.MemoryState, 0.9, new NextIntervalQuery<Sm2State>(card, Rating.Good, 1)).Should().Be(0);

        core.Core().StepCalls.Select(c => c.Rating).Should().Equal(Rating.Again, Rating.Hard);
        core.Core().CurveCalls.Should().HaveCount(1);
        core.Core().IntervalCalls.Should().HaveCount(2);
    }

    [Fact]
    public void NextInterval_PreservesIntervalSetByMiddlewareBeforeNext()
    {
        var middleware = new LambdaMiddleware<double>("query-interval")
        {
            OnNextInterval = (ctx, next) =>
            {
                ctx.ScheduledDays = 1.0 / 1440;
                next();
            },
        };
        var core = NumericScheduler(middleware);
        var card = core.NewCard(0);
        core.Core().IntervalCalls.Clear();
        core.NextInterval(card.MemoryState, 0.9, new NextIntervalQuery<Sm2State>(card, Rating.Good, 0)).Should().Be(1.0 / 1440);
        core.Core().IntervalCalls.Should().BeEmpty();
    }

    [Fact]
    public void NextInterval_ValidatesInputs()
    {
        var core = NumericScheduler();
        var card = core.NewCard(0);
        var query = new NextIntervalQuery<Sm2State>(card, Rating.Good, 0);
        var retention = () => core.NextInterval(card.MemoryState, 1, query);
        retention.Should().Throw<FsrsValidationException>().WithMessage("desiredRetention must be finite and in (0, 1)");
        var elapsed = () => core.NextInterval(card.MemoryState, 0.9, query with { ElapsedDays = -1 });
        elapsed.Should().Throw<FsrsValidationException>().WithMessage("elapsedDays must be finite and non-negative");
        var grade = () => core.NextInterval(card.MemoryState, 0.9, query with { Grade = Rating.Manual });
        grade.Should().Throw<FsrsValidationException>().WithMessage("Expected grade");
        var state = () => core.NextInterval(new Sm2State(double.NaN, 1, 1), 0.9, query);
        state.Should().Throw<FsrsValidationException>().WithMessage("Expected SM2 memory state");

        core.Core().NextIntervalOverride = (_, _) => -1;
        var negative = () => core.NextInterval(card.MemoryState, 0.9, query);
        negative.Should().Throw<FsrsValidationException>().WithMessage("scheduledDays must be finite and non-negative");
    }

    // ---------------------------------------------------------------- forget

    [Fact]
    public void Forget_ResetsReviewedCardThroughNewCardDefaults()
    {
        var card = Core.NewCard(0);
        var first = Core.Review(card, Rating.Good, 0);
        var second = Core.Review(first.Card, Rating.Again, first.Card.MemoryState.Interval);

        var forgotten = Core.Forget(second.Card, 9);

        forgotten.State.Should().Be(State.New);
        forgotten.ScheduleStatus.Should().Be(ScheduleStatus.New);
        forgotten.MemoryState.Should().Be(new Sm2State(0, Sm2Model.DefaultWeights[2], 0));
        forgotten.Reps.Should().Be(0);
        forgotten.Lapses.Should().Be(0);
    }

    [Fact]
    public void Forget_CanPreserveStatsFromForgottenCard()
    {
        var config = ConfigBuilder().Set(StatsMiddleware.ClearStatsOnForget, false);
        var core = StatsScheduler(config.ToFieldBag());
        var first = core.Review(core.NewCard(0), Rating.Good, 0);
        var second = core.Review(first.Card, Rating.Again, first.Card.MemoryState.Interval);

        var forgotten = core.Forget(second.Card, 9);

        forgotten.State.Should().Be(State.New);
        forgotten.MemoryState.Interval.Should().Be(0);
        forgotten.Reps.Should().Be(2);
        forgotten.Lapses.Should().Be(1);
    }

    [Fact]
    public void Forget_PassesOldCardDataToMiddlewareDefaultsOnlyForForget()
    {
        var seen = new List<string>();
        var middleware = new LambdaMiddleware<double>("forgetDefaultContext")
        {
            Card = [SourceMiddleware.SourceCard],
            OnCardDefaults = (ctx, card) =>
            {
                if (ctx.Operation == CardDefaultOperation.NewCard)
                {
                    ctx.ExistingCard.Should().BeNull();
                    seen.Add("none");
                    card.Set(SourceMiddleware.SourceCard, "fresh");
                    return;
                }

                seen.Add(ctx.ExistingCard!.ScheduleStatus.Value);
                card.Set(SourceMiddleware.SourceCard, ctx.ExistingCard.Get(SourceMiddleware.SourceCard));
            },
        };
        var core = StatsScheduler(null, middleware);
        var card = core.NewCard(0);

        var forgotten = core.Forget(card.With(SourceMiddleware.SourceCard, "old-source"), 1);

        seen.Should().Equal("none", "new");
        forgotten.Get(SourceMiddleware.SourceCard).Should().Be("old-source");
    }

    [Fact]
    public void Forget_UsesChronoCardDefaultsAtForgetTime()
    {
        var core = DateDefinition().Use(StatsMiddleware.Instance).Create(Config);
        var forgottenAt = Utc(2026, 2, 1);
        var reviewed = core.Review(core.NewCard(Utc(2026, 1, 1)), Rating.Good, Utc(2026, 1, 2));

        var forgotten = core.Forget(reviewed.Card, forgottenAt);

        forgotten.State.Should().Be(State.New);
        forgotten.DueAt.Should().Be(forgottenAt);
        forgotten.LastReviewAt.Should().BeNull();

        var now = core.Forget(reviewed.Card);
        now.DueAt.Should().BeAfter(Utc(2026, 1, 1));
        now.LastReviewAt.Should().BeNull();
    }

    [Fact]
    public void Forget_ValidatesCardAndNow()
    {
        var card = Core.NewCard();
        var badCard = () => Core.Forget(card with { Fields = FieldBag.Empty }, 0);
        badCard.Should().Throw<FsrsValidationException>();
        var badNow = () => Core.Forget(card, double.NaN);
        badNow.Should().Throw<FsrsValidationException>().WithMessage("Expected finite number");
    }

    // ---------------------------------------------------------------- rollback

    [Fact]
    public void Rollback_RestoresPreviousCardState()
    {
        var result = Core.Review(Core.NewCard(), Rating.Good, 0);
        var restored = Core.Rollback(result.Card, result.Revlog);
        restored.State.Should().Be(State.New);
        restored.ScheduleStatus.Should().Be(ScheduleStatus.New);
        restored.MemoryState.Interval.Should().Be(0);
        restored.Reps.Should().Be(0);
    }

    [Fact]
    public void Rollback_RollsNumericChronoBackToFieldlessNewCard()
    {
        var core = NumericScheduler();
        var reviewed = core.Review(core.NewCard(2), Rating.Good, 5);
        var restored = core.Rollback(reviewed.Card, reviewed.Revlog);
        restored.State.Should().Be(State.New);
        restored.ScheduleStatus.Should().Be(ScheduleStatus.New);
        restored.Fields.Should().BeEmpty();
    }

    [Fact]
    public void Rollback_RunsHandlersWhenRevlogStateIsNew()
    {
        var result = Core.Review(Core.NewCard(), Rating.Good, 0);
        var restored = Core.Rollback(result.Card, result.Revlog with { ScheduleStatus = ScheduleStatus.Review });
        restored.MemoryState.Interval.Should().Be(0);
        restored.MemoryState.EaseFactor.Should().Be(Sm2Model.DefaultWeights[2]);
        restored.Reps.Should().Be(0);
        restored.State.Should().Be(State.New);
        restored.ScheduleStatus.Should().Be(ScheduleStatus.Review);
    }

    [Fact]
    public void Rollback_DoesNotResetStatsWhenRevlogStateIsNew()
    {
        var first = Core.Review(Core.NewCard(), Rating.Good, 0);
        var second = Core.Review(first.Card, Rating.Good, first.Card.MemoryState.Interval);
        var restored = Core.Rollback(second.Card, second.Revlog with { State = State.New, ScheduleStatus = ScheduleStatus.New });
        restored.MemoryState.Interval.Should().Be(1);
        restored.MemoryState.EaseFactor.Should().Be(Sm2Model.DefaultWeights[2]);
        restored.Reps.Should().Be(1);
        restored.State.Should().Be(State.New);
        restored.ScheduleStatus.Should().Be(ScheduleStatus.New);
    }

    [Fact]
    public void Rollback_RestoresLapsesOnLapseRollback()
    {
        var r1 = Core.Review(Core.NewCard(), Rating.Good, 0);
        var r2 = Core.Review(r1.Card, Rating.Again, r1.Card.MemoryState.Interval);
        r2.Card.Lapses.Should().Be(1);

        var restored = Core.Rollback(r2.Card, r2.Revlog);
        restored.Lapses.Should().Be(0);
        restored.State.Should().Be(State.Review);
    }

    [Fact]
    public void Rollback_RestoresOnlyFieldsThatBelongToTheCardShape()
    {
        var core = DateDefinition().Use(SourceMiddleware.Instance, StatsMiddleware.Instance).Create(SourceConfigBag("test-source"));
        var card = core.NewCard(Utc(2026, 1, 1));
        var result = core.Review(card, Rating.Good, Utc(2026, 1, 3));
        var revlog = result.Revlog
            .With(DateTimeChrono.DueAt, Utc(2026, 1, 2))
            .With(DateTimeChrono.ReviewTime, Utc(2026, 1, 3))
            .With(SourceMiddleware.Audit, "test-source");

        var restored = core.Rollback(result.Card, revlog);

        restored.DueAt.Should().Be(revlog.DueAt);
        restored.LastReviewAt.Should().BeNull();
        restored.Get(SourceMiddleware.SourceCard).Should().Be("test-source");
        restored.Fields.ContainsKey("audit").Should().BeFalse();
    }

    [Fact]
    public void Rollback_RestoresNonNewChronoCardFieldsFromRevlogTimes()
    {
        var core = DateDefinition().Create(Config);
        var first = core.Review(core.NewCard(Utc(2026, 6, 28)), Rating.Good, Utc(2026, 6, 30));
        var second = core.Review(first.Card, Rating.Good, first.Card.DueAt);
        var revlog = second.Revlog
            .With(DateTimeChrono.DueAt, Utc(2026, 6, 30))
            .With(DateTimeChrono.ReviewTime, Utc(2026, 7, 6));

        var restored = core.Rollback(second.Card, revlog);

        restored.ScheduleStatus.Should().Be(ScheduleStatus.Review);
        restored.DueAt.Should().Be(revlog.DueAt);
        restored.LastReviewAt.Should().Be(revlog.LastReviewAt);
    }

    [Fact]
    public void Rollback_SuppliesTheSameMissingPreviousContextOnNewCardAndNewCardRollback()
    {
        var seen = new List<double?>();
        var dueAt = new FieldKey<double>("dueAt");
        var chrono = new TestChrono
        {
            CardFields = [dueAt],
            RevlogFields = [dueAt],
            OnNormalizeReview = (_, time) => new ChronoTimes<double>(null, time),
            OnNormalizeRollback = revlog => new ChronoTimes<double>(99, revlog.Get(dueAt)),
            OnWriteRevlogDefaults = (revlog, _, time, _) => revlog.Set(dueAt, time),
            OnWriteCardDefaults = (card, _, time, previous) =>
            {
                seen.Add(previous);
                card.Set(dueAt, time);
            },
        };
        var core = SchedulerDefinition.Define(Sm2Model.Instance, chrono).Create(Config);
        var card = core.NewCard(5);
        var reviewed = core.Review(card, Rating.Good, 8);
        var revlog = reviewed.Revlog.With(dueAt, 5);

        var restored = core.Rollback(reviewed.Card, revlog);

        reviewed.Revlog.State.Should().Be(State.New);
        seen.Should().HaveCount(3);
        seen[2].Should().Be(seen[0]);
        restored.Should().Be(card);
    }

    [Fact]
    public void Rollback_ValidatesRevlogAndCard()
    {
        var result = Core.Review(Core.NewCard(), Rating.Good, 0);
        var badRating = () => Core.Rollback(result.Card, result.Revlog with { Rating = Rating.Manual });
        badRating.Should().Throw<FsrsValidationException>().WithMessage("Expected grade");
        var badCard = () => Core.Rollback(result.Card with { Fields = FieldBag.Empty }, result.Revlog);
        badCard.Should().Throw<FsrsValidationException>();
    }

    // ---------------------------------------------------------------- card schema validation

    [Fact]
    public void CardValidation_ValidatesCardInput()
    {
        var card = Core.NewCard();
        var empty = () => Core.Review(card with { Fields = FieldBag.Empty }, Rating.Good);
        empty.Should().Throw<FsrsValidationException>();
        var nullCard = () => Core.Review(null!, Rating.Good);
        nullCard.Should().Throw<FsrsValidationException>().WithMessage("Expected card object");
        var model = () => Core.Review(card with { MemoryState = new Sm2State(double.NaN, 2.5, 0) }, Rating.Good);
        model.Should().Throw<FsrsValidationException>().WithMessage("Expected SM2 memory state");
    }
}
