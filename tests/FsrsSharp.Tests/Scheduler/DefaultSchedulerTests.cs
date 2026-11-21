using Xunit;
using AwesomeAssertions;
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
using FsrsSharp.Tests.Kit;
using FsrsSharp.Tests.Middlewares;
using static FsrsSharp.Tests.Scheduler.SchedulerTestSupport;

namespace FsrsSharp.Tests.Scheduler;

/// <summary>
/// Port of ts-fsrs <c>scheduler/default-scheduler.spec.ts</c>. The tests that compare against the legacy v5
/// scheduler live in <see cref="DefaultSchedulerLegacyParityTests"/>. Persisted-JSON round trips are not ported
/// (cards and revlogs are in-memory records; the rollback assertions run on the results directly).
/// </summary>
public class DefaultSchedulerTests
{
    private static FieldBag CardInput(object cardId) => MiddlewareTestSchedulers.CardInput(cardId);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RestoresPersistedReviewsWithFuzzAndShortTerm(bool enableShortTerm)
    {
        var options = new DefaultSchedulerOptions { EnableShortTerm = enableShortTerm, EnableFuzz = true };
        var scheduler = DefaultScheduler.Create(options);
        var reloaded = DefaultScheduler.Create(options);
        var card = scheduler.NewCard(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), CardInput("persisted-rollback"));
        uint seed = 12345;

        for (var index = 0; index < 200; index++)
        {
            seed = unchecked(seed * 1_664_525u + 1_013_904_223u);
            var now = AddMs(card.DueAt, (seed % 240) * 3_600_000.0);
            var result = scheduler.Review(card, AllGrades[(int)((seed >> 24) % 4)], now);
            reloaded.Rollback(result.Card, result.Revlog).Should().Be(card);
            card = result.Card;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnwindsAPersistedHistoryBackToTheNewCardWithShortTerm(bool enableShortTerm)
    {
        var options = new DefaultSchedulerOptions { EnableShortTerm = enableShortTerm, EnableFuzz = true };
        var scheduler = DefaultScheduler.Create(options);
        var reloaded = DefaultScheduler.Create(options);
        var initialCard = scheduler.NewCard(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), CardInput("unwind-rollback"));
        List<Card<Fsrs7State>> cardsBefore = [initialCard];
        List<ScheduleResult<Fsrs7State>> results = [];
        var card = initialCard;
        uint seed = 987_654_321;

        for (var index = 0; index < 50; index++)
        {
            seed = unchecked(seed * 1_664_525u + 1_013_904_223u);
            var result = scheduler.Review(card, AllGrades[(int)((seed >> 24) % 4)], AddMs(card.DueAt, (seed % 240) * 3_600_000.0));
            results.Add(result);
            card = result.Card;
            cardsBefore.Add(card);
        }

        var restored = reloaded.Rollback(results[^1].Card, results[^1].Revlog);
        for (var index = results.Count - 1; index >= 0; index--)
        {
            restored.Should().Be(cardsBefore[index], $"rollback #{index + 1}");
            if (index == 0) break;
            restored = reloaded.Rollback(restored, results[index - 1].Revlog);
        }

        restored.Should().Be(initialCard);
        restored.LastReviewAt.Should().BeNull();
        restored.State.Should().Be(State.New);
    }

    [Fact]
    public void DistinguishesReviewHistoriesWithIdenticalDueDatesAndMemoryStates()
    {
        var weights = Fsrs6Constants.DefaultWeights.ToArray();
        weights[0] = weights[1] = weights[4] = weights[5] = 1;
        var scheduler = DefaultScheduler.Create(FsrsVersion.Fsrs6, new DefaultSchedulerOptions
        {
            Weights = weights,
            EnableShortTerm = true,
            EnableFuzz = false,
        });
        var original = scheduler.NewCard(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), CardInput("same-due"));
        var firstA = scheduler.Review(original, Rating.Again, new DateTime(2026, 1, 2, 12, 4, 30, DateTimeKind.Utc));
        var firstB = scheduler.Review(original, Rating.Hard, new DateTime(2026, 1, 2, 12, 0, 0, DateTimeKind.Utc));
        var now = new DateTime(2026, 1, 3, 12, 0, 0, DateTimeKind.Utc);
        var secondA = scheduler.Review(firstA.Card, Rating.Good, now);
        var secondB = scheduler.Review(firstB.Card, Rating.Good, now);

        firstA.Card.DueAt.Should().Be(firstB.Card.DueAt);
        firstA.Card.MemoryState.Stability.Should().Be(firstB.Card.MemoryState.Stability);
        firstA.Card.MemoryState.Difficulty.Should().Be(firstB.Card.MemoryState.Difficulty);
        secondA.Revlog.LastReviewAt.Should().NotBe(secondB.Revlog.LastReviewAt);
        scheduler.Rollback(secondA.Card, secondA.Revlog).Should().Be(firstA.Card);
        scheduler.Rollback(secondB.Card, secondB.Revlog).Should().Be(firstB.Card);
    }

    public class Validation
    {
        [Fact]
        public void DelegatesReviewInputValidationToTheSchedulerCore()
        {
            var scheduler = DefaultScheduler.Create();
            var card = CreateFsrs7StateCard(State.Review);
            var builder = card.Fields.ToBuilder();
            builder.SetRaw("dueAt", "2025-02-20");
            var badDue = card with { Fields = builder.ToFieldBag() };

            var act = () => scheduler.Review(badDue, Rating.Good, Now);

            act.Should().Throw<FsrsValidationException>().WithMessage("*dueAt*");
        }

        [Theory]
        [InlineData(Rating.Manual)]
        [InlineData((Rating)5)]
        public void RejectsReviewGrade(Rating grade)
        {
            var scheduler = DefaultScheduler.Create();
            var card = scheduler.NewCard(Now, CardInput("invalid-grade"));

            var act = () => scheduler.Review(card, grade, Now);

            act.Should().Throw<FsrsValidationException>().WithMessage("*Expected grade*");
        }

        [Fact]
        public void RejectsManualRevlogsDuringRollback()
        {
            var scheduler = DefaultScheduler.Create();
            var reviewed = scheduler.Review(scheduler.NewCard(Now, CardInput("manual")), Rating.Good, Now);

            var act = () => scheduler.Rollback(reviewed.Card, reviewed.Revlog with { Rating = Rating.Manual });

            act.Should().Throw<FsrsValidationException>().WithMessage("*Expected grade*");
        }
    }

    public class VersionPresets
    {
        public static TheoryData<FsrsVersion, double[]> Presets => new()
        {
            { FsrsVersion.Fsrs3, [.. Fsrs3Constants.DefaultWeights] },
            { FsrsVersion.Fsrs4, [.. Fsrs4Constants.DefaultWeights] },
            { FsrsVersion.Fsrs4Dot5, [.. Fsrs4Dot5Constants.DefaultWeights] },
            { FsrsVersion.Fsrs5, [.. Fsrs5Constants.DefaultWeights] },
            { FsrsVersion.Fsrs6, [.. Fsrs6Constants.DefaultWeights] },
        };

        [Theory]
        [MemberData(nameof(Presets))]
        public void UsesTheModelAndParameterMigrator(FsrsVersion version, double[] weights)
        {
            var scheduler = DefaultScheduler.Create(version);
            var card = scheduler.NewCard(Now, CardInput(version.ToString()));

            var result = scheduler.Review(card, Rating.Again, Now);

            result.Card.MemoryState.Stability.Should().Be(weights[(int)Rating.Again - 1]);
        }

        [Fact]
        public void DefaultsToFsrs7()
        {
            var defaultScheduler = DefaultScheduler.Create();
            var card = defaultScheduler.NewCard(Now, CardInput("fsrs-7"));
            var defaultResult = defaultScheduler.Review(card, Rating.Good, Now);

            defaultResult.Card.MemoryState.Stability.Should().Be(Fsrs7Constants.DefaultWeights[(int)Rating.Good - 1]);

            var widened = DefaultScheduler.Create(FsrsVersion.Fsrs7);
            var widenedResult = widened.Review(
                new Card<FsrsState>(card.MemoryState, card.State, card.ScheduleStatus, card.Fields), Rating.Good, Now);
            widenedResult.Card.MemoryState.Should().BeOfType<Fsrs7State>().Which.Should().Be(defaultResult.Card.MemoryState);
            widenedResult.Card.Fields.ShouldBeBag(defaultResult.Card.Fields);
            widenedResult.Revlog.Fields.ShouldBeBag(defaultResult.Revlog.Fields);
        }

        [Fact]
        public void DefaultDefinitionIsTheFsrs7Preset()
        {
            DefaultScheduler.Create().Definition.Should().BeSameAs(DefaultScheduler.Fsrs7Definition);
            DefaultScheduler.GetDefinition(FsrsVersion.Fsrs7).Should().BeSameAs(DefaultScheduler.GetDefinition(FsrsVersion.Fsrs7));
            DefaultScheduler.GetDefinition(FsrsVersion.Fsrs7).Model.Name.Should().Be(DefaultScheduler.Fsrs7Definition.Model.Name);
        }

        [Fact]
        public void RejectsUnsupportedFsrsVersionsClearly()
        {
            var act = () => DefaultScheduler.Create((FsrsVersion)99);

            act.Should().Throw<FsrsValidationException>().WithMessage("Unsupported FSRS version \"99\"");
        }
    }

    public class PublicApi
    {
        [Fact]
        public void CreatesACardWithACallerProvidedCardIdWithoutConfig()
        {
            var card = DefaultScheduler.Create().NewCard(Now, CardInput("test-card-id"));

            card.CardId.Should().Be("test-card-id");
            card.DueAt.Should().Be(Now);
        }

        [Fact]
        public void PreservesCardIdThroughReviewRollbackAndForget()
        {
            var scheduler = DefaultScheduler.Create();
            var card = scheduler.NewCard(Now, CardInput(42));
            var reviewed = scheduler.Review(card, Rating.Easy, Now);
            var rolledBack = scheduler.Rollback(reviewed.Card, reviewed.Revlog);
            var forgotten = scheduler.Forget(reviewed.Card, AddMs(Now, Day));

            reviewed.Card.CardId.Should().Be(42);
            rolledBack.CardId.Should().Be(42);
            forgotten.CardId.Should().Be(42);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ReturnsTheCoreForgetCardWithClearStatsOnForget(bool clearStatsOnForget)
        {
            var scheduler = DefaultScheduler.Create(new DefaultSchedulerOptions { ClearStatsOnForget = clearStatsOnForget });
            var fields = new FieldBag.Builder()
                .Set(FuzzingMiddleware.CardIdField, "forget-card")
                .Set(DateTimeChrono.DueAt, AddMs(Now, 2 * Day))
                .Set(ScheduledDaysMiddleware.ScheduledDays, 9.0)
                .Set(LearningStepsMiddleware.LearningStepField, 1)
                .Set(StatsMiddleware.Reps, 12)
                .Set(StatsMiddleware.Lapses, 3)
                .Set(DateTimeChrono.LastReviewAt, (DateTime?)AddMs(Now, -7 * Day))
                .ToFieldBag();
            var card = new Card<Fsrs7State>(new Fsrs7State(9.5, 9.5, 4.5), State.Relearning, ScheduleStatus.Learning, fields);

            var actual = scheduler.Forget(card, Now);

            var expectedFields = new FieldBag.Builder()
                .Set(FuzzingMiddleware.CardIdField, "forget-card")
                .Set(DateTimeChrono.DueAt, Now)
                .Set(ScheduledDaysMiddleware.ScheduledDays, 0.0)
                .Set(LearningStepsMiddleware.LearningStepField, 0)
                .Set(StatsMiddleware.Reps, clearStatsOnForget ? 0 : 12)
                .Set(StatsMiddleware.Lapses, clearStatsOnForget ? 0 : 3)
                .Set(DateTimeChrono.LastReviewAt, (DateTime?)null)
                .ToFieldBag();
            actual.Should().Be(new Card<Fsrs7State>(new Fsrs7State(0, 0, 0), State.New, ScheduleStatus.New, expectedFields));
        }
    }

    public class Options
    {
        [Fact]
        public void HonorsDesiredRetention()
        {
            var card = CreateFsrs7StateCard(State.Review, "retention");
            card = card with { MemoryState = new Fsrs7State(35, 35, 5.2) };
            var low = DefaultScheduler.Create(new DefaultSchedulerOptions { DesiredRetention = 0.8 }).Review(card, Rating.Good, Now);
            var high = DefaultScheduler.Create(new DefaultSchedulerOptions { DesiredRetention = 0.95 }).Review(card, Rating.Good, Now);

            low.Card.ScheduledDays.Should().BeGreaterThan(high.Card.ScheduledDays);
        }

        [Fact]
        public void SnapshotsMutableLearningStepParametersBeforeLazyPreviewIteration()
        {
            List<string> learningSteps = ["1m"];
            List<string> relearningSteps = ["10m"];
            var scheduler = DefaultScheduler.Create(FsrsVersion.Fsrs6, new DefaultSchedulerOptions
            {
                LearningSteps = learningSteps,
                RelearningSteps = relearningSteps,
            });
            var card = scheduler.NewCard(Now, CardInput("mutable-step-parameters"));
            var expected = DefaultScheduler.Create(FsrsVersion.Fsrs6, new DefaultSchedulerOptions
            {
                LearningSteps = ["1m"],
                RelearningSteps = ["10m"],
            }).Review(card, Rating.Again, Now);
            var preview = scheduler.Preview(card, Now);

            learningSteps[0] = "1d";
            relearningSteps[0] = "1d";

            var first = preview.First();
            first.Card.Should().Be(expected.Card);
            first.Revlog.Should().Be(expected.Revlog);
        }
    }

    public class MiddlewareIntegration
    {
        [Fact]
        public void WiresCardIdBasedFuzzing()
        {
            var scheduler = DefaultScheduler.Create(new DefaultSchedulerOptions { EnableFuzz = true });
            var firstCard = CreateFsrs7StateCard(State.Review, "fuzz-card-a");
            firstCard = firstCard with { MemoryState = new Fsrs7State(80, 80, 5.2) };
            var secondCard = firstCard with { Fields = firstCard.Fields.With(FuzzingMiddleware.CardIdField, "fuzz-card-b") };

            double[] ScheduledDays(Card<Fsrs7State> card) =>
                [.. scheduler.Preview(card, Now).Select(item => item.Card.ScheduledDays)];
            var first = ScheduledDays(firstCard);

            first.Should().Equal(ScheduledDays(firstCard));
            first.Should().NotEqual(ScheduledDays(secondCard));
        }
    }

    public class LegacyCardCompatibility
    {
        [Fact]
        public void RejectsANegativeLegacyScheduledDays()
        {
            var scheduler = DefaultScheduler.Create(FsrsVersion.Fsrs6, new DefaultSchedulerOptions { EnableShortTerm = true });
            var card = CreateStateCard(State.Review);
            card = card with { Fields = card.Fields.With(ScheduledDaysMiddleware.ScheduledDays, -1.0) };

            var act = () => scheduler.Review(card, Rating.Good, Now);

            act.Should().Throw<FsrsValidationException>().WithMessage("*scheduledDays must be finite and non-negative*");
        }

        [Fact]
        public void KeepsScheduledDaysAlignedWithTheSecondPrecisionLearningStepDue()
        {
            var scheduler = DefaultScheduler.Create(FsrsVersion.Fsrs6, new DefaultSchedulerOptions
            {
                EnableShortTerm = true,
                LearningSteps = ["23.999h"],
                RelearningSteps = ["47.999h"],
            });

            foreach (var (state, seconds, days, expectedState, status) in new (State, long, double, State, ScheduleStatus)[]
                     {
                         (State.New, 86_396, 0, State.Learning, ScheduleStatus.Learning),
                         (State.Review, 172_796, 1, State.Review, ScheduleStatus.Review),
                     })
            {
                var actual = scheduler.Review(CreateStateCard(state), Rating.Again, Now);

                MsBetween(actual.Card.DueAt, Now).Should().Be(seconds * 1_000);
                actual.Card.ScheduledDays.Should().Be(days);
                actual.Card.State.Should().Be(expectedState);
                actual.Card.ScheduleStatus.Should().Be(status);
            }
        }
    }
}
