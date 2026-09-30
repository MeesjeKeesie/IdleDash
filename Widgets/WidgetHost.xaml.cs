using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using IdleDash.Core;

namespace IdleDash.Widgets;

/// <summary>Houder rond elke widget: regelt slepen, formaat aanpassen, instellingen, thema en verwijderen.</summary>
public partial class WidgetHost : UserControl
{
    private const double MinSize = 144;
    private static int s_topZ = 1;

    private readonly double _grid;
    private bool _dragging;
    private Point _dragStart;
    private double _startX, _startY;

    public WidgetConfig Config { get; }
    public WidgetBase Widget { get; }

    public event EventHandler? LayoutChanged;
    public event EventHandler? RemoveRequested;
    public event EventHandler? SettingsRequested;

    public WidgetHost(WidgetConfig config, WidgetBase widget, double gridSize, AppSettings settings)
    {
        InitializeComponent();
        Config = config;
        Widget = widget;
        _grid = gridSize;

        widget.Attach(config, settings);
        widget.SettingsRequested += () => SettingsRequested?.Invoke(this, EventArgs.Empty);

        ContentSlot.Content = widget;
        if (config.ThemeName != null) ApplyTheme(ThemeManager.Resolve(settings, config.ThemeName));
        Width = config.Width;
        Height = config.Height;
        Canvas.SetLeft(this, config.X);
        Canvas.SetTop(this, config.Y);
    }

    /// <summary>Eigen thema voor deze widget, of null voor het thema van het dashboard.</summary>
    public void ApplyTheme(ThemeSettings? theme)
    {
        Resources = theme == null ? new ResourceDictionary() : ThemeManager.CreateBrushes(theme);
        Widget.Refresh();
    }

    public void SetEditMode(bool on)
    {
        EditLayer.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (!on && _dragging)
        {
            _dragging = false;
            EditLayer.ReleaseMouseCapture();
        }
    }

    private Canvas? ParentCanvas => Parent as Canvas;

    private double Snap(double value) => Math.Round(value / _grid) * _grid;

    // ── Slepen ──

    private void EditLayer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ParentCanvas == null) return;
        _dragging = true;
        _dragStart = e.GetPosition(ParentCanvas);
        _startX = Canvas.GetLeft(this);
        _startY = Canvas.GetTop(this);
        Panel.SetZIndex(this, ++s_topZ);   // gesleepte widget komt bovenop
        EditLayer.CaptureMouse();
        e.Handled = true;
    }

    private void EditLayer_MouseMove(object sender, MouseEventArgs e)
    {
        var canvas = ParentCanvas;
        if (!_dragging || canvas == null) return;

        var position = e.GetPosition(canvas);
        double x = Snap(_startX + position.X - _dragStart.X);
        double y = Snap(_startY + position.Y - _dragStart.Y);

        Canvas.SetLeft(this, Math.Clamp(x, 0, Math.Max(0, canvas.ActualWidth - ActualWidth)));
        Canvas.SetTop(this, Math.Clamp(y, 0, Math.Max(0, canvas.ActualHeight - ActualHeight)));
    }

    private void EditLayer_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        EditLayer.ReleaseMouseCapture();

        Config.X = Canvas.GetLeft(this);
        Config.Y = Canvas.GetTop(this);
        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    // ── Formaat aanpassen ──

    private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        var canvas = ParentCanvas;
        double maxWidth = canvas != null ? canvas.ActualWidth - Canvas.GetLeft(this) : double.MaxValue;
        double maxHeight = canvas != null ? canvas.ActualHeight - Canvas.GetTop(this) : double.MaxValue;

        Width = Math.Clamp(Width + e.HorizontalChange, MinSize, Math.Max(MinSize, maxWidth));
        Height = Math.Clamp(Height + e.VerticalChange, MinSize, Math.Max(MinSize, maxHeight));
    }

    private void ResizeThumb_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        Width = Math.Max(MinSize, Snap(Width));
        Height = Math.Max(MinSize, Snap(Height));

        Config.Width = Width;
        Config.Height = Height;
        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RemoveButton_Click(object sender, RoutedEventArgs e) =>
        RemoveRequested?.Invoke(this, EventArgs.Empty);

    private void SettingsButton_Click(object sender, RoutedEventArgs e) =>
        SettingsRequested?.Invoke(this, EventArgs.Empty);
}
