using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SExpressionSharp
{
    /// <summary>
    /// A node in an S-expression tree: a token followed by an ordered sequence of atoms, nested
    /// forms and comments.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The model is deliberately schema-free. Nothing here knows what a KiCad token means, which is
    /// why a file written by a newer KiCad still round-trips through it unchanged.
    /// </para>
    /// <para>
    /// <see cref="Items"/> is the single source of truth and holds everything in source order;
    /// <see cref="Values"/> and <see cref="Children"/> are live views over it, so a form written as
    /// <c>(a 1 (b) 2)</c> is written back as <c>(a 1 (b) 2)</c> and not as <c>(a 1 2 (b))</c>.
    /// </para>
    /// </remarks>
    public sealed class SExpression
    {
        [Flags]
        private enum Flag : byte
        {
            None = 0,
            Dirty = 1,
            LayoutInvalid = 2,
            DocumentContainer = 4,
        }

        private SItem[] _items = Array.Empty<SItem>();
        private string _token;
        private SExpression? _parent;
        private Flag _flags;

        internal string? Source;
        internal int SourceStart = -1;
        internal int SourceLength;

        /// <summary>
        /// Creates a new S-expression with the specified token name.
        /// </summary>
        /// <param name="token">The token or name of this expression.</param>
        public SExpression(string token)
        {
            ArgumentNullException.ThrowIfNull(token);
            _token = token;
        }

        /// <summary>
        /// Creates a new S-expression with the specified token name and values.
        /// </summary>
        /// <param name="token">The token or name of this expression.</param>
        /// <param name="values">Initial values for this expression.</param>
        public SExpression(string token, params string[] values)
            : this(token)
        {
            ArgumentNullException.ThrowIfNull(values);
            if (values.Length == 0)
            {
                return;
            }

            _items = new SItem[values.Length];
            for (var i = 0; i < values.Length; i++)
            {
                _items[i] = SItem.CreateAtom(values[i]);
            }
        }

        /// <summary>
        /// Gets or sets the token or name of this S-expression.
        /// </summary>
        public string Token
        {
            get => _token;
            set
            {
                ArgumentNullException.ThrowIfNull(value);
                if (string.Equals(_token, value, StringComparison.Ordinal))
                {
                    return;
                }

                _token = value;
                _flags |= Flag.LayoutInvalid;
                MarkDirty();
            }
        }

        /// <summary>
        /// Gets the form that contains this one, or <see langword="null"/> for a root.
        /// </summary>
        public SExpression? Parent => _parent is null || _parent.IsDocumentContainer ? null : _parent;

        /// <summary>
        /// Gets every element of this form in source order: atoms, nested forms and comments.
        /// </summary>
        public SItemList Items => new(this);

        /// <summary>
        /// Gets the atoms of this expression as a live, mutable list of strings.
        /// </summary>
        /// <remarks>
        /// Adding through this view inserts after the last existing atom, which keeps the familiar
        /// "values, then children" shape for forms that have it.
        /// </remarks>
        public SValueCollection Values => new(this);

        /// <summary>
        /// Gets the nested forms of this expression as a live, mutable list.
        /// </summary>
        public SChildCollection Children => new(this);

        /// <summary>
        /// Gets the comments attached inside this form, in order, without their leading <c>#</c>.
        /// </summary>
        public IEnumerable<string> Comments
        {
            get
            {
                foreach (var item in _items)
                {
                    if (item.Kind == SItemKind.Comment)
                    {
                        yield return item.Text ?? string.Empty;
                    }
                }
            }
        }

        /// <summary>
        /// True when this form (or anything under it) has been changed since it was parsed, or when
        /// it was never parsed at all. A writer reproduces an unmodified form byte for byte.
        /// </summary>
        public bool IsModified => (_flags & Flag.Dirty) != 0 || Source is null;

        /// <summary>True when this form was produced by a parser and still knows its source text.</summary>
        public bool HasSource => Source is not null && SourceStart >= 0;

        /// <summary>
        /// Gets the exact text this form was parsed from, or an empty span when it was not parsed.
        /// </summary>
        public ReadOnlySpan<char> SourceSpan => Source is null || SourceStart < 0 ? default : Source.AsSpan(SourceStart, SourceLength);

        internal SItem[] ItemsArray => _items;

        internal bool LayoutInvalid => (_flags & Flag.LayoutInvalid) != 0;

        internal bool IsDocumentContainer => (_flags & Flag.DocumentContainer) != 0;

        internal void MarkAsDocumentContainer() => _flags |= Flag.DocumentContainer;

        // ------------------------------------------------------------------- parsing entry points

        /// <summary>Parses the first top-level form in <paramref name="text"/>.</summary>
        /// <param name="text">S-expression text.</param>
        /// <returns>The first top-level form.</returns>
        public static SExpression Parse(string text) => new SExpressionParser().Parse(text);

        /// <summary>Parses the first top-level form in a file.</summary>
        /// <param name="path">Path to the file.</param>
        /// <returns>The first top-level form.</returns>
        public static SExpression Load(string path) => new SExpressionParser().ParseFile(path);

        // ----------------------------------------------------------------------------- navigation

        /// <summary>
        /// Gets the first child form with the given token, or <see langword="null"/>.
        /// </summary>
        /// <param name="token">The token to look for.</param>
        public SExpression? this[string token] => GetChild(token);

        /// <summary>
        /// Gets the first child S-expression with the specified token.
        /// </summary>
        /// <param name="token">The token to search for.</param>
        /// <returns>The first child matching the token, or null if not found.</returns>
        public SExpression? GetChild(string token)
        {
            foreach (var item in _items)
            {
                if (item.Kind == SItemKind.Expression && string.Equals(item.Expression!.Token, token, StringComparison.Ordinal))
                {
                    return item.Expression;
                }
            }

            return null;
        }

        /// <summary>
        /// Gets all child S-expressions with the specified token.
        /// </summary>
        /// <param name="token">The token to search for.</param>
        /// <returns>An enumerable of matching child expressions.</returns>
        public IEnumerable<SExpression> GetChildren(string token)
        {
            foreach (var item in _items)
            {
                if (item.Kind == SItemKind.Expression && string.Equals(item.Expression!.Token, token, StringComparison.Ordinal))
                {
                    yield return item.Expression;
                }
            }
        }

        /// <summary>
        /// Walks a <c>/</c>-separated token path from this form and returns the first match.
        /// <c>*</c> matches any token.
        /// </summary>
        /// <param name="path">For example <c>lib_symbols/symbol/property</c>.</param>
        /// <returns>The first matching form, or <see langword="null"/>.</returns>
        public SExpression? Find(string path) => FindAll(path).FirstOrDefault();

        /// <summary>
        /// Walks a <c>/</c>-separated token path from this form and returns every match, in document order.
        /// <c>*</c> matches any token.
        /// </summary>
        /// <param name="path">For example <c>lib_symbols/symbol/property</c>.</param>
        /// <returns>Every matching form.</returns>
        public IEnumerable<SExpression> FindAll(string path)
        {
            ArgumentNullException.ThrowIfNull(path);
            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                return Enumerable.Empty<SExpression>();
            }

            IEnumerable<SExpression> current = new[] { this };
            foreach (var segment in segments)
            {
                var seg = segment;
                current = current.SelectMany(e => seg == "*" ? e.Children.AsEnumerable() : e.GetChildren(seg));
            }

            return current;
        }

        /// <summary>
        /// Enumerates every form below this one, depth first, in document order.
        /// </summary>
        /// <param name="token">When given, only forms with this token are returned.</param>
        /// <returns>The matching descendants.</returns>
        public IEnumerable<SExpression> Descendants(string? token = null)
        {
            foreach (var item in _items)
            {
                if (item.Kind != SItemKind.Expression)
                {
                    continue;
                }

                var child = item.Expression!;
                if (token is null || string.Equals(child.Token, token, StringComparison.Ordinal))
                {
                    yield return child;
                }

                foreach (var d in child.Descendants(token))
                {
                    yield return d;
                }
            }
        }

        // -------------------------------------------------------------------------- reading values

        /// <summary>
        /// Gets the value at the specified index.
        /// </summary>
        /// <param name="index">The index of the value to get.</param>
        /// <returns>The value at the specified index, or null if the index is out of range.</returns>
        public string? GetValue(int index)
        {
            if (index < 0)
            {
                return null;
            }

            var seen = 0;
            foreach (var item in _items)
            {
                if (item.Kind == SItemKind.Atom && seen++ == index)
                {
                    return item.Text;
                }
            }

            return null;
        }

        /// <summary>
        /// Gets the first value as a string.
        /// </summary>
        /// <returns>The first value, or null if there are no values.</returns>
        public string? GetValueAsString() => GetValue(0);

        /// <summary>
        /// Gets the value at the specified index as a double, using the invariant culture.
        /// </summary>
        /// <param name="index">The index of the value to get.</param>
        /// <returns>The value as a double, or 0 if the value cannot be parsed.</returns>
        public double GetValueAsDouble(int index = 0) =>
            double.TryParse(GetValue(index), NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : 0;

        /// <summary>
        /// Gets the value at the specified index as an integer, using the invariant culture.
        /// </summary>
        /// <param name="index">The index of the value to get.</param>
        /// <returns>The value as an integer, or 0 if the value cannot be parsed.</returns>
        public int GetValueAsInt(int index = 0) =>
            int.TryParse(GetValue(index), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : 0;

        /// <summary>
        /// Gets the value at the specified index as a boolean.
        /// </summary>
        /// <param name="index">The index of the value to get.</param>
        /// <returns>The value as a boolean, or false if the value cannot be parsed.</returns>
        public bool GetValueAsBool(int index = 0)
        {
            var value = GetValue(index);
            return value is not null
                && (string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(value, "1", StringComparison.Ordinal));
        }

        /// <summary>
        /// Tries to read the value at <paramref name="index"/> as <typeparamref name="T"/>, invariant culture.
        /// </summary>
        /// <typeparam name="T">Any parsable type, e.g. <see cref="double"/> or <see cref="int"/>.</typeparam>
        /// <param name="index">The value index.</param>
        /// <param name="value">The parsed value.</param>
        /// <returns>True when the value exists and parsed.</returns>
        public bool TryGetValue<T>(int index, out T value)
            where T : IParsable<T>
        {
            var raw = GetValue(index);
            if (raw is not null && T.TryParse(raw, CultureInfo.InvariantCulture, out var parsed))
            {
                value = parsed;
                return true;
            }

            value = default!;
            return false;
        }

        /// <summary>
        /// Reads the value at <paramref name="index"/> as <typeparamref name="T"/>, or returns <paramref name="fallback"/>.
        /// </summary>
        /// <typeparam name="T">Any parsable type.</typeparam>
        /// <param name="index">The value index.</param>
        /// <param name="fallback">Returned when the value is missing or unparsable.</param>
        /// <returns>The parsed value or the fallback.</returns>
        public T GetValue<T>(int index, T fallback)
            where T : IParsable<T> => TryGetValue<T>(index, out var v) ? v : fallback;

        /// <summary>
        /// Reads the value at <paramref name="index"/> as <typeparamref name="T"/> or throws.
        /// </summary>
        /// <typeparam name="T">Any parsable type.</typeparam>
        /// <param name="index">The value index.</param>
        /// <returns>The parsed value.</returns>
        /// <exception cref="FormatException">The value is missing or does not parse.</exception>
        public T GetRequiredValue<T>(int index = 0)
            where T : IParsable<T> => TryGetValue<T>(index, out var v)
                ? v
                : throw new SExpressionFormatException($"({_token}) has no value at index {index} parsable as {typeof(T).Name}.");

        /// <summary>
        /// Reads a value out of a child form in one call: <c>node.GetChildValue("uuid")</c>.
        /// </summary>
        /// <param name="token">The child token.</param>
        /// <param name="index">The value index inside the child.</param>
        /// <returns>The value, or <see langword="null"/> when the child or value is missing.</returns>
        public string? GetChildValue(string token, int index = 0) => GetChild(token)?.GetValue(index);

        // -------------------------------------------------------------------------- writing values

        /// <summary>
        /// Replaces the value at <paramref name="index"/>, appending atoms when the index is past the end.
        /// </summary>
        /// <param name="index">The value index.</param>
        /// <param name="value">The new text.</param>
        /// <param name="quote">How to write it; <see cref="SQuoteStyle.Auto"/> keeps the style the atom already had.</param>
        /// <returns>This expression, for chaining.</returns>
        public SExpression SetValue(int index, string value, SQuoteStyle quote = SQuoteStyle.Auto)
        {
            ArgumentNullException.ThrowIfNull(value);
            ArgumentOutOfRangeException.ThrowIfNegative(index);

            var seen = 0;
            for (var i = 0; i < _items.Length; i++)
            {
                if (_items[i].Kind != SItemKind.Atom)
                {
                    continue;
                }

                if (seen++ == index)
                {
                    var style = quote == SQuoteStyle.Auto ? _items[i].QuoteStyle : quote;
                    ReplaceItem(i, SItem.CreateAtom(value, style));
                    return this;
                }
            }

            for (var i = seen; i < index; i++)
            {
                AppendValue(string.Empty, SQuoteStyle.Auto);
            }

            AppendValue(value, quote);
            return this;
        }

        /// <summary>
        /// Sets a value on a child form, creating the child when it is missing.
        /// </summary>
        /// <param name="token">The child token.</param>
        /// <param name="value">The new text.</param>
        /// <param name="quote">How to write it.</param>
        /// <returns>The child form.</returns>
        public SExpression SetChildValue(string token, string value, SQuoteStyle quote = SQuoteStyle.Auto)
        {
            var child = GetChild(token);
            if (child is null)
            {
                child = new SExpression(token);
                AddChild(child);
            }

            child.SetValue(0, value, quote);
            return child;
        }

        /// <summary>
        /// Writes a formattable value at <paramref name="index"/> using the invariant culture.
        /// </summary>
        /// <typeparam name="T">Any formattable type, e.g. <see cref="double"/>.</typeparam>
        /// <param name="index">The value index.</param>
        /// <param name="value">The value to write.</param>
        /// <param name="format">An optional format string.</param>
        /// <returns>This expression, for chaining.</returns>
        public SExpression SetValue<T>(int index, T value, string? format = null)
            where T : IFormattable
        {
            ArgumentNullException.ThrowIfNull(value);
            return SetValue(index, value.ToString(format, CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Appends one atom to this form, after the last atom it already has.
        /// </summary>
        /// <param name="value">The atom text.</param>
        /// <param name="quote">How to write it.</param>
        /// <returns>This expression, for chaining.</returns>
        public SExpression AddValue(string value, SQuoteStyle quote = SQuoteStyle.Auto)
        {
            AppendValue(value, quote);
            return this;
        }

        /// <summary>
        /// Appends several atoms to this form.
        /// </summary>
        /// <param name="values">The atom texts.</param>
        /// <returns>This expression, for chaining.</returns>
        public SExpression AddValues(params string[] values)
        {
            ArgumentNullException.ThrowIfNull(values);
            foreach (var v in values)
            {
                AppendValue(v, SQuoteStyle.Auto);
            }

            return this;
        }

        /// <summary>
        /// Appends a line comment inside this form.
        /// </summary>
        /// <param name="text">The comment text, without the leading <c>#</c>.</param>
        /// <returns>This expression, for chaining.</returns>
        public SExpression AddComment(string text)
        {
            InsertItem(_items.Length, SItem.CreateComment(text));
            return this;
        }

        /// <summary>
        /// Adds a child S-expression to this expression.
        /// </summary>
        /// <param name="child">The child expression to add.</param>
        public void AddChild(SExpression child)
        {
            ArgumentNullException.ThrowIfNull(child);
            InsertItem(_items.Length, SItem.CreateExpression(child));
        }

        /// <summary>
        /// Creates and adds a new child S-expression with the specified token and values.
        /// </summary>
        /// <param name="token">The token for the new child expression.</param>
        /// <param name="values">Values for the new child expression.</param>
        /// <returns>The newly created child expression.</returns>
        public SExpression CreateChild(string token, params string[] values)
        {
            var child = new SExpression(token, values);
            AddChild(child);
            return child;
        }

        /// <summary>
        /// Removes the first child form with the given token.
        /// </summary>
        /// <param name="token">The token to remove.</param>
        /// <returns>True when a child was removed.</returns>
        public bool RemoveChild(string token)
        {
            for (var i = 0; i < _items.Length; i++)
            {
                if (_items[i].Kind == SItemKind.Expression && string.Equals(_items[i].Expression!.Token, token, StringComparison.Ordinal))
                {
                    RemoveItemAt(i);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Removes every child form with the given token.
        /// </summary>
        /// <param name="token">The token to remove.</param>
        /// <returns>How many were removed.</returns>
        public int RemoveChildren(string token)
        {
            var removed = 0;
            for (var i = _items.Length - 1; i >= 0; i--)
            {
                if (_items[i].Kind == SItemKind.Expression && string.Equals(_items[i].Expression!.Token, token, StringComparison.Ordinal))
                {
                    RemoveItemAt(i);
                    removed++;
                }
            }

            return removed;
        }

        /// <summary>
        /// Deep-copies this form. The copy keeps its link to the original source text, so an
        /// untouched copy still writes out byte for byte.
        /// </summary>
        /// <returns>The copy, with no parent.</returns>
        public SExpression Clone()
        {
            var copy = new SExpression(_token)
            {
                Source = Source,
                SourceStart = SourceStart,
                SourceLength = SourceLength,
                _flags = _flags & ~Flag.DocumentContainer,
                _items = _items.Length == 0 ? Array.Empty<SItem>() : new SItem[_items.Length],
            };

            for (var i = 0; i < _items.Length; i++)
            {
                var item = _items[i];
                if (item.Kind == SItemKind.Expression)
                {
                    var childCopy = item.Expression!.Clone();
                    childCopy._parent = copy;
                    copy._items[i] = SItem.ParsedExpression(childCopy, item.SourceStart, item.SourceLength);
                }
                else
                {
                    copy._items[i] = item;
                }
            }

            if (IsDocumentContainer)
            {
                copy._flags |= Flag.DocumentContainer;
            }

            return copy;
        }

        /// <summary>
        /// Renders this form to text. A parsed, unmodified form comes back exactly as it was written.
        /// </summary>
        /// <returns>The S-expression text.</returns>
        public string ToText() => new SExpressionWriter().Write(this);

        /// <summary>
        /// Renders this form to text with explicit options.
        /// </summary>
        /// <param name="options">Writer options.</param>
        /// <returns>The S-expression text.</returns>
        public string ToText(SExpressionWriterOptions options) => new SExpressionWriter(options).Write(this);

        /// <summary>
        /// Returns a short debugging representation of this S-expression.
        /// </summary>
        /// <returns>The token and its values.</returns>
        public override string ToString() => $"({_token} {string.Join(" ", Values)})";

        // ------------------------------------------------------------------------- item plumbing

        internal SExpression? GetFirstChild()
        {
            foreach (var item in _items)
            {
                if (item.Kind == SItemKind.Expression)
                {
                    return item.Expression;
                }
            }

            return null;
        }

        internal int CountOf(SItemKind kind)
        {
            var n = 0;
            foreach (var item in _items)
            {
                if (item.Kind == kind)
                {
                    n++;
                }
            }

            return n;
        }

        internal void AppendValue(string value, SQuoteStyle quote)
        {
            ArgumentNullException.ThrowIfNull(value);
            var at = 0;
            for (var i = 0; i < _items.Length; i++)
            {
                if (_items[i].Kind == SItemKind.Atom)
                {
                    at = i + 1;
                }
            }

            InsertItem(at, SItem.CreateAtom(value, quote));
        }

        internal void InsertItem(int index, SItem item)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(index, _items.Length);
            Attach(item);

            var next = new SItem[_items.Length + 1];
            Array.Copy(_items, 0, next, 0, index);
            next[index] = item;
            Array.Copy(_items, index, next, index + 1, _items.Length - index);
            _items = next;

            _flags |= Flag.LayoutInvalid;
            MarkDirty();
        }

        internal void RemoveItemAt(int index)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _items.Length);
            Detach(_items[index]);

            var next = _items.Length == 1 ? Array.Empty<SItem>() : new SItem[_items.Length - 1];
            Array.Copy(_items, 0, next, 0, index);
            Array.Copy(_items, index + 1, next, index, _items.Length - index - 1);
            _items = next;

            _flags |= Flag.LayoutInvalid;
            MarkDirty();
        }

        internal void ReplaceItem(int index, SItem item)
        {
            var old = _items[index];
            if (old.RawValid && old == item)
            {
                return;
            }

            Detach(old);
            Attach(item);

            // Keep the old item's slot so the writer can still splice the whitespace around it and
            // change nothing but this one atom.
            _items[index] = old.IsFromSource ? item.InSlotOf(old) : item;
            MarkDirty();
        }

        internal void ClearItems()
        {
            foreach (var item in _items)
            {
                Detach(item);
            }

            _items = Array.Empty<SItem>();
            _flags |= Flag.LayoutInvalid;
            MarkDirty();
        }

        /// <summary>Installs the items and source span the parser produced. No dirty marking.</summary>
        internal void SetParsed(SItem[] items, string? source, int start, int length)
        {
            _items = items;
            Source = source;
            SourceStart = start;
            SourceLength = length;
        }

        /// <summary>Parent link set by the parser as it harvests a form's items.</summary>
        internal void SetParsedParent(SExpression parent) => _parent = parent;

        private void Attach(SItem item)
        {
            if (item.Kind == SItemKind.Expression)
            {
                var child = item.Expression!;
                child._parent?.DetachChildReference(child);
                child._parent = this;
            }
        }

        private void Detach(SItem item)
        {
            if (item.Kind == SItemKind.Expression && ReferenceEquals(item.Expression!._parent, this))
            {
                item.Expression._parent = null;
            }
        }

        private void DetachChildReference(SExpression child)
        {
            for (var i = 0; i < _items.Length; i++)
            {
                if (_items[i].Kind == SItemKind.Expression && ReferenceEquals(_items[i].Expression, child))
                {
                    RemoveItemAt(i);
                    return;
                }
            }
        }

        private void MarkDirty()
        {
            var node = this;
            while (node is not null && (node._flags & Flag.Dirty) == 0)
            {
                node._flags |= Flag.Dirty;
                node = node._parent;
            }
        }
    }
}
