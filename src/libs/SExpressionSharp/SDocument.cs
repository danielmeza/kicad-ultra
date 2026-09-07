using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SExpressionSharp
{
    /// <summary>
    /// A whole S-expression file: every top-level form in order, plus the comments and whitespace
    /// between them.
    /// </summary>
    /// <remarks>
    /// A <c>.kicad_sch</c> holds exactly one top-level form, but a <c>.kicad_dru</c> is a sequence of
    /// them interleaved with <c>#</c> comment blocks. Parsing one with <see cref="SExpressionParser.Parse(string)"/>
    /// yields the first form and drops the rest of the file, which is why this type exists.
    /// It enumerates as the top-level forms, so <c>foreach (var rule in document)</c> reads naturally.
    /// </remarks>
    public sealed class SDocument : IReadOnlyList<SExpression>
    {
        private readonly SExpression _container;

        /// <summary>Creates an empty document.</summary>
        public SDocument() => _container = new SExpression(string.Empty);

        internal SDocument(SExpression container) => _container = container;

        internal SExpression Container => _container;

        /// <summary>Gets every top-level element in order: forms and comments.</summary>
        public SItemList Items => _container.Items;

        /// <summary>Gets the top-level forms in order.</summary>
        public SChildCollection Forms => _container.Children;

        /// <summary>Gets the first top-level form, or <see langword="null"/> for an empty document.</summary>
        public SExpression? Root => _container.GetFirstChild();

        /// <summary>Gets the top-level comments in order, without their leading <c>#</c>.</summary>
        public IEnumerable<string> Comments => _container.Comments;

        /// <summary>Gets the number of top-level forms.</summary>
        public int Count => _container.CountOf(SItemKind.Expression);

        /// <summary>Gets a top-level form by position.</summary>
        /// <param name="index">Zero-based index.</param>
        public SExpression this[int index] => _container.Children[index];

        /// <summary>Gets the first top-level form with the given token.</summary>
        /// <param name="token">The token to look for.</param>
        public SExpression? this[string token] => _container.GetChild(token);

        /// <summary>True when anything in the document has changed since it was parsed.</summary>
        public bool IsModified => _container.IsModified;

        /// <summary>Gets the text this document was parsed from, or <see langword="null"/>.</summary>
        public string? SourceText => _container.Source;

        /// <summary>Parses every top-level form in <paramref name="text"/>.</summary>
        /// <param name="text">S-expression text.</param>
        /// <returns>The document.</returns>
        public static SDocument Parse(string text) => new SExpressionParser().ParseAll(text);

        /// <summary>Parses every top-level form in a file.</summary>
        /// <param name="path">Path to the file.</param>
        /// <returns>The document.</returns>
        public static SDocument Load(string path) => new SExpressionParser().ParseAllFile(path);

        /// <summary>Parses every top-level form in a file, reading it asynchronously.</summary>
        /// <param name="path">Path to the file.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The document.</returns>
        public static Task<SDocument> LoadAsync(string path, CancellationToken cancellationToken = default) =>
            new SExpressionParser().ParseAllFileAsync(path, cancellationToken);

        /// <summary>Appends a top-level form.</summary>
        /// <param name="form">The form to append.</param>
        public void Add(SExpression form) => _container.AddChild(form);

        /// <summary>Appends a top-level comment.</summary>
        /// <param name="text">The comment text, without the leading <c>#</c>.</param>
        public void AddComment(string text) => _container.AddComment(text);

        /// <summary>Finds forms by a <c>/</c>-separated token path, starting at the top level.</summary>
        /// <param name="path">For example <c>kicad_sch/lib_symbols/symbol</c>.</param>
        /// <returns>Every matching form.</returns>
        public IEnumerable<SExpression> FindAll(string path) => _container.FindAll(path);

        /// <summary>Finds the first form matching a <c>/</c>-separated token path.</summary>
        /// <param name="path">For example <c>kicad_sch/lib_symbols/symbol</c>.</param>
        /// <returns>The first match, or <see langword="null"/>.</returns>
        public SExpression? Find(string path) => _container.Find(path);

        /// <summary>Renders the document. An unmodified parsed document comes back byte for byte.</summary>
        /// <returns>The file text.</returns>
        public string ToText() => new SExpressionWriter().Write(this);

        /// <summary>Renders the document with explicit options.</summary>
        /// <param name="options">Writer options.</param>
        /// <returns>The file text.</returns>
        public string ToText(SExpressionWriterOptions options) => new SExpressionWriter(options).Write(this);

        /// <summary>Writes the document to a file.</summary>
        /// <param name="path">Destination path.</param>
        public void Save(string path) => File.WriteAllText(path, ToText());

        /// <summary>Writes the document to a file asynchronously.</summary>
        /// <param name="path">Destination path.</param>
        /// <param name="cancellationToken">Cancels the write.</param>
        /// <returns>A task that completes when the file is written.</returns>
        public Task SaveAsync(string path, CancellationToken cancellationToken = default) =>
            File.WriteAllTextAsync(path, ToText(), cancellationToken);

        /// <inheritdoc />
        public IEnumerator<SExpression> GetEnumerator() => _container.Children.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <inheritdoc />
        public override string ToString() => $"SDocument({Count} form{(Count == 1 ? string.Empty : "s")})";
    }
}
