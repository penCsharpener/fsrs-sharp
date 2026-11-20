using AwesomeAssertions;
using FsrsSharp.Kit;
using FsrsSharp.Middlewares;
using Xunit;
using static FsrsSharp.Tests.Middlewares.FuzzMonotonicTestSchedulers;

namespace FsrsSharp.Tests.Middlewares.Fuzzing;

/// <summary>
/// Port of <c>middlewares/fuzzing/schema.spec.ts</c>. Upstream's standalone schemas become the
/// middleware's <see cref="FieldKey{T}"/>s: the config is parsed by creating a scheduler, the card init
/// input by <c>NewCard</c>, and the card / review-log fields through their keys' validators.
/// </summary>
public class FuzzingSchemaTests
{
    private static FieldBag.Builder Config(Action<FieldBag.Builder> configure)
    {
        var builder = Fsrs6Config();
        configure(builder);
        return builder;
    }

    private static Scheduler<FsrsSharp.Models.FsrsState, DateTime> Create(FieldBag.Builder config) =>
        Fsrs6Definition(FuzzingMiddleware.Instance).Create(config);

    [Fact]
    public void ParsesFuzzingConfig()
    {
        var scheduler = Create(Config(b => b.Set(FuzzingMiddleware.EnableFuzz, true).Set(FuzzingMiddleware.MaximumInterval, 365d)));

        scheduler.Config.Get(FuzzingMiddleware.EnableFuzz).Should().BeTrue();
        scheduler.Config.Get(FuzzingMiddleware.MaximumInterval).Should().Be(365);
        scheduler.Config.Get(KitSchemas.FractionalDays).Should().BeFalse();
    }

    public static TheoryData<string, Action<FieldBag.Builder>> InvalidConfigs => new()
    {
        { "empty", _ => { } },
        { "enableFuzz number", b => { b.SetRaw("enableFuzz", 1d); b.SetRaw("maximumInterval", 365d); } },
        { "maximumInterval 0", b => { b.SetRaw("enableFuzz", true); b.SetRaw("maximumInterval", 0d); } },
        { "maximumInterval 1.5", b => { b.SetRaw("enableFuzz", true); b.SetRaw("maximumInterval", 1.5); } },
        { "maximumInterval NaN", b => { b.SetRaw("enableFuzz", true); b.SetRaw("maximumInterval", double.NaN); } },
        { "fractionalDays null", b => { b.SetRaw("enableFuzz", true); b.SetRaw("maximumInterval", 365d); b.SetRaw("fractionalDays", null); } },
        { "fractionalDays number", b => { b.SetRaw("enableFuzz", true); b.SetRaw("maximumInterval", 365d); b.SetRaw("fractionalDays", 1d); } },
        { "fractionalDays string", b => { b.SetRaw("enableFuzz", true); b.SetRaw("maximumInterval", 365d); b.SetRaw("fractionalDays", "true"); } },
    };

    [Theory]
    [MemberData(nameof(InvalidConfigs))]
    public void RejectsInvalidConfig(string name, Action<FieldBag.Builder> configure)
    {
        _ = name;
        var act = () => Create(Config(configure));
        act.Should().Throw<FsrsValidationException>();
    }

    [Fact]
    public void RejectsANullConfig()
    {
        var act = () => Fsrs6Definition(FuzzingMiddleware.Instance).Create((FieldBag)null!);
        act.Should().Throw<FsrsValidationException>();
    }

    [Fact]
    public void ReportsUpstreamMessagesForInvalidMaximumInterval()
    {
        FuzzingMiddleware.MaximumInterval.Validate(0d).Should().Be("Expected positive integer maximumInterval");
        FuzzingMiddleware.MaximumInterval.Validate(1.5).Should().Be("Expected positive integer maximumInterval");
        FuzzingMiddleware.MaximumInterval.Validate(double.NaN).Should().Be("Expected positive integer maximumInterval");
        FuzzingMiddleware.MaximumInterval.Validate(365d).Should().BeNull();
    }

    [Fact]
    public void ParsesMissingStringAndNumericCardIds()
    {
        var scheduler = Create(Config(b => b.Set(FuzzingMiddleware.EnableFuzz, true).Set(FuzzingMiddleware.MaximumInterval, 365d)));

        scheduler.NewCard(Now, FieldBag.Empty).CardId.Should().BeOfType<string>();
        scheduler.NewCard(Now, CardInput("card-1")).CardId.Should().Be("card-1");
        scheduler.NewCard(Now, CardInput(42)).CardId.Should().Be(42);
        scheduler.NewCard(Now, CardInput(42.5)).CardId.Should().Be(42.5);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void RejectsAnInvalidNewCardInput(object? cardId)
    {
        var scheduler = Create(Config(b => b.Set(FuzzingMiddleware.EnableFuzz, true).Set(FuzzingMiddleware.MaximumInterval, 365d)));
        var input = new FieldBag.Builder();
        input.SetRaw("cardId", cardId);

        var act = () => scheduler.NewCard(Now, input.ToFieldBag());

        act.Should().Throw<FsrsValidationException>().WithMessage("Expected new card cardId");
    }

    [Fact]
    public void ParsesStringAndNumericCardIdsOnCardsAndRevlogs()
    {
        FuzzingMiddleware.CardIdField.Validate("card-1").Should().BeNull();
        FuzzingMiddleware.CardIdField.Validate(42).Should().BeNull();
        FuzzingMiddleware.CardIdRevlog.Validate(42).Should().BeNull();
        StatsMiddleware.Reps.Validate(2).Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(double.NaN)]
    public void RequiresAValidCardId(object? value)
    {
        FuzzingMiddleware.CardIdField.Validate(value).Should().Be("Expected card cardId");
        FuzzingMiddleware.CardIdRevlog.Validate(value).Should().Be("Expected revlog cardId");
    }

    [Fact]
    public void RejectsACardWithoutCardId()
    {
        // Upstream reports 'Expected card cardId'; the field-bag parser reports a missing required field.
        var scheduler = Create(Config(b => b.Set(FuzzingMiddleware.EnableFuzz, true).Set(FuzzingMiddleware.MaximumInterval, 365d)));
        var card = scheduler.NewCard(Now);
        var withoutCardId = card with { Fields = card.Fields.Without(FuzzingMiddleware.CardIdField) };

        var act = () => scheduler.Review(withoutCardId, Rating.Good, Now);

        act.Should().Throw<FsrsValidationException>().WithMessage("*cardId*");
    }

    [Fact]
    public void RequiresValidReps()
    {
        StatsMiddleware.Reps.Validate(-1).Should().Be("Expected non-negative integer reps");
        StatsMiddleware.Reps.Validate(1.5).Should().NotBeNull();
        StatsMiddleware.Reps.Validate("1").Should().NotBeNull();
    }
}
