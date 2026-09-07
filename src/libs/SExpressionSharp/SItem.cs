using System;

namespace SExpressionSharp
{
    /// <summary>
    /// What an <see cref="SItem"/> holds.
    /// </summary>
    public enum SItemKind : byte
    {
        /// <summary>A bare or quoted atom, e.g. <c>1.27</c> or <c>"F.Cu"</c>.</summary>
        Atom = 0,

        /// <summary>A nested form, e.g. <c>(at 0 0 90)</c>.</summary>
        Expression = 1,

        /// <summary>A line comment. KiCad's own writers never emit one, but <c>.kicad_dru</c> files are full of them.</summary>
        Comment = 2,
    }

    /// <summary>
    /// How an atom is written back out.
    /// </summary>
    public enum SQuoteStyle : byte
    {
        /// <summary>Decide from the content: quote only when the atom could not be read back bare.</summary>
        Auto = 0,

        /// <summary>Write bare, unless the content makes that impossible (then it is quoted anyway).</summary>
        Bare = 1,

        /// <summary>Always write quoted. This is what the parser records for an atom that arrived quoted.</summary>
        Quoted = 2,
    }

    /// <summary>
    /// One element of a form, in source order. A form is a token followed by a sequence of these:
    /// keeping them in one list is what makes <c>(a 1 (b) 2)</c> round-trip as written instead of
    /// being re-ordered into values-then-children.
    /// </summary>
    /// <remarks>
    /// Sixteen bytes wide on purpose: one reference plus two packed integers. A 145 KB schematic is
    /// about 25 000 of these, so the width of this struct is the parser's dominant memory cost.
    /// </remarks>
    public readonly struct SItem : IEquatable<SItem>
    {
        private const int KindMask = 0b11;
        private const int QuoteShift = 2;
        private const int QuoteMask = 0b11;
        private const int RawFlag = 1 << 4;
        private const int LengthShift = 5;

        /// <summary>The largest source span an item can record; beyond this the span is simply not tracked.</summary>
        internal const int MaxSourceLength = (1 << 27) - 1;

        private readonly object? _payload;
        private readonly int _start;
        private readonly int _packed;

        /// <summary>Fast path for the parser: the span is known good, so nothing is validated.</summary>
        private SItem(SItemKind kind, object? payload, SQuoteStyle quote, int start, int length)
        {
            _payload = payload;
            _start = start;
            _packed = ((int)kind & KindMask)
                | (((int)quote & QuoteMask) << QuoteShift)
                | RawFlag
                | (length << LengthShift);
        }

        private SItem(SItemKind kind, object? payload, SQuoteStyle quote, int start, int length, bool rawValid)
        {
            _payload = payload;
            if (start < 0 || (uint)length > MaxSourceLength)
            {
                _start = -1;
                length = 0;
                rawValid = false;
            }
            else
            {
                _start = start;
            }

            _packed = ((int)kind & KindMask)
                | (((int)quote & QuoteMask) << QuoteShift)
                | (rawValid ? RawFlag : 0)
                | (length << LengthShift);
        }

        /// <summary>Creates an atom item.</summary>
        /// <param name="text">The decoded atom text (no surrounding quotes, no escapes).</param>
        /// <param name="quote">How to write it back out.</param>
        /// <returns>The item.</returns>
        public static SItem CreateAtom(string text, SQuoteStyle quote = SQuoteStyle.Auto)
        {
            ArgumentNullException.ThrowIfNull(text);
            return new SItem(SItemKind.Atom, text, quote, -1, 0, false);
        }

        /// <summary>Creates an item holding a nested form.</summary>
        /// <param name="expression">The nested form.</param>
        /// <returns>The item.</returns>
        public static SItem CreateExpression(SExpression expression)
        {
            ArgumentNullException.ThrowIfNull(expression);
            return new SItem(SItemKind.Expression, expression, SQuoteStyle.Auto, -1, 0, false);
        }

        /// <summary>Creates a line-comment item.</summary>
        /// <param name="text">The comment text <em>without</em> the leading comment character.</param>
        /// <returns>The item.</returns>
        public static SItem CreateComment(string text)
        {
            ArgumentNullException.ThrowIfNull(text);
            return new SItem(SItemKind.Comment, text, SQuoteStyle.Auto, -1, 0, false);
        }

        internal static SItem ParsedAtom(string text, SQuoteStyle quote, int start, int length) =>
            new(SItemKind.Atom, text, quote, start, length);

        internal static SItem ParsedComment(string text, int start, int length) =>
            new(SItemKind.Comment, text, SQuoteStyle.Auto, start, length);

        internal static SItem ParsedExpression(SExpression expression, int start, int length) =>
            new(SItemKind.Expression, expression, SQuoteStyle.Auto, start, length);

        /// <summary>Keeps this item's slot in the source -- so the writer can still splice the whitespace around it -- but marks its text as replaced.</summary>
        internal SItem InSlotOf(in SItem original) =>
            new(Kind, _payload, QuoteStyle, original._start, original.SourceLength, false);

        /// <summary>Gets what this item holds.</summary>
        public SItemKind Kind => (SItemKind)(_packed & KindMask);

        /// <summary>Gets the atom text or the comment text; <see langword="null"/> for a nested form.</summary>
        public string? Text => _payload as string;

        /// <summary>Gets how an atom is written back out.</summary>
        public SQuoteStyle QuoteStyle => (SQuoteStyle)((_packed >> QuoteShift) & QuoteMask);

        /// <summary>Gets the nested form; <see langword="null"/> unless <see cref="Kind"/> is <see cref="SItemKind.Expression"/>.</summary>
        public SExpression? Expression => _payload as SExpression;

        /// <summary>True when this item came from a parse and still carries its original source span.</summary>
        public bool IsFromSource => _start >= 0;

        internal int SourceStart => _start;

        internal int SourceLength => _packed >>> LengthShift;

        internal bool RawValid => (_packed & RawFlag) != 0;

        /// <inheritdoc />
        public bool Equals(SItem other) =>
            (_packed & (KindMask | (QuoteMask << QuoteShift))) == (other._packed & (KindMask | (QuoteMask << QuoteShift)))
            && (ReferenceEquals(_payload, other._payload)
                || (_payload is string a && other._payload is string b && string.Equals(a, b, StringComparison.Ordinal)));

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is SItem other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() =>
            HashCode.Combine(_packed & (KindMask | (QuoteMask << QuoteShift)), _payload is string s ? s.GetHashCode(StringComparison.Ordinal) : _payload?.GetHashCode() ?? 0);

        /// <summary>Value equality.</summary>
        /// <param name="left">Left operand.</param>
        /// <param name="right">Right operand.</param>
        /// <returns>True when equal.</returns>
        public static bool operator ==(SItem left, SItem right) => left.Equals(right);

        /// <summary>Value inequality.</summary>
        /// <param name="left">Left operand.</param>
        /// <param name="right">Right operand.</param>
        /// <returns>True when different.</returns>
        public static bool operator !=(SItem left, SItem right) => !left.Equals(right);

        /// <inheritdoc />
        public override string ToString() => Kind switch
        {
            SItemKind.Atom => Text ?? string.Empty,
            SItemKind.Comment => "#" + Text,
            _ => Expression?.ToString() ?? "()",
        };
    }
}
