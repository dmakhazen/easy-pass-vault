using System.Runtime.InteropServices;
using System.Windows;
using EasyPassVault.Core;

namespace EasyPassVault.App;

/// <summary>
/// "Text / JSON" for ".json" text files of the vault: the JSON view is read-only and formatted on screen only.
/// It never changes the draft, so it never makes the vault unsaved. Every file opens in "Text".
/// </summary>
public partial class MainWindow
{
    private bool jsonView;

    private bool IsJsonFile => draft is { Kind: ItemKind.Text } item &&
        item.Title.Trim().EndsWith(".json", StringComparison.OrdinalIgnoreCase);

    private void UpdateTextView()
    {
        if (!IsJsonFile) jsonView = false;
        TextViewSwitch.Visibility = IsJsonFile ? Visibility.Visible : Visibility.Collapsed;
        TextHost.Visibility = TextTools.Visibility = jsonView ? Visibility.Collapsed : Visibility.Visible;
        JsonView.Visibility = jsonView ? Visibility.Visible : Visibility.Collapsed;
        PlainViewButton.Style = (Style)FindResource(jsonView ? "Segment" : "SegmentActive");
        JsonViewButton.Style = (Style)FindResource(jsonView ? "SegmentActive" : "Segment");
        // The generator needs an open item whose text can be edited: the JSON view is read-only.
        GeneratePasswordButton.IsEnabled = draft is not null && !jsonView;
    }

    private void SetJsonView(bool on)
    {
        if (busy || draft is null || (on && !IsJsonFile)) return;
        jsonView = on;
        if (on) JsonView.Show(TextPreview.FromText(draft.Text, json: true), T);
        UpdateTextView();
    }

    private void PlainViewClick(object sender, RoutedEventArgs e) => SetJsonView(false);
    private void JsonViewClick(object sender, RoutedEventArgs e) => SetJsonView(true);

    /// <summary>"Copy all" of a text view; returns the status line that is also shown in the window's status bar.</summary>
    private string CopyAllText(string text)
    {
        string message;
        try { writeClipboard(text); message = T("Текст скопирован"); }
        catch (Exception ex) when (ex is ExternalException or InvalidOperationException)
        { message = T("Буфер обмена занят другой программой. Повторите копирование."); }
        Status.Text = message;
        return message;
    }
}
