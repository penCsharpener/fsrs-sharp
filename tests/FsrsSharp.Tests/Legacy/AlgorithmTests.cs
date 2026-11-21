using AwesomeAssertions;
using FsrsSharp.Legacy;
using FsrsSharp.Models;
using FsrsSharp.Models.Fsrs6;
using Xunit;

namespace FsrsSharp.Tests.Legacy;

/// <summary>Port of <c>algorithm.test.ts</c> — the DSR math on its own, below the scheduler. Every number
/// asserted here is upstream's, computed there with decimal.js and reproduced here as a literal; if any of
/// them drifts, one of the formulas has been ported wrong even though the higher-level golden vectors might
/// still happen to line up.</summary>
public class AlgorithmTests
{
    private static Fsrs6Algorithm CreateAlgorithm(FsrsParameters? parameters = null)
    {
        parameters ??= Defaults.GeneratorParameters();
        return new Fsrs6Algorithm(parameters.W, parameters.EnableShortTerm, Fsrs6Constants.ModelBounds);
    }

    private static FsrsState NextState(Fsrs f, FsrsState? memoryState, double elapsedDays, Rating rating) =>
        f.Model.Step(new(memoryState, rating, elapsedDays));

    [Fact]
    public void ComputeDecayFactor_ShouldFollowW20_ForAnFsrs5WeightSet()
    {
        var parameters = Defaults.GeneratorParameters(new FsrsParametersInput { W = TestParameters.W5 });

        var (decay, factor) = Fsrs6Algorithm.ComputeDecayFactor(parameters.W);

        decay.Should().Be(-Constants.Fsrs5DefaultDecay);
        factor.Should().Be(FsrsMath.RoundTo(Math.Pow(0.9, 1 / decay) - 1, 8));
    }

    [Fact]
    public void ComputeDecayFactor_ShouldFollowW20_ForAnFsrs6WeightSet()
    {
        var parameters = Defaults.GeneratorParameters(new FsrsParametersInput { W = TestParameters.W });

        var (decay, factor) = Fsrs6Algorithm.ComputeDecayFactor(parameters.W);

        decay.Should().Be(-Constants.Fsrs6DefaultDecay);
        factor.Should().Be(FsrsMath.RoundTo(Math.Pow(0.9, 1 / decay) - 1, 8));
    }

    [Fact]
    public void ForgettingCurve_ShouldMatchTheReferenceImplementation()
    {
        var algorithm = CreateAlgorithm();

        var collection = new[] { 0, 1, 2, 3 }.Select(t => algorithm.ForgettingCurve(t, 1.0));

        // R(0,S) = 1 by definition, and R(S,S) = 0.9 is what makes stability mean "days until 90% recall".
        collection.Should().Equal(1.0, 0.9, 0.84588465, 0.8093881);
    }

    [Fact]
    public void InitStability_ShouldBeTheFirstFourWeights()
    {
        var parameters = Defaults.GeneratorParameters();
        var algorithm = CreateAlgorithm();

        LegacyHelpers.Grades.Select(algorithm.InitStability)
            .Should().Equal(parameters.W[0], parameters.W[1], parameters.W[2], parameters.W[3]);
    }

    [Fact]
    public void InitDifficulty_ShouldFollowTheW4W5Formula()
    {
        var w = Defaults.GeneratorParameters().W;
        var algorithm = CreateAlgorithm();

        var expected = LegacyHelpers.Grades.Select(grade =>
            FsrsMath.RoundTo(w[4] - Math.Exp(w[5] * ((int)grade - 1)) + 1, 8));

        LegacyHelpers.Grades.Select(algorithm.InitDifficulty).Should().Equal(expected);
    }

    [Fact]
    public void NextDifficulty_ShouldMatchUpstream()
    {
        var algorithm = CreateAlgorithm();

        LegacyHelpers.Grades.Select(grade => algorithm.NextDifficulty(5.0, grade))
            .Should().Equal(8.34176237, 6.66599536, 4.99022837, 3.31446137);
    }

    [Fact]
    public void NextRecallStability_ShouldMatchUpstream()
    {
        var algorithm = CreateAlgorithm();
        double[] d = [1, 2, 3, 4];
        double[] s = [5, 5, 5, 5];
        double[] r = [0.9, 0.8, 0.7, 0.6];

        LegacyHelpers.Grades.Select((grade, i) => algorithm.NextRecallStability(d[i], s[i], r[i], grade))
            .Should().Equal(25.60252118, 28.22657096, 58.65599107, 127.2266925);
    }

    [Fact]
    public void NextForgetStability_ShouldMatchUpstream()
    {
        var algorithm = CreateAlgorithm();
        double[] d = [1, 2, 3, 4];
        double[] s = [5, 5, 5, 5];
        double[] r = [0.9, 0.8, 0.7, 0.6];

        Enumerable.Range(0, 4).Select(i => algorithm.NextForgetStability(d[i], s[i], r[i]))
            .Should().Equal(1.05253961, 1.18943295, 1.36808387, 1.58498896);
    }

    [Fact]
    public void NextShortTermStability_ShouldMatchUpstream()
    {
        var algorithm = CreateAlgorithm();

        // Hard and Good are pinned at the input stability by the max(sinc, 1) floor — a same-day review can
        // never *lose* stability on a passing grade.
        LegacyHelpers.Grades.Select(grade => algorithm.NextShortTermStability(5, grade))
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
            }).Model.NextInterval(new FsrsState(1.0, 0), retention));

        // Upstream notes this sits +3 days off fsrs-rs's own reference figure at retention 0.1, because
        // fsrs-rs computes in f32 and both ts-fsrs and this port compute in f64.
        intervals.Should().Equal(3116766 + 3, 34793, 2508, 387, 90, 27, 9, 3, 1, 1);
    }

    [Fact]
    public void NextInterval_ShouldDefaultDesiredRetentionToRequestRetention()
    {
        var f = new Fsrs(new FsrsParametersInput { RequestRetention = 0.8 });

        f.Model.NextInterval(new FsrsState(1.0, 0), f.Parameters.RequestRetention)
            .Should().Be(f.Model.NextInterval(new FsrsState(1.0, 0), 0.8));
    }

    [Fact]
    public void NextInterval_ShouldNotApplyMaximumInterval()
    {
        // Upstream PR 74.
        var parameters = Defaults.GeneratorParameters(new FsrsParametersInput { MaximumInterval = 365 });
        var (decay, factor) = Fsrs6Algorithm.ComputeDecayFactor(parameters.W);
        var intervalModifier = (Math.Pow(parameters.RequestRetention, 1 / decay) - 1) / factor;
        var f = new Fsrs(new FsrsParametersInput { MaximumInterval = 365 });
        const double stability = 737.47;

        var nextIvl = f.Model.NextInterval(new FsrsState(stability, 0), parameters.RequestRetention);

        nextIvl.Should().Be(Helpers.JsRound(stability * intervalModifier));
        nextIvl.Should().BeGreaterThan(parameters.MaximumInterval);
    }

    [Fact]
    public void ChangeParameters_ShouldFollowTheLiveParametersObject()
    {
        var f = new Fsrs();
        f.Parameters.Should().BeEquivalentTo(Defaults.GeneratorParameters());

        const double requestRetention = 0.8;
        var w19Floor = f.Parameters.EnableShortTerm ? 0.01 : 0;
        double[] updateW =
        [
            1.14, 1.01, 5.44, 14.67, 5.3024, 1.5662, 1.2503, 0.0028, 1.5489, 0.1763, 0.9953, 2.7473,
            0.0179, 0.3105, 0.3976, 0.0, 2.0902, 0.48, 0.64, w19Floor, 0.1542,
        ];
        f.UpdateParameters(new FsrsParametersInput { RequestRetention = requestRetention, W = updateW, EnableFuzz = true });
        f.Parameters.RequestRetention.Should().Be(requestRetention);
        f.Parameters.W.Should().Equal(updateW);
        f.Parameters.EnableFuzz.Should().BeTrue();

        f.Parameters.RequestRetention = Constants.DefaultRequestRetention;
        f.Parameters.RequestRetention.Should().Be(Constants.DefaultRequestRetention);

        f.Parameters.W = Constants.DefaultW.ToArray();
        f.Parameters.W.Should().Equal(Constants.DefaultW);

        f.Parameters.MaximumInterval = 365;
        f.Parameters.MaximumInterval.Should().Be(365);

        f.Parameters.EnableFuzz = Constants.DefaultEnableFuzz;
        f.Parameters.EnableFuzz.Should().Be(Constants.DefaultEnableFuzz);

        f.UpdateParameters(new FsrsParametersInput());
        f.Parameters.Should().BeEquivalentTo(Defaults.GeneratorParameters());

        f.Parameters.EnableShortTerm = false;
        f.Parameters.EnableShortTerm.Should().BeFalse();
    }

    [Fact]
    public void InPlaceWeightMutation_ShouldUpdateTheModelWeights()
    {
        var f = new Fsrs();
        var state = new FsrsState(10, 5);
        const double elapsedDays = 1;
        var initial = f.Model.ForgettingCurve(state, elapsedDays);

        f.Parameters.W[20] = 0.3;

        var updated = f.Model.ForgettingCurve(state, elapsedDays);
        updated.Should().NotBe(initial);
        updated.Should().Be(Fsrs6Algorithm.ComputeForgettingCurve(f.Parameters.W, elapsedDays, state.Stability));
    }

    [Fact]
    public void ChangeParameters_ShouldClipAndMigrateAssignedWeights_AndSwitchTheScheduler()
    {
        var f = new Fsrs();

        f.Parameters.W = new double[21];
        f.Parameters.W.Should().Equal(Constants.ClampParameters(Constants.W17W18Ceiling).Select(range => range.Min));

        // Assigning EnableShortTerm swaps the scheduler: long-term gives a day-scale interval for Again too.
        f.Parameters.W = Constants.DefaultW.ToArray();
        f.Parameters.EnableShortTerm = false;
        var again = f.Next(Defaults.CreateEmptyCard(TestParameters.Epoch), TestParameters.Epoch, Rating.Again).Card;
        again.State.Should().Be(State.Review);
        again.ScheduledDays.Should().BeGreaterThan(0);
    }

    [Fact]
    public void NextInterval_ShouldValidateDesiredRetention()
    {
        var f = new Fsrs();

        f.Model.NextInterval(new FsrsState(1, 0), Constants.DefaultRequestRetention).Should().Be(1);
        foreach (var retention in new[] { 1.2, -0.2 })
        {
            var next = () => f.Model.NextInterval(new FsrsState(1, 0), retention);
            next.Should().Throw<FsrsValidationException>().WithMessage("Desired retention rate should be in the range (0,1]");
        }
    }

    [Fact]
    public void NextState_ShouldTreatAZeroedStateAsBrandNew()
    {
        var f = new Fsrs();

        var fromZeroed = NextState(f, new FsrsState(0, 0), 1, Rating.Again);

        double.IsNaN(fromZeroed.Stability).Should().BeFalse();
        NextState(f, null, 1, Rating.Again).Should().Be(fromZeroed);
    }

    [Fact]
    public void NextState_ShouldRejectADifficultyBelowOne()
    {
        var f = new Fsrs();
        var init = NextState(f, null, 0, Rating.Good);

        var next = () => NextState(f, new FsrsState(init.Stability, 0), 1, Rating.Again);

        next.Should().Throw<FsrsValidationException>().WithMessage("Invalid memory state*");
    }

    [Fact]
    public void NextState_ShouldRejectAStabilityBelowSMin()
    {
        var f = new Fsrs();
        var init = NextState(f, null, 0, Rating.Good);

        var next = () => NextState(f, new FsrsState(0, init.Stability), 1, Rating.Again);

        next.Should().Throw<FsrsValidationException>().WithMessage("Invalid memory state*");
    }

    [Fact]
    public void NextState_ShouldRejectANegativeElapsedTime()
    {
        var f = new Fsrs();

        var next = () => NextState(f, new FsrsState(0, 0), -1, Rating.Again);

        next.Should().Throw<FsrsValidationException>().WithMessage("Invalid delta_t*");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    public void NextState_ShouldRejectAnOutOfRangeGrade(int grade)
    {
        var f = new Fsrs();
        var init = NextState(f, null, 0, Rating.Good);

        var next = () => NextState(f, init, 1, (Rating)grade);

        next.Should().Throw<FsrsValidationException>().WithMessage("Invalid grade*");
    }

    [Fact]
    public void NextState_ShouldClampStabilityToSMin()
    {
        var f = new Fsrs();

        var next = NextState(f, new FsrsState(0.00102011, 9.98210112), 1, Rating.Again);

        next.Stability.Should().BeGreaterThanOrEqualTo(Constants.SMin);
    }

    [Fact]
    public void NextState_ShouldLoseStabilityOnAgain_WithShortTermDisabled()
    {
        var f = new Fsrs(new FsrsParametersInput { EnableShortTerm = false });

        var next = NextState(f, new FsrsState(10.0, 4.0), 1, Rating.Again);

        next.Stability.Should().BeLessThan(10.0);
    }
}
