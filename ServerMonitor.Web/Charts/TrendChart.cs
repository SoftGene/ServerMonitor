using System.Globalization;

namespace ServerMonitor.Web.Charts;

/// <summary>One unbroken stretch of hours, as the two shapes that draw it.</summary>
/// <param name="Area">Polygon points for the band of hourly peaks.</param>
/// <param name="Average">Polyline points for the hourly averages.</param>
public sealed record TrendSegment(string Area, string Average);

/// <summary>
/// Turns hourly summaries into SVG coordinates.
/// </summary>
/// <remarks>
/// A plain static class rather than logic inside the page, because everything here is worth
/// testing and none of it needs a browser: given a list of hours it returns strings, and the two
/// things most likely to be wrong — where a line breaks, and how a number is formatted — are both
/// visible in those strings. The page keeps the parts that genuinely need a page.
/// </remarks>
public static class TrendChart
{
    public const double Width = 1000;

    // 0% sits on the baseline at 210 and 100% at 10, leaving room for the stroke at either end.
    private const double Baseline = 210;
    private const double Height = 200;

    /// <summary>
    /// A gap wider than this starts a new segment rather than continuing the line.
    /// </summary>
    /// <remarks>
    /// Summaries are hourly, so consecutive points are an hour apart. Ninety minutes is the
    /// nearest round number that tolerates the ordinary case and still catches a missing hour.
    /// </remarks>
    private static readonly TimeSpan MaxGap = TimeSpan.FromMinutes(90);

    public static double Y(double percent) => Baseline - Math.Clamp(percent, 0, 100) / 100 * Height;

    /// <summary>
    /// Formats a number for SVG, which only ever reads a full stop as the decimal point.
    /// </summary>
    /// <remarks>
    /// The very first defect this project shipped was exactly this: a Russian locale rendered
    /// "12,5" into an attribute, the browser gave up on the whole shape, and every indicator went
    /// blank. Machine formats take the invariant culture; only text shown to a person takes the
    /// user's.
    /// </remarks>
    public static string Num(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>
    /// Builds the drawable segments, splitting the line wherever the data stops.
    /// </summary>
    /// <remarks>
    /// A single polyline through every point would draw a straight line across an outage, which
    /// reads as "steady" when the truth is "nothing was measured". The gap stays a gap.
    /// </remarks>
    /// <param name="hours">The summaries, oldest first.</param>
    /// <param name="average">Picks the averaged value of the metric being drawn.</param>
    /// <param name="peak">Picks the peak value of the metric being drawn.</param>
    public static List<TrendSegment> Build<T>(
        IReadOnlyList<T> hours,
        Func<T, DateTime> timestamp,
        Func<T, double> average,
        Func<T, double> peak)
    {
        var segments = new List<TrendSegment>();

        if (hours.Count == 0)
        {
            return segments;
        }

        var startUtc = timestamp(hours[0]);
        var span = (timestamp(hours[^1]) - startUtc).TotalHours;

        // A single hour of data would otherwise divide by zero and, having no width, draw nothing
        // at all. Giving it the full width shows the one value it does have.
        double X(DateTime hour) => span <= 0 ? 0 : (hour - startUtc).TotalHours / span * Width;

        var run = new List<T>();

        foreach (var hour in hours)
        {
            if (run.Count > 0 && timestamp(hour) - timestamp(run[^1]) > MaxGap)
            {
                Flush();
                run = [];
            }

            run.Add(hour);
        }

        Flush();

        return segments;

        void Flush()
        {
            if (run.Count == 0)
            {
                return;
            }

            var xs = run.Select(item => X(timestamp(item))).ToList();
            var points = run;

            // A run of one hour has no length to draw a line along, so it becomes a short stroke.
            // Otherwise an isolated hour after an outage would be drawn and remain invisible.
            if (run.Count == 1)
            {
                xs = [Math.Max(0, xs[0] - 2), Math.Min(Width, xs[0] + 2)];
                points = [run[0], run[0]];
            }

            var averageLine = string.Join(' ', points.Select((item, i) =>
                $"{Num(xs[i])},{Num(Y(average(item)))}"));

            // The peak is drawn as an area down to the baseline rather than a second line: two
            // lines of the same shape are hard to tell apart, a band behind a line is not.
            var area = string.Join(' ', points.Select((item, i) =>
                    $"{Num(xs[i])},{Num(Y(peak(item)))}"))
                + $" {Num(xs[^1])},{Num(Y(0))} {Num(xs[0])},{Num(Y(0))}";

            segments.Add(new TrendSegment(area, averageLine));
        }
    }
}
