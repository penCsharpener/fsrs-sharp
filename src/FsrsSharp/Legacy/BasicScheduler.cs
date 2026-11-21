using FsrsSharp.Kit;
using FsrsSharp.Middlewares;
using FsrsSharp.Models;

namespace FsrsSharp.Legacy;

/// <summary>
/// The default scheduler, used when <see cref="FsrsParameters.EnableShortTerm"/> is true
/// (the default). Ported from ts-fsrs's <c>legacy/impl/basic_scheduler.ts</c>: routes New and
/// (Re)Learning cards through the configured (re)learning steps (minutes-scale intervals)
/// before they graduate to day-scale Review intervals.
/// </summary>
public sealed class BasicScheduler : AbstractScheduler
{
    private const double SecondsPerMinute = 60;

    private readonly LearningStepsResolver _learningSteps;
    private readonly LearningStepsConfig _learningStepsConfig;
    private LearningStepsResult? _learningStepsResult;

    public BasicScheduler(Card card, DateTime now, IModelCore<FsrsState> model, FsrsParameters parameters, LearningStepsResolver? learningSteps = null)
        : base(card, now, model, parameters)
    {
        _learningStepsConfig = new LearningStepsConfig(parameters.LearningSteps, parameters.RelearningSteps);
        _learningSteps = learningSteps ?? LearningStepsCore.CalculateLearningSteps;
    }

    private (double ScheduledMinutes, int NextSteps) GetLearningInfo(Card card, Rating grade)
    {
        var steps = _learningStepsResult ??= _learningSteps(_learningStepsConfig, card.State, card.LearningSteps);
        steps.TryGetValue(grade, out var info);
        var scheduledMinutes = Helpers.JsRound(Math.Max(0, info.ScheduledMinutes) * SecondsPerMinute) / SecondsPerMinute;
        return (scheduledMinutes, Math.Max(0, info.NextStep));
    }

    private void ApplyLearningSteps(Card nextCard, Rating grade, State toState)
    {
        var (scheduledMinutes, nextSteps) = GetLearningInfo(Current, grade);

        if (scheduledMinutes is > 0 and < 1440 /* 1440 minutes = 1 day */)
        {
            nextCard.LearningSteps = nextSteps;
            nextCard.ScheduledDays = 0;
            nextCard.State = toState;
            nextCard.Due = LegacyHelpers.DateScheduler(ReviewTime, scheduledMinutes);
        }
        else
        {
            nextCard.State = State.Review;
            if (scheduledMinutes >= 1440)
            {
                nextCard.LearningSteps = nextSteps;
                nextCard.Due = LegacyHelpers.DateScheduler(ReviewTime, scheduledMinutes);
                nextCard.ScheduledDays = (int)Math.Floor(scheduledMinutes / 1440);
            }
            else
            {
                nextCard.LearningSteps = 0;
                var interval = SchedulerNextInterval(nextCard, ElapsedDays);
                nextCard.ScheduledDays = interval;
                nextCard.Due = LegacyHelpers.DateScheduler(ReviewTime, interval, isDay: true);
            }
        }
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
        var retrievability = Model.ForgettingCurve(new FsrsState(Current.Stability, Current.Difficulty), interval);
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
        var days = MonotonicInterval.CalculateScheduleDays(
            [
                SchedulerNextInterval(nextHard, interval),
                SchedulerNextInterval(nextGood, interval),
                SchedulerNextInterval(nextEasy, interval),
            ],
            Parameters.MaximumInterval);

        Apply(nextHard, (int)days[0]);
        Apply(nextGood, (int)days[1]);
        Apply(nextEasy, (int)days[2]);

        void Apply(Card card, int scheduledDays)
        {
            card.ScheduledDays = scheduledDays;
            card.Due = LegacyHelpers.DateScheduler(ReviewTime, scheduledDays, isDay: true);
        }
    }
}
