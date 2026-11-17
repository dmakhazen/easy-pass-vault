using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using EasyPassVault.Core;

namespace EasyPassVault.App;

// Window geometry, the Compact / Wide view and the draggable borders between the three areas.
// Reading and writing the settings file is done by WindowLayoutStore; this part only applies the numbers.
public partial class MainWindow
{
    private string layoutPath = Path.Combine(AppContext.BaseDirectory, WindowLayout.FileName);
    private WindowLayout layout = new();
    private bool compact;
    private bool shown;
    private (bool Compact, bool EditorOnly)? appliedAreas;

    private static WindowLayout.Area Screens => new(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
        SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);

    private void RestoreWindowLayout()
    {
        layout = WindowLayoutStore.Load(layoutPath).Normalize(Screens);
        compact = layout.Compact;
        ApplyWindowSize();
        if (layout.Left is { } left && layout.Top is { } top)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top = top;
        }
        if (layout.Maximized) WindowState = WindowState.Maximized;
        ApplyViewMode();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        shown = true;
    }

    private void ApplyWindowSize()
    {
        MinWidth = compact ? WindowLayout.MinCompactWidth : WindowLayout.MinWideWidth;
        MinHeight = compact ? WindowLayout.MinHeight : 640;
        if (WindowState != WindowState.Normal) return;
        Width = compact ? layout.CompactWidth : layout.Width;
        Height = compact ? layout.CompactHeight : layout.Height;
        var screens = Screens;
        if (double.IsFinite(Left) && Left + Width > screens.Left + screens.Width)
            Left = Math.Max(screens.Left, screens.Left + screens.Width - Width);
    }

    /// <summary>Copies the current size, position and area widths into <see cref="layout"/>.</summary>
    private void RememberWindowLayout()
    {
        double width = Width, height = Height, left = Left, top = Top;
        if (WindowState != WindowState.Normal)
        {
            Rect restore = RestoreBounds;
            if (restore.IsEmpty) width = height = left = top = double.NaN;
            else (width, height, left, top) = (restore.Width, restore.Height, restore.Left, restore.Top);
        }
        if (double.IsFinite(width) && double.IsFinite(height))
            layout = compact ? layout with { CompactWidth = width, CompactHeight = height }
                : layout with { Width = width, Height = height };
        if (double.IsFinite(left) && double.IsFinite(top)) layout = layout with { Left = left, Top = top };
        RememberColumns();
        layout = layout with { Compact = compact, Maximized = WindowState == WindowState.Maximized };
    }

    private void RememberColumns()
    {
        if (appliedAreas is { Compact: false })
            layout = layout with { FoldersWidth = FoldersColumn.Width.Value, ListWidth = ListColumn.Width.Value };
    }

    private void SaveWindowLayout()
    {
        // A window that was never shown (startup check, offscreen tests) has nothing worth remembering.
        if (shown) WindowLayoutStore.Save(layoutPath, layout);
    }

    private void SetCompact(bool value)
    {
        if (value == compact) return;
        RememberWindowLayout();
        compact = value;
        if (compact && WindowState != WindowState.Normal) WindowState = WindowState.Normal;
        ApplyWindowSize();
        ApplyViewMode();
        UpdateVaultHeader();
    }

    private void CompactViewClick(object sender, RoutedEventArgs e) => SetCompact(true);
    private void WideViewClick(object sender, RoutedEventArgs e) => SetCompact(false);

    private void ApplyViewMode()
    {
        Visibility wideOnly = compact ? Visibility.Collapsed : Visibility.Visible;
        Visibility compactOnly = compact ? Visibility.Visible : Visibility.Collapsed;
        ViewSwitch.Visibility = SaveShortcut.Visibility = SaveVersionButton.Visibility = LockLabel.Visibility =
            FolderTitle.Visibility = GeneratorLabel.Visibility = wideOnly;
        ExpandButton.Visibility = LockIcon.Visibility = BackButton.Visibility = CompactFolderPicker.Visibility = compactOnly;
        LockButton.Style = GeneratePasswordButton.Style = (Style)FindResource(compact ? "HeaderIcon" : "Header");
        EditorFields.Margin = compact ? new Thickness(16, 14, 16, 14) : new Thickness(28, 22, 28, 22);
        ApplyAreas();
    }

    /// <summary>Wide: folders, list and editor side by side. Compact: the list, or the opened item in its place.</summary>
    private void ApplyAreas()
    {
        var wanted = (compact, compact && draft is not null);
        if (appliedAreas == wanted) return;
        RememberColumns();
        appliedAreas = wanted;
        bool editorOnly = wanted.Item2;
        FoldersPane.Visibility = FoldersSplitter.Visibility = ListSplitter.Visibility =
            compact ? Visibility.Collapsed : Visibility.Visible;
        ListPane.Visibility = editorOnly ? Visibility.Collapsed : Visibility.Visible;
        EditorPane.Visibility = compact && !editorOnly ? Visibility.Collapsed : Visibility.Visible;
        var star = new GridLength(1, GridUnitType.Star);
        var none = new GridLength(0);
        if (compact)
        {
            FoldersColumn.MinWidth = RestColumn.MinWidth = ListColumn.MinWidth = EditorColumn.MinWidth = 0;
            FoldersColumn.Width = none;
            ListColumn.Width = editorOnly ? none : star;
            EditorColumn.Width = editorOnly ? star : none;
            return;
        }
        FoldersColumn.MinWidth = WindowLayout.MinFoldersWidth;
        ListColumn.MinWidth = WindowLayout.MinListWidth;
        EditorColumn.MinWidth = WindowLayout.MinEditorWidth;
        FoldersColumn.Width = new GridLength(layout.FoldersWidth);
        ListColumn.Width = new GridLength(layout.ListWidth);
        EditorColumn.Width = star;
        RestColumn.MinWidth = layout.ListWidth + WindowLayout.MinEditorWidth;
        // The window may still have its previous (compact) size here: fit only after the next layout pass.
        Dispatcher.BeginInvoke(FitColumns, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>Keeps the editor at least at its minimum width after a drag or a window resize.</summary>
    private void FitColumns()
    {
        if (appliedAreas is not { Compact: false }) return;
        double folders = FoldersColumn.Width.Value, list = ListColumn.Width.Value;
        if (Workspace.ActualWidth > 0)
            (folders, list) = WindowLayout.FitColumns(Workspace.ActualWidth, folders, list);
        if (folders != FoldersColumn.Width.Value) FoldersColumn.Width = new GridLength(folders);
        if (list != ListColumn.Width.Value) ListColumn.Width = new GridLength(list);
        // The folder border may take room from the editor only, never push the list out of the window.
        RestColumn.MinWidth = list + WindowLayout.MinEditorWidth;
    }

    private void SplitterDragCompleted(object sender, DragCompletedEventArgs e) => FitColumns();
    private void WorkspaceSizeChanged(object sender, SizeChangedEventArgs e) => FitColumns();

    private void MenuClick(object sender, RoutedEventArgs e)
    {
        MainMenu.PlacementTarget = MenuButton;
        MainMenu.IsOpen = true;
    }

    // Sort order is a view setting: it lives in the layout file and never reaches the vault.
    private static readonly (ItemSort Sort, string Label)[] ItemSorts =
    [
        (ItemSort.NameAscending, "По названию А→Я"), (ItemSort.NameDescending, "По названию Я→А"),
        (ItemSort.ModifiedNewest, "По дате: сначала новые"), (ItemSort.ModifiedOldest, "По дате: сначала старые")
    ];

    private void ApplySortTexts()
    {
        if (ItemSortMenu.Items.Count == 0)
            foreach (var (sort, _) in ItemSorts)
            {
                var item = new MenuItem { Tag = sort };
                item.Click += (_, _) => SetItemSort(sort);
                ItemSortMenu.Items.Add(item);
            }
        foreach (MenuItem item in ItemSortMenu.Items)
        {
            var sort = (ItemSort)item.Tag;
            item.Header = T(ItemSorts.First(entry => entry.Sort == sort).Label);
            item.IsChecked = sort == layout.ItemSort;
        }
        ItemSortButton.Content = T(ItemSorts.First(entry => entry.Sort == layout.ItemSort).Label);
        FolderSortButton.Content = T(layout.FolderSort == FolderSort.NameDescending ? "Я→А" : "А→Я");
    }

    private void SetItemSort(ItemSort sort)
    {
        if (busy || sort == layout.ItemSort) return;
        layout = layout with { ItemSort = sort };
        ApplySortTexts();
        if (session is not null) RefreshLists();
    }

    private void SetFolderSort(FolderSort sort)
    {
        if (busy || sort == layout.FolderSort) return;
        layout = layout with { FolderSort = sort };
        ApplySortTexts();
        if (session is not null) RefreshLists();
    }

    private void ItemSortClick(object sender, RoutedEventArgs e)
    {
        ItemSortMenu.PlacementTarget = ItemSortButton;
        ItemSortMenu.IsOpen = true;
    }

    private void FolderSortClick(object sender, RoutedEventArgs e) =>
        SetFolderSort(layout.FolderSort == FolderSort.NameDescending ? FolderSort.NameAscending : FolderSort.NameDescending);

    private void CompactFolderChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!updating && CompactFolderPicker.SelectedItem is FolderInfo folder) SelectFolder(folder.Id);
    }

    private void BackToListClick(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        // Edits stay staged in pendingItems, exactly as when another folder is selected.
        draft = original = null;
        RefreshLists();
        ShowDraft();
    }
}
