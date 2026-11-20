using AwesomeAssertions;
using Xunit;

namespace FsrsSharp.Tests;

/// <summary>Port of the portable half of <c>error.test.ts</c>: the exception hierarchy and the four places the
/// library refuses input. Upstream's stack-frame and module-export cases don't apply here — this port has one
/// public assembly and no separate error entry point — and its <c>TypeConvert</c> cases have no counterpart
/// because dates arrive as <see cref="DateTime"/> rather than a string/number union.</summary>
public class ErrorsTests
{
    [Fact]
    public void FsrsValidationException_ShouldBeAnFsrsException()
    {
        var error = new FsrsValidationException("Invalid value");

        error.Should().BeAssignableTo<FsrsException>();
        error.Should().BeAssignableTo<Exception>();
        error.Message.Should().Be("Invalid value");
    }

    [Fact]
    public void FsrsException_ShouldCarryADefaultMessage()
    {
        new FsrsException().Message.Should().Be("FSRS Error");
    }

    [Fact]
    public void CheckParameters_ShouldThrowValidation_ForABadLength()
    {
        var check = () => Defaults.CheckParameters([0.40255]);

        check.Should().Throw<FsrsValidationException>().WithMessage("Invalid parameter length*");
    }

    [Fact]
    public void NextState_ShouldThrowValidation_ForAnImpossibleMemoryState()
    {
        var algorithm = new FsrsAlgorithm();

        var next = () => algorithm.NextState(new FsrsMemoryState(Stability: 0, Difficulty: 0.5), 0, Rating.Good);

        next.Should().Throw<FsrsValidationException>().WithMessage("Invalid memory state*");
    }

    [Fact]
    public void Next_ShouldThrowValidation_ForAManualRating()
    {
        var f = new Fsrs();

        var next = () => f.Next(Defaults.CreateEmptyCard(), TestParameters.FixedClock, Rating.Manual);

        next.Should().Throw<FsrsValidationException>().WithMessage("Cannot review a manual rating");
    }
}
