using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TopNote;

internal sealed class FakeDialogs : INoteDialogs
{
    public string OpenPath, SavePath;
    public UnsavedChoice Choice = UnsavedChoice.Cancel;
    public bool Convert, Overwrite;
    public int Errors, Prompts, ExternalPrompts;
    public string ChooseOpen(Window owner) { return OpenPath; }
    public string ChooseSave(Window owner, string currentPath) { return SavePath; }
    public UnsavedChoice AskUnsaved(Window owner, string name) { Prompts++; return Choice; }
    public bool AskConvertUtf8(Window owner) { return Convert; }
    public bool AskExternalChange(Window owner) { ExternalPrompts++; return Overwrite; }
    public void ShowError(Window owner, string action, Exception error) { Errors++; }
}

internal static class TestWindow
{
    [DllImport("user32.dll", EntryPoint="GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr window, int index);
    private static int passed;
    private static string directory;

    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception("FAIL: " + message);
        passed++;
        Console.WriteLine("PASS: " + message);
    }

    [STAThread]
    private static int Main()
    {
        directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "window-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        MainWindow window = null;
        FakeDialogs dialogs = new FakeDialogs();
        try
        {
            Application application = new Application();
            application.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            window = new MainWindow(dialogs);
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -12000;
            window.Top = -12000;
            window.ShowActivated = false;
            window.ShowInTaskbar = false;
            window.Show();
            window.UpdateLayout();
            IntPtr handle = new WindowInteropHelper(window).Handle;
            Check(window.Topmost && (GetWindowLong(handle, -20) & 8) != 0, "Default pin sets actual WS_EX_TOPMOST window style");
            window.TogglePin();
            Check(!window.Topmost && (GetWindowLong(handle, -20) & 8) == 0, "Pin toggle removes actual topmost style");
            window.TogglePin();
            Check(window.Topmost && (GetWindowLong(handle, -20) & 8) != 0, "Pin toggle restores actual topmost style");
            Check(window.Editor.WordWrap && !window.Editor.IsReadOnly && !window.Editor.Options.ConvertTabsToSpaces, "Editor supports editable paragraphs, tabs and word wrap");

            window.Editor.Text = "论文笔记 \U0001F4DD\r\n核心结论";
            Check(window.Document.IsDirty && window.Title.StartsWith("* "), "Typing updates document and dirty title");
            Check(!window.NewDocument() && window.Editor.Text.Contains("核心结论"), "Cancel on New preserves typed content");
            dialogs.Choice = UnsavedChoice.Save;
            Check(!window.NewDocument() && window.Document.IsDirty, "Cancel Save As stops New");
            dialogs.SavePath = Path.Combine(directory, "中文笔记.txt");
            Check(window.SaveDocument(false) && !window.Document.IsDirty && File.ReadAllText(dialogs.SavePath).Contains("\U0001F4DD"), "Save writes UTF-8 Chinese/emoji and clears dirty state");
            window.Editor.AppendText("\r\n增补");
            Check(window.Document.IsDirty, "Additional typing marks saved document dirty");
            window.Editor.Undo();
            Check(!window.Document.IsDirty, "Undo back to saved content clears dirty state");

            window.Editor.AppendText("旧内容");
            dialogs.Choice = UnsavedChoice.Discard;
            Check(window.NewDocument() && window.Editor.Text == "", "Discard on New clears editor");
            window.Editor.Undo();
            Check(window.Editor.Text == "", "New document cannot undo into previous document");

            window.Editor.Text = "这段笔记需要保留";
            dialogs.OpenPath = Path.Combine(directory, "missing.txt");
            int errors = dialogs.Errors;
            Check(!window.OpenDocument() && window.Editor.Text == "这段笔记需要保留" && dialogs.Errors == errors + 1,
                "Failed Open preserves original dirty note");
            string loadedPath = Path.Combine(directory, "to-open.txt");
            File.WriteAllText(loadedPath, "已打开\n第二行", new UTF8Encoding(false));
            dialogs.OpenPath = loadedPath;
            dialogs.Choice = UnsavedChoice.Cancel;
            Check(!window.OpenDocument() && window.Editor.Text == "这段笔记需要保留", "Cancel unsaved prompt stops Open");
            dialogs.Choice = UnsavedChoice.Discard;
            Check(window.OpenDocument() && window.Editor.Text == "已打开\r\n第二行" && !window.Document.IsDirty, "Open normalizes editor newlines without marking dirty");
            window.Editor.AppendText("修改");
            dialogs.Choice = UnsavedChoice.Save;
            dialogs.OpenPath = loadedPath;
            Check(window.OpenDocument() && window.Editor.Text.Contains("修改") && !window.Document.IsDirty, "Opening same file after Save loads newly saved content");

            File.WriteAllText(loadedPath, "其他程序的内容", new UTF8Encoding(false));
            window.Editor.AppendText("本地增补");
            Check(!window.SaveDocument(false) && window.Document.IsDirty && File.ReadAllText(loadedPath) == "其他程序的内容" && dialogs.ExternalPrompts == 1,
                "External modification is protected when overwrite declined");
            dialogs.Overwrite = true;
            Check(window.SaveDocument(false) && File.ReadAllText(loadedPath).Contains("本地增补") && !window.Document.IsDirty,
                "Explicit overwrite saves current notes");
            File.Delete(loadedPath);
            Check(window.SaveDocument(false) && File.Exists(loadedPath), "Clean Save recreates externally deleted file after confirmation");

            string saveAs = Path.Combine(directory, "另存为.txt");
            dialogs.SavePath = saveAs;
            Check(window.SaveDocument(true) && window.Document.FilePath == saveAs && File.Exists(saveAs), "Save As switches path and preserves text");
            window.Editor.AppendText("尚未保存");
            File.SetAttributes(saveAs, FileAttributes.ReadOnly);
            errors = dialogs.Errors;
            Check(!window.CanLeaveDocument() && window.Document.IsDirty && dialogs.Errors == errors + 1, "Failed Save blocks leaving current document");
            File.SetAttributes(saveAs, FileAttributes.Normal);

            dialogs.Choice = UnsavedChoice.Discard;
            window.NewDocument();
            Render(window, 600, 610, "preview-empty.png");
            window.Editor.Text = "论文阅读笔记\r\n\r\n题目：Attention Is All You Need\r\n\r\n一、核心想法\r\n通过注意力机制，建立不同位置之间的联系。\r\n\r\n二、阅读时的问题\r\n• 这个结论依赖哪些假设？\r\n• 和之前读过的方法有什么区别？\r\n\r\n三、自己的理解\r\n把窗口放在论文旁边，随时记录想法。\r\n\r\n待查：实验设置与评价指标。";
            Render(window, 600, 610, "preview-note.png");
            Render(window, 404, 430, "preview-narrow.png");
            Check(true, "WPF templates and sample note render at normal and minimum width");

            dialogs.Choice = UnsavedChoice.Cancel;
            window.Close();
            Check(window.IsVisible, "Cancel closing keeps actual window open");
            dialogs.Choice = UnsavedChoice.Discard;
            window.Close();
            Check(!window.IsVisible, "Discard closing closes actual window");
            application.Shutdown();
            Console.WriteLine("RESULT: " + passed + " window checks passed.");
            return 0;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            if (window != null) { dialogs.Choice = UnsavedChoice.Discard; window.Close(); }
            return 1;
        }
        finally
        {
            string baseDirectory = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory);
            string target = Path.GetFullPath(directory);
            if (target.StartsWith(baseDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(target).StartsWith("window-tests-", StringComparison.Ordinal))
            {
                foreach (string file in Directory.GetFiles(target)) File.SetAttributes(file, FileAttributes.Normal);
                Directory.Delete(target, true);
            }
        }
    }

    private static void Render(MainWindow window, double width, double height, string name)
    {
        window.Width = width + 16;
        window.Height = height + 39;
        window.UpdateLayout();
        FrameworkElement content = (FrameworkElement)window.Content;
        content.Measure(new Size(width, height));
        content.Arrange(new Rect(0, 0, width, height));
        content.UpdateLayout();
        RenderTargetBitmap bitmap = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        PngBitmapEncoder encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (FileStream stream = File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name))) encoder.Save(stream);
    }
}
