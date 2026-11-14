using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using EasyPassVault.Core;

namespace EasyPassVault.App;

/// <summary>
/// Read-only text or JSON view: a warning line, search (Ctrl+F, Enter for the next match) and "Copy all".
/// Used in the attachment preview window and inside the editor for ".json" text files. It never edits anything.
/// </summary>
public sealed class TextPreviewView : DockPanel
{
    private static readonly Brush Muted = Hex("#56665B");
    internal TextBox Body { get; } = new()
    {
        IsReadOnly = true, IsReadOnlyCaretVisible = true, AcceptsReturn = true, AcceptsTab = true,
        TextWrapping = TextWrapping.NoWrap, FontFamily = new FontFamily("Consolas, Courier New"), FontSize = 13,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalContentAlignment = VerticalAlignment.Top, Padding = new Thickness(8, 6, 8, 6)
    };
    internal TextBlock Warning { get; } = new()
    {
        TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = Hex("#754700"), Margin = new Thickness(0, 0, 0, 8),
        Visibility = Visibility.Collapsed
    };
    internal TextBox Query { get; } = new() { MinWidth = 160, Padding = new Thickness(6, 4, 6, 4), VerticalContentAlignment = VerticalAlignment.Center };
    private readonly Button findNext = new() { Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(6, 0, 0, 0) };
    private readonly Button copyAll = new() { Padding = new Thickness(10, 4, 10, 4) };
    private readonly TextBlock found = new() { FontSize = 12, Foreground = Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
    private Func<string, string> T = text => text;

    /// <summary>Puts the whole text on the clipboard; returns the status to show (copied / clipboard busy).</summary>
    internal Func<string, string>? CopyText { get; set; }

    public TextPreviewView()
    {
        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        SetDock(copyAll, Dock.Right);
        bar.Children.Add(copyAll);
        var search = new StackPanel { Orientation = Orientation.Horizontal };
        search.Children.Add(Query); search.Children.Add(findNext); search.Children.Add(found);
        bar.Children.Add(search);
        SetDock(bar, Dock.Top); Children.Add(bar);
        SetDock(Warning, Dock.Top); Children.Add(Warning);
        Children.Add(Body);

        findNext.Click += (_, _) => FindNext();
        copyAll.Click += (_, _) => { if (CopyText is not null) found.Text = CopyText(Body.Text); };
        Query.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; FindNext(); } };
        Query.TextChanged += (_, _) => found.Text = "";
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { e.Handled = true; FocusSearch(); }
        };
    }

    internal void FocusSearch() { Query.Focus(); Query.SelectAll(); }

    /// <summary>Shows a preview; the texts follow the interface language through <paramref name="translate"/>.</summary>
    internal void Show(TextPreviewResult result, Func<string, string> translate)
    {
        T = translate;
        findNext.Content = T("Найти далее");
        copyAll.Content = T("Копировать всё");
        Query.ToolTip = T("Поиск по тексту (Ctrl+F)");
        System.Windows.Automation.AutomationProperties.SetName(Query, T("Поиск по тексту (Ctrl+F)"));
        found.Text = "";
        Body.Text = result.Text;
        Body.CaretIndex = 0;
        Body.ScrollToHome();
        Warning.Text = result.Warning switch
        {
            TextPreviewWarning.InvalidJson when result.ErrorLine is { } line =>
                T("Файл не является корректным JSON, показан исходный текст (ошибка в строке {0}).").Replace("{0}", line.ToString(CultureInfo.InvariantCulture)),
            TextPreviewWarning.InvalidJson => T("Файл не является корректным JSON, показан исходный текст."),
            TextPreviewWarning.NotUtf8 => T("Файл не в кодировке UTF-8: нечитаемые символы заменены на «�»."),
            _ => ""
        };
        Warning.Visibility = Warning.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Selects the next match after the selection, ignoring case, and wraps around to the start.</summary>
    internal bool FindNext()
    {
        string needle = Query.Text;
        if (needle.Length == 0) return false;
        string text = Body.Text;
        int start = Math.Min(text.Length, Body.SelectionStart + Body.SelectionLength);
        int index = text.IndexOf(needle, start, StringComparison.CurrentCultureIgnoreCase);
        if (index < 0) index = text.IndexOf(needle, 0, StringComparison.CurrentCultureIgnoreCase);
        if (index < 0) { found.Text = T("Не найдено"); return false; }
        found.Text = "";
        Body.Select(index, needle.Length);
        int line = Body.GetLineIndexFromCharacterIndex(index);
        if (line >= 0) Body.ScrollToLine(line);
        return true;
    }

    private static Brush Hex(string color) => (Brush)new BrushConverter().ConvertFromString(color)!;
}

/// <summary>A text or JSON attachment in its own window, from memory only.</summary>
internal sealed class TextPreviewWindow : Window
{
    public TextPreviewView View { get; } = new();

    public TextPreviewWindow(string name, TextPreviewResult result, Func<string, string> T, Func<string, string> copy)
    {
        Title = name;
        Width = 760; Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.White; FontFamily = new FontFamily("Segoe UI"); FontSize = 14;
        View.Margin = new Thickness(16);
        View.CopyText = copy;
        View.Show(result, T);
        Content = View;
    }
}
