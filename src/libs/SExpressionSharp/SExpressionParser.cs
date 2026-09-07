using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SExpressionSharp
{
    /// <summary>
    /// Knobs for <see cref="SExpressionParser"/>.
    /// </summary>
    public sealed class SExpressionParserOptions
    {
        /// <summary>
        /// The character that starts a line comment, or <see langword="null"/> to disable comments.
        /// </summary>
        /// <remarks>
        /// KiCad's own writers never emit a comment, but a hand-written <c>.kicad_dru</c> is mostly
        /// comments and they carry the justification for every rule. Measured over a 40-file KiCad
        /// 10.0.6 corpus (schematics, boards, symbol library, worksheet): zero bare atoms begin with
        /// <c>#</c>, so treating it as a comment is safe. Set this to <see langword="null"/> for a
        /// dialect where it is not.
        /// </remarks>
        public char? CommentPrefix { get; set; } = '#';

        /// <summary>Maximum nesting depth before the parser gives up. Guards against hostile input.</summary>
        public int MaxDepth { get; set; } = 256;

        /// <summary>
        /// Keep a reference to the source text so an unmodified tree can be written back byte for
        /// byte. Turn off to drop the reference when only the values matter.
        /// </summary>
        public bool TrackSource { get; set; } = true;

        /// <summary>De-duplicate short atoms and tokens while parsing. Roughly halves allocations on KiCad files.</summary>
        public bool PoolStrings { get; set; } = true;
    }

    /// <summary>
    /// Parser for S-expression text. Instances are cheap but not thread-safe; give each thread its own.
    /// </summary>
    public sealed class SExpressionParser
    {
        private const int PoolMaxLength = 32;
        private const int CacheSize = 4096;

        /// <summary>Everything that ends a bare atom. Vectorised by <see cref="SearchValues"/>.</summary>
        private static readonly SearchValues<char> Delimiters = SearchValues.Create(" \t\r\n()");

        private static readonly SearchValues<char> Whitespace = SearchValues.Create(" \t\r\n");

        private static readonly SearchValues<char> QuoteOrEscape = SearchValues.Create("\"\\");

        private static readonly SearchValues<char> LineBreak = SearchValues.Create("\r\n");

        private readonly SExpressionParserOptions _options;

        // A fixed-size, collision-tolerant string cache. KiCad text repeats itself hard -- tokens,
        // "yes"/"no", layer names, and even coordinates -- so this removes most atom allocations for
        // one array probe and one comparison, which a Dictionary lookup could not match.
        private string[]? _cache;

        // One growable stack for the whole parse. A form's items are pushed here and copied into an
        // exactly-sized array when the form closes, so every node owns one array and no slack.
        private SItem[] _scratch = new SItem[512];
        private int _scratchTop;

        /// <summary>Creates a parser with default options.</summary>
        public SExpressionParser()
            : this(null)
        {
        }

        /// <summary>Creates a parser with explicit options.</summary>
        /// <param name="options">Options, or <see langword="null"/> for the defaults.</param>
        public SExpressionParser(SExpressionParserOptions? options) => _options = options ?? new SExpressionParserOptions();

        /// <summary>Gets the options in force.</summary>
        public SExpressionParserOptions Options => _options;

        /// <summary>
        /// Parse a string containing S-expressions and return the root expression.
        /// </summary>
        /// <param name="text">The text to parse.</param>
        /// <returns>The first top-level expression.</returns>
        /// <remarks>
        /// A file may hold more than one top-level form -- a <c>.kicad_dru</c> always does. Use
        /// <see cref="ParseAll(string)"/> to get all of them; this returns only the first.
        /// </remarks>
        public SExpression Parse(string text) =>
            ParseAll(text).Root ?? throw new SExpressionFormatException("Expected '(' at the start of S-expression");

        /// <summary>
        /// Parse a file containing S-expressions and return the root expression.
        /// </summary>
        /// <param name="filePath">Path to the file to parse.</param>
        /// <returns>The first top-level expression.</returns>
        public SExpression ParseFile(string filePath) => Parse(File.ReadAllText(filePath));

        /// <summary>
        /// Parses every top-level form in <paramref name="text"/>, keeping the comments and the
        /// whitespace between them.
        /// </summary>
        /// <param name="text">The text to parse.</param>
        /// <returns>The document.</returns>
        public SDocument ParseAll(string text)
        {
            ArgumentNullException.ThrowIfNull(text);
            ResetPool();

            var container = new SExpression(string.Empty);
            container.MarkAsDocumentContainer();

            var source = _options.TrackSource && text.Length <= SItem.MaxSourceLength ? text : null;
            _scratchTop = 0;
            var pos = 0;
            var start = _scratchTop;
            ReadItems(text, ref pos, 0, topLevel: true);
            if (pos < text.Length)
            {
                throw new SExpressionFormatException("Unbalanced ')'", text, pos);
            }

            container.SetParsed(Harvest(start, container), source, 0, text.Length);
            return new SDocument(container);
        }

        /// <summary>Parses every top-level form in a file.</summary>
        /// <param name="filePath">Path to the file to parse.</param>
        /// <returns>The document.</returns>
        public SDocument ParseAllFile(string filePath) => ParseAll(File.ReadAllText(filePath));

        /// <summary>Parses every top-level form in a file, reading it asynchronously.</summary>
        /// <param name="filePath">Path to the file to parse.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The document.</returns>
        /// <remarks>
        /// The I/O is async; the parse itself is not incremental on purpose. The tree is several
        /// times the size of the text it came from, so streaming saves nothing, and holding the
        /// source is what lets an unmodified document be written back byte for byte.
        /// </remarks>
        public async Task<SDocument> ParseAllFileAsync(string filePath, CancellationToken cancellationToken = default) =>
            ParseAll(await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false));

        /// <summary>Parses every top-level form read from <paramref name="reader"/>.</summary>
        /// <param name="reader">The reader to drain.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The document.</returns>
        public async Task<SDocument> ParseAllAsync(TextReader reader, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(reader);
            return ParseAll(await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false));
        }

        // -------------------------------------------------------------------------------- scanner

        private void ReadItems(string src, ref int pos, int depth, bool topLevel)
        {
            var s = src.AsSpan();
            var hasComments = _options.CommentPrefix.HasValue;
            var commentChar = _options.CommentPrefix ?? '\0';

            while (true)
            {
                SkipWhitespace(s, ref pos);
                if (pos >= s.Length)
                {
                    if (topLevel)
                    {
                        return;
                    }

                    throw new SExpressionFormatException("Unexpected end of input; expected ')'", src, pos);
                }

                var c = s[pos];
                if (c == ')')
                {
                    return;
                }

                if (c == '(')
                {
                    var formStart = pos;
                    var child = ReadForm(src, ref pos, depth + 1);
                    Push(SItem.ParsedExpression(child, formStart, pos - formStart));
                    continue;
                }

                if (hasComments && c == commentChar)
                {
                    var commentStart = pos;
                    var lineEnd = s[pos..].IndexOfAny(LineBreak);
                    var end = lineEnd < 0 ? s.Length : pos + lineEnd;
                    Push(SItem.ParsedComment(s.Slice(commentStart + 1, end - commentStart - 1).ToString(), commentStart, end - commentStart));
                    pos = end;
                    continue;
                }

                var atomStart = pos;
                if (c == '"')
                {
                    Push(SItem.ParsedAtom(ReadQuoted(src, ref pos), SQuoteStyle.Quoted, atomStart, pos - atomStart));
                }
                else
                {
                    Push(SItem.ParsedAtom(ReadBare(src, ref pos), SQuoteStyle.Bare, atomStart, pos - atomStart));
                }
            }
        }

        private SExpression ReadForm(string src, ref int pos, int depth)
        {
            if (depth > _options.MaxDepth)
            {
                throw new SExpressionFormatException($"Nesting deeper than {_options.MaxDepth}", src, pos);
            }

            var s = src.AsSpan();
            var start = pos;
            pos++;
            SkipWhitespace(s, ref pos);

            var expression = new SExpression(ReadBare(src, ref pos));
            var scratchBase = _scratchTop;
            ReadItems(src, ref pos, depth, topLevel: false);

            if (pos >= s.Length || s[pos] != ')')
            {
                throw new SExpressionFormatException("Expected ')'", src, pos);
            }

            pos++;
            expression.SetParsed(Harvest(scratchBase, expression), _options.TrackSource && src.Length <= SItem.MaxSourceLength ? src : null, start, pos - start);
            return expression;
        }

        private string ReadBare(string src, ref int pos)
        {
            var s = src.AsSpan();
            var start = pos;
            var rel = s[pos..].IndexOfAny(Delimiters);
            pos = rel < 0 ? s.Length : pos + rel;
            return Intern(s.Slice(start, pos - start));
        }

        private string ReadQuoted(string src, ref int pos)
        {
            var s = src.AsSpan();
            var contentStart = pos + 1;
            var rel = s[contentStart..].IndexOfAny(QuoteOrEscape);
            if (rel < 0)
            {
                throw new SExpressionFormatException("Unterminated quoted string", src, pos);
            }

            var i = contentStart + rel;
            if (s[i] == '"')
            {
                var text = Intern(s.Slice(contentStart, i - contentStart));
                pos = i + 1;
                return text;
            }

            return ReadQuotedEscaped(src, ref pos);
        }

        private static string ReadQuotedEscaped(string src, ref int pos)
        {
            var s = src.AsSpan();
            var start = pos;
            var i = pos + 1;
            var sb = new StringBuilder(32);

            while (i < s.Length)
            {
                var c = s[i];
                if (c == '"')
                {
                    pos = i + 1;
                    return sb.ToString();
                }

                if (c == '\\' && i + 1 < s.Length)
                {
                    var next = s[i + 1];

                    // Only the two escapes KiCad actually emits are decoded, so encoding them again
                    // is an exact inverse. Anything else keeps its backslash and stays as written.
                    if (next is '\\' or '"')
                    {
                        sb.Append(next);
                        i += 2;
                        continue;
                    }

                    sb.Append(c).Append(next);
                    i += 2;
                    continue;
                }

                sb.Append(c);
                i++;
            }

            throw new SExpressionFormatException("Unterminated quoted string", src, start);
        }

        private void Push(in SItem item)
        {
            if (_scratchTop == _scratch.Length)
            {
                Array.Resize(ref _scratch, _scratch.Length * 2);
            }

            _scratch[_scratchTop++] = item;
        }

        /// <summary>
        /// Copies everything pushed since <paramref name="scratchBase"/> into an exactly-sized array,
        /// parenting nested forms in the same pass, and pops the scratch stack back.
        /// </summary>
        private SItem[] Harvest(int scratchBase, SExpression owner)
        {
            var count = _scratchTop - scratchBase;
            if (count == 0)
            {
                return Array.Empty<SItem>();
            }

            var items = new SItem[count];
            var scratch = _scratch;
            for (var i = 0; i < count; i++)
            {
                var item = scratch[scratchBase + i];
                item.Expression?.SetParsedParent(owner);
                items[i] = item;
            }

            _scratchTop = scratchBase;
            return items;
        }

        private static void SkipWhitespace(ReadOnlySpan<char> s, ref int pos)
        {
            if (pos >= s.Length)
            {
                return;
            }

            var rel = s[pos..].IndexOfAnyExcept(Whitespace);
            pos = rel < 0 ? s.Length : pos + rel;
        }

        // ----------------------------------------------------------------------------- string pool

        private void ResetPool()
        {
            if (!_options.PoolStrings)
            {
                _cache = null;
                return;
            }

            // Kept between parses on the same instance: a stale entry is still a valid string, and
            // the comparison below is what decides a hit, so there is nothing to invalidate.
            _cache ??= new string[CacheSize];
        }

        private string Intern(ReadOnlySpan<char> span)
        {
            if (span.IsEmpty)
            {
                return string.Empty;
            }

            var cache = _cache;
            if (cache is null || span.Length > PoolMaxLength)
            {
                return span.ToString();
            }

            var slot = (int)(Hash(span) & (CacheSize - 1));
            var hit = cache[slot];
            if (hit is not null && span.SequenceEqual(hit))
            {
                return hit;
            }

            var text = span.ToString();
            cache[slot] = text;
            return text;
        }

        /// <summary>
        /// Length plus three sampled characters. A weak hash is fine here: a collision only costs one
        /// failed comparison and one extra string, never a wrong answer, and this runs once per atom.
        /// </summary>
        private static uint Hash(ReadOnlySpan<char> span)
        {
            var h = ((uint)span.Length * 2654435761u) ^ span[0];
            h = (h << 5) ^ span[^1];
            if (span.Length > 2)
            {
                h ^= (uint)span[span.Length >> 1] << 11;
            }

            return h ^ (h >> 13);
        }
    }
}
