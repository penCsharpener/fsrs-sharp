namespace FsrsSharp;

/// <summary>
/// Used when <see cref="FsrsParameters.EnableShortTerm"/> is false. Ported from ts-fsrs's
/// <c>impl/long_term_scheduler.ts</c>: skips (re)learning steps entirely — every grade
/// (including Again) produces a day-scale Review interval straight away.
/// See https://github.com/open-spaced-repetition/ts-fsrs/issues/98#issuecomment-2241923194
/// </summary>
public sealed class LongTermScheduler : AbstractScheduler
{
    public LongTermScheduler(Card card, DateTime now, FsrsAlgorithm algorithm, SeedStrategy? seedStrategy = null)
        : base(card, now, algorithm, seedStrategy)
    {
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

        Current.ScheduledDays = 0;
        Current.ElapsedDays = 0;

        const int firstInterval = 0;
        var nextAgain = NextDs(firstInterval, Rating.Again);
        var nextHard = NextDs(firstInterval, Rating.Hard);
        var nextGood = NextDs(firstInterval, Rating.Good);
        var nextEasy = NextDs(firstInterval, Rating.Easy);

        ApplyIntervals(nextAgain, nextHard, nextGood, nextEasy, firstInterval);
        MarkReview(nextAgain, nextHard, nextGood, nextEasy);
        StoreAll(nextAgain, nextHard, nextGood, nextEasy);
        return NextByGrade[grade];
    }

    /// <summary>Learning/Relearning cards are scheduled exactly like Review cards when short-term is disabled.</summary>
    protected override RecordLogItem LearningState(Rating grade) => ReviewState(grade);

    protected override RecordLogItem ReviewState(Rating grade)
    {
        if (NextByGrade.TryGetValue(grade, out var exist)) return exist;

        var interval = ElapsedDays;
        var retrievability = Algorithm.ForgettingCurve(interval, Current.Stability);
        var nextAgain = NextDs(interval, Rating.Again, retrievability);
        var nextHard = NextDs(interval, Rating.Hard, retrievability);
        var nextGood = NextDs(interval, Rating.Good, retrievability);
        var nextEasy = NextDs(interval, Rating.Easy, retrievability);

        ApplyIntervals(nextAgain, nextHard, nextGood, nextEasy, interval);
        MarkReview(nextAgain, nextHard, nextGood, nextEasy);
        nextAgain.Lapses += 1;

        StoreAll(nextAgain, nextHard, nextGood, nextEasy);
        return NextByGrade[grade];
    }

    private void ApplyIntervals(Card nextAgain, Card nextHard, Card nextGood, Card nextEasy, double interval)
    {
        var againInterval = Algorithm.NextInterval(nextAgain.Stability, interval);
        var hardInterval = Algorithm.NextInterval(nextHard.Stability, interval);
        var goodInterval = Algorithm.NextInterval(nextGood.Stability, interval);
        var easyInterval = Algorithm.NextInterval(nextEasy.Stability, interval);

        againInterval = Math.Min(againInterval, hardInterval);
        hardInterval = Math.Max(hardInterval, againInterval + 1);
        goodInterval = Math.Max(goodInterval, hardInterval + 1);
        easyInterval = Math.Max(easyInterval, goodInterval + 1);

        nextAgain.ScheduledDays = againInterval;
        nextAgain.Due = Helpers.DateScheduler(ReviewTime, againInterval, isDay: true);

        nextHard.ScheduledDays = hardInterval;
        nextHard.Due = Helpers.DateScheduler(ReviewTime, hardInterval, isDay: true);

        nextGood.ScheduledDays = goodInterval;
        nextGood.Due = Helpers.DateScheduler(ReviewTime, goodInterval, isDay: true);

        nextEasy.ScheduledDays = easyInterval;
        nextEasy.Due = Helpers.DateScheduler(ReviewTime, easyInterval, isDay: true);
    }

    private static void MarkReview(Card nextAgain, Card nextHard, Card nextGood, Card nextEasy)
    {
        foreach (var c in new[] { nextAgain, nextHard, nextGood, nextEasy })
        {
            c.State = State.Review;
            c.LearningSteps = 0;
        }
    }

    private void StoreAll(Card nextAgain, Card nextHard, Card nextGood, Card nextEasy)
    {
        NextByGrade[Rating.Again] = new RecordLogItem(nextAgain, BuildLog(Rating.Again));
        NextByGrade[Rating.Hard] = new RecordLogItem(nextHard, BuildLog(Rating.Hard));
        NextByGrade[Rating.Good] = new RecordLogItem(nextGood, BuildLog(Rating.Good));
        NextByGrade[Rating.Easy] = new RecordLogItem(nextEasy, BuildLog(Rating.Easy));
    }
}
