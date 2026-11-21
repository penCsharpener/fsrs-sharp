using FsrsSharp.Chrono;
using FsrsSharp.Kit;
using FsrsSharp.Middlewares;
using FsrsSharp.Models;
using FsrsSharp.Tests.Middlewares;

namespace FsrsSharp.Tests.Scheduler;

/// <summary>Port of ts-fsrs <c>scheduler/default-scheduler.test-utils.ts</c>.</summary>
internal static class SchedulerTestSupport
{
    public const double Day = 86_400_000;
    public static readonly DateTime Now = new(2025, 2, 20, 12, 0, 0, DateTimeKind.Utc);

    public static readonly Rating[] AllGrades = [Rating.Again, Rating.Hard, Rating.Good, Rating.Easy];
    public static readonly State[] AllStates = [State.New, State.Learning, State.Review, State.Relearning];

    public static readonly Scheduler<FsrsState, DateTime> CardFactory = DefaultScheduler.Create(FsrsVersion.Fsrs6);

    public static ScheduleStatus StatusOf(State state) => state switch
    {
        State.New => ScheduleStatus.New,
        State.Learning => ScheduleStatus.Learning,
        State.Review => ScheduleStatus.Review,
        State.Relearning => ScheduleStatus.Learning,
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    public static DateTime AddMs(DateTime time, double ms) => time.AddTicks((long)(ms * TimeSpan.TicksPerMillisecond));

    public static long Ms(DateTime time) => (time.ToUniversalTime() - DateTime.UnixEpoch).Ticks / TimeSpan.TicksPerMillisecond;

    public static long MsBetween(DateTime later, DateTime earlier) => Ms(later) - Ms(earlier);

    /// <summary>Memory fields and bookkeeping of upstream <c>createStateCard</c>: (stability, difficulty, learningStep, lapses).</summary>
    public static (double Stability, double Difficulty, int LearningStep, int Lapses) Spec(State state) => state switch
    {
        State.Learning => (1.4, 6.4, 1, 0),
        State.Review => (12.4, 5.2, 0, 1),
        State.Relearning => (2.8, 7.1, 1, 2),
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    /// <summary>The non-memory card fields of a (re)learning or review card (<c>shared</c> in upstream).</summary>
    public static FieldBag StateFields(State state, object cardId)
    {
        var (_, _, learningStep, lapses) = Spec(state);
        return new FieldBag.Builder()
            .Set(FuzzingMiddleware.CardIdField, cardId)
            .Set(DateTimeChrono.DueAt, Now)
            .Set(ScheduledDaysMiddleware.ScheduledDays, 10.0)
            .Set(StatsMiddleware.Reps, 8)
            .Set(DateTimeChrono.LastReviewAt, (DateTime?)AddMs(Now, -10 * Day))
            .Set(LearningStepsMiddleware.LearningStepField, learningStep)
            .Set(StatsMiddleware.Lapses, lapses)
            .ToFieldBag();
    }

    /// <summary>Upstream <c>createStateCard</c> for the FSRS-6 card factory.</summary>
    public static Card<FsrsState> CreateStateCard(State state)
    {
        var cardId = $"card-{(int)state}";
        if (state == State.New)
            return CardFactory.NewCard(new DateTime(2025, 1, 5, 3, 4, 5, DateTimeKind.Utc), MiddlewareTestSchedulers.CardInput(cardId));

        var (stability, difficulty, _, _) = Spec(state);
        return new Card<FsrsState>(new FsrsState(stability, difficulty), state, StatusOf(state), StateFields(state, cardId));
    }

    /// <summary>The same card for an FSRS-7 scheduler (stabilityFast mirrors stability like the upstream spreads).</summary>
    public static Card<Fsrs7State> CreateFsrs7StateCard(State state, object? cardId = null)
    {
        var (stability, difficulty, _, _) = Spec(state);
        return new Card<Fsrs7State>(
            new Fsrs7State(stability, stability, difficulty),
            state,
            StatusOf(state),
            StateFields(state, cardId ?? $"card-{(int)state}"));
    }

    public static Rating[] RatingsFrom(uint seed, int count)
    {
        var result = new Rating[count];
        for (var i = 0; i < count; i++)
        {
            seed = unchecked(seed * 1_664_525u + 1_013_904_223u);
            result[i] = AllGrades[(int)((seed >> 24) % 4)];
        }

        return result;
    }
}
