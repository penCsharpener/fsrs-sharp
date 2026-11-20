using System.Collections;
using FsrsSharp.Kit;

namespace FsrsSharp.Models.Fsrs7;

/// <summary>FSRS-7 schemas (ts-fsrs <c>models/fsrs-7/schema.ts</c>): decay, config and memory state.</summary>
public static class Fsrs7Schema
{
    public const string ConfigMessage = "Expected FSRS7 config with exactly 34 finite weights";
    public const string DecayMessage = "Expected finite decay in the range [0.01, 0.95]";

    /// <summary><c>decaySchema</c>: a finite number in [0.01, 0.95].</summary>
    public static FieldKey<double> Decay { get; } = new("decay", value =>
        double.IsFinite(value) && value >= 0.01 && value <= 0.95 ? null : DecayMessage);

    /// <summary>The <c>weights</c> config field: exactly 34 finite numbers.</summary>
    public static FieldKey<IReadOnlyList<double>> Weights { get; } = new("weights", value =>
        value is not null && value.Count == Fsrs7Constants.DefaultWeights.Count && value.All(double.IsFinite)
            ? null
            : ConfigMessage);

    /// <summary>
    /// <c>fsrs7ConfigSchema.parse</c>: requires a <c>weights</c> field holding exactly 34 finite numbers
    /// and returns a config carrying only those. Throws <see cref="FsrsValidationException"/> otherwise.
    /// </summary>
    public static Fsrs7Config ParseConfig(FieldBag? config)
    {
        if (config is null || !config.TryGetValue(Weights.Name, out var raw))
            throw new FsrsValidationException(ConfigMessage);
        return ParseWeights(raw);
    }

    /// <summary>Validates a raw (possibly loosely typed) weights value.</summary>
    public static Fsrs7Config ParseWeights(object? raw)
    {
        if (raw is IReadOnlyList<double> list)
        {
            if (Weights.Validate(list) is { } error) throw new FsrsValidationException(error);
            return new Fsrs7Config(list);
        }
        if (raw is IEnumerable and not string and IEnumerable<object?> items)
        {
            var values = new List<double>();
            foreach (var item in items)
            {
                if (item is not double d || !double.IsFinite(d)) throw new FsrsValidationException(ConfigMessage);
                values.Add(d);
            }
            if (values.Count != Fsrs7Constants.DefaultWeights.Count) throw new FsrsValidationException(ConfigMessage);
            return new Fsrs7Config(values.ToArray());
        }
        throw new FsrsValidationException(ConfigMessage);
    }
}
