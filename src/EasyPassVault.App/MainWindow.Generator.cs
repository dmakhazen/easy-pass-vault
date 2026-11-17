using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using EasyPassVault.Core;

namespace EasyPassVault.App;

// The password generator button of the header. It works for entries and for text files: the password goes
// into the "Password" field, or into the text at the caret. It only changes the draft; the settings file
// keeps the generator's length and character sets, never the password.
public partial class MainWindow
{
    // The entry whose note was the last editor field with the keyboard focus. Pressing the header button
    // moves the focus away, so the place is remembered here.
    private string? noteFocusItemId;

    private void TrackGeneratorTarget(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (e.NewFocus is TextBoxBase or PasswordBox)
            noteFocusItemId = ReferenceEquals(e.NewFocus, TextField) ? draft?.Id : null;
    }

    /// <summary>A text file always takes the password into its text; an entry does when its note was edited last.</summary>
    private bool GeneratorIntoText => draft is { } item && (item.Kind == ItemKind.Text || noteFocusItemId == item.Id);

    private void GeneratePasswordClick(object sender, RoutedEventArgs e)
    {
        if (busy || draft is null || jsonView) return;
        var window = CreateGeneratorWindow();
        window.Owner = this;
        window.ShowDialog();
    }

    /// <summary>Separate from showing it, so the offscreen test can press its buttons.</summary>
    private GeneratorWindow CreateGeneratorWindow()
    {
        bool intoText = GeneratorIntoText, file = draft?.Kind == ItemKind.Text, selection = TextField.SelectionLength > 0;
        string action = !intoText ? "Подставить в пароль" : file ? "Вставить в текст" : "Вставить в заметку";
        string destination = !intoText ? "в поле «Пароль», вместо прежнего"
            : file ? selection ? "в текст файла, вместо выделенного" : "в текст файла, на место курсора"
            : selection ? "в заметку, вместо выделенного" : "в заметку, на место курсора";
        var window = new GeneratorWindow(layout.Generator, T, T(action), T(destination));
        window.Applied += password =>
        {
            if (intoText) InsertGeneratedPassword(password);
            else UseGeneratedPassword(password);
            window.Close();
        };
        window.Closed += (_, _) =>
        {
            if (window.Options == layout.Generator) return;
            layout = layout with { Generator = window.Options };
            if (shown) WindowLayoutStore.Save(layoutPath, layout);
        };
        return window;
    }

    /// <summary>Puts the password into the draft as if it were typed: the entry becomes unsaved.</summary>
    private void UseGeneratedPassword(string password)
    {
        if (draft is not { Kind: ItemKind.Login }) return;
        // Both boxes route to ReadDraft; the visible one also updates the hidden PasswordBox.
        if (VisiblePassword.Visibility == Visibility.Visible) VisiblePassword.Text = password;
        else PasswordField.Password = password;
        Status.Text = T("Пароль подставлен. Сохраните запись.");
    }

    /// <summary>
    /// Types the password into the text at the caret, replacing a selection. One undo step, like the
    /// "Heading" and "Separator" buttons; the caret ends up after the password.
    /// </summary>
    private void InsertGeneratedPassword(string password)
    {
        if (busy || draft is null || jsonView) return;
        bool file = draft.Kind == ItemKind.Text;
        int start = TextField.SelectionStart;
        TextField.BeginChange();
        try { TextField.SelectedText = password; }
        finally { TextField.EndChange(); }
        TextField.Select(start + password.Length, 0);
        TextField.Focus();
        Status.Text = T(file ? "Пароль вставлен в текст. Сохраните файл." : "Пароль вставлен в заметку. Сохраните запись.");
    }
}
