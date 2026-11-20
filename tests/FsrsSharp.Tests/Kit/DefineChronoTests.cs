using AwesomeAssertions;
using FsrsSharp.Kit;
using Xunit;
using static FsrsSharp.Tests.Kit.KitFixtures;

namespace FsrsSharp.Tests.Kit;

/// <summary>
/// Runtime assertions of srs-kit <c>chrono/define-chrono.spec.ts</c> that apply to a hand-written
/// <see cref="IChrono{TTime}"/> (a chrono without card fields, with its own fields, with a custom
/// normalizer). <c>defineChrono</c>'s schema-slot filling and type inference are TS-only.
/// </summary>
public class DefineChronoTests
{
    [Fact]
    public void ChronoWithoutFieldsProducesFieldlessCards()
    {
        var core = SchedulerDefinition.Define(Sm2Model.Instance, new TestChrono()).Create(Config);
        var card = core.NewCard(3);
        card.Fields.Should().BeEmpty();
        var result = core.Review(card, Rating.Good, 3);
        result.Card.Fields.Should().BeEmpty();
        result.Revlog.Fields.Should().BeEmpty();
        core.Rollback(result.Card, result.Revlog).Should().Be(card);
    }

    [Fact]
    public void ChronoCardDefaultsReceiveTimeAndPreviousOnEveryOperation()
    {
        var dueAt = new FieldKey<double>("dueAt");
        var calls = new List<(double Time, double? Previous)>();
        var chrono = new TestChrono
        {
            CardFields = [dueAt],
            RevlogFields = [dueAt],
            OnNormalizeReview = (card, time) => new ChronoTimes<double>(null, card.Get(dueAt)),
            OnNormalizeRollback = revlog => new ChronoTimes<double>(1, revlog.Get(dueAt)),
            OnWriteRevlogDefaults = (revlog, _, time, _) => revlog.Set(dueAt, time),
            OnWriteCardDefaults = (card, _, time, previous) =>
            {
                calls.Add((time, previous));
                card.Set(dueAt, time);
            },
        };
        var core = SchedulerDefinition.Define(Sm2Model.Instance, chrono).Create(Config);

        var card = core.NewCard(2);
        calls.Should().Equal((2, null));

        var reviewed = core.Review(card, Rating.Good, 5);
        // after the review: due = now + scheduledDays (7), previous = now (5)
        calls[^1].Should().Be((6, 5));
        reviewed.Card.Get(dueAt).Should().Be(6);

        core.Forget(reviewed.Card, 9);
        calls[^1].Should().Be((9, null));
    }

    [Fact]
    public void ChronoWithoutCardDefaultsCanStillRollBack()
    {
        // Upstream: "allows rollback when chrono card defaults are absent".
        var current = new FieldKey<double>("current", 0);
        var chrono = new TestChrono
        {
            CardFields = [current],
            RevlogFields = [current],
            OnNormalizeReview = (_, time) => new ChronoTimes<double>(0, time),
            OnNormalizeRollback = revlog => new ChronoTimes<double>(0, revlog.Get(current)),
        };
        var core = SchedulerDefinition.Define(Sm2Model.Instance, chrono).Create(Config);
        var card = new Card<Sm2State>(new Sm2State(1, 2.5, 1), State.Review, ScheduleStatus.Review, FieldBag.Empty);

        var result = core.Review(card, Rating.Good, 1);
        var restored = core.Rollback(result.Card, result.Revlog);

        restored.ScheduleStatus.Should().Be(ScheduleStatus.Review);
        restored.MemoryState.Should().Be(card.MemoryState);
    }

    [Fact]
    public void ChronoFieldsWithValidationRejectBadCards()
    {
        var positive = new FieldKey<double>("positive", v => v > 0 ? null : "Expected positive");
        var chrono = new TestChrono { CardFields = [positive] };
        var core = SchedulerDefinition.Define(Sm2Model.Instance, chrono).Create(Config);
        var card = new Card<Sm2State>(new Sm2State(1, 2.5, 1), State.Review, ScheduleStatus.Review,
            new FieldBag.Builder().Set(positive, -1).ToFieldBag());
        var act = () => core.Review(card, Rating.Good, 1);
        act.Should().Throw<FsrsValidationException>().WithMessage("Expected positive");
    }
}
