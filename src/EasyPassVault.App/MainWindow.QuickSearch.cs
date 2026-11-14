using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using EasyPassVault.Core;

namespace EasyPassVault.App;

// Quick search (Ctrl+K): one field over all folders and both kinds of items. Enter copies the password,
// Ctrl+Enter the login, Tab opens the item in the main window. The password never takes part in the search
// and is never shown here: it is read only at the moment of copying, through the same code as the copy buttons.
public partial class MainWindow
{
    private sealed record PaletteRow(VaultItem Item, string Letter, string Title, string Info, string EnterAction, Brush BadgeBrush);

    private static readonly Brush[] BadgeBrushes = [Frozen("#CFE3D6"), Frozen("#E8DDBD"), Frozen("#D5DEE8")];

    private static Brush Frozen(string color)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
        brush.Freeze();
        return brush;
    }

    private bool PaletteOpen => PaletteOverlay.Visibility == Visibility.Visible;

    private void OpenPalette()
    {
        if (busy || session is null || PaletteOpen) return;
        PaletteOverlay.Visibility = Visibility.Visible;
        PaletteQuery.Clear();
        RefreshPalette();
        PaletteQuery.Focus();
        Keyboard.Focus(PaletteQuery);
    }

    private void ClosePalette()
    {
        if (!PaletteOpen) return;
        PaletteOverlay.Visibility = Visibility.Collapsed;
        PaletteList.ItemsSource = null;
        PaletteQuery.Clear();
    }

    private void QuickSearchClick(object sender, RoutedEventArgs e) => OpenPalette();

    private void PaletteQueryChanged(object sender, TextChangedEventArgs e)
    {
        if (PaletteOpen) RefreshPalette();
    }

    private void RefreshPalette()
    {
        if (session is not { } vault) return;
        var folders = vault.Folders.ToDictionary(folder => folder.Id, folder => folder.ParentId is null ? T("Корень хранилища") : folder.Name);
        string Title(VaultItem item) => string.IsNullOrWhiteSpace(item.Title) ? T(item.DisplayTitle) : item.Title;
        var found = VaultSearch.Find(GetVisibleItems(vault), PaletteQuery.Text, UiCulture, Title);
        var rows = found.Select((item, index) =>
        {
            string title = Title(item);
            string folder = folders.GetValueOrDefault(item.FolderId, "");
            string first = item.Kind == ItemKind.Text ? T("Текстовый файл") : item.Login;
            string info = string.Join(" · ", new[] { first, folder }.Where(part => part.Length > 0));
            return new PaletteRow(item, StringInfo.GetNextTextElement(title.TrimStart()).ToUpper(UiCulture), title, info,
                T(item.Kind == ItemKind.Text ? "открыть файл" : "скопировать пароль"), BadgeBrushes[index % BadgeBrushes.Length]);
        }).ToList();
        PaletteList.ItemsSource = rows;
        PaletteList.SelectedIndex = rows.Count > 0 ? 0 : -1;
        PaletteCount.Text = rows.Count == 0 ? T("Ничего не найдено")
            : T("НАЙДЕНО {0}").Replace("{0}", rows.Count.ToString(UiCulture));
    }

    private PaletteRow? SelectedPaletteRow => PaletteList.SelectedItem as PaletteRow;

    private void MovePaletteSelection(int step)
    {
        int count = PaletteList.Items.Count;
        if (count == 0) return;
        PaletteList.SelectedIndex = Math.Clamp(PaletteList.SelectedIndex + step, 0, count - 1);
        PaletteList.ScrollIntoView(PaletteList.SelectedItem);
    }

    private void PaletteKeyDown(object sender, KeyEventArgs e)
    {
        var modifiers = Keyboard.Modifiers;
        switch (e.Key)
        {
            case Key.Escape: e.Handled = true; ClosePalette(); break;
            case Key.Down: e.Handled = true; MovePaletteSelection(1); break;
            case Key.Up: e.Handled = true; MovePaletteSelection(-1); break;
            case Key.PageDown: e.Handled = true; MovePaletteSelection(8); break;
            case Key.PageUp: e.Handled = true; MovePaletteSelection(-8); break;
            case Key.Enter when modifiers == ModifierKeys.Control: e.Handled = true; PaletteCopy(CopyField.Login); break;
            case Key.Enter when modifiers == ModifierKeys.None: e.Handled = true; PaletteEnter(); break;
            case Key.Tab: e.Handled = true; PaletteShowSelected(); break;
        }
    }

    /// <summary>Enter: the password of an entry goes to the clipboard; a text file is opened instead.</summary>
    private void PaletteEnter()
    {
        if (SelectedPaletteRow is not { } row) return;
        if (row.Item.Kind == ItemKind.Text) ShowItem(row.Item.Id);
        else PaletteCopy(CopyField.Password);
    }

    private void PaletteCopy(CopyField field)
    {
        if (busy || SelectedPaletteRow is not { Item.Kind: ItemKind.Login } row) return;
        var values = CurrentValues(row.Item.Id);
        ClosePalette();
        Copy(field, values, reportEmpty: true);
    }

    private void PaletteShowSelected()
    {
        if (SelectedPaletteRow is { } row) ShowItem(row.Item.Id);
    }

    private void PaletteDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        while (source is not null and not ListBoxItem)
            source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
        if (source is ListBoxItem { DataContext: PaletteRow row })
        {
            e.Handled = true;
            ShowItem(row.Item.Id);
        }
    }

    private void PaletteBackdropDown(object sender, MouseButtonEventArgs e)
    {
        // Only the dimmed area closes the search; clicks inside the panel bubble up here too.
        if (ReferenceEquals(e.OriginalSource, PaletteOverlay)) { e.Handled = true; ClosePalette(); }
    }

    /// <summary>
    /// Opens an item in the main window: all folders, both categories, no list filter, the item selected.
    /// Edits of the item that was open stay staged, exactly as when another row of the list is clicked.
    /// </summary>
    private void ShowItem(string id)
    {
        if (busy || session is not { } vault) return;
        ClosePalette();
        if (!GetVisibleItems(vault).Any(item => item.Id == id)) return;
        updating = true;
        Search.Clear();
        updating = false;
        folderId = "";
        category = null;
        LoadDraft(vault, id);
        RefreshLists();
        ShowDraft();
        if (ItemList.SelectedItem is { } selected) ItemList.ScrollIntoView(selected);
    }
}
