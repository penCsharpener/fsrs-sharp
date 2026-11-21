using FsrsSharp.Chrono;
using FsrsSharp.Kit;
using FsrsSharp.Middlewares;
using FsrsSharp.Models;
using FsrsSharp.Models.Fsrs3;
using FsrsSharp.Models.Fsrs4;
using FsrsSharp.Models.Fsrs4Dot5;
using FsrsSharp.Models.Fsrs5;
using FsrsSharp.Models.Fsrs6;
using FsrsSharp.Models.Fsrs7;

namespace FsrsSharp;

/// <summary>
/// The ready-made ts-fsrs scheduler presets (<c>DefaultScheduler()</c> and <c>getSchedulerPreset()</c>):
/// a <see cref="DateTimeChrono"/> scheduler with the middlewares desired retention, fuzzing, stats,
/// scheduled days, learning steps, maximum interval and monotonic interval, over any FSRS model version.
/// </summary>
public static class DefaultScheduler
{
    private static readonly Lazy<SchedulerDefinition<Fsrs7State, DateTime>> Fsrs7Preset =
        new(() => Apply(SchedulerDefinition.Define(Fsrs7Model.Instance, DateTimeChrono.Instance)));

    private static readonly Lazy<SchedulerDefinition<FsrsState, DateTime>>[] WidenedPresets =
    [
        Widened(Fsrs3Model.Instance),
        Widened(Fsrs4Model.Instance),
        Widened(Fsrs4Dot5Model.Instance),
        Widened(Fsrs5Model.Instance),
        Widened(Fsrs6Model.Instance),
        Widened(Fsrs7ModelAdapter.Instance),
    ];

    /// <summary>The FSRS-7 preset definition (strongly typed memory state). Cached.</summary>
    public static SchedulerDefinition<Fsrs7State, DateTime> Fsrs7Definition => Fsrs7Preset.Value;

    /// <summary>
    /// The preset definition of <paramref name="version"/> with a widened <see cref="FsrsState"/> memory state
    /// (FSRS-7 cards carry an <see cref="Fsrs7State"/> at runtime). Cached.
    /// </summary>
    /// <exception cref="FsrsValidationException">Unsupported version.</exception>
    public static SchedulerDefinition<FsrsState, DateTime> GetDefinition(FsrsVersion version) =>
        WidenedPresets[Index(version)].Value;

    /// <summary>The weights migrator of <paramref name="version"/> (upstream <c>migrateParameters</c>).</summary>
    /// <param name="version">The FSRS version.</param>
    /// <param name="weights">The weights to migrate; <c>null</c> or empty yields the default weights.</param>
    /// <param name="numRelearningSteps">Accepted for upstream parity; the migrators do not use it.</param>
    /// <param name="enableShortTerm">Accepted for upstream parity; the migrators do not use it.</param>
    /// <exception cref="FsrsValidationException">Unsupported version, or weights that cannot be migrated.</exception>
    public static IReadOnlyList<double> MigrateParameters(
        FsrsVersion version,
        IReadOnlyList<double>? weights,
        int numRelearningSteps,
        bool enableShortTerm) =>
        version switch
        {
            FsrsVersion.Fsrs3 => Fsrs3Parameters.Migrate(weights),
            FsrsVersion.Fsrs4 => Fsrs4Parameters.Migrate(weights),
            FsrsVersion.Fsrs4Dot5 => Fsrs4Dot5Parameters.Migrate(weights),
            FsrsVersion.Fsrs5 => Fsrs5Parameters.Migrate(weights),
            FsrsVersion.Fsrs6 => Fsrs6Parameters.Migrate(weights),
            FsrsVersion.Fsrs7 => Fsrs7Parameters.Migrate(weights),
            _ => throw Unsupported(version),
        };

    /// <summary>Creates the default (FSRS-7) scheduler.</summary>
    /// <exception cref="FsrsValidationException">An option is invalid.</exception>
    public static Scheduler<Fsrs7State, DateTime> Create(DefaultSchedulerOptions? options = null)
    {
        options ??= new DefaultSchedulerOptions();
        return Fsrs7Definition.Create(BuildConfig(FsrsVersion.Fsrs7, options));
    }

    /// <summary>Creates a scheduler for <paramref name="version"/> with a widened memory state type.</summary>
    /// <exception cref="FsrsValidationException">Unsupported version, or an option is invalid.</exception>
    public static Scheduler<FsrsState, DateTime> Create(FsrsVersion version, DefaultSchedulerOptions? options = null)
    {
        options ??= new DefaultSchedulerOptions();
        return GetDefinition(version).Create(BuildConfig(version, options));
    }

    /// <summary>Upstream default-scheduler.ts config mapping (internal so tests can pair it with instrumented models).</summary>
    internal static FieldBag.Builder BuildConfig(FsrsVersion version, DefaultSchedulerOptions options)
    {
        var isFsrs7 = version == FsrsVersion.Fsrs7;
        var learningSteps = options.LearningSteps ?? LearningStepsMiddleware.DefaultLearningSteps;
        var relearningSteps = options.RelearningSteps ?? LearningStepsMiddleware.DefaultRelearningSteps;
        var migrated = MigrateParameters(
            version,
            options.Weights is null ? null : [.. options.Weights],
            relearningSteps.Count,
            options.EnableShortTerm);

        var config = new FieldBag.Builder()
            .Set(KitSchemas.FractionalDays, isFsrs7)
            .Set(Fsrs6Model.Weights, migrated)
            .Set(Fsrs6Model.EnableShortTerm, options.EnableShortTerm)
            .Set(Fsrs6Model.NumRelearningSteps, relearningSteps.Count)
            .Set(LearningStepsMiddleware.LearningSteps, [.. learningSteps])
            .Set(LearningStepsMiddleware.RelearningSteps, [.. relearningSteps])
            .Set(LearningStepsMiddleware.AllowModelOverride, options.AllowModelOverride ?? isFsrs7)
            .Set(FuzzingMiddleware.EnableFuzz, options.EnableFuzz)
            .Set(MaximumIntervalMiddleware.MaximumInterval, options.MaximumInterval);
        config.SetRaw(DesiredRetentionMiddleware.DesiredRetention.Name, options.DesiredRetention);
        if (options.GraduatingInterval is { } graduatingInterval)
            config.Set(LearningStepsMiddleware.GraduatingInterval, graduatingInterval);
        if (options.ClearStatsOnForget is { } clearStatsOnForget)
            config.Set(StatsMiddleware.ClearStatsOnForget, clearStatsOnForget);
        return config;
    }

    private static Lazy<SchedulerDefinition<FsrsState, DateTime>> Widened(IModel<FsrsState> model) =>
        new(() => Apply(SchedulerDefinition.Define(model, DateTimeChrono.Instance)));

    internal static SchedulerDefinition<TState, DateTime> Apply<TState>(SchedulerDefinition<TState, DateTime> definition)
        where TState : class =>
        definition.Use(
            DesiredRetentionMiddleware.Instance,
            FuzzingMiddleware.Instance,
            StatsMiddleware.Instance,
            ScheduledDaysMiddleware.Instance,
            LearningStepsMiddleware.Instance,
            MaximumIntervalMiddleware.Instance,
            MonotonicIntervalMiddleware.Instance);

    private static int Index(FsrsVersion version) =>
        Enum.IsDefined(version) ? (int)version : throw Unsupported(version);

    private static FsrsValidationException Unsupported(FsrsVersion version) =>
        new($"Unsupported FSRS version \"{version}\"");
}
