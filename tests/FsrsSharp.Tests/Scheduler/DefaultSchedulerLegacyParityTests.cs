using Xunit;
using AwesomeAssertions;
using FsrsSharp.Chrono;
using FsrsSharp.Kit;
using FsrsSharp.Middlewares;
using FsrsSharp.Models;
using FsrsSharp.Models.Fsrs4;
using FsrsSharp.Models.Fsrs5;
using FsrsSharp.Models.Fsrs6;
using FsrsSharp.Tests.Middlewares;
using static FsrsSharp.Tests.Scheduler.LegacyParityHelpers;
using static FsrsSharp.Tests.Scheduler.SchedulerTestSupport;

namespace FsrsSharp.Tests.Scheduler;

/// <summary>
/// Port of ts-fsrs <c>scheduler/default-scheduler.legacy-parity.spec.ts</c> plus the legacy-comparing tests of
/// <c>default-scheduler.spec.ts</c>. Every test compares against <c>FsrsSharp.Legacy.Fsrs</c>.
/// </summary>
public class DefaultSchedulerLegacyParityTests
{
    private static readonly string[] CardKeys = ["cardId", "dueAt", "lapses", "lastReviewAt", "learningStep", "reps", "scheduledDays"];
    private static readonly string[] RevlogKeys = ["cardId", "dueAt", "lastReviewAt", "learningStep", "reviewTime", "scheduledDays"];

    public static TheoryData<bool, Rating> ShortTermAndGrades
    {
        get
        {
            TheoryData<bool, Rating> data = [];
            foreach (var shortTerm in new[] { true, false })
                foreach (var grade in AllGrades) data.Add(shortTerm, grade);
            return data;
        }
    }

    public static TheoryData<bool, State> ShortTermAndStates
    {
        get
        {
            TheoryData<bool, State> data = [];
            foreach (var shortTerm in new[] { true, false })
                foreach (var state in AllStates) data.Add(shortTerm, state);
            return data;
        }
    }

    public static TheoryData<State> States => [.. AllStates];

    private static Scheduler<FsrsState, DateTime> Fsrs6(DefaultSchedulerOptions options) => DefaultScheduler.Create(FsrsVersion.Fsrs6, options);

    [Theory]
    [MemberData(nameof(ShortTermAndGrades))]
    public void MatchesEveryCardAndRevlogFieldForRating(bool enableShortTerm, Rating grade)
    {
        var options = new DefaultSchedulerOptions { EnableShortTerm = enableShortTerm };
        var card = CreateStateCard(State.Review);
        var actual = Fsrs6(options).Review(card, grade, Now);
        var legacy = Next(options, card, Now, grade);

        actual.Card.Fields.Keys.Order().Should().Equal(CardKeys);
        actual.Card.CardId.Should().Be(card.CardId);
        actual.Card.DueAt.Should().Be(legacy.Card.Due);
        actual.Card.MemoryState.Stability.Should().Be(legacy.Card.Stability);
        actual.Card.MemoryState.Difficulty.Should().Be(legacy.Card.Difficulty);
        actual.Card.ScheduledDays.Should().Be(legacy.Card.ScheduledDays);
        actual.Card.LearningStep.Should().Be(legacy.Card.LearningSteps);
        actual.Card.Reps.Should().Be(legacy.Card.Reps);
        actual.Card.Lapses.Should().Be(legacy.Card.Lapses);
        actual.Card.State.Should().Be(legacy.Card.State);
        actual.Card.ScheduleStatus.Should().Be(StatusOf(legacy.Card.State));
        actual.Card.LastReviewAt.Should().Be(legacy.Card.LastReview);

        actual.Revlog.Fields.Keys.Order().Should().Equal(RevlogKeys);
        actual.Revlog.CardId.Should().Be(card.CardId);
        actual.Revlog.DueAt.Should().Be(card.DueAt);
        actual.Revlog.LastReviewAt.Should().Be(card.LastReviewAt);
        actual.Revlog.MemoryState.Stability.Should().Be(legacy.Log.Stability);
        actual.Revlog.MemoryState.Difficulty.Should().Be(legacy.Log.Difficulty);
        actual.Revlog.ScheduledDays.Should().Be(legacy.Log.ScheduledDays);
        actual.Revlog.LearningStep.Should().Be(legacy.Log.LearningSteps);
        actual.Revlog.Rating.Should().Be(legacy.Log.Rating);
        actual.Revlog.State.Should().Be(legacy.Log.State);
        actual.Revlog.ScheduleStatus.Should().Be(StatusOf(legacy.Log.State));
        actual.Revlog.ReviewTime.Should().Be(legacy.Log.Review);
    }

    [Theory]
    [MemberData(nameof(ShortTermAndStates))]
    public void MatchesEveryRatingFromState(bool enableShortTerm, State state)
    {
        var options = new DefaultSchedulerOptions { EnableShortTerm = enableShortTerm };
        var card = CreateStateCard(state);
        var actual = Fsrs6(options).Preview(card, Now).ToList();

        actual.Select(item => item.Grade).Should().Equal(AllGrades);
        for (var index = 0; index < AllGrades.Length; index++)
        {
            var expected = Review(options, card, Now, AllGrades[index]);
            var item = new ScheduleResult<FsrsState>(actual[index].Card, actual[index].Revlog);
            if (!enableShortTerm && state is State.Learning or State.Relearning)
            {
                item.Card.Should().Be(With(expected.Card, item.Card.DueAt, item.Card.ScheduledDays, card.Lapses));
                item.Card.ScheduledDays.Should().BeLessThanOrEqualTo(expected.Card.ScheduledDays);
                ExpectRevlogParity(item.Revlog, expected.Revlog);
            }
            else
            {
                ExpectFullParity(item, expected);
            }
        }

        if (state == State.New)
        {
            actual.All(item => item.Revlog.DueAt == card.DueAt).Should().BeTrue();
            card.DueAt.Should().NotBe(Now);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MatchesAMultiRoundReviewSequence(bool enableShortTerm)
    {
        var options = new DefaultSchedulerOptions { EnableShortTerm = enableShortTerm };
        var scheduler = Fsrs6(options);
        Rating[] ratings = [Rating.Again, Rating.Hard, Rating.Good, Rating.Again, Rating.Easy, Rating.Good, Rating.Hard, Rating.Easy];
        var card = scheduler.NewCard(Now, MiddlewareTestSchedulers.CardInput($"multi-{enableShortTerm.ToString().ToLowerInvariant()}"));
        var now = Now;

        for (var index = 0; index < ratings.Length; index++)
        {
            var expected = Review(options, card, now, ratings[index]);
            var actual = scheduler.Review(card, ratings[index], now);
            ExpectSequenceParity(actual, expected, card, enableShortTerm);
            card = actual.Card;
            now = AddMs(card.DueAt, index % 2 == 0 ? 0 : Day);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Matches200FixedSeedRandomizedReviewRounds(bool enableShortTerm)
    {
        var options = new DefaultSchedulerOptions { EnableShortTerm = enableShortTerm };
        var scheduler = Fsrs6(options);
        uint seed = enableShortTerm ? 0x6f6eu : 0x6f66u;
        var card = scheduler.NewCard(Now, MiddlewareTestSchedulers.CardInput($"random-{enableShortTerm.ToString().ToLowerInvariant()}"));
        var now = Now;

        double Random()
        {
            seed = unchecked(seed * 1_664_525u + 1_013_904_223u);
            return seed / 4_294_967_296.0;
        }

        for (var round = 0; round < 200; round++)
        {
            var grade = AllGrades[(int)Math.Floor(Random() * AllGrades.Length)];
            var expected = Review(options, card, now, grade);
            var actual = scheduler.Review(card, grade, now);

            ExpectSequenceParity(actual, expected, card, enableShortTerm);
            ExpectRollbackParity(scheduler.Rollback(actual.Card, actual.Revlog), card);

            card = actual.Card;
            var delay = Math.Floor(Random() * 15) * Day;
            var hourOffset = Math.Floor(Random() * 24) * (Day / 24);
            now = AddMs(card.DueAt, delay + hourOffset);
        }
    }

    [Theory]
    [MemberData(nameof(ShortTermAndStates))]
    public void RestoresTheInputCardForState(bool enableShortTerm, State state)
    {
        var scheduler = Fsrs6(new DefaultSchedulerOptions { EnableShortTerm = enableShortTerm });
        var card = CreateStateCard(state);

        foreach (var grade in AllGrades)
        {
            var reviewed = scheduler.Review(card, grade, Now);
            ExpectRollbackParity(scheduler.Rollback(reviewed.Card, reviewed.Revlog), card);
        }
    }

    public static TheoryData<string, State> EdgeCases
    {
        get
        {
            TheoryData<string, State> data = [];
            foreach (var name in EdgeOptions.Keys)
                foreach (var state in AllStates) data.Add(name, state);
            return data;
        }
    }

    private static readonly Dictionary<string, DefaultSchedulerOptions> EdgeOptions = new()
    {
        ["short-term without learning steps"] = new() { EnableShortTerm = true, LearningSteps = [], RelearningSteps = [] },
        ["short-term with day-scale learning steps"] = new() { EnableShortTerm = true, LearningSteps = ["1.5d"], RelearningSteps = ["2d"] },
        ["short-term with zero-minute learning steps"] = new() { EnableShortTerm = true, LearningSteps = ["0m"], RelearningSteps = ["0m"] },
        ["short-term with a small maximum interval"] = new() { EnableShortTerm = true, MaximumInterval = 2 },
        ["long-term with a small maximum interval"] = new() { EnableShortTerm = false, MaximumInterval = 2 },
        ["long-term with high requested retention"] = new() { EnableShortTerm = false, DesiredRetention = 0.99 },
    };

    [Theory]
    [MemberData(nameof(EdgeCases))]
    public void ParameterEdgeParityMatchesEveryRatingFromState(string name, State state)
    {
        var options = EdgeOptions[name];
        var card = CreateStateCard(state);
        var scheduler = Fsrs6(options);

        foreach (var grade in AllGrades)
        {
            var expected = Review(options, card, Now, grade);
            var actual = scheduler.Review(card, grade, Now);

            if (options.EnableShortTerm && options.MaximumInterval == 2 && expected.Card.ScheduledDays > options.MaximumInterval)
            {
                actual.Card.Should().Be(With(expected.Card, AddMs(Now, 2 * Day), options.MaximumInterval));
                ExpectRevlogParity(actual.Revlog, expected.Revlog);
                continue;
            }

            if (!options.EnableShortTerm && state is State.Learning or State.Relearning)
            {
                actual.Card.Should().Be(With(expected.Card, actual.Card.DueAt, actual.Card.ScheduledDays, card.Lapses));
                actual.Card.ScheduledDays.Should().BeLessThanOrEqualTo(expected.Card.ScheduledDays);
                ExpectRevlogParity(actual.Revlog, expected.Revlog);
                continue;
            }

            if (options.EnableShortTerm && state == State.New)
            {
                actual.Card.Should().Be(With(expected.Card, actual.Card.DueAt, actual.Card.ScheduledDays));
                actual.Card.ScheduledDays.Should().BeGreaterThanOrEqualTo(expected.Card.ScheduledDays);
                ExpectRevlogParity(actual.Revlog, expected.Revlog);
                continue;
            }

            ExpectFullParity(actual, expected);
        }
    }

    [Theory]
    [InlineData(2.5)]
    [InlineData(0.0)]
    public void ConsumesLegacyScheduledDaysWithoutLosingRollbackParity(double scheduledDays)
    {
        var options = new DefaultSchedulerOptions { EnableShortTerm = true };
        var scheduler = Fsrs6(options);
        var baseCard = CreateStateCard(State.Review);
        var card = baseCard with
        {
            Fields = baseCard.Fields
                .With(FuzzingMiddleware.CardIdField, $"legacy-scheduled-{scheduledDays}")
                .With(ScheduledDaysMiddleware.ScheduledDays, scheduledDays),
        };
        var expected = Review(options, card, Now, Rating.Good);
        var actual = scheduler.Review(card, Rating.Good, Now);

        // The legacy C# card stores scheduled_days as int (upstream: number), so the logged value is truncated there.
        var expectedRevlog = expected.Revlog with
        {
            Fields = expected.Revlog.Fields.With(ScheduledDaysMiddleware.ScheduledDays, scheduledDays),
        };
        actual.Card.Should().Be(expected.Card);
        ExpectRevlogParity(actual.Revlog, expectedRevlog);
        ExpectRollbackParity(scheduler.Rollback(actual.Card, actual.Revlog), card);
        scheduler.Rollback(actual.Card, actual.Revlog).ScheduledDays.Should().Be(scheduledDays);
    }

    [Fact]
    public void KeepsScheduledDaysAlignedWithTheSecondPrecisionLearningStepDue()
    {
        var options = new DefaultSchedulerOptions { EnableShortTerm = true, LearningSteps = ["23.999h"], RelearningSteps = ["47.999h"] };
        var scheduler = Fsrs6(options);

        foreach (var (state, expectedSeconds, expectedDays, expectedState, expectedStatus) in new (State, long, double, State, ScheduleStatus)[]
                 {
                     (State.New, 86_396, 0, State.Learning, ScheduleStatus.Learning),
                     (State.Review, 172_796, 1, State.Review, ScheduleStatus.Review),
                 })
        {
            var card = CreateStateCard(state);
            var actual = scheduler.Review(card, Rating.Again, Now);
            var legacy = Review(options, card, Now, Rating.Again);

            MsBetween(actual.Card.DueAt, Now).Should().Be(expectedSeconds * 1_000);
            actual.Card.ScheduledDays.Should().Be(expectedDays);
            actual.Card.State.Should().Be(expectedState);
            actual.Card.ScheduleStatus.Should().Be(expectedStatus);
            legacy.Card.ScheduledDays.Should().Be(expectedDays);
        }
    }

    // ---- legacy-comparing tests of default-scheduler.spec.ts

    public static TheoryData<FsrsVersion, double[]> MigratedWeights => new()
    {
        { FsrsVersion.Fsrs6, [.. Fsrs4Constants.DefaultWeights] },
        { FsrsVersion.Fsrs6, [.. Fsrs5Constants.DefaultWeights] },
        { FsrsVersion.Fsrs6, [.. Fsrs6Constants.DefaultWeights] },
    };

    [Theory]
    [MemberData(nameof(MigratedWeights))]
    public void MigratesWeightsBeforeScheduling(FsrsVersion version, double[] weights)
    {
        var options = new DefaultSchedulerOptions { Weights = weights, EnableShortTerm = true };
        var scheduler = DefaultScheduler.Create(version, options);
        var card = scheduler.NewCard(Now, MiddlewareTestSchedulers.CardInput($"migrate-{weights.Length}"));

        ExpectFullParity(scheduler.Review(card, Rating.Good, Now), Review(options, card, Now, Rating.Good));
    }

    [Fact]
    public void HonorsDesiredRetentionAndRemainsEqualToTheLegacyScheduler()
    {
        var baseCard = CreateStateCard(State.Review);
        var card = baseCard with
        {
            MemoryState = new FsrsState(35, baseCard.MemoryState.Difficulty),
            Fields = baseCard.Fields.With(FuzzingMiddleware.CardIdField, "retention"),
        };
        var lowOptions = new DefaultSchedulerOptions { DesiredRetention = 0.8 };
        var highOptions = new DefaultSchedulerOptions { DesiredRetention = 0.95 };
        var low = Fsrs6(lowOptions).Review(card, Rating.Good, Now);
        var high = Fsrs6(highOptions).Review(card, Rating.Good, Now);

        ExpectFullParity(low, Review(lowOptions, card, Now, Rating.Good));
        ExpectFullParity(high, Review(highOptions, card, Now, Rating.Good));
        low.Card.ScheduledDays.Should().BeGreaterThan(high.Card.ScheduledDays);
    }

    [Fact]
    public void SnapshotsMutableLearningStepParametersBeforeLazyPreviewIteration()
    {
        List<string> learningSteps = ["1m"];
        List<string> relearningSteps = ["10m"];
        var options = new DefaultSchedulerOptions { LearningSteps = learningSteps, RelearningSteps = relearningSteps };
        var scheduler = Fsrs6(options);
        var card = scheduler.NewCard(Now, MiddlewareTestSchedulers.CardInput("mutable-step-parameters"));
        var expected = Review(options, card, Now, Rating.Again);
        var preview = scheduler.Preview(card, Now);

        learningSteps[0] = "1d";
        relearningSteps[0] = "1d";

        var first = preview.First();
        ExpectFullParity(new ScheduleResult<FsrsState>(first.Card, first.Revlog), expected);
    }
}
