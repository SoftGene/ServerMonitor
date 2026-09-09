// The Web project is referenced under an alias: it and the API both have a Program class from
// their top-level statements, and without this the name would be ambiguous in this assembly.
extern alias WebApp;

using System.Globalization;
using WebApp::ServerMonitor.Web.Charts;

namespace ServerMonitor.Tests.Web;

/// <summary>
/// The chart's geometry, tested without a browser.
/// </summary>
/// <remarks>
/// This is what the interface logic looks like once the part worth testing is separated from the
/// part that needs a page: given hours in, strings out. What these check is not "does it look
/// right" — that still takes eyes — but the two things that are wrong silently. A line drawn
/// straight across an outage looks perfectly fine and states something false, and a decimal comma
/// in an attribute makes the browser discard the whole shape without a word in the console.
/// </remarks>
public class TrendChartTests
{
    private sealed record Hour(DateTime At, double Avg, double Max);

    private static List<TrendSegment> Build(params Hour[] hours) =>
        TrendChart.Build(hours, h => h.At, h => h.Avg, h => h.Max);

    private static DateTime At(int hour) => new(2026, 6, 1, hour, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ContinuousHours_DrawOneSegment()
    {
        var segments = Build(
            new Hour(At(9), 10, 20),
            new Hour(At(10), 30, 40),
            new Hour(At(11), 50, 60));

        var segment = Assert.Single(segments);

        // Three points, so two spaces between them.
        Assert.Equal(3, segment.Average.Split(' ').Length);
    }

    [Fact]
    public void AMissingHour_BreaksTheLine()
    {
        // 09:00, 10:00, then nothing until 14:00 — the machine was off.
        var segments = Build(
            new Hour(At(9), 10, 20),
            new Hour(At(10), 10, 20),
            new Hour(At(14), 10, 20),
            new Hour(At(15), 10, 20));

        // Joining these would draw a flat line across four missing hours, which reads as "steady
        // at 10%" when nothing at all was measured. Two segments leave the gap visible.
        Assert.Equal(2, segments.Count);
        Assert.All(segments, s => Assert.Equal(2, s.Average.Split(' ').Length));
    }

    [Fact]
    public void TheFirstAndLastHour_SitAtTheEdges()
    {
        var segments = Build(
            new Hour(At(9), 0, 0),
            new Hour(At(10), 0, 0),
            new Hour(At(11), 0, 0));

        var points = Assert.Single(segments).Average.Split(' ');

        Assert.StartsWith("0,", points[0]);
        Assert.StartsWith("1000,", points[^1]);
    }

    [Fact]
    public void ASingleHour_IsStillVisible()
    {
        var segments = Build(new Hour(At(9), 50, 50));

        var points = Assert.Single(segments).Average.Split(' ');

        // A run of one has no width, and a zero-length polyline draws nothing at all. It is given
        // a short stroke instead, so an isolated hour after an outage can be seen.
        Assert.Equal(2, points.Length);
        Assert.NotEqual(points[0], points[1]);
    }

    [Fact]
    public void ThePeakArea_ClosesDownToTheBaseline()
    {
        var segments = Build(
            new Hour(At(9), 10, 90),
            new Hour(At(10), 10, 90));

        var points = Assert.Single(segments).Area.Split(' ');

        // Two hours plus the two corners that bring the shape back along the bottom.
        Assert.Equal(4, points.Length);

        var baseline = TrendChart.Num(TrendChart.Y(0));
        Assert.EndsWith(baseline, points[^1]);
        Assert.EndsWith(baseline, points[^2]);
    }

    [Fact]
    public void PercentagesMapOntoTheDrawing()
    {
        // Full scale at the top, empty at the bottom, and half exactly between the two.
        Assert.Equal(10, TrendChart.Y(100));
        Assert.Equal(210, TrendChart.Y(0));
        Assert.Equal(110, TrendChart.Y(50));
    }

    [Fact]
    public void AValueOutsideTheScale_IsClamped()
    {
        // A disk that reports more used than total would otherwise draw above the top of the
        // chart and quietly ruin the layout.
        Assert.Equal(TrendChart.Y(100), TrendChart.Y(140));
        Assert.Equal(TrendChart.Y(0), TrendChart.Y(-5));
    }

    [Fact]
    public void CoordinatesUseAFullStop_WhateverTheCulture()
    {
        var original = CultureInfo.CurrentCulture;

        try
        {
            // Russian formats 12.5 as "12,5". SVG reads the comma as a separator between
            // coordinates, so a single localised number turns one point into two malformed ones
            // and the browser discards the entire shape — silently. This is the defect this
            // project shipped first, and this test is the reason it cannot come back here.
            CultureInfo.CurrentCulture = new CultureInfo("ru-RU");

            var segments = Build(
                new Hour(At(9), 12.5, 12.5),
                new Hour(At(10), 33.3, 33.3));

            var average = Assert.Single(segments).Average;

            Assert.DoesNotContain(",,", average);

            foreach (var point in average.Split(' '))
            {
                var parts = point.Split(',');

                Assert.Equal(2, parts.Length);
                Assert.True(double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out _));
                Assert.True(double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out _));
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void NoHours_DrawNothing()
    {
        Assert.Empty(Build());
    }
}
