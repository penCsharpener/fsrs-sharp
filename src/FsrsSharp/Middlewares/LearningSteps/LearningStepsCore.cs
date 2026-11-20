using System.Globalization;
using System.Text.RegularExpressions;

namespace FsrsSharp.Middlewares;

/// <summary>The (re)learning step lists the resolver works on (upstream <c>LearningStepsConfig</c>). Steps are step units such as <c>"1m"</c>, <c>"1.5h"</c>, <c>"2d"</c>.</summary>
public sealed record LearningStepsConfig(IReadOnlyList<string> LearningSteps, IReadOnlyList<string> RelearningSteps);

/// <summary>One grade's step outcome: wait <see cref="ScheduledMinutes"/>, then continue at <see cref="NextStep"/>.</summary>
public readonly record struct LearningStepSchedule(double ScheduledMinutes, int NextStep);

/// <summary>Per-grade schedule; grades without an entry have no step (upstream <c>LearningStepsResult</c>).</summary>
public sealed class LearningStepsResult : Dictionary<Rating, LearningStepSchedule>;

/// <summary>Resolves the grade-specific step schedule (upstream <c>LearningStepsResolver</c>).</summary>
public delegate LearningStepsResult LearningStepsResolver(LearningStepsConfig config, State state, int learningStep);

/// <summary>Port of <c>learning-steps/core.ts</c>.</summary>
public static partial class LearningStepsCore
{
    [GeneratedRegex(@"^(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?$")]
    private static partial Regex DecimalPattern { get; }

    /// <summary>Converts a step unit (<c>"1m"</c>, <c>"1.5h"</c>, <c>"2d"</c>) to minutes.</summary>
    /// <exception cref="FsrsValidationException">Malformed value, unknown unit or arithmetic overflow.</exception>
    public static double ConvertStepUnitToMinutes(string step)
    {
        if (step is null || step.Length < 2) throw new FsrsValidationException($"Invalid step value: {step}");

        var unit = step[^1];
        var numericPart = step[..^1];
        if (!DecimalPattern.IsMatch(numericPart)) throw new FsrsValidationException($"Invalid step value: {step}");

        if (!double.TryParse(numericPart, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
            throw new FsrsValidationException($"Invalid step value: {step}");

        var minutes = unit switch
        {
            'm' => value,
            'h' => value * 60,
            'd' => value * 1440,
            _ => throw new FsrsValidationException($"Invalid step unit: {step}, expected m/h/d"),
        };

        if (!double.IsFinite(minutes) || !double.IsFinite(minutes * 60)) throw new FsrsValidationException($"Invalid step value: {step}");
        return minutes;
    }

    /// <summary>Calculates the grade-specific schedule for the current learning step (upstream <c>calculateLearningSteps</c>).</summary>
    public static LearningStepsResult CalculateLearningSteps(LearningStepsConfig config, State state, int learningStep)
    {
        var steps = state is State.Relearning or State.Review ? config.RelearningSteps : config.LearningSteps;
        var result = new LearningStepsResult();
        var stepsLength = steps.Count;
        if (stepsLength == 0 || learningStep >= stepsLength) return result;

        var learningStepUnit = steps[Math.Max(0, learningStep)];

        if (state == State.Review)
        {
            result[Rating.Again] = new(ConvertStepUnitToMinutes(learningStepUnit), 0);
            return result;
        }

        var firstMinutes = ConvertStepUnitToMinutes(steps[0]);
        double? secondMinutes = stepsLength > 1 ? ConvertStepUnitToMinutes(steps[1]) : null;
        var hardMinutes = secondMinutes is { } second ? (firstMinutes + second) / 2 : firstMinutes * 1.5;
        result[Rating.Again] = new(firstMinutes, 0);
        result[Rating.Hard] = new(hardMinutes, learningStep);

        var nextIndex = learningStep + 1;
        if (nextIndex >= 0 && nextIndex < stepsLength && !string.IsNullOrEmpty(steps[nextIndex]))
        {
            var nextMinutes = learningStep == 0 && secondMinutes is { } s2 ? s2 : ConvertStepUnitToMinutes(steps[nextIndex]);
            if (nextMinutes > 0) result[Rating.Good] = new(nextMinutes, nextIndex);
        }

        return result;
    }
}
