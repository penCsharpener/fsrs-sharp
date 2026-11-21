namespace FsrsSharp;

/// <summary>Options for <see cref="DefaultScheduler"/> (ts-fsrs <c>DefaultSchedulerOptions</c>). Every property is optional.</summary>
public sealed record DefaultSchedulerOptions
{
    /// <summary>Model weights. Older layouts are migrated to the chosen version's layout. Defaults to the version's default weights.</summary>
    public IReadOnlyList<double>? Weights { get; init; }

    /// <summary>Enable explicit learning steps. FSRS-7 always retains its intrinsic fast trace.</summary>
    public bool EnableShortTerm { get; init; } = true;

    /// <summary>Target retention in (0, 1). Defaults to 0.9.</summary>
    public double DesiredRetention { get; init; } = 0.9;

    /// <summary>Defaults to <c>["1m", "10m"]</c>.</summary>
    public IReadOnlyList<string>? LearningSteps { get; init; }

    /// <summary>Defaults to <c>["10m"]</c>.</summary>
    public IReadOnlyList<string>? RelearningSteps { get; init; }

    /// <summary>Minimum days for a review card; shorter intervals stay in (re)learning. Defaults to 1.</summary>
    public double? GraduatingInterval { get; init; }

    /// <summary>Let longer model intervals override learning steps; defaults to true for FSRS-7 only.</summary>
    public bool? AllowModelOverride { get; init; }

    /// <summary>Apply deterministic, <c>cardId</c>-seeded fuzzing to review intervals.</summary>
    public bool EnableFuzz { get; init; }

    /// <summary>Upper bound for review intervals in days. Defaults to 36500.</summary>
    public double MaximumInterval { get; init; } = 36_500;

    /// <summary>Whether forget clears reps and lapses; defaults to true.</summary>
    public bool? ClearStatsOnForget { get; init; }
}
