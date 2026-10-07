using System;
using System.Reflection;
using System.Windows;
using System.Runtime.Versioning;

[assembly: AssemblyTitle("置顶记事本")]
[assembly: AssemblyDescription("用于阅读论文与文章时记录笔记的轻量置顶 TXT 编辑器")]
[assembly: AssemblyProduct("置顶记事本")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]
[assembly: TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]

namespace TopNote
{
    internal static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            Application application = new Application();
            application.ShutdownMode = ShutdownMode.OnMainWindowClose;
            MainWindow window = new MainWindow(new NoteDialogs());
            application.MainWindow = window;
            if (args.Length > 0)
                window.Loaded += delegate { window.OpenPath(args[0], false); };
            application.Run(window);
        }
    }
}
