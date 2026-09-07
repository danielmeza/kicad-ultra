using System;
using System.Collections.Generic;
using System.Globalization;

using SExpressionSharp;

namespace KiCadSharp.Cli
{
    /// <summary>
    /// Resolves a dotted or slashed path over a parsed document.
    /// </summary>
    /// <remarks>
    /// Segments are separated by '.' or '/', and each segment names a child token, optionally with a
    /// zero-based occurrence index in brackets:
    /// <code>
    ///   kicad_sch.version              first (version ...) under the (kicad_sch ...) form
    ///   kicad_sch/paper                the same, slash-separated
    ///   kicad_sch.symbol[2].property   the third (symbol ...), then its (property ...) children
    /// </code>
    /// Without an index a segment keeps every match, so a path can fan out to several nodes.
    /// The first segment is matched against the document's top-level forms.
    /// </remarks>
    internal static class SExpressionPath
    {
        public static IReadOnlyList<SExpression> Resolve(SExpressionDocument document, string path)
        {
            var segments = path.Split(new[] { '.', '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                throw new FormatException("The path is empty.");
            }

            IReadOnlyList<SExpression> current = document.Forms;

            for (var depth = 0; depth < segments.Length; depth++)
            {
                var (token, index) = ParseSegment(segments[depth]);

                var matches = new List<SExpression>();
                foreach (var node in current)
                {
                    // The first segment selects among the top-level forms themselves; every later
                    // segment selects among the children of what the previous segment matched.
                    var candidates = depth == 0 ? new[] { node } : (IEnumerable<SExpression>)node.Children;
                    foreach (var candidate in candidates)
                    {
                        if (candidate.Token == token)
                        {
                            matches.Add(candidate);
                        }
                    }
                }

                if (index is int wanted)
                {
                    matches = wanted >= 0 && wanted < matches.Count
                        ? new List<SExpression> { matches[wanted] }
                        : new List<SExpression>();
                }

                current = matches;
                if (current.Count == 0)
                {
                    return current;
                }
            }

            return current;
        }

        private static (string Token, int? Index) ParseSegment(string segment)
        {
            var open = segment.IndexOf('[');
            if (open < 0)
            {
                return (segment, null);
            }

            if (!segment.EndsWith("]", StringComparison.Ordinal))
            {
                throw new FormatException($"Path segment '{segment}' opens '[' without a closing ']'.");
            }

            var inner = segment.Substring(open + 1, segment.Length - open - 2);
            if (!int.TryParse(inner, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) || index < 0)
            {
                throw new FormatException($"Path segment '{segment}' has '{inner}' where a zero-based index was expected.");
            }

            return (segment.Substring(0, open), index);
        }
    }
}
