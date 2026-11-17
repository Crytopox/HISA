using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Utilities;
using Hisa.Core.Models;

namespace Hisa.App;

public sealed partial class XmppLinkText : SelectableTextBlock
{
    // Transcript runs carry their own font sizes. Keep the paragraph default
    // small so it does not impose a minimum height on the padding lines.
    internal const double TranscriptBaseFontSize = 0.001;

    public static readonly StyledProperty<string?> TimeTextProperty =
        AvaloniaProperty.Register<XmppLinkText, string?>(nameof(TimeText));

    public static readonly StyledProperty<string?> SenderTextProperty =
        AvaloniaProperty.Register<XmppLinkText, string?>(nameof(SenderText));

    public static readonly StyledProperty<string?> BodyTextProperty =
        AvaloniaProperty.Register<XmppLinkText, string?>(nameof(BodyText));

    public static readonly StyledProperty<IBrush?> TimeForegroundProperty =
        AvaloniaProperty.Register<XmppLinkText, IBrush?>(nameof(TimeForeground));

    public static readonly StyledProperty<IBrush?> SenderForegroundProperty =
        AvaloniaProperty.Register<XmppLinkText, IBrush?>(nameof(SenderForeground));

    public static readonly StyledProperty<bool> SenderOpenableProperty =
        AvaloniaProperty.Register<XmppLinkText, bool>(nameof(SenderOpenable));

    public static readonly RoutedEvent<RoutedEventArgs> SenderActivatedEvent =
        RoutedEvent.Register<XmppLinkText, RoutedEventArgs>(nameof(SenderActivated), RoutingStrategies.Bubble);

    public static readonly RoutedEvent<XmppLinkActivatedEventArgs> LinkActivatedEvent =
        RoutedEvent.Register<XmppLinkText, XmppLinkActivatedEventArgs>(nameof(LinkActivated), RoutingStrategies.Bubble);

    public static readonly StyledProperty<string?> HtmlBodyProperty =
        AvaloniaProperty.Register<XmppLinkText, string?>(nameof(HtmlBody));

    public static readonly StyledProperty<string?> DefaultTipProperty =
        AvaloniaProperty.Register<XmppLinkText, string?>(nameof(DefaultTip));

    public static readonly StyledProperty<IReadOnlyList<XmppTranscriptRun>?> DocumentProperty =
        AvaloniaProperty.Register<XmppLinkText, IReadOnlyList<XmppTranscriptRun>?>(nameof(Document));
    public static readonly StyledProperty<bool> UnderlineLinksProperty =
        AvaloniaProperty.Register<XmppLinkText, bool>(nameof(UnderlineLinks), defaultValue: true);

    public static readonly RoutedEvent<RoutedEventArgs> ActionActivatedEvent =
        RoutedEvent.Register<XmppLinkText, RoutedEventArgs>(nameof(ActionActivated), RoutingStrategies.Bubble);

    private static readonly IBrush LinkBrush = Brush.Parse("#E0C06A");

    private static readonly Cursor LinkCursor = new(StandardCursorType.Hand);
    private static readonly Cursor TextCursor = new(StandardCursorType.Ibeam);
    private string? _parsedText;
    private string _content = "";
    private Link[] _links = [];
    private bool _rebuilding;
    private int _senderStart = -1;
    private int _senderLength;
    private PointerPressedEventArgs? _linkPress;
    private IPointer? _selectionPointer;
    private IReadOnlyList<TextRun>? _layoutRuns;
    private SenderSpan[] _senders = [];
    private ActionSpan[] _actions = [];
    private MessageSpan[] _messages = [];
    private DateSpan[] _dates = [];

    public double LayoutFontSize { get; set; }

    public XmppLinkText()
    {
        SelectionBrush = Brush.Parse("#335E86");
        SelectionForegroundBrush = Brushes.White;
    }

    public string? TimeText
    {
        get => GetValue(TimeTextProperty);
        set => SetValue(TimeTextProperty, value);
    }

    public string? SenderText
    {
        get => GetValue(SenderTextProperty);
        set => SetValue(SenderTextProperty, value);
    }

    public string? BodyText
    {
        get => GetValue(BodyTextProperty);
        set => SetValue(BodyTextProperty, value);
    }

    public string? HtmlBody
    {
        get => GetValue(HtmlBodyProperty);
        set => SetValue(HtmlBodyProperty, value);
    }

    public string? DefaultTip
    {
        get => GetValue(DefaultTipProperty);
        set => SetValue(DefaultTipProperty, value);
    }

    public IReadOnlyList<XmppTranscriptRun>? Document
    {
        get => GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    public bool UnderlineLinks
    {
        get => GetValue(UnderlineLinksProperty);
        set => SetValue(UnderlineLinksProperty, value);
    }

    public string? ActivatedSenderJid { get; private set; }
    public string? ActivatedAction { get; private set; }

    public event EventHandler<RoutedEventArgs> ActionActivated
    {
        add => AddHandler(ActionActivatedEvent, value);
        remove => RemoveHandler(ActionActivatedEvent, value);
    }

    public IBrush? TimeForeground
    {
        get => GetValue(TimeForegroundProperty);
        set => SetValue(TimeForegroundProperty, value);
    }

    public IBrush? SenderForeground
    {
        get => GetValue(SenderForegroundProperty);
        set => SetValue(SenderForegroundProperty, value);
    }

    public bool SenderOpenable
    {
        get => GetValue(SenderOpenableProperty);
        set => SetValue(SenderOpenableProperty, value);
    }

    public event EventHandler<RoutedEventArgs> SenderActivated
    {
        add => AddHandler(SenderActivatedEvent, value);
        remove => RemoveHandler(SenderActivatedEvent, value);
    }

    public event EventHandler<XmppLinkActivatedEventArgs> LinkActivated
    {
        add => AddHandler(LinkActivatedEvent, value);
        remove => RemoveHandler(LinkActivatedEvent, value);
    }

    public bool IsOverSender(Point point) => SenderAt(point);
    internal bool IsSelecting => _selectionPointer?.Captured == this || SelectionStart != SelectionEnd;

    internal static void ClearSelectionOnOutsideClick(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not TopLevel window ||
            window.FocusManager?.GetFocusedElement() is not XmppLinkText text ||
            text.SelectionStart == text.SelectionEnd ||
            e.KeyModifiers.HasFlag(KeyModifiers.Shift) ||
            e.GetCurrentPoint(window).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed)
            return;

        var start = Math.Min(text.SelectionStart, text.SelectionEnd);
        var length = Math.Abs(text.SelectionEnd - text.SelectionStart);
        if (e.Source != text || !text.Hits(start, length, e.GetPosition(text)))
            text.ClearSelection();
    }

    internal void EndSelection()
    {
        if (_selectionPointer?.Captured == this) _selectionPointer.Capture(null);
        SetCurrentValue(SelectionStartProperty, 0);
        SetCurrentValue(SelectionEndProperty, 0);
    }

    static XmppLinkText()
    {
        AffectsRender<XmppLinkText>(TimeTextProperty, SenderTextProperty, BodyTextProperty, HtmlBodyProperty,
            TimeForegroundProperty, SenderForegroundProperty, SenderOpenableProperty, DocumentProperty);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (_rebuilding) return;
        if (change.Property == DocumentProperty || change.Property == UnderlineLinksProperty ||
            (Document is not null && (change.Property == FontSizeProperty || change.Property == FontFamilyProperty ||
                change.Property == ForegroundProperty || change.Property == FontWeightProperty)))
        {
            if (Document is null) RebuildInlines();
            else RebuildDocument();
            return;
        }
        if (Document is not null) return;
        if (change.Property == TimeTextProperty || change.Property == SenderTextProperty ||
            change.Property == BodyTextProperty || change.Property == HtmlBodyProperty ||
            change.Property == TimeForegroundProperty ||
            change.Property == SenderForegroundProperty || change.Property == FontWeightProperty ||
            change.Property == ForegroundProperty || change.Property == FontSizeProperty)
            RebuildInlines();
    }

    private void RebuildInlines()
    {
        _layoutRuns = null;
        _rebuilding = true;
        try
        {
            var time = TimeText;
            var sender = SenderText;
            var body = BodyText ?? "";
            var full = new StringBuilder();
            Inlines?.Clear();
            _senderStart = -1;
            _senderLength = 0;
            _senders = [];
            _actions = [];
            _messages = [];
            _dates = [];

            if (!string.IsNullOrEmpty(time))
            {
                var stamp = $"[{time}] ";
                Inlines.Add(new Run(stamp)
                {
                    Foreground = TimeForeground ?? Foreground
                });
                full.Append(stamp);
            }

            if (!string.IsNullOrEmpty(sender))
            {
                var label = $"{sender}: ";
                _senderStart = full.Length;
                _senderLength = label.Length;
                Inlines.Add(new Run(label)
                {
                    Foreground = SenderForeground ?? Foreground,
                    FontWeight = FontWeight.SemiBold
                });
                full.Append(label);
            }

            var links = new List<Link>();
            var formatted = XmppHtmlFormatting.Parse(HtmlBody);
            if (formatted is { Count: > 0 })
                AppendFormatted(formatted, links, full);
            else
                AppendPlain(body, links, full);
            _content = full.ToString();
            _parsedText = _content;
            _links = links.ToArray();
        }
        finally
        {
            _rebuilding = false;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        UpdatePointer(e.GetPosition(this));
        base.OnPointerMoved(e);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        Cursor = TextCursor;
        ToolTip.SetTip(this, null);
        base.OnPointerExited(e);
    }

    private void OnPointerPressedCore(PointerPressedEventArgs e)
    {
        if (TryActivateLink(e)) return;
        var properties = e.GetCurrentPoint(this).Properties;
        var left = properties.IsLeftButtonPressed || properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed;
        var point = e.GetPosition(this);
        if (e.ClickCount == 2 && left && SenderJidAt(point) is { } jid)
        {
            e.Handled = true;
            ActivatedSenderJid = jid == "\0" ? null : jid;
            RaiseEvent(new RoutedEventArgs(SenderActivatedEvent, this));
            return;
        }

        Focus();
        base.OnPointerPressed(e);
        if (left && e.Pointer.Captured == this) _selectionPointer = e.Pointer;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e) => OnPointerPressedCore(e);

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        if (ReferenceEquals(_selectionPointer, e.Pointer)) _selectionPointer = null;
        base.OnPointerCaptureLost(e);
    }

    private bool TryActivateLink(PointerPressedEventArgs e)
    {
        if (ReferenceEquals(_linkPress, e)) return true;
        var properties = e.GetCurrentPoint(this).Properties;
        var left = properties.IsLeftButtonPressed || properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed;
        var point = e.GetPosition(this);
        if (!left) return false;
        if (ActionAt(point) is { } action)
        {
            _linkPress = e;
            ActivatedAction = action;
            e.Handled = true;
            RaiseEvent(new RoutedEventArgs(ActionActivatedEvent, this));
            return true;
        }
        if (LinkAt(point) is not { } opened) return false;
        _linkPress = e;
        e.Handled = true;
        RaiseEvent(new XmppLinkActivatedEventArgs(opened, this));
        return true;
    }

    private void UpdatePointer(Point point)
    {
        if (LinkAt(point) is { } link)
        {
            Cursor = LinkCursor;
            ToolTip.SetTip(this, link.AbsoluteUri);
            return;
        }

        if (ActionAt(point) is not null)
        {
            Cursor = LinkCursor;
            ToolTip.SetTip(this, null);
            return;
        }

        if (SenderJidAt(point) is { Length: > 0 })
        {
            Cursor = LinkCursor;
            ToolTip.SetTip(this, "Double-click to open chat");
            return;
        }

        Cursor = TextCursor;
        ToolTip.SetTip(this, DefaultTip);
    }

    private void AppendPlain(string body, List<Link> links, StringBuilder full)
    {
        foreach (var span in XmppLinkSpans.Split(body))
            AppendRun(span.Text, false, false, span.Url is not null, FontSize, span.Url, links, full);
    }

    private void AppendFormatted(IReadOnlyList<XmppFormattedRun> runs, List<Link> links, StringBuilder full)
    {
        foreach (var run in runs)
        {
            if (run.Link is not null && Uri.TryCreate(run.Link, UriKind.Absolute, out var parsed))
            {
                AppendRun(run.Text, run.Bold, run.Italic, true, run.FontSize ?? FontSize, parsed, links, full);
                continue;
            }

            foreach (var span in XmppLinkSpans.Split(run.Text))
                AppendRun(span.Text, run.Bold, run.Italic, run.Underline || span.Url is not null,
                    run.FontSize ?? FontSize, span.Url, links, full);
        }
    }

    private void AppendRun(string text, bool bold, bool italic, bool underline, double size, Uri? url,
        List<Link> links, StringBuilder full, IBrush? foreground = null)
    {
        if (text.Length == 0) return;
        var run = new Run(text)
        {
            Foreground = url is null ? foreground ?? Foreground : LinkBrush,
            FontWeight = bold ? FontWeight.Bold : FontWeight,
            FontStyle = italic ? FontStyle.Italic : FontStyle.Normal,
            FontSize = size
        };
        if (underline && (url is null || UnderlineLinks))
            run.TextDecorations = new TextDecorationCollection { new TextDecoration { Location = TextDecorationLocation.Underline } };
        if (url is not null)
            links.Add(new(full.Length, text.Length, url));
        Inlines.Add(run);
        full.Append(text);
    }

    private Point TextPoint(Point point) => new(point.X - Padding.Left, point.Y - Padding.Top);

    private static readonly IBrush DividerBrush = Brush.Parse("#29384B");
    private static readonly IBrush UnreadDividerBrush = Brush.Parse("#D96464");
    private static readonly IBrush DateRuleBrush = Brush.Parse("#8A8478");
    private static readonly IBrush DateTextBrush = Brush.Parse("#D6D0C4");
    private static readonly IBrush DateGapBrush = Brush.Parse("#101823");
    private static readonly Pen DateRulePen = new(DateRuleBrush, 1);

    protected override TextLayout CreateTextLayout(string? text)
    {
        // The default selection override uses the block's font size for every selected run.
        // Transcript runs carry their own sizes, so only override each run's foreground.
        var runs = _textRuns ?? _layoutRuns;
        if (runs is null || runs.Count == 0) return base.CreateTextLayout(text);
        _layoutRuns = runs;
        if (_textRuns is not null && SelectionStart == SelectionEnd) return base.CreateTextLayout(text);

        var typeface = new Typeface(FontFamily, FontStyle, FontWeight, FontStretch);
        var defaults = new GenericTextRunProperties(typeface, FontFeatures, FontSize, TextDecorations, Foreground);
        var paragraph = new GenericTextParagraphProperties(FlowDirection, TextAlignment, true, false,
            defaults, TextWrapping, LineHeight, 0, LetterSpacing);
        return new TextLayout(new InlinesTextSource(runs, SelectedForegrounds(runs)), paragraph, TextTrimming,
            double.IsNaN(_constraint.Width) ? 0 : _constraint.Width,
            double.IsNaN(_constraint.Height) ? 0 : _constraint.Height, MaxLines);
    }

    private IReadOnlyList<ValueSpan<TextRunProperties>>? SelectedForegrounds(IReadOnlyList<TextRun> runs)
    {
        var start = Math.Min(SelectionStart, SelectionEnd);
        var end = Math.Max(SelectionStart, SelectionEnd);
        if (start == end || SelectionForegroundBrush is not { } brush) return null;

        var spans = new List<ValueSpan<TextRunProperties>>();
        var offset = 0;
        foreach (var run in runs)
        {
            var first = Math.Max(start, offset);
            offset += run.Length;
            var last = Math.Min(end, offset);
            if (last > first && run is TextCharacters characters)
            {
                var style = characters.Properties;
                // Padding and date-label placeholders must stay invisible when selected.
                if (style.ForegroundBrush is not ISolidColorBrush { Color.A: 0 })
                    spans.Add(new(first, last - first, new GenericTextRunProperties(style.Typeface, style.FontFeatures,
                        style.FontRenderingEmSize, style.TextDecorations, brush, style.BackgroundBrush,
                        style.BaselineAlignment, style.CultureInfo)));
            }
            if (offset >= end) break;
        }
        return spans;
    }

    protected override void RenderTextLayout(DrawingContext context, Point origin)
    {
        var width = Math.Max(0, Bounds.Width);
        foreach (var (message, top, bottom) in MessageBands())
        {
            if (message.Fill is null && message.Bar is null && !message.Divider && !message.UnreadDivider) continue;
            var band = new Rect(0, origin.Y + top, width, bottom - top);
            if (message.Fill is not null)
                context.FillRectangle(message.Fill, band);
            if (message.Bar is not null)
                context.FillRectangle(message.Bar, new Rect(0, band.Y, 3, band.Height));
            if (message.UnreadDivider)
                context.FillRectangle(UnreadDividerBrush, new Rect(0, band.Y, band.Width, 2));
            else if (message.Divider)
                context.FillRectangle(DividerBrush, new Rect(0, band.Y, band.Width, 1));
        }
        base.RenderTextLayout(context, origin);
        foreach (var date in _dates)
            DrawDateRule(context, origin, date);
    }

    private IEnumerable<(MessageSpan Message, double Top, double Bottom)> MessageBands()
    {
        var lines = TextLayout.TextLines;
        var index = 0;
        var y = 0d;
        foreach (var message in _messages)
        {
            while (index < lines.Count && lines[index].FirstTextSourceIndex < message.Start)
                y += lines[index++].Height;
            var top = y;
            var end = message.Start + message.Length;
            while (index < lines.Count && lines[index].FirstTextSourceIndex < end)
                y += lines[index++].Height;
            if (y > top)
                yield return (message, top, y);
        }
    }

    private void DrawDateRule(DrawingContext context, Point origin, DateSpan date)
    {
        var rects = TextLayout.HitTestTextRange(date.Start, Math.Max(1, date.Length)).ToArray();
        if (rects.Length == 0 || string.IsNullOrEmpty(date.Label)) return;
        var top = origin.Y + rects.Min(rect => rect.Y);
        var bottom = origin.Y + rects.Max(rect => rect.Bottom);
        var mid = (top + bottom) / 2;
        var width = Math.Max(0, Bounds.Width);
        context.DrawLine(DateRulePen, new Point(0, mid), new Point(width, mid));
        var formatted = new FormattedText(date.Label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily, FontStyle, FontWeight.Normal),
            LayoutFontSize > 1 ? LayoutFontSize : FontSize, DateTextBrush);
        var textWidth = formatted.Width;
        var x = Math.Max(0, (width - textWidth) / 2);
        context.FillRectangle(DateGapBrush, new Rect(x - 8, top, textWidth + 16, Math.Max(1, bottom - top)));
        context.DrawText(formatted, new Point(x, top + Math.Max(0, (bottom - top - formatted.Height) / 2)));
    }

    public IReadOnlyList<(long Id, double Top, double Bottom)> MessageBounds() =>
        MessageBands().Select(band => (band.Message.Id, band.Top + Padding.Top, band.Bottom + Padding.Top)).ToArray();

    private void RebuildDocument()
    {
        var runs = Document;
        if (runs is null) return;
        _layoutRuns = null;
        _rebuilding = true;
        try
        {
            Inlines?.Clear();
            var links = new List<Link>();
            var senders = new List<SenderSpan>();
            var actions = new List<ActionSpan>();
            var messages = new List<MessageSpan>();
            var dates = new List<DateSpan>();
            var full = new StringBuilder();
            foreach (var run in runs)
            {
                if (string.IsNullOrEmpty(run.Text)) continue;
                var start = full.Length;
                var size = run.FontSize ?? (LayoutFontSize > 1 ? LayoutFontSize : FontSize);
                AppendRun(run.Text, run.Bold, run.Italic, run.Underline || run.Link is not null, size,
                    run.Link, links, full, run.Foreground);
                var length = full.Length - start;
                if (length <= 0) continue;
                if (!string.IsNullOrEmpty(run.SenderJid))
                    senders.Add(new(start, length, run.SenderJid));
                if (!string.IsNullOrEmpty(run.Action))
                    actions.Add(new(start, length, run.Action));
                if (run.DateRule && run.DateLabel is { Length: > 0 } label)
                    dates.Add(new(start, label.Length, label));
                if (run.MessageId != 0)
                    messages.Add(new(start, length, run.MessageId, run.Highlight, run.Bar, run.Divider, run.UnreadDivider));
            }
            _content = full.ToString();
            _parsedText = _content;
            _links = links.ToArray();
            _senders = senders.ToArray();
            _actions = actions.ToArray();
            _messages = MergeMessages(messages);
            _dates = dates.ToArray();
            _senderStart = -1;
            _senderLength = 0;
            InvalidateVisual();
        }
        finally
        {
            _rebuilding = false;
        }
    }

    private static MessageSpan[] MergeMessages(List<MessageSpan> spans)
    {
        if (spans.Count == 0) return [];
        var merged = new List<MessageSpan>();
        var current = spans[0];
        for (var index = 1; index < spans.Count; index++)
        {
            var next = spans[index];
            if (next.Id == current.Id && next.Start <= current.Start + current.Length)
                current = current with
                {
                    Length = next.Start + next.Length - current.Start,
                    Fill = current.Fill ?? next.Fill,
                    Bar = current.Bar ?? next.Bar,
                    Divider = current.Divider || next.Divider,
                    UnreadDivider = current.UnreadDivider || next.UnreadDivider
                };
            else
            {
                merged.Add(current);
                current = next;
            }
        }
        merged.Add(current);
        return merged.ToArray();
    }

    private bool Hits(int start, int length, Point point)
    {
        var textPoint = TextPoint(point);
        foreach (var rect in TextLayout.HitTestTextRange(start, length))
        {
            if (textPoint.X >= rect.X - 2 && textPoint.X <= rect.Right + 2 &&
                textPoint.Y >= rect.Y - 1 && textPoint.Y <= rect.Bottom + 1)
                return true;
        }
        return false;
    }

    private string? ActionAt(Point point)
    {
        foreach (var action in _actions)
            if (Hits(action.Start, action.Length, point)) return action.Action;
        return null;
    }

    private string? SenderJidAt(Point point)
    {
        foreach (var sender in _senders)
            if (Hits(sender.Start, sender.Length, point)) return sender.Jid;
        return SenderOpenable && LegacySenderAt(point) ? "\0" : null;
    }

    private bool SenderAt(Point point) => SenderJidAt(point) is { Length: > 0 };

    private bool LegacySenderAt(Point point)
    {
        if (_senderStart < 0 || _senderLength <= 0 || !Bounds.WithX(0).WithY(0).Contains(point)) return false;
        var textPoint = TextPoint(point);
        var hit = TextLayout.HitTestPoint(textPoint);
        if (hit.IsInside)
            return hit.TextPosition >= _senderStart && hit.TextPosition < _senderStart + _senderLength;
        // Fallback when hit-test misses (selection/layout races): treat left prefix band as sender.
        if (_content.Length == 0) return false;
        var prefix = Math.Clamp((_senderStart + _senderLength) / (double)_content.Length, 0, 1);
        return textPoint.X <= Bounds.Width * Math.Max(prefix, 0.15) && textPoint.Y <= FontSize * 1.6;
    }

    private Uri? LinkAt(Point point)
    {
        var links = Links();
        if (links.Length == 0) return null;
        var textPoint = TextPoint(point);
        foreach (var link in links)
        {
            foreach (var rect in TextLayout.HitTestTextRange(link.Start, link.Length))
            {
                if (textPoint.X >= rect.X - 2 && textPoint.X <= rect.Right + 2 &&
                    textPoint.Y >= rect.Y - 1 && textPoint.Y <= rect.Bottom + 1)
                    return link.Url;
            }
        }
        return null;
    }

    private Link[] Links()
    {
        var text = _content.Length > 0 ? _content : Text ?? "";
        if (text == _parsedText) return _links;
        _parsedText = text;
        var links = new List<Link>();
        var cursor = 0;
        foreach (var span in XmppLinkSpans.Split(text))
        {
            if (span.Url is not null)
                links.Add(new(cursor, span.Text.Length, span.Url));
            cursor += span.Text.Length;
        }
        return _links = links.ToArray();
    }

    private sealed record Link(int Start, int Length, Uri Url);
    private sealed record SenderSpan(int Start, int Length, string Jid);
    private sealed record ActionSpan(int Start, int Length, string Action);
    private sealed record MessageSpan(int Start, int Length, long Id, IBrush? Fill, IBrush? Bar,
        bool Divider, bool UnreadDivider);
    private sealed record DateSpan(int Start, int Length, string Label);
}

public sealed record XmppTranscriptRun
{
    public required string Text { get; init; }
    public IBrush? Foreground { get; init; }
    public bool Bold { get; init; }
    public bool Italic { get; init; }
    public bool Underline { get; init; }
    public double? FontSize { get; init; }
    public Uri? Link { get; init; }
    public string? SenderJid { get; init; }
    public string? Action { get; init; }
    public IBrush? Highlight { get; init; }
    public IBrush? Bar { get; init; }
    public bool Divider { get; init; }
    public bool UnreadDivider { get; init; }
    public bool DateRule { get; init; }
    public string? DateLabel { get; init; }
    public long MessageId { get; init; }
}

public sealed class XmppLinkActivatedEventArgs : RoutedEventArgs
{
    public Uri Url { get; }

    public XmppLinkActivatedEventArgs(Uri url, object? source)
        : base(XmppLinkText.LinkActivatedEvent, source) => Url = url;
}
