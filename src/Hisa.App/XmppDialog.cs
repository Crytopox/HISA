using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace Hisa.App;

internal sealed class XmppDialog : Window
{
    private static readonly IBrush WindowBg = Brush.Parse("#0E1520");
    private static readonly IBrush PanelBg = Brush.Parse("#101823");
    private static readonly IBrush TextPrimary = Brush.Parse("#E8EEF7");
    private static readonly IBrush TextSecondary = Brush.Parse("#9AA8BC");
    private static readonly IBrush ChromeBrush = Brush.Parse("#304057");
    private static readonly IBrush Feedback = Brush.Parse("#E4BD7E");

    private readonly StackPanel _actions = new()
    {
        Orientation = Orientation.Horizontal,
        HorizontalAlignment = HorizontalAlignment.Right,
        Spacing = 6,
        Margin = new Thickness(6, 8, 6, 4)
    };
    private bool _busy;

    public StackPanel Body { get; } = new() { Spacing = 8 };
    public TextBlock Status { get; } = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Foreground = Feedback,
        FontSize = 11,
        Margin = new Thickness(0, 4, 0, 0)
    };

    public XmppDialog(string title)
    {
        Title = title;
        Width = 465;
        MinWidth = 340;
        MaxHeight = 740;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SystemDecorations = SystemDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        FontFamily = new FontFamily("Inter");
        Foreground = TextPrimary;
        CanResize = false;

        Styles.Add(new Style(x => x.OfType<Button>().Class("compact"))
        {
            Setters =
            {
                new Setter(Button.PaddingProperty, new Thickness(8, 3)),
                new Setter(Button.FontSizeProperty, 12d)
            }
        });
        Styles.Add(new Style(x => x.OfType<Button>().Class("window"))
        {
            Setters =
            {
                new Setter(Button.PaddingProperty, new Thickness(8, 0)),
                new Setter(Button.MinWidthProperty, 28d),
                new Setter(Button.MinHeightProperty, 22d),
                new Setter(Button.BackgroundProperty, Brushes.Transparent),
                new Setter(Button.BorderThicknessProperty, new Thickness(0)),
                new Setter(Button.ForegroundProperty, Brush.Parse("#B9CCE3")),
                new Setter(Button.FontSizeProperty, 14d),
                new Setter(Button.HorizontalContentAlignmentProperty, HorizontalAlignment.Center),
                new Setter(Button.VerticalContentAlignmentProperty, VerticalAlignment.Center)
            }
        });

        var titleBar = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new Thickness(4, 2, 2, 6) };
        titleBar.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        };
        titleBar.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = TextPrimary,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0)
        });
        var close = new Button { Content = "×", FontSize = 15 };
        close.Classes.Add("window");
        close.Classes.Add("close");
        ToolTip.SetTip(close, "Close");
        close.Click += (_, _) => Close();
        Grid.SetColumn(close, 1);
        titleBar.Children.Add(close);

        Body.Children.Add(Status);
        var scroll = new ScrollViewer
        {
            Content = new Border { Padding = new Thickness(0, 0, 14, 0), Child = Body },
            MaxHeight = 560,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
        };
        var panel = new Border
        {
            Background = PanelBg,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12),
            Margin = new Thickness(2, 0),
            Child = scroll
        };

        var root = new Grid { RowDefinitions = new("Auto,*,Auto"), Margin = new Thickness(4) };
        root.Children.Add(titleBar);
        Grid.SetRow(panel, 1);
        root.Children.Add(panel);
        Grid.SetRow(_actions, 2);
        root.Children.Add(_actions);

        Content = new Border
        {
            Background = WindowBg,
            BorderBrush = ChromeBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            ClipToBounds = true,
            Child = root
        };
    }

    public void Add(Control control)
    {
        StyleControl(control);
        Body.Children.Insert(Body.Children.Count - 1, control);
    }

    public TextBox Text(string label, string? value, string? watermark = null, bool secret = false)
    {
        var field = new TextBox { Text = value, Watermark = watermark, FontSize = 12, MinHeight = 28 };
        if (secret) field.PasswordChar = '●';
        AddLabeled(label, field);
        return field;
    }

    public CheckBox Check(string label, bool selected)
    {
        var field = new CheckBox
        {
            Content = label,
            IsChecked = selected,
            Foreground = TextPrimary,
            FontSize = 12
        };
        Add(field);
        return field;
    }

    public ComboBox Choose(string label, IEnumerable<string> values, int selected)
    {
        var field = new ComboBox
        {
            ItemsSource = values,
            SelectedIndex = selected,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            FontSize = 12
        };
        AddLabeled(label, field);
        return field;
    }

    private void AddLabeled(string label, Control control)
    {
        Avalonia.Automation.AutomationProperties.SetName(control, label);
        Add(new StackPanel
        {
            Spacing = 3,
            Children =
            {
                new TextBlock { Text = label, FontSize = 11, Foreground = TextSecondary },
                control
            }
        });
    }

    private static void StyleControl(Control control)
    {
        if (control is TextBlock block)
        {
            block.Foreground ??= TextPrimary;
            if (block.FontSize <= 0 || double.IsNaN(block.FontSize)) block.FontSize = 12;
        }
    }

    public Task OpenAsync(Window owner, string action, Func<Task<bool>> submit, bool cancel = true, bool submitLast = false)
    {
        var save = new Button { Content = action, IsDefault = true };
        save.Classes.Add("compact");
        if (!submitLast) _actions.Children.Add(save);
        if (cancel)
        {
            var close = new Button { Content = "Cancel", IsCancel = true };
            close.Classes.Add("compact");
            close.Click += (_, _) => Close();
            _actions.Children.Add(close);
        }
        if (submitLast) _actions.Children.Add(save);
        save.Click += async (_, _) => await RunAsync(save, async () => { if (await submit()) Close(); });
        Topmost = IsTopmostChain(owner);
        Opened += (_, _) => Activate();
        return ShowDialog(owner);
    }

    private static bool IsTopmostChain(Window? window)
    {
        while (window is not null)
        {
            if (window.Topmost) return true;
            window = window.Owner as Window;
        }
        return false;
    }

    public Button Action(string label, Func<Task> action) => Action(_actions, label, action);

    public Button Action(Panel panel, string label, Func<Task> action)
    {
        var button = new Button { Content = label, Margin = new Thickness(0, 0, 5, 0) };
        button.Classes.Add("compact");
        button.Click += async (_, _) => await RunAsync(button, action);
        panel.Children.Add(button);
        return button;
    }

    private async Task RunAsync(Button button, Func<Task> action)
    {
        if (_busy) return;
        _busy = true;
        button.IsEnabled = false;
        Status.Text = "";
        try { await action(); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { Status.Text = ex.Message; }
        catch (Exception) { Status.Text = "The Jabber action could not be completed."; }
        finally { _busy = false; button.IsEnabled = true; }
    }

    public static async Task<bool> ConfirmAsync(Window owner, string title, string description)
    {
        var confirmed = false;
        var dialog = new XmppDialog(title);
        dialog.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Foreground = TextPrimary });
        await dialog.OpenAsync(owner, "Continue", () => Task.FromResult(confirmed = true));
        return confirmed;
    }
}
