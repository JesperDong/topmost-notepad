using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Folding;

namespace TopNote
{
    public sealed class MainWindow : Window
    {
        private readonly INoteDialogs dialogs;
        private NoteDocument document;
        private readonly TextEditor editor;
        private FoldingManager foldingManager;
        private List<RegionBlock> regions = new List<RegionBlock>();
        private readonly ToggleButton pinButton;
        private readonly TextBlock pinLabel, statusText, countText, placeholder;
        private bool loading;
        private string savedStatus;
        public NoteDocument Document { get { return document; } }
        public TextEditor Editor { get { return editor; } }
        public FoldingManager Foldings { get { return foldingManager; } }

        public MainWindow(INoteDialogs noteDialogs)
        {
            dialogs = noteDialogs;
            document = new NoteDocument();
            Title = "无标题 — 置顶记事本";
            Width = 600;
            Height = 660;
            MinWidth = 420;
            MinHeight = 280;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = new FontFamily("Microsoft YaHei UI");
            FontSize = 13;
            Background = Brushes.White;
            Topmost = true;
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);

            Assembly assembly = Assembly.GetExecutingAssembly();
            using (Stream resource = assembly.GetManifestResourceStream("TopNote.MainWindow.xaml"))
                Content = XamlReader.Load(resource);
            using (Stream resource = assembly.GetManifestResourceStream("TopNote.app.ico"))
            {
                if (resource != null)
                {
                    BitmapFrame icon = BitmapFrame.Create(resource, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    Icon = icon;
                }
            }
            FrameworkElement root = (FrameworkElement)Content;
            editor = (TextEditor)root.FindName("Editor");
            editor.Options.EnableHyperlinks = false;
            editor.Options.EnableEmailHyperlinks = false;
            editor.Options.ConvertTabsToSpaces = false;
            editor.Options.EnableImeSupport = true;
            editor.Options.AllowToggleOverstrikeMode = false;
            editor.Options.EnableRectangularSelection = false;
            editor.Options.EnableTextDragDrop = false;
            editor.Options.CutCopyWholeLine = false;
            editor.Document.UndoStack.SizeLimit = 200;
            foldingManager = FoldingManager.Install(editor.TextArea);
            FoldingElementGenerator.TextBrush = new SolidColorBrush(Color.FromRgb(80, 112, 154));
            pinButton = (ToggleButton)root.FindName("PinButton");
            pinLabel = (TextBlock)root.FindName("PinLabel");
            statusText = (TextBlock)root.FindName("StatusText");
            countText = (TextBlock)root.FindName("CountText");
            placeholder = (TextBlock)root.FindName("Placeholder");
            ((Button)root.FindName("NewButton")).Click += delegate { NewDocument(); };
            ((Button)root.FindName("OpenButton")).Click += delegate { OpenDocument(); };
            ((Button)root.FindName("SaveButton")).Click += delegate { SaveDocument(false); };
            Button more = (Button)root.FindName("SaveMoreButton");
            more.Click += delegate
            {
                more.ContextMenu.PlacementTarget = more;
                more.ContextMenu.Placement = PlacementMode.Bottom;
                more.ContextMenu.IsOpen = true;
            };
            ((MenuItem)more.ContextMenu.Items[0]).Click += delegate { SaveDocument(true); };
            ((Button)root.FindName("KnowledgeButton")).Click += delegate { CreateKnowledgeBlock(); };
            Button knowledgeMore = (Button)root.FindName("KnowledgeMoreButton");
            knowledgeMore.Click += delegate
            {
                knowledgeMore.ContextMenu.PlacementTarget = knowledgeMore;
                knowledgeMore.ContextMenu.Placement = PlacementMode.Bottom;
                knowledgeMore.ContextMenu.IsOpen = true;
            };
            ((MenuItem)knowledgeMore.ContextMenu.Items[0]).Click += delegate { ToggleCurrentBlock(); };
            ((MenuItem)knowledgeMore.ContextMenu.Items[1]).Click += delegate { SetAllBlocksCollapsed(true); };
            ((MenuItem)knowledgeMore.ContextMenu.Items[2]).Click += delegate { SetAllBlocksCollapsed(false); };
            ((MenuItem)knowledgeMore.ContextMenu.Items[4]).Click += delegate { RemoveCurrentBlock(); };
            InstallEditorMenu();
            pinButton.Checked += delegate { UpdatePin(); };
            pinButton.Unchecked += delegate { UpdatePin(); };
            editor.TextChanged += delegate
            {
                if (loading) return;
                document.Text = editor.Text;
                savedStatus = null;
                UpdateFoldings(false);
                RefreshStatus();
            };
            Closing += OnClosing;
            ContentRendered += delegate { editor.Focus(); };
            PreviewKeyDown += OnKeyDown;
            editor.PreviewTextInput += delegate(object sender, TextCompositionEventArgs e)
            {
                if (ExpandHiddenSelection()) e.Handled = true;
            };
            editor.AddHandler(CommandManager.PreviewExecutedEvent, new ExecutedRoutedEventHandler(delegate(object sender, ExecutedRoutedEventArgs e)
            {
                if (PrepareEditingCommand(e.Command)) e.Handled = true;
            }));
            editor.PreviewMouseWheel += delegate(object sender, MouseWheelEventArgs e)
            {
                if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
                AdjustFont(e.Delta > 0 ? 1 : -1);
                e.Handled = true;
            };
            RefreshStatus();
        }

        public void TogglePin()
        {
            pinButton.IsChecked = pinButton.IsChecked != true;
        }

        private void UpdatePin()
        {
            Topmost = pinButton.IsChecked == true;
            pinLabel.Text = Topmost ? "已置顶" : "置顶";
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if ((e.Key == Key.Back || e.Key == Key.Delete || e.Key == Key.Enter) && PrepareDestructiveEdit(e.Key == Key.Back))
            {
                e.Handled = true;
                return;
            }
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0 || (Keyboard.Modifiers & ModifierKeys.Alt) != 0) return;
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            if (e.Key == Key.N && !shift) { NewDocument(); e.Handled = true; }
            else if (e.Key == Key.O && !shift) { OpenDocument(); e.Handled = true; }
            else if (e.Key == Key.S) { SaveDocument(shift); e.Handled = true; }
            else if (e.Key == Key.T && !shift) { TogglePin(); e.Handled = true; }
            else if (e.Key == Key.K && shift) { CreateKnowledgeBlock(); e.Handled = true; }
            else if (e.Key == Key.U && shift) { RemoveCurrentBlock(); e.Handled = true; }
            else if (e.Key == Key.Space && shift) { ToggleCurrentBlock(); e.Handled = true; }
            else if (e.Key == Key.D1 && shift) { SetAllBlocksCollapsed(true); e.Handled = true; }
            else if (e.Key == Key.D2 && shift) { SetAllBlocksCollapsed(false); e.Handled = true; }
            else if (e.Key == Key.OemPlus || e.Key == Key.Add) { AdjustFont(1); e.Handled = true; }
            else if (e.Key == Key.OemMinus || e.Key == Key.Subtract) { AdjustFont(-1); e.Handled = true; }
            else if ((e.Key == Key.D0 || e.Key == Key.NumPad0) && !shift) { editor.FontSize = 16; placeholder.FontSize = 16; e.Handled = true; }
        }

        private void AdjustFont(int delta)
        {
            editor.FontSize = Math.Max(10, Math.Min(32, editor.FontSize + delta));
            placeholder.FontSize = editor.FontSize;
        }

        private void InstallEditorMenu()
        {
            ContextMenu menu = new ContextMenu();
            MenuItem item = new MenuItem { Header = "设为 / 添加补充知识", InputGestureText = "Ctrl+Shift+K" };
            item.Click += delegate { CreateKnowledgeBlock(); };
            menu.Items.Add(item);
            item = new MenuItem { Header = "展开 / 收起当前块", InputGestureText = "Ctrl+Shift+Space" };
            item.Click += delegate { ToggleCurrentBlock(); };
            menu.Items.Add(item);
            item = new MenuItem { Header = "取消当前折叠（保留文字）", InputGestureText = "Ctrl+Shift+U" };
            item.Click += delegate { RemoveCurrentBlock(); };
            menu.Items.Add(item);
            menu.Items.Add(new Separator());
            menu.Items.Add(new MenuItem { Header = "撤销", Command = ApplicationCommands.Undo, CommandTarget = editor });
            menu.Items.Add(new MenuItem { Header = "重做", Command = ApplicationCommands.Redo, CommandTarget = editor });
            menu.Items.Add(new Separator());
            menu.Items.Add(new MenuItem { Header = "剪切", Command = ApplicationCommands.Cut, CommandTarget = editor });
            menu.Items.Add(new MenuItem { Header = "复制", Command = ApplicationCommands.Copy, CommandTarget = editor });
            menu.Items.Add(new MenuItem { Header = "粘贴", Command = ApplicationCommands.Paste, CommandTarget = editor });
            menu.Items.Add(new MenuItem { Header = "全选", Command = ApplicationCommands.SelectAll, CommandTarget = editor });
            editor.ContextMenu = menu;
        }

        private void UpdateFoldings(bool closeAll)
        {
            regions = KnowledgeRegions.Parse(editor.Text);
            List<NewFolding> definitions = new List<NewFolding>();
            foreach (RegionBlock block in regions)
                definitions.Add(new NewFolding(block.FoldStart, block.EndOffset) { Name = " … " + block.BodyLineCount + "行 ", DefaultClosed = closeAll });
            foldingManager.UpdateFoldings(definitions, -1);
            if (closeAll) SetAllBlocksCollapsed(true);
        }

        private RegionBlock CurrentRegion()
        {
            int caret = editor.CaretOffset;
            // In nested blocks, act on the innermost region at the caret.
            return regions.Where(delegate(RegionBlock b) { return caret >= b.HeaderStart && caret <= b.EndOffset; })
                .OrderBy(delegate(RegionBlock b) { return b.EndOffset - b.HeaderStart; }).FirstOrDefault();
        }

        public bool CreateKnowledgeBlock()
        {
            try
            {
                RegionEdit edit = KnowledgeRegions.Wrap(editor.Text, editor.SelectionStart, editor.SelectionLength);
                ReplaceTextAsOneEdit(edit.Text);
                editor.Select(edit.SelectionWasEmpty ? edit.ContentStart : edit.HeaderStart, 0);
                editor.CaretOffset = edit.SelectionWasEmpty ? edit.ContentStart : edit.HeaderStart;
                RegionBlock block = regions.FirstOrDefault(delegate(RegionBlock b) { return b.HeaderStart == edit.HeaderStart; });
                if (block == null)
                {
                    statusText.Text = "标记未能组成完整知识块，文字已保留，请检查起止标记";
                    return false;
                }
                FoldingSection section = foldingManager.GetFoldingsAt(block.FoldStart).FirstOrDefault();
                if (section == null) return false;
                section.IsFolded = !edit.SelectionWasEmpty;
                editor.Focus();
                editor.ScrollTo(editor.Document.GetLineByOffset(editor.CaretOffset).LineNumber, 1);
                return true;
            }
            catch (ArgumentException e)
            {
                savedStatus = e.Message.Split(new char[] { '\r', '\n' })[0];
                RefreshStatus();
                statusText.Text = savedStatus;
                return false;
            }
        }

        private void ReplaceTextAsOneEdit(string text)
        {
            // Replace only the changed span. Other folding sections retain their
            // tracked offsets and expanded state, and undo is a single operation.
            string old = editor.Text;
            int prefix = 0;
            while (prefix < old.Length && prefix < text.Length && old[prefix] == text[prefix]) prefix++;
            int suffix = 0;
            while (suffix < old.Length - prefix && suffix < text.Length - prefix && old[old.Length - 1 - suffix] == text[text.Length - 1 - suffix]) suffix++;
            using (editor.Document.RunUpdate())
                editor.Document.Replace(prefix, old.Length - prefix - suffix, text.Substring(prefix, text.Length - prefix - suffix));
        }

        public bool RemoveCurrentBlock()
        {
            RegionBlock block = CurrentRegion();
            if (block == null) return false;
            KnowledgeRegions.Remove(editor.Text, block); // Validate offsets before modifying the editor.
            using (editor.Document.RunUpdate())
            {
                editor.Document.Remove(block.FooterStart, block.FooterLength);
                editor.Document.Remove(block.HeaderStart, block.HeaderLength);
            }
            editor.Select(Math.Min(block.HeaderStart, editor.Document.TextLength), 0);
            editor.CaretOffset = editor.SelectionStart;
            editor.Focus();
            return true;
        }

        public bool ToggleCurrentBlock()
        {
            RegionBlock block = CurrentRegion();
            if (block == null) return false;
            FoldingSection section = foldingManager.GetFoldingsAt(block.FoldStart).First();
            if (!section.IsFolded) { editor.Select(block.HeaderStart, 0); editor.CaretOffset = block.HeaderStart; }
            section.IsFolded = !section.IsFolded;
            editor.Focus();
            return true;
        }

        public void SetAllBlocksCollapsed(bool collapsed)
        {
            if (collapsed && regions.Count > 0)
            {
                int caret = editor.CaretOffset;
                RegionBlock enclosing = regions.Where(delegate(RegionBlock b) { return caret >= b.HeaderStart && caret <= b.EndOffset; })
                    .OrderBy(delegate(RegionBlock b) { return b.HeaderStart; }).FirstOrDefault();
                if (enclosing != null) { editor.Select(enclosing.HeaderStart, 0); editor.CaretOffset = enclosing.HeaderStart; }
            }
            foreach (FoldingSection section in foldingManager.AllFoldings) section.IsFolded = collapsed;
        }

        public bool ExpandHiddenSelection()
        {
            if (editor.SelectionLength == 0) return false;
            int start = editor.SelectionStart, end = start + editor.SelectionLength;
            bool expanded = false;
            foreach (FoldingSection section in foldingManager.AllFoldings)
                if (section.IsFolded && start < section.EndOffset && end > section.StartOffset)
                { section.IsFolded = false; expanded = true; }
            if (expanded) ShowExpandedForEdit();
            return expanded;
        }

        public bool PrepareDestructiveEdit(bool backward)
        {
            if (ExpandHiddenSelection()) return true;
            if (editor.SelectionLength != 0) return false;
            int caret = editor.CaretOffset;
            bool expanded = false;
            foreach (FoldingSection section in foldingManager.AllFoldings)
                if (section.IsFolded && caret == (backward ? section.EndOffset : section.StartOffset))
                { section.IsFolded = false; expanded = true; }
            if (expanded) ShowExpandedForEdit();
            return expanded;
        }

        public bool PrepareEditingCommand(ICommand command)
        {
            bool backward = command == EditingCommands.Backspace || command == EditingCommands.DeletePreviousWord;
            bool deletion = backward || command == EditingCommands.Delete || command == EditingCommands.DeleteNextWord || command == AvalonEditCommands.DeleteLine;
            bool breaks = command == EditingCommands.EnterParagraphBreak || command == EditingCommands.EnterLineBreak;
            if (!deletion && !breaks && command != ApplicationCommands.Cut && command != ApplicationCommands.Paste) return false;
            if (ExpandHiddenSelection()) return true;
            if (deletion && editor.SelectionLength == 0)
            {
                RegionBlock block = CurrentRegion();
                if (block != null)
                {
                    FoldingSection section = foldingManager.GetFoldingsAt(block.FoldStart).FirstOrDefault();
                    if (section != null && section.IsFolded)
                    {
                        section.IsFolded = false;
                        ShowExpandedForEdit();
                        return true;
                    }
                }
            }
            return breaks && PrepareDestructiveEdit(false);
        }

        private void ShowExpandedForEdit()
        {
            statusText.Text = "已展开隐藏内容，请确认后继续编辑";
        }

        private void OnClosing(object sender, CancelEventArgs e)
        {
            if (!CanLeaveDocument()) e.Cancel = true;
        }

        public bool CanLeaveDocument()
        {
            if (!document.IsDirty) return true;
            UnsavedChoice choice = dialogs.AskUnsaved(this, document.DisplayName);
            if (choice == UnsavedChoice.Cancel) return false;
            if (choice == UnsavedChoice.Save) return SaveDocument(false);
            return true;
        }

        public bool NewDocument()
        {
            if (!CanLeaveDocument()) return false;
            SetDocument(new NoteDocument());
            return true;
        }

        public bool OpenDocument()
        {
            string path = dialogs.ChooseOpen(this);
            if (path == null) return false;
            return OpenPath(path, true);
        }

        public bool OpenPath(string path, bool askUnsaved)
        {
            if (askUnsaved && !CanLeaveDocument()) return false;
            try
            {
                NoteDocument next = NoteDocument.Open(path);
                SetDocument(next);
                return true;
            }
            catch (Exception e)
            {
                if (!IsFileError(e)) throw;
                dialogs.ShowError(this, "打开", e);
                return false;
            }
        }

        public bool SaveDocument(bool saveAs)
        {
            string path = document.FilePath;
            if (path == null || saveAs) path = dialogs.ChooseSave(this, path);
            if (path == null) return false;
            try
            {
                bool externallyChanged = document.HasExternalChanges(path);
                if (externallyChanged && !dialogs.AskExternalChange(this)) return false;
                // A clean ordinary save must preserve the original byte sequence,
                // including mixed line endings, instead of rewriting it unnecessarily.
                if (document.IsDirty || saveAs || document.FilePath == null || externallyChanged)
                {
                    try { document.Save(path, false); }
                    catch (EncoderFallbackException)
                    {
                        if (!dialogs.AskConvertUtf8(this)) return false;
                        document.Save(path, true);
                    }
                }
                savedStatus = "已保存 " + DateTime.Now.ToString("HH:mm");
                RefreshStatus();
                editor.Focus();
                return true;
            }
            catch (Exception e)
            {
                if (!IsFileError(e)) throw;
                dialogs.ShowError(this, "保存", e);
                return false;
            }
        }

        private static bool IsFileError(Exception e)
        {
            return e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException || e is System.Security.SecurityException;
        }

        private void SetDocument(NoteDocument next)
        {
            loading = true;
            document = next;
            editor.Text = document.Text;
            editor.Document.UndoStack.ClearAll();
            editor.CaretOffset = 0;
            UpdateFoldings(true);
            editor.ScrollToHome();
            loading = false;
            savedStatus = null;
            RefreshStatus();
            editor.Focus();
        }

        private void RefreshStatus()
        {
            Title = (document.IsDirty ? "* " : "") + document.DisplayName + " — 置顶记事本";
            string state = document.IsDirty ? "未保存修改" : (savedStatus ?? (document.FilePath == null ? "尚未保存" : "已打开"));
            statusText.Text = state + " · " + document.EncodingName;
            statusText.ToolTip = document.FilePath ?? "保存后会显示文件位置";
            // Count Unicode code points, excluding line separators.
            int count = 0;
            for (int i = 0; i < document.Text.Length; i++)
            {
                char c = document.Text[i];
                if (c == '\r' || c == '\n') continue;
                if (char.IsHighSurrogate(c) && i + 1 < document.Text.Length && char.IsLowSurrogate(document.Text[i + 1])) i++;
                count++;
            }
            countText.Text = count.ToString("N0") + " 字";
            placeholder.Visibility = document.Text.Length == 0 && document.FilePath == null ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
