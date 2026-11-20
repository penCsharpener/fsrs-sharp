using AwesomeAssertions;
using Xunit;

namespace FsrsSharp.Tests;

/// <summary>Port of <c>algorithm.test.ts</c> — the DSR math on its own, below the scheduler. Every number
/// asserted here is upstream's, computed there with decimal.js and reproduced here as a literal; if any of
/// them drifts, one of the formulas has been ported wrong even though the higher-level golden vectors might
/// still happen to line up.</summary>
public class AlgorithmTests
{
    [Fact]
    public void ComputeDecayFactor_ShouldFollowW20_ForAnFsrs5WeightSet()
    {
        var parameters = Defaults.GeneratorParameters(new FsrsParametersInput { W = TestParameters.W5 });

        var (decay, factor) = FsrsAlgorithm.ComputeDecayFactor(parameters.W);

        decay.Should().Be(-Constants.Fsrs5DefaultDecay);
        factor.Should().Be(Helpers.RoundTo(Math.Pow(0.9, 1 / decay) - 1, 8));
    }

    [Fact]
    public void ComputeDecayFactor_ShouldFollowW20_ForAnFsrs6WeightSet()
    {
        var parameters = Defaults.GeneratorParameters(new FsrsParametersInput { W = TestParameters.W });

        var (decay, factor) = FsrsAlgorithm.ComputeDecayFactor(parameters.W);

        decay.Should().Be(-Constants.Fsrs6DefaultDecay);
        factor.Should().Be(Helpers.RoundTo(Math.Pow(0.9, 1 / decay) - 1, 8));
    }

    [Fact]
    public void ForgettingCurve_ShouldMatchTheReferenceImplementation()
    {
        var algorithm = new FsrsAlgorithm();

        var collection = new[] { 0, 1, 2, 3 }.Select(t => algorithm.ForgettingCurve(t, 1.0));

        // R(0,S) = 1 by definition, and R(S,S) = 0.9 is what makes stability mean "days until 90% recall".
        collection.Should().Equal(1.0, 0.9, 0.84588465, 0.8093881);
    }

    [Fact]
    public void InitStability_ShouldBeTheFirstFourWeights()
    {
        var parameters = Defaults.GeneratorParameters();
        var algorithm = new FsrsAlgorithm();

        Helpers.Grades.Select(algorithm.InitStability)
            .Should().Equal(parameters.W[0], parameters.W[1], parameters.W[2], parameters.W[3]);
    }

    [Fact]
    public void InitDifficulty_ShouldFollowTheW4W5Formula()
    {
        var w = Defaults.GeneratorParameters().W;
        var algorithm = new FsrsAlgorithm();

        var expected = Helpers.Grades.Select(grade =>
            Helpers.RoundTo(w[4] - Math.Exp(w[5] * ((int)grade - 1)) + 1, 8));

        Helpers.Grades.Select(algorithm.InitDifficulty).Should().Equal(expected);
    }

    [Fact]
    public void NextDifficulty_ShouldMatchUpstream()
    {
        var algorithm = new FsrsAlgorithm();

        Helpers.Grades.Select(grade => algorithm.NextDifficulty(5.0, grade))
            .Should().Equal(8.34176237, 6.66599536, 4.99022837, 3.31446137);
    }

    [Fact]
    public void NextRecallStability_ShouldMatchUpstream()
    {
        var algorithm = new FsrsAlgorithm();
        double[] d = [1, 2, 3, 4];
        double[] s = [5, 5, 5, 5];
        double[] r = [0.9, 0.8, 0.7, 0.6];

        Helpers.Grades.Select((grade, i) => algorithm.NextRecallStability(d[i], s[i], r[i], grade))
            .Should().Equal(25.60252118, 28.22657096, 58.65599107, 127.2266925);
    }

    [Fact]
    public void NextForgetStability_ShouldMatchUpstream()
    {
        var algorithm = new FsrsAlgorithm();
        double[] d = [1, 2, 3, 4];
        double[] s = [5, 5, 5, 5];
        double[] r = [0.9, 0.8, 0.7, 0.6];

        Enumerable.Range(0, 4).Select(i => algorithm.NextForgetStability(d[i], s[i], r[i]))
            .Should().Equal(1.05253961, 1.18943295, 1.36808387, 1.58498896);
    }

    [Fact]
    public void NextShortTermStability_ShouldMatchUpstream()
    {
        var algorithm = new FsrsAlgorithm();

        // Hard and Good are pinned at the input stability by the max(sinc, 1) floor — a same-day review can
        // never *lose* stability on a passing grade.
        Helpers.Grades.Select(grade => algorithm.NextShortTermStability(5, grade))
            .Should().Equal(1.596818, 5, 5, 8.12960956);
    }

    [Fact]
    public void NextInterval_ShouldFollowRequestedRetention()
    {
        var intervals = Enumerable.Range(1, 10)
            .Select(i => (double)i / 10)
            .Select(retention => new Fsrs(new FsrsParametersInput
            {
                RequestRetention = retention,
                MaximumInterval = int.MaxValue,
            }).NextInterval(1.0, 0));

        // Upstream notes this sits +3 days off fsrs-rs's own reference figure at retention 0.1, because
        // fsrs-rs computes in f32 and both ts-fsrs and this port compute in f64.
        intervals.Should().Equal(3116769, 34793, 2508, 387, 90, 27, 9, 3, 1, 1);
    }

    [Fact]
    public void NextInterval_ShouldRespectMaximumInterval()
    {
        var f = new Fsrs(new FsrsParametersInput { MaximumInterval = 365 });

        f.NextInterval(737.47, 0).Should().Be(365);
    }

    [Fact]
    public void NextInterval_ShouldStayInsideTheFuzzRange_WhenCappedAndFuzzed()
    {
        var parameters = Defaults.GeneratorParameters(new FsrsParametersInput { MaximumInterval = 365 });
        var (decay, factor) = FsrsAlgorithm.ComputeDecayFactor(parameters.W);
        var intervalModifier = (Math.Pow(parameters.RequestRetention, 1 / decay) - 1) / factor;
        const double stability = 737.47;
        const double elapsedDays = 98;

        var fuzzed = new Fsrs(new FsrsParametersInput { MaximumInterval = 365, EnableFuzz = true })
            .NextInterval(stability, elapsedDays);

        var (minIvl, maxIvl) = Helpers.GetFuzzRange(
            Helpers.JsRound(stability * intervalModifier), elapsedDays, parameters.MaximumInterval);
        maxIvl.Should().Be(parameters.MaximumInterval);
        fuzzed.Should().BeGreaterThanOrEqualTo(minIvl).And.BeLessThanOrEqualTo(maxIvl);
    }

    [Fact]
    public void ApplyFuzz_ShouldRoundAndReturnUnchanged_WhenFuzzingIsDisabled()
    {
        new FsrsAlgorithm(new FsrsParametersInput { EnableFuzz = false }).ApplyFuzz(3.2, 0).Should().Be(3);
    }

    [Fact]
    public void ApplyFuzz_ShouldRoundAndReturnUnchanged_BelowTwoAndAHalfDays()
    {
        // The fuzz range is only defined from 2.5 days up; anything shorter is returned as-is even with
        // fuzzing on.
        new FsrsAlgorithm(new FsrsParametersInput { EnableFuzz = true }).ApplyFuzz(2.3, 0).Should().Be(2);
    }

    [Theory]
    [InlineData(2.5)]
    [InlineData(3)]
    public void ApplyFuzz_ShouldStayInsideTheFuzzRange_FromTwoAndAHalfDaysUp(double interval)
    {
        var algorithm = new FsrsAlgorithm(new FsrsParametersInput { EnableFuzz = true });

        var fuzzed = algorithm.ApplyFuzz(interval, 0);

        var (minIvl, maxIvl) = Helpers.GetFuzzRange(
            Helpers.JsRound(interval), 0, Constants.DefaultMaximumInterval);
        fuzzed.Should().BeGreaterThanOrEqualTo(minIvl).And.BeLessThanOrEqualTo(maxIvl);
    }

    [Fact]
    public void IntervalModifier_ShouldBeOne_AtTheDefaultRetention()
    {
        // I(r,s) with r = 0.9 is exactly s, which is the definition stability is built on.
        new Fsrs().IntervalModifier.Should().Be(1);
    }

    [Fact]
    public void UpdateParameters_ShouldRecomputeTheIntervalModifier()
    {
        var f = new Fsrs();

        f.UpdateParameters(new FsrsParametersInput { RequestRetention = 0.8 });

        f.Parameters.RequestRetention.Should().Be(0.8);
        f.IntervalModifier.Should().Be(f.CalculateIntervalModifier(0.8));
    }

    [Fact]
    public void UpdateParameters_ShouldRestoreTheDefaults_ForAnEmptyOverride()
    {
        var f = new Fsrs(new FsrsParametersInput { RequestRetention = 0.8, EnableFuzz = true });

        f.UpdateParameters(new FsrsParametersInput());

        f.Parameters.Should().BeEquivalentTo(Defaults.GeneratorParameters());
    }

    [Theory]
    [InlineData(1.2)]
    [InlineData(-0.2)]
    [InlineData(0)]
    public void CalculateIntervalModifier_ShouldRejectRetentionOutsideZeroToOne(double retention)
    {
        var calculate = () => new FsrsAlgorithm().CalculateIntervalModifier(retention);

        calculate.Should().Throw<FsrsValidationException>()
            .WithMessage("Requested retention rate should be in the range (0,1]");
    }

    [Fact]
    public void NextState_ShouldTreatAZeroedStateAsBrandNew()
    {
        var f = new Fsrs();

        var fromZeroed = f.NextState(new FsrsMemoryState(0, 0), 1, Rating.Again);

        double.IsNaN(fromZeroed.Stability).Should().BeFalse();
        f.NextState(null, 1, Rating.Again).Should().Be(fromZeroed);
    }

    [Fact]
    public void NextState_ShouldRejectADifficultyBelowOne()
    {
        var f = new Fsrs();
        var init = f.NextState(null, 0, Rating.Good);

        var next = () => f.NextState(new FsrsMemoryState(init.Stability, 0), 1, Rating.Again);

        next.Should().Throw<FsrsValidationException>().WithMessage("Invalid memory state*");
    }

    [Fact]
    public void NextState_ShouldRejectAStabilityBelowSMin()
    {
        var f = new Fsrs();
        var init = f.NextState(null, 0, Rating.Good);

        var next = () => f.NextState(new FsrsMemoryState(0, init.Stability), 1, Rating.Again);

        next.Should().Throw<FsrsValidationException>().WithMessage("Invalid memory state*");
    }

    [Fact]
    public void NextState_ShouldRejectANegativeElapsedTime()
    {
        var f = new Fsrs();

        var next = () => f.NextState(new FsrsMemoryState(0, 0), -1, Rating.Again);

        next.Should().Throw<FsrsValidationException>().WithMessage("Invalid delta_t*");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    public void NextState_ShouldRejectAnOutOfRangeGrade(int grade)
    {
        var f = new Fsrs();
        var init = f.NextState(null, 0, Rating.Good);

        var next = () => f.NextState(init, 1, (Rating)grade);

        next.Should().Throw<FsrsValidationException>().WithMessage("Invalid grade*");
    }

    [Fact]
    public void NextState_ShouldClampStabilityToSMin()
    {
        var f = new Fsrs();

        var next = f.NextState(new FsrsMemoryState(0.00102011, 9.98210112), 1, Rating.Again);

        next.Stability.Should().BeGreaterThanOrEqualTo(Constants.SMin);
    }

    [Fact]
    public void NextState_ShouldLoseStabilityOnAgain_WithShortTermDisabled()
    {
        var f = new Fsrs(new FsrsParametersInput { EnableShortTerm = false });

        var next = f.NextState(new FsrsMemoryState(10.0, 4.0), 1, Rating.Again);

        next.Stability.Should().BeLessThan(10.0);
    }
}
