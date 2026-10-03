using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using IdleDash.Core;
using IdleDash.Services;

namespace IdleDash.Widgets;

/// <summary>
/// Kiezer voor een lamp of groep: kleur (kleurvlak en kleurbolletjes), wittint (een balk van warm naar koel,
/// in de echte kleur van het wit) en helderheid. Opent via het knopje op de tegel.
/// </summary>
public sealed class LightPicker : Border
{
    private const double FieldWidth = 268;
    private static readonly (double Hue, double Saturation)[] Presets =
        { (0, 100), (25, 100), (50, 100), (120, 85), (175, 85), (220, 100), (275, 85), (320, 70) };

    private readonly Func<SmartAction, int?, int?, Task<string?>> _apply;
    private readonly TextBlock _status = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly DispatcherTimer _throttle = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private (SmartAction Action, int? Value, int? Value2)? _pending;

    public LightPicker(SmartDevice device, Func<SmartAction, int?, int?, Task<string?>> apply)
    {
        _apply = apply;
        Width = FieldWidth + 34;
        Padding = new Thickness(16, 14, 16, 16);
        CornerRadius = new CornerRadius(16);
        BorderThickness = new Thickness(1);
        SetResourceReference(BackgroundProperty, "PanelBrush");
        SetResourceReference(BorderBrushProperty, "PanelBorderBrush");
        SetResourceReference(TextElement.FontFamilyProperty, "UiFont");
        _throttle.Tick += async (_, _) => await FlushAsync();

        var panel = new StackPanel();
        panel.Children.Add(Label(device.Name, 17, "TextPrimaryBrush", top: 0));
        if (device.Color is { SupportsColor: true } color)
        {
            panel.Children.Add(Label(Loc.T("Kleur"), 13, "TextSecondaryBrush"));
            panel.Children.Add(ColorField(color.Hue, color.Saturation));
            panel.Children.Add(Swatches());
        }
        if (device.Color is { SupportsTemperature: true } white)
        {
            panel.Children.Add(Label(Loc.T("Wit"), 13, "TextSecondaryBrush"));
            panel.Children.Add(TemperatureBar(white));
        }
        if (device.Brightness != null)
        {
            panel.Children.Add(Label(Loc.T("Helderheid"), 13, "TextSecondaryBrush"));
            panel.Children.Add(BrightnessBar(device));
        }
        _status.SetResourceReference(TextBlock.ForegroundProperty, "BarWarnBrush");
        panel.Children.Add(_status);
        Child = panel;
    }

    /// <summary>De kleur waarin een lamp nu brandt (voor de helderheidsbalk en de tegel), of null als dat niet bekend is.</summary>
    public static Color? LampColor(LightColor? color)
    {
        if (color?.Kelvin is int kelvin)
        {
            var (r, g, b) = ColorMath.KelvinToRgb(kelvin);
            return Color.FromRgb(r, g, b);
        }
        if (color?.Hue is double hue && color.Saturation is double saturation)
        {
            var (r, g, b) = ColorMath.HsToRgb(hue, saturation);
            return Color.FromRgb(r, g, b);
        }
        return null;
    }

    // ─────────────────────────── Versturen ───────────────────────────

    /// <summary>Tijdens het slepen hooguit drie keer per seconde versturen; bij loslaten altijd de laatste stand.</summary>
    private void Send(SmartAction action, int? value, int? value2, bool final)
    {
        _pending = (action, value, value2);
        if (final)
        {
            _throttle.Stop();
            _ = FlushAsync();
        }
        else if (!_throttle.IsEnabled)
        {
            _throttle.Start();
        }
    }

    private async Task FlushAsync()
    {
        _throttle.Stop();
        if (_pending is not { } step) return;
        _pending = null;
        _status.Text = await _apply(step.Action, step.Value, step.Value2) ?? "";
    }

    // ─────────────────────────── Onderdelen ───────────────────────────

    private FrameworkElement ColorField(double? hue, double? saturation)
    {
        const double height = 120;
        var hues = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        for (int i = 0; i <= 6; i++)
        {
            var (r, g, b) = ColorMath.HsToRgb(i * 60, 100);
            hues.GradientStops.Add(new GradientStop(Color.FromRgb(r, g, b), i / 6.0));
        }
        var canvas = new Canvas
        {
            Width = FieldWidth,
            Height = height,
            Background = hues,
            Cursor = Cursors.Cross,
            Clip = new RectangleGeometry(new Rect(0, 0, FieldWidth, height), 10, 10),
        };
        // Naar onderen toe steeds witter: bovenin volle kleur, onderin wit
        canvas.Children.Add(new Rectangle
        {
            Width = FieldWidth,
            Height = height,
            Fill = new LinearGradientBrush(Color.FromArgb(0, 255, 255, 255), Colors.White, 90),
            IsHitTestVisible = false,
        });
        var marker = Marker(16);
        canvas.Children.Add(marker);
        void Place(double x, double y)
        {
            Canvas.SetLeft(marker, x - 8);
            Canvas.SetTop(marker, y - 8);
        }
        if (hue is double h && saturation is double s) Place(h / 360 * FieldWidth, (1 - s / 100) * height);
        else marker.Visibility = Visibility.Collapsed;

        AttachDrag(canvas, (point, final) =>
        {
            double x = Math.Clamp(point.X, 0, FieldWidth), y = Math.Clamp(point.Y, 0, height);
            marker.Visibility = Visibility.Visible;
            Place(x, y);
            Send(SmartAction.SetColor, (int)Math.Round(x / FieldWidth * 360) % 360, (int)Math.Round((1 - y / height) * 100), final);
        });
        return canvas;
    }

    private FrameworkElement Swatches()
    {
        var row = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        foreach (var (hue, saturation) in Presets)
        {
            var (r, g, b) = ColorMath.HsToRgb(hue, saturation);
            var dot = new Border
            {
                Width = 26,
                Height = 26,
                CornerRadius = new CornerRadius(13),
                Margin = new Thickness(0, 0, 7, 0),
                Background = new SolidColorBrush(Color.FromRgb(r, g, b)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
            };
            dot.MouseLeftButtonUp += (_, _) => Send(SmartAction.SetColor, (int)hue, (int)saturation, final: true);
            row.Children.Add(dot);
        }
        return row;
    }

    private FrameworkElement TemperatureBar(LightColor color)
    {
        int min = Math.Min(color.MinKelvin, color.MaxKelvin), max = Math.Max(color.MinKelvin, color.MaxKelvin);
        if (max - min < 500) (min, max) = (2000, 6500);
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        for (int i = 0; i <= 8; i++)
        {
            var (r, g, b) = ColorMath.KelvinToRgb(min + (max - min) * i / 8.0);
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(r, g, b), i / 8.0));
        }
        double? position = color.Kelvin is int current ? Math.Clamp((double)(current - min) / (max - min), 0, 1) : null;
        return Bar(brush, position, (fraction, final) =>
            Send(SmartAction.SetTemperature, (int)Math.Round(min + (max - min) * fraction), null, final),
            Loc.T("Warm"), Loc.T("Koel"));
    }

    private FrameworkElement BrightnessBar(SmartDevice device)
    {
        var tint = LampColor(device.Color) ?? Colors.White;
        var brush = new LinearGradientBrush(Color.FromRgb(0x26, 0x26, 0x26), tint, 0);
        double position = device.IsOn == true ? Math.Clamp((device.Brightness ?? 100) / 100.0, 0, 1) : 0;
        return Bar(brush, position, (fraction, final) =>
            Send(SmartAction.Brightness, Math.Max(1, (int)Math.Round(fraction * 100)), null, final),
            Loc.T("Zacht"), Loc.T("Fel"));
    }

    /// <summary>Een balk waar je op klikt of over sleept, met een rondje dat de stand laat zien.</summary>
    private FrameworkElement Bar(Brush fill, double? position, Action<double, bool> picked, string left, string right)
    {
        const double height = 26;
        var canvas = new Canvas { Width = FieldWidth, Height = height, Background = Brushes.Transparent, Cursor = Cursors.Hand };
        var track = new Border { Width = FieldWidth, Height = height, CornerRadius = new CornerRadius(height / 2), Background = fill, IsHitTestVisible = false };
        canvas.Children.Add(track);
        var marker = Marker(20);
        canvas.Children.Add(marker);
        void Place(double fraction)
        {
            Canvas.SetLeft(marker, Math.Clamp(fraction * FieldWidth - 10, 0, FieldWidth - 20));
            Canvas.SetTop(marker, (height - 20) / 2);
        }
        if (position is double p) Place(p);
        else marker.Visibility = Visibility.Collapsed;
        AttachDrag(canvas, (point, final) =>
        {
            double fraction = Math.Clamp(point.X / FieldWidth, 0, 1);
            marker.Visibility = Visibility.Visible;
            Place(fraction);
            picked(fraction, final);
        });

        var labels = new Grid { Margin = new Thickness(2, 4, 2, 0) };
        var leftText = Label(left, 11.5, "TextMutedBrush", top: 0);
        var rightText = Label(right, 11.5, "TextMutedBrush", top: 0);
        rightText.HorizontalAlignment = HorizontalAlignment.Right;
        labels.Children.Add(leftText);
        labels.Children.Add(rightText);

        var stack = new StackPanel();
        stack.Children.Add(canvas);
        stack.Children.Add(labels);
        return stack;
    }

    private static Ellipse Marker(double size) => new()
    {
        Width = size,
        Height = size,
        Stroke = Brushes.White,
        StrokeThickness = 2.5,
        Fill = Brushes.Transparent,
        IsHitTestVisible = false,
        Effect = new DropShadowEffect { BlurRadius = 5, ShadowDepth = 0, Opacity = 0.7 },
    };

    private static void AttachDrag(FrameworkElement element, Action<Point, bool> pick)
    {
        element.MouseLeftButtonDown += (_, e) =>
        {
            element.CaptureMouse();
            pick(e.GetPosition(element), false);
            e.Handled = true;
        };
        element.MouseMove += (_, e) =>
        {
            if (element.IsMouseCaptured) pick(e.GetPosition(element), false);
        };
        element.MouseLeftButtonUp += (_, e) =>
        {
            if (!element.IsMouseCaptured) return;
            element.ReleaseMouseCapture();
            pick(e.GetPosition(element), true);
            e.Handled = true;
        };
    }

    private static TextBlock Label(string text, double size, string brush, double top = 12)
    {
        var block = new TextBlock { Text = text, FontSize = size, Margin = new Thickness(0, top, 0, 6), TextTrimming = TextTrimming.CharacterEllipsis };
        block.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return block;
    }
}
