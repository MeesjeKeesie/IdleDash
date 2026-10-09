using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace IdleDash.Core;

/// <summary>Een klein maantje dat de fase van vandaag laat zien (verlicht deel in warm maanlicht).</summary>
public static class MoonDrawing
{
    private static readonly SolidColorBrush Moonlight = Frozen(Color.FromRgb(0xF4, 0xD9, 0xA3));

    public static FrameworkElement Create(double phase, double size)
    {
        var grid = new Grid { Width = size, Height = size };
        var dark = new Ellipse { Width = size, Height = size, Opacity = 0.3 };
        dark.SetResourceReference(Shape.FillProperty, "TextMutedBrush");
        grid.Children.Add(dark);
        grid.Children.Add(new System.Windows.Shapes.Path { Data = LitGeometry(phase, size / 2), Fill = Moonlight });
        return grid;
    }

    /// <summary>Het verlichte deel: een halve cirkel aan de lichte kant plus de schaduwgrens (een halve ellips).</summary>
    public static Geometry LitGeometry(double phase, double r)
    {
        double illumination = (1 - Math.Cos(2 * Math.PI * phase)) / 2;
        if (illumination < 0.02) return Geometry.Empty;
        if (illumination > 0.98) return new EllipseGeometry(new Point(r, r), r, r);
        bool waxing = phase < 0.5, gibbous = illumination > 0.5;
        var top = new Point(r, 0);
        var bottom = new Point(r, 2 * r);
        var figure = new PathFigure { StartPoint = top, IsClosed = true };
        figure.Segments.Add(new ArcSegment(bottom, new Size(r, r), 0, false, waxing ? SweepDirection.Clockwise : SweepDirection.Counterclockwise, true));
        var back = waxing == gibbous ? SweepDirection.Clockwise : SweepDirection.Counterclockwise;
        figure.Segments.Add(new ArcSegment(top, new Size(Math.Max(r * Math.Abs(Math.Cos(2 * Math.PI * phase)), 0.01), r), 0, false, back, true));
        var geometry = new PathGeometry(new[] { figure });
        geometry.Freeze();
        return geometry;
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
