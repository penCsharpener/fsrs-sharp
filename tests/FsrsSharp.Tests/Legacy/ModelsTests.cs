using AwesomeAssertions;
using FsrsSharp.Legacy;
using Xunit;

namespace FsrsSharp.Tests.Legacy;

/// <summary>Port of <c>models.test.ts</c>. The numeric values of <see cref="State"/> and <see cref="Rating"/>
/// are part of the wire format — ts-fsrs, py-fsrs and this port all agree on them so that a card can be moved
/// between implementations — which is why they're asserted rather than left to the compiler's discretion.</summary>
public class ModelsTests
{
    [Theory]
    [InlineData(State.New, 0, "New")]
    [InlineData(State.Learning, 1, "Learning")]
    [InlineData(State.Review, 2, "Review")]
    [InlineData(State.Relearning, 3, "Relearning")]
    public void State_ShouldHaveTheSameNumericValueAndNameAsUpstream(State state, int value, string name)
    {
        ((int)state).Should().Be(value);
        ((State)value).Should().Be(state);
        state.ToString().Should().Be(name);
        Enum.Parse<State>(name).Should().Be(state);
    }

    [Theory]
    [InlineData(Rating.Manual, 0, "Manual")]
    [InlineData(Rating.Again, 1, "Again")]
    [InlineData(Rating.Hard, 2, "Hard")]
    [InlineData(Rating.Good, 3, "Good")]
    [InlineData(Rating.Easy, 4, "Easy")]
    public void Rating_ShouldHaveTheSameNumericValueAndNameAsUpstream(Rating rating, int value, string name)
    {
        ((int)rating).Should().Be(value);
        ((Rating)value).Should().Be(rating);
        rating.ToString().Should().Be(name);
        Enum.Parse<Rating>(name).Should().Be(rating);
    }

    [Fact]
    public void LegacyFacade_ShouldReuseTheRootPrimitivesAndModelTypes()
    {
        // Upstream's v6 models.test.ts asserts that the legacy facade re-exports srs-kit's Rating/State/schemas
        // (identity, not copies). The C# equivalent: legacy cards and logs use the root Rating/State enums, the
        // legacy grade list is the root one, and the legacy model's memory state is Models.FsrsState.
        LegacyHelpers.Grades.Should().Equal(FsrsSharp.Grades.All);
        new Fsrs().Model.Should().BeAssignableTo<FsrsSharp.Kit.IModelCore<FsrsSharp.Models.FsrsState>>();
        new Card().State.Should().Be(FsrsSharp.State.New);
        new ReviewLog().Rating.Should().Be(FsrsSharp.Rating.Manual);
    }
}
