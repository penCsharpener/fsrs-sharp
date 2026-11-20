namespace FsrsSharp;

/// <summary>
/// A memory state consisting of stability and difficulty, as used by
/// <see cref="FsrsAlgorithm.NextState"/>.
/// </summary>
public readonly record struct FsrsMemoryState(double Stability, double Difficulty);

/// <summary>
/// A flashcard being scheduled by FSRS.
/// </summary>
public class Card
{
    public DateTime Due { get; set; }
    public double Stability { get; set; }
    public double Difficulty { get; set; }

    /// <summary>
    /// Number of days elapsed since the last review.
    /// Kept only for backwards compatibility with ts-fsrs/py-fsrs's ReviewLog shape;
    /// FSRS-6 scheduling itself does not depend on it.
    /// </summary>
    public int ElapsedDays { get; set; }

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

    /// <summary>Number of days elapsed since the last review. Deprecated upstream.</summary>
    public int ElapsedDays { get; set; }

    /// <summary>Number of days between the two reviews before this one. Deprecated upstream.</summary>
    public int LastElapsedDays { get; set; }

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
/// so defaults, migration and clamping of <see cref="W"/> are applied consistently.
/// </summary>
public class FsrsParameters
{
    public double RequestRetention { get; set; } = Constants.DefaultRequestRetention;
    public int MaximumInterval { get; set; } = Constants.DefaultMaximumInterval;
    public double[] W { get; set; } = (double[])Constants.DefaultW.Clone();
    public bool EnableFuzz { get; set; } = Constants.DefaultEnableFuzz;

    /// <summary>When false, (re)learning steps are skipped and every review goes straight to Review state.</summary>
    public bool EnableShortTerm { get; set; } = Constants.DefaultEnableShortTerm;

    public string[] LearningSteps { get; set; } = (string[])Constants.DefaultLearningSteps.Clone();
    public string[] RelearningSteps { get; set; } = (string[])Constants.DefaultRelearningSteps.Clone();

    public FsrsParameters Clone() => new()
    {
        RequestRetention = RequestRetention,
        MaximumInterval = MaximumInterval,
        W = (double[])W.Clone(),
        EnableFuzz = EnableFuzz,
        EnableShortTerm = EnableShortTerm,
        LearningSteps = (string[])LearningSteps.Clone(),
        RelearningSteps = (string[])RelearningSteps.Clone(),
    };
}
