using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using EasyPassVault.Core;

namespace EasyPassVault.App;

public partial class MainWindow
{
    private const string FolderDragFormat = "easy-pass-vault/folder";
    private const string ItemDragFormat = "easy-pass-vault/item";
    private (string Format, string Id)? dragCandidate;
    private (string Id, string FolderId)? draggedItem;
    private Point dragStart;

    private void FolderHeaderPressed(object sender, MouseButtonEventArgs e)
    {
        dragCandidate = sender is Border { Tag: FolderInfo { ParentId: not null } folder }
            ? (FolderDragFormat, folder.Id)
            : null;
        dragStart = e.GetPosition(this);
    }

    private void ItemListPressed(object sender, MouseButtonEventArgs e)
    {
        draggedItem = null;
        dragCandidate = null;
        if (e.OriginalSource is not DependencyObject source ||
            ItemsControl.ContainerFromElement(ItemList, source) is not ListBoxItem { Content: VaultItem item }) return;
        string sourceFolder = pendingItems.TryGetValue(item.Id, out var pending) ? pending.Item.FolderId : item.FolderId;
        draggedItem = (item.Id, sourceFolder);
        dragCandidate = (ItemDragFormat, item.Id);
        dragStart = e.GetPosition(this);
    }

    private void DragSourceMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (dragCandidate is not null) draggedItem = null;
        dragCandidate = null;
    }

    private void DragSourceMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || busy)
        {
            if (dragCandidate is not null) draggedItem = null;
            dragCandidate = null;
            return;
        }
        if (dragCandidate is not { } candidate) return;
        Point position = e.GetPosition(this);
        if (Math.Abs(position.X - dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        dragCandidate = null;
        try { DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(candidate.Format, candidate.Id), DragDropEffects.Move); }
        finally { draggedItem = null; }
    }

    private void FolderHeaderDragOver(object sender, DragEventArgs e)
    {
        var header = (Border)sender;
        var destination = (FolderInfo)header.Tag;
        bool valid = FindDraggedItem(e, destination) is not null || FindDraggedFolder(e, destination) is not null;
        header.Background = valid ? (Brush)FindResource("FolderDropBrush") : Brushes.Transparent;
        e.Effects = valid ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void FolderHeaderDragLeave(object sender, DragEventArgs e) =>
        ((Border)sender).Background = Brushes.Transparent;

    private async void FolderHeaderDrop(object sender, DragEventArgs e)
    {
        var header = (Border)sender;
        header.Background = Brushes.Transparent;
        var destination = (FolderInfo)header.Tag;
        string? itemId = FindDraggedItem(e, destination);
        var folder = FindDraggedFolder(e, destination);
        e.Effects = itemId is null && folder is null ? DragDropEffects.None : DragDropEffects.Move;
        e.Handled = true;
        if (itemId is not null)
        {
            MoveItemDraft(itemId, destination.Id);
            return;
        }
        if (folder is null || session is not { } vault) return;
        if (!await Run(T("Сохранение…"), () => vault.MoveFolder(folder.Id, destination.Id))) return;

        draft = original = null;
        folderId = folder.Id;
        RefreshLists();
        ShowDraft();
        Status.Text = T("Папка перемещена");
    }

    private string? FindDraggedItem(DragEventArgs e, FolderInfo destination) =>
        !busy && session is not null && draggedItem is { } item && item.FolderId != destination.Id &&
        e.Data.GetData(ItemDragFormat, autoConvert: false) is string id && id == item.Id ? id : null;

    private void MoveItemDraft(string itemId, string destinationId)
    {
        if (busy || session is not { } vault) return;
        LoadDraft(vault, itemId);
        draft = draft! with { FolderId = destinationId };
        TrackDraft();
        folderId = destinationId;
        RefreshLists();
        ShowDraft();
    }

    private FolderInfo? FindDraggedFolder(DragEventArgs e, FolderInfo destination)
    {
        if (busy || session is null || e.Data.GetData(FolderDragFormat, autoConvert: false) is not string id ||
            !folderNodes.TryGetValue(id, out var node) || node.Tag is not FolderInfo { ParentId: not null } folder ||
            folder.ParentId == destination.Id) return null;

        for (FolderInfo current = destination; ; current = (FolderInfo)folderNodes[current.ParentId].Tag)
        {
            if (current.Id == id) return null;
            if (current.ParentId is null) return folder;
        }
    }
}
