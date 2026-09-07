using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

using SExpressionSharp;

namespace KiCadSharp.Cli
{
    /// <summary>
    /// Every top-level form read out of one file, plus whether the library was able to give us all of them.
    /// </summary>
    internal sealed class SExpressionDocument
    {
        public SExpressionDocument(IReadOnlyList<SExpression> forms, bool allFormsRead)
        {
            Forms = forms;
            AllFormsRead = allFormsRead;
        }

        /// <summary>The top-level forms, in file order.</summary>
        public IReadOnlyList<SExpression> Forms { get; }

        /// <summary>
        /// False when the library could only return the first top-level form, so anything after it in
        /// the file was not read. See <see cref="SExpressionSource"/>.
        /// </summary>
        public bool AllFormsRead { get; }
    }

    /// <summary>
    /// Reads a file into its top-level forms.
    /// </summary>
    /// <remarks>
    /// A .kicad_dru is a <em>sequence</em> of forms, and <c>SExpressionParser.Parse</c> returns only the
    /// first one — 26 KB in, one 12-byte <c>(version 1)</c> out. <c>ParseAll</c>/<c>ParseAllFile</c> are
    /// being added to SExpressionSharp in a parallel change, so this binds to them by reflection: the CLI
    /// compiles and runs against today's library, and picks the multi-form API up automatically once it
    /// ships, without a second implementation of the parser living here.
    /// Collapse this type into a direct <c>ParseAllFile</c> call once that change has landed.
    /// </remarks>
    internal static class SExpressionSource
    {
        private static readonly MethodInfo? ParseAllFileMethod =
            typeof(SExpressionParser).GetMethod("ParseAllFile", BindingFlags.Public | BindingFlags.Instance, new[] { typeof(string) });

        private static readonly MethodInfo? ParseAllMethod =
            typeof(SExpressionParser).GetMethod("ParseAll", BindingFlags.Public | BindingFlags.Instance, new[] { typeof(string) });

        /// <summary>True when the referenced SExpressionSharp exposes the multi-form API.</summary>
        public static bool SupportsMultipleForms => ParseAllFileMethod is not null || ParseAllMethod is not null;

        public static SExpressionDocument ReadFile(string path)
        {
            var parser = new SExpressionParser();

            if (ParseAllFileMethod is not null)
            {
                return new SExpressionDocument(ToList(ParseAllFileMethod.Invoke(parser, new object[] { path })), allFormsRead: true);
            }

            var text = File.ReadAllText(path);
            return ReadText(text);
        }

        public static SExpressionDocument ReadText(string text)
        {
            var parser = new SExpressionParser();

            if (ParseAllMethod is not null)
            {
                return new SExpressionDocument(ToList(ParseAllMethod.Invoke(parser, new object[] { text })), allFormsRead: true);
            }

            return new SExpressionDocument(new[] { parser.Parse(text) }, allFormsRead: false);
        }

        private static IReadOnlyList<SExpression> ToList(object? parseAllResult)
        {
            if (parseAllResult is IReadOnlyList<SExpression> list)
            {
                return list;
            }

            if (parseAllResult is IEnumerable enumerable)
            {
                var forms = new List<SExpression>();
                foreach (var item in enumerable)
                {
                    if (item is SExpression form)
                    {
                        forms.Add(form);
                    }
                }

                return forms;
            }

            throw new InvalidOperationException(
                $"SExpressionSharp's ParseAll returned {parseAllResult?.GetType().FullName ?? "null"}, which is not a sequence of SExpression.");
        }
    }
}
