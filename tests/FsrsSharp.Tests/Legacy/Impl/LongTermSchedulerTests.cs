using AwesomeAssertions;
using FsrsSharp.Legacy;
using Xunit;

namespace FsrsSharp.Tests.Legacy.Impl;

/// <summary>Port of <c>impl/long-term_scheduler.test.ts</c>. Until now the long-term scheduler (the one used
/// when <see cref="FsrsParameters.EnableShortTerm"/> is false, so every review goes straight to Review state
/// with no learning steps) had exactly one assertion behind it in this port. Upstream drives five full
/// thirteen- and eight-review sequences through it and pins the interval, stability *and* difficulty after
/// every single review — reproduced here verbatim.</summary>
public class LongTermSchedulerTests
{
    private static Fsrs Create() =>
        TestParameters.CreateWith(TestParameters.WLongTerm, enableShortTerm: false);

    private sealed record History(int[] Intervals, double[] Stability, double[] Difficulty);

    /// <summary>Replays <paramref name="ratings"/>, each review taken exactly when the previous one scheduled
    /// it, and asserts on the way through that <c>Repeat(...)[grade]</c> and <c>Next(..., grade)</c> agree —
    /// upstream's regression guard for ts-fsrs#105, where the two drifted apart.</summary>
    private static History Replay(Fsrs f, params Rating[] ratings)
    {
        var card = Defaults.CreateEmptyCard();
        var now = TestParameters.Epoch;
        var intervals = new List<int>();
        var stability = new List<double>();
        var difficulty = new List<double>();

        foreach (var rating in ratings)
        {
            var record = f.Repeat(card, now)[rating];
            record.Should().BeEquivalentTo(Create().Next(card, now, rating));

            card = record.Card;
            intervals.Add(card.ScheduledDays);
            stability.Add(card.Stability);
            difficulty.Add(card.Difficulty);
            now = card.Due;
        }

        return new History([.. intervals], [.. stability], [.. difficulty]);
    }

    [Fact]
    public void LongTerm_ShouldMatchUpstream_ForThirteenGoodsWithTwoLapses()
    {
        var history = Replay(Create(),
            Rating.Good, Rating.Good, Rating.Good, Rating.Good, Rating.Good, Rating.Good,
            Rating.Again, Rating.Again,
            Rating.Good, Rating.Good, Rating.Good, Rating.Good, Rating.Good);

        history.Intervals.Should().Equal(3, 13, 48, 155, 445, 1158, 17, 3, 11, 37, 112, 307, 773);
        history.Stability.Should().Equal(
            3.0412, 13.09130698, 48.15848988, 154.93732625, 445.05562739, 1158.07779739,
            16.63063166, 3.01732209, 11.42247264, 37.37521902, 111.8752758, 306.5974569, 772.94026648);
        history.Difficulty.Should().Equal(
            4.49094334, 4.26664289, 4.05746029, 3.86237659, 3.68044154, 3.51076891,
            4.69833071, 5.55956298, 5.26323756, 4.98688448, 4.72915759, 4.4888015, 4.26464541);
    }

    [Fact]
    public void LongTerm_ShouldMatchUpstream_StartingFromAgain()
    {
        var history = Replay(Create(),
            Rating.Again, Rating.Hard, Rating.Good, Rating.Easy,
            Rating.Again, Rating.Hard, Rating.Good, Rating.Easy);

        history.Intervals.Should().Equal(1, 2, 6, 41, 4, 7, 21, 133);
        history.Stability.Should().Equal(
            0.4197, 1.0344317, 5.5356759, 41.0033667, 4.46605519, 6.67743292, 20.88868155, 132.81849454);
        history.Difficulty.Should().Equal(
            7.1434, 7.03653841, 6.64066485, 5.92312772, 6.44779861, 6.45995078, 6.10293922, 5.36588547);
    }

    [Fact]
    public void LongTerm_ShouldMatchUpstream_StartingFromHard()
    {
        var history = Replay(Create(),
            Rating.Hard, Rating.Good, Rating.Easy, Rating.Again,
            Rating.Hard, Rating.Good, Rating.Easy, Rating.Again);

        history.Intervals.Should().Equal(2, 7, 54, 5, 8, 26, 171, 8);
        history.Stability.Should().Equal(
            1.1869, 6.59167572, 53.76078737, 5.0853693, 8.09786749, 25.52991279, 171.16195166, 8.11072373);
        history.Difficulty.Should().Equal(
            6.23225985, 5.89059466, 5.14583392, 5.884097, 5.99269555, 5.667177, 4.91430736, 5.71619151);
    }

    [Fact]
    public void LongTerm_ShouldMatchUpstream_StartingFromGood()
    {
        var history = Replay(Create(),
            Rating.Good, Rating.Easy, Rating.Again, Rating.Hard,
            Rating.Good, Rating.Easy, Rating.Again, Rating.Hard);

        history.Intervals.Should().Equal(3, 33, 4, 7, 26, 193, 9, 14);
        history.Stability.Should().Equal(
            3.0412, 32.65484522, 4.22256838, 7.2325009, 25.52681746, 193.36618775, 8.63899847, 14.31323867);
        history.Difficulty.Should().Equal(
            4.49094334, 3.69538259, 4.83221448, 5.12078462, 4.85403286, 4.07165035, 5.1050878, 5.34697075);
    }

    [Fact]
    public void LongTerm_ShouldMatchUpstream_StartingFromEasy()
    {
        var history = Replay(Create(),
            Rating.Easy, Rating.Again, Rating.Hard, Rating.Good,
            Rating.Easy, Rating.Again, Rating.Hard, Rating.Good);

        history.Intervals.Should().Equal(15, 3, 6, 27, 240, 10, 17, 60);
        history.Stability.Should().Equal(
            15.2441, 3.25621013, 6.32684549, 26.56339029, 239.70462771, 9.75621519, 17.06035531, 59.59547542);
        history.Difficulty.Should().Equal(
            1.16304343, 2.99573557, 3.59851762, 3.43436666, 2.60045771, 4.03816348, 4.46259158, 4.24020203);
    }

    [Fact]
    public void SwitchingBetweenSchedulersMidStream_ShouldMatchUpstream()
    {
        // Good(short) -> Good(long) -> Again(long) -> Good(long) -> Good(short) -> Again(short): the states a
        // learner would actually walk through if the setting were flipped between reviews.
        Rating[] grades = [Rating.Good, Rating.Good, Rating.Again, Rating.Good, Rating.Good, Rating.Again];
        bool[] shortTerm = [true, false, false, false, true, true];

        var now = TestParameters.Epoch;
        var card = Defaults.CreateEmptyCard(now);
        var f = TestParameters.CreateWith(TestParameters.WLongTerm);
        var intervals = new List<int>();
        var stability = new List<double>();
        var difficulty = new List<double>();
        var states = new List<State>();

        for (var i = 0; i < grades.Length; i++)
        {
            f.UpdateParameters(new FsrsParametersInput
            {
                W = TestParameters.WLongTerm,
                EnableShortTerm = shortTerm[i],
            });

            var record = f.Repeat(card, now)[grades[i]];
            record.Should().BeEquivalentTo(
                TestParameters.CreateWith(TestParameters.WLongTerm, shortTerm[i]).Next(card, now, grades[i]));

            card = record.Card;
            now = card.Due;
            intervals.Add(card.ScheduledDays);
            stability.Add(card.Stability);
            difficulty.Add(card.Difficulty);
            states.Add(card.State);
        }

        intervals.Should().Equal(0, 4, 1, 5, 19, 0);
        stability.Should().Equal(3.0412, 3.0412, 1.21778427, 4.73753014, 19.02294877, 3.20676576);
        difficulty.Should().Equal(
            4.49094334, 4.26664289, 5.24649844, 4.97127357, 4.71459886, 5.57136081);
        states.Should().Equal(
            State.Learning, State.Review, State.Review, State.Review, State.Review, State.Relearning);
    }

    [Fact]
    public void Retrievability_ShouldBeOne_TenMinutesIntoAOneDayInterval()
    {
        var f = TestParameters.CreateWith(
        [
            0.4072, 1.1829, 3.1262, 15.4722, 7.2102, 0.5316, 1.0651, 0.0234, 1.616,
            0.1544, 1.0824, 1.9813, 0.0953, 0.2975, 2.2042, 0.2407, 2.9466, 0.5034, 0.6567,
        ], enableShortTerm: false);
        var now = TestParameters.Utc(2024, 8, 3, 18, 15, 34, 500);
        var viewDate = TestParameters.Utc(2024, 8, 3, 18, 25, 34, 500);

        var card = f.Repeat(Defaults.CreateEmptyCard(now), now)[Rating.Again].Card;

        // Elapsed days floors to 0 within the same day, so the curve hasn't started falling yet.
        FormatPercent(f.Retrievability(card, viewDate)).Should().Be("100.00%");

        card = new Card
        {
            Due = TestParameters.Utc(2024, 8, 4, 18, 15, 34, 500),
            Stability = 0.4072,
            Difficulty = 7.2102,
            ScheduledDays = 1,
            Reps = 1,
            Lapses = 0,
            LearningSteps = 0,
            State = State.Review,
            LastReview = now,
        };
        FormatPercent(f.Retrievability(card, viewDate)).Should().Be("100.00%");
    }

    private static string FormatPercent(double value) =>
        (value * 100).ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + "%";
}
