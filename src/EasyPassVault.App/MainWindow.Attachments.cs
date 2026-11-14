using System.IO;
using System.Windows;
using System.Windows.Media;
using EasyPassVault.Core;
using Microsoft.Win32;

namespace EasyPassVault.App;

/// <summary>
/// Attachments of the open entry or text file (Attachments.dc.html): drop zone, list, preview from memory,
/// "Save as…" and removal. Adding and removing only edit the draft; the vault is written by Save as usual.
/// </summary>
public partial class MainWindow
{
    private sealed record AttachmentRow(VaultAttachment Attachment, string Badge, string Name, string Info, Brush Tint,
        Visibility PreviewVisibility, Thickness Separator);

    private static readonly Brush[] AttachmentTints = new[] { "#CFE3D6", "#E8DDBD", "#D5DEE8" }
        .Select(color => (Brush)new BrushConverter().ConvertFromString(color)!).ToArray();

    // Dialogs are delegates so the WPF tests can answer them; the defaults show the real dialogs.
    private Func<string, string, string, bool> confirm = null!;
    private Func<string[]?> pickAttachmentFiles = null!;
    private Func<string, string?> pickAttachmentTarget = null!;
    private Action<Window> showAttachmentWindow = null!;

    private void InitAttachments()
    {
        confirm = (title, message, accept) => new ConfirmWindow(title, message, accept, T) { Owner = this }.ShowDialog() == true;
        pickAttachmentFiles = () =>
        {
            var dialog = new OpenFileDialog { Multiselect = true, CheckFileExists = true, Title = T("Добавить вложения") };
            return dialog.ShowDialog(this) == true ? dialog.FileNames : null;
        };
        pickAttachmentTarget = name =>
        {
            string extension = Path.GetExtension(name);
            var dialog = new SaveFileDialog
            {
                FileName = name, OverwritePrompt = true, AddExtension = extension.Length > 0, DefaultExt = extension,
                Filter = (extension.Length > 0 ? $"*{extension}|*{extension}|" : "") + "*.*|*.*"
            };
            return dialog.ShowDialog(this) == true ? dialog.FileName : null;
        };
        showAttachmentWindow = window => { window.Owner = this; window.Show(); };
        JsonView.CopyText = CopyAllText;
    }

    private string Megabytes(long bytes) => (bytes / (1024.0 * 1024.0)).ToString(bytes < 10L * 1024 * 1024 ? "0.0" : "N0", UiCulture);

    /// <summary>The vault file size once the pending drafts are saved (the file itself when nothing is pending).</summary>
    private long EstimatedVaultSize() => session is null ? 0
        : VaultAttachments.EstimateSize(session.FileSize, pendingItems.Values.Select(pending => (pending.Item, pending.Original)));

    private string AttachmentInfo(VaultAttachment attachment)
    {
        string kind = VaultAttachments.KindOf(attachment.Name) switch
        {
            AttachmentKind.Image => T("Картинка"),
            AttachmentKind.Document => T("Документ"),
            AttachmentKind.Archive => T("Архив"),
            _ => T("Файл")
        };
        var parts = new List<string> { kind, VersionsWindow.FormatSize(attachment.Size, T, UiCulture) };
        if (attachment.Added is { } added)
            parts.Add(T("добавлено {0}").Replace("{0}", added.ToLocalTime().ToString("d", UiCulture)));
        return string.Join(" · ", parts);
    }

    /// <summary>Title with the count, total size, the rows and the vault space bar.</summary>
    private void ShowAttachments()
    {
        var attachments = draft?.Attachments ?? [];
        AttachmentsTitle.Text = T("Вложения · {0}").Replace("{0}", attachments.Count.ToString(UiCulture));
        AttachmentsTotal.Text = attachments.Count == 0 ? T("хранятся внутри зашифрованного файла")
            : T("всего {0} · хранятся внутри зашифрованного файла")
                .Replace("{0}", VersionsWindow.FormatSize(VaultAttachments.TotalSize(attachments), T, UiCulture));
        AttachmentList.ItemsSource = attachments.Select((attachment, index) => new AttachmentRow(attachment,
            VaultAttachments.Badge(attachment.Name), attachment.Name, AttachmentInfo(attachment),
            AttachmentTints[index % AttachmentTints.Length],
            VaultAttachments.CanPreview(attachment.Name) ? Visibility.Visible : Visibility.Collapsed,
            new Thickness(0, 0, 0, index == attachments.Count - 1 ? 0 : 1))).ToArray();
        AttachmentListBorder.Visibility = attachments.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        UpdateVaultSpace();
    }

    /// <summary>"Vault space: N of 512 MB" under the drop zone. Counts unsaved attachments as well.</summary>
    private void UpdateVaultSpace()
    {
        if (session is null) return;
        long size = EstimatedVaultSize();
        double share = Math.Clamp(size / (double)VaultAttachments.MaxBytes, 0, 1);
        VaultSpaceTrack.ColumnDefinitions[0].Width = new GridLength(share, GridUnitType.Star);
        VaultSpaceTrack.ColumnDefinitions[1].Width = new GridLength(1 - share, GridUnitType.Star);
        VaultSpaceFill.Background = (Brush)FindResource(size > VaultAttachments.WarnBytes ? "PendingSaveForeground" : "Green");
        VaultSpaceText.Text = (size != session.FileSize ? "≈ " : "") + T("{0} из 512 МБ").Replace("{0}", Megabytes(size));
    }

    /// <summary>Replaces the draft's attachments: the item becomes edited, Revert brings the old list back.</summary>
    private void SetAttachments(IReadOnlyList<VaultAttachment> attachments)
    {
        if (draft is null || session is null) return;
        draft = draft with { Attachments = VaultAttachments.Sorted(attachments) };
        TrackDraft();
        RefreshLists();
        ShowAttachments();
        UpdateSaveState();
        UpdateFolderActions();
    }

    /// <summary>
    /// Adds files to the open draft. Warns above 100 MB of estimated vault size, refuses above 512 MB.
    /// Files are read off the UI thread straight into protected memory.
    /// </summary>
    private async Task<bool> AddAttachmentFiles(IReadOnlyList<string> paths)
    {
        if (busy || session is null || draft is null) return false;
        FileInfo[] files;
        try { files = paths.Where(File.Exists).Select(path => new FileInfo(path)).ToArray(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { files = []; }
        if (files.Length == 0) { Status.Text = T("Перетащите сюда картинки, документы или архивы"); return false; }

        long estimate = EstimatedVaultSize() + files.Sum(file => file.Length);
        switch (VaultAttachments.Check(estimate))
        {
            case SizeCheck.TooLarge:
                ShowError(T("Хранилище станет примерно {0} МБ — больше предела 512 МБ. Файлы не добавлены. Такой файл открывался бы и сохранялся слишком долго и мог бы не поместиться в память. Храните крупные файлы отдельно.")
                    .Replace("{0}", Megabytes(estimate)));
                return false;
            case SizeCheck.Warn when !confirm(T("Добавить вложения"),
                T("Хранилище станет примерно {0} МБ. Открытие и каждое сохранение будут занимать заметное время, а каждая новая версия файла займёт столько же места на диске.")
                    .Replace("{0}", Megabytes(estimate)), T("Добавить")):
                return false;
        }

        var names = draft.Attachments.Select(attachment => attachment.Name).ToList();
        var plan = new List<(string Path, string Name)>();
        foreach (var file in files)
        {
            string name = VaultAttachments.UniqueName(names, file.Name);
            names.Add(name);
            plan.Add((file.FullName, name));
        }
        var loaded = new List<VaultAttachment>();
        DateTime now = DateTime.UtcNow;
        if (!await Run(T("Читаю файлы…"), () =>
            { foreach (var (path, name) in plan) loaded.Add(VaultAttachments.Load(path, name, now)); })) return false;
        if (draft is null) return false;
        SetAttachments([.. draft.Attachments, .. loaded]);
        Status.Text = T("Вложений добавлено: {0}. Сохраните хранилище.").Replace("{0}", loaded.Count.ToString(UiCulture));
        return true;
    }

    private async void PickAttachmentsClick(object sender, RoutedEventArgs e)
    {
        if (busy || draft is null) return;
        if (pickAttachmentFiles() is { Length: > 0 } paths) await AddAttachmentFiles(paths);
    }

    private static bool HasFiles(DragEventArgs e) => e.Data.GetDataPresent(DataFormats.FileDrop);

    private void AttachmentDragOver(object sender, DragEventArgs e)
    {
        if (!HasFiles(e)) return;
        bool accept = !busy && session is not null && draft is not null;
        e.Effects = accept ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        HighlightDropZone(accept);
    }

    private void AttachmentDragLeave(object sender, DragEventArgs e) => HighlightDropZone(false);

    private void HighlightDropZone(bool on)
    {
        DropOutline.Fill = (Brush)new BrushConverter().ConvertFromString(on ? "#DDEEE3" : "#F1F7F3")!;
        DropOutline.Stroke = (Brush)new BrushConverter().ConvertFromString(on ? "#2F7059" : "#7FA895")!;
    }

    private void AttachmentDrop(object sender, DragEventArgs e)
    {
        if (!HasFiles(e)) return;
        e.Handled = true;
        HighlightDropZone(false);
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
        // Explorer waits until Drop returns: dialogs and reading come after it.
        Dispatcher.BeginInvoke(async () => await AddAttachmentFiles(paths));
    }

    private static VaultAttachment? RowAttachment(object sender) =>
        (sender as FrameworkElement)?.DataContext is AttachmentRow row ? row.Attachment : null;

    /// <summary>
    /// The preview window of an attachment, built from a copy of its bytes in memory (the copy is cleared): a picture,
    /// formatted JSON or plain text. Null when WPF cannot decode the picture. The attachment itself is not touched.
    /// </summary>
    private Window? CreateAttachmentPreview(VaultAttachment attachment) => TextPreview.KindOf(attachment.Name) switch
    {
        PreviewKind.Image => ImagePreviewWindow.TryCreate(attachment.Name, attachment.Content.ReadBytes()),
        PreviewKind.Json or PreviewKind.Text => CreateTextPreview(attachment.Name,
            TextPreview.Build(attachment.Content.ReadBytes(), TextPreview.KindOf(attachment.Name) == PreviewKind.Json)),
        _ => null
    };

    private TextPreviewWindow CreateTextPreview(string name, TextPreviewResult result)
    {
        var window = new TextPreviewWindow(name, result, T, CopyAllText);
        window.Loaded += (_, _) => window.View.Body.Focus();
        return window;
    }

    private async void PreviewAttachmentClick(object sender, RoutedEventArgs e)
    {
        if (busy || RowAttachment(sender) is not { } attachment) return;
        var kind = TextPreview.KindOf(attachment.Name);
        if (kind == PreviewKind.None) return;
        if (kind == PreviewKind.Image)
        {
            if (CreateAttachmentPreview(attachment) is { } image) showAttachmentWindow(image);
            else ShowError(T("Не удалось показать картинку: формат не поддерживается или файл повреждён."));
            return;
        }
        if (attachment.Size > TextPreview.AskAboveBytes && !confirm(T("Просмотр"),
            T("Вложение больше 20 МБ ({0}). Просмотр может открываться медленно. Открыть?")
                .Replace("{0}", VersionsWindow.FormatSize(attachment.Size, T, UiCulture)), T("Открыть"))) return;
        // Decoding and formatting run off the UI thread; the window itself is made here.
        TextPreviewResult? result = null;
        if (!await Run(T("Открываю…"), () => result = TextPreview.Build(attachment.Content.ReadBytes(), kind == PreviewKind.Json))) return;
        showAttachmentWindow(CreateTextPreview(attachment.Name, result!));
    }

    private async void SaveAttachmentClick(object sender, RoutedEventArgs e)
    {
        if (busy || RowAttachment(sender) is not { } attachment) return;
        if (pickAttachmentTarget(attachment.Name) is not { } path) return;
        if (await Run(T("Сохранение…"), () => VaultAttachments.SaveAs(attachment, path)))
            Status.Text = T("Вложение сохранено");
    }

    private void RemoveAttachmentClick(object sender, RoutedEventArgs e)
    {
        if (busy || draft is null || RowAttachment(sender) is not { } attachment) return;
        if (!confirm(T("Удалить вложение"), T("Удалить вложение «{0}»?").Replace("{0}", attachment.Name), T("Удалить"))) return;
        SetAttachments(draft.Attachments.Where(item => !ReferenceEquals(item, attachment)).ToList());
        Status.Text = T("Вложение удалено. Сохраните хранилище.");
    }
}
