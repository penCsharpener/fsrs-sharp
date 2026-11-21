namespace FsrsSharp.Legacy;

/// <summary>
/// Direct port of the Alea PRNG used by ts-fsrs for fuzzing (itself a port of
/// Johannes Baagøe's algorithm, via davidbau/seedrandom). Deterministic for a
/// given seed string, so scheduling with fuzz enabled stays reproducible.
/// </summary>
internal sealed class Alea
{
    private double _c;
    private double _s0;
    private double _s1;
    private double _s2;

    public Alea(string? seed)
    {
        var mash = new Mash();
        _c = 1;
        _s0 = mash.Hash(" ");
        _s1 = mash.Hash(" ");
        _s2 = mash.Hash(" ");

        seed ??= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();

        _s0 -= mash.Hash(seed);
        if (_s0 < 0) _s0 += 1;
        _s1 -= mash.Hash(seed);
        if (_s1 < 0) _s1 += 1;
        _s2 -= mash.Hash(seed);
        if (_s2 < 0) _s2 += 1;
    }

    /// <summary>Returns the next pseudo-random double in [0, 1), like calling the JS <c>alea()</c> generator.</summary>
    public double Next()
    {
        var t = 2091639.0 * _s0 + _c * 2.3283064365386963e-10; // 2^-32
        _s0 = _s1;
        _s1 = _s2;
        _c = Math.Floor(t);
        _s2 = t - _c;
        return _s2;
    }

    /// <summary>The generator state: <c>alea.state()</c> / <c>alea.importState()</c>.</summary>
    public AleaState State
    {
        get => new(_c, _s0, _s1, _s2);
        set => (_c, _s0, _s1, _s2) = (value.C, value.S0, value.S1, value.S2);
    }

    /// <summary>A signed 32-bit integer: <c>(next() * 2^32) | 0</c>.</summary>
    public int Int32() => unchecked((int)(uint)(Next() * 4294967296.0));

    /// <summary>A 53-bit-resolution double in [0, 1) (ts-fsrs's <c>prng.double()</c>).</summary>
    public double Double()
    {
        var high = Next();
        var low = Next();
        return high + (int)(low * 2097152.0) * 1.1102230246251565e-16; // 2^-53
    }

    private sealed class Mash
    {
        private double _n = 0xefc8249d;

        public double Hash(string data)
        {
            foreach (var ch in data)
            {
                _n += ch;
                var h = 0.02519603282416938 * _n;
                _n = ToUint32(h);
                h -= _n;
                h *= _n;
                _n = ToUint32(h);
                h -= _n;
                _n += h * 4294967296.0; // 2^32
            }

            return ToUint32(_n) * 2.3283064365386963e-10; // 2^-32
        }

        // Mirrors JS's `>>> 0` (unsigned 32-bit truncation) on a double.
        private static double ToUint32(double x) => unchecked((uint)(long)x);
    }
}

/// <summary>Snapshot of an <see cref="Alea"/> generator.</summary>
internal readonly record struct AleaState(double C, double S0, double S1, double S2);
