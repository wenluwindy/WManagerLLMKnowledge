using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using ExcelDataReader;
using ExcelNumberFormat;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.Exceptions;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace WManager.Knowledge
{
    internal static class KnowledgeDocumentReader
    {
        private const int MaximumCharacters = 2000000;
        private static OpenSettings ReadSettings => new OpenSettings { AutoSave = false, MaxCharactersInPart = MaximumCharacters * 8L };

        internal static string Read(string path, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension == ".txt" || extension == ".md")
            {
                string text = KnowledgeTextFile.Read(path);
                ct.ThrowIfCancellationRequested();
                if (text.Length > MaximumCharacters) throw TooLarge();
                return text;
            }
            if (extension == ".doc" || extension == ".ppt")
                throw new NotSupportedException("旧版 .doc/.ppt 尚不支持，请在 Office 或 LibreOffice 中另存为 .docx/.pptx 后导入。");

            var output = new DocumentText(ct);
            try
            {
                switch (extension)
                {
                    case ".docx": case ".docm": ReadWord(path, output); break;
                    case ".pptx": case ".pptm": ReadPresentation(path, output); break;
                    case ".xls": case ".xlsx": case ".xlsm": ReadSpreadsheet(path, output); break;
                    case ".pdf": ReadPdf(path, output); break;
                    default: throw new NotSupportedException("支持 TXT、Markdown、Word (.docx/.docm)、PPT (.pptx/.pptm)、Excel (.xls/.xlsx/.xlsm) 和文字型 PDF。");
                }
            }
            catch (PdfDocumentEncryptedException ex)
            { throw new InvalidDataException("PDF 受密码保护，请解除密码后重新导入：" + path, ex); }
            catch (Exception ex) when (ex is OpenXmlPackageException || ex is PdfDocumentFormatException || ex is ExcelDataReader.Exceptions.HeaderException || ex is ExcelDataReader.Exceptions.InvalidPasswordException)
            { throw new InvalidDataException("文档解析失败，请确认文件未损坏、未加密，且扩展名与实际格式一致：" + path + "。" + ex.Message, ex); }

            ct.ThrowIfCancellationRequested();
            if (!output.HasContent)
                throw new InvalidDataException(extension == ".pdf"
                    ? "PDF 中没有可提取的文字；扫描版或图片型 PDF 需要先进行 OCR，再导入。"
                    : "文档中没有可提取的文字或单元格内容；图片中的文字需要先进行 OCR。");
            return output.ToString();
        }

        private static void ReadWord(string path, DocumentText output)
        {
            using (var document = WordprocessingDocument.Open(path, false, ReadSettings))
            {
                var part = document.MainDocumentPart;
                if (part?.Document?.Body == null) throw new InvalidDataException("Word 文档缺少正文。");
                foreach (var child in part.Document.Body.ChildElements) AppendWordBlock(child, output);
                foreach (var header in part.HeaderParts)
                    if (header.Header != null) { output.Heading("Word 页眉"); AppendWordBlock(header.Header, output); }
                foreach (var footer in part.FooterParts)
                    if (footer.Footer != null) { output.Heading("Word 页脚"); AppendWordBlock(footer.Footer, output); }
                if (part.FootnotesPart?.Footnotes != null)
                    foreach (var note in part.FootnotesPart.Footnotes.Elements<W.Footnote>())
                        if (note.Type == null || note.Type.Value == W.FootnoteEndnoteValues.Normal)
                        { output.Heading("Word 脚注 " + note.Id); AppendWordBlock(note, output); }
                if (part.EndnotesPart?.Endnotes != null)
                    foreach (var note in part.EndnotesPart.Endnotes.Elements<W.Endnote>())
                        if (note.Type == null || note.Type.Value == W.FootnoteEndnoteValues.Normal)
                        { output.Heading("Word 尾注 " + note.Id); AppendWordBlock(note, output); }
            }
        }

        private static void AppendWordBlock(OpenXmlElement element, DocumentText output)
        {
            output.CheckCancellation();
            if (element is W.Paragraph paragraph)
            {
                string text = WordParagraph(paragraph);
                string style = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? "";
                bool heading = style.StartsWith("Heading", StringComparison.OrdinalIgnoreCase) || style == "Title"
                    || paragraph.ParagraphProperties?.OutlineLevel != null;
                output.Content((heading && !string.IsNullOrWhiteSpace(text) ? "# " : "") + text);
            }
            else if (element is W.Table table)
            {
                foreach (var row in table.Elements<W.TableRow>())
                    AppendTableRow(row.Elements<W.TableCell>().Select(cell =>
                        string.Join(" / ", cell.Descendants<W.Paragraph>().Select(WordParagraph).Where(text => !string.IsNullOrWhiteSpace(text)))), output);
            }
            else
                foreach (var child in element.ChildElements) AppendWordBlock(child, output);
        }

        private static string WordParagraph(W.Paragraph paragraph)
        {
            var text = new StringBuilder();
            foreach (var element in paragraph.Descendants())
            {
                if (element.Ancestors<W.DeletedRun>().Any() || element.Ancestors<W.MoveFromRun>().Any()) continue;
                if (element is W.Text run) text.Append(run.Text);
                else if (element is W.TabChar) text.Append('\t');
                else if (element is W.Break || element is W.CarriageReturn) text.Append('\n');
            }
            return text.ToString().Trim();
        }

        private static void ReadPresentation(string path, DocumentText output)
        {
            using (var document = PresentationDocument.Open(path, false, ReadSettings))
            {
                var part = document.PresentationPart;
                if (part?.Presentation?.SlideIdList == null) throw new InvalidDataException("PPT 文档缺少幻灯片列表。");
                int number = 0;
                // Follow presentation relationships, not file names or the order of SlideParts.
                foreach (var slideId in part.Presentation.SlideIdList.Elements<P.SlideId>())
                {
                    output.CheckCancellation();
                    if (slideId.RelationshipId == null || !(part.GetPartById(slideId.RelationshipId.Value) is SlidePart slide))
                        throw new InvalidDataException("PPT 幻灯片关系无效。");
                    output.Heading("幻灯片 " + (++number));
                    if (slide.Slide != null) AppendPresentationBlock(slide.Slide, output);
                    if (slide.NotesSlidePart?.NotesSlide != null)
                    {
                        foreach (var shape in slide.NotesSlidePart.NotesSlide.Descendants<P.Shape>())
                        {
                            var placeholder = shape.Descendants<P.PlaceholderShape>().FirstOrDefault();
                            if (placeholder?.Type?.Value != P.PlaceholderValues.Body) continue;
                            output.Heading("幻灯片 " + number + " 备注");
                            AppendPresentationBlock(shape, output);
                        }
                    }
                }
            }
        }

        private static void AppendPresentationBlock(OpenXmlElement element, DocumentText output)
        {
            output.CheckCancellation();
            if (element is A.Paragraph paragraph) output.Content(DrawingParagraph(paragraph));
            else if (element is A.Table table)
                foreach (var row in table.Elements<A.TableRow>())
                    AppendTableRow(row.Elements<A.TableCell>().Select(cell =>
                        string.Join(" / ", cell.Descendants<A.Paragraph>().Select(DrawingParagraph))), output);
            else
                foreach (var child in element.ChildElements) AppendPresentationBlock(child, output);
        }

        private static string DrawingParagraph(A.Paragraph paragraph)
        {
            var text = new StringBuilder();
            foreach (var element in paragraph.Descendants())
                if (element is A.Text run) text.Append(run.Text);
                else if (element is A.Break) text.Append('\n');
            return text.ToString().Trim();
        }

        private static void AppendTableRow(IEnumerable<string> cells, DocumentText output)
        {
            var values = cells.ToArray();
            if (values.Any(value => !string.IsNullOrWhiteSpace(value))) output.Content(string.Join(" | ", values));
        }

        private static void ReadSpreadsheet(string path, DocumentText output)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            using (var stream = File.OpenRead(path))
            using (var reader = ExcelReaderFactory.CreateReader(stream, new ExcelReaderConfiguration
            { FallbackEncoding = Encoding.GetEncoding(936), LeaveOpen = false }))
            {
                do
                {
                    output.CheckCancellation();
                    output.Heading("工作表 " + reader.Name);
                    while (reader.Read())
                    {
                        output.CheckCancellation();
                        var cells = new List<string>();
                        for (int column = 0; column < reader.FieldCount; column++)
                        {
                            output.CheckCancellation();
                            object value = reader.GetValue(column);
                            if (value == null) continue;
                            string text = FormatCell(value, reader.GetNumberFormatString(column)).Trim();
                            if (text.Length == 0) continue;
                            cells.Add(ColumnName(column) + (reader.Depth + 1).ToString(CultureInfo.InvariantCulture)
                                + ": " + text.Replace("\r\n", " / ").Replace('\r', ' ').Replace('\n', ' '));
                        }
                        if (cells.Count > 0) output.Content(string.Join(" | ", cells));
                    }
                } while (reader.NextResult());
            }
        }

        private static string FormatCell(object value, string format)
        {
            if (!string.IsNullOrEmpty(format) && !(value is string) && !(value is bool))
            {
                var numberFormat = new NumberFormat(format);
                if (numberFormat.IsValid) return numberFormat.Format(value, CultureInfo.InvariantCulture);
            }
            if (value is DateTime date) return date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            if (value is bool boolean) return boolean ? "TRUE" : "FALSE";
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static string ColumnName(int zeroBasedColumn)
        {
            string result = "";
            for (int column = zeroBasedColumn + 1; column > 0; column = (column - 1) / 26)
                result = (char)('A' + (column - 1) % 26) + result;
            return result;
        }

        private static void ReadPdf(string path, DocumentText output)
        {
            using (var document = PdfDocument.Open(path))
                for (int number = 1; number <= document.NumberOfPages; number++)
                {
                    output.CheckCancellation();
                    string text = ContentOrderTextExtractor.GetText(document.GetPage(number));
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    output.Heading("PDF 第 " + number + " 页");
                    output.Content(text);
                }
        }

        private static InvalidDataException TooLarge() => new InvalidDataException("提取的文档文字超过 200 万字符，请拆分文档后导入。");

        private sealed class DocumentText
        {
            private readonly StringBuilder text = new StringBuilder();
            private readonly CancellationToken ct;
            public bool HasContent { get; private set; }
            public DocumentText(CancellationToken ct) => this.ct = ct;
            public void CheckCancellation() => ct.ThrowIfCancellationRequested();
            public void Heading(string value) => Append("# " + value);
            public void Content(string value)
            {
                if (string.IsNullOrWhiteSpace(value)) return;
                Append(value.Trim());
                HasContent = true;
            }
            private void Append(string value)
            {
                CheckCancellation();
                if (text.Length + value.Length + 2 > MaximumCharacters) throw TooLarge();
                text.Append(value).Append("\n\n");
            }
            public override string ToString() => text.ToString().Trim();
        }
    }
}
