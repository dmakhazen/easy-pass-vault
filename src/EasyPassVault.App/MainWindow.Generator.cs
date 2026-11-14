using System.Windows;
using EasyPassVault.Core;

namespace EasyPassVault.App;

// The password generator button of the entry editor. The password goes only into the draft;
// the settings file keeps the generator's length and character sets, never the password.
public partial class MainWindow
{
    private void GeneratePasswordClick(object sender, RoutedEventArgs e)
    {
        if (busy || draft is not { Kind: ItemKind.Login }) return;
        var window = CreateGeneratorWindow();
        window.Owner = this;
        window.ShowDialog();
    }

    /// <summary>Separate from showing it, so the offscreen test can press its buttons.</summary>
    private GeneratorWindow CreateGeneratorWindow()
    {
        var window = new GeneratorWindow(layout.Generator, T);
        window.Applied += password =>
        {
            UseGeneratedPassword(password);
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
}
