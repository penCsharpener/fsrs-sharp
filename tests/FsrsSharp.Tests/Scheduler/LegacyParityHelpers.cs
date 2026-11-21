using AwesomeAssertions;
using FsrsSharp.Chrono;
using FsrsSharp.Kit;
using FsrsSharp.Middlewares;
using FsrsSharp.Models;
using static FsrsSharp.Tests.Scheduler.SchedulerTestSupport;

namespace FsrsSharp.Tests.Scheduler;

/// <summary>What the legacy v5 scheduler produced for a card (upstream <c>Card</c> from <c>legacy/</c>).</summary>
internal sealed record LegacyCardView(
    DateTime Due,
    double Stability,
    double Difficulty,
    double ScheduledDays,
    int LearningSteps,
    int Reps,
    int Lapses,
    State State,
    DateTime? LastReview);

/// <summary>What the legacy v5 scheduler logged for a review (upstream <c>ReviewLog</c> from <c>legacy/</c>).</summary>
internal sealed record LegacyLogView(
    double Stability,
    double Difficulty,
    double ScheduledDays,
    int LearningSteps,
    Rating Rating,
    State State,
    DateTime Review);

internal sealed record LegacyNext(LegacyCardView Card, LegacyLogView Log);

/// <summary>
/// Port of ts-fsrs <c>scheduler/default-scheduler.legacy-test-utils.ts</c>. The only code that touches the legacy
/// v5 API is <see cref="Next"/>; it is compiled out until <c>FsrsSharp.Legacy</c> is in place. To enable the parity
/// tests, define <c>FSRS_LEGACY_V6</c> and remove the <c>Skip</c> arguments in
/// <see cref="DefaultSchedulerLegacyParityTests"/>.
/// </summary>
internal static class LegacyParityHelpers
{
    /// <summary>Upstream <c>legacyNext</c>: <c>new FSRS(toLegacyParameters(options)).next(toLegacyCard(card), now, grade)</c>.</summary>
    public static LegacyNext Next(DefaultSchedulerOptions options, Card<FsrsState> card, DateTime now, Rating grade)
    {
#if FSRS_LEGACY_V6
        var defaults = FsrsSharp.Legacy.Defaults.GeneratorParameters();
        var fsrs = new FsrsSharp.Legacy.Fsrs(new FsrsSharp.Legacy.FsrsParametersInput
        {
            W = options.Weights?.ToArray() ?? defaults.W.ToArray(),
            EnableShortTerm = options.EnableShortTerm,
            RequestRetention = options.DesiredRetention,
            LearningSteps = (options.LearningSteps ?? defaults.LearningSteps).ToArray(),
            RelearningSteps = (options.RelearningSteps ?? defaults.RelearningSteps).ToArray(),
            EnableFuzz = options.EnableFuzz,
            MaximumInterval = (int)options.MaximumInterval,
        });
        var legacyCard = new FsrsSharp.Legacy.Card
        {
            Due = card.DueAt,
            Stability = card.MemoryState.Stability,
            Difficulty = card.MemoryState.Difficulty,
            ScheduledDays = (int)card.ScheduledDays,
            LearningSteps = card.LearningStep,
            Reps = card.Reps,
            Lapses = card.Lapses,
            State = card.State,
            LastReview = card.LastReviewAt,
        };
        var item = fsrs.Next(legacyCard, now, grade);
        return new LegacyNext(
            new LegacyCardView(
                item.Card.Due, item.Card.Stability, item.Card.Difficulty, item.Card.ScheduledDays, item.Card.LearningSteps,
                item.Card.Reps, item.Card.Lapses, item.Card.State, item.Card.LastReview),
            new LegacyLogView(
                item.Log.Stability, item.Log.Difficulty, item.Log.ScheduledDays, item.Log.LearningSteps,
                item.Log.Rating, item.Log.State, item.Log.Review));
#else
        _ = (options, card, now, grade);
        throw new NotSupportedException("Needs FsrsSharp.Legacy v6 (define FSRS_LEGACY_V6).");
#endif
    }

    private static Card<FsrsState> FromLegacyCard(LegacyCardView card, object cardId) =>
        new(
            new FsrsState(card.Stability, card.Difficulty),
            card.State,
            StatusOf(card.State),
            new FieldBag.Builder()
                .Set(FuzzingMiddleware.CardIdField, cardId)
                .Set(DateTimeChrono.DueAt, card.Due)
                .Set(ScheduledDaysMiddleware.ScheduledDays, card.ScheduledDays)
                .Set(LearningStepsMiddleware.LearningStepField, card.LearningSteps)
                .Set(StatsMiddleware.Reps, card.Reps)
                .Set(StatsMiddleware.Lapses, card.Lapses)
                .Set(DateTimeChrono.LastReviewAt, card.LastReview)
                .ToFieldBag());

    private static ReviewLog<FsrsState> FromLegacyRevlog(LegacyLogView log, Card<FsrsState> card) =>
        new(
            new FsrsState(log.Stability, log.Difficulty),
            log.Rating,
            log.State,
            StatusOf(log.State),
            new FieldBag.Builder()
                .Set(FuzzingMiddleware.CardIdRevlog, card.CardId)
                .Set(DateTimeChrono.DueAt, card.DueAt)
                .Set(DateTimeChrono.LastReviewAt, card.LastReviewAt)
                .Set(DateTimeChrono.ReviewTime, log.Review)
                .Set(ScheduledDaysMiddleware.ScheduledDays, log.ScheduledDays)
                .Set(LearningStepsMiddleware.LearningStepField, log.LearningSteps)
                .ToFieldBag());

    /// <summary>Upstream <c>legacyReview</c>.</summary>
    public static ScheduleResult<FsrsState> Review(DefaultSchedulerOptions options, Card<FsrsState> card, DateTime now, Rating grade)
    {
        var result = Next(options, card, now, grade);
        return new ScheduleResult<FsrsState>(FromLegacyCard(result.Card, card.CardId), FromLegacyRevlog(result.Log, card));
    }

    /// <summary>The expected card with the scheduler-decided fields replaced (upstream <c>{ ...expected.card, dueAt, scheduledDays }</c>).</summary>
    public static Card<FsrsState> With(Card<FsrsState> card, DateTime? dueAt = null, double? scheduledDays = null, int? lapses = null)
    {
        var fields = card.Fields.ToBuilder();
        if (dueAt is { } due) fields.Set(DateTimeChrono.DueAt, due);
        if (scheduledDays is { } days) fields.Set(ScheduledDaysMiddleware.ScheduledDays, days);
        if (lapses is { } count) fields.Set(StatsMiddleware.Lapses, count);
        return card with { Fields = fields.ToFieldBag() };
    }

    public static void ExpectRollbackParity(Card<FsrsState> actual, Card<FsrsState> expected) => actual.Should().Be(expected);

    public static void ExpectRevlogParity(ReviewLog<FsrsState> actual, ReviewLog<FsrsState> expected) => actual.Should().Be(expected);

    public static void ExpectFullParity(ScheduleResult<FsrsState> actual, ScheduleResult<FsrsState> expected)
    {
        actual.Card.Should().Be(expected.Card);
        ExpectRevlogParity(actual.Revlog, expected.Revlog);
    }

    public static void ExpectSequenceParity(
        ScheduleResult<FsrsState> actual,
        ScheduleResult<FsrsState> expected,
        Card<FsrsState> previousCard,
        bool enableShortTerm)
    {
        if (enableShortTerm && previousCard.State is State.Learning or State.Relearning)
        {
            actual.Card.Should().Be(With(expected.Card, actual.Card.DueAt, actual.Card.ScheduledDays));
            actual.Card.ScheduledDays.Should().BeGreaterThanOrEqualTo(expected.Card.ScheduledDays);
            ExpectRevlogParity(actual.Revlog, expected.Revlog);
            return;
        }

        ExpectFullParity(actual, expected);
    }
}
