using Xunit;
using AwesomeAssertions;
using FsrsSharp.Chrono;
using FsrsSharp.Kit;
using FsrsSharp.Models;
using FsrsSharp.Models.Fsrs6;
using FsrsSharp.Reschedule;
using FsrsSharp.Tests.Middlewares;
using static FsrsSharp.Tests.Scheduler.SchedulerTestSupport;

namespace FsrsSharp.Tests.Reschedule;

/// <summary>
/// Port of ts-fsrs <c>reschedule/reschedule.spec.ts</c>. Not ported: the <c>Temporal.Instant</c> test (no such
/// chrono; <see cref="DateTime"/> in UTC is the instant), the type-only <c>expectTypeOf</c> checks, and the
/// time-schema transform case (chronos here validate but never transform review times).
/// </summary>
public class RescheduleTests
{
    private const double DayMs = 86_400_000;

    private static FieldBag.Builder ModelConfig() => MiddlewareTestSchedulers.Fsrs6Config(enableShortTerm: true, numRelearningSteps: 1);

    private static SpyModel<FsrsState> DateSpy { get; } = new(Fsrs6Model.Instance);
    private static SpyModel<FsrsState> NumericSpy { get; } = new(Fsrs6Model.Instance);

    private static readonly Scheduler<FsrsState, DateTime> DateScheduler =
        SchedulerDefinition.Define<FsrsState, DateTime>(DateSpy, DateTimeChrono.Instance).Create(ModelConfig());

    private static readonly Scheduler<FsrsState, double> NumericScheduler =
        SchedulerDefinition.Define<FsrsState, double>(NumericSpy, NumericChrono.Instance).Create(ModelConfig());

    private static readonly Rescheduler<FsrsState, DateTime> DateReschedule = new(DateScheduler);
    private static readonly Rescheduler<FsrsState, double> NumericReschedule = new(NumericScheduler);

    private static DateTime At(double ms) => DateTime.UnixEpoch.AddTicks((long)(ms * TimeSpan.TicksPerMillisecond));

    private static RescheduleReview<DateTime> D(Rating rating, double ms) => new(rating, At(ms));

    private static RescheduleReview<double> N(Rating rating, double time) => new(rating, time);

    private static ReplayInput<FsrsState, double> NumericInput(FsrsState? initial, params RescheduleReview<double>[] history) =>
        new(history, initial);

    private static ReplayInput<FsrsState, DateTime> DateInput(params RescheduleReview<DateTime>[] history) => new(history);

    public class Replay
    {
        [Fact]
        public void RebuildsMemoryWithOneModelForwardCall()
        {
            DateSpy.Clear();

            var result = DateReschedule.Replay(DateInput(D(Rating.Good, DayMs), D(Rating.Hard, DayMs * 2), D(Rating.Easy, DayMs * 6)));

            var call = DateSpy.Forwards.Should().ContainSingle().Which;
            call.History.Should().Equal(new ModelReview(Rating.Good, 0), new ModelReview(Rating.Hard, 1), new ModelReview(Rating.Easy, 4));
            call.InitialState.Should().BeNull();
            result.MemoryStates.Should().Equal(call.Result);
            result.MemoryState.Should().Be(result.MemoryStates[^1]);
        }

        [Fact]
        public void RejectsEmptyHistoryEvenWhenAnInitialStateIsProvided()
        {
            var act = () => NumericReschedule.Replay(NumericInput(new FsrsState(12, 6)));

            act.Should().Throw<FsrsValidationException>();
        }

        [Fact]
        public void RejectsHistoryThatContainsOnlyManualRatings()
        {
            var act = () => NumericReschedule.Replay(NumericInput(null, N(Rating.Manual, 0)));

            act.Should().Throw<FsrsValidationException>().WithMessage("Reschedule requires at least one non-manual review");
        }

        [Fact]
        public void ValidatesReviewRatings()
        {
            var act = () => NumericReschedule.Replay(NumericInput(null, N((Rating)5, 0)));

            act.Should().Throw<FsrsValidationException>();
        }

        [Fact]
        public void UsesTheConfiguredNumericChronology()
        {
            NumericSpy.Clear();

            NumericReschedule.Replay(NumericInput(null, N(Rating.Good, 3), N(Rating.Again, 8.5)));

            var call = NumericSpy.Forwards.Should().ContainSingle().Which;
            call.History.Should().Equal(new ModelReview(Rating.Good, 0), new ModelReview(Rating.Again, 5.5));
            call.InitialState.Should().BeNull();
        }

        [Fact]
        public void SortsReviewTimesBeforeCalculatingIntervalsWithoutMutatingTheInput()
        {
            NumericSpy.Clear();
            RescheduleReview<double>[] history = [N(Rating.Easy, 3), N(Rating.Good, 1)];
            var original = history.ToArray();

            NumericReschedule.Replay(new ReplayInput<FsrsState, double>(history));

            NumericSpy.Forwards.Should().ContainSingle().Which.History
                .Should().Equal(new ModelReview(Rating.Good, 0), new ModelReview(Rating.Easy, 2));
            history.Should().Equal(original);
        }

        [Fact]
        public void PassesTheInitialStateToTheModel()
        {
            NumericSpy.Clear();
            var initial = new FsrsState(12, 6);

            NumericReschedule.Replay(NumericInput(initial, N(Rating.Good, 3)));

            NumericSpy.Forwards.Should().ContainSingle().Which.InitialState.Should().BeSameAs(initial);
        }

        [Fact]
        public void FiltersManualRatingsBeforeCallingModelForward()
        {
            NumericSpy.Clear();

            var result = NumericReschedule.Replay(NumericInput(null, N(Rating.Good, 3), N(Rating.Manual, 4), N(Rating.Again, 8.5)));

            NumericSpy.Forwards.Should().ContainSingle().Which.History
                .Should().Equal(new ModelReview(Rating.Good, 0), new ModelReview(Rating.Again, 5.5));
            result.MemoryStates.Should().HaveCount(2);
        }

        [Fact]
        public void RejectsReviewTimesThatMoveBackwards()
        {
            var act = () => DateReschedule.Replay(
                DateInput(D(Rating.Good, DayMs * 2), D(Rating.Easy, DayMs)),
                new FsrsSharp.Reschedule.RescheduleOptions { SortHistory = false });

            act.Should().Throw<FsrsValidationException>().WithMessage("*Review times must produce finite non-negative intervals*");
        }

        [Fact]
        public void RejectsReviewTimesThatProduceNonFiniteIntervals()
        {
            var scheduler = SchedulerDefinition.Define<FsrsState, double>(Fsrs6Model.Instance, new NaNDifferenceChrono()).Create(ModelConfig());
            var reschedule = new Rescheduler<FsrsState, double>(scheduler);

            var act = () => reschedule.Replay(NumericInput(null, N(Rating.Good, 0), N(Rating.Easy, 1)));

            act.Should().Throw<FsrsValidationException>().WithMessage("*Review times must produce finite non-negative intervals*");
        }
    }

    public class RescheduleMethod
    {
        [Fact]
        public void PassesReviewsToSchedulerForwardWithoutModelIntervals()
        {
            RescheduleReview<double>[] history = [N(Rating.Good, 1), N(Rating.Easy, 3)];

            var result = NumericReschedule.Reschedule(
                new RescheduleInput<FsrsState, double>(history),
                new FsrsSharp.Reschedule.RescheduleOptions { SortHistory = false });

            var expected = NumericScheduler.Forward([.. history.Select(r => new SchedulerReview<double>(r.Rating, r.ReviewTime))]);
            result.Results.Should().Equal(expected);
        }

        [Fact]
        public void ReturnsOneCardAndRevlogPerReview()
        {
            var result = DateReschedule.Reschedule(new RescheduleInput<FsrsState, DateTime>(
                [D(Rating.Good, DayMs), D(Rating.Hard, DayMs * 2), D(Rating.Again, DayMs * 6)]));

            result.Results.Should().HaveCount(3);
            result.Card.Should().Be(result.Results[2].Card);
            result.Results[2].Revlog.Rating.Should().Be(Rating.Again);
            result.Card.State.Should().Be(State.Review);
            Ms(result.Card.DueAt).Should().BeGreaterThan((long)(DayMs * 6));
        }

        [Fact]
        public void MatchesAnEquivalentChainOfSchedulerReviewCalls()
        {
            RescheduleReview<DateTime>[] history = [D(Rating.Good, DayMs), D(Rating.Easy, DayMs * 4)];

            var results = DateReschedule.Reschedule(new RescheduleInput<FsrsState, DateTime>(history)).Results;

            var card = DateScheduler.NewCard(history[0].ReviewTime);
            List<ScheduleResult<FsrsState>> expected = [];
            foreach (var review in history)
            {
                var item = DateScheduler.Review(card, review.Rating, review.ReviewTime);
                card = item.Card;
                expected.Add(item);
            }

            results.Should().Equal(expected);
        }

        [Fact]
        public void SortsAndFiltersTheHistoryLikeReplayDoes()
        {
            var results = DateReschedule.Reschedule(new RescheduleInput<FsrsState, DateTime>(
                [D(Rating.Easy, DayMs * 4), D(Rating.Manual, DayMs * 2), D(Rating.Good, DayMs)])).Results;

            results.Should().HaveCount(2);
            results[0].Revlog.Rating.Should().Be(Rating.Good);
            results[1].Revlog.Rating.Should().Be(Rating.Easy);
        }

        [Fact]
        public void StartsFromTheGivenInitialCard()
        {
            var seeded = DateScheduler.Review(DateScheduler.NewCard(At(DayMs)), Rating.Good, At(DayMs));

            var results = DateReschedule.Reschedule(new RescheduleInput<FsrsState, DateTime>(
                [D(Rating.Good, DayMs * 3)], seeded.Card)).Results;

            results.Should().HaveCount(1);
            results[0].Revlog.State.Should().Be(State.Review);
        }

        [Fact]
        public void RejectsHistoryWithoutANonManualReview()
        {
            var act = () => DateReschedule.Reschedule(new RescheduleInput<FsrsState, DateTime>([D(Rating.Manual, DayMs)]));

            act.Should().Throw<FsrsValidationException>();
        }

        [Fact]
        public void RejectsReviewTimesThatMoveBackwards()
        {
            var act = () => DateReschedule.Reschedule(
                new RescheduleInput<FsrsState, DateTime>([D(Rating.Good, DayMs * 10), D(Rating.Good, 0)]),
                new FsrsSharp.Reschedule.RescheduleOptions { SortHistory = false });

            act.Should().Throw<FsrsValidationException>();
        }

        [Fact]
        public void RejectsReviewTimesThatProduceNonFiniteIntervals()
        {
            var act = () => NumericReschedule.Reschedule(new RescheduleInput<FsrsState, double>(
                [N(Rating.Good, 0), N(Rating.Easy, double.NaN)]));

            act.Should().Throw<FsrsValidationException>();
        }
    }

    /// <summary>A numeric chrono whose <c>difference</c> always yields NaN (upstream mocks <c>chrono.difference</c>).</summary>
    private sealed class NaNDifferenceChrono : IChrono<double>
    {
        private static readonly NumericChrono Inner = NumericChrono.Instance;

        public IReadOnlyList<FieldKey> ConfigKeys => Inner.ConfigKeys;
        public IReadOnlyList<FieldKey> CardFields => Inner.CardFields;
        public IReadOnlyList<FieldKey> RevlogFields => Inner.RevlogFields;

        public ChronoTimes<double> NormalizeReview(FieldBag cardFields, double time) => Inner.NormalizeReview(cardFields, time);

        public ChronoTimes<double> NormalizeRollback(FieldBag revlogFields) => Inner.NormalizeRollback(revlogFields);

        public void WriteCardDefaults(FieldBag.Builder card, FieldBag config, double time, double? previous) =>
            Inner.WriteCardDefaults(card, config, time, previous);

        public void WriteRevlogDefaults(FieldBag.Builder revlog, FieldBag config, double time, ChronoTimes<double> previous) =>
            Inner.WriteRevlogDefaults(revlog, config, time, previous);

        public IChronoCore<double> Create(FieldBag config) => new Core(Inner.Create(config));

        private sealed class Core(IChronoCore<double> inner) : IChronoCore<double>
        {
            public double Now() => inner.Now();
            public int Compare(double left, double right) => inner.Compare(left, right);
            public double Difference(double from, double to) => double.NaN;
            public double Add(double from, double days) => inner.Add(from, days);
        }
    }
}
