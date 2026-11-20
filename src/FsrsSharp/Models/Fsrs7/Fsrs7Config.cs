namespace FsrsSharp.Models.Fsrs7;

/// <summary>The FSRS-7 model configuration (ts-fsrs <c>FSRS7Config</c>).</summary>
public sealed record Fsrs7Config(IReadOnlyList<double> Weights);
