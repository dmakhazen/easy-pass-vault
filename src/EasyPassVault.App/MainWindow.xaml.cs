using System.ComponentModel;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EasyPassVault.Core;
using Microsoft.Win32;

namespace EasyPassVault.App;

public partial class MainWindow : Window
{
    private VaultSession? session;
    private VaultItem? draft;
    private VaultItem? original;
    private string folderId = "";
    private ItemKind category;
    private bool updating = true;
    private bool busy;
    private bool allowClose;
    private bool Dirty => draft is not null && draft != original;

    public MainWindow()
    {
        InitializeComponent(); updating = false;
        PreviewKeyDown += async (_, e) =>
        {
            if (busy || Keyboard.Modifiers != ModifierKeys.Control) return;
            if (e.Key == Key.S) { e.Handled = true; await SaveDraft(); }
            if (e.Key == Key.L) { e.Handled = true; await LockVault(); }
        };
    }

    private async Task<bool> Run(string message, Action action)
    {
        if (busy) return false;
        busy = true; Workspace.IsEnabled = Welcome.IsEnabled = LockButton.IsEnabled = false;
        Status.Text = message;
        try { await Task.Run(action); return true; }
        catch (VaultException ex) { ShowError(ex.Message); return false; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        { ShowError("Операция не выполнена. Проверьте доступ к файлу, свободное место и доступность носителя. Исходное хранилище не перезаписывалось после ошибки чтения."); return false; }
        catch (Exception)
        { ShowError("Операция не выполнена из-за внутренней ошибки. Черновик оставлен в редакторе. Подробности, содержащие секреты, не записываются в журнал."); return false; }
        finally { busy = false; Workspace.IsEnabled = Welcome.IsEnabled = true; LockButton.IsEnabled = session is not null; }
    }

    private void ShowError(string message)
    { Status.Text = "Ошибка · изменения не сохранены"; MessageBox.Show(this, message, "easy-pass-vault", MessageBoxButton.OK, MessageBoxImage.Warning); }

    private string? Prompt(string title, string label, bool secret = false, bool confirm = false, string initial = "")
    {
        var window = new PromptWindow(this, title, label, secret, confirm, initial);
        bool? accepted = window.ShowDialog();
        string? value = accepted == true ? window.Value : null;
        window.Clear(); return value;
    }

    private async void OpenClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Хранилище KeePass (*.kdbx)|*.kdbx", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        string? password = Prompt("Открытие хранилища", "Введите главный пароль", secret: true);
        if (password is null) return;
        VaultSession? opened = null;
        if (await Run("Открытие и проверка целостности…", () => opened = VaultSession.Open(dialog.FileName, password)))
        { session = opened; EnterWorkspace(); }
    }

    private async void CreateClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "Хранилище KeePass (*.kdbx)|*.kdbx", DefaultExt = ".kdbx", FileName = "Личное.kdbx", OverwritePrompt = true };
        if (dialog.ShowDialog(this) != true) return;
        if (File.Exists(dialog.FileName)) { ShowError("Выберите новое имя файла. Создание не перезаписывает существующие хранилища."); return; }
        string? password = Prompt("Новое хранилище", "Придумайте главный пароль от 12 символов. Восстановить забытый пароль нельзя.", true, true);
        if (password is null) return;
        VaultSession? created = null;
        if (await Run("Создание зашифрованного хранилища…", () => created = VaultSession.Create(dialog.FileName, password)))
        { session = created; EnterWorkspace(); }
    }

    private void EnterWorkspace()
    {
        folderId = ""; category = ItemKind.Login; draft = original = null;
        Welcome.Visibility = Visibility.Collapsed; Workspace.Visibility = Visibility.Visible; LockButton.IsEnabled = true;
        LockButton.Visibility = Visibility.Visible;
        VaultName.Text = Path.GetFileName(session!.FilePath); VaultName.ToolTip = session.FilePath;
        RefreshLists(); ShowDraft(); Status.Text = "Хранилище открыто · KDBX / Argon2id · сохранение по кнопке или Ctrl+S";
    }

    private void RefreshLists()
    {
        if (session is null) return;
        updating = true;
        try
        {
            var folders = new List<FolderInfo> { new("", "Все папки") };
            folders.AddRange(session.Folders);
            FolderList.ItemsSource = folders;
            FolderList.SelectedItem = folders.FirstOrDefault(f => f.Id == folderId) ?? folders[0];
            FolderTitle.Text = ((FolderInfo)FolderList.SelectedItem).Name;
            ItemFolder.ItemsSource = session.Folders;
            if (draft is not null) ItemFolder.SelectedItem = session.Folders.FirstOrDefault(f => f.Id == draft.FolderId);
            string query = Search.Text.Trim();
            var items = session.Items.Where(x => x.Kind == category && (folderId.Length == 0 || x.FolderId == folderId) &&
                new[] { x.Title, x.Login, x.Site, x.Text }.Any(s => s.Contains(query, StringComparison.OrdinalIgnoreCase))).ToList();
            ItemList.ItemsSource = items;
            ItemList.SelectedItem = items.FirstOrDefault(x => x.Id == draft?.Id);
            ListCaption.Text = $"{(category == ItemKind.Login ? "Записи · логин и пароль" : "Файлы · свободный текст")} · {items.Count}";
            LoginCategory.Style = (Style)FindResource(category == ItemKind.Login ? "Primary" : typeof(Button));
            TextCategory.Style = (Style)FindResource(category == ItemKind.Text ? "Primary" : typeof(Button));
        }
        finally { updating = false; }
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
            PasswordField.Visibility = Visibility.Visible; VisiblePassword.Visibility = Visibility.Collapsed; ShowPassword.Content = "Показать";
            SiteField.Text = draft?.Site ?? ""; TextField.Text = draft?.Text ?? "";
            ItemFolder.SelectedItem = session?.Folders.FirstOrDefault(f => f.Id == draft?.FolderId);
            if (draft is null) return;
            EditorTitle.Text = draft.DisplayTitle;
            KindCaption.Text = draft.Kind == ItemKind.Text ? "ФАЙЛЫ / Текст внутри хранилища" : "ЗАПИСИ / Логин, пароль и сайт";
            LoginFields.Visibility = draft.Kind == ItemKind.Login ? Visibility.Visible : Visibility.Collapsed;
            TextCaption.Text = draft.Kind == ItemKind.Text ? "СОДЕРЖИМОЕ ФАЙЛА" : "ЗАМЕТКА";
            TextField.MinHeight = draft.Kind == ItemKind.Text ? 280 : 150;
            UpdateSaveState();
        }
        finally { updating = false; }
    }

    private void UpdateSaveState()
    {
        if (draft is null) return;
        SaveState.Text = Dirty ? "Есть несохранённые изменения" : "Сохранено в зашифрованный файл";
        if (original is null && !draft.HasContent) SaveState.Text = "Новый черновик · заполните любое поле";
        SaveButton.IsEnabled = Dirty && draft.HasContent;
        EditorTitle.Text = draft.DisplayTitle;
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
        if (updating || busy || draft is null) return;
        draft = draft with { Title = TitleField.Text, Login = LoginField.Text, Password = PasswordField.Password,
            Site = SiteField.Text, Text = TextField.Text, FolderId = (ItemFolder.SelectedItem as FolderInfo)?.Id ?? draft.FolderId };
        UpdateSaveState();
    }

    private async Task<bool> SaveDraft()
    {
        if (busy || session is null || draft is null) return false;
        if (!Dirty) return true;
        if (!draft.HasContent) { ShowError("Заполните хотя бы одно поле."); return false; }
        var saving = draft;
        if (!await Run("Шифрование и безопасное сохранение…", () => session.SaveItem(saving))) return false;
        original = draft; RefreshLists(); UpdateSaveState(); Status.Text = "Сохранено в зашифрованный файл · " + DateTime.Now.ToString("HH:mm:ss"); return true;
    }

    private async Task<bool> MayLeave()
    {
        if (busy) return false;
        if (!Dirty || (original is null && draft?.HasContent != true)) return true;
        var answer = MessageBox.Show(this, "Есть несохранённые изменения.\n\nДа — сохранить в хранилище и продолжить.\nНет — отбросить изменения.\nОтмена — остаться в редакторе.", "Сохранить изменения?", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return answer == MessageBoxResult.No || (answer == MessageBoxResult.Yes && await SaveDraft());
    }

    private async void FolderChanged(object sender, SelectionChangedEventArgs e)
    {
        if (updating || session is null || FolderList.SelectedItem is not FolderInfo folder) return;
        if (await MayLeave()) { folderId = folder.Id; draft = original = null; }
        RefreshLists(); ShowDraft();
    }
    private void SearchChanged(object sender, TextChangedEventArgs e) { if (!updating && !busy) RefreshLists(); }
    private async void ItemChanged(object sender, SelectionChangedEventArgs e)
    {
        if (updating || session is null || ItemList.SelectedItem is not VaultItem item || item.Id == draft?.Id) return;
        if (await MayLeave()) { draft = original = session.GetItem(item.Id); }
        RefreshLists(); ShowDraft();
    }
    private async Task SetCategory(ItemKind kind)
    {
        if (category == kind || !await MayLeave()) return;
        category = kind; draft = original = null; RefreshLists(); ShowDraft();
    }
    private async void LoginCategoryClick(object sender, RoutedEventArgs e) => await SetCategory(ItemKind.Login);
    private async void TextCategoryClick(object sender, RoutedEventArgs e) => await SetCategory(ItemKind.Text);
    private async Task Add(ItemKind kind)
    {
        if (session is null || !await MayLeave()) return;
        category = kind; original = null;
        string targetFolder = folderId.Length > 0 ? folderId : session.Folders.Skip(1).FirstOrDefault()?.Id ?? session.Folders[0].Id;
        draft = new(Convert.ToHexString(RandomNumberGenerator.GetBytes(16)), targetFolder, kind, "");
        updating = true; Search.Clear(); updating = false; RefreshLists(); ShowDraft(); TitleField.Focus();
    }
    private async void AddLoginClick(object sender, RoutedEventArgs e) => await Add(ItemKind.Login);
    private async void AddTextClick(object sender, RoutedEventArgs e) => await Add(ItemKind.Text);
    private async void SaveClick(object sender, RoutedEventArgs e) => await SaveDraft();
    private void RevertClick(object sender, RoutedEventArgs e) { draft = original; RefreshLists(); ShowDraft(); Status.Text = "Изменения отменены"; }
    private void ShowPasswordClick(object sender, RoutedEventArgs e)
    {
        updating = true;
        bool show = VisiblePassword.Visibility != Visibility.Visible;
        if (show) VisiblePassword.Text = PasswordField.Password; else VisiblePassword.Clear();
        VisiblePassword.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        PasswordField.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        ShowPassword.Content = show ? "Скрыть" : "Показать"; updating = false;
    }

    private async void DeleteItemClick(object sender, RoutedEventArgs e)
    {
        if (session is null || draft is null) return;
        if (MessageBox.Show(this, "Удалить выбранный элемент?", "Удаление", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        if (original is not null && !await Run("Сохранение удаления…", () => session.DeleteItem(draft.Id))) return;
        draft = original = null; RefreshLists(); ShowDraft(); Status.Text = "Элемент удалён";
    }
    private async void AddFolderClick(object sender, RoutedEventArgs e)
    {
        if (session is null || !await MayLeave()) return;
        string? name = Prompt("Новая папка", "Название папки"); if (name is null) return;
        if (!await Run("Сохранение папки…", () => session.AddFolder(name))) return;
        draft = original = null; RefreshLists(); ShowDraft(); Status.Text = "Папка сохранена в хранилище";
    }
    private async void RenameFolderClick(object sender, RoutedEventArgs e)
    {
        if (session is null || folderId.Length == 0 || !await MayLeave()) return;
        string? name = Prompt("Переименовать папку", "Новое название", initial: session.Folders.First(f => f.Id == folderId).Name);
        if (name is null || !await Run("Сохранение названия…", () => session.RenameFolder(folderId, name))) return;
        draft = original = null; RefreshLists(); ShowDraft(); Status.Text = "Название папки сохранено";
    }
    private async void DeleteFolderClick(object sender, RoutedEventArgs e)
    {
        if (session is null || folderId.Length == 0 || !await MayLeave()) return;
        if (MessageBox.Show(this, "Удалить пустую папку?", "Удаление папки", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        if (!await Run("Удаление папки…", () => session.DeleteFolder(folderId))) return;
        folderId = ""; draft = original = null; RefreshLists(); ShowDraft(); Status.Text = "Папка удалена";
    }
    private async Task<bool> LockVault()
    {
        if (session is null) return true;
        if (!await MayLeave()) return false;
        session.Dispose(); session = null; draft = original = null;
        updating = true; FolderList.ItemsSource = ItemList.ItemsSource = ItemFolder.ItemsSource = null; Search.Clear(); updating = false;
        ShowDraft(); Workspace.Visibility = Visibility.Collapsed; Welcome.Visibility = Visibility.Visible;
        VaultName.Text = "Локальное хранилище"; VaultName.ToolTip = null; LockButton.IsEnabled = false;
        LockButton.Visibility = Visibility.Collapsed;
        Status.Text = "Хранилище закрыто · для открытия нужен главный пароль"; return true;
    }
    private async void LockClick(object sender, RoutedEventArgs e) => await LockVault();
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (allowClose) return;
        e.Cancel = true;
        if (busy) { Status.Text = "Дождитесь завершения текущей операции перед закрытием."; return; }
        if (!await LockVault()) return;
        allowClose = true; Close();
    }
}
