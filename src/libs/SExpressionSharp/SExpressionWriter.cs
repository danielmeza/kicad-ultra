using System;
using System.Buffers;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SExpressionSharp
{
    /// <summary>
    /// How <see cref="SExpressionWriter"/> lays text out.
    /// </summary>
    public enum SExpressionFormat
    {
        /// <summary>
        /// Reproduce the original text wherever it is still known, and format only what changed.
        /// This is the default and the only mode that can be byte-identical.
        /// </summary>
        Auto = 0,

        /// <summary>Same as <see cref="Auto"/>; spelled out for callers who want to be explicit.</summary>
        Preserve = 1,

        /// <summary>Always re-format from the tree, ignoring where it came from.</summary>
        Canonical = 2,
    }

    /// <summary>
    /// Knobs for <see cref="SExpressionWriter"/>. They only affect text the writer has to format
    /// itself; text it can reproduce from the source is reproduced exactly.
    /// </summary>
    public sealed class SExpressionWriterOptions
    {
        /// <summary>How much of the original layout to keep. Defaults to <see cref="SExpressionFormat.Auto"/>.</summary>
        public SExpressionFormat Format { get; set; } = SExpressionFormat.Auto;

        /// <summary>One level of indentation. Defaults to a tab, which is what KiCad writes.</summary>
        public string Indent { get; set; } = "\t";

        /// <summary>The line separator. Defaults to <c>\n</c>, which is what KiCad writes on every platform.</summary>
        public string NewLine { get; set; } = "\n";

        /// <summary>The character used to introduce a comment.</summary>
        public char CommentPrefix { get; set; } = '#';
    }

    /// <summary>
    /// Writer for S-expression text.
    /// </summary>
    /// <remarks>
    /// A tree that came from a parser and has not been touched is written back byte for byte: the
    /// writer copies the source span instead of re-formatting. Edit one atom and only that atom's
    /// text changes -- the whitespace around it, its siblings and every untouched subtree are still
    /// copied verbatim. A tree built in memory has no source, so it is formatted from scratch.
    /// </remarks>
    public sealed class SExpressionWriter
    {
        /// <summary>Characters that stop an atom being written bare.</summary>
        private static readonly SearchValues<char> MustQuote = SearchValues.Create(" \t\r\n\f\v()\"\\");

        private static readonly SearchValues<char> MustEscape = SearchValues.Create("\"\\");

        private readonly SExpressionWriterOptions _options;

        /// <summary>Creates a writer with default options.</summary>
        public SExpressionWriter()
            : this(null)
        {
        }

        /// <summary>Creates a writer with explicit options.</summary>
        /// <param name="options">Options, or <see langword="null"/> for the defaults.</param>
        public SExpressionWriter(SExpressionWriterOptions? options) => _options = options ?? new SExpressionWriterOptions();

        /// <summary>Gets the options in force.</summary>
        public SExpressionWriterOptions Options => _options;

        /// <summary>
        /// Convert an S-expression to a string.
        /// </summary>
        /// <param name="expression">The expression to convert.</param>
        /// <param name="indentLevel">The starting indentation level (0 by default).</param>
        /// <returns>The formatted S-expression string.</returns>
        public string Write(SExpression expression, int indentLevel = 0)
        {
            ArgumentNullException.ThrowIfNull(expression);
            var sb = new StringBuilder(Capacity(expression));
            var verbatim = AppendNode(expression, sb, indentLevel, leadingIndent: true);
            if (!verbatim)
            {
                AppendNewLine(sb);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Convert a whole document -- every top-level form, with the comments and whitespace between
        /// them -- to a string. An unmodified parsed document comes back byte for byte.
        /// </summary>
        /// <param name="document">The document to convert.</param>
        /// <returns>The file text.</returns>
        public string Write(SDocument document)
        {
            ArgumentNullException.ThrowIfNull(document);
            var container = document.Container;
            if (CanCopyVerbatim(container))
            {
                return container.Source!.Substring(container.SourceStart, container.SourceLength);
            }

            var sb = new StringBuilder(Capacity(container));
            if (!(CanSplice(container) && TrySplice(container, sb, 0)))
            {
                sb.Length = 0;
                AppendDocumentCanonical(container, sb);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Write an S-expression to a file, always leaving a trailing newline.
        /// </summary>
        /// <param name="expression">The expression to write.</param>
        /// <param name="filePath">The path of the file to write to.</param>
        public void WriteToFile(SExpression expression, string filePath)
        {
            var content = Write(expression);
            if (!content.EndsWith('\n'))
            {
                content += _options.NewLine;
            }

            File.WriteAllText(filePath, content);
        }

        /// <summary>Write a document to a file.</summary>
        /// <param name="document">The document to write.</param>
        /// <param name="filePath">The path of the file to write to.</param>
        public void WriteToFile(SDocument document, string filePath) => File.WriteAllText(filePath, Write(document));

        /// <summary>Write a document to a file asynchronously.</summary>
        /// <param name="document">The document to write.</param>
        /// <param name="filePath">The path of the file to write to.</param>
        /// <param name="cancellationToken">Cancels the write.</param>
        /// <returns>A task that completes when the file is written.</returns>
        public Task WriteToFileAsync(SDocument document, string filePath, CancellationToken cancellationToken = default) =>
            File.WriteAllTextAsync(filePath, Write(document), cancellationToken);

        /// <summary>Writes an expression to a <see cref="TextWriter"/>.</summary>
        /// <param name="expression">The expression to write.</param>
        /// <param name="writer">The destination.</param>
        public void WriteTo(SExpression expression, TextWriter writer)
        {
            ArgumentNullException.ThrowIfNull(writer);
            writer.Write(Write(expression));
        }

        /// <summary>Writes a document to a <see cref="TextWriter"/>.</summary>
        /// <param name="document">The document to write.</param>
        /// <param name="writer">The destination.</param>
        public void WriteTo(SDocument document, TextWriter writer)
        {
            ArgumentNullException.ThrowIfNull(writer);
            writer.Write(Write(document));
        }

        // ----------------------------------------------------------------------------- rendering

        /// <summary>Appends one node. Returns true when its text came out of the source rather than the formatter.</summary>
        private bool AppendNode(SExpression e, StringBuilder sb, int level, bool leadingIndent)
        {
            if (leadingIndent)
            {
                AppendIndent(sb, level);
            }

            if (CanCopyVerbatim(e))
            {
                sb.Append(e.Source, e.SourceStart, e.SourceLength);
                return true;
            }

            if (CanSplice(e))
            {
                var mark = sb.Length;
                if (TrySplice(e, sb, level))
                {
                    return true;
                }

                sb.Length = mark;
            }

            AppendCanonical(e, sb, level);
            return false;
        }

        private bool CanCopyVerbatim(SExpression e) =>
            _options.Format != SExpressionFormat.Canonical && e.HasSource && !e.IsModified;

        private bool CanSplice(SExpression e) =>
            _options.Format != SExpressionFormat.Canonical && e.HasSource && !e.LayoutInvalid && e.ItemsArray.Length > 0;

        /// <summary>
        /// Rebuilds a changed form out of the source it came from: every gap between two items is
        /// copied verbatim, so the only bytes that move are the ones that were edited.
        /// </summary>
        private bool TrySplice(SExpression e, StringBuilder sb, int level)
        {
            var src = e.Source!;
            var start = e.SourceStart;
            var end = start + e.SourceLength;
            var items = e.ItemsArray;

            var cursor = start;
            foreach (var item in items)
            {
                if (!item.IsFromSource || item.SourceStart < cursor || item.SourceStart + item.SourceLength > end)
                {
                    return false;
                }

                cursor = item.SourceStart + item.SourceLength;
            }

            cursor = start;
            foreach (var item in items)
            {
                sb.Append(src, cursor, item.SourceStart - cursor);
                if (item.Kind == SItemKind.Expression)
                {
                    AppendNode(item.Expression!, sb, level + 1, leadingIndent: false);
                }
                else if (item.RawValid)
                {
                    sb.Append(src, item.SourceStart, item.SourceLength);
                }
                else
                {
                    AppendLeaf(item, sb);
                }

                cursor = item.SourceStart + item.SourceLength;
            }

            sb.Append(src, cursor, end - cursor);
            return true;
        }

        private void AppendCanonical(SExpression e, StringBuilder sb, int level)
        {
            sb.Append('(').Append(e.Token);

            var multiline = false;
            foreach (var item in e.ItemsArray)
            {
                if (item.Kind != SItemKind.Atom)
                {
                    multiline = true;
                    break;
                }
            }

            foreach (var item in e.ItemsArray)
            {
                if (item.Kind == SItemKind.Atom)
                {
                    sb.Append(' ');
                    AppendLeaf(item, sb);
                }
                else if (item.Kind == SItemKind.Comment)
                {
                    AppendNewLine(sb);
                    AppendIndent(sb, level + 1);
                    AppendLeaf(item, sb);
                }
                else
                {
                    AppendNewLine(sb);
                    AppendNode(item.Expression!, sb, level + 1, leadingIndent: true);
                }
            }

            if (multiline)
            {
                AppendNewLine(sb);
                AppendIndent(sb, level);
            }

            sb.Append(')');
        }

        private void AppendDocumentCanonical(SExpression container, StringBuilder sb)
        {
            var first = true;
            foreach (var item in container.ItemsArray)
            {
                if (!first)
                {
                    AppendNewLine(sb);
                }

                first = false;

                if (item.Kind == SItemKind.Expression)
                {
                    AppendNode(item.Expression!, sb, 0, leadingIndent: false);
                }
                else
                {
                    AppendLeaf(item, sb);
                }
            }

            AppendNewLine(sb);
        }

        private void AppendLeaf(in SItem item, StringBuilder sb)
        {
            if (item.Kind == SItemKind.Comment)
            {
                sb.Append(_options.CommentPrefix).Append(item.Text);
                return;
            }

            var text = item.Text ?? string.Empty;
            if (item.QuoteStyle != SQuoteStyle.Quoted && !NeedsQuotes(text, _options.CommentPrefix))
            {
                sb.Append(text);
                return;
            }

            sb.Append('"');
            if (text.AsSpan().IndexOfAny(MustEscape) < 0)
            {
                sb.Append(text);
            }
            else
            {
                foreach (var c in text)
                {
                    if (c is '"' or '\\')
                    {
                        sb.Append('\\');
                    }

                    sb.Append(c);
                }
            }

            sb.Append('"');
        }

        private void AppendIndent(StringBuilder sb, int level)
        {
            if (level <= 0)
            {
                return;
            }

            var indent = _options.Indent;
            if (indent.Length == 1)
            {
                sb.Append(indent[0], level);
                return;
            }

            for (var i = 0; i < level; i++)
            {
                sb.Append(indent);
            }
        }

        private void AppendNewLine(StringBuilder sb)
        {
            var nl = _options.NewLine;
            if (nl.Length == 1)
            {
                sb.Append(nl[0]);
                return;
            }

            sb.Append(nl);
        }

        private static int Capacity(SExpression e) => e.HasSource ? e.SourceLength + 64 : 256;

        /// <summary>
        /// True when the atom could not be read back as written without quotes. This is the only
        /// guess the writer makes, and it is only reached for atoms that were never parsed --
        /// a parsed atom remembers whether it arrived quoted.
        /// </summary>
        private static bool NeedsQuotes(string value, char commentPrefix) =>
            value.Length == 0 || value[0] == commentPrefix || value.AsSpan().ContainsAny(MustQuote);
    }
}
