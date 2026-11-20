using FsrsSharp.Kit;

namespace FsrsSharp.Middlewares;

/// <summary>
/// (Re)learning steps and graduation (<c>ts-fsrs.learning-steps</c>). Config: <c>enableShortTerm</c>,
/// <c>learningSteps</c>, <c>relearningSteps</c>, <c>graduatingInterval</c> (default 1 day),
/// <c>allowModelOverride</c> (default false). Declares the card and revlog field <c>learningStep</c>.
/// </summary>
public sealed class LearningStepsMiddleware : Middleware
{
    private const int MinutesPerDay = 1440;
    private const int SecondsPerMinute = 60;
    private static readonly object ResolvedStepsKey = new();

    public static IReadOnlyList<string> DefaultLearningSteps { get; } = ["1m", "10m"];
    public static IReadOnlyList<string> DefaultRelearningSteps { get; } = ["10m"];
    public const double DefaultGraduatingInterval = 1;

    public static FieldKey<bool> EnableShortTerm { get; } = new("enableShortTerm");

    public static FieldKey<IReadOnlyList<string>> LearningSteps { get; } =
        new("learningSteps", v => IsStepList(v) ? null : "Expected valid learningSteps array");

    public static FieldKey<IReadOnlyList<string>> RelearningSteps { get; } =
        new("relearningSteps", v => IsStepList(v) ? null : "Expected valid relearningSteps array");

    public static FieldKey<double> GraduatingInterval { get; } =
        new("graduatingInterval", DefaultGraduatingInterval,
            v => double.IsFinite(v) && v > 0 ? null : "Expected positive graduatingInterval days");

    public static FieldKey<bool> AllowModelOverride { get; } = new("allowModelOverride", false);

    public static FieldKey<int> LearningStepField { get; } =
        new("learningStep", 0, v => v >= 0 ? null : "Expected non-negative integer learningStep");

    public static LearningStepsMiddleware Instance { get; } = new();

    private LearningStepsMiddleware() { }

    public override string Name => "ts-fsrs.learning-steps";
    public override IReadOnlyList<FieldKey> ConfigKeys { get; } =
        [EnableShortTerm, LearningSteps, RelearningSteps, GraduatingInterval, AllowModelOverride];
    public override IReadOnlyList<FieldKey> CardFields { get; } = [LearningStepField];
    public override IReadOnlyList<FieldKey> RevlogFields { get; } = [LearningStepField];

    /// <summary>Reads the focused step config out of a scheduler config.</summary>
    public static LearningStepsConfig GetStepsConfig(FieldBag config) =>
        new(config.Get(LearningSteps), config.Get(RelearningSteps));

    private static bool IsStepList(IReadOnlyList<string>? value)
    {
        if (value is null) return false;
        foreach (var step in value)
            if (!IsStepUnit(step)) return false;
        return true;
    }

    private static bool IsStepUnit(string? value)
    {
        if (value is null) return false;
        try
        {
            // A single step can yield Hard at 1.5x; Chrono converts minutes to ms.
            return double.IsFinite(LearningStepsCore.ConvertStepUnitToMinutes(value) * 90_000);
        }
        catch (FsrsValidationException)
        {
            return false;
        }
    }

    public override void WriteCardDefaults<TState>(CardDefaultContext<TState> context, FieldBag.Builder card) =>
        card.Set(LearningStepField, 0);

    public override void NextInterval<TState, TTime>(NextIntervalContext<TState, TTime> context, Action next)
    {
        if (!context.Config.Get(EnableShortTerm))
        {
            next();
            return;
        }

        var resolved = ResolveLearningSteps(context);
        next();

        // Restore the exact learning step after downstream day-level middleware, but let downstream
        // policies shape a model interval that overrides it.
        var grade = context.Grade;
        if (resolved.StepIntervals.TryGetValue(grade, out var stepInterval) && !resolved.OverridesStep.GetValueOrDefault(grade))
            context.ScheduledDays = stepInterval;
    }

    public override void Review<TState, TTime>(ReviewContext<TState, TTime> context, Action next)
    {
        var config = context.Config;
        var resolved = config.Get(EnableShortTerm) ? ResolveLearningSteps(context) : null;
        next();
        var card = context.Card;
        var grade = context.Grade;
        var graduatingInterval = config.Get(GraduatingInterval);
        var scheduledDays = context.ScheduledDays;
        LearningStepSchedule? step =
            resolved is not null && resolved.StepIntervals.ContainsKey(grade) && resolved.Steps.TryGetValue(grade, out var s)
                ? s
                : null;

        context.ResultRevlog.Set(LearningStepField, card.Get(LearningStepField));
        context.ResultCard.Set(LearningStepField, 0);

        // Intervals below graduation stay in (re)learning, and an Again capped by the model override
        // always (re)learns. Only (re)learning cards keep a step.
        if ((grade == Rating.Again && resolved is not null && resolved.OverridesStep.GetValueOrDefault(grade))
            || (scheduledDays is { } days && days < graduatingInterval))
        {
            context.ResultCard.State = NextLearningState(card.State);
            context.ResultCard.ScheduleStatus = ScheduleStatus.Learning;
            if (step is { } st) context.ResultCard.Set(LearningStepField, Math.Max(0, st.NextStep));
        }
    }

    public override void Rollback<TState, TTime>(RollbackContext<TState, TTime> context, Action next)
    {
        next();
        context.ResultCard.Set(LearningStepField, context.Revlog.Get(LearningStepField));
    }

    private static ResolvedLearningSteps ResolveLearningSteps<TState, TTime>(NextIntervalContext<TState, TTime> context)
        where TState : class
        where TTime : struct
    {
        var candidate = context.Candidate;
        if (candidate.Items.TryGetValue(ResolvedStepsKey, out var cached) && cached is ResolvedLearningSteps existing) return existing;

        var card = context.Card;
        var config = context.Config;
        var stepConfig = GetStepsConfig(config);
        var stepList = card.State is State.Relearning or State.Review ? stepConfig.RelearningSteps : stepConfig.LearningSteps;
        var steps = LearningStepsCore.CalculateLearningSteps(stepConfig, card.State, card.Get(LearningStepField));
        var stepIntervals = new Dictionary<Rating, double>();
        foreach (var grade in Grades.All)
        {
            if (!steps.TryGetValue(grade, out var step)) continue;
            var minutes = Helpers.JsRound(Math.Max(0, step.ScheduledMinutes) * SecondsPerMinute) / SecondsPerMinute;
            if (minutes > 0) stepIntervals[grade] = minutes / MinutesPerDay;
        }

        var resolved = new ResolvedLearningSteps(steps, stepList.Count > 0, stepIntervals);
        candidate.Items[ResolvedStepsKey] = resolved;

        var nextInterval = candidate.NextInterval;
        var allowModelOverride = config.Get(AllowModelOverride);
        var graduatingInterval = config.Get(GraduatingInterval);
        candidate.NextInterval = (memoryState, desiredRetention) =>
        {
            if (candidate.FindGrade(memoryState) is not { } grade) return nextInterval(memoryState, desiredRetention);
            var interval = GetLearningStepInterval(resolved, grade, allowModelOverride, graduatingInterval,
                () => nextInterval(memoryState, desiredRetention));
            resolved.OverridesStep[grade] = resolved.StepIntervals.TryGetValue(grade, out var stepInterval) && interval != stepInterval;
            return interval;
        };
        return resolved;
    }

    /// <summary>
    /// Applies steps and graduation to a grade's interval. The model interval is only evaluated when a
    /// policy needs it. With steps <c>["1m","10m"]</c> / <c>["10m"]</c> and <c>graduatingInterval: 1</c>:
    /// New+Good(override off) = 10m; New+Again(on) = 1m; New+Hard(on, model 14h) = 14h; Review+Again(on, model 13d) = 1d (capped);
    /// last-step Learning+Good = 1d (graduates); Review+Good = at least 1d; with empty steps the model interval is kept.
    /// </summary>
    private static double GetLearningStepInterval(
        ResolvedLearningSteps resolved, Rating grade, bool allowModelOverride, double graduatingInterval, Func<double> modelInterval)
    {
        var hasStep = resolved.StepIntervals.TryGetValue(grade, out var stepInterval);
        if (grade == Rating.Again)
        {
            if (!hasStep) return modelInterval();
            // Again may follow the model, but never past graduation.
            return allowModelOverride ? Math.Max(stepInterval, Math.Min(modelInterval(), graduatingInterval)) : stepInterval;
        }

        // Hard, Good, and Easy without a step graduate or stay in review when steps are configured,
        // so they wait at least the graduating interval.
        if (!hasStep)
            return resolved.HasSteps ? Math.Max(modelInterval(), graduatingInterval) : modelInterval();
        return allowModelOverride ? Math.Max(stepInterval, modelInterval()) : stepInterval;
    }

    private static State NextLearningState(State state) => state switch
    {
        State.New => State.Learning,
        State.Review => State.Relearning,
        _ => state,
    };

    private sealed class ResolvedLearningSteps(LearningStepsResult steps, bool hasSteps, Dictionary<Rating, double> stepIntervals)
    {
        public LearningStepsResult Steps { get; } = steps;
        public bool HasSteps { get; } = hasSteps;
        /// <summary>Second-rounded step intervals in days, only for steps longer than zero.</summary>
        public Dictionary<Rating, double> StepIntervals { get; } = stepIntervals;
        /// <summary>Grades whose last resolved interval replaced their step with the model.</summary>
        public Dictionary<Rating, bool> OverridesStep { get; } = [];
    }
}

/// <summary>Typed access to the <c>learningStep</c> field of cards and review logs.</summary>
public static class LearningStepsExtensions
{
    extension<TState>(Card<TState> card) where TState : class
    {
        public int LearningStep => card.Get(LearningStepsMiddleware.LearningStepField);
    }

    extension<TState>(ReviewLog<TState> revlog) where TState : class
    {
        public int LearningStep => revlog.Get(LearningStepsMiddleware.LearningStepField);
    }
}
