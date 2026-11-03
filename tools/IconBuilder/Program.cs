using System.IO;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        const string folder = "src/EasyPassVault.App/Assets";
        using var source = File.OpenRead(Path.Combine(folder, "VaultIcon.xaml"));
        var image = (DrawingImage)XamlReader.Load(source);
        int[] sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
        var frames = new List<byte[]>();
        foreach (int size in sizes)
        {
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen()) context.DrawImage(image, new Rect(0, 0, size, size));
            var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
            using var buffer = new MemoryStream(); png.Save(buffer); frames.Add(buffer.ToArray());
            if (size == 256) File.WriteAllBytes(Path.Combine(folder, "vault.png"), buffer.ToArray());
        }
        using var output = new BinaryWriter(File.Create(Path.Combine(folder, "vault.ico")));
        output.Write((ushort)0); output.Write((ushort)1); output.Write((ushort)sizes.Length);
        uint offset = (uint)(6 + 16 * sizes.Length);
        for (int i = 0; i < sizes.Length; i++)
        {
            output.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); output.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
            output.Write((byte)0); output.Write((byte)0); output.Write((ushort)1); output.Write((ushort)32);
            output.Write((uint)frames[i].Length); output.Write(offset); offset += (uint)frames[i].Length;
        }
        foreach (byte[] frame in frames) output.Write(frame);
        Console.WriteLine("Built transparent icon frames: " + string.Join(", ", sizes));
    }
}
