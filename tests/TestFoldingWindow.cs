using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Folding;
using TopNote;

internal sealed class FoldingFakeDialogs : INoteDialogs
{
    public string OpenPath, SavePath;
    public UnsavedChoice Choice = UnsavedChoice.Cancel;
    public int Prompts, Errors;
    public string ChooseOpen(Window owner) { return OpenPath; }
    public string ChooseSave(Window owner, string currentPath) { return SavePath; }
    public UnsavedChoice AskUnsaved(Window owner, string name) { Prompts++; return Choice; }
    public bool AskConvertUtf8(Window owner) { return true; }
    public bool AskExternalChange(Window owner) { return true; }
    public void ShowError(Window owner, string action, Exception error) { Errors++; }
}

internal static class TestFoldingWindow
{
    private static int passed;
    private static string directory;
    private static MainWindow window;
    private static FoldingFakeDialogs dialogs;

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        passed++;
        Console.WriteLine("PASS: " + message);
    }

    private static void Fresh(string text)
    {
        dialogs.Choice = UnsavedChoice.Discard;
        Check(window.NewDocument(), "New document resets test fixture");
        window.Editor.Text = text;
        window.Editor.Document.UndoStack.ClearAll();
        window.Editor.Select(0, 0);
        window.Editor.CaretOffset = 0;
    }

    private static FoldingSection Section(int index)
    {
        return window.Foldings.AllFoldings.ElementAt(index);
    }

    private static void SelectText(string value)
    {
        int start = window.Editor.Text.IndexOf(value, StringComparison.Ordinal);
        if (start < 0) throw new Exception("Test fixture cannot find selection: " + value);
        window.Editor.Select(start, value.Length);
    }

    private static void Save(string name)
    {
        dialogs.SavePath = Path.Combine(directory, name);
        Check(window.SaveDocument(true), "Save test fixture: " + name);
    }

    [STAThread]
    private static int Main()
    {
        directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
            "folding-window-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        Application application = null;
        dialogs = new FoldingFakeDialogs();
        try
        {
            application = new Application();
            application.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            window = new MainWindow(dialogs);
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -12000;
            window.Top = -12000;
            window.ShowActivated = false;
            window.ShowInTaskbar = false;
            window.Show();
            window.UpdateLayout();
            Check(!window.Editor.Options.EnableTextDragDrop && !window.Editor.Options.CutCopyWholeLine,
                "Drag/drop and implicit whole-line Cut are disabled to protect folded content");

            string original = "论文核心结论\r\n相关知识第一行\r\n相关知识第二行 \U0001F4DA\r\n继续阅读正文\r\n";
            Fresh(original);
            SelectText("相关知识第一行\r\n相关知识第二行 \U0001F4DA\r\n");
            Check(window.CreateKnowledgeBlock(), "Selected paragraphs become a knowledge block");
            string wrapped = window.Editor.Text;
            RegionBlock block = KnowledgeRegions.Parse(wrapped).Single();
            Check(window.Foldings.AllFoldings.Count() == 1 && Section(0).IsFolded,
                "Selected block is initially collapsed");
            Check(wrapped.Substring(block.ContentStart, block.ContentLength) ==
                "相关知识第一行\r\n相关知识第二行 \U0001F4DA\r\n" &&
                wrapped.StartsWith("论文核心结论\r\n", StringComparison.Ordinal) &&
                wrapped.EndsWith("继续阅读正文\r\n", StringComparison.Ordinal),
                "Wrapping preserves Chinese, emoji, body and neighbouring paragraphs");
            window.Editor.Undo();
            Check(window.Editor.Text == original && !window.Foldings.AllFoldings.Any(),
                "Single Undo restores the original note without knowledge markers");
            window.Editor.Redo();
            Check(window.Editor.Text == wrapped && window.Foldings.AllFoldings.Count() == 1,
                "Single Redo restores the complete knowledge block");

            Save("折叠保存.txt");
            window.Editor.Document.UndoStack.ClearAll();
            window.Editor.CaretOffset = KnowledgeRegions.Parse(window.Editor.Text)[0].HeaderStart;
            Check(window.ToggleCurrentBlock(), "Current block toggles");
            window.SetAllBlocksCollapsed(false);
            window.SetAllBlocksCollapsed(true);
            Check(!window.Document.IsDirty && !window.Editor.Document.UndoStack.CanUndo &&
                window.Editor.Text == wrapped,
                "Collapse and expand change neither saved text nor dirty state nor Undo history");
            Check(File.ReadAllText(dialogs.SavePath) == wrapped,
                "Saving a collapsed block includes all hidden text and marker lines");
            dialogs.OpenPath = dialogs.SavePath;
            Check(window.OpenDocument() && window.Editor.Text == wrapped && Section(0).IsFolded &&
                !window.Document.IsDirty, "Reopening TXT restores all hidden text and collapses the block");
            window.SetAllBlocksCollapsed(false);
            Check(!Section(0).IsFolded && window.Editor.Text.Contains("相关知识第二行 \U0001F4DA"),
                "Reopened hidden body can be expanded intact");
            window.Editor.CaretOffset = KnowledgeRegions.Parse(window.Editor.Text)[0].HeaderStart;
            Check(window.RemoveCurrentBlock() && window.Editor.Text == original,
                "Remove folding markers retains the original note body");
            window.Editor.Undo();
            Check(window.Editor.Text == wrapped && window.Foldings.AllFoldings.Count() == 1,
                "Removing a block can be undone without losing body text");

            Fresh("正文第一段\r\n正文第二段");
            window.Editor.CaretOffset = "正文第一段\r\n".Length;
            Check(window.CreateKnowledgeBlock(), "Empty selection inserts an empty knowledge block");
            block = KnowledgeRegions.Parse(window.Editor.Text).Single();
            Check(!Section(0).IsFolded && window.Editor.CaretOffset == block.ContentStart,
                "Empty inserted block stays open with caret ready for typing");
            window.Editor.Document.Insert(window.Editor.CaretOffset, "补充概念：可直接输入 \U0001F4DD");
            Check(window.Editor.Text.Contains("补充概念：可直接输入 \U0001F4DD") &&
                KnowledgeRegions.Parse(window.Editor.Text).Count == 1 && !Section(0).IsFolded,
                "Typing into an empty block keeps the typed content visible and foldable");

            Fresh("前言\r\n第一块知识\r\n中间正文\r\n第二块知识\r\n结尾\r\n");
            SelectText("第一块知识");
            Check(window.CreateKnowledgeBlock(), "First of multiple blocks is created");
            window.SetAllBlocksCollapsed(false);
            SelectText("第二块知识");
            Check(window.CreateKnowledgeBlock() && window.Foldings.AllFoldings.Count() == 2,
                "Second independent block is created");
            Check(!Section(0).IsFolded && Section(1).IsFolded,
                "Creating a second block preserves the first block's expanded state");
            window.SetAllBlocksCollapsed(true);
            Check(window.Foldings.AllFoldings.All(delegate(FoldingSection section) { return section.IsFolded; }),
                "Collapse all affects both blocks");
            window.SetAllBlocksCollapsed(false);
            Check(window.Foldings.AllFoldings.All(delegate(FoldingSection section) { return !section.IsFolded; }),
                "Expand all affects both blocks");
            window.SetAllBlocksCollapsed(true);
            int oldStart = Section(1).StartOffset;
            window.Editor.Document.Insert(0, "新增论文信息\r\n");
            Check(window.Foldings.AllFoldings.Count() == 2 && Section(1).StartOffset > oldStart &&
                Section(0).IsFolded && Section(1).IsFolded &&
                Section(1).StartOffset == KnowledgeRegions.Parse(window.Editor.Text)[1].FoldStart,
                "Editing above blocks tracks offsets and preserves collapsed state");

            Fresh("【补充知识：外层】\r\n外层知识\r\n【补充知识：内层】\r\n内层知识\r\n【/补充知识】\r\n外层结论\r\n【/补充知识】\r\n正文");
            Check(window.Foldings.AllFoldings.Count() == 2, "Nested marker blocks create two folding sections");
            window.SetAllBlocksCollapsed(false);
            window.Editor.CaretOffset = window.Editor.Text.IndexOf("内层知识", StringComparison.Ordinal);
            Check(window.ToggleCurrentBlock() && !Section(0).IsFolded && Section(1).IsFolded,
                "Toggle at a nested body acts on the innermost block");
            window.SetAllBlocksCollapsed(false);
            window.Editor.CaretOffset = window.Editor.Text.IndexOf("内层知识", StringComparison.Ordinal);
            Check(window.RemoveCurrentBlock() && window.Foldings.AllFoldings.Count() == 1 &&
                window.Editor.Text.Contains("内层知识") && window.Editor.Text.Contains("外层知识"),
                "Removing inner folding preserves both inner and outer body text");

            string nested = "【补充知识：外层】\r\n外层知识\r\n【补充知识：内层】\r\n内层知识\r\n【/补充知识】\r\n外层结论\r\n【/补充知识】\r\n正文";
            Fresh(nested);
            window.SetAllBlocksCollapsed(false);
            window.Editor.CaretOffset = window.Editor.Text.IndexOf("内层知识", StringComparison.Ordinal);
            window.SetAllBlocksCollapsed(true);
            Check(window.Editor.CaretOffset == KnowledgeRegions.Parse(window.Editor.Text)[0].HeaderStart &&
                Section(0).IsFolded && Section(1).IsFolded,
                "Collapse all from a nested body places caret on the visible outermost header");
            window.SetAllBlocksCollapsed(false);
            Section(1).IsFolded = true;
            window.Editor.Select(0, 0);
            window.Editor.CaretOffset = 0;
            Check(window.RemoveCurrentBlock() && window.Foldings.AllFoldings.Count() == 1 && Section(0).IsFolded &&
                window.Editor.Text.Contains("内层知识") && window.Editor.Text.Contains("外层知识"),
                "Removing outer markers preserves the inner block's collapsed state and body");
            window.Editor.Undo();
            Check(window.Editor.Text == nested && window.Foldings.AllFoldings.Count() == 2,
                "A single Undo restores both removed outer markers around the inner block");

            Fresh("正文\r\n【补充知识：旧标题】\r\n孤立标记后面的知识\r\n");
            string orphanOriginal = window.Editor.Text;
            SelectText("【补充知识：旧标题】");
            Check(!window.CreateKnowledgeBlock() && window.Editor.Text == orphanOriginal &&
                !window.Editor.Document.UndoStack.CanUndo,
                "Wrapping an orphan header is rejected without throwing or changing the note");

            Fresh("【补充知识：术语】\r\n应该自动显示的隐藏正文\r\n【/补充知识】\r\n正文");
            window.SetAllBlocksCollapsed(true);
            block = KnowledgeRegions.Parse(window.Editor.Text).Single();
            window.Editor.Document.Remove(block.FooterStart, block.FooterLength);
            Check(!window.Foldings.AllFoldings.Any() && window.Editor.Text.Contains("应该自动显示的隐藏正文"),
                "Deleting a closing marker removes the fold and exposes its complete body");

            Fresh("前言\r\n【补充知识：概念】\r\n隐藏内容不可误删\r\n【/补充知识】\r\n继续正文\r\n");
            string protectedText = window.Editor.Text;
            window.Editor.Select(Section(0).StartOffset, 0);
            window.Editor.CaretOffset = Section(0).StartOffset;
            Section(0).IsFolded = true;
            Check(window.PrepareDestructiveEdit(false) && !Section(0).IsFolded &&
                window.Editor.Text == protectedText, "Delete at a folded start first expands without deleting hidden content");
            Check(!window.PrepareDestructiveEdit(false), "Delete can continue once the content is visible");
            window.Editor.Select(Section(0).EndOffset, 0);
            window.Editor.CaretOffset = Section(0).EndOffset;
            Section(0).IsFolded = true;
            Check(window.PrepareDestructiveEdit(true) && !Section(0).IsFolded &&
                window.Editor.Text == protectedText, "Backspace at a folded end first expands without deleting hidden content");
            Check(!window.PrepareDestructiveEdit(true), "Backspace can continue once the content is visible");
            window.Editor.Select(0, window.Editor.Document.TextLength);
            Section(0).IsFolded = true;
            Check(window.ExpandHiddenSelection() && !Section(0).IsFolded && window.Editor.Text == protectedText,
                "Selection that includes hidden content expands before text replacement");
            Check(!window.ExpandHiddenSelection(), "Selection replacement can continue after hidden content is revealed");

            Fresh("前言\r\n【补充知识：概念】\r\n隐藏内容不可误删\r\n【/补充知识】\r\n继续正文\r\n");
            protectedText = window.Editor.Text;
            block = KnowledgeRegions.Parse(protectedText).Single();
            window.Editor.Select(block.HeaderStart, 0);
            window.Editor.CaretOffset = block.HeaderStart;
            Section(0).IsFolded = true;
            AvalonEditCommands.DeleteLine.Execute(null, window.Editor.TextArea);
            Check(window.Editor.Text == protectedText && !Section(0).IsFolded,
                "Actual AvalonEdit DeleteLine command first expands a collapsed heading without deletion");
            AvalonEditCommands.DeleteLine.Execute(null, window.Editor.TextArea);
            Check(window.Editor.Text != protectedText && window.Editor.Text.Contains("隐藏内容不可误删") &&
                !window.Foldings.AllFoldings.Any(),
                "Second DeleteLine executes normally after the heading is expanded");

            Fresh("前言\r\n【补充知识：概念】\r\n隐藏内容不可误删\r\n【/补充知识】\r\n继续正文\r\n");
            protectedText = window.Editor.Text;
            window.Editor.Select(0, window.Editor.Document.TextLength);
            Section(0).IsFolded = true;
            EditingCommands.DeleteNextWord.Execute(null, window.Editor.TextArea);
            Check(window.Editor.Text == protectedText && !Section(0).IsFolded,
                "Actual DeleteNextWord overlapping hidden selection expands it before deleting");
            EditingCommands.DeleteNextWord.Execute(null, window.Editor.TextArea);
            Check(window.Editor.Text.Length < protectedText.Length,
                "Second DeleteNextWord executes normally after the selected body becomes visible");

            Fresh("前言\r\n【补充知识：概念】\r\n隐藏内容不可误删\r\n【/补充知识】\r\n继续正文\r\n");
            protectedText = window.Editor.Text;
            ICommand[] deletions = new ICommand[] { EditingCommands.DeleteNextWord,
                EditingCommands.DeletePreviousWord, EditingCommands.Delete, EditingCommands.Backspace };
            foreach (ICommand command in deletions)
            {
                block = KnowledgeRegions.Parse(window.Editor.Text).Single();
                window.Editor.Select(block.HeaderStart, 0);
                window.Editor.CaretOffset = block.HeaderStart;
                Section(0).IsFolded = true;
                Check(window.PrepareEditingCommand(command) && !Section(0).IsFolded &&
                    window.Editor.Text == protectedText,
                    "Deletion command guard expands collapsed header: " + ((RoutedCommand)command).Name);
            }

            window.Editor.Document.Insert(0, "尚未保存\r\n");
            dialogs.Choice = UnsavedChoice.Cancel;
            int prompts = dialogs.Prompts;
            window.Close();
            Check(window.IsVisible && dialogs.Prompts == prompts + 1 && window.Document.IsDirty,
                "Actual Close asks to save dirty notes and Cancel keeps the window open");
            dialogs.Choice = UnsavedChoice.Discard;
            Check(window.NewDocument() && window.Editor.Text == "" && !window.Foldings.AllFoldings.Any(),
                "New document clears note and all folding sections");
            window.Editor.Undo();
            Check(window.Editor.Text == "" && !window.Editor.Document.UndoStack.CanUndo,
                "New document cannot undo back into the old note or hidden blocks");

            Fresh("论文阅读笔记\r\n\r\n一、核心想法\r\n注意力机制建立不同位置之间的联系。\r\n\r\n【补充知识：查询、键和值】\r\n查询（Query）：当前正在寻找的信息。\r\n键（Key）：用于比较信息相关性的特征。\r\n值（Value）：最后被汇总的实际内容。\r\n这部分帮助理解概念，阅读时可以暂时收起。\r\n【/补充知识】\r\n\r\n二、自己的问题\r\n为什么要缩放点积？实验结果依赖哪些设置？\r\n\r\n【补充知识：缩放点积】\r\n维度较大时，点积可能变大。\r\n缩放有助于避免 softmax 的梯度过小。\r\n【/补充知识】\r\n\r\n三、下一步\r\n继续阅读实验部分。\r\n");
            window.SetAllBlocksCollapsed(true);
            Render(600, 610, "preview-folded.png");
            window.SetAllBlocksCollapsed(false);
            Render(600, 740, "preview-expanded.png");
            Check(true, "Actual WPF folding UI renders expanded and collapsed previews");
            Save("预览笔记.txt");
            window.SetAllBlocksCollapsed(true);
            prompts = dialogs.Prompts;
            window.Close();
            Check(!window.IsVisible && dialogs.Prompts == prompts,
                "Collapsing saved notes alone does not prompt on actual Close");
            Check(dialogs.Errors == 0, "No unexpected file dialog errors occurred");
            application.Shutdown();
            Console.WriteLine("RESULT: " + passed + " folding window checks passed.");
            return 0;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            if (window != null) { dialogs.Choice = UnsavedChoice.Discard; window.Close(); }
            if (application != null) application.Shutdown();
            return 1;
        }
        finally
        {
            string baseDirectory = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory);
            string target = Path.GetFullPath(directory);
            if (target.StartsWith(baseDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase) &&
                Path.GetFileName(target).StartsWith("folding-window-tests-", StringComparison.Ordinal))
                Directory.Delete(target, true);
        }
    }

    private static void Render(double width, double height, string filename)
    {
        window.Width = width + 16;
        window.Height = height + 39;
        window.UpdateLayout();
        window.Dispatcher.Invoke(DispatcherPriority.ContextIdle, new Action(delegate { }));
        FrameworkElement content = (FrameworkElement)window.Content;
        content.Measure(new Size(width, height));
        content.Arrange(new Rect(0, 0, width, height));
        content.UpdateLayout();
        RenderTargetBitmap bitmap = new RenderTargetBitmap((int)width, (int)height,
            96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        PngBitmapEncoder encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (FileStream stream = File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, filename)))
            encoder.Save(stream);
    }
}
