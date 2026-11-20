using AwesomeAssertions;
using Xunit;

namespace FsrsSharp.Tests;

/// <summary>Port of <c>reschedule.test.ts</c>. <see cref="Fsrs.Reschedule"/> replays a stored review log from
/// scratch through the current parameters, which is how an application applies re-optimized weights to people
/// who already have history — so "replaying the log lands where grading it live would have landed" is the
/// property the whole feature rests on. Upstream checks it by brute force, over every four-grade sequence and
/// then every five-entry sequence including manual entries, and that exhaustive parity check is reproduced
/// here rather than sampled.</summary>
public class RescheduleTests
{
    private static DateTime Ms(long unixMilliseconds) =>
        DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds).UtcDateTime;

    /// <summary>2024-08-11T01:00:00Z — upstream's fixed clock for this file.</summary>
    private static readonly DateTime MockNow = Ms(1723338000000);

    private static readonly Comparison<FsrsHistoryEntry> ByReviewDate =
        (a, b) => (int)Helpers.DateDiff(a.Review, b.Review, Helpers.DiffUnit.Days);

    private static List<FsrsHistoryEntry> BuildReviews(params Rating[] ratings) => ratings
        .Select((rating, index) => new FsrsHistoryEntry
        {
            Rating = rating,
            Review = MockNow.AddDays(index + 1),
            State = rating == Rating.Manual ? FsrsSharp.State.New : null,
        })
        .ToList();

    private sealed record ReviewState(
        double Difficulty, DateTime Due, Rating Rating, DateTime? Review, double Stability,
        State State, int Reps, int Lapses, int ElapsedDays, int ScheduledDays, int LearningSteps);

    /// <summary>Upstream's hand-rolled control: walks the same review log forward one <see cref="Fsrs.Next"/>
    /// call at a time, reconstructing the card between steps from nothing but what the previous step produced.
    /// The point is that it shares no code with <see cref="Fsrs.Reschedule"/>, so agreement between the two is
    /// evidence rather than tautology.</summary>
    private static List<ReviewState> Experiment(Fsrs scheduler, List<FsrsHistoryEntry> reviews, bool skipManual)
    {
        if (skipManual)
        {
            reviews = reviews.Where(review => review.Rating != Rating.Manual).ToList();
        }

        var states = new List<ReviewState>();
        for (var index = 0; index < reviews.Count; index++)
        {
            var review = reviews[index];
            Card currentCard;
            if (index >= 1)
            {
                var previous = states[index - 1];
                currentCard = new Card
                {
                    Due = previous.Due,
                    Stability = previous.Stability,
                    Difficulty = previous.Difficulty,
                    ElapsedDays = index >= 2 && states[index - 2].Review is { } beforeLast && previous.Review is { } last
                        ? (int)Helpers.DateDiff(last, beforeLast, Helpers.DiffUnit.Days)
                        : 0,
                    ScheduledDays = previous.Review is { } reviewedAt
                        ? (int)Helpers.DateDiff(reviewedAt, previous.Due, Helpers.DiffUnit.Days)
                        : 0,
                    Reps = previous.Reps,
                    Lapses = previous.Lapses,
                    LearningSteps = previous.LearningSteps,
                    State = previous.State,
                    LastReview = previous.Review,
                };
            }
            else
            {
                currentCard = Defaults.CreateEmptyCard(MockNow);
            }

            Card card;
            ReviewLog log;
            if (review.Rating != Rating.Manual)
            {
                (card, log) = scheduler.Next(currentCard, review.Review, review.Rating);
            }
            else
            {
                var previous = index >= 1 ? states[index - 1] : null;
                log = new ReviewLog
                {
                    Rating = Rating.Manual,
                    State = FsrsSharp.State.New,
                    Due = previous?.Due ?? MockNow,
                    Stability = previous?.Stability ?? 0,
                    Difficulty = previous?.Difficulty ?? 0,
                    ElapsedDays = previous?.ElapsedDays ?? 0,
                    LastElapsedDays = previous?.ElapsedDays ?? 0,
                    ScheduledDays = previous?.ScheduledDays ?? 0,
                    LearningSteps = previous?.LearningSteps ?? 0,
                    Review = review.Review,
                };
                card = Defaults.CreateEmptyCard(review.Review);
            }

            states.Add(new ReviewState(
                card.Difficulty, card.Due, log.Rating, log.Review, card.Stability, card.State,
                card.Reps, card.Lapses, card.ElapsedDays, card.ScheduledDays, card.LearningSteps));
        }

        return states;
    }

    private static void AssertReschedulingMatchesReplay(
        Fsrs scheduler, IEnumerable<Rating[]> sequences, bool skipManual)
    {
        foreach (var sequence in sequences)
        {
            var reviews = BuildReviews(sequence);
            var control = scheduler.Reschedule(Defaults.CreateEmptyCard(), reviews, new RescheduleOptions
            {
                ReviewsOrderBy = ByReviewDate,
                SkipManual = skipManual,
            }).Collections;
            var replayed = Experiment(scheduler, reviews, skipManual);

            control.Should().HaveCount(replayed.Count);
            for (var i = 0; i < control.Count; i++)
            {
                var card = control[i].Card;
                var expected = replayed[i];

                card.Difficulty.Should().Be(expected.Difficulty);
                card.Due.Should().Be(expected.Due);
                card.Stability.Should().Be(expected.Stability);
                card.State.Should().Be(expected.State);
                card.LastReview.Should().Be(expected.Review);
                card.Reps.Should().Be(expected.Reps);
                card.Lapses.Should().Be(expected.Lapses);
                card.ElapsedDays.Should().Be(expected.ElapsedDays);
                card.ScheduledDays.Should().Be(expected.ScheduledDays);
            }
        }
    }

    private static IEnumerable<Rating[]> Sequences(Rating[] alphabet, int length) => length == 1
        ? alphabet.Select(rating => new[] { rating })
        : Sequences(alphabet, length - 1).SelectMany(
            prefix => alphabet, (prefix, rating) => prefix.Append(rating).ToArray());

    [Fact]
    public void Reschedule_ShouldMatchAReplay_ForEveryFourGradeSequence()
    {
        var sequences = Sequences(Helpers.Grades, 4).ToList();

        sequences.Should().HaveCount(256); // 4^4 — guards against a vacuous pass if the generator breaks
        AssertReschedulingMatchesReplay(new Fsrs(), sequences, skipManual: true);
    }

    [Fact]
    public void Reschedule_ShouldMatchAReplay_ForEveryFiveEntrySequenceIncludingManual()
    {
        Rating[] alphabet = [Rating.Manual, Rating.Again, Rating.Hard, Rating.Good, Rating.Easy];

        var sequences = Sequences(alphabet, 5).ToList();

        sequences.Should().HaveCount(3125); // 5^5
        AssertReschedulingMatchesReplay(new Fsrs(), sequences, skipManual: false);
    }

    [Fact]
    public void Reschedule_ShouldRequireAState_ForAManualEntry()
    {
        var reviews = BuildReviews(Rating.Easy, Rating.Good, Rating.Manual, Rating.Good)
            .Select(review => new FsrsHistoryEntry { Rating = review.Rating, Review = review.Review })
            .ToList();

        var reschedule = () => new Fsrs().Reschedule(
            Defaults.CreateEmptyCard(), reviews, new RescheduleOptions { SkipManual = false });

        reschedule.Should().Throw<FsrsValidationException>()
            .WithMessage("reschedule: state is required for manual rating");
    }

    [Fact]
    public void Reschedule_ShouldRequireADueDate_ForAManualEntryThatIsNotNew()
    {
        var reviews = BuildReviews(Rating.Easy, Rating.Good, Rating.Manual, Rating.Good)
            .Select(review => new FsrsHistoryEntry
            {
                Rating = review.Rating,
                Review = review.Review,
                State = review.Rating == Rating.Manual ? FsrsSharp.State.Review : null,
            })
            .ToList();

        var reschedule = () => new Fsrs().Reschedule(
            Defaults.CreateEmptyCard(), reviews, new RescheduleOptions { SkipManual = false });

        reschedule.Should().Throw<FsrsValidationException>()
            .WithMessage("reschedule: due is required for manual rating");
    }

    [Fact]
    public void Reschedule_ShouldAdoptAManualEntrysMemoryState()
    {
        var reviews = BuildReviews(Rating.Easy, Rating.Good, Rating.Manual, Rating.Good)
            .Select(review => new FsrsHistoryEntry
            {
                Rating = review.Rating,
                Review = review.Review,
                State = review.Rating == Rating.Manual ? FsrsSharp.State.Review : null,
                Difficulty = 3.2828565,
                Stability = 21.79806877,
                Due = Ms(1725469200000),
            })
            .ToList();

        var control = new Fsrs().Reschedule(Defaults.CreateEmptyCard(), reviews, new RescheduleOptions
        {
            SkipManual = false,
            Now = Ms(1725843600000),
        }).Collections;

        control[2].Card.Should().BeEquivalentTo(new Card
        {
            Due = Ms(1725469200000),
            Stability = 21.79806877,
            Difficulty = 3.2828565,
            ElapsedDays = 1,
            ScheduledDays = 21,
            Reps = 3,
            Lapses = 0,
            LearningSteps = 0,
            State = FsrsSharp.State.Review,
            LastReview = Ms(1723597200000),
        });
        control[2].Log.Should().BeEquivalentTo(new ReviewLog
        {
            Rating = Rating.Manual,
            State = FsrsSharp.State.Review,
            Due = Ms(1723510800000),
            Stability = 13.48506225,
            Difficulty = 1,
            ElapsedDays = 1,
            LastElapsedDays = 1,
            ScheduledDays = 13,
            LearningSteps = 0,
            Review = Ms(1723597200000),
        });

        // The review after the manual entry has to continue from the state the manual entry installed, not
        // from the one the replay had reached on its own.
        control[3].Card.Should().BeEquivalentTo(new Card
        {
            Due = Ms(1725843600000),
            Stability = 25.33029347,
            Difficulty = 3.27480201,
            ElapsedDays = 1,
            ScheduledDays = 25,
            Reps = 4,
            Lapses = 0,
            LearningSteps = 0,
            State = FsrsSharp.State.Review,
            LastReview = Ms(1723683600000),
        });
    }

    [Fact]
    public void Reschedule_ShouldKeepTheReplayedMemoryState_WhenAManualEntryOmitsIt()
    {
        var reviews = BuildReviews(Rating.Easy, Rating.Good, Rating.Manual, Rating.Good)
            .Select(review => new FsrsHistoryEntry
            {
                Rating = review.Rating,
                Review = review.Review,
                State = review.Rating == Rating.Manual ? FsrsSharp.State.Review : null,
                Due = Ms(1725469200000),
            })
            .ToList();
        var currentCard = new Card
        {
            Due = Ms(1725238800000),
            Stability = 21.79806877,
            Difficulty = 3.2828565,
            ElapsedDays = 1,
            ScheduledDays = 2,
            Reps = 5,
            Lapses = 0,
            LearningSteps = 0,
            State = FsrsSharp.State.Review,
            LastReview = Ms(1725584400000),
        };

        var result = new Fsrs().Reschedule(currentCard, reviews, new RescheduleOptions
        {
            SkipManual = false,
            Now = Ms(1725584400000),
        });

        result.Collections[2].Card.Should().BeEquivalentTo(new Card
        {
            Due = Ms(1725469200000),
            Stability = 13.48506225,
            Difficulty = 1,
            ElapsedDays = 1,
            ScheduledDays = 21,
            Reps = 3,
            Lapses = 0,
            LearningSteps = 0,
            State = FsrsSharp.State.Review,
            LastReview = Ms(1723597200000),
        });
        // The current card is already due where the replay says it should be, so there is nothing to align.
        result.RescheduleItem.Should().BeNull();
    }

    [Fact]
    public void Reschedule_ShouldProduceAnAlignmentItem_WhenTheStoredDueDateDisagrees()
    {
        var scheduler = new Fsrs();
        Rating[] test = [Rating.Easy, Rating.Good, Rating.Good, Rating.Good];
        var reviews = BuildReviews(test)
            .Select(review => new FsrsHistoryEntry
            {
                Rating = review.Rating,
                Review = review.Review,
                Due = Ms(1725469200000),
            })
            .ToList();
        var reviewAt = Ms(1723683600000);
        var expectedCard = new Card
        {
            Due = Ms(1725670800000),
            Stability = 23.06595495,
            Difficulty = 1,
            ElapsedDays = 1,
            ScheduledDays = 23,
            Reps = 4,
            Lapses = 0,
            LearningSteps = 0,
            State = FsrsSharp.State.Review,
            LastReview = reviewAt,
        };
        var expectedLog = new ReviewLog
        {
            Rating = Rating.Good,
            State = FsrsSharp.State.Review,
            Due = Ms(1723597200000),
            Stability = 18.37626562,
            Difficulty = 1,
            ElapsedDays = 1,
            LastElapsedDays = 1,
            ScheduledDays = 18,
            LearningSteps = 0,
            Review = reviewAt,
        };

        var currentCard = Defaults.CreateEmptyCard(MockNow);
        for (var index = 0; index < test.Length; index++)
        {
            var result = scheduler.Reschedule(currentCard, reviews, new RescheduleOptions
            {
                SkipManual = false,
                UpdateMemoryState = true,
                Now = reviewAt,
            });

            var scheduledDays = (int)Helpers.DateDiff(
                result.RescheduleItem!.Value.Card.Due, currentCard.Due, Helpers.DiffUnit.Days);

            result.Collections[^1].Card.Should().BeEquivalentTo(expectedCard);
            result.Collections[^1].Log.Should().BeEquivalentTo(expectedLog);

            result.RescheduleItem!.Value.Card.Should().BeEquivalentTo(new Card
            {
                Due = expectedCard.Due,
                Stability = expectedCard.Stability,
                Difficulty = expectedCard.Difficulty,
                ElapsedDays = expectedCard.ElapsedDays,
                ScheduledDays = expectedCard.ScheduledDays,
                LearningSteps = 0,
                State = expectedCard.State,
                Lapses = expectedCard.Lapses,
                Reps = currentCard.Reps + 1,
                LastReview = reviewAt,
            });
            result.RescheduleItem!.Value.Log.Should().BeEquivalentTo(new ReviewLog
            {
                Rating = Rating.Manual,
                State = currentCard.State,
                Due = currentCard.LastReview ?? currentCard.Due,
                Stability = currentCard.Stability,
                Difficulty = currentCard.Difficulty,
                ElapsedDays = expectedLog.ElapsedDays,
                LastElapsedDays = currentCard.ElapsedDays,
                ScheduledDays = scheduledDays,
                LearningSteps = 0,
                Review = reviewAt,
            });

            currentCard = result.Collections[index].Card;
        }
    }

    [Fact]
    public void Reschedule_ShouldHandleAnEmptyHistory()
    {
        var scheduler = new Fsrs();

        var withEmptyList = scheduler.Reschedule(Defaults.CreateEmptyCard(), []);
        withEmptyList.Collections.Should().BeEmpty();
        withEmptyList.RescheduleItem.Should().BeNull();

        var withNoList = scheduler.Reschedule(Defaults.CreateEmptyCard());
        withNoList.Collections.Should().BeEmpty();
        withNoList.RescheduleItem.Should().BeNull();
    }

    [Fact]
    public void Reschedule_ShouldMatchUpstreamsGoldenVectors_ForBothSchedulers()
    {
        var f = new Fsrs();
        DateTime[] reviewedAt =
        [
            TestParameters.Utc(2024, 9, 13),
            TestParameters.Utc(2024, 9, 13),
            TestParameters.Utc(2024, 9, 17),
            TestParameters.Utc(2024, 9, 28),
        ];
        var reviews = reviewedAt
            .Select(at => new FsrsHistoryEntry { Rating = Rating.Good, Review = at })
            .ToList();

        var shortTerm = f.Reschedule(
            Defaults.CreateEmptyCard(), reviews, new RescheduleOptions { SkipManual = false });

        shortTerm.RescheduleItem.Should().NotBeNull();
        shortTerm.Collections.Should().HaveCount(4);
        shortTerm.Collections.Select(item => item.Card.ScheduledDays).Should().Equal(0, 2, 16, 53);
        shortTerm.Collections.Select(item => item.Card.Stability)
            .Should().Equal(2.3065, 2.3065, 16.18802274, 52.76325584);
        shortTerm.Collections.Select(item => item.Card.Difficulty)
            .Should().Equal(2.11810397, 2.11121424, 2.1043314, 2.09745544);

        f.UpdateParameters(new FsrsParametersInput { EnableShortTerm = false });
        var longTerm = f.Reschedule(
            Defaults.CreateEmptyCard(), reviews, new RescheduleOptions { SkipManual = false });

        longTerm.RescheduleItem.Should().NotBeNull();
        longTerm.Collections.Should().HaveCount(4);
        // Only the first interval differs: without learning steps the very first Good already schedules days
        // rather than minutes. The memory state is identical either way.
        longTerm.Collections.Select(item => item.Card.ScheduledDays).Should().Equal(3, 3, 16, 53);
        longTerm.Collections.Select(item => item.Card.Stability)
            .Should().Equal(2.3065, 2.3065, 16.18802274, 52.76325584);
        longTerm.Collections.Select(item => item.Card.Difficulty)
            .Should().Equal(2.11810397, 2.11121424, 2.1043314, 2.09745544);
    }

    [Fact]
    public void Reschedule_ShouldReturnNoAlignmentItem_WhenTheCardIsAlreadyWhereTheReplayPutsIt()
    {
        var reviews = new[]
        {
            TestParameters.Utc(2024, 9, 13),
            TestParameters.Utc(2024, 9, 13),
            TestParameters.Utc(2024, 9, 17),
            TestParameters.Utc(2024, 9, 28),
        }.Select(at => new FsrsHistoryEntry { Rating = Rating.Good, Review = at }).ToList();
        var currentCard = new Card
        {
            Due = Ms(1732060800000),
            Stability = 52.44876819,
            Difficulty = 2.11475301,
            ElapsedDays = 11,
            ScheduledDays = 52,
            Reps = 4,
            Lapses = 0,
            LearningSteps = 0,
            State = FsrsSharp.State.Review,
            LastReview = TestParameters.Utc(2024, 10, 28),
        };

        var result = new Fsrs().Reschedule(currentCard, reviews, new RescheduleOptions
        {
            SkipManual = false,
            FirstCard = Defaults.CreateEmptyCard(TestParameters.Utc(2024, 9, 13)),
            UpdateMemoryState = true,
            Now = TestParameters.Utc(2024, 10, 27),
        });

        result.RescheduleItem.Should().BeNull();
    }

    [Fact]
    public void Reschedule_ShouldRestoreAForgottenCard_FromItsHistory()
    {
        // The practical case: a card was reset to New, but its review log survived — replaying it has to put
        // the card back exactly where grading those reviews live had left it.
        var scheduler = new Fsrs();
        var reviews = new[]
        {
            TestParameters.Utc(2024, 9, 13),
            TestParameters.Utc(2024, 9, 13),
            TestParameters.Utc(2024, 9, 17),
            TestParameters.Utc(2024, 9, 28),
        }.Select(at => new FsrsHistoryEntry { Rating = Rating.Good, Review = at }).ToList();

        var live = Defaults.CreateEmptyCard(MockNow);
        var history = new List<Card>();
        foreach (var review in reviews)
        {
            live = scheduler.Next(live, review.Review, review.Rating).Card;
            history.Add(live);
        }

        var forgotten = scheduler.Forget(live, TestParameters.Utc(2024, 10, 27)).Card;

        var result = scheduler.Reschedule(forgotten, reviews, new RescheduleOptions
        {
            FirstCard = Defaults.CreateEmptyCard(TestParameters.Utc(2024, 9, 28)),
            UpdateMemoryState = true,
            Now = TestParameters.Utc(2024, 10, 27),
        });

        result.RescheduleItem.Should().NotBeNull();
        result.RescheduleItem!.Value.Card.Due.Should().Be(history[^1].Due);
        result.RescheduleItem!.Value.Card.Stability.Should().Be(history[^1].Stability);
        result.RescheduleItem!.Value.Card.Difficulty.Should().Be(history[^1].Difficulty);
    }

    [Fact]
    public void Reschedule_ShouldLandOnTheSameMemoryStateAsGradingLive()
    {
        // The property the vocab-trainer's FSRS backfill depends on, stated on its own rather than buried in
        // the exhaustive parity check above.
        var f = TestParameters.Create();
        DateTime[] reviewedAt =
        [
            TestParameters.Utc(2024, 1, 1),
            TestParameters.Utc(2024, 1, 3),
            TestParameters.Utc(2024, 1, 10),
        ];
        Rating[] ratings = [Rating.Good, Rating.Again, Rating.Good];

        var live = Defaults.CreateEmptyCard(reviewedAt[0]);
        for (var i = 0; i < ratings.Length; i++)
        {
            live = f.Next(live, reviewedAt[i], ratings[i]).Card;
        }

        var history = ratings
            .Select((rating, i) => new FsrsHistoryEntry { Rating = rating, Review = reviewedAt[i] })
            .ToList();
        var replayed = f.Reschedule(
            Defaults.CreateEmptyCard(reviewedAt[0]),
            history,
            new RescheduleOptions { FirstCard = Defaults.CreateEmptyCard(reviewedAt[0]) }).Collections[^1].Card;

        replayed.Should().BeEquivalentTo(live);
    }
}
