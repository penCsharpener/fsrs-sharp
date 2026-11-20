namespace FsrsSharp;

/// <summary>
/// The default scheduler, used when <see cref="FsrsParameters.EnableShortTerm"/> is true
/// (the default). Ported from ts-fsrs's <c>impl/basic_scheduler.ts</c>: routes New and
/// (Re)Learning cards through the configured (re)learning steps (minutes-scale intervals)
/// before they graduate to day-scale Review intervals.
/// </summary>
public sealed class BasicScheduler : AbstractScheduler
{
    private readonly LearningStepsStrategy _learningStepsStrategy;

    public BasicScheduler(Card card, DateTime now, FsrsAlgorithm algorithm, SeedStrategy? seedStrategy = null, LearningStepsStrategy? learningStepsStrategy = null)
        : base(card, now, algorithm, seedStrategy)
    {
        _learningStepsStrategy = learningStepsStrategy ?? Strategies.DefaultLearningSteps;
    }

    private (double ScheduledMinutes, int NextSteps) GetLearningInfo(Card card, Rating grade)
    {
        var steps = _learningStepsStrategy(Algorithm.Parameters, card.State, card.LearningSteps);
        steps.TryGetValue(grade, out var info);
        return (Math.Max(0, info.ScheduledMinutes), Math.Max(0, info.NextStep));
    }

    /// <summary>Applies the (re)learning steps schedule to <paramref name="nextCard"/> in place.</summary>
    private void ApplyLearningSteps(Card nextCard, Rating grade, State toState)
    {
        var (scheduledMinutes, nextSteps) = GetLearningInfo(Current, grade);

        if (scheduledMinutes is > 0 and < 1440 /* 1440 minutes = 1 day */)
        {
            nextCard.LearningSteps = nextSteps;
            nextCard.ScheduledDays = 0;
            nextCard.State = toState;
            nextCard.Due = Helpers.DateScheduler(ReviewTime, Helpers.JsRound(scheduledMinutes));
        }
        else
        {
            nextCard.State = State.Review;
            if (scheduledMinutes >= 1440)
            {
                nextCard.LearningSteps = nextSteps;
                nextCard.Due = Helpers.DateScheduler(ReviewTime, Helpers.JsRound(scheduledMinutes));
                nextCard.ScheduledDays = (int)Math.Floor(scheduledMinutes / 1440);
            }
            else
            {
                nextCard.LearningSteps = 0;
                var interval = Algorithm.NextInterval(nextCard.Stability, ElapsedDays);
                nextCard.ScheduledDays = interval;
                nextCard.Due = Helpers.DateScheduler(ReviewTime, interval, isDay: true);
            }
        }
    }

    private Card NextDs(double t, Rating grade, double? r = null)
    {
        var nextState = Algorithm.NextState(new FsrsMemoryState(Current.Stability, Current.Difficulty), t, grade, r);
        var card = Current.Clone();
        card.Difficulty = nextState.Difficulty;
        card.Stability = nextState.Stability;
        return card;
    }

    protected override RecordLogItem NewState(Rating grade)
    {
        if (NextByGrade.TryGetValue(grade, out var exist)) return exist;

        var next = NextDs(ElapsedDays, grade);
        ApplyLearningSteps(next, grade, State.Learning);
        var item = new RecordLogItem(next, BuildLog(grade));
        NextByGrade[grade] = item;
        return item;
    }

    protected override RecordLogItem LearningState(Rating grade)
    {
        if (NextByGrade.TryGetValue(grade, out var exist)) return exist;

        var next = NextDs(ElapsedDays, grade);
        ApplyLearningSteps(next, grade, Last.State /* Learning or Relearning */);
        var item = new RecordLogItem(next, BuildLog(grade));
        NextByGrade[grade] = item;
        return item;
    }

    protected override RecordLogItem ReviewState(Rating grade)
    {
        if (NextByGrade.TryGetValue(grade, out var exist)) return exist;

        var interval = ElapsedDays;
        var retrievability = Algorithm.ForgettingCurve(interval, Current.Stability);
        var nextAgain = NextDs(interval, Rating.Again, retrievability);
        var nextHard = NextDs(interval, Rating.Hard, retrievability);
        var nextGood = NextDs(interval, Rating.Good, retrievability);
        var nextEasy = NextDs(interval, Rating.Easy, retrievability);

        ApplyReviewIntervals(nextHard, nextGood, nextEasy, interval);
        foreach (var c in new[] { nextHard, nextGood, nextEasy })
        {
            c.State = State.Review;
            c.LearningSteps = 0;
        }

        ApplyLearningSteps(nextAgain, Rating.Again, State.Relearning);
        nextAgain.Lapses += 1;

        var itemAgain = new RecordLogItem(nextAgain, BuildLog(Rating.Again));
        var itemHard = new RecordLogItem(nextHard, BuildLog(Rating.Hard));
        var itemGood = new RecordLogItem(nextGood, BuildLog(Rating.Good));
        var itemEasy = new RecordLogItem(nextEasy, BuildLog(Rating.Easy));

        NextByGrade[Rating.Again] = itemAgain;
        NextByGrade[Rating.Hard] = itemHard;
        NextByGrade[Rating.Good] = itemGood;
        NextByGrade[Rating.Easy] = itemEasy;
        return NextByGrade[grade];
    }

    private void ApplyReviewIntervals(Card nextHard, Card nextGood, Card nextEasy, double interval)
    {
        var hardInterval = Algorithm.NextInterval(nextHard.Stability, interval);
        var goodInterval = Algorithm.NextInterval(nextGood.Stability, interval);
        hardInterval = Math.Min(hardInterval, goodInterval);
        goodInterval = Math.Max(goodInterval, hardInterval + 1);
        var easyInterval = Math.Max(Algorithm.NextInterval(nextEasy.Stability, interval), goodInterval + 1);

        nextHard.ScheduledDays = hardInterval;
        nextHard.Due = Helpers.DateScheduler(ReviewTime, hardInterval, isDay: true);

        nextGood.ScheduledDays = goodInterval;
        nextGood.Due = Helpers.DateScheduler(ReviewTime, goodInterval, isDay: true);

        nextEasy.ScheduledDays = easyInterval;
        nextEasy.Due = Helpers.DateScheduler(ReviewTime, easyInterval, isDay: true);
    }
}
