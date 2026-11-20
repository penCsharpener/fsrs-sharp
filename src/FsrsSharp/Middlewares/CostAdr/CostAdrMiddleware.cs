using FsrsSharp.Kit;
using FsrsSharp.Models;

namespace FsrsSharp.Middlewares;

/// <summary>
/// Cost-ADR retention policy (ts-fsrs <c>schedulerCostAdrMiddleware</c>). Use instead of a fixed
/// desired-retention middleware; interval limits belong to the maximum-interval middleware. Requires an
/// FSRS memory state (<see cref="FsrsState"/>). Config: <c>costAdrPolicy</c> and <c>goalCostWeight</c>
/// (finite, in [0, 1024]).
/// </summary>
public sealed class CostAdrMiddleware : Middleware
{
    private sealed class RetentionCache(DesiredRetentionByGrade retentions)
    {
        public DesiredRetentionByGrade Retentions { get; } = retentions;
        public int LastGrade { get; set; }
    }

    private static readonly object RetentionsKey = new();

    public static FieldKey<CostAdrPolicy> Policy { get; } = new("costAdrPolicy", v => CostAdrSchema.ValidatePolicy(v));

    public static FieldKey<double> GoalCostWeight { get; } = new("goalCostWeight", CostAdrSchema.ValidateGoalCostWeight);

    public static CostAdrMiddleware Instance { get; } = new();

    private CostAdrMiddleware() { }

    public override string Name => "ts-fsrs.cost-adr";
    public override IReadOnlyList<FieldKey> ConfigKeys { get; } = [Policy, GoalCostWeight];

    public override void NextInterval<TState, TTime>(NextIntervalContext<TState, TTime> context, Action next)
    {
        var policy = context.Config.Get(Policy);
        var goalCostWeight = context.Config.Get(GoalCostWeight);
        var candidate = context.Candidate;
        if (!candidate.Items.TryGetValue(RetentionsKey, out var existing))
        {
            existing = new RetentionCache(candidate.DesiredRetention);
            candidate.Items[RetentionsKey] = existing;
        }
        var cached = (RetentionCache)existing!;

        foreach (var grade in Grades.All)
        {
            if (grade > context.Grade) break;
            if ((int)grade <= cached.LastGrade) continue;
            if ((object)candidate.Step(grade) is not FsrsState state || FsrsState.Validate(state) is not null)
                throw new FsrsValidationException("Expected FSRS memory state");
            cached.Retentions[grade] = CostAdrCore.EvaluateRetention(policy, state, goalCostWeight);
            cached.LastGrade = (int)grade;
        }
        next();
    }
}
