using Android.Content;
using Android.OS;
using Android.Content.PM;
using Android.Views;
using Android.Views.InputMethods;
using Android.Window;
using EasyPassVault.Core;
using static EasyPassVault.Droid.L;

namespace EasyPassVault.Droid;

/// <summary>
/// The whole app is one activity with screens swapped in place: unlock, list, item, settings.
/// It only reads: the vault is opened through <see cref="VaultSession.OpenReadOnly"/> and nothing is written.
/// </summary>
[Activity(Label = "easy-pass-vault", MainLauncher = true, Theme = "@style/AppTheme",
    WindowSoftInputMode = SoftInput.AdjustResize,
    // Rotation and similar changes must not rebuild the activity: the open screen would be lost.
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.ScreenLayout |
        ConfigChanges.SmallestScreenSize | ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden |
        ConfigChanges.UiMode | ConfigChanges.Density | ConfigChanges.FontScale | ConfigChanges.Locale |
        ConfigChanges.LayoutDirection)]
public sealed partial class MainActivity : Activity
{
    // Static: the system may rebuild the activity while the process and the unlocked vault stay alive.
    private static VaultSession? session;
    private static string vaultName = "";
    private static long stoppedAt = -1;

    private FrameLayout root = null!;
    private Action? back;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
#if !ALLOW_SCREENSHOTS
        // No screenshots, no preview in the list of recent apps. Only a test build leaves this out.
        Window!.SetFlags(WindowManagerFlags.Secure, WindowManagerFlags.Secure);
#endif
        Select(Preferences.GetString(LanguageKey, null));
        root = new FrameLayout(this);
        root.SetBackgroundColor(Ui.Paper);
        root.SetOnApplyWindowInsetsListener(new SystemBarsPadding());
        ApplyDirection();
        SetContentView(root);
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
            OnBackInvokedDispatcher.RegisterOnBackInvokedCallback(0, new BackCallback(GoBack));

        if (session is null || Expired()) Lock(null);
        else ShowList();
    }

    protected override void OnStop()
    {
        base.OnStop();
        stoppedAt = SystemClock.ElapsedRealtime();
    }

    protected override void OnStart()
    {
        base.OnStart();
        if (session is not null && Expired())
            Lock(T("The vault was locked: the app stayed in the background too long."));
        stoppedAt = -1;
    }

    protected override void OnDestroy()
    {
        if (IsFinishing) CloseSession();
        base.OnDestroy();
    }

    /// <summary>True when auto-lock is on and the app has been in the background longer than its delay.</summary>
    private bool Expired()
    {
        long delay = AutoLockSeconds * 1000L;
        return delay > 0 && stoppedAt >= 0 && SystemClock.ElapsedRealtime() - stoppedAt > delay;
    }

    private static void CloseSession()
    {
        session?.Dispose();
        session = null;
        vaultName = "";
    }

    private void Lock(string? message)
    {
        CloseSession();
        listScreen = null;
        ShowUnlock(message);
    }

    /// <summary>Arabic is laid out and aligned right to left, every other language left to right.</summary>
    private void ApplyDirection()
    {
        root.LayoutDirection = RightToLeft ? Android.Views.LayoutDirection.Rtl : Android.Views.LayoutDirection.Ltr;
        root.TextDirection = RightToLeft ? TextDirection.Rtl : TextDirection.Ltr;
    }

    private void Show(View screen, Action? goBack)
    {
        if (CurrentFocus is { } focused)
            ((InputMethodManager)GetSystemService(InputMethodService)!).HideSoftInputFromWindow(focused.WindowToken, 0);
        root.RemoveAllViews();
        root.AddView(screen, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        back = goBack;
    }

    private void GoBack()
    {
        if (back is { } action) action();
        else Finish();
    }

#pragma warning disable CS0672, CS0618, CA1422 // Before Android 13 the back button arrives only here.
    public override void OnBackPressed() => GoBack();
#pragma warning restore CS0672, CS0618, CA1422

    private sealed class BackCallback(Action action) : Java.Lang.Object, IOnBackInvokedCallback
    {
        public void OnBackInvoked() => action();
    }

    /// <summary>Keeps the content clear of the status bar, the navigation bar and the keyboard.</summary>
    private sealed class SystemBarsPadding : Java.Lang.Object, View.IOnApplyWindowInsetsListener
    {
        public WindowInsets OnApplyWindowInsets(View view, WindowInsets insets)
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(30))
            {
                var area = insets.GetInsets(WindowInsets.Type.SystemBars() | WindowInsets.Type.Ime() | WindowInsets.Type.DisplayCutout());
                view.SetPadding(area.Left, area.Top, area.Right, area.Bottom);
                return WindowInsets.Consumed;
            }
#pragma warning disable CS0618, CA1422 // The only API before Android 11.
            view.SetPadding(insets.SystemWindowInsetLeft, insets.SystemWindowInsetTop, insets.SystemWindowInsetRight, insets.SystemWindowInsetBottom);
            return insets.ConsumeSystemWindowInsets()!;
#pragma warning restore CS0618, CA1422
        }
    }
}
