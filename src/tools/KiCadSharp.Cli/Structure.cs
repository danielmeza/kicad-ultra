using System;
using System.Collections.Generic;

using SExpressionSharp;

namespace KiCadSharp.Cli
{
    /// <summary>Shape statistics for one parsed form.</summary>
    internal sealed record FormStructure(string Token, int Children, int Nodes, int Values, int Depth);

    internal static class Structure
    {
        public static FormStructure Describe(SExpression form)
        {
            var nodes = 0;
            var values = 0;
            var depth = Walk(form, 1, ref nodes, ref values);
            return new FormStructure(form.Token, form.Children.Count, nodes, values, depth);
        }

        private static int Walk(SExpression node, int level, ref int nodes, ref int values)
        {
            nodes++;
            values += node.Values.Count;

            var deepest = level;
            foreach (var child in node.Children)
            {
                var childDepth = Walk(child, level + 1, ref nodes, ref values);
                if (childDepth > deepest)
                {
                    deepest = childDepth;
                }
            }

            return deepest;
        }

        /// <summary>
        /// Structural equality: same token, same values in the same order, same children in the same
        /// order. This is what a lossless parse/write round trip has to preserve.
        /// </summary>
        public static bool SameTree(SExpression left, SExpression right, out string difference)
        {
            return Compare(left, right, left.Token, out difference);
        }

        private static bool Compare(SExpression left, SExpression right, string path, out string difference)
        {
            if (left.Token != right.Token)
            {
                difference = $"{path}: token '{left.Token}' became '{right.Token}'";
                return false;
            }

            if (left.Values.Count != right.Values.Count)
            {
                difference = $"{path}: {left.Values.Count} value(s) became {right.Values.Count}";
                return false;
            }

            for (var i = 0; i < left.Values.Count; i++)
            {
                if (!string.Equals(left.Values[i], right.Values[i], StringComparison.Ordinal))
                {
                    difference = $"{path}: value[{i}] '{left.Values[i]}' became '{right.Values[i]}'";
                    return false;
                }
            }

            if (left.Children.Count != right.Children.Count)
            {
                difference = $"{path}: {left.Children.Count} child form(s) became {right.Children.Count}";
                return false;
            }

            for (var i = 0; i < left.Children.Count; i++)
            {
                if (!Compare(left.Children[i], right.Children[i], $"{path}.{left.Children[i].Token}[{i}]", out difference))
                {
                    return false;
                }
            }

            difference = string.Empty;
            return true;
        }

        /// <summary>The distinct child tokens of a node, with how often each occurs, in first-seen order.</summary>
        public static IReadOnlyList<KeyValuePair<string, int>> ChildTokenCounts(IEnumerable<SExpression> nodes)
        {
            var order = new List<string>();
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var node in nodes)
            {
                if (!counts.TryGetValue(node.Token, out var count))
                {
                    order.Add(node.Token);
                    count = 0;
                }

                counts[node.Token] = count + 1;
            }

            var result = new List<KeyValuePair<string, int>>(order.Count);
            foreach (var token in order)
            {
                result.Add(new KeyValuePair<string, int>(token, counts[token]));
            }

            return result;
        }
    }
}
