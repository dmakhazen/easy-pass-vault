using System.ComponentModel;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
    private sealed record FolderHeader(string Name, int Count, double Indent);
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
        markupView = new TextMarkupView(TextField, TextUnder, TextOver);
        InitAttachments();
        for (int i = 0; i < Languages.Length; i++)
        {
            int index = i;
            var item = new MenuItem { Header = Languages[i].Name };
            item.Click += (_, _) => { if (!busy) SetLanguage(index); };
            LanguageMenu.Items.Add(item);
        }
        RestoreWindowLayout();
        SetLanguage(0);
        updating = false;
        PreviewKeyDown += async (_, e) =>
        {
            if (busy || Keyboard.Modifiers != ModifierKeys.Control) return;
            if (e.Key == Key.S) { e.Handled = true; await SaveChanges(); }
            if (e.Key == Key.L) { e.Handled = true; await LockVault(); }
            if (e.Key == Key.K && session is not null)
            {
                e.Handled = true;
                if (PaletteOpen) ClosePalette(); else OpenPalette();
            }
        };
        Loaded += (_, _) => FocusWelcomePassword();
    }

    private async Task<bool> Run(string message, Action action)
    {
        if (busy) return false;
        busy = true;
        Workspace.IsEnabled = Welcome.IsEnabled = HeaderActions.IsEnabled = false;
        Status.Text = message;
        ShowBusy(message);
        try { await Task.Run(action); return true; }
        catch (VaultException ex) { ShowError(TranslateError(ex.Message)); return false; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        { ShowError(TranslateError("Операция не выполнена. Проверьте доступ к файлу, свободное место и доступность носителя. Исходное хранилище не перезаписывалось после ошибки чтения.")); return false; }
        catch (Exception)
        { ShowError(TranslateError("Операция не выполнена из-за внутренней ошибки. Черновик оставлен в редакторе. Подробности, содержащие секреты, не записываются в журнал.")); return false; }
        finally
        {
            busy = false;
            ShowBusy(null);
            Workspace.IsEnabled = Welcome.IsEnabled = HeaderActions.IsEnabled = true;
            LockButton.IsEnabled = session is not null;
            UpdateVaultHeader();
        }
    }

    /// <summary>The spinner over the whole window while work runs off the UI thread; null hides it.</summary>
    private void ShowBusy(string? message)
    {
        BusyOverlay.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
        BusyText.Text = message ?? "";
        BusyRotation.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, message is null ? null
            : new System.Windows.Media.Animation.DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9))
                { RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever });
    }

    private void ShowError(string message)
    { ShowBusy(null); Status.Text = T("Операция не выполнена."); MessageBox.Show(this, message, "easy-pass-vault", MessageBoxButton.OK, MessageBoxImage.Warning); }

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
        if (!await OpenVault(dialog.FileName, password)) UpdateWelcome();
    }

    /// <summary>Opens a vault file; on success it becomes the first of the recent files.</summary>
    private async Task<bool> OpenVault(string path, string password)
    {
        VaultSession? opened = null;
        string? newer = null;
        if (!await Run(T("Открываю…"), () =>
            { opened = VaultSession.Open(path, password); newer = opened.NewerVersionPath; })) return false;
        session = opened; EnterWorkspace();
        if (newer is not null)
            MessageBox.Show(this, T("Это не последняя версия хранилища. В папке есть более новая: {0}.")
                .Replace("{0}", Path.GetFileName(newer)), "easy-pass-vault", MessageBoxButton.OK, MessageBoxImage.Warning);
        return true;
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
        if (await Run(T("Сохраняю и шифрую…"), () => created = VaultSession.Create(path, password)))
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
        NewFileMenuItem.Visibility = ExportMenuItem.Visibility = VersionsMenuItem.Visibility = MenuSeparator.Visibility =
            QuickSearchButton.Visibility = Visibility.Visible;
        welcomePath = null;
        RecentPassword.Clear();
        if (session is not null) RememberRecent(session.FilePath);
        UpdateVaultHeader();
        RefreshLists(); ShowDraft(); Status.Text = T("Хранилище открыто");
    }

    private void UpdateVaultHeader()
    {
        string fileName = session is null ? T("Локальное хранилище") : Path.GetFileName(session.FilePath);
        // The compact header has room only for "name · vN"; the full path stays in the tooltip.
        if (session is not null && compact && VaultVersions.Parse(session.FilePath) is { Number: > 0 } current)
            fileName = $"{current.BaseName} · v{current.Number}";
        VaultName.Text = fileName;
        VaultName.ToolTip = session?.FilePath;
        string? next = null;
        try { next = session is null ? null : Path.GetFileName(session.NextVersionPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or VaultException) { }
        // TextBlock, not a string: WPF would treat the first "_" of the file name as an access key and hide it.
        string number = next is null ? "…" : VaultVersions.Parse(next).Number.ToString(UiCulture);
        string fullAction = T("Создать новый файл и зашифровать в {0}").Replace("{0}", next ?? "…");
        SaveVersionButton.Content = new TextBlock { Text = T("Новый файл v{0}").Replace("{0}", number) };
        SaveVersionButton.ToolTip = fullAction;
        System.Windows.Automation.AutomationProperties.SetName(SaveVersionButton, fullAction);
        NewFileMenuItem.Header = new TextBlock { Text = fullAction };
        ExportMenuItem.Header = T("Экспорт в текст…");
        SaveButton.ToolTip = T("Сохранить и зашифровать") + " (Ctrl+S)";
        LockButton.ToolTip = compact ? T("Закрыть хранилище") + " (Ctrl+L)" : "Ctrl+L";
        UpdateStatusLine();
    }

    // The compact header is narrow: time only for today, date only for earlier days.
    private string ShortTime(DateTime local) => local.ToString(local.Date == DateTime.Today ? "t" : "d", UiCulture);

    /// <summary>Second header line: the last save time, or the number of unsaved edits in amber.</summary>
    private void UpdateStatusLine()
    {
        SaveState.Text = session is null ? ""
            : NeedsSaveDecision ? T("Не сохранено правок: {0}").Replace("{0}", pendingItems.Count.ToString(UiCulture))
            : session.LastSavedAt is not { } savedAt ? T("Дата сохранения неизвестна")
            : compact ? T("Сохранено") + " · " + ShortTime(savedAt.ToLocalTime().DateTime)
            : T("Последнее сохранение: {0}").Replace("{0}", savedAt.ToLocalTime().ToString("g", UiCulture));
        // The size of the encrypted file on disk, so a growing vault is noticed early.
        if (session is not null) SaveState.Text += " · " + VersionsWindow.FormatSize(session.FileSize, T, UiCulture);
        UpdateVaultSpace();
        SaveState.ToolTip = SaveState.Text.Length == 0 ? null : SaveState.Text;
        SaveState.Foreground = (Brush)FindResource(NeedsSaveDecision ? "PendingSaveForeground" : "Secondary");
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
            // Display order only: the vault itself keeps its own order and is not touched by sorting.
            var vaultFolders = VaultSorting.SortFolders(session.Folders, layout.FolderSort, UiCulture).ToArray();
            vaultFolders[0] = vaultFolders[0] with { Name = T("Корень хранилища") };
            RefreshFolderTree(vaultFolders);
            var pickerFolders = vaultFolders.Prepend(new FolderInfo("", T("Все папки"))).ToArray();
            CompactFolderPicker.ItemsSource = pickerFolders;
            CompactFolderPicker.SelectedItem = pickerFolders.First(f => f.Id == folderId);
            ItemFolder.ItemsSource = vaultFolders;
            if (draft is not null) ItemFolder.SelectedItem = vaultFolders.FirstOrDefault(f => f.Id == draft.FolderId);
            string query = Search.Text.Trim();
            var visibleItems = GetVisibleItems(session);
            RefreshFolderCounts(visibleItems);
            // A query looks through every folder: the selected one applies only to the plain list.
            bool searching = query.Length > 0;
            var matches = visibleItems.Where(x => (searching || folderId.Length == 0 || x.FolderId == folderId) &&
                new[] { x.Title, x.Login, x.Site, x.Text }.Any(s => s.Contains(query, StringComparison.OrdinalIgnoreCase))).ToList();
            var items = VaultSorting.SortItems(category is null ? matches : matches.Where(x => x.Kind == category),
                layout.ItemSort, UiCulture, item => string.IsNullOrWhiteSpace(item.Title) ? T(item.DisplayTitle) : item.Title);
            SearchNoResults.Visibility = query.Length > 0 && items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            FolderTitle.Text = searching ? T("Найдено {0} · во всех папках").Replace("{0}", items.Count.ToString(UiCulture))
                : folderId.Length == 0 ? T("Все папки") : ((FolderInfo)folderNodes[folderId].Tag).Name;
            FolderList.Opacity = AllFoldersButton.Opacity = searching ? 0.55 : 1;
            var rowTexts = (ItemTitleConverter)Resources["ItemTitleConverter"];
            rowTexts.SearchPath = searching ? item => FolderPathText(item.FolderId) : null;
            rowTexts.SearchQuery = query;
            ItemList.ItemsSource = items;
            ItemList.SelectedItem = items.FirstOrDefault(x => x.Id == draft?.Id);
            AllCategory.Content = T("Все") + $" {matches.Count}";
            LoginCategory.Content = T("Записи") + $" {matches.Count(x => x.Kind == ItemKind.Login)}";
            TextCategory.Content = T("Файлы") + $" {matches.Count(x => x.Kind == ItemKind.Text)}";
            AllCategory.Style = (Style)FindResource(category is null ? "SegmentActive" : "Segment");
            LoginCategory.Style = (Style)FindResource(category == ItemKind.Login ? "SegmentActive" : "Segment");
            TextCategory.Style = (Style)FindResource(category == ItemKind.Text ? "SegmentActive" : "Segment");
            UpdateFolderActions();
        }
        finally { updating = false; }
    }

    private VaultItem[] GetVisibleItems(VaultSession vault) =>
        vault.Items.Select(item => pendingItems.TryGetValue(item.Id, out var pending)
            ? pending.Item.WithoutPassword()
            : item).Concat(pendingItems.Values.Where(pending => pending.Original is null)
                .Select(pending => pending.Item.WithoutPassword())).ToArray();

    private void RefreshFolderCounts(IReadOnlyList<VaultItem> items)
    {
        var counts = items.GroupBy(item => item.FolderId).ToDictionary(group => group.Key, group => group.Count());
        foreach (var (id, node) in folderNodes)
        {
            var folder = (FolderInfo)node.Tag;
            int depth = 0;
            for (var parent = folder.ParentId; parent is not null; parent = ((FolderInfo)folderNodes[parent].Tag).ParentId) depth++;
            var header = new FolderHeader(folder.DisplayName, counts.GetValueOrDefault(id), depth * 16);
            if (!header.Equals(node.Header)) node.Header = header;
        }
        AllFoldersCount.Text = items.Count.ToString(UiCulture);
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
        AllFoldersButton.Tag = folderId.Length == 0 ? "Selected" : null;
        AllFoldersName.FontWeight = folderId.Length == 0 ? FontWeights.SemiBold : FontWeights.Normal;
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
            ApplyAreas();
            Editor.Visibility = draft is null ? Visibility.Collapsed : Visibility.Visible;
            EmptyEditor.Visibility = draft is null ? Visibility.Visible : Visibility.Collapsed;
            // Clear controls even when collapsing the editor.
            TitleField.Text = draft?.Title ?? ""; LoginField.Text = draft?.Login ?? "";
            PasswordField.Password = draft?.Password ?? ""; VisiblePassword.Clear();
            PasswordField.Visibility = Visibility.Visible; VisiblePassword.Visibility = Visibility.Collapsed; ShowPassword.Content = T("Показать");
            SiteField.Text = draft?.Site ?? ""; TextField.Text = draft?.Text ?? "";
            ItemFolder.SelectedItem = ItemFolder.ItemsSource?.Cast<FolderInfo>().FirstOrDefault(f => f.Id == draft?.FolderId);
            UpdateSaveState();
            ShowAttachments();
            jsonView = false; UpdateTextView();
            if (draft is null) return;
            LoginFields.Visibility = draft.Kind == ItemKind.Login ? Visibility.Visible : Visibility.Collapsed;
            TextCaption.Text = draft.Kind == ItemKind.Text ? T("Содержимое файла") : T("Заметка");
            TextField.MinHeight = draft.Kind == ItemKind.Text ? 280 : 92;
        }
        finally { updating = false; UpdateSearchMarks(center: false); }
    }

    private void UpdateSaveState()
    {
        SaveButton.IsEnabled = SaveVersionButton.IsEnabled = NeedsSaveDecision;
        NewFileMenuItem.IsEnabled = NeedsSaveDecision;
        UpdateStatusLine();
        RevertButton.Visibility = Dirty ? Visibility.Visible : Visibility.Collapsed;
        RevertButton.IsEnabled = Dirty;
        RevertButton.Content = T(original is null ? "Закрыть черновик" : "Отменить изменения");
        UpdateCopyState();
        UpdateCrumbs();
        if (draft is null) return;
        EditorTitle.Text = string.IsNullOrWhiteSpace(draft.Title) ? T(draft.DisplayTitle) : draft.Title;
        KindCaption.Text = draft.Kind == ItemKind.Text ? T("Файлы") : T("Записи"); // The folder is in the path above.
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
        UpdateFolderActions();
        UpdateTextView();
        UpdateSearchMarks(center: false);
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
        if (!await Run(T("Сохраняю и шифрую…"), () =>
            { if (newVersion) vault.SaveNewVersion(saving); else vault.SaveItems(saving); })) return false;
        if (draft is not null && pendingItems.ContainsKey(draft.Id)) original = draft;
        pendingItems.Clear();
        if (newVersion) RememberRecent(vault.FilePath);
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
        if (session is null || busy || (id == folderId && !Searching)) return;
        // Choosing a folder leaves the search: the list shows that folder again.
        if (Searching) { updating = true; Search.Clear(); updating = false; }
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
        searchHit = 0;
        UpdateSearchMarks(center: true);
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
        if (original is not null && !await Run(T("Сохраняю и шифрую…"), () => session.DeleteItem(draft.Id))) return;
        pendingItems.Remove(draft.Id);
        draft = original = null; RefreshLists(); ShowDraft(); Status.Text = T("Элемент удалён");
    }
    private async void AddFolderClick(object sender, RoutedEventArgs e)
    {
        if (session is null || busy) return;
        string? name = Prompt(T("Новая папка"), T("Название папки")); if (name is null) return;
        string parentId = folderId.Length == 0 ? session.Folders[0].Id : folderId;
        if (!await Run(T("Сохраняю и шифрую…"), () => session.AddFolder(name, parentId))) return;
        draft = original = null; RefreshLists(); ShowDraft(); Status.Text = T("Папка сохранена");
        folderNodes[parentId].IsExpanded = true;
    }
    private async void RenameFolderClick(object sender, RoutedEventArgs e)
    {
        // The root is always shown as "Vault root": renaming it would change nothing on screen.
        if (session is null || folderId.Length == 0 || folderId == displayedFolders[0].Id || busy) return;
        string? name = Prompt(T("Переименовать"), T("Новое название"), initial: session.Folders.First(f => f.Id == folderId).DisplayName);
        if (name is null || !await Run(T("Сохраняю и шифрую…"), () => session.RenameFolder(folderId, name))) return;
        draft = original = null; RefreshLists(); ShowDraft(); Status.Text = T("Папка сохранена");
    }
    /// <summary>Why the selected folder cannot be deleted right now, or null when it can.</summary>
    private string? FolderDeleteBlocker() =>
        session is null || folderId.Length == 0 ? "Выберите папку в дереве."
        : folderId == displayedFolders[0].Id ? "Корневую папку удалить нельзя."
        : !session.IsFolderEmpty(folderId) ? "В папке есть элементы или подпапки. Поиск и фильтры могут скрывать элементы."
        : pendingItems.Values.Any(pending => pending.Item.FolderId == folderId) ? "Сначала сохраните или отмените черновики в этой папке."
        : null;

    /// <summary>Keeps the folder menu in step with the selected folder: what can be renamed and deleted.</summary>
    private void UpdateFolderActions()
    {
        string? reason = FolderDeleteBlocker();
        FolderMenuDelete.IsEnabled = reason is null;
        FolderMenuDelete.ToolTip = reason is null ? null : T(reason);
        FolderMenuAdd.IsEnabled = session is not null && folderId.Length > 0;
        FolderMenuRename.IsEnabled = session is not null && folderId.Length > 0 && folderId != displayedFolders[0].Id;
    }

    private void OpenFolderMenu(UIElement target, PlacementMode placement)
    {
        if (busy || session is null || folderId.Length == 0) return;
        UpdateFolderActions();
        FolderMenu.PlacementTarget = target;
        FolderMenu.Placement = placement;
        FolderMenu.IsOpen = true;
    }

    /// <summary>Right click (or the menu key) on a folder: select it first, then show its menu.</summary>
    private void FolderMenuOpening(object sender, ContextMenuEventArgs e)
    {
        e.Handled = true; // The menu is opened below, never by WPF itself: empty space of the tree has no menu.
        bool keyboard = e.CursorLeft < 0 && e.CursorTop < 0;
        TreeViewItem? node = FolderList.SelectedItem as TreeViewItem;
        if (!keyboard)
        {
            var source = e.OriginalSource as DependencyObject;
            while (source is not null and not TreeViewItem)
                source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
            node = source as TreeViewItem;
        }
        if (busy || node?.Tag is not FolderInfo folder) return;
        if (!node.IsSelected) node.IsSelected = true;
        if (folderId != folder.Id) return;
        if (keyboard) OpenFolderMenu(node, PlacementMode.Bottom); else OpenFolderMenu(FolderList, PlacementMode.MousePoint);
    }

    private void FolderMoreClick(object sender, RoutedEventArgs e) => OpenFolderMenu((UIElement)sender, PlacementMode.Bottom);

    private void FolderTreeKeyDown(object sender, KeyEventArgs e)
    {
        if (busy || session is null || folderId.Length == 0 || Keyboard.Modifiers != ModifierKeys.None) return;
        if (e.Key == Key.F2) { e.Handled = true; RenameFolderClick(sender, e); }
        else if (e.Key == Key.Delete) { e.Handled = true; DeleteFolderClick(sender, e); }
    }

    private async void DeleteFolderClick(object sender, RoutedEventArgs e)
    {
        if (session is null || busy) return;
        // The same checks for the menu item and for the Delete key; the key explains why nothing happened.
        if (FolderDeleteBlocker() is { } reason) { Status.Text = T(reason); return; }
        if (MessageBox.Show(this, T("Удалить пустую папку?"), T("Удалить папку"), MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        if (!await Run(T("Сохраняю и шифрую…"), () => session.DeleteFolder(folderId))) return;
        folderId = ""; draft = original = null; RefreshLists(); ShowDraft(); Status.Text = T("Папка удалена");
    }
    private async Task<bool> LockVault()
    {
        if (session is null) return true;
        if (!await MayLeave()) return false;
        ClosePalette();
        session.Dispose(); session = null; draft = original = null;
        pendingItems.Clear();
        updating = true;
        FolderList.Items.Clear();
        folderNodes.Clear();
        displayedFolders = [];
        ItemList.ItemsSource = ItemFolder.ItemsSource = CompactFolderPicker.ItemsSource = null;
        Search.Clear();
        updating = false;
        ShowDraft(); Workspace.Visibility = Visibility.Collapsed; Welcome.Visibility = Visibility.Visible;
        VaultName.Text = T("Локальное хранилище"); VaultName.ToolTip = null; LockButton.IsEnabled = false;
        LockButton.Visibility = Visibility.Collapsed;
        VersionActions.Visibility = Visibility.Collapsed;
        NewFileMenuItem.Visibility = ExportMenuItem.Visibility = VersionsMenuItem.Visibility = MenuSeparator.Visibility =
            QuickSearchButton.Visibility = Visibility.Collapsed;
        UpdateVaultHeader();
        UpdateWelcome();
        FocusWelcomePassword();
        Status.Text = T("Хранилище закрыто"); return true;
    }
    private async void LockClick(object sender, RoutedEventArgs e) => await LockVault();
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (allowClose) { RememberWindowLayout(); return; }
        if (busy)
        {
            e.Cancel = true;
            Status.Text = T("Дождитесь завершения операции.");
            return;
        }
        if (!closePending && !NeedsSaveDecision) { RememberWindowLayout(); return; }

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
        SaveWindowLayout();
        base.OnClosed(e);
    }
}
