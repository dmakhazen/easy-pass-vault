using System.Globalization;
using Android.Content;
using Android.OS;
using Android.Graphics;
using Android.Views;
using EasyPassVault.Core;
using static EasyPassVault.Droid.L;
using AndroidUri = Android.Net.Uri;

namespace EasyPassVault.Droid;

public sealed partial class MainActivity
{
    private const string HiddenPassword = "••••••••";

    private void ShowItem(string id)
    {
        if (session is null) { Lock(null); return; }
        var item = session.GetItem(id);
        var culture = Culture;

        var column = new LinearLayout(this) { Orientation = Orientation.Vertical };
        int side = this.Dp(16);
        column.SetPadding(side, this.Dp(12), side, this.Dp(24));

        var backRow = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        backRow.AddView(Ui.MakeButton(this, "‹  " + T("Back"), primary: false, ShowList, small: true));
        column.AddView(backRow);

        var title = Ui.Label(this, TitleOf(item), 22, Ui.Ink, bold: true);
        title.SetTextIsSelectable(true);
        column.AddView(title, Ui.Below(this, 16));

        var about = new List<string> { T(item.Kind == ItemKind.Text ? "File" : "Entry") };
        if (folderNames.TryGetValue(item.FolderId, out var folder)) about.Add(folder);
        if (item.Modified is { } modified) about.Add(T("modified {0}", modified.ToLocalTime().ToString("d MMM yyyy, HH:mm", culture)));
        column.AddView(Ui.Label(this, string.Join(" · ", about), 13, Ui.Muted), Ui.Below(this, 4));

        if (item.Kind == ItemKind.Login)
        {
            if (item.Login.Length > 0) column.AddView(Field(T("Username"), item.Login, sensitive: false), Ui.Below(this, 16));
            if (item.Password.Length > 0) column.AddView(PasswordField(item.Password), Ui.Below(this, 12));
            if (item.Site.Length > 0) column.AddView(Field(T("Website"), item.Site, sensitive: false, site: true), Ui.Below(this, 12));
            if (item.Text.Length > 0) column.AddView(Field(T("Note"), item.Text, sensitive: true), Ui.Below(this, 12));
        }
        else if (item.Text.Length > 0) column.AddView(Field(T("Text"), item.Text, sensitive: true), Ui.Below(this, 16));

        if (item.Attachments.Count > 0) column.AddView(Attachments(item.Attachments, culture), Ui.Below(this, 12));

        if (item is { Login.Length: 0, Password.Length: 0, Site.Length: 0, Text.Length: 0, Attachments.Count: 0 })
            column.AddView(Ui.Label(this, T("This item has no data."), 15, Ui.Muted), Ui.Below(this, 24));

        var scroll = new ScrollView(this);
        scroll.AddView(column);
        Show(scroll, ShowList);
    }

    private LinearLayout Card(string label)
    {
        var card = new LinearLayout(this) { Orientation = Orientation.Vertical, Background = Ui.Box(this, Color.White, 12, Ui.Line) };
        int inner = this.Dp(14);
        card.SetPadding(inner, inner, inner, inner);
        card.AddView(Ui.Label(this, label, 12, Ui.Muted));
        return card;
    }

    private LinearLayout Field(string label, string value, bool sensitive, bool site = false)
    {
        var card = Card(label);
        var text = Ui.Label(this, value, 16, Ui.Ink);
        text.SetTextIsSelectable(true);
        card.AddView(text, Ui.Below(this, 4));

        var buttons = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        buttons.AddView(Ui.MakeButton(this, T("Copy"), primary: false, () => Copy(label, value, sensitive), small: true));
        // Only a well-formed http(s) address is handed to the browser, the same rule as on Windows.
        if (site && SiteAddress.TryGetWebUri(value, out var address))
            buttons.AddView(Ui.MakeButton(this, T("Open"), primary: false, () => OpenSite(address), small: true), Ui.Beside(this, 8));
        card.AddView(buttons, Ui.Below(this, 10));
        return card;
    }

    private LinearLayout PasswordField(string password)
    {
        var card = Card(T("Password"));
        // Hidden by a fixed number of dots: the length of the password is not shown either.
        var text = Ui.Label(this, HiddenPassword, 16, Ui.Ink);
        text.SetTypeface(Typeface.Monospace, TypefaceStyle.Normal);
        card.AddView(text, Ui.Below(this, 4));

        var buttons = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        buttons.AddView(Ui.MakeButton(this, T("Copy"), primary: true, () => Copy(T("Password"), password, sensitive: true), small: true));
        Button? toggle = null;
        bool visible = false;
        toggle = Ui.MakeButton(this, T("Show"), primary: false, () =>
        {
            visible = !visible;
            text.Text = visible ? password : HiddenPassword;
            text.SetTextIsSelectable(visible);
            toggle!.Text = T(visible ? "Hide" : "Show");
        }, small: true);
        buttons.AddView(toggle, Ui.Beside(this, 8));
        card.AddView(buttons, Ui.Below(this, 10));
        return card;
    }

    private LinearLayout Attachments(IReadOnlyList<VaultAttachment> attachments, CultureInfo culture)
    {
        var card = Card($"{T("Attachments")} · {attachments.Count}");
        foreach (var attachment in attachments)
        {
            var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
            var name = Ui.Label(this, attachment.Name, 15, Ui.Ink);
            row.AddView(name, Ui.Beside(this, 0, weight: 1));
            row.AddView(Ui.Label(this, FormatSize(attachment.Size, culture), 13, Ui.Muted), Ui.Beside(this, 12));
            card.AddView(row, Ui.Below(this, 8));
        }
        card.AddView(Ui.Label(this, T("Attachments can be opened and saved in the Windows version."), 12, Ui.Muted), Ui.Below(this, 10));
        return card;
    }

    private static string FormatSize(long bytes, CultureInfo culture) =>
        bytes < 1024 ? $"{bytes} {T("B")}"
        : bytes < 1024 * 1024 ? (bytes / 1024.0).ToString("0.#", culture) + " " + T("KB")
        : (bytes / (1024.0 * 1024)).ToString("0.#", culture) + " " + T("MB");

    private void Copy(string label, string value, bool sensitive)
    {
        var clipboard = (ClipboardManager)GetSystemService(ClipboardService)!;
        var clip = ClipData.NewPlainText(label, value)!;
        if (sensitive)
        {
            // Android 13+ hides such content in the clipboard preview; older versions ignore the mark.
            var extras = new PersistableBundle();
            extras.PutBoolean("android.content.extra.IS_SENSITIVE", true);
            clip.Description!.Extras = extras;
        }
        clipboard.PrimaryClip = clip;
        // Android 13+ shows its own confirmation.
        if (!OperatingSystem.IsAndroidVersionAtLeast(33))
            Toast.MakeText(this, T("{0}: copied", label), ToastLength.Short)!.Show();
    }

    private void OpenSite(Uri address)
    {
        try { StartActivity(new Intent(Intent.ActionView, AndroidUri.Parse(address.AbsoluteUri))); }
        catch (ActivityNotFoundException) { Toast.MakeText(this, T("There is no app to open the website."), ToastLength.Short)!.Show(); }
    }
}
