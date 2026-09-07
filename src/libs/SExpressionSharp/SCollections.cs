using System;
using System.Collections;
using System.Collections.Generic;

namespace SExpressionSharp
{
    /// <summary>
    /// The ordered contents of a form: atoms, nested forms and comments as they appear in the source.
    /// Mutating this list is what every other view ultimately does.
    /// </summary>
    /// <remarks>A view, not storage: it holds one reference and costs nothing to obtain.</remarks>
    public readonly struct SItemList : IList<SItem>, IReadOnlyList<SItem>
    {
        private readonly SExpression _owner;

        internal SItemList(SExpression owner) => _owner = owner;

        /// <summary>
        /// The form this view belongs to. A view is only ever valid when it came from
        /// <see cref="SExpression"/>; a default-constructed one (which a C# collection expression can
        /// produce, because this is a struct with an <c>Add</c> method) has nothing to act on.
        /// </summary>
        private SExpression Owner => _owner ?? throw new InvalidOperationException(
            "This view is not attached to an expression. Get it from SExpression.Values, .Children or .Items instead of constructing one.");


        /// <inheritdoc />
        public int Count => Owner.ItemsArray.Length;

        /// <inheritdoc />
        public bool IsReadOnly => false;

        /// <inheritdoc />
        public SItem this[int index]
        {
            get => Owner.ItemsArray[index];
            set => Owner.ReplaceItem(index, value);
        }

        /// <inheritdoc />
        public void Add(SItem item) => Owner.InsertItem(Owner.ItemsArray.Length, item);

        /// <inheritdoc />
        public void Insert(int index, SItem item) => Owner.InsertItem(index, item);

        /// <inheritdoc />
        public void RemoveAt(int index) => Owner.RemoveItemAt(index);

        /// <inheritdoc />
        public void Clear() => Owner.ClearItems();

        /// <inheritdoc />
        public bool Contains(SItem item) => IndexOf(item) >= 0;

        /// <inheritdoc />
        public int IndexOf(SItem item) => Array.IndexOf(Owner.ItemsArray, item);

        /// <inheritdoc />
        public void CopyTo(SItem[] array, int arrayIndex) => Owner.ItemsArray.CopyTo(array, arrayIndex);

        /// <inheritdoc />
        public bool Remove(SItem item)
        {
            var i = IndexOf(item);
            if (i < 0)
            {
                return false;
            }

            RemoveAt(i);
            return true;
        }

        /// <inheritdoc />
        public IEnumerator<SItem> GetEnumerator() => ((IEnumerable<SItem>)Owner.ItemsArray).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => Owner.ItemsArray.GetEnumerator();

        /// <summary>Gets the items as a span, for allocation-free iteration.</summary>
        /// <returns>The span.</returns>
        public ReadOnlySpan<SItem> AsSpan() => Owner.ItemsArray;
    }

    /// <summary>
    /// A live view of a form's atoms as strings. Source-compatible with the plain
    /// <c>List&lt;string&gt;</c> this used to be, but backed by the ordered item list.
    /// </summary>
    public readonly struct SValueCollection : IList<string>, IReadOnlyList<string>
    {
        private readonly SExpression _owner;

        internal SValueCollection(SExpression owner) => _owner = owner;

        /// <summary>
        /// The form this view belongs to. A view is only ever valid when it came from
        /// <see cref="SExpression"/>; a default-constructed one (which a C# collection expression can
        /// produce, because this is a struct with an <c>Add</c> method) has nothing to act on.
        /// </summary>
        private SExpression Owner => _owner ?? throw new InvalidOperationException(
            "This view is not attached to an expression. Get it from SExpression.Values, .Children or .Items instead of constructing one.");


        /// <inheritdoc />
        public int Count => Owner.CountOf(SItemKind.Atom);

        /// <inheritdoc />
        public bool IsReadOnly => false;

        /// <inheritdoc />
        public string this[int index]
        {
            get => Owner.GetValue(index) ?? throw new ArgumentOutOfRangeException(nameof(index));
            set => Owner.SetValue(index, value);
        }

        /// <inheritdoc />
        public void Add(string item) => Owner.AppendValue(item, SQuoteStyle.Auto);

        /// <summary>Appends an atom with an explicit quoting style.</summary>
        /// <param name="item">The atom text.</param>
        /// <param name="quote">How to write it back out.</param>
        public void Add(string item, SQuoteStyle quote) => Owner.AppendValue(item, quote);

        /// <summary>Appends several atoms.</summary>
        /// <param name="items">The atom texts.</param>
        public void AddRange(IEnumerable<string> items)
        {
            ArgumentNullException.ThrowIfNull(items);
            foreach (var i in items)
            {
                Owner.AppendValue(i, SQuoteStyle.Auto);
            }
        }

        /// <inheritdoc />
        public void Insert(int index, string item)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            var itemIndex = ItemIndexOf(index);
            if (itemIndex < 0)
            {
                Owner.AppendValue(item, SQuoteStyle.Auto);
                return;
            }

            Owner.InsertItem(itemIndex, SItem.CreateAtom(item));
        }

        /// <inheritdoc />
        public void RemoveAt(int index)
        {
            var itemIndex = ItemIndexOf(index);
            if (itemIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            Owner.RemoveItemAt(itemIndex);
        }

        /// <inheritdoc />
        public void Clear()
        {
            var items = Owner.ItemsArray;
            for (var i = items.Length - 1; i >= 0; i--)
            {
                if (Owner.ItemsArray[i].Kind == SItemKind.Atom)
                {
                    Owner.RemoveItemAt(i);
                }
            }
        }

        /// <inheritdoc />
        public int IndexOf(string item)
        {
            var seen = 0;
            foreach (var i in Owner.ItemsArray)
            {
                if (i.Kind != SItemKind.Atom)
                {
                    continue;
                }

                if (string.Equals(i.Text, item, StringComparison.Ordinal))
                {
                    return seen;
                }

                seen++;
            }

            return -1;
        }

        /// <inheritdoc />
        public bool Contains(string item) => IndexOf(item) >= 0;

        /// <inheritdoc />
        public void CopyTo(string[] array, int arrayIndex)
        {
            ArgumentNullException.ThrowIfNull(array);
            foreach (var v in this)
            {
                array[arrayIndex++] = v;
            }
        }

        /// <inheritdoc />
        public bool Remove(string item)
        {
            var i = IndexOf(item);
            if (i < 0)
            {
                return false;
            }

            RemoveAt(i);
            return true;
        }

        /// <inheritdoc />
        public IEnumerator<string> GetEnumerator() => Enumerate(Owner).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private static IEnumerable<string> Enumerate(SExpression owner)
        {
            foreach (var i in owner.ItemsArray)
            {
                if (i.Kind == SItemKind.Atom)
                {
                    yield return i.Text!;
                }
            }
        }

        private int ItemIndexOf(int valueIndex)
        {
            var seen = 0;
            var items = Owner.ItemsArray;
            for (var i = 0; i < items.Length; i++)
            {
                if (items[i].Kind == SItemKind.Atom && seen++ == valueIndex)
                {
                    return i;
                }
            }

            return -1;
        }
    }

    /// <summary>
    /// A live view of a form's nested forms. Source-compatible with the plain
    /// <c>List&lt;SExpression&gt;</c> this used to be.
    /// </summary>
    public readonly struct SChildCollection : IList<SExpression>, IReadOnlyList<SExpression>
    {
        private readonly SExpression _owner;

        internal SChildCollection(SExpression owner) => _owner = owner;

        /// <summary>
        /// The form this view belongs to. A view is only ever valid when it came from
        /// <see cref="SExpression"/>; a default-constructed one (which a C# collection expression can
        /// produce, because this is a struct with an <c>Add</c> method) has nothing to act on.
        /// </summary>
        private SExpression Owner => _owner ?? throw new InvalidOperationException(
            "This view is not attached to an expression. Get it from SExpression.Values, .Children or .Items instead of constructing one.");


        /// <inheritdoc />
        public int Count => Owner.CountOf(SItemKind.Expression);

        /// <inheritdoc />
        public bool IsReadOnly => false;

        /// <inheritdoc />
        public SExpression this[int index]
        {
            get
            {
                var i = ItemIndexOf(index);
                return i >= 0 ? Owner.ItemsArray[i].Expression! : throw new ArgumentOutOfRangeException(nameof(index));
            }

            set
            {
                var i = ItemIndexOf(index);
                if (i < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                Owner.ReplaceItem(i, SItem.CreateExpression(value));
            }
        }

        /// <inheritdoc />
        public void Add(SExpression item) => Owner.AddChild(item);

        /// <summary>Appends several child forms.</summary>
        /// <param name="items">The forms to append.</param>
        public void AddRange(IEnumerable<SExpression> items)
        {
            ArgumentNullException.ThrowIfNull(items);
            foreach (var i in items)
            {
                Owner.AddChild(i);
            }
        }

        /// <inheritdoc />
        public void Insert(int index, SExpression item)
        {
            var i = ItemIndexOf(index);
            Owner.InsertItem(i < 0 ? Owner.ItemsArray.Length : i, SItem.CreateExpression(item));
        }

        /// <inheritdoc />
        public void RemoveAt(int index)
        {
            var i = ItemIndexOf(index);
            if (i < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            Owner.RemoveItemAt(i);
        }

        /// <inheritdoc />
        public void Clear()
        {
            for (var i = Owner.ItemsArray.Length - 1; i >= 0; i--)
            {
                if (Owner.ItemsArray[i].Kind == SItemKind.Expression)
                {
                    Owner.RemoveItemAt(i);
                }
            }
        }

        /// <inheritdoc />
        public int IndexOf(SExpression item)
        {
            var seen = 0;
            foreach (var i in Owner.ItemsArray)
            {
                if (i.Kind != SItemKind.Expression)
                {
                    continue;
                }

                if (ReferenceEquals(i.Expression, item))
                {
                    return seen;
                }

                seen++;
            }

            return -1;
        }

        /// <inheritdoc />
        public bool Contains(SExpression item) => IndexOf(item) >= 0;

        /// <inheritdoc />
        public void CopyTo(SExpression[] array, int arrayIndex)
        {
            ArgumentNullException.ThrowIfNull(array);
            foreach (var v in this)
            {
                array[arrayIndex++] = v;
            }
        }

        /// <inheritdoc />
        public bool Remove(SExpression item)
        {
            var i = IndexOf(item);
            if (i < 0)
            {
                return false;
            }

            RemoveAt(i);
            return true;
        }

        /// <inheritdoc />
        public IEnumerator<SExpression> GetEnumerator() => Enumerate(Owner).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private static IEnumerable<SExpression> Enumerate(SExpression owner)
        {
            foreach (var i in owner.ItemsArray)
            {
                if (i.Kind == SItemKind.Expression)
                {
                    yield return i.Expression!;
                }
            }
        }

        private int ItemIndexOf(int childIndex)
        {
            if (childIndex < 0)
            {
                return -1;
            }

            var seen = 0;
            var items = Owner.ItemsArray;
            for (var i = 0; i < items.Length; i++)
            {
                if (items[i].Kind == SItemKind.Expression && seen++ == childIndex)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
