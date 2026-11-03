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
            var picker = (ComboBox)window.FindName("LanguagePicker");
            var categoryButton = (Button)window.FindName("LoginCategory");
            var translatedLabels = new HashSet<string>();
            for (int index = 0; index < picker.Items.Count; index++)
            {
                picker.SelectedIndex = index;
                if (categoryButton.Content is string label) translatedLabels.Add(label);
            }
            if (picker.Items.Count != 10 || translatedLabels.Count < 9)
                throw new Exception("Language selection did not update the entry category");
            picker.SelectedIndex = 0;
            // Render the application's own visual tree offscreen. No desktop screenshot or UI control.
            var view = (FrameworkElement)window.Content;
            window.Content = null;
            view.Resources.MergedDictionaries.Add(window.Resources);
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
            string textOnlyPath = Path.Combine(root, "synthetic-text-only.kdbx");
            using (var textOnly = VaultSession.Create(textOnlyPath, password))
            {
                Set("session", textOnly); Call("EnterWorkspace");
                Call("Add", ItemKind.Text);
                Control<TextBox>("TitleField").Text = "Synthetic text file";
                Control<TextBox>("TextField").Text = "Synthetic saved text";
                Call("Add", ItemKind.Text);
                Assert(Control<ListBox>("ItemList").Items.Count == 1, "The first draft disappeared while adding a second file");
                Control<TextBox>("TitleField").Text = "Synthetic second file";
                Call("SetCategory", ItemKind.Login);
                Assert(textOnly.Items.Count == 0, "Navigating saved the drafts before confirmation");
                Assert(Control<TextBlock>("EmptyEditor").Visibility == Visibility.Visible &&
                    Control<Button>("SaveButton").IsEnabled, "Global save unavailable without a selected item");
                Await((Task)Call("SaveChanges", new object?[] { null })!);
                Assert(Control<TextBlock>("SaveState").Text == "Сохранено", "The clean save indicator is incorrect");
                Assert(!Control<Button>("SaveButton").IsEnabled, "Save remained enabled after saving");
                Await((Task)Call("LockVault")!);
            }
            using (var reopened = VaultSession.Open(textOnlyPath, password))
            {
                Assert(reopened.Items.Count == 2, "A draft from the batch was not saved");
                Set("session", reopened); Call("EnterWorkspace");
                Assert(Control<ListBox>("ItemList").Items.Count == 2, "The reopened vault hid its text files");
                Assert(Control<Button>("TextCategory").Content?.ToString() == "Файлы · 2", "The file count was not shown");
                var search = Control<TextBox>("Search");
                var searchHint = Control<TextBlock>("SearchHint");
                Assert(searchHint.Visibility == Visibility.Visible, "Empty search is missing its hint");
                search.Text = "not present";
                Assert(searchHint.Visibility == Visibility.Collapsed, "The hint overlaps the search query");
                Assert(Control<Button>("ClearSearchButton").Visibility == Visibility.Visible &&
                    Control<TextBlock>("SearchNoResults").Visibility == Visibility.Visible,
                    "Active search was not indicated");
                Assert(Control<ListBox>("ItemList").Items.Count == 0, "Search did not filter files");
                Call("ClearSearchClick", null, new RoutedEventArgs());
                Assert(searchHint.Visibility == Visibility.Visible && Control<ListBox>("ItemList").Items.Count == 2,
                    "Clearing search did not restore the hint and file");
                Await((Task)Call("LockVault")!);
            }
            using var session = VaultSession.Create(vaultPath, password);
            string parentId = session.Folders[1].Id;
            session.AddFolder("Synthetic child", parentId);
            Set("session", session); Call("EnterWorkspace");
            var tree = Control<TreeView>("FolderList");
            var rootNode = (TreeViewItem)tree.Items[0];
            Assert(tree.Items.Count == 1 && rootNode.IsExpanded && ((FolderInfo)rootNode.Tag).Id == session.Folders[0].Id,
                "Folder tree did not show one expanded root");
            var parent = rootNode.Items.Cast<TreeViewItem>().Single(node => ((FolderInfo)node.Tag).Id == parentId);
            parent.IsExpanded = true;
            var child = (TreeViewItem)parent.Items[0];
            string childId = ((FolderInfo)child.Tag).Id;
            child.IsSelected = true;
            Assert(Control<TextBlock>("FolderTitle").Text.EndsWith(" / Synthetic child"), "Selecting a nested folder did not show its path");
            var deleteFolder = Control<Button>("DeleteFolderButton");
            Control<TextBox>("Search").Text = "nonmatching query";
            Assert(deleteFolder.IsEnabled, "An empty folder could not be deleted with an active search");
            parent.IsSelected = true;
            Assert(!deleteFolder.IsEnabled && deleteFolder.ToolTip?.ToString()?.Contains("подпапки") == true,
                "Search made a folder with a child look deletable");
            Control<TextBox>("Search").Clear();
            Call("SelectFolder", "");
            Call("Add", ItemKind.Login);
            Control<TextBox>("TitleField").Text = "Почта Example";
            Control<TextBox>("LoginField").Text = "demo@example.com";
            Control<PasswordBox>("PasswordField").Password = "Fictional-UI-secret";
            Control<TextBox>("SiteField").Text = "https://example.com";
            Assert(Control<Button>("SaveButton").IsEnabled, "Save disabled for nonempty draft");
            Assert(session.Items.Count == 0, "Typing saved automatically");
            Await((Task)Call("SaveChanges", new object?[] { null })!);
            Assert(session.Items.Count == 1, "Save did not persist login");
            Assert(Control<TextBlock>("SaveState").Text.Contains("Сохранено"), "Save state incorrect");
            Assert(!Control<Button>("RevertButton").IsEnabled, "Revert enabled without edits");
            Assert(window.WindowState == WindowState.Normal, "The app opens maximized");
            // Reserve room for the title bar and resize borders in this offscreen layout.
            var defaultViewport = new Size(window.Width - 20, window.Height - 50);
            foreach (var size in new[] { new Size(950, 640), new Size(1600, 900), defaultViewport })
            {
                view.Measure(size);
                view.Arrange(new Rect(new Point(), size));
                view.UpdateLayout();
            }
            Assert(Control<ScrollViewer>("EditorScroll").ScrollableHeight == 0, "Entry fields did not fit at the default window size");
            Capture("login-editor");
            Control<TextBox>("TitleField").Text = "Изменённый черновик";
            Assert(Control<Button>("RevertButton").IsEnabled, "Revert disabled with unsaved edits");
            Call("RevertClick", null, new RoutedEventArgs());
            Assert(Control<TextBox>("TitleField").Text == "Почта Example", "Revert failed");
            Assert(!Control<Button>("SaveButton").IsEnabled, "Save remained enabled after reverting the only change");
            Call("Add", ItemKind.Text);
            Assert(!Control<Button>("SaveButton").IsEnabled, "Save was enabled for a blank draft");
            Call("SetCategory", ItemKind.Login);
            Assert(session.Items.Count == 1, "Blank draft retained");
            Call("Add", ItemKind.Text);
            Control<TextBox>("TitleField").Text = "Домашняя сеть.txt";
            Control<TextBox>("TextField").Text = "Вымышленная заметка\n\nИмя сети: Example Home\nРоутер: demo-router";
            Await((Task)Call("SaveChanges", new object?[] { null })!);
            Assert(session.Items.Count == 2, "Text file not saved");
            Assert(Control<StackPanel>("LoginFields").Visibility == Visibility.Collapsed, "Login fields visible for text");
            Assert(Control<TextBox>("TextField").VerticalContentAlignment == VerticalAlignment.Top,
                "Text contents were not aligned to the top");
            var textItem = session.Items.Single(item => item.Kind == ItemKind.Text);
            Call("MoveItemDraft", textItem.Id, childId);
            Assert(session.GetItem(textItem.Id).FolderId == textItem.FolderId &&
                Control<Button>("SaveButton").IsEnabled, "Moving an item should stage a change");
            Control<TextBox>("Search").Text = "nonmatching query";
            Assert((int)child.Header.GetType().GetProperty("Count")!.GetValue(child.Header)! == 1,
                "Search hid the folder's actual item count");
            Control<TextBox>("Search").Clear();
            Await((Task)Call("SaveChanges", new object?[] { null })!);
            Capture("text-editor");
            Await((Task)Call("LockVault")!);
            using (var reopened = VaultSession.Open(vaultPath, password))
            {
                Assert(reopened.Items.Count == 2, "UI saves not readable after reopen");
                Assert(reopened.GetItem(textItem.Id).FolderId == childId, "Moved file did not keep its destination");
                Assert(reopened.Folders.Any(folder => folder.ParentId == parentId && folder.DisplayName == "Synthetic child"),
                    "Nested folder did not survive reopening");
                Set("session", reopened); Call("EnterWorkspace");
                var list = Control<ListBox>("ItemList");
                Assert(list.Items.Count == 2, "Reopening did not show both entries and files");
                Assert(Control<Button>("AllCategory").Content?.ToString() == "Все · 2", "The All category count was not shown");
                Call("SetCategory", ItemKind.Login);
                Assert(list.Items.Cast<VaultItem>().Single().Kind == ItemKind.Login, "The entry filter did not hide files");
                Call("SetCategory", ItemKind.Text);
                Assert(list.Items.Cast<VaultItem>().Single().Kind == ItemKind.Text, "The file filter did not hide entries");
                Call("SetCategory", new object?[] { null });
                Assert(list.Items.Count == 2, "The All category did not restore both item types");
                Await((Task)Call("LockVault")!);
            }
            Assert(Control<PasswordBox>("PasswordField").Password.Length == 0, "Password field not cleared on lock");
            Assert(Control<ListBox>("ItemList").Items.Count == 0, "Items not cleared on lock");
            using var closingSession = VaultSession.Open(vaultPath, password);
            Set("session", closingSession); Call("EnterWorkspace");
            bool closed = false;
            window.Closed += (_, _) => closed = true;
            window.Close();
            Assert(closed && Control<Grid>("Welcome").Visibility == Visibility.Collapsed,
                "Closing an unchanged vault switched to the welcome screen or delayed closing");
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
