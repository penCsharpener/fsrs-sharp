using AwesomeAssertions;
using FsrsSharp.Chrono;
using FsrsSharp.Kit;
using Xunit;
using static FsrsSharp.Tests.Kit.KitFixtures;

namespace FsrsSharp.Tests.Kit;

/// <summary>
/// Port of srs-kit <c>scheduler/rollback-due-at.spec.ts</c>. The Temporal-instant cases are not ported
/// (that chrono has no C# counterpart). Upstream's JSON round trip is replaced by passing the same
/// immutable cards to a freshly created scheduler.
/// </summary>
public class RollbackDueAtTests
{
    private static readonly SchedulerDefinition<Sm2State, DateTime> Definition = DateDefinition();
    private static readonly Scheduler<Sm2State, DateTime> DateCore = Definition.Create(Config);

    private static readonly DateTime CreatedAt = Utc(2026, 1, 1);
    private static readonly DateTime FirstReview = Utc(2026, 1, 2);
    private static readonly DateTime SecondReview = Utc(2026, 1, 10);

    [Fact]
    public void AfterTheFirstReviewOfANewCard()
    {
        var card = DateCore.NewCard(CreatedAt);
        var reviewed = DateCore.Review(card, Rating.Good, FirstReview);

        var restored = DateCore.Rollback(reviewed.Card, reviewed.Revlog);

        reviewed.Revlog.LastReviewAt.Should().BeNull();
        restored.DueAt.Should().Be(card.DueAt);
        restored.LastReviewAt.Should().BeNull();
    }

    [Fact]
    public void AfterASecondReviewWhenTheCardCarriesLastReviewAt()
    {
        var first = DateCore.Review(DateCore.NewCard(CreatedAt), Rating.Good, FirstReview);
        first.Card.LastReviewAt.Should().Be(FirstReview);

        var second = DateCore.Review(first.Card, Rating.Good, SecondReview);

        second.Revlog.DueAt.Should().Be(first.Card.DueAt);
        second.Revlog.LastReviewAt.Should().Be(first.Card.LastReviewAt);
        second.Revlog.ReviewTime.Should().Be(SecondReview);

        var restored = DateCore.Rollback(second.Card, second.Revlog);
        restored.DueAt.Should().Be(first.Card.DueAt);
        restored.LastReviewAt.Should().Be(first.Card.LastReviewAt);
    }

    [Fact]
    public void ForAnOverdueCardWithoutPullingDueAtForwardToTheReviewTime()
    {
        var first = DateCore.Review(DateCore.NewCard(CreatedAt), Rating.Good, FirstReview);
        var dueBefore = first.Card.DueAt;
        dueBefore.Should().BeBefore(SecondReview);

        var second = DateCore.Review(first.Card, Rating.Good, SecondReview);
        var restored = DateCore.Rollback(second.Card, second.Revlog);

        restored.DueAt.Should().Be(dueBefore);
        restored.DueAt.Should().BeBefore(SecondReview);
    }

    [Fact]
    public void RoundTripsDueAtAcrossAReviewRollbackCycleForAnyReviewCount()
    {
        var card = DateCore.NewCard(CreatedAt);
        var now = FirstReview;

        for (var index = 0; index < 4; index++)
        {
            var dueBefore = card.DueAt;
            var lastReviewBefore = card.LastReviewAt;
            var reviewed = DateCore.Review(card, Rating.Good, now);

            var restored = DateCore.Rollback(reviewed.Card, reviewed.Revlog);
            restored.DueAt.Should().Be(dueBefore, $"review #{index + 1} dueAt");
            restored.LastReviewAt.Should().Be(lastReviewBefore, $"review #{index + 1} lastReviewAt");

            card = reviewed.Card;
            now = now.AddDays(5);
        }
    }

    [Fact]
    public void RollsBackBothPersistedReviewsUsingAFreshScheduler()
    {
        var card = DateCore.NewCard(CreatedAt);
        var first = DateCore.Review(card, Rating.Good, FirstReview);
        var second = DateCore.Review(first.Card, Rating.Good, SecondReview);

        var reloaded = Definition.Create(Config);
        var restored2 = reloaded.Rollback(second.Card, second.Revlog);
        var restored1 = reloaded.Rollback(restored2, first.Revlog);

        restored2.Should().Be(first.Card);
        restored1.Should().Be(card);
    }

    [Fact]
    public void UnwindsALongReviewHistoryBackToTheNewCard()
    {
        Rating[] grades = [Rating.Again, Rating.Hard, Rating.Good, Rating.Easy];
        var initialCard = DateCore.NewCard(CreatedAt);
        var cardsBefore = new List<Card<Sm2State>> { initialCard };
        var results = new List<ScheduleResult<Sm2State>>();
        var card = initialCard;
        var now = FirstReview;

        for (var index = 0; index < 20; index++)
        {
            var result = DateCore.Review(card, grades[index % grades.Length], now);
            results.Add(result);
            card = result.Card;
            cardsBefore.Add(card);
            now = card.DueAt.AddHours(index % 5 * 3);
        }

        var reloaded = Definition.Create(Config);
        var restored = reloaded.Rollback(results[^1].Card, results[^1].Revlog);

        for (var index = results.Count - 1; index >= 0; index--)
        {
            restored.Should().Be(cardsBefore[index], $"rollback #{index + 1}");
            if (index == 0) break;
            restored = reloaded.Rollback(restored, results[index - 1].Revlog);
        }

        restored.Should().Be(initialCard);
    }

    [Fact]
    public void PreservesAnExplicitlyNullPreviousReviewTimeOnANonNewCard()
    {
        var first = DateCore.Review(DateCore.NewCard(CreatedAt), Rating.Good, FirstReview);
        var card = first.Card.With(DateTimeChrono.LastReviewAt, null);
        var second = DateCore.Review(card, Rating.Good, SecondReview);

        second.Revlog.LastReviewAt.Should().BeNull();
        DateCore.Rollback(second.Card, second.Revlog).Should().Be(card);
    }
}
