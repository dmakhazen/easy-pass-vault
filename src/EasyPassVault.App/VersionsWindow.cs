using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EasyPassVault.Core;

namespace EasyPassVault.App;

/// <summary>
/// The files of the series of the open vault (IdeaVersions.dc.html): number, name, time and size on disk,
/// "open now" and "newest" marks. Choosing another version only returns its path; the main window decides what to do.
/// </summary>
internal sealed class VersionsWindow : Window
{
    public string? ChosenPath { get; private set; }

    /// <remarks>The caller sets <see cref="Window.Owner"/> before showing it.</remarks>
    public VersionsWindow(string currentPath, IReadOnlyList<VaultVersions.VersionFile> files, string? nextPath,
        Func<string, string> T, CultureInfo culture, Func<string, bool> showInExplorer)
    {
        Title = T("Версии хранилища «{0}»").Replace("{0}", VaultVersions.Parse(currentPath).BaseName);
        Width = 600; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.White;
        FontFamily = new FontFamily("Segoe UI"); FontSize = 14;
        Foreground = (Brush)new BrushConverter().ConvertFromString("#263832")!;
        var green = (Brush)FindResource("Green");
        var secondary = (Brush)FindResource("Secondary");

        var panel = new DockPanel { Margin = new Thickness(30, 22, 30, 22) };
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var folder = new TextBlock
        {
            Text = T("папка {0}").Replace("{0}", Path.GetDirectoryName(currentPath)), FontSize = 12, Foreground = secondary,
            VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(10, 0, 0, 2), MaxWidth = 260,
            TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = Path.GetDirectoryName(currentPath)
        };
        DockPanel.SetDock(folder, Dock.Right);
        header.Children.Add(folder);
        header.Children.Add(new TextBlock { Text = Title, FontSize = 19, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        DockPanel.SetDock(header, Dock.Top);
        panel.Children.Add(header);

        var footer = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var explorer = new Button { Content = T("Показать в проводнике"), Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(10, 0, 0, 0) };
        var explorerError = new TextBlock { Foreground = (Brush)FindResource("PendingSaveForeground"), FontSize = 12, Visibility = Visibility.Collapsed,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
        explorer.Click += (_, _) =>
        {
            bool shown = showInExplorer(currentPath);
            explorerError.Text = shown ? "" : T("Не удалось открыть проводник.");
            explorerError.Visibility = shown ? Visibility.Collapsed : Visibility.Visible;
        };
        DockPanel.SetDock(explorer, Dock.Right);
        footer.Children.Add(explorer);
        footer.Children.Add(new TextBlock
        {
            Text = nextPath is null ? "" : T("Следующий файл: {0}").Replace("{0}", Path.GetFileName(nextPath)),
            FontSize = 12, Foreground = secondary, VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = nextPath
        });
        var bottom = new StackPanel();
        bottom.Children.Add(footer);
        bottom.Children.Add(explorerError);
        DockPanel.SetDock(bottom, Dock.Bottom);
        panel.Children.Add(bottom);

        var rows = new StackPanel();
        foreach (var file in files)
        {
            var row = new Border
            {
                Background = file.IsCurrent ? (Brush)new BrushConverter().ConvertFromString("#E1EADE")! : (Brush)new BrushConverter().ConvertFromString("#FAFBF8")!,
                BorderBrush = file.IsCurrent ? green : (Brush)new BrushConverter().ConvertFromString("#DCE3DC")!,
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 0, 0, 8)
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(new TextBlock
            {
                Text = file.Number > 0 ? "v" + file.Number.ToString(CultureInfo.InvariantCulture) : "—",
                FontSize = 16, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)new BrushConverter().ConvertFromString("#24493B")!
            });
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = Path.GetFileName(file.Path), FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = file.Path });
            string modified = file.ModifiedUtc == default ? "" : T("изменён {0}").Replace("{0}", file.ModifiedUtc.ToLocalTime().ToString("g", culture));
            string sub = string.Join(" · ", new[] { modified, FormatSize(file.Size, T, culture), file.IsLatest ? T("самая новая") : "" }
                .Where(part => part.Length > 0));
            text.Children.Add(new TextBlock { Text = sub, FontSize = 12, Foreground = secondary, Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            FrameworkElement action;
            if (file.IsCurrent)
                action = new Border
                {
                    Background = green, CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 2, 8, 2), VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock { Text = T("открыта"), FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White }
                };
            else
            {
                var open = new Button { Content = T("Открыть"), Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                string path = file.Path;
                System.Windows.Automation.AutomationProperties.SetName(open, T("Открыть") + " " + Path.GetFileName(path));
                open.Click += (_, _) => { ChosenPath = path; DialogResult = true; };
                action = open;
            }
            action.Margin = new Thickness(10, 0, 0, 0);
            Grid.SetColumn(action, 2);
            grid.Children.Add(action);
            row.Child = grid;
            rows.Children.Add(row);
        }
        panel.Children.Add(new ScrollViewer { Content = rows, MaxHeight = 400, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = panel;
    }

    internal static string FormatSize(long bytes, Func<string, string> T, CultureInfo culture) =>
        bytes < 1024 * 1024
            ? T("{0} КБ").Replace("{0}", Math.Max(1, (long)Math.Ceiling(bytes / 1024.0)).ToString("N0", culture))
            : T("{0} МБ").Replace("{0}", (bytes / (1024.0 * 1024.0)).ToString("0.0", culture));
}
