using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EasyPassVault.Core;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        string root = Path.GetFullPath(".artifacts/ui-tests/" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            var assembly = Assembly.LoadFrom(Path.GetFullPath("src/EasyPassVault.App/bin/Debug/net10.0-windows/win-x64/easy-pass-vault.dll"));
            var app = (Application)Activator.CreateInstance(assembly.GetType("EasyPassVault.App.App")!)!;
            app.GetType().GetMethod("InitializeComponent")!.Invoke(app, null);
            var window = (Window)Activator.CreateInstance(assembly.GetType("EasyPassVault.App.MainWindow")!)!;
            // Render the application's own visual tree offscreen. No desktop screenshot or UI control.
            var view = (FrameworkElement)window.Content;
            window.Content = null;
            object? Call(string name, params object?[] args) => window.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, args);
            void Set(string name, object? value) => window.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(window, value);
            T Control<T>(string name) => (T)window.FindName(name);
            void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
            void Await(Task task)
            {
                if (!task.IsCompleted)
                {
                    var frame = new DispatcherFrame();
                    var dispatcher = Dispatcher.CurrentDispatcher;
                    task.ContinueWith(_ => dispatcher.BeginInvoke(() => frame.Continue = false), TaskScheduler.Default);
                    Dispatcher.PushFrame(frame);
                }
                task.GetAwaiter().GetResult();
            }
            void Capture(string name)
            {
                view.Measure(new Size(1180, 800)); view.Arrange(new Rect(0, 0, 1180, 800)); view.UpdateLayout();
                var bitmap = new RenderTargetBitmap(1180, 800, 96, 96, PixelFormats.Pbgra32); bitmap.Render(view);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(root, name + ".png")); encoder.Save(file);
            }
            Capture("welcome");
            string vaultPath = Path.Combine(root, "synthetic-ui.kdbx");
            const string password = "Fictional-UI-password-482!";
            using var session = VaultSession.Create(vaultPath, password);
            Set("session", session); Call("EnterWorkspace");
            Await((Task)Call("Add", ItemKind.Login)!);
            Control<TextBox>("TitleField").Text = "Почта Example";
            Control<TextBox>("LoginField").Text = "demo@example.com";
            Control<PasswordBox>("PasswordField").Password = "Fictional-UI-secret";
            Control<TextBox>("SiteField").Text = "https://example.com";
            Assert(Control<Button>("SaveButton").IsEnabled, "Save disabled for nonempty draft");
            Assert(session.Items.Count == 0, "Typing saved automatically");
            Await((Task)Call("SaveDraft")!);
            Assert(session.Items.Count == 1, "Save did not persist login");
            Assert(Control<TextBlock>("SaveState").Text.Contains("Сохранено"), "Save state incorrect");
            Capture("login-editor");
            Control<TextBox>("TitleField").Text = "Изменённый черновик";
            Call("RevertClick", null, new RoutedEventArgs());
            Assert(Control<TextBox>("TitleField").Text == "Почта Example", "Revert failed");
            Await((Task)Call("Add", ItemKind.Text)!);
            Assert(!Control<Button>("SaveButton").IsEnabled, "Blank draft save enabled");
            Await((Task)Call("SetCategory", ItemKind.Login)!);
            Assert(session.Items.Count == 1, "Blank draft retained");
            Await((Task)Call("Add", ItemKind.Text)!);
            Control<TextBox>("TitleField").Text = "Домашняя сеть.txt";
            Control<TextBox>("TextField").Text = "Вымышленная заметка\n\nИмя сети: Example Home\nРоутер: demo-router";
            Await((Task)Call("SaveDraft")!);
            Assert(session.Items.Count == 2, "Text file not saved");
            Assert(Control<StackPanel>("LoginFields").Visibility == Visibility.Collapsed, "Login fields visible for text");
            Capture("text-editor");
            using (var reopened = VaultSession.Open(vaultPath, password)) Assert(reopened.Items.Count == 2, "UI saves not readable after reopen");
            Await((Task)Call("LockVault")!);
            Assert(Control<PasswordBox>("PasswordField").Password.Length == 0, "Password field not cleared on lock");
            Assert(Control<ListBox>("ItemList").Items.Count == 0, "Items not cleared on lock");
            Console.WriteLine("PASS WPF: manual save, saved status, revert, blank draft, both categories, reopen, lock cleanup");
            Console.WriteLine("Offscreen UI renders: " + root);
            app.Shutdown(); return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL UI " + ex.GetBaseException().GetType().Name + ": " + ex.GetBaseException().Message);
            return 1;
        }
    }
}
