using System.ComponentModel;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using EasyPassVault.Core;
using Microsoft.Win32;

namespace EasyPassVault.App;

public partial class MainWindow : Window
{
    private VaultSession? session;
    private VaultItem? draft;
    private VaultItem? original;
    private readonly Dictionary<string, (VaultItem Item, VaultItem? Original)> pendingItems = [];
    private string folderId = "";
    private sealed record FolderHeader(string Name, int Count);
    private readonly Dictionary<string, TreeViewItem> folderNodes = [];
    private FolderInfo[] displayedFolders = [];
    private ItemKind? category;
    private bool updating = true;
    private bool busy;
    private bool allowClose;
    private bool closePending;
    private bool Dirty => draft is not null && draft != original;
    private bool NeedsSaveDecision => pendingItems.Count > 0;

    public MainWindow()
    {
        InitializeComponent();
        LanguagePicker.ItemsSource = Languages;
        LanguagePicker.DisplayMemberPath = nameof(UiLanguage.Name);
        LanguagePicker.SelectedIndex = 0;
        updating = false;
        PreviewKeyDown += async (_, e) =>
        {
            if (busy || Keyboard.Modifiers != ModifierKeys.Control) return;
            if (e.Key == Key.S) { e.Handled = true; await SaveChanges(); }
            if (e.Key == Key.L) { e.Handled = true; await LockVault(); }
        };
    }

    private async Task<bool> Run(string message, Action action)
    {
        if (busy) return false;
        busy = true;
        Workspace.IsEnabled = Welcome.IsEnabled = LockButton.IsEnabled = VersionActions.IsEnabled =
            ExportButton.IsEnabled = LanguagePicker.IsEnabled = false;
        Status.Text = message;
        try { await Task.Run(action); return true; }
        catch (VaultException ex) { ShowError(TranslateError(ex.Message)); return false; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        { ShowError(TranslateError("Операция не выполнена. Проверьте доступ к файлу, свободное место и доступность носителя. Исходное хранилище не перезаписывалось после ошибки чтения.")); return false; }
        catch (Exception)
        { ShowError(TranslateError("Операция не выполнена из-за внутренней ошибки. Черновик оставлен в редакторе. Подробности, содержащие секреты, не записываются в журнал.")); return false; }
        finally
        {
            busy = false;
            Workspace.IsEnabled = Welcome.IsEnabled = VersionActions.IsEnabled = ExportButton.IsEnabled =
                LanguagePicker.IsEnabled = true;
            LockButton.IsEnabled = session is not null;
            UpdateVaultHeader();
        }
    }

    private void ShowError(string message)
    { Status.Text = T("Операция не выполнена."); MessageBox.Show(this, message, "easy-pass-vault", MessageBoxButton.OK, MessageBoxImage.Warning); }

    private string? Prompt(string title, string label, bool secret = false, bool confirm = false, string initial = "")
    {
        var window = new PromptWindow(this, title, label, T, secret, confirm, initial);
        bool? accepted = window.ShowDialog();
        string? value = accepted == true ? window.Value : null;
        window.Clear(); return value;
    }

    private async void OpenClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "KeePass (*.kdbx)|*.kdbx", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        string? password = Prompt(T("Открытие хранилища"), T("Введите главный пароль"), secret: true);
        if (password is null) return;
        VaultSession? opened = null;
        string? newer = null;
        if (await Run(T("Открытие и проверка целостности…"), () =>
            { opened = VaultSession.Open(dialog.FileName, password); newer = opened.NewerVersionPath; }))
        {
            session = opened; EnterWorkspace();
            if (newer is not null)
                MessageBox.Show(this, T("Это не последняя версия хранилища. В папке есть более новая: {0}.")
                    .Replace("{0}", Path.GetFileName(newer)), "easy-pass-vault", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void CreateClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "KeePass (*.kdbx)|*.kdbx", DefaultExt = ".kdbx", FileName = "Personal.kdbx", OverwritePrompt = true };
        if (dialog.ShowDialog(this) != true) return;
        string path;
        try { path = VaultVersions.FirstPath(dialog.FileName, DateTime.Today); }
        catch (VaultException ex) { ShowError(TranslateError(ex.Message)); return; }
        if (File.Exists(path)) { ShowError(T("Файл уже существует. Выберите другое имя; существующее хранилище не перезаписано.")); return; }
        string? password = Prompt(T("Новое хранилище"), T("Придумайте главный пароль от 12 символов. Восстановить забытый пароль нельзя."), true, true);
        if (password is null) return;
        VaultSession? created = null;
        if (await Run(T("Создание зашифрованного хранилища…"), () => created = VaultSession.Create(path, password)))
        { session = created; EnterWorkspace(); }
    }

    private void EnterWorkspace()
    {
        category = null;
        folderId = "";
        draft = original = null;
        Welcome.Visibility = Visibility.Collapsed; Workspace.Visibility = Visibility.Visible; LockButton.IsEnabled = true;
        LockButton.Visibility = Visibility.Visible;
        VersionActions.Visibility = Visibility.Visible;
        SaveStateBadge.Visibility = Visibility.Visible;
        ExportButton.Visibility = Visibility.Visible;
        UpdateVaultHeader();
        RefreshLists(); ShowDraft(); Status.Text = T("Хранилище открыто");
    }

    private void UpdateVaultHeader()
    {
        VaultName.Text = session is null ? T("Локальное хранилище") : Path.GetFileName(session.FilePath);
        VaultName.ToolTip = session?.FilePath;
        SavedAt.Text = session is null ? "" : session.LastSavedAt is { } savedAt
            ? T("Последнее сохранение: {0}").Replace("{0}", savedAt.ToLocalTime().ToString("g", UiCulture))
            : T("Дата сохранения неизвестна");
        string? next = null;
        try { next = session is null ? null : Path.GetFileName(session.NextVersionPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or VaultException) { }
        // TextBlock, not a string: WPF would treat the first "_" of the file name as an access key and hide it.
        SaveVersionButton.Content = new TextBlock { Text = T("Создать новый файл и зашифровать в {0}").Replace("{0}", next ?? "…") };
    }

    private async void ExportClick(object sender, RoutedEventArgs e)
    {
        if (busy || session is null) return;
        string title = T("Экспорт в текст…");
        if (MessageBox.Show(this, T("Экспорт включает все сохранённые записи и пароли в открытом виде. Продолжить?"),
            title, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        var dialog = new SaveFileDialog
        {
            Title = title,
            Filter = "TXT (*.txt)|*.txt",
            DefaultExt = ".txt",
            FileName = Path.GetFileNameWithoutExtension(session.FilePath) + "-export.txt",
            OverwritePrompt = false
        };
        if (dialog.ShowDialog(this) != true) return;
        if (File.Exists(dialog.FileName))
        {
            ShowError(T("Файл уже существует. Выберите другое имя; существующее хранилище не перезаписано."));
            return;
        }
        if (await Run(title, () => session.ExportText(dialog.FileName)))
            Status.Text = T("Экспорт завершён");
    }

    private void RefreshLists()
    {
        if (session is null) return;
        updating = true;
        try
        {
            var vaultFolders = session.Folders.ToArray();
            vaultFolders[0] = vaultFolders[0] with { Name = T("Корень хранилища") };
            RefreshFolderTree(vaultFolders);
            FolderTitle.Text = folderId.Length == 0 ? T("Все папки") : ((FolderInfo)folderNodes[folderId].Tag).Name;
            ItemFolder.ItemsSource = vaultFolders;
            if (draft is not null) ItemFolder.SelectedItem = vaultFolders.FirstOrDefault(f => f.Id == draft.FolderId);
            string query = Search.Text.Trim();
            var visibleItems = GetVisibleItems(session);
            RefreshFolderCounts(visibleItems);
            var matches = visibleItems.Where(x => (folderId.Length == 0 || x.FolderId == folderId) &&
                new[] { x.Title, x.Login, x.Site, x.Text }.Any(s => s.Contains(query, StringComparison.OrdinalIgnoreCase))).ToList();
            var items = category is null ? matches : matches.Where(x => x.Kind == category).ToList();
            SearchNoResults.Visibility = query.Length > 0 && items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ItemList.ItemsSource = items;
            ItemList.SelectedItem = items.FirstOrDefault(x => x.Id == draft?.Id);
            AllCategory.Content = T("Все") + $" · {matches.Count}";
            LoginCategory.Content = T("Записи") + $" · {matches.Count(x => x.Kind == ItemKind.Login)}";
            TextCategory.Content = T("Файлы") + $" · {matches.Count(x => x.Kind == ItemKind.Text)}";
            AllCategory.Style = (Style)FindResource(category is null ? "Primary" : typeof(Button));
            LoginCategory.Style = (Style)FindResource(category == ItemKind.Login ? "Primary" : typeof(Button));
            TextCategory.Style = (Style)FindResource(category == ItemKind.Text ? "Primary" : typeof(Button));
            UpdateDeleteFolderState();
        }
        finally { updating = false; }
    }

    private VaultItem[] GetVisibleItems(VaultSession vault) =>
        vault.Items.Select(item => pendingItems.TryGetValue(item.Id, out var pending)
            ? pending.Item with { Password = "" }
            : item).Concat(pendingItems.Values.Where(pending => pending.Original is null)
                .Select(pending => pending.Item with { Password = "" })).ToArray();

    private void RefreshFolderCounts(IReadOnlyList<VaultItem> items)
    {
        var counts = items.GroupBy(item => item.FolderId).ToDictionary(group => group.Key, group => group.Count());
        foreach (var (id, node) in folderNodes)
        {
            var header = new FolderHeader(((FolderInfo)node.Tag).DisplayName, counts.GetValueOrDefault(id));
            if (!header.Equals(node.Header)) node.Header = header;
        }
    }

    private void RefreshFolderTree(FolderInfo[] vaultFolders)
    {
        if (!displayedFolders.SequenceEqual(vaultFolders))
        {
            var expanded = folderNodes.Where(pair => pair.Value.IsExpanded).Select(pair => pair.Key).ToHashSet();
            folderNodes.Clear();
            FolderList.Items.Clear();
            foreach (var folder in vaultFolders)
            {
                var node = new TreeViewItem
                {
                    Tag = folder,
                    FontWeight = folder.ParentId is null ? FontWeights.SemiBold : FontWeights.Normal,
                    IsExpanded = (folder.ParentId is null && displayedFolders.Length == 0) || expanded.Contains(folder.Id)
                };
                if (folder.ParentId is not null)
                    folderNodes[folder.ParentId].Items.Add(node);
                else
                    FolderList.Items.Add(node);
                folderNodes.Add(folder.Id, node);
            }
            displayedFolders = vaultFolders;
        }

        if (!folderNodes.ContainsKey(folderId)) folderId = "";
        AllFoldersButton.Content = T("Все папки");
        AllFoldersButton.Style = (Style)FindResource(folderId.Length == 0 ? "Primary" : typeof(Button));
        if (folderId.Length == 0)
        {
            if (FolderList.SelectedItem is TreeViewItem node) node.IsSelected = false;
            return;
        }
        var selected = folderNodes[folderId];
        if (selected.IsSelected) return;
        var folderInfo = (FolderInfo)selected.Tag;
        while (folderInfo.ParentId is { } parentId)
        {
            var parent = folderNodes[parentId];
            parent.IsExpanded = true;
            folderInfo = (FolderInfo)parent.Tag;
        }
        selected.IsSelected = true;
    }

    private void ShowDraft()
    {
        updating = true;
        try
        {
            Editor.Visibility = draft is null ? Visibility.Collapsed : Visibility.Visible;
            EmptyEditor.Visibility = draft is null ? Visibility.Visible : Visibility.Collapsed;
            // Clear controls even when collapsing the editor.
            TitleField.Text = draft?.Title ?? ""; LoginField.Text = draft?.Login ?? "";
            PasswordField.Password = draft?.Password ?? ""; VisiblePassword.Clear();
            PasswordField.Visibility = Visibility.Visible; VisiblePassword.Visibility = Visibility.Collapsed; ShowPassword.Content = T("Показать");
            SiteField.Text = draft?.Site ?? ""; TextField.Text = draft?.Text ?? "";
            ItemFolder.SelectedItem = ItemFolder.ItemsSource?.Cast<FolderInfo>().FirstOrDefault(f => f.Id == draft?.FolderId);
            UpdateSaveState();
            if (draft is null) return;
            KindCaption.Text = draft.Kind == ItemKind.Text ? T("Файлы") : T("Записи");
            LoginFields.Visibility = draft.Kind == ItemKind.Login ? Visibility.Visible : Visibility.Collapsed;
            TextCaption.Text = draft.Kind == ItemKind.Text ? T("СОДЕРЖИМОЕ ФАЙЛА") : T("ЗАМЕТКА");
            TextField.MinHeight = draft.Kind == ItemKind.Text ? 280 : 92;
        }
        finally { updating = false; }
    }

    private void UpdateSaveState()
    {
        SaveButton.IsEnabled = SaveVersionButton.IsEnabled = NeedsSaveDecision;
        SaveState.Text = T(NeedsSaveDecision ? "Есть несохранённые изменения" : "Сохранено");
        SaveState.Foreground = (Brush)FindResource(NeedsSaveDecision ? "PendingSaveForeground" : "Green");
        SaveStateBadge.Background = NeedsSaveDecision ? (Brush)FindResource("PendingSaveBackground") : Brushes.Transparent;
        RevertButton.Visibility = Dirty ? Visibility.Visible : Visibility.Collapsed;
        RevertButton.IsEnabled = Dirty;
        RevertButton.Content = T(original is null ? "Закрыть черновик" : "Отменить изменения");
        if (draft is not null)
            EditorTitle.Text = string.IsNullOrWhiteSpace(draft.Title) ? T(draft.DisplayTitle) : draft.Title;
    }

    private void FieldChanged(object sender, TextChangedEventArgs e) => ReadDraft();
    private void PasswordChanged(object sender, RoutedEventArgs e) => ReadDraft();
    private void ItemFolderChanged(object sender, SelectionChangedEventArgs e) => ReadDraft();
    private void VisiblePasswordChanged(object sender, TextChangedEventArgs e)
    {
        if (updating || VisiblePassword.Visibility != Visibility.Visible) return;
        updating = true; PasswordField.Password = VisiblePassword.Text; updating = false; ReadDraft();
    }
    private void ReadDraft()
    {
        if (updating || busy || draft is null || session is not { } vault) return;
        string previousFolder = draft.FolderId;
        bool wasPending = pendingItems.ContainsKey(draft.Id);
        draft = draft with { Title = TitleField.Text, Login = LoginField.Text, Password = PasswordField.Password,
            Site = SiteField.Text, Text = TextField.Text, FolderId = (ItemFolder.SelectedItem as FolderInfo)?.Id ?? draft.FolderId };
        TrackDraft();
        if (draft.FolderId != previousFolder || (original is null && wasPending != pendingItems.ContainsKey(draft.Id)))
            RefreshFolderCounts(GetVisibleItems(vault));
        UpdateSaveState();
        UpdateDeleteFolderState();
    }

    private void TrackDraft()
    {
        if (draft is null) return;
        if (Dirty && (original is not null || draft.HasContent))
            pendingItems[draft.Id] = (draft, original);
        else
            pendingItems.Remove(draft.Id);
    }

    private Task<bool> SaveChanges() => SaveCore(newVersion: false);

    /// <summary>Writes all pending changes into the next {name}_{date}_v{N}.kdbx and switches to it.</summary>
    private Task<bool> SaveNewVersionFile() => SaveCore(newVersion: true);

    private async void SaveVersionClick(object sender, RoutedEventArgs e) => await SaveNewVersionFile();

    private async Task<bool> SaveCore(bool newVersion)
    {
        if (busy || session is not { } vault) return false;
        if (!NeedsSaveDecision) return true;
        var saving = pendingItems.Values.Select(pending => pending.Item).ToArray();
        if (!await Run(T("Шифрование и безопасное сохранение…"), () =>
            { if (newVersion) vault.SaveNewVersion(saving); else vault.SaveItems(saving); })) return false;
        if (draft is not null && pendingItems.ContainsKey(draft.Id)) original = draft;
        pendingItems.Clear();
        RefreshLists();
        UpdateSaveState();
        Status.Text = T("Хранилище открыто");
        return true;
    }

    private async Task<bool> MayLeave()
    {
        if (busy) return false;
        if (!NeedsSaveDecision) return true;
        var answer = MessageBox.Show(this, T("Да — сохранить. Нет — отбросить. Отмена — остаться."), T("Сохранить изменения?"), MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return answer == MessageBoxResult.No || (answer == MessageBoxResult.Yes && await SaveChanges());
    }

    private void FolderChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (updating || session is null || e.NewValue is not TreeViewItem { Tag: FolderInfo folder }) return;
        SelectFolder(folder.Id);
    }

    private void AllFoldersClick(object sender, RoutedEventArgs e) => SelectFolder("");

    private void SelectFolder(string id)
    {
        if (session is null || busy || id == folderId) return;
        folderId = id;
        draft = original = null;
        RefreshLists(); ShowDraft();
    }
    private void SearchChanged(object sender, TextChangedEventArgs e)
    {
        bool active = !string.IsNullOrWhiteSpace(Search.Text);
        ClearSearchButton.Visibility = Search.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (active)
        {
            Search.Background = (Brush)FindResource("ActiveSearchBackground");
            Search.BorderBrush = (Brush)FindResource("Green");
        }
        else
        {
            Search.ClearValue(BackgroundProperty);
            Search.ClearValue(BorderBrushProperty);
            SearchNoResults.Visibility = Visibility.Collapsed;
        }
        if (!updating && !busy) RefreshLists();
    }
    private void ClearSearchClick(object sender, RoutedEventArgs e)
    {
        Search.Clear();
        Search.Focus();
    }
    private void ItemChanged(object sender, SelectionChangedEventArgs e)
    {
        if (updating || busy || session is null || ItemList.SelectedItem is not VaultItem item || item.Id == draft?.Id) return;
        LoadDraft(session, item.Id);
        RefreshLists(); ShowDraft();
    }

    private void LoadDraft(VaultSession vault, string itemId)
    {
        if (pendingItems.TryGetValue(itemId, out var pending))
            (draft, original) = pending;
        else
            draft = original = vault.GetItem(itemId);
    }
    private void SetCategory(ItemKind? kind)
    {
        if (busy || category == kind) return;
        category = kind; draft = original = null; RefreshLists(); ShowDraft();
    }
    private void AllCategoryClick(object sender, RoutedEventArgs e) => SetCategory(null);
    private void LoginCategoryClick(object sender, RoutedEventArgs e) => SetCategory(ItemKind.Login);
    private void TextCategoryClick(object sender, RoutedEventArgs e) => SetCategory(ItemKind.Text);
    private void Add(ItemKind kind)
    {
        if (session is null || busy) return;
        if (category is not null) category = kind;
        original = null;
        string targetFolder = folderId.Length > 0 ? folderId : session.Folders.Skip(1).FirstOrDefault()?.Id ?? session.Folders[0].Id;
        draft = new(Convert.ToHexString(RandomNumberGenerator.GetBytes(16)), targetFolder, kind, "");
        updating = true; Search.Clear(); updating = false; RefreshLists(); ShowDraft(); TitleField.Focus();
    }
    private void AddLoginClick(object sender, RoutedEventArgs e) => Add(ItemKind.Login);
    private void AddTextClick(object sender, RoutedEventArgs e) => Add(ItemKind.Text);
    private async void SaveClick(object sender, RoutedEventArgs e) => await SaveChanges();
    private void RevertClick(object sender, RoutedEventArgs e)
    {
        if (draft is not null) pendingItems.Remove(draft.Id);
        draft = original;
        RefreshLists();
        ShowDraft();
        Status.Text = T("Изменения отменены");
    }
    private void ShowPasswordClick(object sender, RoutedEventArgs e)
    {
        updating = true;
        bool show = VisiblePassword.Visibility != Visibility.Visible;
        if (show) VisiblePassword.Text = PasswordField.Password; else VisiblePassword.Clear();
        VisiblePassword.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        PasswordField.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        ShowPassword.Content = show ? T("Скрыть") : T("Показать"); updating = false;
    }

    private async void DeleteItemClick(object sender, RoutedEventArgs e)
    {
        if (session is null || draft is null) return;
        if (MessageBox.Show(this, T("Удалить выбранный элемент?"), T("Удалить элемент"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        if (original is not null && !await Run(T("Сохранение…"), () => session.DeleteItem(draft.Id))) return;
        pendingItems.Remove(draft.Id);
        draft = original = null; RefreshLists(); ShowDraft(); Status.Text = T("Элемент удалён");
    }
    private async void AddFolderClick(object sender, RoutedEventArgs e)
    {
        if (session is null || busy) return;
        string? name = Prompt(T("Новая папка"), T("Название папки")); if (name is null) return;
        string parentId = folderId.Length == 0 ? session.Folders[0].Id : folderId;
        if (!await Run(T("Сохранение…"), () => session.AddFolder(name, parentId))) return;
        draft = original = null; RefreshLists(); ShowDraft(); Status.Text = T("Папка сохранена");
        folderNodes[parentId].IsExpanded = true;
    }
    private async void RenameFolderClick(object sender, RoutedEventArgs e)
    {
        if (session is null || folderId.Length == 0 || busy) return;
        string? name = Prompt(T("Переименовать"), T("Новое название"), initial: session.Folders.First(f => f.Id == folderId).DisplayName);
        if (name is null || !await Run(T("Сохранение…"), () => session.RenameFolder(folderId, name))) return;
        draft = original = null; RefreshLists(); ShowDraft(); Status.Text = T("Папка сохранена");
    }
    private void UpdateDeleteFolderState()
    {
        string? reason;
        if (session is null || folderId.Length == 0)
            reason = "Выберите папку в дереве.";
        else if (folderId == displayedFolders[0].Id)
            reason = "Корневую папку удалить нельзя.";
        else if (!session.IsFolderEmpty(folderId))
            reason = "В папке есть элементы или подпапки. Поиск и фильтры могут скрывать элементы.";
        else if (pendingItems.Values.Any(pending => pending.Item.FolderId == folderId))
            reason = "Сначала сохраните или отмените черновики в этой папке.";
        else
            reason = null;

        DeleteFolderButton.IsEnabled = reason is null;
        DeleteFolderButton.ToolTip = reason is null ? null : T(reason);
    }

    private async void DeleteFolderClick(object sender, RoutedEventArgs e)
    {
        if (session is null || busy || !DeleteFolderButton.IsEnabled) return;
        if (MessageBox.Show(this, T("Удалить пустую папку?"), T("Удалить пустую папку"), MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        if (!await Run(T("Сохранение…"), () => session.DeleteFolder(folderId))) return;
        folderId = ""; draft = original = null; RefreshLists(); ShowDraft(); Status.Text = T("Папка удалена");
    }
    private async Task<bool> LockVault()
    {
        if (session is null) return true;
        if (!await MayLeave()) return false;
        session.Dispose(); session = null; draft = original = null;
        pendingItems.Clear();
        updating = true;
        FolderList.Items.Clear();
        folderNodes.Clear();
        displayedFolders = [];
        ItemList.ItemsSource = ItemFolder.ItemsSource = null;
        Search.Clear();
        updating = false;
        ShowDraft(); Workspace.Visibility = Visibility.Collapsed; Welcome.Visibility = Visibility.Visible;
        VaultName.Text = T("Локальное хранилище"); VaultName.ToolTip = null; LockButton.IsEnabled = false;
        LockButton.Visibility = Visibility.Collapsed;
        VersionActions.Visibility = Visibility.Collapsed;
        SaveStateBadge.Visibility = Visibility.Collapsed;
        ExportButton.Visibility = Visibility.Collapsed;
        UpdateVaultHeader();
        Status.Text = T("Хранилище закрыто"); return true;
    }
    private async void LockClick(object sender, RoutedEventArgs e) => await LockVault();
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (allowClose) return;
        if (busy)
        {
            e.Cancel = true;
            Status.Text = T("Дождитесь завершения операции.");
            return;
        }
        if (!closePending && !NeedsSaveDecision) return;

        e.Cancel = true;
        if (closePending) return;
        closePending = true;
        try
        {
            // Let WPF finish the canceled close before showing a dialog or calling Close again.
            await System.Windows.Threading.Dispatcher.Yield();
            if (!await MayLeave()) return;
            allowClose = true;
            Close();
        }
        finally { closePending = false; }
    }

    protected override void OnClosed(EventArgs e)
    {
        session?.Dispose();
        session = null;
        draft = original = null;
        pendingItems.Clear();
        base.OnClosed(e);
    }
}
