using System;
using System.Collections.Generic;
using System.Text;

namespace TopNote
{
    // Offsets use UTF-16 character positions, as do WPF and AvalonEdit.
    // Marker lengths include their line delimiter, so removing a region leaves
    // only the original body lines and does not join unrelated neighbouring lines.
    public sealed class RegionBlock
    {
        public int HeaderStart { get; private set; }
        public int HeaderLength { get; private set; }
        public int ContentStart { get; private set; }
        public int ContentEnd { get; private set; }
        public int FooterStart { get; private set; }
        public int FooterLength { get; private set; }
        public int EndOffset { get; private set; }
        public int TotalEndOffset { get; private set; }
        public string Title { get; private set; }
        public int BodyLineCount { get; private set; }
        public int FoldStart { get; private set; }
        public int ContentLength { get { return ContentEnd - ContentStart; } }

        internal RegionBlock(int headerStart, int headerLength, int foldStart,
            int contentStart, int contentEnd, int footerStart, int footerLength,
            int endOffset, int totalEndOffset, string title, int bodyLineCount)
        {
            HeaderStart = headerStart;
            HeaderLength = headerLength;
            FoldStart = foldStart;
            ContentStart = contentStart;
            ContentEnd = contentEnd;
            FooterStart = footerStart;
            FooterLength = footerLength;
            EndOffset = endOffset;
            TotalEndOffset = totalEndOffset;
            Title = title;
            BodyLineCount = bodyLineCount;
        }
    }

    public sealed class RegionEdit
    {
        public string Text { get; private set; }
        public int HeaderStart { get; private set; }
        public int ContentStart { get; private set; }
        public bool SelectionWasEmpty { get; private set; }

        internal RegionEdit(string text, int headerStart, int contentStart, bool empty)
        {
            Text = text;
            HeaderStart = headerStart;
            ContentStart = contentStart;
            SelectionWasEmpty = empty;
        }
    }

    public static class KnowledgeRegions
    {
        public const string HeaderPrefix = "【补充知识：";
        public const string Footer = "【/补充知识】";
        private const string HeaderSuffix = "】";

        private sealed class TextLine
        {
            public int Start;
            public int Length;
            public int DelimiterLength;
            public int End { get { return Start + Length; } }
            public int TotalEnd { get { return End + DelimiterLength; } }
        }

        private sealed class OpenHeader
        {
            public int LineIndex;
            public string Title;
        }

        public static List<RegionBlock> Parse(string text)
        {
            if (text == null) throw new ArgumentNullException("text");
            List<TextLine> lines = ReadLines(text);
            List<RegionBlock> result = new List<RegionBlock>();
            Stack<OpenHeader> stack = new Stack<OpenHeader>();
            for (int i = 0; i < lines.Count; i++)
            {
                TextLine line = lines[i];
                string value = text.Substring(line.Start, line.Length);
                string title;
                if (TryReadHeader(value, out title))
                {
                    stack.Push(new OpenHeader { LineIndex = i, Title = title });
                }
                else if (value == Footer && stack.Count != 0)
                {
                    OpenHeader open = stack.Pop();
                    TextLine header = lines[open.LineIndex];
                    result.Add(new RegionBlock(header.Start,
                        header.Length + header.DelimiterLength, header.End,
                        header.TotalEnd, line.Start, line.Start,
                        line.Length + line.DelimiterLength, line.End,
                        line.TotalEnd, open.Title, i - open.LineIndex - 1));
                }
            }
            result.Sort(delegate(RegionBlock a, RegionBlock b) {
                return a.HeaderStart.CompareTo(b.HeaderStart);
            });
            return result;
        }

        public static RegionEdit Wrap(string text, int selectionStart, int selectionLength)
        {
            if (text == null) throw new ArgumentNullException("text");
            if (selectionStart < 0 || selectionStart > text.Length)
                throw new ArgumentOutOfRangeException("selectionStart");
            if (selectionLength < 0 || selectionLength > text.Length - selectionStart)
                throw new ArgumentOutOfRangeException("selectionLength");

            List<TextLine> lines = ReadLines(text);
            string newline = GetNewline(text, lines);
            TextLine first = lines[FindLine(lines, selectionStart)];
            int start = first.Start;
            if (selectionLength == 0)
            {
                string header = HeaderPrefix + "新知识" + HeaderSuffix + newline;
                string inserted = header + newline + Footer + newline;
                string updated = text.Insert(start, inserted);
                ValidateNewRegion(updated, start, start + header.Length,
                    start + header.Length + newline.Length);
                return new RegionEdit(updated, start,
                    start + header.Length, true);
            }

            int selectionEnd = selectionStart + selectionLength;
            TextLine last = lines[FindLine(lines, selectionEnd)];
            // A selection ending exactly at the next line start excludes that line.
            int end = selectionEnd == last.Start ? selectionEnd : last.TotalEnd;
            ValidateSelection(Parse(text), start, end);

            string body = text.Substring(start, end - start);
            string title = MakeTitle(body);
            string headerText = HeaderPrefix + title + HeaderSuffix + newline;
            if (!EndsInNewline(body)) body += newline;
            string replacement = headerText + body + Footer + newline;
            string wrapped = text.Substring(0, start) + replacement + text.Substring(end);
            ValidateNewRegion(wrapped, start, start + headerText.Length,
                start + headerText.Length + body.Length);
            return new RegionEdit(wrapped,
                start, start + headerText.Length, false);
        }

        public static string Remove(string text, RegionBlock block)
        {
            if (text == null) throw new ArgumentNullException("text");
            if (block == null) throw new ArgumentNullException("block");
            // Refuse stale offsets rather than deleting a user's unrelated text.
            RegionBlock current = null;
            foreach (RegionBlock candidate in Parse(text))
            {
                if (candidate.HeaderStart == block.HeaderStart &&
                    candidate.HeaderLength == block.HeaderLength &&
                    candidate.FooterStart == block.FooterStart &&
                    candidate.FooterLength == block.FooterLength &&
                    candidate.Title == block.Title)
                {
                    current = candidate;
                    break;
                }
            }
            if (current == null)
                throw new ArgumentException("这个知识块的内容已发生变化，请重新选择后再取消折叠。", "block");
            string withoutFooter = text.Remove(current.FooterStart, current.FooterLength);
            return withoutFooter.Remove(current.HeaderStart, current.HeaderLength);
        }

        private static bool TryReadHeader(string value, out string title)
        {
            title = null;
            if (value.Length < HeaderPrefix.Length + HeaderSuffix.Length ||
                !value.StartsWith(HeaderPrefix, StringComparison.Ordinal) ||
                !value.EndsWith(HeaderSuffix, StringComparison.Ordinal)) return false;
            title = value.Substring(HeaderPrefix.Length,
                value.Length - HeaderPrefix.Length - HeaderSuffix.Length);
            return true;
        }

        private static List<TextLine> ReadLines(string text)
        {
            List<TextLine> lines = new List<TextLine>();
            int start = 0;
            int position = 0;
            while (position < text.Length)
            {
                char value = text[position];
                if (value != '\r' && value != '\n')
                {
                    position++;
                    continue;
                }
                int delimiter = value == '\r' && position + 1 < text.Length &&
                    text[position + 1] == '\n' ? 2 : 1;
                lines.Add(new TextLine { Start = start, Length = position - start,
                    DelimiterLength = delimiter });
                position += delimiter;
                start = position;
            }
            // The final empty line makes an EOF caret after a newline behave like
            // the same position in a normal text editor.
            lines.Add(new TextLine { Start = start, Length = text.Length - start,
                DelimiterLength = 0 });
            return lines;
        }

        private static int FindLine(List<TextLine> lines, int offset)
        {
            int low = 0;
            int high = lines.Count - 1;
            while (low < high)
            {
                int middle = low + (high - low + 1) / 2;
                if (lines[middle].Start <= offset) low = middle;
                else high = middle - 1;
            }
            return low;
        }

        private static string GetNewline(string text, List<TextLine> lines)
        {
            foreach (TextLine line in lines)
                if (line.DelimiterLength != 0)
                    return text.Substring(line.End, line.DelimiterLength);
            return "\r\n";
        }

        private static bool EndsInNewline(string text)
        {
            return text.Length != 0 && (text[text.Length - 1] == '\r' ||
                text[text.Length - 1] == '\n');
        }

        private static void ValidateSelection(List<RegionBlock> blocks, int start, int end)
        {
            foreach (RegionBlock block in blocks)
            {
                if (end <= block.HeaderStart || start >= block.TotalEndOffset) continue;
                if (start <= block.HeaderStart && end >= block.TotalEndOffset) continue;
                if (start >= block.ContentStart && end <= block.ContentEnd) continue;
                throw new ArgumentException("选区跨过了已有知识块的边界。请完整选择这个知识块，或只选择块内正文。", "selectionLength");
            }
        }

        private static void ValidateNewRegion(string text, int headerStart,
            int contentStart, int footerStart)
        {
            // An unmatched marker in the selected body can consume the generated
            // footer or close the generated header early. Reject that edit before
            // returning offsets which the editor would mistake for a complete block.
            foreach (RegionBlock block in Parse(text))
            {
                if (block.HeaderStart == headerStart && block.ContentStart == contentStart &&
                    block.FooterStart == footerStart &&
                    block.EndOffset == footerStart + Footer.Length) return;
            }
            throw new ArgumentException("选区中有未成对的补充知识标记，无法建立折叠块。请先补全或删除这些标记。", "selectionLength");
        }

        private static string MakeTitle(string body)
        {
            foreach (TextLine line in ReadLines(body))
            {
                string value = body.Substring(line.Start, line.Length).Trim();
                if (value.Length == 0) continue;
                StringBuilder simplified = new StringBuilder();
                bool pendingSpace = false;
                for (int i = 0; i < value.Length; i++)
                {
                    char c = value[i];
                    if (char.IsWhiteSpace(c))
                    {
                        pendingSpace = simplified.Length != 0;
                        continue;
                    }
                    if (pendingSpace) simplified.Append(' ');
                    pendingSpace = false;
                    simplified.Append(c);
                }
                string result = simplified.ToString();
                int count = 0;
                int offset = 0;
                while (offset < result.Length && count < 16)
                {
                    if (char.IsHighSurrogate(result[offset]) && offset + 1 < result.Length &&
                        char.IsLowSurrogate(result[offset + 1])) offset += 2;
                    else offset++;
                    count++;
                }
                return result.Substring(0, offset);
            }
            return "新知识";
        }
    }
}
