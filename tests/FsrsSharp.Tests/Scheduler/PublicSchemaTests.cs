using Xunit;
using AwesomeAssertions;
using FsrsSharp.Kit;

namespace FsrsSharp.Tests.Scheduler;

/// <summary>
/// Port of ts-fsrs <c>public-schema.spec.ts</c>. Upstream re-exports srs-kit's field schemas; here the parsers live
/// on <see cref="KitSchemas"/>, so the identity check (<c>schema toBe original</c>) has no counterpart and the
/// validation behavior is asserted directly.
/// </summary>
public class PublicSchemaTests
{
    public static TheoryData<string, Func<double, double>, double, double> Schemas => new()
    {
        { "scheduledDaysSchema", KitSchemas.ParseScheduledDays, 0, -1 },
        { "elapsedDaysSchema", KitSchemas.ParseElapsedDays, 0.5, -1 },
        { "desiredRetentionSchema", KitSchemas.ParseDesiredRetention, 0.9, 1 },
    };

    [Theory]
    [MemberData(nameof(Schemas))]
    public void ReExportsSchemaWithItsValidationIntact(string name, Func<double, double> parse, double valid, double invalid)
    {
        name.Should().NotBeEmpty();
        parse(valid).Should().Be(valid);
        var invalidValue = () => parse(invalid);
        invalidValue.Should().Throw<FsrsValidationException>();
        var nan = () => parse(double.NaN);
        nan.Should().Throw<FsrsValidationException>();
    }
}
