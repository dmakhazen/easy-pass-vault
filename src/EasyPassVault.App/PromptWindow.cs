using System.Windows;
using System.Windows.Controls;

namespace EasyPassVault.App;

internal sealed class PromptWindow : Window
{
    private readonly PasswordBox password = new();
    private readonly PasswordBox repeat = new();
    private readonly TextBox text = new();
    public string Value => secret ? password.Password : text.Text;
    private readonly bool secret;
    public PromptWindow(Window owner, string title, string label, bool isSecret, bool confirm = false, string initial = "")
    {
        Owner = owner; Title = title; secret = isSecret;
        Width = 450; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) });
        text.Text = initial;
        panel.Children.Add(secret ? password : text);
        if (confirm)
        {
            panel.Children.Add(new TextBlock { Text = "Повторите главный пароль", Margin = new Thickness(0, 16, 0, 8) });
            panel.Children.Add(repeat);
        }
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
        panel.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var cancel = new Button { Content = "Отмена", IsCancel = true };
        var ok = new Button { Content = confirm ? "Создать" : "Продолжить", IsDefault = true };
        ok.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(Value)) { error.Text = "Поле не может быть пустым."; return; }
            if (confirm && Value.Length < 12) { error.Text = "Для нового хранилища используйте пароль длиной от 12 символов."; return; }
            if (confirm && password.Password != repeat.Password) { error.Text = "Пароли не совпадают."; return; }
            DialogResult = true;
        };
        buttons.Children.Add(cancel); buttons.Children.Add(ok); panel.Children.Add(buttons); Content = panel;
        Loaded += (_, _) => { if (secret) password.Focus(); else { text.Focus(); text.SelectAll(); } };
    }
    public void Clear() { password.Clear(); repeat.Clear(); text.Clear(); }
}
