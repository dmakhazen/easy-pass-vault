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
    private static readonly Brush HitBrush = Freeze(Color.FromRgb(0xFF, 0xE9, 0x8A));
    private static readonly Brush CurrentHitBrush = Freeze(Color.FromRgb(0xFF, 0xB3, 0x00));
    private static readonly Pen CurrentHitPen = FreezePen(Color.FromRgb(0xB3, 0x74, 0x00));
    private const int MaxHits = 1000;

    private readonly TextBox box;
    private readonly MarkupLayer under, over;
    private readonly List<Mark> marks = [];
    private readonly List<(Rect Area, bool Current)> hits = [];
    private IReadOnlyList<int> hitStarts = [];
    private string query = "";
    private int currentHit = -1;
    private bool scheduled;

    private sealed record Mark(MarkupKind Kind, Rect Line, double TextLeft, bool Active, string Text, int TextStart);

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

    /// <summary>
    /// The search query to mark yellow in the text and the number of the match to mark stronger (-1: none).
    /// Drawing only: the text, the selection and the caret are not touched.
    /// </summary>
    internal void SetSearch(string query, int current)
    {
        if (this.query == query && currentHit == current) return;
        this.query = query; currentHit = current;
        Schedule();
    }

    /// <summary>The first line of the current match in the text box's coordinates, as of the last update.</summary>
    internal Rect? CurrentHit => hits.Where(hit => hit.Current).Select(hit => (Rect?)hit.Area).FirstOrDefault();

    /// <summary>The number of marked pieces as of the last update, for tests.</summary>
    internal int HitAreas => hits.Count;

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
        hits.Clear();
        hitStarts = [];
        try { Collect(); CollectHits(); }
        catch (InvalidOperationException) { marks.Clear(); hits.Clear(); } // Layout not ready: draw plain text, the next change redraws.
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
            marks.Add(new(line.Kind, new Rect(first.X, first.Top, 0, Math.Max(0, bottom - first.Top)), first.X, active, shown,
                line.Start + (line.Kind == MarkupKind.Heading ? TextMarkup.HeadingPrefix.Length : 0)));
        }
    }

    /// <summary>One rectangle per line of every match of the query; a match wrapped by the box gives several.</summary>
    private void CollectHits()
    {
        if (query.Length == 0 || box.ActualWidth <= 0) return;
        string text = box.Text;
        hitStarts = TextMatches.Find(text, query, MaxHits);
        if (hitStarts.Count == 0) return;
        if (!box.IsArrangeValid) box.UpdateLayout();
        for (int number = 0; number < hitStarts.Count; number++)
        {
            Rect area = Rect.Empty;
            for (int index = hitStarts[number]; index < hitStarts[number] + query.Length; index++)
            {
                if (text[index] is '\r' or '\n') continue;
                Rect left = box.GetRectFromCharacterIndex(index), right = box.GetRectFromCharacterIndex(index, true);
                if (left.IsEmpty || right.IsEmpty) continue;
                var glyph = new Rect(new Point(Math.Min(left.X, right.X), left.Top), new Point(Math.Max(left.X, right.X), left.Bottom));
                if (!area.IsEmpty && Math.Abs(area.Top - glyph.Top) < 0.5) { area.Union(glyph); continue; }
                if (!area.IsEmpty) hits.Add((area, number == currentHit));
                area = glyph;
            }
            if (!area.IsEmpty) hits.Add((area, number == currentHit));
        }
    }

    private static void DrawHit(DrawingContext dc, Rect area, bool current) =>
        dc.DrawRoundedRectangle(current ? CurrentHitBrush : HitBrush, current ? CurrentHitPen : null, area, 2, 2);

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
        foreach (var hit in hits) DrawHit(dc, hit.Area, hit.Current);
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
                // The heading is redrawn here without its "# ", so its matches are marked on the redrawn text.
                for (int number = 0; number < hitStarts.Count; number++)
                {
                    int start = hitStarts[number] - mark.TextStart;
                    if (start < 0 || start + query.Length > mark.Text.Length) continue;
                    if (formatted.BuildHighlightGeometry(new Point(mark.TextLeft, mark.Line.Top), start, query.Length) is { } shape)
                        DrawHit(dc, shape.Bounds, number == currentHit);
                }
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
