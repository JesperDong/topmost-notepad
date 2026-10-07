using System;
using System.Collections.Generic;
using TopNote;

internal static class TestRegions
{
    private static int checks;
    private const string H = "【补充知识：";
    private const string F = "【/补充知识】";

    private static void Equal<T>(T expected, T actual, string message)
    {
        checks++;
        if (!object.Equals(expected, actual))
            throw new Exception(message + ": expected=" + expected + ", actual=" + actual);
    }

    private static void True(bool value, string message)
    {
        Equal(true, value, message);
    }

    private static void Throws<T>(Action action, string message) where T : Exception
    {
        checks++;
        try { action(); }
        catch (T) { return; }
        throw new Exception(message + ": expected " + typeof(T).Name);
    }

    private static int Main()
    {
        try { Run(); return 0; }
        catch (Exception error)
        {
            Console.WriteLine("FAILED after " + checks + " checks: " + error.Message);
            return 1;
        }
    }

    private static void Run()
    {
        foreach (string nl in new string[] { "\n", "\r\n", "\r" })
        {
            string header = H + "标题】";
            string text = "前文" + nl + header + nl + "补充一" + nl + "补充二" + nl + F + nl + "后文";
            List<RegionBlock> blocks = KnowledgeRegions.Parse(text);
            Equal(1, blocks.Count, "parse count " + nl.Length);
            RegionBlock block = blocks[0];
            Equal("标题", block.Title, "title");
            Equal(2 + nl.Length, block.HeaderStart, "header start");
            Equal(header.Length + nl.Length, block.HeaderLength, "header length includes delimiter");
            Equal(block.HeaderStart + header.Length, block.FoldStart, "fold starts before delimiter");
            Equal(block.HeaderStart + block.HeaderLength, block.ContentStart, "body start");
            Equal(text.IndexOf(F, StringComparison.Ordinal), block.ContentEnd, "body end");
            Equal(block.ContentEnd, block.FooterStart, "footer start");
            Equal(F.Length + nl.Length, block.FooterLength, "footer length includes delimiter");
            Equal(block.FooterStart + F.Length, block.EndOffset, "end excludes footer delimiter");
            Equal(block.EndOffset + nl.Length, block.TotalEndOffset, "total end includes delimiter");
            Equal(2, block.BodyLineCount, "body line count");
            Equal("前文" + nl + "补充一" + nl + "补充二" + nl + "后文", KnowledgeRegions.Remove(text, block), "remove keeps body");

            string tail = H + "尾部】" + nl + "正文" + nl + F;
            RegionBlock tailBlock = KnowledgeRegions.Parse(tail)[0];
            Equal(tail.Length, tailBlock.EndOffset, "footer at EOF");
            Equal(tail.Length, tailBlock.TotalEndOffset, "no delimiter at EOF");
            Equal(F.Length, tailBlock.FooterLength, "footer EOF length");
            Equal("正文" + nl, KnowledgeRegions.Remove(tail, tailBlock), "remove EOF footer");

            string original = "alpha" + nl + "beta" + nl + "gamma";
            RegionEdit edit = KnowledgeRegions.Wrap(original, 2, 6 + nl.Length);
            RegionBlock wrapped = KnowledgeRegions.Parse(edit.Text)[0];
            Equal("alpha", wrapped.Title, "first body line is title");
            Equal(2, wrapped.BodyLineCount, "wrap extends to full selected lines");
            Equal("alpha" + nl + "beta" + nl, edit.Text.Substring(wrapped.ContentStart, wrapped.ContentLength), "whole line body");
            Equal(original, KnowledgeRegions.Remove(edit.Text, wrapped), "selected wrap remove roundtrip");
            Equal(false, edit.SelectionWasEmpty, "selected wrap flag");

            RegionEdit atBoundary = KnowledgeRegions.Wrap(original, 1, 4 + nl.Length);
            RegionBlock boundaryBlock = KnowledgeRegions.Parse(atBoundary.Text)[0];
            Equal(1, boundaryBlock.BodyLineCount, "next line start excluded");
            Equal(original, KnowledgeRegions.Remove(atBoundary.Text, boundaryBlock), "boundary roundtrip");

            RegionEdit empty = KnowledgeRegions.Wrap(original, original.IndexOf("beta", StringComparison.Ordinal) + 2, 0);
            RegionBlock blank = KnowledgeRegions.Parse(empty.Text)[0];
            Equal(original.IndexOf("beta", StringComparison.Ordinal), empty.HeaderStart, "empty caret inserts before current line");
            Equal("新知识", blank.Title, "blank default title");
            Equal(1, blank.BodyLineCount, "blank body has one line");
            Equal(nl, empty.Text.Substring(blank.ContentStart, blank.ContentLength), "blank content");
            Equal("alpha" + nl + nl + "beta" + nl + "gamma", KnowledgeRegions.Remove(empty.Text, blank), "empty inserted body preserved");
            Equal(true, empty.SelectionWasEmpty, "empty wrap flag");
            Equal(blank.ContentStart, empty.ContentStart, "edit caret location");
        }

        string nested = H + "外层】\n外正文\n" + H + "内层】\n内正文\n" + F + "\n外末尾\n" + F + "\n";
        List<RegionBlock> parsedNested = KnowledgeRegions.Parse(nested);
        Equal(2, parsedNested.Count, "nested count");
        Equal("外层", parsedNested[0].Title, "nested ordered outer first");
        Equal("内层", parsedNested[1].Title, "nested inner second");
        Equal(5, parsedNested[0].BodyLineCount, "outer counts all nested lines");
        Equal(1, parsedNested[1].BodyLineCount, "inner body count");
        string innerRemoved = KnowledgeRegions.Remove(nested, parsedNested[1]);
        Equal(1, KnowledgeRegions.Parse(innerRemoved).Count, "remove inner retains outer");
        Equal(1, KnowledgeRegions.Parse(KnowledgeRegions.Remove(nested, parsedNested[0])).Count, "remove outer retains inner");

        RegionEdit nestedWhole = KnowledgeRegions.Wrap(nested, 0, nested.Length);
        Equal(3, KnowledgeRegions.Parse(nestedWhole.Text).Count, "whole region nesting allowed");
        RegionBlock outer = parsedNested[0];
        RegionBlock inner = parsedNested[1];
        RegionEdit bodyNested = KnowledgeRegions.Wrap(nested, inner.ContentStart, 1);
        Equal(3, KnowledgeRegions.Parse(bodyNested.Text).Count, "within body nesting allowed");
        Throws<ArgumentException>(delegate { KnowledgeRegions.Wrap(nested, outer.HeaderStart + 1, 1); }, "header only rejects");
        Throws<ArgumentException>(delegate { KnowledgeRegions.Wrap(nested, outer.ContentStart, outer.FooterStart + 1 - outer.ContentStart); }, "crossing footer rejects");
        Throws<ArgumentException>(delegate { KnowledgeRegions.Wrap(nested, outer.HeaderStart, outer.ContentStart + 1); }, "crossing header rejects");
        Throws<ArgumentException>(delegate { KnowledgeRegions.Wrap(nested, inner.ContentStart, outer.FooterStart - inner.ContentStart); }, "partial nested block rejects");
        Throws<ArgumentException>(delegate { KnowledgeRegions.Remove("prefix\n" + nested, outer); }, "stale offset refuses deletion");
        Throws<ArgumentException>(delegate { KnowledgeRegions.Remove(nested.Replace("外层", "变化"), outer); }, "stale title refuses deletion");

        Equal(0, KnowledgeRegions.Parse(H + "孤立】\n正文").Count, "unpaired header ignored");
        Equal(0, KnowledgeRegions.Parse("正文\n" + F).Count, "unpaired footer ignored");
        Equal(0, KnowledgeRegions.Parse("prefix " + H + "非独立】\n正文\n" + F).Count, "header must start line");
        Equal(0, KnowledgeRegions.Parse(H + "非独立】 suffix\n正文\n" + F).Count, "header must end line");
        Equal(0, KnowledgeRegions.Parse(H + "标题】\n正文\n " + F).Count, "footer must be standalone");
        Equal(1, KnowledgeRegions.Parse(H + "未配对外层】\n" + H + "内层】\n正文\n" + F).Count, "paired inner survives unmatched outer");
        Equal(0, KnowledgeRegions.Parse("").Count, "empty parse");

        string loneHeader = H + "旧标题】";
        Throws<ArgumentException>(delegate { KnowledgeRegions.Wrap(loneHeader, 0, loneHeader.Length); }, "selected lone header cannot steal new footer");
        Equal(H + "旧标题】", loneHeader, "rejected header edit leaves text unchanged");
        string loneFooter = F;
        Throws<ArgumentException>(delegate { KnowledgeRegions.Wrap(loneFooter, 0, loneFooter.Length); }, "selected lone footer cannot close new header");
        Equal(F, loneFooter, "rejected footer edit leaves text unchanged");
        string headerInBody = "前正文\n" + H + "旧标题】\n后正文\n";
        Throws<ArgumentException>(delegate { KnowledgeRegions.Wrap(headerInBody, 0, headerInBody.Length); }, "unmatched header inside selected body rejects");
        Equal("前正文\n" + H + "旧标题】\n后正文\n", headerInBody, "rejected header body leaves text unchanged");
        string footerInBody = "前正文\n" + F + "\n后正文\n";
        Throws<ArgumentException>(delegate { KnowledgeRegions.Wrap(footerInBody, 0, footerInBody.Length); }, "unmatched footer inside selected body rejects");
        Equal("前正文\n" + F + "\n后正文\n", footerInBody, "rejected footer body leaves text unchanged");

        string externalHeader = H + "未配对外部】\n独立正文\n";
        RegionEdit outsideHeaderEdit = KnowledgeRegions.Wrap(externalHeader, externalHeader.IndexOf("独立正文", StringComparison.Ordinal), 4);
        List<RegionBlock> outsideHeaderBlocks = KnowledgeRegions.Parse(outsideHeaderEdit.Text);
        Equal(1, outsideHeaderBlocks.Count, "external unmatched header permits complete selected block");
        Equal(outsideHeaderEdit.HeaderStart, outsideHeaderBlocks[0].HeaderStart, "selected new header is paired");
        Equal(externalHeader, KnowledgeRegions.Remove(outsideHeaderEdit.Text, outsideHeaderBlocks[0]), "external unmatched header remains unchanged");
        RegionEdit outsideHeaderBlank = KnowledgeRegions.Wrap(externalHeader, externalHeader.IndexOf("独立正文", StringComparison.Ordinal), 0);
        Equal(outsideHeaderBlank.HeaderStart, KnowledgeRegions.Parse(outsideHeaderBlank.Text)[0].HeaderStart, "external unmatched header permits blank block");
        Equal(H + "未配对外部】\n\n独立正文\n", KnowledgeRegions.Remove(outsideHeaderBlank.Text, KnowledgeRegions.Parse(outsideHeaderBlank.Text)[0]), "blank block preserves external header and original line");

        string externalFooter = "独立正文\n" + F + "\n";
        RegionEdit outsideFooterEdit = KnowledgeRegions.Wrap(externalFooter, 0, 4);
        List<RegionBlock> outsideFooterBlocks = KnowledgeRegions.Parse(outsideFooterEdit.Text);
        Equal(1, outsideFooterBlocks.Count, "external unmatched footer permits complete selected block");
        Equal(outsideFooterEdit.HeaderStart, outsideFooterBlocks[0].HeaderStart, "footer external new header is paired");
        Equal(externalFooter, KnowledgeRegions.Remove(outsideFooterEdit.Text, outsideFooterBlocks[0]), "external unmatched footer remains unchanged");
        RegionEdit outsideFooterBlank = KnowledgeRegions.Wrap(externalFooter, 0, 0);
        Equal(outsideFooterBlank.HeaderStart, KnowledgeRegions.Parse(outsideFooterBlank.Text)[0].HeaderStart, "external unmatched footer permits blank block");
        Equal("\n" + externalFooter, KnowledgeRegions.Remove(outsideFooterBlank.Text, KnowledgeRegions.Parse(outsideFooterBlank.Text)[0]), "blank block preserves external footer and original line");

        RegionEdit beforeLoneHeader = KnowledgeRegions.Wrap(loneHeader, 2, 0);
        Equal(beforeLoneHeader.HeaderStart, KnowledgeRegions.Parse(beforeLoneHeader.Text)[0].HeaderStart, "blank before lone header has generated pair");
        Equal("\r\n" + loneHeader, KnowledgeRegions.Remove(beforeLoneHeader.Text, KnowledgeRegions.Parse(beforeLoneHeader.Text)[0]), "blank before lone header leaves original marker intact");
        RegionEdit beforeLoneFooter = KnowledgeRegions.Wrap(loneFooter, 2, 0);
        Equal(beforeLoneFooter.HeaderStart, KnowledgeRegions.Parse(beforeLoneFooter.Text)[0].HeaderStart, "blank before lone footer has generated pair");
        Equal("\r\n" + loneFooter, KnowledgeRegions.Remove(beforeLoneFooter.Text, KnowledgeRegions.Parse(beforeLoneFooter.Text)[0]), "blank before lone footer leaves original marker intact");

        RegionEdit blankDocument = KnowledgeRegions.Wrap("", 0, 0);
        Equal(H + "新知识】\r\n\r\n" + F + "\r\n", blankDocument.Text, "empty document uses CRLF");
        RegionEdit eofText = KnowledgeRegions.Wrap("正文", 0, 2);
        Equal("正文\r\n", KnowledgeRegions.Remove(eofText.Text, KnowledgeRegions.Parse(eofText.Text)[0]), "EOF gets marker separator");
        RegionEdit eofCaret = KnowledgeRegions.Wrap("正文\n", 3, 0);
        True(eofCaret.Text.StartsWith("正文\n" + H, StringComparison.Ordinal), "EOF caret after newline inserts at final line");

        string whitespaceText = "\n  标题\t   有空格 \n正文\n";
        RegionEdit whitespace = KnowledgeRegions.Wrap(whitespaceText, 0, whitespaceText.Length);
        Equal("标题 有空格", KnowledgeRegions.Parse(whitespace.Text)[0].Title, "title trims and simplifies whitespace");
        string emoji = "😀😀😀😀😀😀😀😀😀😀😀😀😀😀😀😀😀结束";
        RegionBlock emojiTitle = KnowledgeRegions.Parse(KnowledgeRegions.Wrap(emoji, 0, emoji.Length).Text)[0];
        Equal(32, emojiTitle.Title.Length, "title holds sixteen surrogate pairs");
        Equal("😀😀😀😀😀😀😀😀😀😀😀😀😀😀😀😀", emojiTitle.Title, "emoji title truncates at Unicode boundary");
        Equal("abcdefghijklmnop", KnowledgeRegions.Parse(KnowledgeRegions.Wrap("abcdefghijklmnopq", 0, 17).Text)[0].Title, "ASCII title limit");
        Equal("新知识", KnowledgeRegions.Parse(KnowledgeRegions.Wrap(" \t\n", 0, 3).Text)[0].Title, "blank selected title");
        Throws<ArgumentNullException>(delegate { KnowledgeRegions.Parse(null); }, "null parse");
        Throws<ArgumentOutOfRangeException>(delegate { KnowledgeRegions.Wrap("abc", -1, 0); }, "negative selection");
        Throws<ArgumentOutOfRangeException>(delegate { KnowledgeRegions.Wrap("abc", 1, Int32.MaxValue); }, "overflow selection");
        Throws<ArgumentOutOfRangeException>(delegate { KnowledgeRegions.Wrap("abc", 4, 0); }, "selection outside document");
        Console.WriteLine("Passed " + checks + " region checks.");
    }
}
