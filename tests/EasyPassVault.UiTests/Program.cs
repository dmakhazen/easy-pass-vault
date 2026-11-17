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
            var languageMenu = (MenuItem)window.FindName("LanguageMenu");
            var categoryButton = (Button)window.FindName("LoginCategory");
            var lockLabel = (TextBlock)window.FindName("LockLabel");
            void SetLanguage(int index) =>
                window.GetType().GetMethod("SetLanguage", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, [index]);
            var translatedLabels = new HashSet<string>();
            var closeLabels = new HashSet<string>();
            for (int index = 0; index < languageMenu.Items.Count; index++)
            {
                SetLanguage(index);
                if (categoryButton.Content is string label) translatedLabels.Add(label);
                closeLabels.Add(lockLabel.Text);
                if (!((MenuItem)languageMenu.Items[index]!).IsChecked)
                    throw new Exception("The chosen language is not marked in the menu");
            }
            if (languageMenu.Items.Count != 10 || translatedLabels.Count < 9 || closeLabels.Count != 10)
                throw new Exception("Language selection did not update the entry category or the close button");
            SetLanguage(0);
            if (lockLabel.Text != "Закрыть хранилище") throw new Exception("The lock button was not renamed");
            if (((FrameworkElement)window.FindName("MenuButton")).Visibility != Visibility.Visible ||
                languageMenu.Visibility != Visibility.Visible ||
                ((MenuItem)window.FindName("ExportMenuItem")).Visibility == Visibility.Visible)
                throw new Exception("The start screen must offer the language menu and nothing vault-specific");
            // Render the application's own visual tree offscreen. No desktop screenshot or UI control.
            var view = (FrameworkElement)window.Content;
            window.Content = null;
            view.Resources.MergedDictionaries.Add(window.Resources);
            object? Call(string name, params object?[] args) => window.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, args);
            object? Get(string name) => window.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window);
            void Click(UIElement button) => button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
            {
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
                {
                    var child = VisualTreeHelper.GetChild(parent, i);
                    if (child is T match) yield return match;
                    foreach (var nested in Descendants<T>(child)) yield return nested;
                }
            }
            void Set(string name, object? value) => window.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(window, value);
            T Control<T>(string name) => (T)window.FindName(name);
            void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
            T Await2<T>(Task<T> task) { Await(task); return task.Result; }
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
            RenderTargetBitmap Capture(string name)
            {
                int width = (bool)window.GetType().GetField("compact", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)! ? 444 : 1164;
                // Let item lists regenerate their rows first, as the running app does between frames.
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
                view.Measure(new Size(width, 721)); view.Arrange(new Rect(0, 0, width, 721)); view.UpdateLayout();
                var bitmap = new RenderTargetBitmap(width, 721, 96, 96, PixelFormats.Pbgra32); bitmap.Render(view);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(root, name + ".png")); encoder.Save(file);
                return bitmap;
            }
            Color PixelAt(RenderTargetBitmap bitmap, Point point)
            {
                var pixel = new byte[4];
                bitmap.CopyPixels(new Int32Rect((int)point.X, (int)point.Y, 1, 1), pixel, 4, 0);
                return Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
            }
            // A point of the text box in the rendered frame: on the right end of the given line (no text there).
            Point LineEndPoint(TextBox box, int index, bool middle)
            {
                Rect line = box.GetRectFromCharacterIndex(index);
                double y = middle ? Math.Floor(line.Top + line.Height / 2) : line.Top + 3;
                return box.TranslatePoint(new Point(box.ActualWidth - 30, y), view);
            }
            var bandColor = Color.FromRgb(0xD5, 0xE6, 0xDB);
            var ruleColor = Color.FromRgb(0xA9, 0xB9, 0xAF);
            void AssertMarkupFrame(string name, string text)
            {
                var box = Control<TextBox>("TextField");
                var frame = Capture(name);
                var shown = (System.Collections.IEnumerable)Get("markupView")!.GetType().GetProperty("Shown", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(Get("markupView"))!;
                Assert(shown.Cast<object>().Count() == TextMarkup.Parse(text).Count && shown.Cast<object>().All(mark => (bool)mark.GetType().GetField("Item2")!.GetValue(mark)!),
                    "Headings and separators are not all drawn with hidden marks without a caret: " + name);
                foreach (var line in TextMarkup.Parse(text))
                {
                    var color = PixelAt(frame, LineEndPoint(box, line.Start, line.Kind == MarkupKind.Separator));
                    Assert(color == (line.Kind == MarkupKind.Heading ? bandColor : ruleColor),
                        $"No {line.Kind} drawn in {name} at {line.Start}: {color}");
                }
            }
            // Settings (recent files) go to the test folder, never next to the test runner.
            Set("layoutPath", Path.Combine(root, WindowLayout.FileName));
            Set("layout", new WindowLayout());
            Call("UpdateWelcome");
            Assert(Control<StackPanel>("WelcomeIntro").Visibility == Visibility.Visible &&
                Control<StackPanel>("WelcomeRecent").Visibility == Visibility.Collapsed &&
                (string)Control<Button>("WelcomeOpenButton").Content == "Открыть файл хранилища",
                "The first start screen without recent files changed");
            Capture("welcome");
            object CopyFieldValue(string name) => Enum.Parse(window.GetType().GetNestedType("CopyField", BindingFlags.NonPublic)!, name);
            var keySource = new OffscreenSource();
            void Key(UIElement target, string handler, System.Windows.Input.Key key) =>
                Call(handler, target, new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, keySource, 0, key)
                    { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent });
            WindowLayout CurrentLayout() => (WindowLayout)Get("layout")!;
            IEnumerable<T> LogicalDescendants<T>(DependencyObject parent) where T : DependencyObject
            {
                foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
                {
                    if (child is T match) yield return match;
                    foreach (var nested in LogicalDescendants<T>(child)) yield return nested;
                }
            }
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
                Await((Task)Call("SaveChanges")!);
                Assert(Control<TextBlock>("SaveState").Text.StartsWith("Последнее сохранение: "), "The clean save indicator is incorrect");
                Assert(!Control<Button>("SaveButton").IsEnabled, "Save remained enabled after saving");
                Assert(textOnly.FilePath == Path.GetFullPath(textOnlyPath), "Plain save switched to another file");
                string today = DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                var versionButton = Control<Button>("SaveVersionButton");
                string VersionLabel() => ((TextBlock)versionButton.Content).Text;
                string FullVersionAction() => ((TextBlock)Control<MenuItem>("NewFileMenuItem").Header).Text;
                Assert(VersionLabel() == "Новый файл v1", "The new-version button does not show the short next version");
                Assert(FullVersionAction() == $"Создать новый файл и зашифровать в synthetic-text-only_{today}_v1.kdbx" &&
                    (string)versionButton.ToolTip == FullVersionAction(),
                    "The menu and the tooltip do not show the full next file name");
                Assert(Control<MenuItem>("ExportMenuItem").Visibility == Visibility.Visible &&
                    (string)Control<MenuItem>("ExportMenuItem").Header == "Экспорт в текст…" &&
                    !Control<MenuItem>("NewFileMenuItem").IsEnabled, "The menu of an open vault is incomplete");
                Assert(!versionButton.IsEnabled, "The new-version button is enabled without changes");
                Await((Task)Call("SaveNewVersionFile")!);
                Assert(textOnly.FilePath == Path.GetFullPath(textOnlyPath), "A new version was created without changes");

                Call("SetCategory", new object?[] { null });
                var list = Control<ListBox>("ItemList");
                list.SelectedItem = list.Items.Cast<VaultItem>().First();
                Assert(!versionButton.IsEnabled, "Selecting an item enabled the new-version button");
                Control<TextBox>("TextField").Text += "\nEdited before a new version";
                Assert(versionButton.IsEnabled && Control<Button>("SaveButton").IsEnabled &&
                    Control<MenuItem>("NewFileMenuItem").IsEnabled, "An edit did not enable both save buttons");
                Assert(Control<TextBlock>("SaveState").Text.StartsWith("Не сохранено правок: 1 · ") &&
                    Control<TextBlock>("SaveState").Foreground == (Brush)app.FindResource("PendingSaveForeground"),
                    "The header does not count unsaved edits in amber");
                Await((Task)Call("SaveNewVersionFile")!);
                Assert(Path.GetFileName(textOnly.FilePath) == $"synthetic-text-only_{today}_v1.kdbx" &&
                    Control<TextBlock>("VaultName").Text == Path.GetFileName(textOnly.FilePath) &&
                    VersionLabel() == "Новый файл v2" && FullVersionAction().EndsWith($"synthetic-text-only_{today}_v2.kdbx"),
                    "Creating a new version did not switch files or update the button");
                Assert(!versionButton.IsEnabled && !Control<Button>("SaveButton").IsEnabled,
                    "Save buttons stayed enabled after creating a new version");
                Assert(File.Exists(textOnlyPath), "The previous file disappeared");
                Assert(CurrentLayout().RecentVaults.Select(item => item.Path).SequenceEqual([textOnly.FilePath, Path.GetFullPath(textOnlyPath)]),
                    "The new version file did not become the first recent file");

                // Versions of this vault: the list, the next file, Explorer, and switching to another version.
                string? explorerPath = null;
                Set("showInExplorer", (Action<string>)(path => explorerPath = path));
                Assert(Control<MenuItem>("VersionsMenuItem").Visibility == Visibility.Visible &&
                    (string)Control<MenuItem>("VersionsMenuItem").Header == "Версии этого хранилища…", "The menu has no versions item");
                var versions = (Window)Call("CreateVersionsWindow", textOnly)!;
                var versionTexts = LogicalDescendants<TextBlock>(versions).Select(block => block.Text).ToArray();
                Assert(versionTexts.Contains(Path.GetFileName(textOnly.FilePath)) && versionTexts.Count(text => text == "открыта") == 1 &&
                    versionTexts.Any(text => text.Contains("самая новая")) &&
                    versionTexts.Contains($"Следующий файл: synthetic-text-only_{today}_v2.kdbx") &&
                    versions.Title == "Версии хранилища «synthetic-text-only»", "The versions window misses the file, its marks or the next file");
                Click(LogicalDescendants<Button>(versions).Single(button => (string)button.Content == "Показать в проводнике"));
                Assert(explorerPath == textOnly.FilePath, "Explorer was not asked to select the open file");
                versions.Close();
                Assert((bool)Await2((Task<bool>)Call("OpenOtherVersion", textOnlyPath)!) && Get("session") is null &&
                    Control<Grid>("Welcome").Visibility == Visibility.Visible &&
                    Control<TextBlock>("RecentName").Text == "synthetic-text-only.kdbx" &&
                    Control<TextBlock>("RecentInfo").Text.Contains("есть версия новее") &&
                    Control<PasswordBox>("RecentPassword").Password.Length == 0,
                    "Choosing another version did not close the vault and show that file on the start screen");
                textOnlyPath = textOnly.FilePath;
                Await((Task)Call("LockVault")!);
            }
            using (var reopened = VaultSession.Open(textOnlyPath, password))
            {
                Assert(reopened.Items.Count == 2, "A draft from the batch was not saved");
                Set("session", reopened); Call("EnterWorkspace");
                Assert(Control<ListBox>("ItemList").Items.Count == 2, "The reopened vault hid its text files");
                Assert(Control<Button>("TextCategory").Content?.ToString() == "Файлы 2", "The file count was not shown");
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
            // The folder menu replaces the three buttons under the tree.
            var folderMenu = Control<ContextMenu>("FolderMenu").Items.OfType<MenuItem>().ToArray();
            Assert(folderMenu.Select(item => (string)item.Header).SequenceEqual(["Новая вложенная папка", "Переименовать", "Удалить папку"]) &&
                folderMenu[1].InputGestureText == "F2" && folderMenu[2].InputGestureText == "Del",
                "The folder menu does not have its three actions");
            Assert(window.FindName("DeleteFolderButton") is null && Control<Button>("AddFolderButton").Content?.ToString() == "+ Новая папка",
                "The buttons under the folder tree were not reduced to New folder");
            var deleteFolder = Control<MenuItem>("FolderMenuDelete");
            Assert(Control<MenuItem>("FolderMenuRename").IsEnabled && folderMenu[0].IsEnabled, "A selected folder cannot be renamed from the menu");
            Control<TextBox>("Search").Text = "nonmatching query";
            Assert(deleteFolder.IsEnabled, "An empty folder could not be deleted with an active search");
            parent.IsSelected = true;
            Assert(!deleteFolder.IsEnabled && deleteFolder.ToolTip?.ToString()?.Contains("подпапки") == true,
                "Search made a folder with a child look deletable");
            Control<TextBox>("Search").Clear();
            rootNode.IsSelected = true;
            Assert(!deleteFolder.IsEnabled && deleteFolder.ToolTip?.ToString() == "Корневую папку удалить нельзя." &&
                !Control<MenuItem>("FolderMenuRename").IsEnabled, "The vault root can be renamed or deleted from the menu");
            Call("SelectFolder", "");
            Assert(!deleteFolder.IsEnabled && !folderMenu[0].IsEnabled, "The folder menu is active without a selected folder");

            // One-click copy. The clipboard and the browser are replaced: the test never touches the real ones.
            string? copied = null;
            bool clipboardBusy = false;
            Uri? opened = null;
            Set("writeClipboard", (Action<string>)(text =>
            {
                if (clipboardBusy) throw new System.Runtime.InteropServices.COMException("Synthetic busy clipboard");
                copied = text;
            }));
            Set("openInBrowser", (Action<Uri>)(uri => opened = uri));
            var status = Control<TextBlock>("Status");
            var copyLogin = Control<Button>("CopyLoginButton");
            var copyPassword = Control<Button>("CopyPasswordButton");
            var copySite = Control<Button>("CopySiteButton");
            var openSite = Control<Button>("OpenSiteButton");
            Call("Add", ItemKind.Login);
            Assert(!copyLogin.IsEnabled && !copyPassword.IsEnabled && !copySite.IsEnabled && !openSite.IsEnabled,
                "Copy buttons are enabled for empty values");
            Click(copyPassword);
            Assert(copied is null, "An empty value was copied");
            Control<TextBox>("TitleField").Text = "Почта Example";
            Control<TextBox>("LoginField").Text = "demo@example.com";
            Assert(copyLogin.IsEnabled && !copyPassword.IsEnabled && !copySite.IsEnabled, "Only the filled field may be copied");
            Control<PasswordBox>("PasswordField").Password = "Fictional-UI-secret";
            Control<TextBox>("SiteField").Text = "example.com";
            Assert(copyPassword.IsEnabled && copySite.IsEnabled && !openSite.IsEnabled &&
                openSite.ToolTip?.ToString()?.Contains("https://") == true, "A site without http(s) can be opened");
            Control<TextBox>("SiteField").Text = "https://example.com";
            Assert(openSite.IsEnabled && openSite.ToolTip is null, "A valid https address cannot be opened");
            Click(copyPassword);
            Assert(copied == "Fictional-UI-secret" && status.Text == "Пароль скопирован", "The unsaved password of the draft was not copied");
            Click(copyLogin);
            Assert(copied == "demo@example.com" && status.Text == "Логин скопирован", "The login was not copied");
            Click(copySite);
            Assert(copied == "https://example.com" && status.Text == "Адрес скопирован", "The site was not copied");
            Click(openSite);
            Assert(opened?.AbsoluteUri == "https://example.com/", "The site was not passed to the browser");
            clipboardBusy = true; copied = null;
            Click(copyPassword);
            Assert(copied is null && status.Text.StartsWith("Буфер обмена занят") && !status.Text.Contains("Fictional"),
                "A busy clipboard was not reported in the status line");
            clipboardBusy = false;
            Assert(Control<Button>("SaveButton").IsEnabled, "Save disabled for nonempty draft");
            Assert(session.Items.Count == 0, "Typing saved automatically");
            Await((Task)Call("SaveChanges")!);
            Assert(session.Items.Count == 1, "Save did not persist login");
            Assert(Control<TextBlock>("SaveState").Text.StartsWith("Последнее сохранение: "), "Save state incorrect");
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
            // The fields up to the note fit; the attachments section below them may scroll.
            var editorScroll = Control<ScrollViewer>("EditorScroll");
            var textHost = Control<Grid>("TextHost");
            Assert(textHost.TranslatePoint(new Point(0, textHost.ActualHeight), editorScroll).Y <= editorScroll.ViewportHeight,
                "Entry fields did not fit at the default window size");
            Capture("login-editor");

            // Password generator: "Use in entry" puts the password into the draft like typing does; nothing is saved.
            Assert(Control<Button>("GeneratePasswordButton").IsVisible || Control<Button>("GeneratePasswordButton").Visibility == Visibility.Visible,
                "The generator button is missing from the password row");
            Assert(Control<StackPanel>("TextTools").Visibility == Visibility.Visible, "The note of an entry has no heading and separator buttons");
            var generator = (Window)Call("CreateGeneratorWindow")!;
            string generated = (string)generator.GetType().GetProperty("Password")!.GetValue(generator)!;
            var applyGenerated = (Button)generator.GetType().GetProperty("ApplyButton", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(generator)!;
            Assert(generated.Length == 20 && applyGenerated.IsEnabled && !Control<Button>("SaveButton").IsEnabled,
                "The generator did not open with a 20-character password");
            var generatorView = (FrameworkElement)generator.Content;
            generatorView.Measure(new Size(444, double.PositiveInfinity)); generatorView.Arrange(new Rect(0, 0, 444, generatorView.DesiredSize.Height)); generatorView.UpdateLayout();
            var generatorFrame = new RenderTargetBitmap(444, (int)Math.Ceiling(generatorView.DesiredSize.Height), 96, 96, PixelFormats.Pbgra32);
            generatorFrame.Render(generatorView);
            var generatorEncoder = new PngBitmapEncoder(); generatorEncoder.Frames.Add(BitmapFrame.Create(generatorFrame));
            using (var file = File.Create(Path.Combine(root, "generator.png"))) generatorEncoder.Save(file);
            Click(applyGenerated);
            Assert(Control<PasswordBox>("PasswordField").Password == generated && Control<Button>("SaveButton").IsEnabled &&
                Control<Button>("RevertButton").IsEnabled && status.Text == "Пароль подставлен. Сохраните запись.",
                "The generated password did not become an unsaved change of the draft");
            Assert(session.GetItem(session.Items.Single().Id).Password == "Fictional-UI-secret", "Using a generated password saved the vault");
            var noSets = (Window)Call("CreateGeneratorWindow")!;
            var boxes = LogicalDescendants<CheckBox>(noSets).ToArray();
            Assert(boxes.Length == 5, "The generator must have five options");
            foreach (var box in boxes.Take(4)) box.IsChecked = false;
            var noSetsApply = (Button)noSets.GetType().GetProperty("ApplyButton", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(noSets)!;
            Assert(!noSetsApply.IsEnabled && (string)noSets.GetType().GetProperty("Password")!.GetValue(noSets)! == "",
                "Use in entry is enabled without character sets");
            boxes[0].IsChecked = true;
            ((System.Windows.Controls.Primitives.RangeBase)LogicalDescendants<Slider>(noSets).Single()).Value = 33;
            noSets.Close();
            Assert(CurrentLayout().Generator == new PasswordOptions { Length = 33, Lower = false, Digits = false, Symbols = false },
                "The generator settings were not remembered");
            Assert(!System.Text.Json.JsonSerializer.Serialize(CurrentLayout()).Contains(generated, StringComparison.Ordinal),
                "The settings hold the generated password");
            Set("layout", CurrentLayout() with { Generator = new PasswordOptions() });
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
            Await((Task)Call("SaveChanges")!);
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
            Await((Task)Call("SaveChanges")!);
            Capture("text-editor");

            // "Heading" and "Separator" edit the text as typing does: unsaved draft, Ctrl+Z, the caret's line.
            var textField = Control<TextBox>("TextField");
            string savedText = textField.Text;
            textField.CaretIndex = 3;
            Call("HeadingClick", null, new RoutedEventArgs());
            Assert(textField.Text == "# " + savedText && Control<Button>("SaveButton").IsEnabled &&
                ((VaultItem)Get("draft")!).Text == textField.Text && textField.CaretIndex == 5, "Heading did not mark the caret's line");
            textField.Undo();
            Assert(textField.Text == savedText && !Control<Button>("SaveButton").IsEnabled, "Ctrl+Z did not undo the heading in one step");
            textField.Redo();
            Assert(textField.Text == "# " + savedText, "Redo did not restore the heading");
            textField.CaretIndex = textField.Text.IndexOf("Имя сети", StringComparison.Ordinal) + 2;
            Call("DividerClick", null, new RoutedEventArgs());
            string marked = "# Вымышленная заметка\n\nИмя сети: Example Home\n---\nРоутер: demo-router";
            Assert(textField.Text == marked && textField.CaretIndex == marked.IndexOf("Роутер", StringComparison.Ordinal),
                "Separator was not inserted after the caret's line");
            textField.CaretIndex = 4;
            Call("HeadingClick", null, new RoutedEventArgs());
            Assert(textField.Text == marked[2..], "Heading was not removed from a heading line");
            textField.Undo();
            Assert(textField.Text == marked, "Ctrl+Z did not bring the heading back");
            AssertMarkupFrame("text-markup", marked);
            Call("RevertClick", null, new RoutedEventArgs());
            Assert(textField.Text == savedText && !Control<Button>("SaveButton").IsEnabled, "Revert did not drop the markup edits");

            // Frame of the redesign: header height, area widths, list badges, compact view.
            var header = Control<Border>("HeaderBar");
            Assert(header.ActualHeight == 56, "The header is not a single 56 px row");
            Assert(Control<Border>("FoldersPane").ActualWidth == 220 && Control<Border>("ListPane").ActualWidth == 310,
                "Default area widths differ from the mockup");
            var converter = (System.Windows.Data.IValueConverter)window.Resources["ItemTitleConverter"];
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            Assert((string)converter.Convert(textItem, typeof(string), "letter", culture) == "Д" &&
                (string)converter.Convert(textItem, typeof(string), "sub", culture) == "Текстовый файл" &&
                (string)converter.Convert(session.Items.Single(item => item.Kind == ItemKind.Login), typeof(string), "sub", culture) == "demo@example.com",
                "List rows do not show the letter badge and the second line");
            var listColumn = Control<ColumnDefinition>("ListColumn");
            listColumn.Width = new GridLength(900);
            Call("SplitterDragCompleted", null, null);
            Assert(listColumn.Width.Value == 1164 - 220 - 380, "A dragged border left no room for the editor");
            listColumn.Width = new GridLength(310);
            Call("SetCompact", true);
            Capture("compact-editor");
            Assert(Control<Border>("FoldersPane").Visibility == Visibility.Collapsed &&
                Control<Border>("ListPane").Visibility == Visibility.Collapsed &&
                Control<Grid>("EditorPane").Visibility == Visibility.Visible && Control<Grid>("EditorPane").ActualWidth == 444 &&
                Control<Button>("BackButton").Visibility == Visibility.Visible,
                "The compact view did not open the selected item in place of the list");
            string compactMarked = "# Домашний Wi-Fi\nСеть: Primer-Home\n  ---  \n# Гостевая сеть с очень длинным названием, которое переносится на вторую строку\n## не заголовок\n----\nКонец";
            Control<TextBox>("TextField").Text = compactMarked;
            AssertMarkupFrame("text-markup-compact", compactMarked);
            Call("RevertClick", null, new RoutedEventArgs());
            Assert(window.MinWidth <= 460 && window.Width == 460, "The compact window cannot be about 460 px wide");
            Call("BackToListClick", null, new RoutedEventArgs());
            Capture("compact-list");
            Assert(Control<Border>("ListPane").Visibility == Visibility.Visible && Control<Border>("ListPane").ActualWidth == 444 &&
                Control<Grid>("EditorPane").Visibility == Visibility.Collapsed,
                "Back to list did not return to the full-width list");
            var compactPicker = Control<ComboBox>("CompactFolderPicker");
            Assert(compactPicker.Visibility == Visibility.Visible && compactPicker.Items.Count == session.Folders.Count + 1 &&
                ((FolderInfo)compactPicker.SelectedItem).Id == childId, "The compact folder picker does not list all folders");
            compactPicker.SelectedIndex = 0;
            Call("SetCategory", new object?[] { null });
            Assert(Control<ListBox>("ItemList").Items.Count == 2, "Choosing all folders in the compact picker did not show every item");
            compactPicker.SelectedItem = compactPicker.Items.Cast<FolderInfo>().Single(folder => folder.Id == childId);
            Assert(Control<ListBox>("ItemList").Items.Cast<VaultItem>().Single().Id == textItem.Id,
                "The compact folder picker did not filter the list");
            Control<ListBox>("ItemList").SelectedIndex = 0;
            Assert(Control<Grid>("EditorPane").Visibility == Visibility.Visible &&
                Control<TextBox>("TitleField").Text == "Домашняя сеть.txt", "Selecting an item in the compact list did not open it");
            compactPicker.SelectedIndex = 0;
            Call("SetCompact", false);
            Capture("wide-again");
            Assert(window.Width == 1180 && window.MinWidth == 950 && Control<Border>("FoldersPane").ActualWidth == 220 &&
                Control<Border>("ListPane").ActualWidth == 310 && Control<Border>("ViewSwitch").Visibility == Visibility.Visible,
                "Returning to the wide view did not restore the three areas");

            // Copy buttons of the list rows: entries only, no change of selection.
            var rows = Control<ListBox>("ItemList");
            VaultItem Row(ItemKind kind) => rows.Items.Cast<VaultItem>().Single(item => item.Kind == kind);
            Button RowButton(ItemKind kind, string name)
            {
                view.UpdateLayout();
                return Descendants<Button>(rows.ItemContainerGenerator.ContainerFromItem(Row(kind))).Single(button => button.Name == name);
            }
            rows.SelectedItem = Row(ItemKind.Text);
            Assert(Row(ItemKind.Login) is { Password: "", HasPassword: true }, "List rows keep passwords in memory or forget that one exists");
            Assert(RowButton(ItemKind.Text, "RowCopyLogin").Visibility == Visibility.Collapsed &&
                RowButton(ItemKind.Text, "RowCopyPassword").Visibility == Visibility.Collapsed &&
                RowButton(ItemKind.Login, "RowCopyPassword").Visibility == Visibility.Collapsed,
                "Copy buttons are shown for a text file or for a row that is neither selected nor under the mouse");
            copied = null;
            Click(RowButton(ItemKind.Login, "RowCopyPassword"));
            Assert(copied == "Fictional-UI-secret" && status.Text == "Пароль скопирован" &&
                ((VaultItem)rows.SelectedItem).Kind == ItemKind.Text && Control<TextBox>("TitleField").Text == "Домашняя сеть.txt",
                "Copying from a list row failed or changed the selected item");
            Click(RowButton(ItemKind.Login, "RowCopyLogin"));
            Assert(copied == "demo@example.com" && status.Text == "Логин скопирован", "The login of a list row was not copied");
            rows.SelectedItem = Row(ItemKind.Login);
            Assert(RowButton(ItemKind.Login, "RowCopyLogin") is { Visibility: Visibility.Visible, IsEnabled: true } &&
                RowButton(ItemKind.Login, "RowCopyPassword") is { Visibility: Visibility.Visible, IsEnabled: true },
                "The selected entry does not show its two copy buttons");
            Control<TextBox>("LoginField").Text = "edited@example.com";
            Click(RowButton(ItemKind.Login, "RowCopyLogin"));
            Assert(copied == "edited@example.com", "A list row did not copy the edited draft value");
            Call("RevertClick", null, new RoutedEventArgs());
            Capture("row-copy");

            // Sorting is a view setting: it reorders the lists and leaves the vault and the save state alone.
            byte[] beforeSort = File.ReadAllBytes(session.FilePath);
            string Titles() => string.Join("|", rows.Items.Cast<VaultItem>().Select(item => item.Title));
            Assert(Titles() == "Домашняя сеть.txt|Почта Example" && (string)Control<Button>("ItemSortButton").Content == "По названию А→Я",
                "Items are not sorted by title by default");
            Call("SetItemSort", ItemSort.NameDescending);
            Assert(Titles() == "Почта Example|Домашняя сеть.txt", "Sorting by title Z to A failed");
            Call("SetItemSort", ItemSort.ModifiedOldest);
            Assert(Titles() == "Почта Example|Домашняя сеть.txt", "Sorting by date, oldest first, failed");
            Call("SetItemSort", ItemSort.ModifiedNewest);
            var sortMenu = Control<ContextMenu>("ItemSortMenu").Items.Cast<MenuItem>().ToArray();
            Assert(Titles() == "Домашняя сеть.txt|Почта Example" && sortMenu.Length == 4 &&
                sortMenu.Single(item => item.IsChecked).Header.ToString() == "По дате: сначала новые" &&
                (string)Control<Button>("ItemSortButton").Content == "По дате: сначала новые" &&
                ((VaultItem)rows.SelectedItem).Kind == ItemKind.Login, "Sorting by date, newest first, failed or lost the selection");
            string FolderOrder() => string.Join("|", ((TreeViewItem)tree.Items[0]).Items.Cast<TreeViewItem>().Select(node => ((FolderInfo)node.Tag).DisplayName));
            Assert(FolderOrder() == "Заметки|Личное|Работа" && (string)Control<Button>("FolderSortButton").Content == "А→Я",
                "Folders are not sorted by name by default");
            Call("FolderSortClick", null, new RoutedEventArgs());
            Assert(FolderOrder() == "Работа|Личное|Заметки" && (string)Control<Button>("FolderSortButton").Content == "Я→А" &&
                ((FolderInfo)((TreeViewItem)tree.Items[0]).Tag).Id == session.Folders[0].Id &&
                ((TreeViewItem)tree.Items[0]).Items.Cast<TreeViewItem>().Single(node => ((FolderInfo)node.Tag).Id == parentId).Items.Count == 1,
                "Reversing the folder order failed or broke the tree");
            var chosen = (WindowLayout)Get("layout")!;
            Assert(chosen.ItemSort == ItemSort.ModifiedNewest && chosen.FolderSort == FolderSort.NameDescending,
                "The chosen sort order is not kept with the window layout");
            Assert(!Control<Button>("SaveButton").IsEnabled && beforeSort.SequenceEqual(File.ReadAllBytes(session.FilePath)),
                "Sorting marked the vault as changed or wrote to it");
            Capture("sorted");
            Call("FolderSortClick", null, new RoutedEventArgs());
            Call("SetItemSort", ItemSort.NameAscending);

            // Quick search (Ctrl+K). The clipboard is the replaced one from above.
            var overlay = Control<Grid>("PaletteOverlay");
            var query = Control<TextBox>("PaletteQuery");
            var found = Control<ListBox>("PaletteList");
            var foundCount = Control<TextBlock>("PaletteCount");
            string RowInfo(int index) => (string)found.Items[index]!.GetType().GetProperty("Info")!.GetValue(found.Items[index])!;
            string FolderName(string id) => session.Folders.Single(folder => folder.Id == id).Name;
            var loginItem = session.Items.Single(item => item.Kind == ItemKind.Login);
            Assert(Control<Button>("QuickSearchButton") is { Visibility: Visibility.Visible } searchButton &&
                (string)searchButton.ToolTip == "Быстрый поиск (Ctrl+K)", "The header has no quick search button");
            Click(Control<Button>("QuickSearchButton"));
            Assert(overlay.Visibility == Visibility.Visible && found.Items.Count == 2 && foundCount.Text == "НАЙДЕНО 2",
                "Quick search did not open with every item");
            query.Text = "fictional-ui";
            Assert(found.Items.Count == 0 && foundCount.Text == "Ничего не найдено", "Quick search found a password");
            query.Text = "РОУТЕР";
            Assert(found.Items.Count == 1 && RowInfo(0) == "Текстовый файл · " + FolderName(childId), "Quick search over the text of a file failed");
            query.Text = "почта";
            Assert(found.Items.Count == 1 && found.SelectedIndex == 0 && RowInfo(0) == "demo@example.com · " + FolderName(loginItem.FolderId),
                "Quick search over titles failed or the row has no login and folder");
            Capture("palette");
            copied = null;
            Key(query, "PaletteKeyDown", System.Windows.Input.Key.Enter);
            Assert(copied == "Fictional-UI-secret" && status.Text == "Пароль скопирован" && overlay.Visibility == Visibility.Collapsed,
                "Enter did not copy the password and close quick search");
            Call("OpenPalette");
            query.Text = "почта";
            Call("PaletteCopy", CopyFieldValue("Login"));
            Assert(copied == "demo@example.com" && status.Text == "Логин скопирован" && overlay.Visibility == Visibility.Collapsed,
                "Ctrl+Enter did not copy the login");
            Call("OpenPalette");
            Key(query, "PaletteKeyDown", System.Windows.Input.Key.Escape);
            Assert(overlay.Visibility == Visibility.Collapsed, "Esc did not close quick search");
            Call("OpenPalette");
            overlay.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0,
                System.Windows.Input.MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
            Assert(overlay.Visibility == Visibility.Collapsed, "A click on the dimmed area did not close quick search");
            rows.SelectedItem = Row(ItemKind.Login);
            Call("OpenPalette");
            query.Text = "роутер";
            Key(query, "PaletteKeyDown", System.Windows.Input.Key.Enter);
            Assert(overlay.Visibility == Visibility.Collapsed && Control<TextBox>("TitleField").Text == "Домашняя сеть.txt" &&
                (string)Get("folderId")! == "" && ((VaultItem)rows.SelectedItem).Kind == ItemKind.Text,
                "Enter on a text file did not open it in the main window");
            Call("SetCompact", true);
            Call("BackToListClick", null, new RoutedEventArgs());
            Call("OpenPalette");
            Capture("palette-compact");
            query.Text = "почта";
            Key(query, "PaletteKeyDown", System.Windows.Input.Key.Tab);
            Assert(Control<Grid>("EditorPane").Visibility == Visibility.Visible && Control<TextBox>("TitleField").Text == "Почта Example" &&
                overlay.Visibility == Visibility.Collapsed, "Tab did not open the entry in the compact view");
            Call("SetCompact", false);

            // The search above the list looks through every folder; the open item shows its folder path as buttons;
            // Enter and the arrow button walk over the matches in the text, then over the found items.
            var mainSearch = Control<TextBox>("Search");
            var mainList = Control<ListBox>("ItemList");
            var matchCount = Control<TextBlock>("SearchCount");
            var nextMatch = Control<Button>("NextMatchButton");
            var folderTitle = Control<TextBlock>("FolderTitle");
            void Settle() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            string DraftId() => ((VaultItem)Get("draft")!).Id;
            string rootId = session.Folders[0].Id;
            Call("SelectFolder", rootId);
            Assert(mainList.Items.Count == 0 && nextMatch.Visibility == Visibility.Collapsed, "The vault root lists items of nested folders");
            mainSearch.Text = "E";
            Assert(mainList.Items.Count == 2 && folderTitle.Text == "Найдено 2 · во всех папках" &&
                nextMatch.Visibility == Visibility.Visible && (string)nextMatch.ToolTip == "Следующее совпадение (Enter)" &&
                (string)Get("folderId")! == rootId && Control<TreeView>("FolderList").Opacity < 1,
                "Search did not look through every folder");
            Key(mainSearch, "SearchKeyDown", System.Windows.Input.Key.Enter);
            Assert(Get("draft") is VaultItem && mainList.SelectedIndex == 0, "Enter in the search did not open the first found item");
            mainList.SelectedItem = mainList.Items.Cast<VaultItem>().Single(item => item.Id == textItem.Id);
            Settle();
            Assert(DraftId() == textItem.Id && matchCount.Text == "1 / 5", "The open file did not count the matches in its text");
            var markup = Get("markupView")!;
            int MarkedAreas() => (int)markup.GetType().GetProperty("HitAreas", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(markup)!;
            Rect? CurrentMark() => (Rect?)markup.GetType().GetProperty("CurrentHit", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(markup);
            Capture("search-all-folders"); Settle();
            Assert(MarkedAreas() == 5 && CurrentMark() is { Width: > 0 }, "The matches were not marked in the text: " + MarkedAreas());
            var firstMark = CurrentMark()!.Value;
            Click(nextMatch);
            Settle();
            Assert(matchCount.Text == "2 / 5" && DraftId() == textItem.Id && CurrentMark() is { } secondMark && secondMark != firstMark,
                "The arrow button did not go to the next match");
            Capture("search-all-folders");
            for (int step = 0; step < 3; step++) Call("NextMatch");
            Assert(matchCount.Text == "5 / 5" && DraftId() == textItem.Id, "The last match of the text was skipped");
            Call("NextMatch");
            Assert(DraftId() != textItem.Id && mainList.SelectedItem is VaultItem { Kind: ItemKind.Login },
                "After the last match the next found item was not opened");
            var rowConverter = (System.Windows.Data.IValueConverter)window.Resources["ItemTitleConverter"];
            object foundFile = mainList.Items.Cast<VaultItem>().Single(item => item.Id == textItem.Id);
            Assert(((string)rowConverter.Convert(foundFile, typeof(string), "sub", System.Globalization.CultureInfo.InvariantCulture)).EndsWith(" › Synthetic child") &&
                (Visibility)rowConverter.Convert(foundFile, typeof(Visibility), "pathIcon", System.Globalization.CultureInfo.InvariantCulture) == Visibility.Visible,
                "Found rows do not show their folder");
            Assert((string)rowConverter.Convert(foundFile, typeof(string), "query", System.Globalization.CultureInfo.InvariantCulture) == "E",
                "Found rows do not get the query to mark in their titles");
            mainList.SelectedItem = mainList.Items.Cast<VaultItem>().Single(item => item.Id == textItem.Id);
            var crumbs = Control<WrapPanel>("FolderCrumbs").Children.OfType<Button>().ToArray();
            Assert(crumbs.Select(crumb => (string)crumb.Tag).SequenceEqual([parentId, childId]) &&
                ((string)crumbs[1].ToolTip).EndsWith("Synthetic child") && Control<TextBlock>("KindCaption").Text == "Файлы",
                "The open item does not show its folder path");
            Click(crumbs[0]);
            Assert(mainSearch.Text.Length == 0 && (string)Get("folderId")! == parentId && DraftId() == textItem.Id &&
                nextMatch.Visibility == Visibility.Collapsed && matchCount.Text.Length == 0 && Control<TreeView>("FolderList").Opacity == 1,
                "A folder of the path did not clear the search and open that folder");
            Settle();
            Assert(MarkedAreas() == 0, "Marks stayed in the text after the search was cleared");
            mainSearch.Text = "E";
            Call("SelectFolder", childId);
            Assert(mainSearch.Text.Length == 0 && (string)Get("folderId")! == childId && mainList.Items.Count == 1,
                "Choosing a folder did not leave the search");
            // A match far down a long text is brought to the middle of the visible part of the editor.
            mainList.SelectedIndex = 0;
            var longBox = Control<TextBox>("TextField");
            var longScroll = Control<ScrollViewer>("EditorScroll");
            longBox.Text = string.Join('\n', Enumerable.Range(1, 200).Select(number => number == 150 ? "строка с меткой zzz" : "вымышленная строка " + number));
            Capture("search-long-text");
            mainSearch.Text = "zzz";
            Settle(); Capture("search-long-text"); Settle();
            double markY = longBox.TranslatePoint(new Point(0, CurrentMark()!.Value.Top + CurrentMark()!.Value.Height / 2), longScroll).Y;
            Assert(matchCount.Text == "1 / 1" && longScroll.VerticalOffset > 0 && Math.Abs(markY - longScroll.ViewportHeight / 2) < 4,
                $"The match was not brought to the middle of the editor: {markY} of {longScroll.ViewportHeight}");
            mainSearch.Clear();
            Call("RevertClick", null, new RoutedEventArgs());
            mainSearch.Text = "nothing like this";
            Key(mainSearch, "SearchKeyDown", System.Windows.Input.Key.Enter);
            Key(mainSearch, "SearchKeyDown", System.Windows.Input.Key.Escape);
            Assert(mainSearch.Text.Length == 0, "Esc did not clear the search");
            Assert(!Control<Button>("SaveButton").IsEnabled, "Quick search changed the vault");
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
                Assert(Control<Button>("AllCategory").Content?.ToString() == "Все 2", "The All category count was not shown");
                Call("SetCategory", ItemKind.Login);
                Assert(list.Items.Cast<VaultItem>().Single().Kind == ItemKind.Login, "The entry filter did not hide files");
                Call("SetCategory", ItemKind.Text);
                Assert(list.Items.Cast<VaultItem>().Single().Kind == ItemKind.Text, "The file filter did not hide entries");
                Call("SetCategory", new object?[] { null });
                Assert(list.Items.Count == 2, "The All category did not restore both item types");
                Await((Task)Call("LockVault")!);
            }
            Assert(Control<PasswordBox>("PasswordField").Password.Length == 0, "Password field not cleared on lock");
            Assert(Control<Button>("QuickSearchButton").Visibility == Visibility.Collapsed &&
                Control<MenuItem>("VersionsMenuItem").Visibility == Visibility.Collapsed, "Vault actions stayed on the start screen");

            // Start screen with recent files: the last one in the card, the others below.
            var recentPaths = CurrentLayout().RecentVaults.Select(item => item.Path).ToArray();
            Assert(recentPaths.Length == 3 && recentPaths[0] == Path.GetFullPath(vaultPath) && recentPaths[1] == Path.GetFullPath(textOnlyPath),
                "The recent list is not in the order of opening");
            Assert(Control<StackPanel>("WelcomeRecent").Visibility == Visibility.Visible &&
                Control<StackPanel>("WelcomeIntro").Visibility == Visibility.Collapsed &&
                Control<TextBlock>("RecentName").Text == "synthetic-ui.kdbx" && Control<Border>("RecentLatestBadge").Visibility == Visibility.Visible &&
                Control<DockPanel>("RecentPasswordRow").Visibility == Visibility.Visible &&
                (string)Control<Button>("WelcomeOpenButton").Content == "Открыть другой файл…" &&
                (string)Control<Button>("WelcomeCreateButton").Content == "Создать новое хранилище",
                "The start screen does not show the last file with a password field");
            var others = Control<ItemsControl>("OtherRecentList").Items.Cast<object>().ToArray();
            string Prop(object row, string name) => (string)row.GetType().GetProperty(name)!.GetValue(row)!;
            Assert(others.Length == 2 && Prop(others[0], "Name") == Path.GetFileName(textOnlyPath) && Prop(others[0], "Warning") == "" &&
                Prop(others[1], "Name") == "synthetic-text-only.kdbx" && Prop(others[1], "Warning") == "есть версия новее",
                "Other recent files or the newer-version mark are wrong");
            Capture("welcome-recent");
            var cardPassword = Control<PasswordBox>("RecentPassword");
            Assert(!(bool)Await2((Task<bool>)Call("OpenRecent")!) && status.Text == "Введите главный пароль" && Get("session") is null,
                "An empty master password was accepted");
            Call("OtherRecentClick", new Button { DataContext = others[0] }, new RoutedEventArgs());
            Assert(Control<TextBlock>("RecentName").Text == Path.GetFileName(textOnlyPath) &&
                Control<Border>("RecentLatestBadge").Visibility == Visibility.Collapsed, "A click on another recent file did not make it the card");
            cardPassword.Password = password;
            Assert((bool)Await2((Task<bool>)Call("OpenRecent")!) && Get("session") is VaultSession fromCard &&
                fromCard.FilePath == Path.GetFullPath(textOnlyPath) && cardPassword.Password.Length == 0 &&
                Control<Grid>("Workspace").Visibility == Visibility.Visible, "The card did not open its file or kept the password");
            Assert(CurrentLayout().RecentVaults[0].Path == Path.GetFullPath(textOnlyPath), "Opening from the card did not move the file to the top");
            Await((Task)Call("LockVault")!);

            // A file that is gone: grey, "file not found", no password field, can be removed from the list.
            string missing = Path.Combine(root, "gone_2026-01-01_v3.kdbx");
            Set("layout", CurrentLayout() with { RecentVaults = CurrentLayout().RecentVaults.Touch(missing, DateTime.UtcNow) });
            Call("UpdateWelcome");
            Assert(Control<TextBlock>("RecentInfo").Text.Contains("файл не найден") &&
                Control<DockPanel>("RecentPasswordRow").Visibility == Visibility.Collapsed &&
                Control<Button>("RecentRemoveButton").Visibility == Visibility.Visible, "A missing file is not shown as missing");
            Capture("welcome-missing");
            Click(Control<Button>("RecentRemoveButton"));
            Assert(!CurrentLayout().RecentVaults.Contains(missing) && Control<TextBlock>("RecentName").Text == Path.GetFileName(textOnlyPath),
                "Removing a missing file from the list failed");
            string settings = File.ReadAllText(Path.Combine(root, WindowLayout.FileName));
            Assert(settings.Contains("synthetic-ui.kdbx", StringComparison.Ordinal) && !settings.Contains(missing.Replace("\\", "\\\\"), StringComparison.Ordinal) &&
                !settings.Contains(password, StringComparison.Ordinal) && !settings.Contains("Fictional-UI-secret", StringComparison.Ordinal) &&
                !settings.Contains("Почта Example", StringComparison.Ordinal) && !settings.Contains("demo@example.com", StringComparison.Ordinal),
                "The settings file holds more than paths and times");
            Assert(Control<ListBox>("ItemList").Items.Count == 0, "Items not cleared on lock");

            // Attachments: list and drop zone on the frame, preview decoded from memory, add/remove edit the draft.
            string attachDir = Path.Combine(root, "attachment-sources");
            Directory.CreateDirectory(attachDir);
            string pngPath = Path.Combine(attachDir, "Фото счётчика (пример).png");
            var greenPixels = Enumerable.Range(0, 4 * 3).SelectMany(_ => new byte[] { 0x59, 0x70, 0x2F, 0xFF }).ToArray();
            var pngEncoder = new PngBitmapEncoder();
            pngEncoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(4, 3, 96, 96, PixelFormats.Bgra32, null, greenPixels, 16)));
            using (var pngFile = File.Create(pngPath)) pngEncoder.Save(pngFile);
            string pdfPath = Path.Combine(attachDir, "Договор (пример).pdf");
            File.WriteAllBytes(pdfPath, System.Security.Cryptography.RandomNumberGenerator.GetBytes(2048));
            string attachVault = Path.Combine(root, "synthetic-attachments.kdbx");
            using (var attachSession = VaultSession.Create(attachVault, password))
            {
                Set("session", attachSession); Call("EnterWorkspace");
                Call("Add", ItemKind.Login);
                Control<TextBox>("TitleField").Text = "Договор аренды (пример)";
                var attachmentList = Control<ItemsControl>("AttachmentList");
                Assert(Control<TextBlock>("AttachmentsTitle").Text == "Вложения · 0" &&
                    Control<Border>("AttachmentListBorder").Visibility == Visibility.Collapsed &&
                    Control<Grid>("AttachmentDropZone").Visibility == Visibility.Visible, "An item without attachments shows a list");
                Assert((bool)Await2((Task<bool>)Call("AddAttachmentFiles", (IReadOnlyList<string>)[pngPath, pdfPath, pdfPath])!),
                    "Files were not added");
                var attachmentRows = attachmentList.Items.Cast<object>().ToArray();
                Assert(attachmentRows.Select(row => Prop(row, "Name")).SequenceEqual(["Договор (пример) (2).pdf", "Договор (пример).pdf", "Фото счётчика (пример).png"]),
                    "Attachment names or the ' (2)' suffix are wrong: " + string.Join(", ", attachmentRows.Select(row => Prop(row, "Name"))));
                Assert(attachmentRows.Select(row => Prop(row, "Badge")).SequenceEqual(["PDF", "PDF", "PNG"]) &&
                    Prop(attachmentRows[2], "Info").StartsWith("Картинка · 1 КБ · добавлено ") && Prop(attachmentRows[0], "Info").StartsWith("Документ · 2 КБ"),
                    "Badges or the type, size and date line are wrong: " + Prop(attachmentRows[2], "Info"));
                Assert(attachmentRows.Select(row => (Visibility)row.GetType().GetProperty("PreviewVisibility")!.GetValue(row)!)
                    .SequenceEqual([Visibility.Collapsed, Visibility.Collapsed, Visibility.Visible]), "Preview is offered for a non-image");
                Assert(Control<TextBlock>("AttachmentsTitle").Text == "Вложения · 3" &&
                    Control<TextBlock>("AttachmentsTotal").Text.StartsWith("всего 5 КБ · хранятся внутри") &&
                    Control<Button>("SaveButton").IsEnabled && Control<TextBlock>("VaultSpaceText").Text.StartsWith("≈ ") &&
                    Control<TextBlock>("VaultSpaceText").Text.EndsWith(" из 512 МБ"), "The attachment header, total or space bar is wrong: " +
                    Control<TextBlock>("AttachmentsTotal").Text + " | " + Control<TextBlock>("VaultSpaceText").Text);
                var listed = Control<ListBox>("ItemList").Items.Cast<VaultItem>().Single();
                Assert(listed.AttachmentCount == 3, "The list does not count the unsaved attachments");

                // The frame: the editor scrolled to the attachments, list attachmentRows and the dashed drop zone drawn.
                Capture("attachments-top");
                Control<ScrollViewer>("EditorScroll").ScrollToEnd();
                var attachmentsFrame = Capture("attachments");
                var dropOutline = Control<System.Windows.Shapes.Rectangle>("DropOutline");
                var dropColor = PixelAt(attachmentsFrame, dropOutline.TranslatePoint(new Point(8, dropOutline.ActualHeight / 2), view));
                Assert(dropColor == Color.FromRgb(0xF1, 0xF7, 0xF3), "The drop zone is not on the frame: " + dropColor);
                var badge = Descendants<Border>(attachmentList).First(border => border.Width == 40);
                var badgeColor = PixelAt(attachmentsFrame, badge.TranslatePoint(new Point(4, 4), view));
                Assert(badgeColor == Color.FromRgb(0xCF, 0xE3, 0xD6), "The attachment list is not on the frame: " + badgeColor);
                var clip = Descendants<StackPanel>(Control<ListBox>("ItemList")).First(panel => panel.Name == "ClipBadge");
                Assert(clip.Visibility == Visibility.Visible && clip.ActualWidth > 0 && Descendants<TextBlock>(clip).Single().Text == "3", "The list row has no paper clip with the count");

                // Preview: decoded from the protected bytes in memory, never from a file.
                Window? previewed = null;
                Set("showAttachmentWindow", (Action<Window>)(shown => previewed = shown));
                Call("PreviewAttachmentClick", new Button { DataContext = attachmentRows[2] }, new RoutedEventArgs());
                var image = (BitmapSource)previewed!.GetType().GetProperty("Image")!.GetValue(previewed)!;
                Assert(image.PixelWidth == 4 && image.PixelHeight == 3 && previewed.Title == "Фото счётчика (пример).png",
                    "The image preview did not decode the picture");
                var decoded = new byte[4];
                new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0).CopyPixels(new Int32Rect(1, 1, 1, 1), decoded, 4, 0);
                Assert(decoded.SequenceEqual(new byte[] { 0x59, 0x70, 0x2F, 0xFF }), "The previewed pixels differ from the attachment");
                previewed.Close();

                // Saving with the busy indicator, then reopening: the same bytes.
                var saving = (Task)Call("SaveChanges")!;
                Assert(Control<Grid>("BusyOverlay").Visibility == Visibility.Visible && Control<TextBlock>("BusyText").Text == "Сохраняю и шифрую…",
                    "No busy indicator while saving");
                Capture("busy");
                Await(saving);
                Assert(Control<Grid>("BusyOverlay").Visibility == Visibility.Collapsed && !Control<Button>("SaveButton").IsEnabled,
                    "The busy indicator stayed or the save did not finish");
                Assert(Control<TextBlock>("SaveState").Text.StartsWith("Последнее сохранение: ") &&
                    Control<TextBlock>("SaveState").Text.EndsWith(" КБ"), "The header does not show the vault size: " + Control<TextBlock>("SaveState").Text);
                string savedId = listed.Id;
                using (var check = VaultSession.Open(attachVault, password))
                {
                    var stored = check.GetItem(savedId).Attachments.Single(a => a.Name == "Фото счётчика (пример).png");
                    Assert(stored.Content.ReadBytes().SequenceEqual(File.ReadAllBytes(pngPath)), "The saved attachment differs from the file");
                }

                // Remove asks first; it makes the draft edited, and Revert brings the attachment back.
                string? asked = null;
                bool answer = false;
                Set("confirm", (Func<string, string, string, bool>)((title, message, accept) => { asked = message + " / " + accept; return answer; }));
                attachmentRows = attachmentList.Items.Cast<object>().ToArray();
                Call("RemoveAttachmentClick", new Button { DataContext = attachmentRows[0] }, new RoutedEventArgs());
                Assert(asked == "Удалить вложение «Договор (пример) (2).pdf»? / Удалить" && attachmentList.Items.Count == 3 &&
                    !Control<Button>("SaveButton").IsEnabled, "Removing did not ask or removed without consent");
                answer = true;
                Call("RemoveAttachmentClick", new Button { DataContext = attachmentRows[0] }, new RoutedEventArgs());
                Assert(attachmentList.Items.Count == 2 && Control<Button>("SaveButton").IsEnabled &&
                    Control<Button>("RevertButton").Visibility == Visibility.Visible &&
                    Control<TextBlock>("SaveState").Text.StartsWith("Не сохранено правок: 1"), "Removing did not mark the vault unsaved");
                Call("RevertClick", null, new RoutedEventArgs());
                Assert(attachmentList.Items.Count == 3 && !Control<Button>("SaveButton").IsEnabled, "Revert did not bring the attachment back");

                // Above 100 MB of vault the user is warned first ("Add" / "Cancel"). A sparse file: nothing is read when cancelled.
                string bigPath = Path.Combine(attachDir, "большой архив (пример).zip");
                using (var big = new FileStream(bigPath, FileMode.CreateNew)) big.SetLength(150L * 1024 * 1024);
                answer = false; asked = null;
                Assert(!(bool)Await2((Task<bool>)Call("AddAttachmentFiles", (IReadOnlyList<string>)[bigPath])!) &&
                    asked?.StartsWith("Хранилище станет примерно 150 МБ. Открытие и каждое сохранение") == true && asked.EndsWith(" / Добавить") &&
                    attachmentList.Items.Count == 3 && !Control<Button>("SaveButton").IsEnabled, "No warning above 100 MB: " + asked);
                File.Delete(bigPath);
                Await((Task)Call("LockVault")!);
            }

            // JSON and plain text: attachment previews from memory, and "Text / JSON" of a ".json" text file.
            object Member(object target, string name) =>
                target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(target)!;
            void PumpUntil(Func<bool> done, string message)
            {
                for (int i = 0; i < 500 && !done(); i++)
                {
                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
                    Thread.Sleep(10);
                }
                Assert(done(), message);
            }
            string goodJson = Path.Combine(attachDir, "настройки (пример).json");
            File.WriteAllText(goodJson, "{\"сервер\":\"example.com\",\"порты\":[80,443],\"tls\":null}");
            string badJson = Path.Combine(attachDir, "битый (пример).json");
            File.WriteAllText(badJson, "{\n\"a\": 1,\n\"b\": oops\n}");
            string jsonVault = Path.Combine(root, "synthetic-json.kdbx");
            using (var jsonSession = VaultSession.Create(jsonVault, password))
            {
                Set("session", jsonSession); Call("EnterWorkspace");
                Call("Add", ItemKind.Text);
                Assert(Control<Border>("TextViewSwitch").Visibility == Visibility.Collapsed, "A text file without .json offers the JSON view");
                Control<TextBox>("TitleField").Text = "data.json";
                const string storedJson = "{\"x\":{\"y\":[1,2]},\"z\":\"# не заголовок\"}";
                Control<TextBox>("TextField").Text = storedJson;
                Assert(Control<Border>("TextViewSwitch").Visibility == Visibility.Visible, "A .json text file has no Text / JSON switch");
                Assert((bool)Await2((Task<bool>)Call("AddAttachmentFiles", (IReadOnlyList<string>)[goodJson, badJson])!), "JSON files were not added");
                var jsonRows = Control<ItemsControl>("AttachmentList").Items.Cast<object>().ToArray();
                Assert(jsonRows.All(row => (Visibility)Member(row, "PreviewVisibility") == Visibility.Visible), "JSON attachments have no View button");
                Window? shownText = null;
                Set("showAttachmentWindow", (Action<Window>)(w => shownText = w));
                Call("PreviewAttachmentClick", new Button { DataContext = jsonRows.Single(row => Prop(row, "Name") == "настройки (пример).json") }, new RoutedEventArgs());
                PumpUntil(() => shownText is not null, "The JSON preview did not open");
                var textView = Member(shownText!, "View");
                var body = (TextBox)Member(textView, "Body");
                Assert(body.IsReadOnly && body.FontFamily.Source.StartsWith("Consolas") &&
                    body.Text.Replace("\r\n", "\n") == "{\n  \"сервер\": \"example.com\",\n  \"порты\": [\n    80,\n    443\n  ],\n  \"tls\": null\n}" &&
                    ((TextBlock)Member(textView, "Warning")).Visibility == Visibility.Collapsed, "The JSON attachment is not shown formatted: " + body.Text);
                ((TextBox)Member(textView, "Query")).Text = "TLS";
                Assert((bool)textView.GetType().GetMethod("FindNext", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(textView, null)! &&
                    body.SelectedText == "tls", "Search inside the preview did not find the text");
                copied = null;
                Click(LogicalDescendants<Button>(shownText!).Single(button => (string)button.Content == "Копировать всё"));
                Assert(copied == body.Text && status.Text == "Текст скопирован", "Copy all did not copy the shown text");
                shownText!.Close(); shownText = null;
                Call("PreviewAttachmentClick", new Button { DataContext = jsonRows.Single(row => Prop(row, "Name") == "битый (пример).json") }, new RoutedEventArgs());
                PumpUntil(() => shownText is not null, "The broken JSON preview did not open");
                textView = Member(shownText!, "View");
                var badWarning = (TextBlock)Member(textView, "Warning");
                Assert(((TextBox)Member(textView, "Body")).Text == File.ReadAllText(badJson) && badWarning.Visibility == Visibility.Visible &&
                    badWarning.Text == "Файл не является корректным JSON, показан исходный текст (ошибка в строке 3).",
                    "Broken JSON is not shown as is with a warning: " + badWarning.Text);
                shownText!.Close();
                var draftNow = (VaultItem)Get("draft")!;
                Assert(draftNow.Attachments.Single(a => a.Name == "настройки (пример).json").Content.ReadBytes().SequenceEqual(File.ReadAllBytes(goodJson)),
                    "Viewing changed the attachment bytes");
                Await((Task)Call("SaveChanges")!);

                // The JSON view of the file's own text: formatted, read-only, the text and the saved state unchanged.
                Call("JsonViewClick", null, new RoutedEventArgs());
                var jsonView = Control<FrameworkElement>("JsonView");
                var jsonBody = (TextBox)Member(jsonView, "Body");
                Assert(jsonView.Visibility == Visibility.Visible && Control<Grid>("TextHost").Visibility == Visibility.Collapsed &&
                    Control<StackPanel>("TextTools").Visibility == Visibility.Collapsed && jsonBody.IsReadOnly &&
                    jsonBody.Text.Replace("\r\n", "\n") == "{\n  \"x\": {\n    \"y\": [\n      1,\n      2\n    ]\n  },\n  \"z\": \"# не заголовок\"\n}",
                    "The JSON view is not shown formatted: " + jsonBody.Text);
                Assert(Control<TextBox>("TextField").Text == storedJson && ((VaultItem)Get("draft")!).Text == storedJson &&
                    !Control<Button>("SaveButton").IsEnabled && Control<Button>("RevertButton").Visibility == Visibility.Collapsed &&
                    Control<TextBlock>("SaveState").Text.StartsWith("Последнее сохранение: "), "The JSON view changed the text or made the vault unsaved");
                Control<ScrollViewer>("EditorScroll").ScrollToHome();
                Capture("json-view");
                Call("PlainViewClick", null, new RoutedEventArgs());
                Assert(Control<Grid>("TextHost").Visibility == Visibility.Visible && jsonView.Visibility == Visibility.Collapsed, "Back to Text failed");
                Call("JsonViewClick", null, new RoutedEventArgs());
                Call("ShowDraft");
                Assert(Control<Grid>("TextHost").Visibility == Visibility.Visible, "A file did not open in the Text view");
                using (var check = VaultSession.Open(jsonVault, password))
                    Assert(check.Items.Single().Text == storedJson, "The JSON view reformatted the stored text");
                Await((Task)Call("LockVault")!);
            }

            using var closingSession = VaultSession.Open(vaultPath, password);
            Set("session", closingSession); Call("EnterWorkspace");
            bool closed = false;
            window.Closed += (_, _) => closed = true;
            window.Close();
            Assert(closed && Control<Grid>("Welcome").Visibility == Visibility.Collapsed,
                "Closing an unchanged vault switched to the welcome screen or delayed closing");
            Console.WriteLine("PASS WPF: attachments (list, drop zone, preview from memory, remove, warning), busy indicator, password generator, heading and separator lines, manual save, saved status, revert, blank draft, both categories, reopen, lock cleanup, header menu, compact view, one-click copy, folder menu, sorting, quick search, recent files, versions");
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

/// <summary>Keyboard events need a presentation source; this one has no window and nothing on screen.</summary>
internal sealed class OffscreenSource : PresentationSource
{
    private Visual? root;
    public override Visual? RootVisual { get => root; set => root = value; }
    public override bool IsDisposed => false;
    protected override CompositionTarget? GetCompositionTargetCore() => null;
}
