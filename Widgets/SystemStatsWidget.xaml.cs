using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using IdleDash.Services;

namespace IdleDash.Widgets;

public partial class SystemStatsWidget : WidgetBase
{
    private readonly SystemMonitor _monitor = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _busy;

    public SystemStatsWidget()
    {
        InitializeComponent();
        _timer.Tick += async (_, _) => await UpdateAsync();
    }

    protected override async void OnStart()
    {
        _timer.Start();
        await UpdateAsync();
    }

    protected override void OnStop() => _timer.Stop();

    private async Task UpdateAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            // Meten op de achtergrond, zodat het scherm nooit hapert
            var s = await Task.Run(() => _monitor.Read());

            CpuText.Text = s.Cpu is double cpu ? $"{cpu:0}%" : "–";
            SetBar(CpuBar, s.Cpu);

            GpuText.Text = s.Gpu is double gpu ? $"{gpu:0}%" : "–";
            GpuTempText.Text = s.GpuTemp is double temp ? $"{temp:0}°C" : "";
            SetBar(GpuBar, s.Gpu);

            RamText.Text = s.RamTotalGb > 0 ? $"{s.RamUsedGb:0.0} / {s.RamTotalGb:0} GB" : "–";
            SetBar(RamBar, s.RamTotalGb > 0 ? s.RamUsedGb / s.RamTotalGb * 100 : null);
        }
        finally
        {
            _busy = false;
        }
    }

    private void SetBar(ProgressBar bar, double? value)
    {
        bar.Opacity = value.HasValue ? 1 : 0.35;
        double v = Math.Clamp(value ?? 0, 0, 100);

        // Boven 85% kleurt het balkje warm oranje
        bar.Foreground = (Brush)FindResource(v >= 85 ? "BarWarnBrush" : "BarBrush");
        bar.BeginAnimation(RangeBase.ValueProperty, new DoubleAnimation(v, TimeSpan.FromMilliseconds(600))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
    }
}
