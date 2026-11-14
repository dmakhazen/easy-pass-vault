using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EasyPassVault.App;

/// <summary>A question with two named buttons ("Add" / "Cancel", "Delete" / "Cancel").</summary>
internal sealed class ConfirmWindow : Window
{
    public ConfirmWindow(string title, string message, string accept, Func<string, string> T)
    {
        Title = title;
        Width = 460; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.White; FontFamily = new FontFamily("Segoe UI"); FontSize = 14;
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var cancel = new Button { Content = T("Отмена"), IsCancel = true, IsDefault = true };
        var ok = new Button { Content = accept };
        ok.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(cancel); buttons.Children.Add(ok);
        panel.Children.Add(buttons);
        Content = panel;
    }
}

/// <summary>
/// An image attachment shown from memory. The bytes are decoded from a <see cref="MemoryStream"/> and cleared;
/// nothing is written to disk and no other program is started.
/// </summary>
internal sealed class ImagePreviewWindow : Window
{
    public BitmapSource Image { get; }

    private ImagePreviewWindow(string name, BitmapSource image)
    {
        Image = image;
        Title = name;
        Background = (Brush)new BrushConverter().ConvertFromString("#1E2B26")!;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var area = SystemParameters.WorkArea;
        Width = Math.Clamp(image.PixelWidth + 40, 360, area.Width * 0.85);
        Height = Math.Clamp(image.PixelHeight + 70, 280, area.Height * 0.85);
        Content = new Image
        {
            Source = image, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Margin = new Thickness(12)
        };
    }

    /// <summary>Decodes the image or returns null when WPF cannot read it (unknown format, damaged file, no codec).</summary>
    public static ImagePreviewWindow? TryCreate(string name, byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            BitmapSource frame = decoder.Frames[0];
            frame.Freeze();
            return new ImagePreviewWindow(name, frame);
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException or InvalidOperationException
            or IOException or System.Runtime.InteropServices.COMException or OverflowException)
        { return null; }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
