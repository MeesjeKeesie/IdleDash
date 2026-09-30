using System.Windows;
using System.Windows.Controls;

namespace IdleDash.Core;

/// <summary>
/// Vertaalt vaste teksten uit XAML (TextBlock, knoppen, tooltips, venstertitels) zonder dat de XAML
/// hoeft te veranderen. Teksten die de code intussen zelf heeft aangepast, worden met rust gelaten.
/// </summary>
public static partial class Loc
{
    private static readonly DependencyProperty SourceProperty =
        DependencyProperty.RegisterAttached("LocSource", typeof(string), typeof(Loc));
    private static readonly DependencyProperty AppliedProperty =
        DependencyProperty.RegisterAttached("LocApplied", typeof(string), typeof(Loc));
    private static readonly DependencyProperty TipSourceProperty =
        DependencyProperty.RegisterAttached("LocTipSource", typeof(string), typeof(Loc));
    private static readonly DependencyProperty TipAppliedProperty =
        DependencyProperty.RegisterAttached("LocTipApplied", typeof(string), typeof(Loc));

    /// <param name="recordNew">
    /// true bij het aanmaken van een scherm: de huidige teksten zijn de Nederlandse bron.
    /// false bij een taalwissel: alleen eerder vastgelegde teksten opnieuw vertalen, zodat inhoud die
    /// later is toegevoegd (een afspraak die toevallig "Mist" heet) nooit per ongeluk wordt vertaald.
    /// </param>
    public static void Apply(DependencyObject root, bool recordNew = true)
    {
        foreach (var element in Descendants(root))
        {
            switch (element)
            {
                case TextBlock text when text.Inlines.Count <= 1:
                    Translate(text, text.Text, value => text.Text = value, SourceProperty, AppliedProperty, recordNew);
                    break;
                case ContentControl control when control.Content is string content:
                    Translate(control, content, value => control.Content = value, SourceProperty, AppliedProperty, recordNew);
                    break;
            }
            if (element is Window window)
                Translate(window, window.Title, value => window.Title = value, TipSourceProperty, TipAppliedProperty, recordNew);
            else if (element is FrameworkElement fe && fe.ToolTip is string tip)
                Translate(fe, tip, value => fe.ToolTip = value, TipSourceProperty, TipAppliedProperty, recordNew);
        }
    }

    private static void Translate(DependencyObject element, string current, Action<string> set,
        DependencyProperty sourceProperty, DependencyProperty appliedProperty, bool recordNew)
    {
        var source = (string?)element.GetValue(sourceProperty);
        var applied = (string?)element.GetValue(appliedProperty);
        if (source == null)
        {
            if (!recordNew || string.IsNullOrWhiteSpace(current)) return;
            source = current;   // eerste keer: de tekst uit de XAML is de Nederlandse bron
            element.SetValue(sourceProperty, source);
        }
        else if (current != applied)
        {
            return;             // de code heeft deze tekst intussen zelf gezet: niet overschrijven
        }
        string value = T(source);
        element.SetValue(appliedProperty, value);
        if (value != current) set(value);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        foreach (object child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is not DependencyObject d) continue;
            foreach (var descendant in Descendants(d)) yield return descendant;
        }
    }
}
