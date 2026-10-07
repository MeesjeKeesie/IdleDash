using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using IdleDash.Core;
using IdleDash.Services;

namespace IdleDash.Widgets;

/// <summary>Je open taken uit Google Taken: afvinken en nieuwe taken toevoegen kan direct vanaf het dashboard.</summary>
public partial class TasksWidget : WidgetBase
{

    private readonly AppSettings _settings;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(3) };
    private string _listId = "";
    private string? _loadedListSetting;
    private List<TaskItem>? _items;

    /// <summary>Takenlijst van deze widget (uit 1.0/1.1: de lijst uit de algemene instellingen).</summary>
    private string? ListSetting => Config.Get<string?>("list", _settings.GoogleTaskListId);
    private bool _loading;
    private bool _hasData;

    public TasksWidget(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        _timer.Tick += async (_, _) => await RefreshAsync();
    }

    protected override async void OnStart()
    {
        GoogleService.StateChanged += OnGoogleStateChanged;
        _timer.Start();
        await RefreshAsync();
    }

    protected override void OnStop()
    {
        GoogleService.StateChanged -= OnGoogleStateChanged;
        _timer.Stop();
    }

    public override async void OnSettingsChanged()
    {
        // Andere takenlijst gekozen in de instellingen
        if (IsRunning && ListSetting != _loadedListSetting) await RefreshAsync();
        else Refresh();
    }

    private async void OnGoogleStateChanged() => await RefreshAsync();

    private async Task RefreshAsync()
    {
        if (_loading) return;
        if (ShowProblemIfAny()) return;

        _loading = true;
        try
        {
            _loadedListSetting = ListSetting;
            var (listId, items) = await GoogleService.GetTasksAsync(ListSetting);
            _listId = listId;
            _hasData = true;
            _items = items;
            Render(items);
        }
        catch (Exception ex)
        {
            GoogleService.HandleError(ex);
            if (!ShowProblemIfAny() && !_hasData)
                ShowMessage(Loc.T("Je taken ophalen lukt nu niet. Controleer je internetverbinding."), showButton: false);
        }
        finally
        {
            _loading = false;
        }
    }

    private bool ShowProblemIfAny()
    {
        string? problem = GoogleService.DescribeProblem(Loc.T("je taken"));
        if (problem == null) return false;
        _hasData = false;
        ShowMessage(problem, showButton: GoogleService.State != GoogleState.Connecting);
        return true;
    }

    private void ShowMessage(string text, bool showButton)
    {
        TaskList.Children.Clear();
        NewTaskRow.Visibility = Visibility.Collapsed;
        MessageText.Text = text;
        MessageButton.Visibility = showButton ? Visibility.Visible : Visibility.Collapsed;
        MessagePanel.Visibility = Visibility.Visible;
    }

    private void Render(List<TaskItem> items)
    {
        if (Config.Get("order", "due") == "due") items = TaskOrder.ByDue(items, t => t.Due, t => t.IsSubtask);
        MessagePanel.Visibility = Visibility.Collapsed;
        NewTaskRow.Visibility = Visibility.Visible;
        TaskList.Children.Clear();

        if (items.Count == 0)
        {
            TaskList.Children.Add(new TextBlock
            {
                Text = Loc.T("Geen open taken."),
                FontSize = 16,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                Margin = new Thickness(0, 4, 0, 0),
            });
            return;
        }

        foreach (var item in items)
            TaskList.Children.Add(CreateRow(item));
    }

    private Grid CreateRow(TaskItem item)
    {
        var row = new Grid { Margin = new Thickness(item.IsSubtask ? 30 : 0, 0, 0, 10) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var check = new Button
        {
            Style = (Style)FindResource("CheckCircleButton"),
            ToolTip = Loc.T("Afvinken"),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        check.Click += async (_, _) => await CompleteAsync(item, row);

        var title = new TextBlock
        {
            Text = item.Title,
            FontSize = 16,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
        };
        Grid.SetColumn(title, 1);

        row.Children.Add(check);
        row.Children.Add(title);

        if (item.Due is DateTime due)
        {
            var dueText = new TextBlock
            {
                Text = DescribeDue(due),
                FontSize = 13,
                Margin = new Thickness(12, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)FindResource(due < DateTime.Today ? "BarWarnBrush" : "TextMutedBrush"),
            };
            Grid.SetColumn(dueText, 2);
            row.Children.Add(dueText);
        }
        return row;
    }

    private static string DescribeDue(DateTime due)
    {
        var today = DateTime.Today;
        if (due == today) return Loc.T("vandaag");
        if (due == today.AddDays(1)) return Loc.T("morgen");
        if (due == today.AddDays(-1)) return Loc.T("gisteren");
        if (due > today && due < today.AddDays(7)) return due.ToString("dddd", Loc.Culture);
        return due.ToString("d MMM", Loc.Culture).TrimEnd('.');
    }

    private async Task CompleteAsync(TaskItem item, Grid row)
    {
        if (string.IsNullOrEmpty(_listId)) return;

        row.IsEnabled = false;
        row.BeginAnimation(OpacityProperty, new DoubleAnimation(0.25, TimeSpan.FromMilliseconds(250)));
        try
        {
            await GoogleService.CompleteTaskAsync(_listId, item.Id);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            GoogleService.HandleError(ex);
            row.BeginAnimation(OpacityProperty, null);
            row.IsEnabled = true;
        }
    }

    private async void NewTaskBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            NewTaskBox.Text = "";
            Keyboard.ClearFocus();
            return;
        }
        if (e.Key != Key.Enter) return;

        string title = NewTaskBox.Text.Trim();
        if (title.Length == 0 || string.IsNullOrEmpty(_listId)) return;

        NewTaskBox.IsEnabled = false;
        try
        {
            await GoogleService.AddTaskAsync(_listId, title);
            NewTaskBox.Text = "";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            GoogleService.HandleError(ex);
        }
        finally
        {
            NewTaskBox.IsEnabled = true;
            if (NewTaskRow.Visibility == Visibility.Visible) NewTaskBox.Focus();
        }
    }

    private void NewTaskBox_TextChanged(object sender, TextChangedEventArgs e) =>
        NewTaskPlaceholder.Visibility = NewTaskBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void MessageButton_Click(object sender, RoutedEventArgs e) => App.Instance.ShowSettings();

    public override void Refresh()
    {
        if (_items != null && GoogleService.IsConnected) Render(_items);
    }

    public override FrameworkElement? CreateSettings(Action saved)
    {
        var panel = new StackPanel();
        panel.Children.Add(Ui.Section(Loc.T("Taken")));
        panel.Children.Add(Ui.Label(Loc.T("Takenlijst")));
        var holder = new StackPanel();
        holder.Children.Add(Ui.Hint(GoogleService.IsConnected ? Loc.T("Lijsten ophalen…") : Loc.T("Koppel eerst Google in de instellingen van IdleDash.")));
        panel.Children.Add(holder);
        panel.Children.Add(Ui.Label(Loc.T("Volgorde")));
        panel.Children.Add(Ui.Combo(new[]
        {
            (Loc.T("Wat het eerst af moet bovenaan"), "due"), (Loc.T("Zoals in Google Taken"), "google"),
        }, Config.Get("order", "due"), v => { Config.Set("order", v); saved(); }));
        panel.Loaded += async (_, _) =>
        {
            if (!GoogleService.IsConnected) return;
            try
            {
                var lists = await GoogleService.GetTaskListsAsync();
                holder.Children.Clear();
                holder.Children.Add(Ui.Combo(lists.Select(l => (l.Title, (string?)l.Id)), ListSetting ?? lists.FirstOrDefault()?.Id,
                    id => { Config.Set("list", id); saved(); }));
            }
            catch (Exception ex)
            {
                GoogleService.HandleError(ex);
            }
        };
        return panel;
    }
}
