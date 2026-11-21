using AwesomeAssertions;
using FsrsSharp.Legacy;
using FsrsSharp.Models;
using Xunit;

namespace FsrsSharp.Tests.Legacy;

/// <summary>Port of <c>error.test.ts</c>: every failure is an <see cref="FsrsException"/> (itself an
/// <see cref="Exception"/>), and the specific classes tell validation and operation failures apart.
/// ts-fsrs's "does not expose the error API from the root entrypoint" assertion is a JavaScript module-export
/// concern with no C# counterpart; <c>TypeConvert</c> is not ported (see the README).</summary>
public class ErrorsTests
{
    private static TException Capture<TException>(Action action, string? message = null, string? pattern = null)
        where TException : FsrsException
    {
        var error = Assert.ThrowsAny<Exception>(action);
        error.Should().BeAssignableTo<TException>();
        error.Should().BeAssignableTo<FsrsException>();
        error.Should().BeAssignableTo<Exception>();
        if (message is not null) error.Message.Should().Be(message);
        if (pattern is not null) error.Message.Should().MatchRegex(pattern);
        return (TException)error;
    }

    [Fact]
    public void FsrsException_ShouldKeepTheNativeExceptionShape()
    {
        var error = new FsrsException("Invalid value");

        error.Should().BeAssignableTo<Exception>();
        error.Message.Should().Be("Invalid value");
        error.GetType().Name.Should().Be("FsrsException");
    }

    [Fact]
    public void SpecificExceptionClasses_ShouldInheritFromFsrsException()
    {
        var validation = new FsrsValidationException("Invalid value");
        var operation = new FsrsOperationException("Invalid operation");

        validation.Should().BeAssignableTo<FsrsException>();
        validation.Should().BeAssignableTo<Exception>();
        validation.Message.Should().Be("Invalid value");
        validation.GetType().Name.Should().Be("FsrsValidationException");

        operation.Should().BeAssignableTo<FsrsException>();
        operation.Should().BeAssignableTo<Exception>();
        operation.Message.Should().Be("Invalid operation");
        operation.GetType().Name.Should().Be("FsrsOperationException");
    }

    [Fact]
    public void FsrsException_ShouldUseTheDefaultMessage()
    {
        new FsrsException().Message.Should().Be("FSRS Error");
    }

    [Fact]
    public void ValidationFailures_ShouldBeWrapped()
    {
        Capture<FsrsValidationException>(() => Defaults.CheckParameters([0.40255]), pattern: "^Invalid parameter length");
    }

    [Fact]
    public void InvalidMemoryState_ShouldBeWrapped()
    {
        var model = new Fsrs().Model;

        Capture<FsrsValidationException>(
            () => model.Step(new(new FsrsState(Stability: 0, Difficulty: 0.5), Rating.Good, 0)),
            message: "Invalid memory state { difficulty: 0.5, stability: 0 }");
    }

    [Fact]
    public void InvalidOperations_ShouldBeWrapped()
    {
        Capture<FsrsValidationException>(
            () => new Fsrs().Next(Defaults.CreateEmptyCard(), TestParameters.FixedClock, Rating.Manual),
            message: "Cannot review a manual rating");
    }

    [Fact]
    public void StackTrace_ShouldPointAtTheBusinessThrowSite()
    {
        var error = Capture<FsrsValidationException>(
            () => Defaults.CheckParameters([0.40255]), pattern: "^Invalid parameter length");

        var firstFrame = error.StackTrace!.Split('\n').First(line => line.TrimStart().StartsWith("at ")).Trim();

        firstFrame.Should().Contain("Defaults.CheckParameters");
        firstFrame.Should().NotContain("FsrsException");
        firstFrame.Should().NotContain("FsrsValidationException");
    }
}
