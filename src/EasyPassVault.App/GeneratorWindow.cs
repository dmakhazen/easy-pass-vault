using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using EasyPassVault.Core;

namespace EasyPassVault.App;

/// <summary>
/// The password generator (IdeaGenerator.dc.html): the password in large monospace, "generate again", strength,
/// length 8–64 and the character sets. "Use in entry" raises <see cref="Applied"/>; the window never stores the password.
/// </summary>
internal sealed class GeneratorWindow : Window
{
    private static readonly Brush Track = Hex("#E3E8E5"), Muted = Hex("#54655C");
    private readonly Func<string, string> T;
    private readonly TextBlock passwordText = new()
    {
        FontFamily = new FontFamily("Consolas, Courier New"), FontSize = 17, TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly Border strengthFill = new() { Height = 6, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock strengthText = new() { FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, MinWidth = 64 };
    private readonly Slider length = new() { Minimum = PasswordOptions.MinLength, Maximum = PasswordOptions.MaxLength, IsSnapToTickEnabled = true, TickFrequency = 1, SmallChange = 1, LargeChange = 4, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock lengthText = new() { FontWeight = FontWeights.SemiBold, Width = 26, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
    private readonly CheckBox upper = new(), lower = new(), digits = new(), symbols = new(), similar = new();
    private readonly Button regenerate = new() { Width = 34, Height = 34, Padding = new Thickness(0), Margin = new Thickness(8, 0, 0, 0), Background = Brushes.White };
    private readonly Button apply = new() { Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(14, 8, 14, 8) };
    private bool loading = true;

    public PasswordOptions Options { get; private set; }
    /// <summary>The password on screen; empty when no character set is chosen.</summary>
    public string Password { get; private set; } = "";
    /// <summary>"Use in entry": the caller puts the password into the draft and closes the window.</summary>
    public event Action<string>? Applied;

    /// <remarks>The caller sets <see cref="Window.Owner"/> before showing it.</remarks>
    public GeneratorWindow(PasswordOptions options, Func<string, string> translate)
    {
        T = translate;
        Options = options.Normalize();
        Title = T("Генератор пароля");
        Width = 460; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        Background = Brushes.White; FontFamily = new FontFamily("Segoe UI"); FontSize = 14; Foreground = Hex("#263832");
        // Check boxes and the slider take the theme's accent (the Windows accent color): use the app's green instead.
        var accent = Color.FromRgb(0x2F, 0x70, 0x59);
        foreach (string key in new[] { "SystemAccentColor", "SystemAccentColorLight1", "SystemAccentColorLight2", "SystemAccentColorLight3",
            "SystemAccentColorDark1", "SystemAccentColorDark2", "SystemAccentColorDark3" })
            Resources[key] = accent;
        foreach (string key in new[] { "AccentFillColorDefaultBrush", "AccentFillColorSecondaryBrush", "AccentFillColorTertiaryBrush",
            "AccentControlElevationBorderBrush", "SliderThumbBackgroundPointerOver", "SliderTrackValueFill", "SliderTrackValueFillPointerOver",
            "SliderTrackValueFillPressed", "SliderThumbBackground", "SliderThumbBackgroundPressed",
            "CheckBoxCheckBackgroundFillChecked", "CheckBoxCheckBackgroundFillCheckedPointerOver", "CheckBoxCheckBackgroundFillCheckedPressed",
            "CheckBoxCheckBackgroundStrokeChecked", "CheckBoxCheckBackgroundStrokeCheckedPointerOver", "CheckBoxCheckBackgroundStrokeCheckedPressed" })
            Resources[key] = Hex("#2F7059");

        var panel = new StackPanel { Margin = new Thickness(18) };

        var output = new DockPanel();
        regenerate.Content = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M13.5,8 A5.5,5.5 0 1 1 11.9,4.1 M13.5,2 V5 H10.5"), Width = 16, Height = 16,
            StrokeThickness = 1.5, Stroke = Foreground
        };
        regenerate.ToolTip = T("Сгенерировать заново");
        System.Windows.Automation.AutomationProperties.SetName(regenerate, T("Сгенерировать заново"));
        regenerate.Click += (_, _) => Regenerate();
        DockPanel.SetDock(regenerate, Dock.Right);
        output.Children.Add(regenerate);
        output.Children.Add(passwordText);
        System.Windows.Automation.AutomationProperties.SetName(passwordText, T("Пароль"));
        panel.Children.Add(new Border
        {
            Padding = new Thickness(12), CornerRadius = new CornerRadius(8), Background = Hex("#F4F6F1"),
            BorderBrush = Hex("#DCE3DC"), BorderThickness = new Thickness(1), Child = output
        });

        var strength = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        strength.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        strength.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        strength.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        strength.Children.Add(new TextBlock { Text = T("Надёжность"), FontSize = 12, Foreground = Muted, VerticalAlignment = VerticalAlignment.Center });
        var track = new Grid { Height = 6, Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        track.Children.Add(new Border { Background = Track, CornerRadius = new CornerRadius(3) });
        track.Children.Add(strengthFill);
        track.SizeChanged += (_, _) => UpdateStrength();
        Grid.SetColumn(track, 1); strength.Children.Add(track);
        Grid.SetColumn(strengthText, 2); strength.Children.Add(strengthText);
        panel.Children.Add(strength);

        var lengthRow = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        var lengthLabel = new TextBlock { Text = T("Длина"), FontSize = 13, Foreground = Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        DockPanel.SetDock(lengthLabel, Dock.Left); lengthRow.Children.Add(lengthLabel);
        DockPanel.SetDock(lengthText, Dock.Right); lengthRow.Children.Add(lengthText);
        lengthRow.Children.Add(length);
        System.Windows.Automation.AutomationProperties.SetName(length, T("Длина"));
        panel.Children.Add(lengthRow);

        var sets = new UniformGrid { Columns = 2, Margin = new Thickness(0, 12, 0, 0) };
        foreach (var (box, label) in new[] { (upper, "Заглавные A–Z"), (lower, "Строчные a–z"), (digits, "Цифры 0–9"),
            (symbols, "Символы"), (similar, "Без похожих: O 0 l 1 I") })
        {
            box.Content = T(label) + (box == symbols ? " " + PasswordGenerator.SymbolSet[..3] + "…" : "");
            box.Margin = new Thickness(0, 4, 12, 4);
            box.VerticalContentAlignment = VerticalAlignment.Center;
            box.Checked += (_, _) => OptionsChanged();
            box.Unchecked += (_, _) => OptionsChanged();
            sets.Children.Add(box);
        }
        symbols.ToolTip = PasswordGenerator.SymbolSet;
        panel.Children.Add(sets);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var cancel = new Button { Content = T("Отмена"), IsCancel = true, Margin = new Thickness(0), Padding = new Thickness(14, 8, 14, 8) };
        apply.Content = T("Подставить в запись");
        apply.Style = (Style)FindResource("Primary");
        apply.Margin = new Thickness(8, 0, 0, 0); apply.Padding = new Thickness(14, 8, 14, 8); apply.FontWeight = FontWeights.SemiBold;
        apply.IsDefault = true;
        apply.Click += (_, _) => { if (Password.Length > 0) Applied?.Invoke(Password); };
        buttons.Children.Add(cancel); buttons.Children.Add(apply);
        panel.Children.Add(buttons);
        Content = panel;

        length.Value = Options.Length;
        upper.IsChecked = Options.Upper; lower.IsChecked = Options.Lower; digits.IsChecked = Options.Digits;
        symbols.IsChecked = Options.Symbols; similar.IsChecked = Options.ExcludeSimilar;
        length.ValueChanged += (_, _) => OptionsChanged();
        loading = false;
        OptionsChanged();
        Closed += (_, _) => { Password = ""; passwordText.Text = ""; };
    }

    internal Button ApplyButton => apply;

    private void OptionsChanged()
    {
        if (loading) return;
        Options = new PasswordOptions
        {
            Length = (int)Math.Round(length.Value), Upper = upper.IsChecked == true, Lower = lower.IsChecked == true,
            Digits = digits.IsChecked == true, Symbols = symbols.IsChecked == true, ExcludeSimilar = similar.IsChecked == true
        }.Normalize();
        lengthText.Text = Options.Length.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Regenerate();
    }

    private void Regenerate()
    {
        bool can = PasswordGenerator.CanGenerate(Options);
        Password = can ? PasswordGenerator.Generate(Options) : "";
        passwordText.Text = can ? Password : T("Выберите хотя бы один набор символов.");
        passwordText.FontFamily = can ? new FontFamily("Consolas, Courier New") : FontFamily;
        passwordText.FontSize = can ? 17 : 13;
        passwordText.Foreground = can ? Foreground : Muted;
        regenerate.IsEnabled = apply.IsEnabled = can;
        UpdateStrength();
    }

    private void UpdateStrength()
    {
        double bits = PasswordGenerator.EntropyBits(Options);
        var level = PasswordGenerator.Strength(bits);
        var color = bits == 0 ? Track : Hex(level switch { PasswordStrength.Weak => "#B4533F", PasswordStrength.Medium => "#B07A18", _ => "#2F7059" });
        strengthFill.Background = color;
        strengthFill.Width = bits == 0 || strengthFill.Parent is not FrameworkElement { ActualWidth: > 0 } track
            ? 0 : track.ActualWidth * Math.Min(1, bits / 128);
        strengthText.Text = bits == 0 ? "—" : T(level switch { PasswordStrength.Weak => "слабая", PasswordStrength.Medium => "средняя", _ => "высокая" });
        strengthText.Foreground = bits == 0 ? Muted : Hex(level switch { PasswordStrength.Weak => "#96483D", PasswordStrength.Medium => "#754700", _ => "#1D5944" });
    }

    private static SolidColorBrush Hex(string color)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
        brush.Freeze();
        return brush;
    }
}
