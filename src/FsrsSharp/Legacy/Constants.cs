using System.Reflection;
using FsrsSharp.Models.Fsrs5;
using FsrsSharp.Models.Fsrs6;

namespace FsrsSharp.Legacy;

/// <summary>
/// Constants ported from ts-fsrs's <c>legacy/constant.ts</c>. Since v6.0.0 they are thin aliases of the
/// FSRS-6 model constants (<see cref="Fsrs6Constants"/>).
/// </summary>
public static class Constants
{
    public const double DefaultRequestRetention = 0.9;
    public const int DefaultMaximumInterval = 36500;
    public const bool DefaultEnableFuzz = false;
    public const bool DefaultEnableShortTerm = true;

    /// <summary>
    /// Package version only (<c>v6.0.0</c>), read from the assembly's informational version, which
    /// <c>Directory.Build.props</c> sets from <c>TsFsrsVersion</c>. Read the selected model name from the model's
    /// <c>Name</c> instead.
    /// </summary>
    public static string FsrsVersion { get; } = "v" + ReadVersion();

    public static readonly double SMin = Fsrs6Constants.ModelBounds["sMin"];
    public static readonly double SMax = Fsrs6Constants.ModelBounds["sMax"];
    public const double InitSMax = Fsrs6Constants.InitSMax;

    /// <summary>The fixed decay used by FSRS-4.5 and FSRS-5, before it became trainable (w20) in FSRS-6.</summary>
    public const double Fsrs5DefaultDecay = Fsrs5Constants.Decay;
    public const double Fsrs6DefaultDecay = Fsrs6Constants.Decay;

    /// <summary>The 21 FSRS-6 default parameters. A fresh copy per call-site is not needed: <see cref="IReadOnlyList{T}"/> is immutable.</summary>
    public static IReadOnlyList<double> DefaultW { get; } = Fsrs6Constants.DefaultWeights;

    public const double W17W18Ceiling = Fsrs6Constants.W17W18Ceiling;

    /// <summary>
    /// The valid [min, max] range for each of the 21 parameters. Returns a fresh array on every call.
    /// </summary>
    /// <remarks>Deprecated upstream: use <see cref="Fsrs6Parameters.Clip"/> to clip weights.</remarks>
    public static (double Min, double Max)[] ClampParameters(double w17W18Ceiling, bool enableShortTerm = DefaultEnableShortTerm) =>
        Fsrs6Constants.ParameterBounds(w17W18Ceiling, enableShortTerm);

    private static string ReadVersion()
    {
        var info = typeof(Constants).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (info is null) return typeof(Constants).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        var plus = info.IndexOf('+');
        return plus >= 0 ? info[..plus] : info;
    }
}
