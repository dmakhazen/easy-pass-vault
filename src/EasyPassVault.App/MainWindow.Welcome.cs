using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using EasyPassVault.Core;

namespace EasyPassVault.App;

// Start screen with the recently opened vault files (IdeaWelcome.dc.html) and the list of versions of the open
// vault (IdeaVersions.dc.html). Of a vault only its full path and the time it was opened are remembered.
public partial class MainWindow
{
    private sealed record RecentRow(string Path, string Name, string Info, string Warning, Brush NameBrush, Brush WarningBrush);

    private static readonly Brush MissingBrush = Frozen("#8A968F");
    private static readonly Brush WarningBrush = Frozen("#754700");
    private static readonly Brush TextBrush = Frozen("#263832");

    /// <summary>The file shown in the start card. Null means the most recently opened one.</summary>
    private string? welcomePath;

    // A field, so the tests can replace it: a test must not start Explorer.
    private Action<string> showInExplorer = path =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = false })?.Dispose();

    private string? WelcomeCardPath => welcomePath ?? layout.RecentVaults.FirstOrDefault()?.Path;

    /// <summary>Puts the vault file first in the recent list and writes the settings file at once.</summary>
    private void RememberRecent(string path)
    {
        layout = layout with { RecentVaults = layout.RecentVaults.Touch(path, DateTime.UtcNow) };
        WindowLayoutStore.Save(layoutPath, layout);
    }

    private (bool Exists, string? Newer) Inspect(string path)
    {
        try
        {
            if (!File.Exists(path)) return (false, null);
            return (true, VaultVersions.FindNewer(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return (File.Exists(path), null);
        }
    }

    private string OpenedText(string path) =>
        layout.RecentVaults.FirstOrDefault(item => RecentVaultList.Same(item.Path, path)) is { } recent
            ? T("открыт {0}").Replace("{0}", recent.OpenedUtc.ToLocalTime().ToString("g", UiCulture)) : "";

    private static string Join(params string?[] parts) => string.Join(" · ", parts.Where(part => !string.IsNullOrEmpty(part)));

    /// <summary>First run: the introduction. Otherwise the card of the last (or chosen) file and the other recent ones.</summary>
    private void UpdateWelcome()
    {
        string? card = WelcomeCardPath;
        bool hasCard = card is not null;
        WelcomeIntro.Visibility = WelcomeSaveHelp.Visibility = hasCard ? Visibility.Collapsed : Visibility.Visible;
        WelcomeRecent.Visibility = hasCard ? Visibility.Visible : Visibility.Collapsed;
        WelcomeButtons.Columns = hasCard ? 2 : 1;
        WelcomeOpenButton.Content = T(hasCard ? "Открыть другой файл…" : "Открыть файл хранилища");
        WelcomeCreateButton.Content = T(hasCard ? "Создать новое хранилище" : "Создать новое…");
        WelcomeOpenButton.Style = (Style)FindResource(hasCard ? typeof(Button) : "Primary");
        WelcomeOpenButton.Margin = hasCard ? new Thickness(0, 0, 4, 0) : new Thickness(0, 0, 0, 8);
        WelcomeCreateButton.Margin = hasCard ? new Thickness(4, 0, 0, 0) : new Thickness(0);
        if (card is null)
        {
            OtherRecentList.ItemsSource = null;
            return;
        }

        var (exists, newer) = Inspect(card);
        int number = VaultVersions.Parse(card).Number;
        RecentName.Text = Path.GetFileName(card);
        RecentName.Foreground = exists ? TextBrush : MissingBrush;
        RecentName.ToolTip = RecentInfo.ToolTip = card;
        // The folder goes last: a long path is cut at the end, the time and the marks stay visible.
        RecentInfo.Text = Join(!exists ? T("файл не найден") : newer is not null ? T("есть версия новее") : number > 0 ? T("самая новая") : null,
            OpenedText(card), Path.GetDirectoryName(card));
        RecentInfo.Foreground = !exists ? MissingBrush : newer is not null ? WarningBrush : (Brush)FindResource("Secondary");
        RecentCard.BorderBrush = exists ? (Brush)FindResource("Green") : Frozen("#DCE3DC");
        RecentLatestBadge.Visibility = exists && layout.RecentVaults.FirstOrDefault() is { } last && RecentVaultList.Same(last.Path, card)
            ? Visibility.Visible : Visibility.Collapsed;
        RecentPasswordRow.Visibility = exists ? Visibility.Visible : Visibility.Collapsed;
        RecentRemoveButton.Visibility = !exists && layout.RecentVaults.Contains(card) ? Visibility.Visible : Visibility.Collapsed;
        if (!exists) RecentPassword.Clear();

        var others = layout.RecentVaults.Where(item => !RecentVaultList.Same(item.Path, card)).Select(item =>
        {
            var (otherExists, otherNewer) = Inspect(item.Path);
            return new RecentRow(item.Path, Path.GetFileName(item.Path), Join(OpenedText(item.Path), Path.GetDirectoryName(item.Path)),
                !otherExists ? T("файл не найден") : otherNewer is not null ? T("есть версия новее") : "",
                otherExists ? TextBrush : MissingBrush, otherExists ? WarningBrush : MissingBrush);
        }).ToList();
        OtherRecentList.ItemsSource = others;
        OtherRecentTitle.Visibility = others.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void FocusWelcomePassword()
    {
        if (session is null && RecentPasswordRow.Visibility == Visibility.Visible && WelcomeRecent.Visibility == Visibility.Visible)
            Dispatcher.BeginInvoke(() => RecentPassword.Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void OtherRecentClick(object sender, RoutedEventArgs e)
    {
        if (busy || sender is not FrameworkElement { DataContext: RecentRow row }) return;
        welcomePath = row.Path;
        RecentPassword.Clear();
        UpdateWelcome();
        FocusWelcomePassword();
    }

    private void RecentRemoveClick(object sender, RoutedEventArgs e)
    {
        if (busy || WelcomeCardPath is not { } card) return;
        layout = layout with { RecentVaults = layout.RecentVaults.Remove(card) };
        WindowLayoutStore.Save(layoutPath, layout);
        welcomePath = null;
        UpdateWelcome();
    }

    private void RecentPasswordChanged(object sender, RoutedEventArgs e) =>
        RecentPasswordHint.Visibility = RecentPassword.SecurePassword.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    private async void RecentPasswordKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None) return;
        e.Handled = true;
        await OpenRecent();
    }

    private async void RecentOpenClick(object sender, RoutedEventArgs e) => await OpenRecent();

    /// <summary>Opens the file of the start card with the typed password. The field is cleared before the attempt.</summary>
    private async Task<bool> OpenRecent()
    {
        if (busy || session is not null || WelcomeCardPath is not { } path) return false;
        string password = RecentPassword.Password;
        RecentPassword.Clear();
        if (password.Length == 0)
        {
            Status.Text = T("Введите главный пароль");
            FocusWelcomePassword();
            return false;
        }
        if (await OpenVault(path, password)) return true;
        UpdateWelcome();
        FocusWelcomePassword();
        return false;
    }

    private async void VersionsClick(object sender, RoutedEventArgs e)
    {
        if (busy || session is not { } vault) return;
        var window = CreateVersionsWindow(vault);
        if (window is null) return;
        window.Owner = this;
        if (window.ShowDialog() != true || window.ChosenPath is not { } chosen) return;
        await OpenOtherVersion(chosen);
    }

    private VersionsWindow? CreateVersionsWindow(VaultSession vault)
    {
        IReadOnlyList<VaultVersions.VersionFile> files;
        string? next = null;
        try
        {
            files = VaultVersions.List(vault.FilePath);
            next = vault.NextVersionPath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or VaultException or OverflowException)
        {
            ShowError(T("Операция не выполнена. Проверьте доступ к файлу, свободное место и доступность носителя. Исходное хранилище не перезаписывалось после ошибки чтения."));
            return null;
        }
        return new VersionsWindow(vault.FilePath, files, next, T, UiCulture, path =>
        {
            try { showInExplorer(path); return true; }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
            { return false; }
        });
    }

    /// <summary>
    /// Another version was chosen: close the open vault (with the usual question about unsaved changes) and show
    /// the start screen with that file in the card. The master password is typed again, never reused.
    /// </summary>
    private async Task<bool> OpenOtherVersion(string path)
    {
        if (!await LockVault()) return false;
        welcomePath = Path.GetFullPath(path);
        RecentPassword.Clear();
        UpdateWelcome();
        FocusWelcomePassword();
        return true;
    }
}
