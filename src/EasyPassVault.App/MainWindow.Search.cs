using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using EasyPassVault.Core;

namespace EasyPassVault.App;

// The search field above the list looks through every folder. This part keeps what goes with it: the folder path
// of the open item (each folder is a button), the "next match" button and the yellow marks in the item's text.
public partial class MainWindow
{
    private const int MaxSearchMarks = 1000;
    private static readonly Geometry FolderGeometry = Geometry.Parse("M 0,1 L 6,1 L 8,3 L 16,3 L 16,13 L 0,13 Z");
    private static readonly Brush FolderIconBrush = Frozen("#56836E");
    private static readonly Brush CrumbSeparatorBrush = Frozen("#9AA89F");

    private int searchHit;
    private string? searchHitItem;
    private string crumbsKey = "";

    private string SearchQuery => Search.Text.Trim();
    private bool Searching => SearchQuery.Length > 0;

    /// <summary>Folders from the top level down to <paramref name="id"/>; the vault root only for items lying in it.</summary>
    private List<FolderInfo> FolderPath(string id)
    {
        var path = new List<FolderInfo>();
        while (folderNodes.TryGetValue(id, out var node) && node.Tag is FolderInfo folder)
        {
            path.Insert(0, folder);
            if (folder.ParentId is null) break;
            id = folder.ParentId;
        }
        if (path.Count > 1) path.RemoveAt(0);
        return path;
    }

    private string FolderPathText(string id) => string.Join(" › ", FolderPath(id).Select(folder => folder.DisplayName));

    /// <summary>The path above the title of the open item. Rebuilt only when the folder, its names or the language change.</summary>
    private void UpdateCrumbs()
    {
        var path = draft is null ? [] : FolderPath((ItemFolder.SelectedItem as FolderInfo)?.Id ?? draft.FolderId);
        string key = language + "\n" + string.Join("\n", path.Select(folder => folder.Id + "\t" + folder.DisplayName));
        if (key == crumbsKey) return;
        crumbsKey = key;
        FolderCrumbs.Children.Clear();
        foreach (var folder in path)
        {
            if (FolderCrumbs.Children.Count > 0)
                FolderCrumbs.Children.Add(new TextBlock { Text = "›", FontSize = 13, Foreground = CrumbSeparatorBrush,
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(1, 0, 1, 0) });
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new System.Windows.Shapes.Path { Data = FolderGeometry, Fill = FolderIconBrush, Width = 14, Height = 11,
                Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            content.Children.Add(new TextBlock { Text = folder.DisplayName, VerticalAlignment = VerticalAlignment.Center });
            string hint = T("Перейти в папку") + ": " + folder.DisplayName;
            var crumb = new Button { Style = (Style)FindResource("Crumb"), Content = content, Tag = folder.Id, ToolTip = hint };
            System.Windows.Automation.AutomationProperties.SetName(crumb, hint);
            crumb.Click += CrumbClick;
            FolderCrumbs.Children.Add(crumb);
        }
    }

    /// <summary>
    /// A folder of the path: the search is cleared and the folder selected. The item stays open; in the compact
    /// window, which shows either the list or the editor, the list of that folder is shown instead.
    /// </summary>
    private void CrumbClick(object sender, RoutedEventArgs e)
    {
        if (busy || session is null || sender is not Button { Tag: string id } || !folderNodes.ContainsKey(id)) return;
        updating = true; Search.Clear(); updating = false;
        folderId = id;
        if (compact) draft = original = null;
        else if (draft is not null && category is not null && draft.Kind != category) category = null;
        RefreshLists(); ShowDraft();
        if (ItemList.SelectedItem is { } selected) ItemList.ScrollIntoView(selected);
    }

    private void SearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None) { e.Handled = true; NextMatch(); }
        else if (e.Key == Key.Escape && Search.Text.Length > 0) { e.Handled = true; Search.Clear(); }
    }

    private void NextMatchClick(object sender, RoutedEventArgs e) => NextMatch();

    /// <summary>
    /// The next match in the text of the open item; after the last one — the next found item, after the last item
    /// the first one again. With nothing open it opens the first found item.
    /// </summary>
    private void NextMatch()
    {
        if (busy || session is null || !Searching) return;
        int count = draft is null || jsonView ? 0 : TextMatches.Find(TextField.Text, SearchQuery, MaxSearchMarks).Count;
        if (searchHit + 1 < count) { searchHit++; UpdateSearchMarks(center: true); return; }
        int items = ItemList.Items.Count;
        if (items == 0) return;
        int next = (ItemList.SelectedIndex + 1) % items;
        if (next == ItemList.SelectedIndex) { searchHit = 0; UpdateSearchMarks(center: true); return; }
        ItemList.SelectedIndex = next; // Opens the item as a click on its row does.
        if (ItemList.SelectedItem is { } selected) ItemList.ScrollIntoView(selected);
    }

    /// <summary>
    /// Keeps the counter, the "next" button and the marks in the text in step with the query and the open item.
    /// A newly opened item starts from its first match, which is brought to the middle of the editor.
    /// </summary>
    private void UpdateSearchMarks(bool center)
    {
        string query = SearchQuery;
        if (draft?.Id != searchHitItem) { searchHitItem = draft?.Id; searchHit = 0; center = true; }
        int count = draft is null || query.Length == 0 ? 0 : TextMatches.Find(TextField.Text, query, MaxSearchMarks).Count;
        if (searchHit >= count) searchHit = 0;
        markupView?.SetSearch(query, count == 0 ? -1 : searchHit);
        SearchCount.Text = query.Length == 0 || draft is null ? ""
            : count == 0 ? "0" : $"{(searchHit + 1).ToString(UiCulture)} / {count.ToString(UiCulture)}";
        SearchCountBox.Visibility = SearchCount.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        NextMatchButton.Visibility = query.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        string hint = T("Следующее совпадение (Enter)");
        NextMatchButton.ToolTip = hint;
        System.Windows.Automation.AutomationProperties.SetName(NextMatchButton, hint);
        if (center && count > 0) Dispatcher.BeginInvoke(DispatcherPriority.Loaded, CenterCurrentMatch);
    }

    /// <summary>Scrolls the editor so that the current match is in the middle of what is visible.</summary>
    private void CenterCurrentMatch()
    {
        if (markupView is null || draft is null || jsonView) return;
        markupView.Update(); // Line positions are known only after layout.
        if (markupView.CurrentHit is not { } hit) return;
        double y = TextField.TranslatePoint(new Point(hit.X, hit.Top + hit.Height / 2), EditorScroll).Y;
        EditorScroll.ScrollToVerticalOffset(EditorScroll.VerticalOffset + y - EditorScroll.ViewportHeight / 2);
    }
}

/// <summary>A <see cref="TextBlock"/> text with the search query marked yellow: titles in the list of items.</summary>
public static class SearchHighlight
{
    private static readonly Brush MarkBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xE9, 0x8A));

    static SearchHighlight() => MarkBrush.Freeze();

    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(SearchHighlight), new PropertyMetadata("", Changed));
    public static readonly DependencyProperty QueryProperty = DependencyProperty.RegisterAttached(
        "Query", typeof(string), typeof(SearchHighlight), new PropertyMetadata("", Changed));

    public static string GetText(DependencyObject target) => (string)target.GetValue(TextProperty);
    public static void SetText(DependencyObject target, string value) => target.SetValue(TextProperty, value);
    public static string GetQuery(DependencyObject target) => (string)target.GetValue(QueryProperty);
    public static void SetQuery(DependencyObject target, string value) => target.SetValue(QueryProperty, value);

    private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not TextBlock block) return;
        string text = GetText(block) ?? "", query = (GetQuery(block) ?? "").Trim();
        block.Inlines.Clear();
        int position = 0;
        foreach (int start in TextMatches.Find(text, query, 50))
        {
            if (start > position) block.Inlines.Add(new Run(text[position..start]));
            block.Inlines.Add(new Run(text.Substring(start, query.Length)) { Background = MarkBrush });
            position = start + query.Length;
        }
        if (position < text.Length) block.Inlines.Add(new Run(text[position..]));
    }
}
