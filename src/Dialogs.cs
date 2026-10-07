using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace TopNote
{
    public enum UnsavedChoice { Save, Discard, Cancel }

    public interface INoteDialogs
    {
        string ChooseOpen(Window owner);
        string ChooseSave(Window owner, string currentPath);
        UnsavedChoice AskUnsaved(Window owner, string name);
        bool AskConvertUtf8(Window owner);
        bool AskExternalChange(Window owner);
        void ShowError(Window owner, string action, Exception exception);
    }

    public sealed class NoteDialogs : INoteDialogs
    {
        private const string Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*";

        public string ChooseOpen(Window owner)
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Title = "打开文本文件";
            dialog.Filter = Filter;
            dialog.CheckFileExists = true;
            dialog.Multiselect = false;
            return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
        }

        public string ChooseSave(Window owner, string currentPath)
        {
            SaveFileDialog dialog = new SaveFileDialog();
            dialog.Title = "保存文本文件";
            dialog.Filter = Filter;
            dialog.DefaultExt = ".txt";
            dialog.AddExtension = true;
            dialog.OverwritePrompt = true;
            dialog.FileName = currentPath == null ? "我的笔记.txt" : Path.GetFileName(currentPath);
            if (currentPath != null) dialog.InitialDirectory = Path.GetDirectoryName(currentPath);
            return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
        }

        public UnsavedChoice AskUnsaved(Window owner, string name)
        {
            MessageBoxResult result = MessageBox.Show(owner,
                "要保存对“" + name + "”的修改吗？\n\n是：保存后继续\n否：不保存，继续\n取消：回到笔记",
                "保存笔记", MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
            if (result == MessageBoxResult.Yes) return UnsavedChoice.Save;
            if (result == MessageBoxResult.No) return UnsavedChoice.Discard;
            return UnsavedChoice.Cancel;
        }

        public bool AskConvertUtf8(Window owner)
        {
            return MessageBox.Show(owner, "原文件编码无法保存部分新输入的字符。\n是否改用 UTF-8 保存，以完整保留文字？",
                "改用 UTF-8", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
        }

        public bool AskExternalChange(Window owner)
        {
            return MessageBox.Show(owner, "这个文件在打开后已被其他程序修改或删除。\n\n是否用当前笔记覆盖或重新创建？\n选“否”可保留当前内容，再使用“另存为”。",
                "文件已发生变化", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
        }

        public void ShowError(Window owner, string action, Exception exception)
        {
            MessageBox.Show(owner, action + "失败。你的当前笔记仍然保留。\n\n" + exception.Message,
                "置顶记事本", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
