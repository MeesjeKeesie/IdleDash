using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IdleDash.Core;
using Cursors = System.Windows.Input.Cursors;

namespace IdleDash;

/// <summary>Zelf kiezen welk deel van de foto op je scherm komt, zoals bij een profielfoto: schuiven en zoomen.</summary>
public sealed class CropWindow : Window
{
    private readonly double _imageW, _imageH, _frameW, _frameH, _minScale;
    private readonly Image _view = new() { Stretch = Stretch.Fill };
    private double _scale, _x, _y;
    private Point? _dragStart;
    private (double X, double Y) _dragOrigin;

    public CropRect? Result { get; private set; }

    public CropWindow(BitmapSource image, double screenWidth, double screenHeight, CropRect? current)
    {
        Ui.StyleWindow(this, 920, 700);
        Title = Loc.T("Uitsnede kiezen");
        _imageW = image.PixelWidth;
        _imageH = image.PixelHeight;

        // Het kader heeft dezelfde verhouding als het scherm van het dashboard
        double ratio = screenWidth > 0 && screenHeight > 0 ? screenWidth / screenHeight : 16.0 / 9;
        _frameW = 820;
        _frameH = _frameW / ratio;
        if (_frameH > 460)
        {
            _frameH = 460;
            _frameW = _frameH * ratio;
        }
        _minScale = CropMath.CoverScale(_imageW, _imageH, _frameW, _frameH);
        if (current is CropRect crop) (_scale, _x, _y) = CropMath.FromCrop(crop, _imageW, _imageH, _frameW);
        else Reset();
        if (_scale < _minScale) Reset();
        _view.Source = image;
        RenderOptions.SetBitmapScalingMode(_view, BitmapScalingMode.HighQuality);

        var canvas = new Canvas { Width = _frameW, Height = _frameH, ClipToBounds = true, Background = Brushes.Black, Cursor = Cursors.SizeAll };
        canvas.Children.Add(_view);
        canvas.MouseLeftButtonDown += (_, e) =>
        {
            _dragStart = e.GetPosition(canvas);
            _dragOrigin = (_x, _y);
            canvas.CaptureMouse();
        };
        canvas.MouseMove += (_, e) =>
        {
            if (_dragStart is not Point start) return;
            var point = e.GetPosition(canvas);
            Move(_dragOrigin.X + point.X - start.X, _dragOrigin.Y + point.Y - start.Y);
        };
        canvas.MouseLeftButtonUp += (_, _) =>
        {
            _dragStart = null;
            canvas.ReleaseMouseCapture();
        };
        canvas.MouseWheel += (_, e) =>
        {
            var point = e.GetPosition(canvas);
            ZoomTo(_scale * (e.Delta > 0 ? 1.12 : 1 / 1.12), point.X, point.Y);
        };

        var frame = new Border
        {
            Child = canvas,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x99, 255, 255, 255)),
            BorderThickness = new Thickness(2),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 14, 0, 0),
        };

        var panel = new StackPanel { Margin = new Thickness(28, 20, 28, 24) };
        panel.Children.Add(Ui.Title(Loc.T("Uitsnede kiezen")));
        panel.Children.Add(Ui.Hint(Loc.T("Sleep de foto om hem te verschuiven en scroll om in of uit te zoomen. Wat in het kader staat, komt op je scherm.")));
        panel.Children.Add(frame);
        var row = Ui.Row(
            Ui.Button("−", () => ZoomTo(_scale / 1.25, _frameW / 2, _frameH / 2)),
            Ui.Button("+", () => ZoomTo(_scale * 1.25, _frameW / 2, _frameH / 2)),
            Ui.Button(Loc.T("Hele kader vullen"), () => { Reset(); Apply(); }),
            Ui.Button(Loc.T("Opslaan"), () =>
            {
                Result = CropMath.ToCrop(_x, _y, _imageW, _imageH, _scale, _frameW, _frameH);
                DialogResult = true;
            }, accent: true),
            Ui.Button(Loc.T("Annuleren"), Close));
        row.Margin = new Thickness(0, 16, 0, 0);
        panel.Children.Add(row);
        Content = new ScrollViewer
        {
            Content = panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Focusable = false,
        };
        Move(_x, _y);
    }

    private void Reset()
    {
        _scale = _minScale;
        _x = (_frameW - _imageW * _scale) / 2;
        _y = (_frameH - _imageH * _scale) / 2;
    }

    private void ZoomTo(double scale, double pointX, double pointY)
    {
        scale = Math.Clamp(scale, _minScale, _minScale * 6);
        var (x, y) = CropMath.ZoomAt(_scale, scale, _x, _y, pointX, pointY);
        _scale = scale;
        Move(x, y);
    }

    private void Move(double x, double y)
    {
        (_x, _y) = CropMath.Clamp(x, y, _imageW, _imageH, _scale, _frameW, _frameH);
        Apply();
    }

    private void Apply()
    {
        _view.Width = _imageW * _scale;
        _view.Height = _imageH * _scale;
        Canvas.SetLeft(_view, _x);
        Canvas.SetTop(_view, _y);
    }
}
