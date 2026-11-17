using Android.Content;
using Android.Util;
using Android.Views;
using static EasyPassVault.Droid.L;

namespace EasyPassVault.Droid;

public sealed partial class MainActivity
{
    private const string Settings = "vault";
    private const string LastUriKey = "uri";
    private const string LanguageKey = "language";
    private const string AutoLockKey = "autolock";

    /// <summary>Off, one minute, five minutes. Off is the default.</summary>
    private static readonly (int Seconds, string Text)[] AutoLockChoices =
    [
        (0, "Off"), (60, "After 1 minute in the background"), (300, "After 5 minutes in the background")
    ];

    // Only settings live here: the address of the last vault file, the language and the auto-lock delay.
    private ISharedPreferences Preferences => GetSharedPreferences(Settings, FileCreationMode.Private)!;

    private int AutoLockSeconds => Preferences.GetInt(AutoLockKey, 0);

    /// <summary>Opens settings; <paramref name="reopen"/> shows the screen they were opened from again.</summary>
    private void ShowSettings(Action reopen)
    {
        var column = new LinearLayout(this) { Orientation = Orientation.Vertical };
        int side = this.Dp(16);
        column.SetPadding(side, this.Dp(12), side, this.Dp(24));

        var backRow = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        backRow.AddView(Ui.MakeButton(this, "‹  " + T("Back"), primary: false, reopen, small: true));
        column.AddView(backRow);
        column.AddView(Ui.Label(this, T("Settings"), 22, Ui.Ink, bold: true), Ui.Below(this, 16));

        column.AddView(Choice(T("Language"), Languages.Select(language => language.Name).ToArray(), Language, index =>
        {
            Preferences.Edit()!.PutString(LanguageKey, Languages[index].Culture)!.Apply();
            Select(Languages[index].Culture);
            ApplyDirection();
            // Every screen is built from the texts of the moment: rebuild the list and this screen.
            listScreen = null;
            ShowSettings(reopen);
        }), Ui.Below(this, 16));

        int current = Math.Max(0, Array.FindIndex(AutoLockChoices, choice => choice.Seconds == AutoLockSeconds));
        var autoLock = Choice(T("Auto-lock"), AutoLockChoices.Select(choice => T(choice.Text)).ToArray(), current,
            index => Preferences.Edit()!.PutInt(AutoLockKey, AutoLockChoices[index].Seconds)!.Apply());
        autoLock.AddView(Ui.Label(this, T("While it is off, the vault stays open until you press Lock or close the app."), 12, Ui.Muted), Ui.Below(this, 8));
        column.AddView(autoLock, Ui.Below(this, 12));

        var scroll = new ScrollView(this);
        scroll.AddView(column);
        Show(scroll, reopen);
    }

    /// <summary>A card with a title and one choice out of several.</summary>
    private LinearLayout Choice(string title, string[] options, int selected, Action<int> choose)
    {
        var card = Card(title);
        var group = new RadioGroup(this) { Orientation = Orientation.Vertical };
        for (int i = 0; i < options.Length; i++)
        {
            var option = new RadioButton(this) { Text = options[i], Id = i + 1 };
            option.SetTextSize(ComplexUnitType.Sp, 16);
            option.SetTextColor(Ui.Ink);
            option.SetPadding(this.Dp(8), this.Dp(10), this.Dp(8), this.Dp(10));
            group.AddView(option, new RadioGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        }
        group.Check(selected + 1);
        group.CheckedChange += (_, e) => { if (e.CheckedId - 1 != selected) { selected = e.CheckedId - 1; choose(selected); } };
        card.AddView(group, Ui.Below(this, 4));
        return card;
    }
}
