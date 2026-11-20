namespace FsrsSharp;

/// <summary>How long to wait, and which step to land on, for one grade during (re)learning.</summary>
public readonly record struct LearningStepInfo(double ScheduledMinutes, int NextStep);

/// <summary>
/// Decides the wait time (in minutes) and next step index for each possible grade,
/// given the card's current (re)learning step. Ported from ts-fsrs's pluggable
/// "LearningSteps" strategy — pass a custom one to <see cref="Fsrs.UseLearningStepsStrategy"/>.
/// </summary>
public delegate IReadOnlyDictionary<Rating, LearningStepInfo> LearningStepsStrategy(FsrsParameters parameters, State state, int currentStep);

/// <summary>
/// Produces the seed used to deterministically fuzz a card's interval. Ported from
/// ts-fsrs's pluggable "Seed" strategy — pass a custom one (e.g. keyed off a stable
/// card id) to <see cref="Fsrs.UseSeedStrategy"/> if you need fuzzing to be stable
/// across re-computations of the same review.
/// </summary>
public delegate string SeedStrategy(AbstractScheduler scheduler);

public static class Strategies
{
    /// <summary>Parses a step like "1m"/"10m"/"1h"/"1d" into a whole number of minutes.</summary>
    public static double StepUnitToMinutes(string step)
    {
        if (string.IsNullOrEmpty(step))
        {
            throw new FsrsValidationException($"Invalid step value: {step}");
        }

        var unit = step[^1];
        if (!double.TryParse(step[..^1], out var value) || !double.IsFinite(value) || value < 0)
        {
            throw new FsrsValidationException($"Invalid step value: {step}");
        }

        return unit switch
        {
            'm' => value,
            'h' => value * 60,
            'd' => value * 1440,
            _ => throw new FsrsValidationException($"Invalid step unit: {step}, expected m/h/d"),
        };
    }

    /// <summary>The default learning-steps strategy used by <see cref="BasicScheduler"/>.</summary>
    public static readonly LearningStepsStrategy DefaultLearningSteps = (parameters, state, curStep) =>
    {
        var steps = state is State.Relearning or State.Review ? parameters.RelearningSteps : parameters.LearningSteps;
        var stepsLength = steps.Length;
        var result = new Dictionary<Rating, LearningStepInfo>();
        if (stepsLength == 0 || curStep >= stepsLength) return result;

        var firstStep = steps[0];

        double AgainInterval() => StepUnitToMinutes(firstStep);

        double HardInterval()
        {
            if (stepsLength == 1) return Helpers.JsRound(StepUnitToMinutes(firstStep) * 1.5);
            var nextStep = steps[1];
            return Helpers.JsRound((StepUnitToMinutes(firstStep) + StepUnitToMinutes(nextStep)) / 2);
        }

        string? StepAt(int index) => index < 0 || index >= stepsLength ? null : steps[index];

        var stepInfo = StepAt(Math.Max(0, curStep));
        if (state == State.Review)
        {
            result[Rating.Again] = new LearningStepInfo(StepUnitToMinutes(stepInfo!), 0);
            return result;
        }

        result[Rating.Again] = new LearningStepInfo(AgainInterval(), 0);
        result[Rating.Hard] = new LearningStepInfo(HardInterval(), curStep);

        var nextInfo = StepAt(curStep + 1);
        if (nextInfo is not null)
        {
            var nextMinutes = StepUnitToMinutes(nextInfo);
            if (nextMinutes != 0)
            {
                result[Rating.Good] = new LearningStepInfo(Helpers.JsRound(nextMinutes), curStep + 1);
            }
        }

        return result;
    };

    /// <summary>The default seed strategy: derived from review time, rep count, and D*S — matches ts-fsrs.</summary>
    public static readonly SeedStrategy DefaultSeed = scheduler =>
    {
        var time = new DateTimeOffset(scheduler.ReviewTime, TimeSpan.Zero).ToUnixTimeMilliseconds();
        var reps = scheduler.Current.Reps;
        var mul = scheduler.Current.Difficulty * scheduler.Current.Stability;
        return $"{time}_{reps}_{mul}";
    };

    /// <summary>
    /// Builds a seed strategy keyed off a stable per-card identifier (plus rep count), so
    /// fuzzing stays consistent for the same card across repeated scheduling calls.
    /// </summary>
    public static SeedStrategy WithCardId(Func<Card, object?> cardIdSelector) => scheduler =>
    {
        var cardId = cardIdSelector(scheduler.Current) ?? 0;
        var reps = scheduler.Current.Reps;
        return $"{cardId}{reps}";
    };
}
