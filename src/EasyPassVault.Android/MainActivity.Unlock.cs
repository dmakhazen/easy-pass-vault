using Android.Content;
using Android.Views;
using Android.Views.InputMethods;
using EasyPassVault.Core;
using static EasyPassVault.Droid.L;
using AndroidUri = Android.Net.Uri;

namespace EasyPassVault.Droid;

public sealed partial class MainActivity
{
    private const int PickVault = 1;

    private AndroidUri? vaultUri;
    private TextView? fileName, unlockError;
    private EditText? passwordBox;
    private Button? openButton, pickButton;
    private ProgressBar? unlockProgress;

    private void ShowUnlock(string? message)
    {
        vaultUri ??= StoredUri();

        var column = new LinearLayout(this) { Orientation = Orientation.Vertical };
        column.SetGravity(GravityFlags.CenterHorizontal);
        int side = this.Dp(24);
        column.SetPadding(side, this.Dp(12), side, side);

        var top = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        top.SetGravity(GravityFlags.End);
        top.AddView(Ui.MakeButton(this, T("Settings"), primary: false, () => ShowSettings(() => ShowUnlock(null)), small: true));
        column.AddView(top, Ui.Below(this, 0));

        var icon = new ImageView(this);
        icon.SetImageResource(Resource.Drawable.vault_icon);
        column.AddView(icon, new LinearLayout.LayoutParams(this.Dp(88), this.Dp(88)) { TopMargin = this.Dp(8) });

        var title = Ui.Label(this, "easy-pass-vault", 24, Ui.Ink, bold: true);
        title.Gravity = GravityFlags.Center;
        column.AddView(title, Ui.Below(this, 14));
        var subtitle = Ui.Label(this, T("Vault viewer"), 15, Ui.Muted);
        subtitle.Gravity = GravityFlags.Center;
        column.AddView(subtitle, Ui.Below(this, 2));

        var card = new LinearLayout(this) { Orientation = Orientation.Vertical, Background = Ui.Box(this, Android.Graphics.Color.White, 14, Ui.Line) };
        int inner = this.Dp(16);
        card.SetPadding(inner, inner, inner, inner);
        card.AddView(Ui.Label(this, T("Vault file"), 12, Ui.Muted));
        fileName = Ui.Label(this, "", 16, Ui.Ink, bold: true);
        card.AddView(fileName, Ui.Below(this, 4));
        pickButton = Ui.MakeButton(this, T("Choose file"), primary: false, PickFile, small: true);
        card.AddView(pickButton, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent) { TopMargin = this.Dp(12) });
        column.AddView(card, Ui.Below(this, 28));

        passwordBox = Ui.Input(this, T("Master password"), password: true);
        passwordBox.ImeOptions = ImeAction.Done;
        // "Done" on the screen keyboard and Enter on a hardware one both open the vault.
        passwordBox.EditorAction += (_, e) =>
        {
            bool enter = e.Event is { KeyCode: Keycode.Enter, Action: KeyEventActions.Down };
            if (e.ActionId != ImeAction.Done && !enter) { e.Handled = false; return; }
            e.Handled = true;
            OpenVault();
        };
        column.AddView(passwordBox, Ui.Below(this, 16));

        openButton = Ui.MakeButton(this, T("Open"), primary: true, OpenVault);
        column.AddView(openButton, Ui.Below(this, 12));

        unlockProgress = new ProgressBar(this) { Indeterminate = true, Visibility = ViewStates.Gone };
        column.AddView(unlockProgress, new LinearLayout.LayoutParams(this.Dp(32), this.Dp(32)) { TopMargin = this.Dp(16) });

        unlockError = Ui.Label(this, message ?? "", 14, message is null ? Ui.Error : Ui.Muted);
        unlockError.Gravity = GravityFlags.Center;
        unlockError.Visibility = message is null ? ViewStates.Gone : ViewStates.Visible;
        column.AddView(unlockError, Ui.Below(this, 16));

        string version = PackageManager!.GetPackageInfo(PackageName!, 0)!.VersionName ?? "";
        var footer = Ui.Label(this, T("Version {0} · view only · offline", version), 12, Ui.Muted);
        footer.Gravity = GravityFlags.Center;
        column.AddView(footer, Ui.Below(this, 28));

        var scroll = new ScrollView(this) { FillViewport = true };
        scroll.AddView(column);
        RefreshFileName();
        Show(scroll, null);
    }

    private void RefreshFileName()
    {
        if (fileName is null) return;
        fileName.Text = vaultUri is null ? T("No file chosen") : DisplayName(vaultUri);
    }

    private void PickFile()
    {
        var intent = new Intent(Intent.ActionOpenDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType("*/*");
        intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantPersistableUriPermission);
        try { StartActivityForResult(intent, PickVault); }
        catch (ActivityNotFoundException) { ShowUnlockError(T("There is no app to choose files on this phone.")); }
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode != PickVault || resultCode != Result.Ok || data?.Data is not { } uri) return;
        // Remember the choice so the file is not picked again on every start. Only its address is stored.
        try { ContentResolver!.TakePersistableUriPermission(uri, ActivityFlags.GrantReadUriPermission); }
        catch (Java.Lang.SecurityException) { /* The provider gives one-time access only: still usable now. */ }
        vaultUri = uri;
        Preferences.Edit()!.PutString(LastUriKey, uri.ToString())!.Apply();
        RefreshFileName();
        ShowUnlockError(null);
    }

    private AndroidUri? StoredUri()
    {
        string? stored = Preferences.GetString(LastUriKey, null);
        return stored is null ? null : AndroidUri.Parse(stored);
    }

    private string DisplayName(AndroidUri uri)
    {
        try
        {
            using var cursor = ContentResolver!.Query(uri, ["_display_name"], null, null, null);
            if (cursor is not null && cursor.MoveToFirst() && cursor.GetString(0) is { Length: > 0 } name) return name;
        }
        catch (Exception ex) when (ex is Java.Lang.Exception or IOException) { /* Fall back to the address. */ }
        return uri.LastPathSegment ?? "vault.kdbx";
    }

    private void ShowUnlockError(string? message)
    {
        if (unlockError is null) return;
        unlockError.SetTextColor(Ui.Error);
        unlockError.Text = message ?? "";
        unlockError.Visibility = message is null ? ViewStates.Gone : ViewStates.Visible;
    }

    private void SetUnlockBusy(bool busy)
    {
        if (openButton is null || pickButton is null || unlockProgress is null) return;
        // The password field stays enabled: disabling it would move the focus to another control.
        openButton.Enabled = pickButton.Enabled = !busy;
        openButton.Alpha = busy ? 0.5f : 1f;
        unlockProgress.Visibility = busy ? ViewStates.Visible : ViewStates.Gone;
    }

    private async void OpenVault()
    {
        if (openButton is not { Enabled: true } || passwordBox is null) return;
        if (vaultUri is not { } uri) { ShowUnlockError(T("Choose the vault file first.")); return; }
        string password = passwordBox.Text ?? "";
        if (password.Length == 0) { ShowUnlockError(T("Enter the master password.")); return; }

        ShowUnlockError(null);
        SetUnlockBusy(true);
        string name = DisplayName(uri);
        try
        {
            // Key derivation is deliberately slow: keep it off the UI thread.
            var opened = await Task.Run(() => VaultSession.OpenReadOnly(ReadVault(uri), name, password));
            if (IsDestroyed) { opened.Dispose(); return; }
            CloseSession();
            session = opened;
            vaultName = name;
            passwordBox.Text = "";
            listScreen = null;
            ShowList();
        }
        catch (VaultException ex) { ShowUnlockError(Error(ex.Message)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Java.Lang.Exception)
        { ShowUnlockError(T("Could not read the file. Choose it again.")); }
        catch (OutOfMemoryException) { ShowUnlockError(T("Not enough memory to open the vault.")); }
        finally { SetUnlockBusy(false); }
    }

    /// <summary>Reads the encrypted file the user picked. The size is limited before it can exhaust memory.</summary>
    private byte[] ReadVault(AndroidUri uri)
    {
        using var input = ContentResolver!.OpenInputStream(uri) ?? throw new IOException("The provider returned no stream.");
        using var buffer = new MemoryStream();
        var chunk = new byte[128 * 1024];
        int read;
        while ((read = input.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (buffer.Length + read > AtomicVaultFile.MaxFileBytes)
                throw new VaultException("Недопустимый размер файла хранилища: он пустой или больше 512 МБ. Файл не расшифровывался и не изменён.");
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }
}
