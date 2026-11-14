using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using EasyPassVault.Core;

namespace EasyPassVault.App;

// One-click copying of the login, password and site, and opening the site in the default browser.
// No timer and no automatic clearing of the clipboard. The copied value is never shown or logged.
public partial class MainWindow
{
    private enum CopyField { Login, Password, Site }

    // Fields, so the tests can replace them: a test must not touch the real clipboard or start a browser.
    private Action<string> writeClipboard = text => Clipboard.SetText(text);
    private Action<Uri> openInBrowser = uri =>
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();

    private void UpdateCopyState()
    {
        var entry = draft is { Kind: ItemKind.Login } ? draft : null;
        CopyLoginButton.IsEnabled = entry is { Login.Length: > 0 };
        CopyPasswordButton.IsEnabled = entry is { Password.Length: > 0 };
        CopySiteButton.IsEnabled = entry is { Site.Length: > 0 };
        bool web = entry is not null && SiteAddress.TryGetWebUri(entry.Site, out _);
        OpenSiteButton.IsEnabled = web;
        OpenSiteButton.ToolTip = web || entry is null || string.IsNullOrWhiteSpace(entry.Site)
            ? null : T("Адрес должен начинаться с http:// или https://");
    }

    /// <summary>The values to copy for a list row: the unsaved draft when there is one, else the saved entry.</summary>
    private VaultItem? CurrentValues(string id) =>
        session is not { } vault ? null
        : draft?.Id == id ? draft
        : pendingItems.TryGetValue(id, out var pending) ? pending.Item
        : vault.GetItem(id);

    /// <param name="reportEmpty">Quick search has no disabled buttons: there an empty field is reported in the status line.</param>
    private void Copy(CopyField field, VaultItem? item, bool reportEmpty = false)
    {
        if (busy || item is null) return;
        string value = field switch { CopyField.Login => item.Login, CopyField.Password => item.Password, _ => item.Site };
        if (value.Length == 0)
        {
            if (reportEmpty && field != CopyField.Site)
                Status.Text = T(field == CopyField.Login ? "У записи нет логина" : "У записи нет пароля");
            return;
        }
        try { writeClipboard(value); }
        // The clipboard is shared: another program may hold it open at this moment.
        catch (Exception ex) when (ex is ExternalException or InvalidOperationException)
        {
            Status.Text = T("Буфер обмена занят другой программой. Повторите копирование.");
            return;
        }
        Status.Text = T(field switch
        {
            CopyField.Login => "Логин скопирован",
            CopyField.Password => "Пароль скопирован",
            _ => "Адрес скопирован"
        });
    }

    private void CopyLoginClick(object sender, RoutedEventArgs e) => Copy(CopyField.Login, draft);
    private void CopyPasswordClick(object sender, RoutedEventArgs e) => Copy(CopyField.Password, draft);
    private void CopySiteClick(object sender, RoutedEventArgs e) => Copy(CopyField.Site, draft);

    private void RowCopyLoginClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: VaultItem item }) Copy(CopyField.Login, CurrentValues(item.Id));
    }

    private void RowCopyPasswordClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: VaultItem item }) Copy(CopyField.Password, CurrentValues(item.Id));
    }

    private void OpenSiteClick(object sender, RoutedEventArgs e)
    {
        if (busy || draft is null || !SiteAddress.TryGetWebUri(draft.Site, out var uri)) return;
        try { openInBrowser(uri); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException
            or System.IO.IOException or PlatformNotSupportedException)
        {
            Status.Text = T("Не удалось открыть адрес в браузере.");
        }
    }
}
