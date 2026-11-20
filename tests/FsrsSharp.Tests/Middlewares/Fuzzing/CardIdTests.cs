using AwesomeAssertions;
using FsrsSharp.Middlewares;
using Xunit;

namespace FsrsSharp.Tests.Middlewares.Fuzzing;

/// <summary>
/// Port of <c>middlewares/fuzzing/card-id.spec.ts</c>. The Web Crypto / <c>Math.random</c> fallback chain does
/// not exist in .NET: <see cref="CardId.Create"/> uses <see cref="Guid.NewGuid"/>, and the manual UUID
/// formatter of the fallback paths is exposed as <see cref="CardId.FormatUuidV4"/>.
/// </summary>
public class CardIdTests
{
    [Fact]
    public void CreatesAUuidV4()
    {
        CardId.Create().Should().MatchRegex("^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$");
        CardId.Create().Should().NotBe(CardId.Create());
    }

    [Fact]
    public void FormatsAUuidV4FromRandomBytes()
    {
        CardId.FormatUuidV4(new byte[16]).Should().Be("00000000-0000-4000-8000-000000000000");
        CardId.FormatUuidV4(Enumerable.Repeat((byte)0xff, 16).ToArray()).Should().Be("ffffffff-ffff-4fff-bfff-ffffffffffff");
    }
}
