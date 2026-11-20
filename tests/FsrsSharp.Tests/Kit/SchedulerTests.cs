using AwesomeAssertions;
using FsrsSharp.Chrono;
using FsrsSharp.Kit;
using FsrsSharp.Middlewares;
using Xunit;
using static FsrsSharp.Tests.Kit.KitFixtures;

namespace FsrsSharp.Tests.Kit;

/// <summary>
/// Port of the runtime assertions of srs-kit <c>scheduler/scheduler.spec.ts</c> (<c>defineScheduler</c>) and
/// <c>scheduler-schema.spec.ts</c>. The type-inference tests, async-schema rejection and the
/// prototype-pollution / property-descriptor tests have no C# equivalent.
/// </summary>
public class SchedulerTests
{
    private static FieldBag SourceConfigBag(string source) =>
        ConfigBuilder().Set(SourceMiddleware.SourceConfig, source).ToFieldBag();

    // ---------------------------------------------------------------- definition

    [Fact]
    public void Definition_PreservesModelAndChronoAndAppendsMiddlewares()
    {
        var definition = NumericDefinition();
        definition.Model.Name.Should().Be("sm2");
        definition.Middlewares.Should().BeEmpty();
        var derived = definition.Use(SourceMiddleware.Instance).Use(StatsMiddleware.Instance);
        derived.Middlewares.Should().Equal(SourceMiddleware.Instance, StatsMiddleware.Instance);
        definition.Middlewares.Should().BeEmpty();
    }

    [Fact]
    public void Config_RejectsNull()
    {
        var act = () => NumericDefinition().Create((FieldBag)null!);
        act.Should().Throw<FsrsValidationException>().WithMessage("Expected scheduler config object");
    }

    [Fact]
    public void Config_ValidatesChronoConfigWhileMerging()
    {
        var offset = new FieldKey<double>("offset", v => double.IsFinite(v) ? null : "Expected offset config");
        var chrono = new TestChrono { ConfigKeys = [offset] };
        var definition = SchedulerDefinition.Define(Sm2Model.Instance, chrono);

        definition.Create(ConfigBuilder().Set(offset, 9)).Config.Get(offset).Should().Be(9);
        var act = () => definition.Create(ConfigBuilder().Set(offset, double.NaN));
        act.Should().Throw<FsrsValidationException>().WithMessage("Expected offset config");
        var missing = () => definition.Create(Config);
        missing.Should().Throw<FsrsValidationException>();
    }

    [Fact]
    public void Config_ValidatesMiddlewareConfigWhileMerging()
    {
        var definition = NumericDefinition().Use(SourceMiddleware.Instance);
        var act = () => definition.Create(Config);
        act.Should().Throw<FsrsValidationException>();
        var bad = ConfigBuilder();
        bad.SetRaw("source", 5);
        var wrongType = () => definition.Create(bad);
        wrongType.Should().Throw<FsrsValidationException>();
        definition.Create(SourceConfigBag("ok")).Config.Get(SourceMiddleware.SourceConfig).Should().Be("ok");
    }

    // ---------------------------------------------------------------- card init input

    [Fact]
    public void NewCard_MergesInputDeclaredByMultipleMiddlewares()
    {
        var note = new FieldKey<string>("note");
        var label = new FieldKey<string>("label");
        LambdaMiddleware<double> Make(FieldKey<string> key) => new(key.Name)
        {
            Init = [key],
            Card = [key],
            OnCardDefaults = (ctx, card) => card.Set(key, ctx.InitInput.Get(key)),
        };

        var scheduler = NumericDefinition()
            .Use(SourceMiddleware.Instance, Make(note), Make(label))
            .Create(SourceConfigBag("config-source"));
        var input = new FieldBag.Builder()
            .Set(SourceMiddleware.SourceInput, "input-source")
            .Set(note, "input-note")
            .Set(label, "input-label");
        input.SetRaw("ignored", 1);

        var card = scheduler.NewCard(4, input.ToFieldBag());

        card.Get(SourceMiddleware.SourceCard).Should().Be("input-source");
        card.Get(note).Should().Be("input-note");
        card.Get(label).Should().Be("input-label");
        // Undeclared input is not carried over.
        card.Fields.ContainsKey("ignored").Should().BeFalse();
    }

    [Fact]
    public void NewCard_FillsDefaultsForCardInitKeys()
    {
        var tag = new FieldKey<string>("tag", "none");
        var middleware = new LambdaMiddleware<double>("tag")
        {
            Init = [tag],
            Card = [tag],
            OnCardDefaults = (ctx, card) => card.Set(tag, ctx.InitInput.Get(tag)),
        };
        var scheduler = NumericDefinition().Use(middleware).Create(Config);
        scheduler.NewCard().Get(tag).Should().Be("none");
        scheduler.NewCard(0, new FieldBag.Builder().Set(tag, "x").ToFieldBag()).Get(tag).Should().Be("x");
    }

    // ---------------------------------------------------------------- scheduleStatus

    [Fact]
    public void ScheduleStatus_IsValidatedAgainstBuiltInAndMiddlewareStatuses()
    {
        var plain = NumericScheduler(StatsMiddleware.Instance);
        var withStatus = NumericScheduler(StatsMiddleware.Instance, new StatusMiddleware());
        var card = plain.NewCard(0);

        var unknown = () => plain.Review(card with { ScheduleStatus = "suspend" }, Rating.Good, 0);
        unknown.Should().Throw<FsrsValidationException>().WithMessage("Expected known scheduleStatus");
        var empty = () => plain.Review(card with { ScheduleStatus = default }, Rating.Good, 0);
        empty.Should().Throw<FsrsValidationException>().WithMessage("Expected scheduleStatus string");
        var badState = () => plain.Review(card with { State = (State)9 }, Rating.Good, 0);
        badState.Should().Throw<FsrsValidationException>().WithMessage("Expected state");

        withStatus.Review(card with { ScheduleStatus = "suspend" }, Rating.Good, 0).Revlog.ScheduleStatus.Value.Should().Be("suspend");
        var typo = () => withStatus.Review(card with { ScheduleStatus = "typo" }, Rating.Good, 0);
        typo.Should().Throw<FsrsValidationException>().WithMessage("Expected known scheduleStatus");

        var reviewed = withStatus.Review(card, Rating.Good, 0);
        var rollback = withStatus.Rollback(reviewed.Card, reviewed.Revlog with { ScheduleStatus = "buried" });
        rollback.ScheduleStatus.Value.Should().Be("buried");
    }

    // ---------------------------------------------------------------- card / revlog schema

    [Fact]
    public void Schema_ValidatesMiddlewareCardAndRevlogFields()
    {
        var scheduler = NumericDefinition().Use(SourceMiddleware.Instance, StatsMiddleware.Instance).Create(SourceConfigBag("used-source"));
        var card = scheduler.NewCard();
        var review = scheduler.Review(card, Rating.Good, 1);

        var noSource = () => scheduler.Review(card with { Fields = card.Fields.Without(SourceMiddleware.SourceCard) }, Rating.Good, 1);
        noSource.Should().Throw<FsrsValidationException>();
        var noAudit = () => scheduler.Rollback(review.Card, review.Revlog with { Fields = review.Revlog.Fields.Without(SourceMiddleware.Audit) });
        noAudit.Should().Throw<FsrsValidationException>();
    }

    [Fact]
    public void Schema_ValidatesMergedChronoAndModelRevlogFields()
    {
        var scheduler = DateDefinition().Create(Config);
        var card = scheduler.NewCard(Utc(2026, 1, 1));
        var review = scheduler.Review(card, Rating.Good, Utc(2026, 1, 3));
        var revlog = review.Revlog;

        var epochDue = () => scheduler.Rollback(review.Card, revlog.With(DateTimeChrono.DueAt, DateTime.UnixEpoch));
        epochDue.Should().Throw<FsrsValidationException>().WithMessage("Expected valid Date fields");
        var badMemory = () => scheduler.Rollback(review.Card, revlog with { MemoryState = new Sm2State(double.NaN, 1, 1) });
        badMemory.Should().Throw<FsrsValidationException>().WithMessage("Expected SM2 memory state");
        var manual = () => scheduler.Rollback(review.Card, revlog with { Rating = Rating.Manual });
        manual.Should().Throw<FsrsValidationException>().WithMessage("Expected grade");
        var noReviewTime = () => scheduler.Rollback(review.Card, revlog with { Fields = revlog.Fields.Without(DateTimeChrono.ReviewTime) });
        noReviewTime.Should().Throw<FsrsValidationException>();
        var epochCard = () => scheduler.Review(card.With(DateTimeChrono.DueAt, DateTime.UnixEpoch), Rating.Good, Utc(2026, 1, 3));
        epochCard.Should().Throw<FsrsValidationException>().WithMessage("Expected valid Date fields");

        scheduler.Rollback(review.Card, revlog).Should().Be(card);
    }

    [Fact]
    public void Schema_DropsFieldsThatAreNotDeclared()
    {
        var scheduler = NumericScheduler(StatsMiddleware.Instance);
        var card = scheduler.NewCard(0);
        var extra = card with { Fields = card.Fields.ToBuilder().Set(new FieldKey<int>("extra"), 1).ToFieldBag() };
        var result = scheduler.Review(extra, Rating.Good, 0);
        result.Card.Fields.ContainsKey("extra").Should().BeFalse();
        result.Revlog.Fields.ContainsKey("extra").Should().BeFalse();
    }

    [Fact]
    public void Schema_FillsDefaultsForOptionalFields()
    {
        var chrono = new TestChrono { CardFields = [new FieldKey<string>("zone", "utc")] };
        var scheduler = SchedulerDefinition.Define(Sm2Model.Instance, chrono).Create(Config);
        var card = scheduler.NewCard(0) with { Fields = FieldBag.Empty };
        scheduler.Review(card, Rating.Good, 0).Card.Get(new FieldKey<string>("zone", "utc")).Should().Be("utc");
    }
}
