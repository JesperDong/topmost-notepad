using System;
using System.IO;
using System.Text;
using System.Security.Cryptography;

namespace TopNote
{
    // Plain-text storage is kept separate from the window so failed reads/writes
    // never replace a user's current editor content or clear the dirty flag.
    public sealed class NoteDocument
    {
        public const long MaximumFileBytes = 20L * 1024 * 1024;
        private string savedText = "";
        private string savedRawText = "";
        private Encoding encoding = StrictUtf8();
        private byte[] preamble = new byte[0];
        private string newline = "\r\n";
        private byte[] savedDiskHash;

        public string FilePath { get; private set; }
        public string Text { get; set; }
        public string EncodingName { get; private set; }
        public bool IsDirty { get { return Text != savedText; } }
        public string DisplayName { get { return FilePath == null ? "无标题" : Path.GetFileName(FilePath); } }

        public NoteDocument()
        {
            Text = "";
            EncodingName = "UTF-8";
        }

        private static Encoding StrictUtf8()
        {
            return new UTF8Encoding(false, true);
        }

        private static bool StartsWith(byte[] bytes, params byte[] prefix)
        {
            if (bytes.Length < prefix.Length) return false;
            for (int i = 0; i < prefix.Length; i++)
                if (bytes[i] != prefix[i]) return false;
            return true;
        }

        public static string Normalize(string value)
        {
            return value.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");
        }

        public static NoteDocument Open(string path)
        {
            path = Path.GetFullPath(path);
            byte[] bytes;
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length > MaximumFileBytes)
                    throw new IOException("这个文件超过 20 MB。请用其他编辑器打开较大的文件。");
                bytes = new byte[(int)stream.Length];
                int read = 0;
                while (read < bytes.Length)
                {
                    int count = stream.Read(bytes, read, bytes.Length - read);
                    if (count == 0) throw new IOException("读取过程中，文件内容发生了变化，请重新打开。");
                    read += count;
                }
            }
            NoteDocument document = new NoteDocument();
            int offset = 0;
            if (StartsWith(bytes, 0xFF, 0xFE, 0x00, 0x00))
            {
                document.encoding = new UTF32Encoding(false, false, true);
                document.EncodingName = "UTF-32 LE";
                offset = 4;
            }
            else if (StartsWith(bytes, 0x00, 0x00, 0xFE, 0xFF))
            {
                document.encoding = new UTF32Encoding(true, false, true);
                document.EncodingName = "UTF-32 BE";
                offset = 4;
            }
            else if (StartsWith(bytes, 0xEF, 0xBB, 0xBF))
            {
                document.EncodingName = "UTF-8 BOM";
                offset = 3;
            }
            else if (StartsWith(bytes, 0xFF, 0xFE))
            {
                document.encoding = new UnicodeEncoding(false, false, true);
                document.EncodingName = "UTF-16 LE";
                offset = 2;
            }
            else if (StartsWith(bytes, 0xFE, 0xFF))
            {
                document.encoding = new UnicodeEncoding(true, false, true);
                document.EncodingName = "UTF-16 BE";
                offset = 2;
            }

            string original;
            try { original = document.encoding.GetString(bytes, offset, bytes.Length - offset); }
            catch (DecoderFallbackException)
            {
                // BOM identifies an encoding conclusively. Do not reinterpret a
                // malformed BOM-marked file as legacy Chinese text.
                if (offset != 0) throw new IOException("文件编码不完整或已损坏，无法读取。");
                document.encoding = Encoding.GetEncoding(54936, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
                document.EncodingName = "GB18030";
                try { original = document.encoding.GetString(bytes); }
                catch (DecoderFallbackException) { throw new IOException("无法识别这个 TXT 的编码。请先将它转换为 UTF-8。"); }
            }
            // Unmarked UTF-16 and binary files must not become silently damaged TXTs.
            if (original.IndexOf('\0') >= 0)
                throw new IOException("文件包含无法显示的字符。若是 UTF-16 文件，请先另存为带 BOM 的 UTF-16 或 UTF-8。");

            if (offset != 0)
            {
                document.preamble = new byte[offset];
                Array.Copy(bytes, document.preamble, offset);
            }
            document.newline = FindNewline(original);
            document.Text = Normalize(original);
            document.savedText = document.Text;
            document.savedRawText = original;
            document.FilePath = path;
            document.savedDiskHash = Hash(bytes);
            return document;
        }

        private static byte[] Hash(byte[] bytes)
        {
            using (SHA256 algorithm = SHA256.Create()) return algorithm.ComputeHash(bytes);
        }

        public bool HasExternalChanges(string path)
        {
            path = Path.GetFullPath(path);
            if (FilePath == null || !String.Equals(path, FilePath, StringComparison.OrdinalIgnoreCase)) return false;
            if (!File.Exists(path)) return true;
            byte[] current;
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (SHA256 algorithm = SHA256.Create()) current = algorithm.ComputeHash(stream);
            for (int i = 0; i < current.Length; i++) if (current[i] != savedDiskHash[i]) return true;
            return false;
        }

        private static string FindNewline(string text)
        {
            int crlf = 0, lf = 0, cr = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\r')
                {
                    if (i + 1 < text.Length && text[i + 1] == '\n') { crlf++; i++; }
                    else cr++;
                }
                else if (text[i] == '\n') lf++;
            }
            if (lf > crlf && lf >= cr) return "\n";
            if (cr > crlf && cr > lf) return "\r";
            return "\r\n";
        }

        public void Save(string path, bool useUtf8)
        {
            path = Path.GetFullPath(path);
            Encoding outputEncoding = useUtf8 ? StrictUtf8() : encoding;
            byte[] outputPreamble = useUtf8 ? new byte[0] : preamble;
            string value = IsDirty ? Normalize(Text).Replace("\r\n", newline) : savedRawText;
            byte[] body = outputEncoding.GetBytes(value);
            // Encode first; an unrepresentable character cannot truncate the file.
            byte[] bytes = new byte[outputPreamble.Length + body.Length];
            Array.Copy(outputPreamble, bytes, outputPreamble.Length);
            Array.Copy(body, 0, bytes, outputPreamble.Length, body.Length);
            AtomicWrite(path, bytes);
            FilePath = path;
            savedText = Text;
            savedRawText = value;
            savedDiskHash = Hash(bytes);
            if (useUtf8)
            {
                encoding = outputEncoding;
                preamble = outputPreamble;
                EncodingName = "UTF-8";
            }
        }

        private static void AtomicWrite(string path, byte[] bytes)
        {
            string directory = Path.GetDirectoryName(path);
            string temporary = Path.Combine(directory, ".topnote-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                // File.Replace leaves the original intact if replacement fails.
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    try { File.Delete(temporary); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }
    }
}
