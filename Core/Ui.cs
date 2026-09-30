using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace IdleDash.Core;

/// <summary>
/// Bouwstenen voor instellingenschermen die in code gemaakt worden (instellingen per widget,
/// thema's, smarthome). Gebruikt de stijlen uit Styles/Controls.xaml.
/// </summary>
public static class Ui
{
    public static readonly Brush WindowBackground = Frozen(Color.FromRgb(0x10, 0x15, 0x1F));
    public static readonly Brush WindowForeground = Frozen(Color.FromRgb(0xEE, 0xF1, 0xF6));

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    /// <summary>Een venster in de donkere stijl van de instellingen.</summary>
    public static void StyleWindow(Window window, double width, double height)
    {
        window.Width = width;
        window.Height = height;
        window.MinWidth = Math.Min(width, 420);
        window.MinHeight = Math.Min(height, 300);
        window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        window.Background = WindowBackground;
        window.Foreground = WindowForeground;
        window.FontFamily = new FontFamily("Segoe UI");
        window.FontSize = 14;
        window.UseLayoutRounding = true;
        window.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Styles/Controls.xaml") });
        window.SourceInitialized += (_, _) => DarkTitleBar(window);
        try
        {
            window.Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/IdleDash.ico"));
        }
        catch
        {
            // zonder icoon werkt het ook
        }
    }

    public static void DarkTitleBar(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        int dark = 1;
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
    }

    public static T Styled<T>(T element, string style) where T : FrameworkElement
    {
        element.SetResourceReference(FrameworkElement.StyleProperty, style);
        return element;
    }

    public static TextBlock Title(string text) =>
        new() { Text = text, FontSize = 26, FontWeight = FontWeights.Light, TextWrapping = TextWrapping.Wrap };

    public static TextBlock Section(string text) => Styled(new TextBlock { Text = text }, "SectionTitle");

    public static TextBlock Label(string text) => Styled(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, "FieldLabel");

    public static TextBlock Hint(string text, double top = 6) =>
        Styled(new TextBlock { Text = text, Margin = new Thickness(0, top, 0, 0) }, "Hint");

    public static ComboBox Combo<T>(IEnumerable<(string Text, T Value)> items, T selected, Action<T> changed)
    {
        var box = new ComboBox();
        foreach (var (text, value) in items)
        {
            var item = new ComboBoxItem { Content = text, Tag = value };
            box.Items.Add(item);
            if (Equals(value, selected)) box.SelectedItem = item;
        }
        if (box.SelectedIndex < 0 && box.Items.Count > 0) box.SelectedIndex = 0;
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedItem is ComboBoxItem item) changed((T)item.Tag!);
        };
        return box;
    }

    public static CheckBox Switch(string text, string? hint, bool value, Action<bool> changed)
    {
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
        if (hint != null) content.Children.Add(Hint(hint, 2));
        var box = Styled(new CheckBox { Content = content, IsChecked = value, Margin = new Thickness(0, 14, 0, 0) }, "Switch");
        box.Checked += (_, _) => changed(true);
        box.Unchecked += (_, _) => changed(false);
        return box;
    }

    public static Button Button(string text, Action click, bool accent = false)
    {
        var button = Styled(new Button
        {
            Content = text,
            Margin = new Thickness(0, 0, 10, 10),
            HorizontalAlignment = HorizontalAlignment.Left,
        }, accent ? "AccentButton" : "SecondaryButton");
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>Tekstveld dat opslaat als je Enter drukt of het veld verlaat.</summary>
    public static TextBox Field(string value, Action<string> committed, double? width = null)
    {
        var box = new TextBox { Text = value };
        if (width != null) box.Width = width.Value;
        string last = value;
        void Commit()
        {
            if (box.Text == last) return;
            last = box.Text;
            committed(box.Text);
        }
        box.LostFocus += (_, _) => Commit();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) Commit();
        };
        return box;
    }

    public static WrapPanel Row(params UIElement[] children)
    {
        var row = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        foreach (var child in children) row.Children.Add(child);
        return row;
    }

    /// <summary>Velden naast elkaar, met labels erboven. Breedtes als "2*", "*" of een getal.</summary>
    public static Grid Columns(params (string Label, UIElement Element, string Width)[] columns)
    {
        var grid = new Grid();
        for (int i = 0; i < columns.Length; i++)
        {
            if (i > 0) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = ParseWidth(columns[i].Width) });
            var cell = new StackPanel();
            if (columns[i].Label.Length > 0) cell.Children.Add(Label(columns[i].Label));
            cell.Children.Add(columns[i].Element);
            Grid.SetColumn(cell, i * 2);
            grid.Children.Add(cell);
        }
        return grid;
    }

    private static GridLength ParseWidth(string width) =>
        width.EndsWith('*')
            ? new GridLength(width.Length == 1 ? 1 : double.Parse(width[..^1], System.Globalization.CultureInfo.InvariantCulture), GridUnitType.Star)
            : new GridLength(double.Parse(width, System.Globalization.CultureInfo.InvariantCulture));

    public static readonly string[] Palette =
    {
        "#DCE4F0", "#8FB3E6", "#2B5DB5", "#A8D5BA", "#3C9D6B", "#FFD6A5",
        "#F2A65A", "#E57373", "#C792EA", "#EEF1F6", "#9AA4B5", "#18202C", "#000000", "#FFFFFF",
    };

    /// <summary>Kleurkiezer: rondjes om uit te kiezen plus een veld voor een eigen #kleurcode.</summary>
    public static FrameworkElement ColorPicker(string current, Action<string> changed)
    {
        var panel = new WrapPanel();
        var hex = new TextBox { Text = current, Width = 104, Margin = new Thickness(0, 0, 0, 8), ToolTip = "#RRGGBB" };
        foreach (string color in Palette)
        {
            var swatch = new Border
            {
                Width = 28,
                Height = 28,
                CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush(ThemeManager.ToColor(color, Colors.Gray)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 8, 8),
                Cursor = Cursors.Hand,
                ToolTip = color,
            };
            swatch.MouseLeftButtonUp += (_, _) =>
            {
                hex.Text = color;
                changed(color);
            };
            panel.Children.Add(swatch);
        }
        void CommitHex()
        {
            string text = hex.Text.Trim();
            if (!text.StartsWith('#')) text = "#" + text;
            if (ThemeColors.Parse(text) != null) changed(text.ToUpperInvariant());
        }
        hex.LostFocus += (_, _) => CommitHex();
        hex.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) CommitHex();
        };
        panel.Children.Add(hex);
        return panel;
    }

    /// <summary>Een regel met tekst en een verwijderknop (voor lijsten zoals bronnen of mappen).</summary>
    public static Grid RemovableRow(string text, string? detail, Action remove)
    {
        var grid = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis });
        if (!string.IsNullOrEmpty(detail)) texts.Children.Add(Hint(detail, 0));
        grid.Children.Add(texts);
        var button = Button(Loc.T("Verwijderen"), remove);
        button.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(button, 1);
        grid.Children.Add(button);
        return grid;
    }
}
