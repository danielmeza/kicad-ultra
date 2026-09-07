using System;
using System.Collections.Generic;

namespace KiCadSharp.Cli
{
    /// <summary>A structural problem found in the raw text, with the position that caused it.</summary>
    internal sealed record ScanProblem(int Line, int Column, string Message)
    {
        public override string ToString() => $"line {Line}, column {Column}: {Message}";
    }

    /// <summary>What a lexical pass over the file found, independent of the parser.</summary>
    internal sealed class ScanResult
    {
        public required int TopLevelForms { get; init; }
        public required int CommentLines { get; init; }
        public required IReadOnlyList<ScanProblem> Problems { get; init; }

        public bool IsWellFormed => Problems.Count == 0;
    }

    /// <summary>
    /// A lexical pass over the raw file: balances parentheses and quoted strings, counts the top-level
    /// forms and the comment lines, and reports the position of anything malformed.
    /// </summary>
    /// <remarks>
    /// This deliberately does not build a tree — that is the parser's job, and <c>validate</c> runs the
    /// real parser too. The scanner exists so that a diagnostic can name a line and column, and so the
    /// top-level form count can be compared against what the parser actually returned.
    /// A <c>#</c> is treated as a line comment only outside a quoted string and only at nesting depth 0,
    /// which is where KiCad puts them in a .kicad_dru; inside a form it is an ordinary token character.
    /// </remarks>
    internal static class SourceScanner
    {
        public static ScanResult Scan(string text)
        {
            var problems = new List<ScanProblem>();
            var forms = 0;
            var commentLines = 0;

            var depth = 0;
            var line = 1;
            var column = 1;
            var inString = false;
            var escaped = false;
            var stringStartLine = 0;
            var stringStartColumn = 0;

            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];

                if (c == '\n')
                {
                    if (inString)
                    {
                        // A newline inside a quoted string is legal in KiCad files, so keep scanning.
                    }

                    line++;
                    column = 1;
                    continue;
                }

                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (c == '\\')
                    {
                        escaped = true;
                    }
                    else if (c == '"')
                    {
                        inString = false;
                    }
                }
                else if (c == '"')
                {
                    inString = true;
                    stringStartLine = line;
                    stringStartColumn = column;
                }
                else if (c == '#' && depth == 0 && IsAtTokenStart(text, i))
                {
                    commentLines++;
                    while (i < text.Length && text[i] != '\n')
                    {
                        i++;
                    }

                    line++;
                    column = 1;
                    continue;
                }
                else if (c == '(')
                {
                    if (depth == 0)
                    {
                        forms++;
                    }

                    depth++;
                }
                else if (c == ')')
                {
                    if (depth == 0)
                    {
                        problems.Add(new ScanProblem(line, column, "unbalanced ')' outside any form"));
                    }
                    else
                    {
                        depth--;
                    }
                }
                else if (depth == 0 && !char.IsWhiteSpace(c))
                {
                    problems.Add(new ScanProblem(line, column, $"unexpected '{c}' outside any form"));
                }

                column++;
            }

            if (inString)
            {
                problems.Add(new ScanProblem(stringStartLine, stringStartColumn, "unterminated quoted string"));
            }

            if (depth > 0)
            {
                problems.Add(new ScanProblem(line, column, $"end of file with {depth} unclosed '('"));
            }

            return new ScanResult { TopLevelForms = forms, CommentLines = commentLines, Problems = problems };
        }

        private static bool IsAtTokenStart(string text, int index) =>
            index == 0 || char.IsWhiteSpace(text[index - 1]);
    }
}
