using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using IdleDash.Core;

namespace IdleDash;

/// <summary>Kleine vensters: iets bevestigen (bv. voordeur ontgrendelen), eventueel met pincode.</summary>
public static class Dialogs
{
    public static (bool Ok, string? Code) Confirm(string title, string message, string yes, bool askCode = false)
    {
        var window = new Window { Topmost = true, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false };
        Ui.StyleWindow(window, 420, 260);
        window.SizeToContent = SizeToContent.Height;
        window.Title = title;

        var panel = new StackPanel { Margin = new Thickness(28, 22, 28, 22) };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 20, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(Ui.Hint(message, 8));

        PasswordBox? code = null;
        if (askCode)
        {
            panel.Children.Add(Ui.Label(Loc.T("Code")));
            code = new PasswordBox();
            panel.Children.Add(code);
        }

        bool ok = false;
        var yesButton = Ui.Button(yes, () => { ok = true; window.Close(); }, accent: true);
        var noButton = Ui.Button(Loc.T("Annuleren"), window.Close);
        var row = Ui.Row(yesButton, noButton);
        row.Margin = new Thickness(0, 22, 0, 0);
        panel.Children.Add(row);
        window.Content = panel;

        window.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) window.Close();
            if (e.Key == Key.Enter) { ok = true; window.Close(); }
        };
        window.Loaded += (_, _) =>
        {
            if (code != null) code.Focus(); else yesButton.Focus();
        };
        window.ShowDialog();
        return (ok, code?.Password);
    }
}
