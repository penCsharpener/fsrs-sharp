using System.Collections;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace FsrsSharp.Kit;

/// <summary>
/// A named, typed slot in a <see cref="FieldBag"/>. This is the C# counterpart of the per-field
/// schemas ts-fsrs v6 / srs-kit compose into a card, review log or scheduler config: every model,
/// chrono and middleware declares the keys it owns, and the scheduler validates them at its
/// boundaries.
/// </summary>
/// <remarks>
/// Keys are identified by <see cref="Name"/>, not by instance: two middlewares that both declare
/// <c>"lapses"</c> share one field, exactly as merged object schemas do upstream. Names use the
/// upstream camelCase spelling so persisted data stays interchangeable with ts-fsrs.
/// </remarks>
public abstract class FieldKey : IEquatable<FieldKey>
{
    private protected FieldKey(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    /// <summary>The field name (upstream camelCase spelling, e.g. <c>"learningStep"</c>).</summary>
    public string Name { get; }

    /// <summary>The CLR type of the stored value.</summary>
    public abstract Type ValueType { get; }

    /// <summary>Whether a missing value is filled with <see cref="DefaultValueBoxed"/> during parsing.</summary>
    public abstract bool HasDefault { get; }

    /// <summary>The default value, boxed. Only meaningful when <see cref="HasDefault"/> is true.</summary>
    public abstract object? DefaultValueBoxed { get; }

    /// <summary>Validates a boxed value. Returns <c>null</c> when valid, else an error message.</summary>
    public abstract string? Validate(object? value);

    /// <summary>
    /// Converts a raw input value to the stored CLR type before validation (upstream schemas accept a
    /// wider input type than they output, e.g. <c>desiredRetention: number | Record</c>). The default
    /// returns the value unchanged.
    /// </summary>
    /// <exception cref="FsrsValidationException">The value cannot be converted.</exception>
    public virtual object? Coerce(object? value) => value;

    /// <summary>
    /// Normalizes a value that already passed <see cref="Validate"/> (upstream schema transforms, e.g.
    /// filling defaults or freezing). Called by the scheduler for config, card, revlog and init-input
    /// fields; the normalized value is what gets stored. The default returns the value unchanged.
    /// </summary>
    public virtual object? Normalize(object? value) => value;

    /// <summary>Coerce, validate, normalize (the order the scheduler applies).</summary>
    /// <exception cref="FsrsValidationException">Coercion or validation failed.</exception>
    internal object? Parse(object? raw)
    {
        var coerced = Coerce(raw);
        var error = Validate(coerced);
        if (error is not null) throw new FsrsValidationException(error);
        return Normalize(coerced);
    }

    public bool Equals(FieldKey? other) => other is not null && other.Name == Name;
    public override bool Equals(object? obj) => obj is FieldKey other && Equals(other);
    public override int GetHashCode() => Name.GetHashCode(StringComparison.Ordinal);
    public override string ToString() => Name;
}

/// <summary>A typed <see cref="FieldKey"/>.</summary>
public sealed class FieldKey<T> : FieldKey
{
    private readonly Func<T, string?>? _validate;
    private readonly Func<object?, T>? _coerce;
    private readonly Func<T, T>? _normalize;
    private readonly T _default = default!;

    /// <summary>A required field (no default).</summary>
    /// <param name="name">Field name.</param>
    /// <param name="validate">Returns an error message, or <c>null</c> when the (coerced) value is valid.</param>
    /// <param name="coerce">Converts raw input to <typeparamref name="T"/> before validation; throws <see cref="FsrsValidationException"/> on failure.</param>
    /// <param name="normalize">Transforms a validated value (stored instead of the input).</param>
    public FieldKey(string name, Func<T, string?>? validate = null, Func<object?, T>? coerce = null, Func<T, T>? normalize = null) : base(name)
    {
        _validate = validate;
        _coerce = coerce;
        _normalize = normalize;
    }

    /// <summary>An optional field that falls back to <paramref name="defaultValue"/> when absent.</summary>
    public FieldKey(string name, T defaultValue, Func<T, string?>? validate = null, Func<object?, T>? coerce = null, Func<T, T>? normalize = null) : base(name)
    {
        _validate = validate;
        _coerce = coerce;
        _normalize = normalize;
        _default = defaultValue;
        HasDefault = true;
    }

    public override object? Coerce(object? value)
    {
        if (_coerce is null) return value;
        try
        {
            return _coerce(value);
        }
        catch (Exception e) when (e is InvalidCastException or FormatException or OverflowException or NullReferenceException)
        {
            throw new FsrsValidationException($"Expected {Name} to be {typeof(T).Name}");
        }
    }

    public override object? Normalize(object? value) =>
        _normalize is not null && value is T typed ? _normalize(typed) : value;

    public override Type ValueType => typeof(T);
    public override bool HasDefault { get; }
    public T Default => _default;
    public override object? DefaultValueBoxed => _default;

    /// <remarks>When the key has a coercion, <paramref name="value"/> is raw input: it is coerced first and a coercion failure is returned as the error.</remarks>
    public override string? Validate(object? value)
    {
        if (_coerce is not null)
        {
            try
            {
                value = Coerce(value);
            }
            catch (FsrsValidationException e)
            {
                return e.Message;
            }
        }

        if (value is null)
        {
            // null is only acceptable for nullable reference/value types.
            if (default(T) is not null) return $"Expected {Name} to be {typeof(T).Name}, got null";
            return _validate?.Invoke(default!);
        }
        if (value is not T typed) return $"Expected {Name} to be {typeof(T).Name}, got {value.GetType().Name}";
        return _validate?.Invoke(typed);
    }
}

/// <summary>
/// An immutable, name-keyed bag of field values with value equality. Cards, review logs and
/// scheduler configs carry the fields contributed by the model, chrono and middlewares here.
/// Typed access goes through <see cref="FieldKey{T}"/>; the extension properties defined next to
/// each middleware/chrono (e.g. <c>card.Reps</c>, <c>card.DueAt</c>) wrap that.
/// </summary>
public sealed class FieldBag : IReadOnlyDictionary<string, object?>, IEquatable<FieldBag>
{
    private readonly ImmutableDictionary<string, object?> _values;

    public static FieldBag Empty { get; } = new(ImmutableDictionary<string, object?>.Empty.WithComparers(StringComparer.Ordinal));

    private FieldBag(ImmutableDictionary<string, object?> values) => _values = values;

    /// <summary>Creates a bag from raw name/value pairs (e.g. deserialized storage).</summary>
    public static FieldBag From(IEnumerable<KeyValuePair<string, object?>> values) =>
        new(ImmutableDictionary.CreateRange(StringComparer.Ordinal, values));

    public T Get<T>(FieldKey<T> key)
    {
        if (_values.TryGetValue(key.Name, out var value)) return (T)value!;
        if (key.HasDefault) return key.Default;
        throw new KeyNotFoundException($"Field '{key.Name}' is not present.");
    }

    public bool TryGet<T>(FieldKey<T> key, [MaybeNullWhen(false)] out T value)
    {
        if (_values.TryGetValue(key.Name, out var raw) && raw is T typed)
        {
            value = typed;
            return true;
        }
        value = default;
        return false;
    }

    public bool Contains(FieldKey key) => _values.ContainsKey(key.Name);

    public FieldBag With<T>(FieldKey<T> key, T value) => new(_values.SetItem(key.Name, value));
    public FieldBag Without(FieldKey key) => new(_values.Remove(key.Name));

    public Builder ToBuilder() => new(_values.ToBuilder());

    // IReadOnlyDictionary
    public object? this[string key] => _values[key];
    public IEnumerable<string> Keys => _values.Keys;
    public IEnumerable<object?> Values => _values.Values;
    public int Count => _values.Count;
    public bool ContainsKey(string key) => _values.ContainsKey(key);
    public bool TryGetValue(string key, out object? value) => _values.TryGetValue(key, out value);
    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => _values.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public bool Equals(FieldBag? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (other.Count != Count) return false;
        foreach (var (k, v) in _values)
        {
            if (!other._values.TryGetValue(k, out var ov)) return false;
            if (!ValueEquals(v, ov)) return false;
        }
        return true;
    }

    private static bool ValueEquals(object? a, object? b)
    {
        if (Equals(a, b)) return true;
        // Lists (e.g. learning steps, weights) compare element-wise.
        if (a is IEnumerable ea && b is IEnumerable eb && a is not string && b is not string)
            return ea.Cast<object?>().SequenceEqual(eb.Cast<object?>());
        return false;
    }

    public override bool Equals(object? obj) => obj is FieldBag other && Equals(other);

    public override int GetHashCode()
    {
        var hash = 0;
        foreach (var (k, v) in _values)
            hash ^= HashCode.Combine(k, v is IEnumerable and not string ? 0 : v?.GetHashCode() ?? 0);
        return hash;
    }

    public override string ToString() =>
        "{ " + string.Join(", ", _values.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key} = {p.Value}")) + " }";

    /// <summary>Mutable builder. Supports collection initializers: <c>new FieldBag.Builder { { Key, value } }</c>.</summary>
    public sealed class Builder : IEnumerable<KeyValuePair<string, object?>>
    {
        private readonly ImmutableDictionary<string, object?>.Builder _values;

        public Builder() : this(ImmutableDictionary.CreateBuilder<string, object?>(StringComparer.Ordinal)) { }
        internal Builder(ImmutableDictionary<string, object?>.Builder values) => _values = values;

        public void Add<T>(FieldKey<T> key, T value) => _values[key.Name] = value;
        /// <summary>Stores a raw (uncoerced) input value for <paramref name="key"/>; the scheduler coerces and validates it.</summary>
        public void AddRaw(FieldKey key, object? value) => _values[key.Name] = value;
        public Builder Set<T>(FieldKey<T> key, T value) { _values[key.Name] = value; return this; }
        public void SetRaw(string name, object? value) => _values[name] = value;
        public bool Remove(FieldKey key) => _values.Remove(key.Name);
        public bool Contains(FieldKey key) => _values.ContainsKey(key.Name);

        public T Get<T>(FieldKey<T> key)
        {
            if (_values.TryGetValue(key.Name, out var value)) return (T)value!;
            if (key.HasDefault) return key.Default;
            throw new KeyNotFoundException($"Field '{key.Name}' is not present.");
        }

        public bool TryGet<T>(FieldKey<T> key, [MaybeNullWhen(false)] out T value)
        {
            if (_values.TryGetValue(key.Name, out var raw) && raw is T typed)
            {
                value = typed;
                return true;
            }
            value = default;
            return false;
        }

        /// <summary>Copies every field of <paramref name="source"/> over this builder (later wins), like <c>Object.assign</c>.</summary>
        public Builder AssignFrom(IEnumerable<KeyValuePair<string, object?>> source)
        {
            foreach (var (k, v) in source) _values[k] = v;
            return this;
        }

        public FieldBag ToFieldBag() => new(_values.ToImmutable());

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => _values.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
