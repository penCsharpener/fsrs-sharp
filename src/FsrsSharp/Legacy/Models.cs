using FsrsSharp.Middlewares;
using FsrsSharp.Models.Fsrs6;

namespace FsrsSharp.Legacy;

/// <summary>
/// A flashcard being scheduled by FSRS.
/// </summary>
public class Card
{
    public DateTime Due { get; set; }
    public double Stability { get; set; }
    public double Difficulty { get; set; }

    public int ScheduledDays { get; set; }

    /// <summary>Which learning/relearning step the card is currently on.</summary>
    public int LearningSteps { get; set; }

    public int Reps { get; set; }
    public int Lapses { get; set; }
    public State State { get; set; }
    public DateTime? LastReview { get; set; }

    public Card Clone() => (Card)MemberwiseClone();
}

/// <summary>
/// A record of one review of a card, as produced alongside the updated
/// <see cref="Card"/> by <see cref="Fsrs.Next"/> / <see cref="Fsrs.Repeat"/>.
/// </summary>
public class ReviewLog
{
    public Rating Rating { get; set; }
    public State State { get; set; }
    public DateTime Due { get; set; }
    public double Stability { get; set; }
    public double Difficulty { get; set; }

    public int ScheduledDays { get; set; }
    public int LearningSteps { get; set; }
    public DateTime Review { get; set; }
}

/// <summary>The (card, log) pair produced for one grade after a review.</summary>
public readonly record struct RecordLogItem(Card Card, ReviewLog Log);

/// <summary>
/// The four possible outcomes of reviewing a card "now", keyed by the grade
/// the learner could give. Mirrors ts-fsrs's <c>repeat()</c> result.
/// </summary>
public class RecordLog
{
    public required RecordLogItem Again { get; init; }
    public required RecordLogItem Hard { get; init; }
    public required RecordLogItem Good { get; init; }
    public required RecordLogItem Easy { get; init; }

    public RecordLogItem this[Rating grade] => grade switch
    {
        Rating.Again => Again,
        Rating.Hard => Hard,
        Rating.Good => Good,
        Rating.Easy => Easy,
        _ => throw new FsrsValidationException($"Invalid grade \"{grade}\", expected Again/Hard/Good/Easy"),
    };

    public IEnumerable<RecordLogItem> AsEnumerable()
    {
        yield return Again;
        yield return Hard;
        yield return Good;
        yield return Easy;
    }
}

/// <summary>
/// Tunable parameters of the FSRS algorithm. Construct with
/// <see cref="Defaults.GeneratorParameters"/> rather than the constructor directly,
/// so defaults and migration of <see cref="W"/> are applied consistently.
/// <para/>
/// Like ts-fsrs's <c>Proxy</c>-backed <c>fsrs.parameters</c>, the instance owned by an <see cref="Fsrs"/>
/// is live: assigning <see cref="W"/> migrates and clips the weights and rebuilds the model, and assigning
/// <see cref="EnableShortTerm"/> rebuilds the model and switches the scheduler.
/// </summary>
public class FsrsParameters
{
    private double[] _w = Constants.DefaultW.ToArray();
    private bool _enableShortTerm = Constants.DefaultEnableShortTerm;

    internal Action? Changed;
    internal bool Live;

    public double RequestRetention { get; set; } = Constants.DefaultRequestRetention;
    public int MaximumInterval { get; set; } = Constants.DefaultMaximumInterval;

    /// <summary>The FSRS-6 weights. On a live instance the assigned value is migrated and clipped first.</summary>
    public double[] W
    {
        get => _w;
        set
        {
            _w = Live
                ? Fsrs6Parameters.Clip(Fsrs6Parameters.Migrate(value), RelearningSteps.Length, _enableShortTerm)
                : value;
            Changed?.Invoke();
        }
    }

    public bool EnableFuzz { get; set; } = Constants.DefaultEnableFuzz;

    /// <summary>When false, the (re)learning steps are not applied and the long-term scheduler is used.</summary>
    public bool EnableShortTerm
    {
        get => _enableShortTerm;
        set
        {
            _enableShortTerm = value;
            Changed?.Invoke();
        }
    }

    public string[] LearningSteps { get; set; } = LearningStepsMiddleware.DefaultLearningSteps.ToArray();
    public string[] RelearningSteps { get; set; } = LearningStepsMiddleware.DefaultRelearningSteps.ToArray();

    /// <summary>A detached copy (not live).</summary>
    public FsrsParameters Clone() => new()
    {
        RequestRetention = RequestRetention,
        MaximumInterval = MaximumInterval,
        _w = (double[])_w.Clone(),
        EnableFuzz = EnableFuzz,
        _enableShortTerm = _enableShortTerm,
        LearningSteps = (string[])LearningSteps.Clone(),
        RelearningSteps = (string[])RelearningSteps.Clone(),
    };
}
