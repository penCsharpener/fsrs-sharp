using AwesomeAssertions;
using Xunit;

namespace FsrsSharp.Tests.Kit;

/// <summary>Ports of srs-kit <c>primitives/rating.spec.ts</c>, <c>state.spec.ts</c> and <c>status.spec.ts</c> (mutation-prevention tests are TS-only).</summary>
public class PrimitivesTests
{
    [Fact]
    public void Rating_DefinesFixedValuesAndGrades()
    {
        Enum.GetValues<Rating>().Select(r => (int)r).Should().Equal(0, 1, 2, 3, 4);
        Grades.All.Select(g => (int)g).Should().Equal(1, 2, 3, 4);
        ((int)Rating.Manual).Should().Be(0);
        ((int)Rating.Again).Should().Be(1);
        ((int)Rating.Hard).Should().Be(2);
        ((int)Rating.Good).Should().Be(3);
        ((int)Rating.Easy).Should().Be(4);

        Grades.Validate(Rating.Good).Should().Be(Rating.Good);
        Grades.IsGrade(Rating.Good).Should().BeTrue();
        Grades.IsGrade(Rating.Manual).Should().BeFalse();
        Grades.IsGrade((Rating)5).Should().BeFalse();
        var manual = () => Grades.Validate(Rating.Manual);
        manual.Should().Throw<FsrsValidationException>().WithMessage("Expected grade");
        var outOfRange = () => Grades.Validate((Rating)5);
        outOfRange.Should().Throw<FsrsValidationException>().WithMessage("Expected grade");
    }

    [Fact]
    public void State_DefinesFixedValues()
    {
        Enum.GetValues<State>().Select(s => (int)s).Should().Equal(0, 1, 2, 3);
        ((int)State.New).Should().Be(0);
        ((int)State.Learning).Should().Be(1);
        ((int)State.Review).Should().Be(2);
        ((int)State.Relearning).Should().Be(3);
        Enum.IsDefined((State)4).Should().BeFalse();
    }

    [Fact]
    public void ScheduleStatus_DefinesFixedBuiltInValues()
    {
        ScheduleStatus.BuiltIn.Select(s => s.Value).Should().Equal("new", "learning", "review");
        ScheduleStatus.New.Value.Should().Be("new");
        ScheduleStatus.Learning.Value.Should().Be("learning");
        ScheduleStatus.Review.Value.Should().Be("review");
        ((ScheduleStatus)"review").Should().Be(ScheduleStatus.Review);
        ScheduleStatus.BuiltIn.Should().NotContain((ScheduleStatus)"relearning");
        ScheduleStatus.Review.ToString().Should().Be("review");
    }
}
