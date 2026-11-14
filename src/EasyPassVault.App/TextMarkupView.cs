using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using EasyPassVault.Core;

namespace EasyPassVault.App;

/// <summary>A layer that only draws: lies under or over the text box and never takes the mouse.</summary>
public sealed class MarkupLayer : FrameworkElement
{
    internal Action<DrawingContext>? Draw;
    public MarkupLayer() { IsHitTestVisible = false; ClipToBounds = true; Focusable = false; }
    protected override void OnRender(DrawingContext dc) => Draw?.Invoke(dc);
}

/// <summary>
/// Shows "# " lines as a green band and "---" lines as a thin line with three dots (MdBand.dc.html) without touching
/// the text: the plain <see cref="TextBox"/> keeps editing, selection, undo, IME and plain-text clipboard as before.
/// A layer under the box draws its background and the bands; a layer over it covers the lines the caret is not on,
/// drawing the heading without its "# " and the separator without its "---". The caret's line (and any selected line)
/// is left to the text box, so its marks are visible and edited as ordinary characters.
/// </summary>
internal sealed class TextMarkupView
{
    private static readonly Brush FieldBrush = Freeze(Color.FromRgb(0xFA, 0xFB, 0xF8));
    private static readonly Brush BandBrush = Freeze(Color.FromRgb(0xD5, 0xE6, 0xDB));
    private static readonly Brush BandText = Freeze(Color.FromRgb(0x17, 0x40, 0x2F));
    private static readonly Brush DotBrush = Freeze(Color.FromRgb(0x6F, 0x8A, 0x7C));
    private static readonly Pen RulePen = FreezePen(Color.FromRgb(0xA9, 0xB9, 0xAF));

    private readonly TextBox box;
    private readonly MarkupLayer under, over;
    private readonly List<Mark> marks = [];
    private bool scheduled;

    private sealed record Mark(MarkupKind Kind, Rect Line, double TextLeft, bool Active, string Text);

    public TextMarkupView(TextBox box, MarkupLayer under, MarkupLayer over)
    {
        this.box = box; this.under = under; this.over = over;
        under.Draw = DrawUnder;
        over.Draw = DrawOver;
        box.TextChanged += (_, _) => Schedule();
        box.SelectionChanged += (_, _) => Schedule();
        box.GotKeyboardFocus += (_, _) => Schedule();
        box.LostKeyboardFocus += (_, _) => Schedule();
        box.SizeChanged += (_, _) => Schedule();
        box.IsVisibleChanged += (_, _) => Schedule();
        box.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, _) => Schedule()));
    }

    /// <summary>Headings and separators found by the last update, for tests: kind and whether the marks are hidden.</summary>
    internal IReadOnlyList<(MarkupKind Kind, bool Hidden)> Shown => marks.Select(mark => (mark.Kind, !mark.Active)).ToArray();

    /// <summary>Line positions are known only after layout, so the layers are redrawn after it.</summary>
    private void Schedule()
    {
        if (scheduled) return;
        scheduled = true;
        box.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, Update);
    }

    internal void Update()
    {
        scheduled = false;
        marks.Clear();
        try { Collect(); }
        catch (InvalidOperationException) { marks.Clear(); } // Layout not ready: draw plain text, the next change redraws.
        under.InvalidateVisual();
        over.InvalidateVisual();
    }

    private void Collect()
    {
        if (box.ActualWidth <= 0) return;
        string text = box.Text;
        var lines = TextMarkup.Parse(text);
        if (lines.Count == 0) return;
        if (!box.IsArrangeValid) box.UpdateLayout();
        bool focused = box.IsKeyboardFocusWithin;
        int selectionStart = box.SelectionStart, selectionEnd = selectionStart + box.SelectionLength;
        foreach (var line in lines)
        {
            Rect first = box.GetRectFromCharacterIndex(line.Start);
            if (first.IsEmpty) continue;
            Rect last = box.GetRectFromCharacterIndex(line.Start + line.Length - 1, true);
            double bottom = last.IsEmpty ? first.Bottom : Math.Max(first.Bottom, last.Bottom);
            bool active = focused && selectionStart <= line.Start + line.Length && selectionEnd >= line.Start;
            string shown = line.Kind == MarkupKind.Heading ? text.Substring(line.Start + TextMarkup.HeadingPrefix.Length,
                line.Length - TextMarkup.HeadingPrefix.Length) : "";
            marks.Add(new(line.Kind, new Rect(first.X, first.Top, 0, Math.Max(0, bottom - first.Top)), first.X, active, shown));
        }
    }

    private (double Left, double Right) Inner => (box.BorderThickness.Left + 1, box.ActualWidth - box.BorderThickness.Right - 1);

    private Rect Band(Mark mark)
    {
        var (left, right) = Inner;
        double x = Math.Max(left + 1, mark.TextLeft - 5);
        return new Rect(x, mark.Line.Top, Math.Max(0, right - 2 - x), mark.Line.Height);
    }

    private void DrawUnder(DrawingContext dc)
    {
        if (box.ActualWidth <= 0) return;
        var border = box.BorderThickness;
        dc.DrawRoundedRectangle(FieldBrush, null, new Rect(border.Left, border.Top,
            Math.Max(0, box.ActualWidth - border.Left - border.Right), Math.Max(0, box.ActualHeight - border.Top - border.Bottom)), 5, 5);
        foreach (var mark in marks.Where(mark => mark.Kind == MarkupKind.Heading))
            dc.DrawRoundedRectangle(BandBrush, null, Band(mark), 5, 5);
    }

    private void DrawOver(DrawingContext dc)
    {
        var (left, right) = Inner;
        double dpi = VisualTreeHelper.GetDpi(over).PixelsPerDip;
        foreach (var mark in marks.Where(mark => !mark.Active))
        {
            if (mark.Kind == MarkupKind.Heading)
            {
                Rect band = Band(mark);
                dc.DrawRoundedRectangle(BandBrush, null, band, 5, 5);
                var formatted = Format(mark.Text, FontWeights.SemiBold, right - mark.TextLeft - 4, dpi);
                // A wrapped heading must stay inside its own lines: fall back to the box's own weight, then cut.
                if (formatted.Height > band.Height + 0.5) formatted = Format(mark.Text, box.FontWeight, right - mark.TextLeft - 4, dpi);
                dc.PushClip(new RectangleGeometry(band));
                dc.DrawText(formatted, new Point(mark.TextLeft, mark.Line.Top));
                dc.Pop();
            }
            else
            {
                dc.DrawRectangle(FieldBrush, null, new Rect(left, mark.Line.Top, Math.Max(0, right - left), mark.Line.Height));
                double y = Math.Floor(mark.Line.Top + mark.Line.Height / 2) + 0.5;
                double start = mark.TextLeft, end = right - 4, middle = Math.Round((start + end) / 2);
                if (end - start < 60) continue;
                dc.DrawLine(RulePen, new Point(start, y), new Point(middle - 18, y));
                dc.DrawLine(RulePen, new Point(middle + 18, y), new Point(end, y));
                foreach (double dot in new[] { middle - 10, middle, middle + 10 })
                    dc.DrawEllipse(DotBrush, null, new Point(dot, y), 2, 2);
            }
        }
    }

    private FormattedText Format(string text, FontWeight weight, double width, double dpi) =>
        new(text, CultureInfo.CurrentUICulture, box.FlowDirection,
            new Typeface(box.FontFamily, box.FontStyle, weight, box.FontStretch), box.FontSize, BandText, dpi)
        { MaxTextWidth = Math.Max(1, width) };

    private static Brush Freeze(Color color) { var brush = new SolidColorBrush(color); brush.Freeze(); return brush; }
    private static Pen FreezePen(Color color) { var pen = new Pen(Freeze(color), 1); pen.Freeze(); return pen; }
}
