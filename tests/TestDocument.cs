using System;
using System.IO;
using System.Text;
using TopNote;

// Standalone storage checks. All temporary files stay beneath this executable.
internal static class TestDocument
{
    private static int passed;
    private static int failed;
    private static string testRoot;

    private static int Main()
    {
        string baseDirectory = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory);
        testRoot = Path.Combine(baseDirectory, "document-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testRoot);
        try
        {
            Run("New document and UTF-8 save", NewDocument);
            Encoding[] encodings = new Encoding[] {
                new UTF8Encoding(false, true), new UTF8Encoding(true, true),
                new UnicodeEncoding(false, true, true), new UnicodeEncoding(true, true, true),
                new UTF32Encoding(false, true, true), new UTF32Encoding(true, true, true),
                Encoding.GetEncoding(54936, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)
            };
            string[] names = new string[] { "UTF-8", "UTF-8 BOM", "UTF-16 LE", "UTF-16 BE", "UTF-32 LE", "UTF-32 BE", "GB18030" };
            string[] newlines = new string[] { "\r\n", "\n", "\r" };
            for (int e = 0; e < encodings.Length; e++)
                for (int n = 0; n < newlines.Length; n++)
                {
                    Encoding selectedEncoding = encodings[e];
                    string selectedName = names[e];
                    string selectedNewline = newlines[n];
                    Run(selectedName + " / " + (n == 0 ? "CRLF" : n == 1 ? "LF" : "CR") + " byte-preserving round trip",
                        delegate { EncodingRoundTrip(selectedEncoding, selectedName, selectedNewline); });
                }
            Run("Read-only destination retains original and dirty state", ReadOnlyFailure);
            Run("Exclusive lock retains original and dirty state", LockedFailure);
            Run("Missing destination directory retains original and dirty state", MissingDirectoryFailure);
            Run("Malformed UTF-8 output retains original and dirty state", MalformedOutputFailure);
            Run("20 MB is accepted; 20 MB plus one byte is refused", FileSizeLimit);
            Run("Malformed BOM-marked and unknown encodings are refused", BadEncoding);
            Run("UTF-8 conversion and future save retain UTF-8", Utf8Conversion);
            Run("Empty BOM-marked file round trips", EmptyBomFile);
            Run("External changes: unchanged, same-size edit, deletion, and restored bytes", ExternalChanges);
            Run("External changes: new file and successful Save As reset baseline", ExternalChangesSave);
            Run("Failed UTF-8 conversion retains source encoding", FailedConversion);
            Run("Mixed newlines are retained on unchanged save", MixedNewlines);
            Console.WriteLine("RESULT: " + passed + " passed, " + failed + " failed.");
            return failed == 0 ? 0 : 1;
        }
        finally
        {
            // Never remove outside this run's uniquely named test directory.
            string resolved = Path.GetFullPath(testRoot);
            if (resolved.StartsWith(baseDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(resolved).StartsWith("document-tests-", StringComparison.Ordinal))
            {
                foreach (string file in Directory.GetFiles(resolved, "*", SearchOption.AllDirectories))
                    File.SetAttributes(file, FileAttributes.Normal);
                Directory.Delete(resolved, true);
            }
        }
    }

    private static void Run(string label, Action test)
    {
        try { test(); passed++; Console.WriteLine("PASS " + label); }
        catch (Exception exception) { failed++; Console.WriteLine("FAIL " + label + ": " + exception.GetType().Name + " - " + exception.Message); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static string UniqueFile()
    {
        return Path.Combine(testRoot, "中文笔记-" + Guid.NewGuid().ToString("N") + ".txt");
    }

    private static byte[] Encoded(Encoding encoding, string text)
    {
        byte[] preamble = encoding.GetPreamble();
        byte[] body = encoding.GetBytes(text);
        byte[] result = new byte[preamble.Length + body.Length];
        Buffer.BlockCopy(preamble, 0, result, 0, preamble.Length);
        Buffer.BlockCopy(body, 0, result, preamble.Length, body.Length);
        return result;
    }

    private static void SameBytes(byte[] expected, byte[] actual)
    {
        Require(expected.Length == actual.Length, "Byte length changed from " + expected.Length + " to " + actual.Length + ".");
        for (int i = 0; i < expected.Length; i++)
            Require(expected[i] == actual[i], "Byte changed at " + i + ".");
    }

    private static void ExpectIo(Action action)
    {
        try { action(); }
        catch (IOException) { return; }
        catch (UnauthorizedAccessException) { return; }
        throw new Exception("Expected an I/O failure.");
    }

    private static void NoTemporaryFiles()
    {
        Require(Directory.GetFiles(testRoot, ".topnote-*.tmp", SearchOption.AllDirectories).Length == 0, "An atomic-write temporary file was left behind.");
    }

    private static void NewDocument()
    {
        NoteDocument doc = new NoteDocument();
        Require(doc.FilePath == null && doc.Text == "" && !doc.IsDirty && doc.EncodingName == "UTF-8", "New document state is incorrect.");
        doc.Text = "中文笔记 😀\r\n第二行";
        Require(doc.IsDirty, "Editing did not make document dirty.");
        string path = UniqueFile();
        doc.Save(path, false);
        SameBytes(new UTF8Encoding(false, true).GetBytes(doc.Text), File.ReadAllBytes(path));
        Require(!doc.IsDirty && doc.FilePath == Path.GetFullPath(path), "Successful save did not update state.");
        Require(NoteDocument.Open(path).Text == doc.Text, "Reopened text differs.");
    }

    private static void EncodingRoundTrip(Encoding encoding, string expectedName, string newline)
    {
        string path = UniqueFile();
        string original = "论文阅读：中文与 emoji 😀" + newline + "第二行 αβ" + newline + "";
        byte[] bytes = Encoded(encoding, original);
        File.WriteAllBytes(path, bytes);
        NoteDocument doc = NoteDocument.Open(path);
        Require(doc.Text == NoteDocument.Normalize(original), "Decoded text differs.");
        Require(doc.EncodingName == expectedName, "Encoding name differs: " + doc.EncodingName);
        Require(!doc.IsDirty, "Open marked document dirty.");
        doc.Save(path, false);
        SameBytes(bytes, File.ReadAllBytes(path));
        Require(!doc.IsDirty, "Unchanged save marked document dirty.");
        doc.Text += "新增 🚀\r\n";
        doc.Save(path, false);
        Require(!doc.IsDirty, "Edited save did not clear dirty state.");
        SameBytes(Encoded(encoding, original + "新增 🚀" + newline), File.ReadAllBytes(path));
        NoTemporaryFiles();
    }

    private static NoteDocument PrepareDirty(out string path, out byte[] original)
    {
        path = UniqueFile();
        original = new UTF8Encoding(false, true).GetBytes("原稿\n原来的内容 😀\n");
        File.WriteAllBytes(path, original);
        NoteDocument doc = NoteDocument.Open(path);
        doc.Text += "还未保存的内容";
        Require(doc.IsDirty, "Preparation did not create dirty state.");
        return doc;
    }

    private static void FailedSaveState(NoteDocument doc, string originalPath, byte[] original)
    {
        SameBytes(original, File.ReadAllBytes(originalPath));
        Require(doc.IsDirty, "Failed save cleared dirty state.");
        Require(doc.FilePath == originalPath, "Failed save changed file path.");
        Require(doc.Text.EndsWith("还未保存的内容", StringComparison.Ordinal), "Failed save changed document content.");
        Require(!doc.HasExternalChanges(originalPath), "Failed save changed disk baseline.");
        NoTemporaryFiles();
    }

    private static void ReadOnlyFailure()
    {
        string path; byte[] original;
        NoteDocument doc = PrepareDirty(out path, out original);
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try { ExpectIo(delegate { doc.Save(path, false); }); }
        finally { File.SetAttributes(path, FileAttributes.Normal); }
        FailedSaveState(doc, path, original);
    }

    private static void LockedFailure()
    {
        string path; byte[] original;
        NoteDocument doc = PrepareDirty(out path, out original);
        using (FileStream handle = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            ExpectIo(delegate { doc.Save(path, false); });
        FailedSaveState(doc, path, original);
    }

    private static void MissingDirectoryFailure()
    {
        string path; byte[] original;
        NoteDocument doc = PrepareDirty(out path, out original);
        string missing = Path.Combine(testRoot, "directory-that-does-not-exist", "note.txt");
        ExpectIo(delegate { doc.Save(missing, true); });
        FailedSaveState(doc, path, original);
        Require(doc.EncodingName == "UTF-8", "Failed conversion changed encoding name.");
        Require(!File.Exists(missing), "Missing-directory save created a file.");
    }

    private static void MalformedOutputFailure()
    {
        string path; byte[] original;
        NoteDocument doc = PrepareDirty(out path, out original);
        doc.Text = "\ud800";
        bool refused = false;
        try { doc.Save(path, false); }
        catch (EncoderFallbackException) { refused = true; }
        Require(refused, "Unpaired surrogate was not refused.");
        SameBytes(original, File.ReadAllBytes(path));
        Require(doc.IsDirty && doc.Text == "\ud800" && doc.FilePath == path, "Failed encoding changed state.");
        NoTemporaryFiles();
    }

    private static void FileSizeLimit()
    {
        string path = UniqueFile();
        byte[] bytes = new byte[(int)NoteDocument.MaximumFileBytes];
        for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)'a';
        File.WriteAllBytes(path, bytes);
        Require(NoteDocument.Open(path).Text.Length == bytes.Length, "Exactly 20 MB was not read completely.");
        using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
            stream.SetLength(NoteDocument.MaximumFileBytes + 1);
        ExpectIo(delegate { NoteDocument.Open(path); });
    }

    private static void BadEncoding()
    {
        byte[][] invalid = new byte[][] {
            new byte[] { 0xEF, 0xBB, 0xBF, 0xC3, 0x28 },
            new byte[] { 0xFF, 0xFE, 0x41 },
            new byte[] { 0xFE, 0xFF, 0x00 },
            new byte[] { 0xFF, 0xFE, 0x00, 0x00, 0x41 },
            new byte[] { 0x00, 0x00, 0xFE, 0xFF, 0x41 },
            new byte[] { 0xFF },
            new byte[] { 0x41, 0x00, 0x42, 0x00 }
        };
        foreach (byte[] bytes in invalid)
        {
            string path = UniqueFile();
            File.WriteAllBytes(path, bytes);
            ExpectIo(delegate { NoteDocument.Open(path); });
            SameBytes(bytes, File.ReadAllBytes(path));
        }
    }

    private static void Utf8Conversion()
    {
        string path = UniqueFile();
        File.WriteAllBytes(path, Encoded(new UnicodeEncoding(true, true, true), "中文 😀\n"));
        NoteDocument doc = NoteDocument.Open(path);
        doc.Save(path, true);
        SameBytes(new UTF8Encoding(false, true).GetBytes("中文 😀\n"), File.ReadAllBytes(path));
        Require(doc.EncodingName == "UTF-8", "Conversion retained old encoding name.");
        doc.Text += "追加\r\n";
        doc.Save(path, false);
        SameBytes(new UTF8Encoding(false, true).GetBytes("中文 😀\n追加\n"), File.ReadAllBytes(path));
    }

    private static void EmptyBomFile()
    {
        string path = UniqueFile();
        byte[] bytes = new UTF8Encoding(true, true).GetPreamble();
        File.WriteAllBytes(path, bytes);
        NoteDocument doc = NoteDocument.Open(path);
        Require(doc.Text == "" && !doc.IsDirty, "Empty BOM file state is incorrect.");
        doc.Save(path, false);
        SameBytes(bytes, File.ReadAllBytes(path));
    }

    private static void MixedNewlines()
    {
        string path = UniqueFile();
        byte[] bytes = new UTF8Encoding(false, true).GetBytes("one\r\ntwo\nthree\rfour\r\n");
        File.WriteAllBytes(path, bytes);
        NoteDocument doc = NoteDocument.Open(path);
        doc.Save(path, false);
        SameBytes(bytes, File.ReadAllBytes(path));
    }

    private static void ExternalChanges()
    {
        string path = UniqueFile();
        byte[] original = new UTF8Encoding(false, true).GetBytes("first line\n");
        File.WriteAllBytes(path, original);
        NoteDocument doc = NoteDocument.Open(path);
        Require(!doc.HasExternalChanges(path), "Unchanged disk was reported changed.");
        Require(!doc.HasExternalChanges(path.ToUpperInvariant()), "Windows case-insensitive path was not recognized.");
        DateTime time = File.GetLastWriteTimeUtc(path);
        byte[] changed = new UTF8Encoding(false, true).GetBytes("other line\n");
        Require(original.Length == changed.Length, "Test setup lengths differ.");
        File.WriteAllBytes(path, changed);
        File.SetLastWriteTimeUtc(path, time);
        Require(doc.HasExternalChanges(path), "Same-size edit with preserved timestamp was missed.");
        Require(!doc.HasExternalChanges(UniqueFile()), "Different destination was reported externally changed.");
        File.WriteAllBytes(path, original);
        Require(!doc.HasExternalChanges(path), "Restored bytes were reported changed.");
        File.Delete(path);
        Require(doc.HasExternalChanges(path), "Deleted file was not detected.");
        Require(!doc.IsDirty, "External detection changed editor dirty state.");
    }

    private static void ExternalChangesSave()
    {
        string path = UniqueFile();
        NoteDocument doc = new NoteDocument();
        Require(!doc.HasExternalChanges(path), "New document reported an external change.");
        doc.Text = "new note";
        doc.Save(path, false);
        Require(!doc.HasExternalChanges(path), "New file save did not set disk baseline.");
        File.WriteAllText(path, "external edit");
        Require(doc.HasExternalChanges(path), "External edit was missed.");
        doc.Text += " saved edit";
        doc.Save(path, false);
        Require(!doc.HasExternalChanges(path), "Successful overwrite did not reset baseline.");
        string anotherPath = UniqueFile();
        doc.Save(anotherPath, true);
        Require(!doc.HasExternalChanges(anotherPath), "Save As did not set new baseline.");
        File.WriteAllText(path, "changed old file");
        Require(!doc.HasExternalChanges(path), "Old path remained the Save As baseline.");
        File.Delete(anotherPath);
        Require(doc.HasExternalChanges(anotherPath), "Save As target deletion was missed.");
    }

    private static void FailedConversion()
    {
        string path = UniqueFile();
        Encoding sourceEncoding = new UnicodeEncoding(true, true, true);
        byte[] original = Encoded(sourceEncoding, "中文原稿 😀\n");
        File.WriteAllBytes(path, original);
        NoteDocument doc = NoteDocument.Open(path);
        doc.Text += "追加\r\n";
        string missing = Path.Combine(testRoot, "another-missing-directory", "note.txt");
        ExpectIo(delegate { doc.Save(missing, true); });
        Require(doc.IsDirty && doc.EncodingName == "UTF-16 BE" && doc.FilePath == path, "Failed UTF-8 conversion changed state.");
        Require(!doc.HasExternalChanges(path), "Failed conversion changed disk baseline.");
        SameBytes(original, File.ReadAllBytes(path));
        doc.Save(path, false);
        SameBytes(Encoded(sourceEncoding, "中文原稿 😀\n追加\n"), File.ReadAllBytes(path));
        NoTemporaryFiles();
    }
}
