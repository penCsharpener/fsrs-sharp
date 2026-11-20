namespace FsrsSharp.Middlewares;

/// <summary>Keeps rating intervals monotonic (ts-fsrs <c>middlewares/monotonic-interval/core.ts</c>).</summary>
public static class MonotonicInterval
{
    private const double SecondsPerDay = 86400;

    /// <summary>
    /// Keeps rating intervals monotonic after rounding and fuzzing. The first interval is capped at the
    /// maximum. Zero-day intervals may remain equal; after a positive result, each later interval is kept
    /// at least one day after the previous result until the maximum is reached. The input is never
    /// mutated. Accepts one to four candidates (Again..Easy).
    /// </summary>
    public static double[] CalculateScheduleDays(IReadOnlyList<double> candidates, double maximumInterval)
    {
        if (candidates.Count is < 1 or > 4)
            throw new ArgumentException("Expected 1 to 4 interval candidates", nameof(candidates));

        var result = new double[candidates.Count];
        result[0] = Math.Min(candidates[0], maximumInterval);
        for (var i = 1; i < candidates.Count; i++)
        {
            var previous = result[i - 1];
            result[i] = Math.Min(Math.Max(candidates[i], previous == 0 ? 0 : previous + 1), maximumInterval);
        }
        return result;
    }

    /// <summary>
    /// Returns the interval of the last candidate after every lower rating was kept monotonic. Once a
    /// day interval appears, later ratings advance by at least one day and sub-day candidates are
    /// ignored. Otherwise sub-day intervals never decrease, and a candidate that is not lower than the
    /// previous one advances by one second when it would land on the same second as the previous result.
    /// Zero-day intervals may remain equal.
    /// </summary>
    public static double CalculateScheduleDay(IReadOnlyList<double> candidates, double maximumInterval)
    {
        double? scheduledDay = null;
        var subDay = candidates[0];
        for (var index = 0; index < candidates.Count; index++)
        {
            var interval = candidates[index];
            if (interval >= 1)
            {
                scheduledDay = Math.Min(
                    scheduledDay is null ? interval : Math.Max(interval, scheduledDay.Value + 1),
                    maximumInterval);
            }
            else if (scheduledDay is null && index > 0)
            {
                var raised = Math.Max(interval, subDay);
                // A rating that is not lower than the previous one moves past it when both land on the
                // same second; a lower rating may share its interval.
                subDay = subDay > 0
                    && ToSeconds(interval) >= ToSeconds(candidates[index - 1])
                    && ToSeconds(raised) == ToSeconds(subDay)
                    ? subDay + 1 / SecondsPerDay
                    : raised;
            }
        }
        return scheduledDay ?? subDay;
    }

    private static double ToSeconds(double days) => Helpers.JsRound(days * SecondsPerDay);
}
