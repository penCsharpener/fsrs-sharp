using FsrsSharp.Legacy;

namespace FsrsSharp.Tests.Legacy;

/// <summary>The exact inputs ts-fsrs's own <c>__tests__</c> suite drives its assertions with, kept in one
/// place so every test here is verifiably running against the upstream fixtures rather than a re-typed
/// approximation of them. The weight vectors are spelled out literally — deliberately *not* read from
/// <see cref="Constants"/> — since a test that asserts golden values must fail if this library's own defaults
/// ever drift away from upstream's, not silently follow them.</summary>
internal static class TestParameters
{
    /// <summary>FSRS-6 (21 weights), from <c>FSRS-6.test.ts</c>.</summary>
    public static readonly double[] W =
    {
        0.212, 1.2931, 2.3065, 8.2956, 6.4133, 0.8334, 3.0194, 0.001, 1.8722,
        0.1666, 0.796, 1.4835, 0.0614, 0.2629, 1.6483, 0.6014, 1.8729, 0.5425,
        0.0912, 0.0658, 0.1542,
    };

    /// <summary>FSRS-5 (19 weights), from <c>FSRS-5.test.ts</c> — migrated to 21 on the way in.</summary>
    public static readonly double[] W5 =
    {
        0.40255, 1.18385, 3.173, 15.69105, 7.1949, 0.5345, 1.4604, 0.0046, 1.54575,
        0.1192, 1.01925, 1.9395, 0.11, 0.29605, 2.2698, 0.2315, 2.9898, 0.51655, 0.6621,
    };

    /// <summary>FSRS-4.5 (17 weights), from <c>default.test.ts</c> / <c>rollback.test.ts</c>.</summary>
    public static readonly double[] W45 =
    {
        0.4, 0.6, 2.4, 5.8, 4.93, 0.94, 0.86, 0.01, 1.49, 0.14, 0.94, 2.18,
        0.05, 0.34, 1.26, 0.29, 2.61,
    };

    /// <summary>The 17-weight set <c>rollback.test.ts</c> and <c>forget.test.ts</c> share.</summary>
    public static readonly double[] WRollback =
    {
        1.14, 1.01, 5.44, 14.67, 5.3024, 1.5662, 1.2503, 0.0028, 1.5489, 0.1763,
        0.9953, 2.7473, 0.0179, 0.3105, 0.3976, 0.0, 2.0902,
    };

    /// <summary>The 19-weight set <c>impl/long-term_scheduler.test.ts</c> drives every golden vector with.</summary>
    public static readonly double[] WLongTerm =
    {
        0.4197, 1.1869, 3.0412, 15.2441, 7.1434, 0.6477, 1.0007, 0.0674, 1.6597,
        0.1712, 1.1178, 2.0225, 0.0904, 0.3025, 2.1214, 0.2498, 2.9466, 0.4891, 0.6468,
    };

    /// <summary>The review instant every upstream golden-vector scenario starts from. Upstream writes
    /// <c>new Date(2022, 11, 29, 12, 30, 0, 0)</c> — a *local* time — but nothing in FSRS-6 depends on the
    /// absolute instant, only on the differences between reviews, so pinning it to UTC here keeps the tests
    /// reproducible on any machine without changing a single asserted number.</summary>
    public static readonly DateTime Epoch = new(2022, 12, 29, 12, 30, 0, DateTimeKind.Utc);

    /// <summary>An instant with no upstream significance, for tests that only need *a* fixed clock.</summary>
    public static readonly DateTime FixedClock = new(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static Fsrs Create(bool? enableShortTerm = null, double? requestRetention = null) =>
        new(new FsrsParametersInput
        {
            W = W,
            EnableShortTerm = enableShortTerm,
            RequestRetention = requestRetention,
        });

    public static Fsrs CreateWith(double[] w, bool? enableShortTerm = null) =>
        new(new FsrsParametersInput { W = w, EnableShortTerm = enableShortTerm });

    public static DateTime Utc(int year, int month, int day, int hour = 0, int minute = 0, int second = 0, int ms = 0) =>
        new(year, month, day, hour, minute, second, ms, DateTimeKind.Utc);
}
