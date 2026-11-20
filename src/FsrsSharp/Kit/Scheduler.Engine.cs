namespace FsrsSharp.Kit;

// Engine implementation of Scheduler<TState,TTime>
// (port of srs-kit scheduler/base.ts, scheduler-schema.ts and default-value.ts).
public sealed partial class Scheduler<TState, TTime>
{
    private const double DefaultDesiredRetention = 0.9;

    private readonly IReadOnlyList<FieldKey> _chronoConfigKeys;
    private readonly IReadOnlyList<FieldKey> _middlewareConfigKeys;
    private readonly IReadOnlyList<FieldKey> _cardInitKeys;
    private readonly IReadOnlyList<FieldKey> _cardKeys;
    private readonly IReadOnlyList<FieldKey> _revlogKeys;
    private readonly HashSet<string> _knownStatuses;
    private readonly IReadOnlyList<Action<NextIntervalContext<TState, TTime>, Action>?> _nextIntervalHandlers;
    private readonly IReadOnlyList<Action<ReviewContext<TState, TTime>, Action>?> _reviewIntervalHandlers;
    private readonly IReadOnlyList<Action<RollbackContext<TState, TTime>, Action>?> _rollbackHandlers;

    internal Scheduler(SchedulerDefinition<TState, TTime> definition, FieldBag config)
    {
        Definition = definition;
        var middlewares = definition.Middlewares;

        _chronoConfigKeys = definition.Chrono.ConfigKeys;
        _middlewareConfigKeys = [.. middlewares.SelectMany(m => m.ConfigKeys)];
        _cardInitKeys = [.. middlewares.SelectMany(m => m.CardInitInputKeys)];
        _cardKeys = [.. definition.Chrono.CardFields, .. middlewares.SelectMany(m => m.CardFields)];
        _revlogKeys = [.. definition.Chrono.RevlogFields, .. middlewares.SelectMany(m => m.RevlogFields)];
        _knownStatuses = [.. ScheduleStatus.BuiltIn.Concat(middlewares.SelectMany(m => m.ScheduleStatuses)).Select(s => s.Value)];

        Config = ParseConfig(definition, config);
        Model = definition.Model.Create(Config, new ModelCreateOptions(Bypass: true));
        Chrono = definition.Chrono.Create(Config);

        _nextIntervalHandlers = [.. middlewares.Select(m => (Action<NextIntervalContext<TState, TTime>, Action>?)m.NextInterval<TState, TTime>)];
        // Keep interval policies at their middleware's original onion position.
        _reviewIntervalHandlers =
        [
            .. middlewares.SelectMany(m => new Action<ReviewContext<TState, TTime>, Action>?[]
            {
                m.Review<TState, TTime>,
                (ctx, next) => m.NextInterval(ctx, next),
            }),
        ];
        _rollbackHandlers = [.. middlewares.Select(m => (Action<RollbackContext<TState, TTime>, Action>?)m.Rollback<TState, TTime>)];
    }

    // ------------------------------------------------------------------ schema: config

    private FieldBag ParseConfig(SchedulerDefinition<TState, TTime> definition, FieldBag? config)
    {
        if (config is null) throw new FsrsValidationException("Expected scheduler config object");

        // Model first (upstream order); it owns its normalization (migrate/clip) and messages.
        var result = definition.Model.ParseConfig(config).ToBuilder();
        foreach (var key in definition.Model.ConfigKeys)
        {
            if (!result.Contains(key) && key.HasDefault) result.SetRaw(key.Name, key.DefaultValueBoxed);
        }

        ParseKeys(config, _chronoConfigKeys, result, "config");
        ParseKeys(config, _middlewareConfigKeys, result, "config");

        var parsed = result.ToFieldBag();
        foreach (var middleware in definition.Middlewares)
        {
            var error = middleware.ValidateConfig(parsed);
            if (error is not null) throw new FsrsValidationException(error);
        }

        return parsed;
    }

    /// <summary>Copies <paramref name="keys"/> from <paramref name="source"/> into <paramref name="target"/>: validate, else default, else fail.</summary>
    private static void ParseKeys(FieldBag source, IReadOnlyList<FieldKey> keys, FieldBag.Builder target, string what)
    {
        foreach (var key in keys)
        {
            if (source.TryGetValue(key.Name, out var raw))
            {
                var error = key.Validate(raw);
                if (error is not null) throw new FsrsValidationException(error);
                target.SetRaw(key.Name, raw);
            }
            else if (key.HasDefault)
            {
                target.SetRaw(key.Name, key.DefaultValueBoxed);
            }
            else
            {
                throw new FsrsValidationException($"Missing required {what} field '{key.Name}'");
            }
        }
    }

    // ------------------------------------------------------------------ schema: card / revlog

    private string? ValidateStatus(ScheduleStatus? status)
    {
        if (status is not { Value: not null } s) return "Expected scheduleStatus string";
        return _knownStatuses.Contains(s.Value) ? null : "Expected known scheduleStatus";
    }

    private static string? ValidateState(State? state) =>
        state is { } s && Enum.IsDefined(s) ? null : "Expected state";

    private TState ValidateMemoryState(TState? memoryState)
    {
        if (memoryState is null) throw new FsrsValidationException("Expected memory state");
        var error = Definition.Model.ValidateMemoryState(memoryState);
        if (error is not null) throw new FsrsValidationException(error);
        return memoryState;
    }

    private Card<TState> ParseCard(TState? memoryState, State? state, ScheduleStatus? status, FieldBag? fields)
    {
        var memory = ValidateMemoryState(memoryState);
        var source = fields ?? FieldBag.Empty;
        var parsed = new FieldBag.Builder();
        ParseKeys(source, Definition.Chrono.CardFields, parsed, "card");

        var error = ValidateStatus(status) ?? ValidateState(state);
        if (error is not null) throw new FsrsValidationException(error);

        ParseKeys(source, [.. Definition.Middlewares.SelectMany(m => m.CardFields)], parsed, "card");
        return new Card<TState>(memory, state!.Value, status!.Value, parsed.ToFieldBag());
    }

    private Card<TState> ParseCard(Card<TState> card)
    {
        if (card is null) throw new FsrsValidationException("Expected card object");
        return ParseCard(card.MemoryState, card.State, card.ScheduleStatus, card.Fields);
    }

    private ReviewLog<TState> ParseRevlog(TState? memoryState, Rating? rating, State? state, ScheduleStatus? status, FieldBag? fields)
    {
        var memory = ValidateMemoryState(memoryState);
        var source = fields ?? FieldBag.Empty;
        var parsed = new FieldBag.Builder();
        ParseKeys(source, Definition.Chrono.RevlogFields, parsed, "revlog");

        var error = ValidateStatus(status) ?? ValidateState(state) ?? (rating is { } r && Grades.IsGrade(r) ? null : "Expected grade");
        if (error is not null) throw new FsrsValidationException(error);

        ParseKeys(source, [.. Definition.Middlewares.SelectMany(m => m.RevlogFields)], parsed, "revlog");
        return new ReviewLog<TState>(memory, rating!.Value, state!.Value, status!.Value, parsed.ToFieldBag());
    }

    private ReviewLog<TState> ParseRevlog(ReviewLog<TState> revlog)
    {
        if (revlog is null) throw new FsrsValidationException("Expected revlog object");
        return ParseRevlog(revlog.MemoryState, revlog.Rating, revlog.State, revlog.ScheduleStatus, revlog.Fields);
    }

    private FieldBag ParseCardInit(FieldBag? input)
    {
        var result = new FieldBag.Builder();
        if (input is null) return result.ToFieldBag();
        foreach (var key in _cardInitKeys)
        {
            if (input.TryGetValue(key.Name, out var raw))
            {
                var error = key.Validate(raw);
                if (error is not null) throw new FsrsValidationException(error);
                result.SetRaw(key.Name, raw);
            }
            else if (key.HasDefault)
            {
                result.SetRaw(key.Name, key.DefaultValueBoxed);
            }
        }

        return result.ToFieldBag();
    }

    private TTime ParseNow(TTime? now)
    {
        if (now is not { } value) return Chrono.Now();
        var error = KitSchemas.ValidateTime(value);
        return error is null ? value : throw new FsrsValidationException(error);
    }

    // ------------------------------------------------------------------ default values

    private Card<TState> CreateDefaultCard(CardDefaultContext<TState> context, TTime time)
    {
        var fields = new FieldBag.Builder();
        Definition.Chrono.WriteCardDefaults(fields, Config, time, null);
        foreach (var middleware in Definition.Middlewares) middleware.WriteCardDefaults(context, fields);
        return new Card<TState>(
            Definition.Model.DefaultMemoryState(Config),
            State.New,
            ScheduleStatus.New,
            fields.ToFieldBag());
    }

    public partial Card<TState> NewCard(TTime? now, FieldBag? input)
    {
        var initInput = ParseCardInit(input);
        return CreateDefaultCard(
            new CardDefaultContext<TState>(CardDefaultOperation.NewCard, Config, initInput, null),
            ParseNow(now));
    }

    public partial Card<TState> Forget(Card<TState> card, TTime? now)
    {
        var parsed = ParseCard(card);
        var time = ParseNow(now);
        return CreateDefaultCard(
            new CardDefaultContext<TState>(CardDefaultOperation.Forget, Config, FieldBag.Empty, parsed),
            time);
    }

    // ------------------------------------------------------------------ review pipeline

    private sealed record PreparedReview(
        Card<TState> Card,
        ChronoTimes<TTime> Time,
        double ElapsedDays,
        TState MemoryState,
        double Retrievability,
        ReviewCandidate<TState> Candidate);

    public partial ScheduleResult<TState> Review(Card<TState> card, Rating grade, TTime? now)
    {
        Grades.Validate(grade);
        var time = ParseNow(now);
        var prepared = PrepareParsedReview(ParseCard(card), time);
        return ParseReviewResult(RunReview(prepared, grade, time));
    }

    public partial SchedulePreview<TState> Preview(Card<TState> card, TTime? now)
    {
        var time = ParseNow(now);
        var prepared = PrepareParsedReview(ParseCard(card), time);
        return new SchedulePreview<TState>(grade =>
        {
            var (resultCard, revlog) = ParseReviewResult(RunReview(prepared, grade, time));
            return new PreviewItem<TState>(grade, resultCard, revlog);
        });
    }

    public partial IReadOnlyList<ScheduleResult<TState>> Forward(IReadOnlyList<SchedulerReview<TTime>> history, Card<TState>? initialCard)
    {
        ArgumentNullException.ThrowIfNull(history);
        // Nothing to replay: skip card initialization (which would read the clock) and validation.
        if (history.Count == 0) return [];

        var results = new ScheduleResult<TState>[history.Count];
        var card = ParseCard(initialCard ?? NewCard(history[0].ReviewTime, null));

        for (var i = 0; i < history.Count; i++)
        {
            var review = history[i];
            var prepared = PrepareParsedReview(card, review.ReviewTime);
            var result = ParseReviewResult(RunReview(prepared, review.Rating, review.ReviewTime));
            results[i] = result;
            card = result.Card;
        }

        return results;
    }

    private PreparedReview PrepareParsedReview(Card<TState> card, TTime now)
    {
        var memoryState = card.MemoryState;
        var time = Definition.Chrono.NormalizeReview(card.Fields, now);
        var rawElapsed = card.State == State.New ? 0 : Chrono.Difference(time.Previous ?? time.Current, now);
        var elapsedDays = KitSchemas.ParseElapsedDays(rawElapsed);
        var retrievability = Model.ForgettingCurve(memoryState, elapsedDays);
        return new PreparedReview(card, time, elapsedDays, memoryState, retrievability,
            CreateCandidate(memoryState, elapsedDays, retrievability));
    }

    private ReviewCandidate<TState> CreateCandidate(
        TState memoryState,
        double elapsedDays,
        double? retrievability = null,
        (Rating Grade, TState State)? initial = null,
        double desiredRetention = DefaultDesiredRetention)
    {
        var stateByGrade = new Dictionary<Rating, TState>();
        if (initial is { } seed) stateByGrade[seed.Grade] = seed.State;
        var gradeByState = new Dictionary<TState, Rating>(ReferenceEqualityComparer.Instance);

        TState Step(Rating grade)
        {
            if (!stateByGrade.TryGetValue(grade, out var next))
            {
                retrievability ??= Model.ForgettingCurve(memoryState, elapsedDays);
                next = Model.Step(new ModelStepInput<TState>(memoryState, grade, elapsedDays, retrievability));
                stateByGrade[grade] = next;
            }

            gradeByState[next] = grade;
            return next;
        }

        Rating? FindGrade(TState next) => gradeByState.TryGetValue(next, out var grade) ? grade : null;

        var intervalCache = new Dictionary<TState, Dictionary<double, double>>(ReferenceEqualityComparer.Instance);

        double NextInterval(TState next, double retention)
        {
            if (!intervalCache.TryGetValue(next, out var inner))
            {
                inner = [];
                intervalCache[next] = inner;
            }
            else if (inner.TryGetValue(retention, out var cached))
            {
                return cached;
            }

            var value = Model.NextInterval(next, retention);
            inner[retention] = value;
            return value;
        }

        return new ReviewCandidate<TState>(new DesiredRetentionByGrade(desiredRetention), Step, FindGrade, NextInterval);
    }

    private ReviewContext<TState, TTime> RunReview(PreparedReview prepared, Rating grade, TTime now)
    {
        var ctx = new ReviewContext<TState, TTime>(
            this, prepared.Card, grade, now, prepared.ElapsedDays, prepared.Retrievability, prepared.Candidate);

        MiddlewarePipeline.Run(_reviewIntervalHandlers, ctx, c =>
        {
            var memoryState = c.Candidate.Step(c.Grade);
            c.ScheduledDays ??= c.Candidate.NextInterval(
                memoryState,
                KitSchemas.ParseDesiredRetention(c.Candidate.DesiredRetention[c.Grade]));
            FinalizeReview(prepared, c, memoryState);
        });
        ApplyReviewChronoDefaults(prepared, ctx);
        return ctx;
    }

    private static void FinalizeReview(PreparedReview prepared, ReviewContext<TState, TTime> ctx, TState newMemoryState)
    {
        ctx.ResultCard.MemoryState = newMemoryState;
        ctx.ResultCard.State = State.Review;
        ctx.ResultCard.ScheduleStatus = ScheduleStatus.Review;

        ctx.ResultRevlog.MemoryState = prepared.MemoryState;
        ctx.ResultRevlog.Rating = ctx.Grade;
        ctx.ResultRevlog.State = prepared.Card.State;
        ctx.ResultRevlog.ScheduleStatus = prepared.Card.ScheduleStatus;
    }

    private void ApplyReviewChronoDefaults(PreparedReview prepared, ReviewContext<TState, TTime> ctx)
    {
        if (ctx.ScheduledDays is not { } scheduledDays)
            throw new FsrsOperationException("Expected scheduledDays after review middleware");

        Definition.Chrono.WriteCardDefaults(ctx.ResultCard.Fields, Config, Chrono.Add(ctx.Now, scheduledDays), ctx.Now);
        Definition.Chrono.WriteRevlogDefaults(
            ctx.ResultRevlog.Fields, Config, prepared.Time.Current, new ChronoTimes<TTime>(prepared.Time.Previous, ctx.Now));
    }

    private ScheduleResult<TState> ParseReviewResult(ReviewContext<TState, TTime> ctx) => new(
        ParseCard(ctx.ResultCard.MemoryState, ctx.ResultCard.State, ctx.ResultCard.ScheduleStatus, ctx.ResultCard.Fields.ToFieldBag()),
        ParseRevlog(
            ctx.ResultRevlog.MemoryState, ctx.ResultRevlog.Rating, ctx.ResultRevlog.State,
            ctx.ResultRevlog.ScheduleStatus, ctx.ResultRevlog.Fields.ToFieldBag()));

    // ------------------------------------------------------------------ nextInterval

    public partial double NextInterval(TState memoryState, double desiredRetention, NextIntervalQuery<TState> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var grade = Grades.Validate(context.Grade);
        var elapsedDays = KitSchemas.ParseElapsedDays(context.ElapsedDays);
        var retention = KitSchemas.ParseDesiredRetention(desiredRetention);
        var card = ParseCard(context.Card);
        var nextMemoryState = ValidateMemoryState(memoryState);

        var candidate = CreateCandidate(card.MemoryState, elapsedDays, null, (grade, nextMemoryState), retention);
        var ctx = new NextIntervalContext<TState, TTime>(this, card, grade, retention, elapsedDays, candidate);
        MiddlewarePipeline.Run(_nextIntervalHandlers, ctx, c =>
        {
            var state = c.Candidate.Step(c.Grade);
            c.ScheduledDays ??= c.Candidate.NextInterval(
                state,
                KitSchemas.ParseDesiredRetention(c.Candidate.DesiredRetention[c.Grade]));
        });
        return ctx.ScheduledDays is { } days
            ? KitSchemas.ParseScheduledDays(days)
            : throw new FsrsValidationException(KitSchemas.ScheduledDaysMessage);
    }

    // ------------------------------------------------------------------ rollback

    public partial Card<TState> Rollback(Card<TState> card, ReviewLog<TState> revlog)
    {
        var parsedRevlog = ParseRevlog(revlog);
        var parsedCard = ParseCard(card);

        var ctx = new RollbackContext<TState, TTime>(this, parsedCard, parsedRevlog);
        MiddlewarePipeline.Run(_rollbackHandlers, ctx, c =>
        {
            c.ResultCard.MemoryState = c.Revlog.MemoryState;
            c.ResultCard.State = c.Revlog.State;
            c.ResultCard.ScheduleStatus = c.Revlog.ScheduleStatus;
        });
        ApplyRollbackChronoDefaults(ctx, parsedRevlog);

        return ParseCard(ctx.ResultCard.MemoryState, ctx.ResultCard.State, ctx.ResultCard.ScheduleStatus, ctx.ResultCard.Fields.ToFieldBag());
    }

    private void ApplyRollbackChronoDefaults(RollbackContext<TState, TTime> ctx, ReviewLog<TState> revlog)
    {
        if (Definition.Chrono.CardFields.Count == 0) return;
        var times = Definition.Chrono.NormalizeRollback(revlog.Fields);
        var isNew = revlog.State == State.New;
        Definition.Chrono.WriteCardDefaults(ctx.ResultCard.Fields, Config, times.Current, isNew ? null : times.Previous);
    }
}
