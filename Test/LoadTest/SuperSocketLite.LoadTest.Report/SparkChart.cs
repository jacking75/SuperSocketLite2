using System.Globalization;
using System.Text;

namespace SuperSocketLite.LoadTest.Report;

/// <summary>
/// 시계열 하나를 인라인 SVG 꺾은선으로 그립니다.
/// </summary>
/// <remarks>
/// 리포트는 파일 하나로 열려야 하므로 외부 차트 라이브러리를 쓰지 않습니다.
/// 값의 절대 크기보다 추세(늘고 있는지, 흔들리는지)를 읽는 것이 목적입니다.
/// </remarks>
public static class SparkChart
{
    private const int Width = 720;
    private const int Height = 160;
    private const int PadLeft = 56;
    private const int PadRight = 12;
    private const int PadTop = 12;
    private const int PadBottom = 24;

    public static string Render(string title, IReadOnlyList<(double X, double Y)> points, string unit, string color)
    {
        if (points.Count == 0)
            return $"<div class=\"chart empty\"><span class=\"chart-title\">{Html.Escape(title)}</span><p class=\"muted\">표본이 없다.</p></div>";

        var minX = points.Min(p => p.X);
        var maxX = points.Max(p => p.X);
        var maxY = points.Max(p => p.Y);
        var minY = Math.Min(0, points.Min(p => p.Y));

        if (maxY <= minY)
            maxY = minY + 1;
        if (maxX <= minX)
            maxX = minX + 1;

        var plotWidth = Width - PadLeft - PadRight;
        var plotHeight = Height - PadTop - PadBottom;

        var path = new StringBuilder();
        for (var i = 0; i < points.Count; i++)
        {
            var (x, y) = points[i];
            var px = PadLeft + ((x - minX) / (maxX - minX) * plotWidth);
            var py = PadTop + plotHeight - ((y - minY) / (maxY - minY) * plotHeight);
            path.Append(i == 0 ? 'M' : 'L');
            path.Append(px.ToString("F1", CultureInfo.InvariantCulture));
            path.Append(' ');
            path.Append(py.ToString("F1", CultureInfo.InvariantCulture));
            path.Append(' ');
        }

        var svg = new StringBuilder();
        svg.Append($"<div class=\"chart\"><span class=\"chart-title\">{Html.Escape(title)}</span>");
        svg.Append($"<svg viewBox=\"0 0 {Width} {Height}\" role=\"img\" aria-label=\"{Html.Escape(title)}\">");

        // 가로 눈금 3개면 추세를 읽기에 충분하고 선을 가리지 않는다.
        for (var i = 0; i <= 2; i++)
        {
            var value = minY + ((maxY - minY) * i / 2.0);
            var y = PadTop + plotHeight - ((value - minY) / (maxY - minY) * plotHeight);
            svg.Append($"<line class=\"grid\" x1=\"{PadLeft}\" y1=\"{y.ToString("F1", CultureInfo.InvariantCulture)}\" x2=\"{Width - PadRight}\" y2=\"{y.ToString("F1", CultureInfo.InvariantCulture)}\" />");
            svg.Append($"<text class=\"axis\" x=\"{PadLeft - 6}\" y=\"{(y + 4).ToString("F1", CultureInfo.InvariantCulture)}\" text-anchor=\"end\">{FormatTick(value)}</text>");
        }

        svg.Append($"<path class=\"series\" style=\"stroke:{color}\" d=\"{path.ToString().Trim()}\" />");
        svg.Append($"<text class=\"axis\" x=\"{PadLeft}\" y=\"{Height - 6}\">{(minX / 1000).ToString("F0", CultureInfo.InvariantCulture)}s</text>");
        svg.Append($"<text class=\"axis\" x=\"{Width - PadRight}\" y=\"{Height - 6}\" text-anchor=\"end\">{(maxX / 1000).ToString("F0", CultureInfo.InvariantCulture)}s</text>");
        svg.Append("</svg>");
        svg.Append($"<span class=\"chart-unit\">{Html.Escape(unit)} · 최대 {FormatTick(maxY)}</span>");
        svg.Append("</div>");

        return svg.ToString();
    }

    private static string FormatTick(double value)
    {
        if (Math.Abs(value) >= 1000)
            return value.ToString("N0", CultureInfo.InvariantCulture);

        return Math.Abs(value) >= 10
            ? value.ToString("F0", CultureInfo.InvariantCulture)
            : value.ToString("F2", CultureInfo.InvariantCulture);
    }
}
