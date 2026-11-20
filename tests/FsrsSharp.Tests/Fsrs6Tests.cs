using AwesomeAssertions;
using Xunit;

namespace FsrsSharp.Tests;

/// <summary>Port of <c>FSRS-6.test.ts</c> — the port's single strongest correctness signal. The thirteen-review
/// <c>ivl_history</c> sequence exercises initial stability, difficulty updates, the forgetting curve, the
/// learning and relearning steps, and the two lapses in the middle that reset the card; every one of them has
/// to agree with upstream to the day for all thirteen numbers to line up. The invariants checked *inside* the
/// loop are upstream's too, and they're the valuable part: at every step, previewing must equal applying,
/// rolling back must undo, and the log's elapsed days must match the clock.</summary>
public class Fsrs6Tests
{
    private static readonly Rating[] IvlRatings =
    [
        Rating.Good, Rating.Good, Rating.Good, Rating.Good, Rating.Good, Rating.Good,
        Rating.Again, Rating.Again,
        Rating.Good, Rating.Good, Rating.Good, Rating.Good, Rating.Good,
    ];

    [Fact]
    public void Repeat_ShouldMatchTheUpstreamGoldenVector_OverThirteenReviews()
    {
        var f = TestParameters.Create();
        var card = Defaults.CreateEmptyCard();
        var now = TestParameters.Epoch;
        var scheduling = f.Repeat(card, now);
        var intervalHistory = new List<int>();

        foreach (var rating in IvlRatings)
        {
            foreach (var check in Helpers.Grades)
            {
                var (checkedCard, log) = scheduling[check];

                f.Rollback(checkedCard, log).Should().BeEquivalentTo(card);
                log.ElapsedDays.Should().Be(card.LastReview is { } last
                    ? (int)Helpers.DateDiff(now, last, Helpers.DiffUnit.Days)
                    : 0);
                scheduling[check].Should().BeEquivalentTo(TestParameters.Create().Next(card, now, check));
            }

            card = scheduling[rating].Card;
            intervalHistory.Add(card.ScheduledDays);
            // The next review happens exactly when this one said it should — that's what makes the sequence
            // reproducible rather than dependent on a wall clock.
            now = card.Due;
            scheduling = f.Repeat(card, now);
        }

        intervalHistory.Should().Equal(0, 2, 11, 46, 163, 498, 0, 0, 2, 4, 7, 12, 21);
    }

    [Fact]
    public void FirstRepeat_ShouldMatchUpstream_ForEveryGrade()
    {
        var f = TestParameters.Create();

        var cards = f.Repeat(Defaults.CreateEmptyCard(), TestParameters.Epoch)
            .AsEnumerable().Select(item => item.Card).ToList();

        cards.Select(c => c.Stability).Should().Equal(0.212, 1.2931, 2.3065, 8.2956);
        cards.Select(c => c.Difficulty).Should().Equal(6.4133, 5.11217071, 2.11810397, 1);
        cards.Select(c => c.Reps).Should().Equal(1, 1, 1, 1);
        cards.Select(c => c.Lapses).Should().Equal(0, 0, 0, 0);
        cards.Select(c => c.ElapsedDays).Should().Equal(0, 0, 0, 0);
        // Only Easy graduates out of the learning steps on the very first review, so only Easy gets a
        // day-scale interval; the other three are still measured in minutes.
        cards.Select(c => c.ScheduledDays).Should().Equal(0, 0, 0, 8);
        cards.Select(c => c.State)
            .Should().Equal(State.Learning, State.Learning, State.Learning, State.Review);
    }

    private static readonly Rating[] MemoryRatings =
        [Rating.Again, Rating.Good, Rating.Good, Rating.Good, Rating.Good, Rating.Good];

    /// <summary>Days waited *before* each of <see cref="MemoryRatings"/> — the first two reviews happen on the
    /// same day, which is what exercises FSRS-6's short-term stability terms (w17/w18).</summary>
    private static readonly int[] MemoryIntervals = [0, 0, 1, 3, 8, 21];

    private static FsrsMemoryState RunViaNext(bool enableShortTerm)
    {
        var f = TestParameters.Create(enableShortTerm);
        var card = Defaults.CreateEmptyCard();
        var now = TestParameters.Epoch;
        for (var i = 0; i < MemoryRatings.Length; i++)
        {
            now = now.AddDays(MemoryIntervals[i]);
            card = f.Next(card, now, MemoryRatings[i]).Card;
        }

        return new FsrsMemoryState(card.Stability, card.Difficulty);
    }

    private static FsrsMemoryState RunViaNextState(bool enableShortTerm)
    {
        var algorithm = new FsrsAlgorithm(new FsrsParametersInput
        {
            W = TestParameters.W,
            EnableShortTerm = enableShortTerm,
        });

        FsrsMemoryState? state = null;
        for (var i = 0; i < MemoryRatings.Length; i++)
        {
            state = algorithm.NextState(state, MemoryIntervals[i], MemoryRatings[i]);
        }

        return state!.Value;
    }

    [Theory]
    [InlineData(true, 53.62691)]
    [InlineData(false, 53.335106)]
    public void Next_ShouldReachTheUpstreamStability_AfterSixReviews(bool enableShortTerm, double expected)
    {
        // 1e-4 is the same `toBeCloseTo(expected, 4)` precision upstream asserts at, not a looser tolerance
        // chosen to make the port pass.
        RunViaNext(enableShortTerm).Stability.Should().BeApproximately(expected, 1e-4);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Next_ShouldReachTheUpstreamDifficulty_AfterSixReviews(bool enableShortTerm)
    {
        // Difficulty is the same either way: it's driven purely by the ratings, never by the elapsed time or
        // by which scheduler picked the intervals.
        RunViaNext(enableShortTerm).Difficulty.Should().BeApproximately(6.3574867, 1e-4);
    }

    [Theory]
    [InlineData(true, 53.62691)]
    [InlineData(false, 53.335106)]
    public void NextState_ShouldReachTheUpstreamMemoryState(bool enableShortTerm, double expectedStability)
    {
        var state = RunViaNextState(enableShortTerm);

        state.Stability.Should().BeApproximately(expectedStability, 1e-4);
        state.Difficulty.Should().BeApproximately(6.3574867, 1e-4);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NextState_ShouldAgreeWithNext_OnTheSameReviewSequence(bool enableShortTerm)
    {
        // `NextState` is the memory model without the scheduler wrapped around it (the entry point an external
        // optimizer would use). Agreement here is exact, not approximate: both paths run the identical
        // arithmetic, so any drift between them is a bug rather than accumulated float error.
        var viaNextState = RunViaNextState(enableShortTerm);
        var viaNext = RunViaNext(enableShortTerm);

        viaNextState.Stability.Should().BeApproximately(viaNext.Stability, 1e-9);
        viaNextState.Difficulty.Should().BeApproximately(viaNext.Difficulty, 1e-9);
    }
}
