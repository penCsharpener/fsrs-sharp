using System.Globalization;
using FsrsSharp.Chrono;
using FsrsSharp.Kit;
using FsrsSharp.Middlewares;
using FsrsSharp.Models;

namespace FsrsSharp.Legacy;

/// <summary>A scheduler for one particular (card, now) pair: previews or applies a grade.</summary>
public interface IScheduler
{
    /// <summary>Computes the outcome of all four possible grades.</summary>
    RecordLog Preview();

    /// <summary>Computes the outcome of one grade.</summary>
    RecordLogItem Review(Rating grade);
}

/// <summary>
/// Ported from ts-fsrs's <c>legacy/abstract_scheduler.ts</c>. Handles the bookkeeping shared by
/// <see cref="BasicScheduler"/> (learning steps enabled) and <see cref="LongTermScheduler"/>
/// (learning steps disabled): elapsed-day tracking, memoized per-grade results, log building,
/// and the New/Learning-Relearning/Review state dispatch. Since v6.0.0 it talks to an
/// <see cref="IModelCore{TState}"/> (the FSRS-6 model) and reads its policies from <see cref="FsrsParameters"/>.
/// </summary>
public abstract class AbstractScheduler : IScheduler
{
    protected readonly Card Last;
    protected readonly IModelCore<FsrsState> Model;
    protected readonly FsrsParameters Parameters;
    protected readonly Dictionary<Rating, RecordLogItem> NextByGrade = new();
    protected int ElapsedDays;

    /// <summary>Fuzz seed: <c>{reviewTimeMs}_{reps}_{difficulty * stability}</c>.</summary>
    protected readonly string Seed;

    public Card Current { get; }

    public DateTime ReviewTime { get; }

    protected AbstractScheduler(Card card, DateTime now, IModelCore<FsrsState> model, FsrsParameters parameters)
    {
        Model = model;
        Parameters = parameters;
        Last = card.Clone();
        Current = card.Clone();
        ReviewTime = now;

        var interval = 0; // card.State == State.New => 0
        if (Current.State != State.New && Current.LastReview is { } lastReview)
        {
            interval = LegacyHelpers.DateDiffInDays(lastReview, ReviewTime);
        }

        Current.LastReview = ReviewTime;
        ElapsedDays = interval;
        Current.Reps += 1;

        Seed = $"{DateTimeChrono.ToUnixMilliseconds(ReviewTime)}_{Current.Reps}_{JsNumber(Current.Difficulty * Current.Stability)}";
    }

    private static string JsNumber(double d) => d == 0 ? "0" : d.ToString("R", CultureInfo.InvariantCulture);

    protected static void CheckGrade(Rating grade)
    {
        if (grade is < Rating.Again or > Rating.Easy)
        {
            throw new FsrsValidationException($"Invalid grade \"{grade}\",expected 1-4");
        }
    }

    public RecordLog Preview() => new()
    {
        Again = Review(Rating.Again),
        Hard = Review(Rating.Hard),
        Good = Review(Rating.Good),
        Easy = Review(Rating.Easy),
    };

    public RecordLogItem Review(Rating grade)
    {
        CheckGrade(grade);
        return Last.State switch
        {
            State.New => NewState(grade),
            State.Learning or State.Relearning => LearningState(grade),
            State.Review => ReviewState(grade),
            _ => throw new FsrsValidationException($"Invalid state \"{Last.State}\""),
        };
    }

    protected abstract RecordLogItem NewState(Rating grade);

    protected abstract RecordLogItem LearningState(Rating grade);

    protected abstract RecordLogItem ReviewState(Rating grade);

    protected ReviewLog BuildLog(Rating rating) => new()
    {
        Rating = rating,
        State = Current.State,
        Due = Last.LastReview ?? Last.Due,
        Stability = Current.Stability,
        Difficulty = Current.Difficulty,
        ScheduledDays = Current.ScheduledDays,
        LearningSteps = Current.LearningSteps,
        Review = ReviewTime,
    };

    /// <summary>The (fuzzed, monotonic-before-capping) day interval for <paramref name="card"/>'s stability.</summary>
    protected int SchedulerNextInterval(Card card, double elapsedDays)
    {
        var baseInterval = Model.NextInterval(new FsrsState(card.Stability, card.Difficulty), Parameters.RequestRetention);
        var interval = Fuzzing.WithFuzzing(
            baseInterval,
            elapsedDays,
            new FuzzingConfigInput(Parameters.EnableFuzz, Parameters.MaximumInterval),
            Seed);
        return (int)interval;
    }

    protected Card NextDs(double t, Rating grade, double? retrievability = null)
    {
        var nextState = Model.Step(new ModelStepInput<FsrsState>(
            new FsrsState(Current.Stability, Current.Difficulty), grade, t, retrievability));
        var card = Current.Clone();
        card.Difficulty = nextState.Difficulty;
        card.Stability = nextState.Stability;
        return card;
    }
}
