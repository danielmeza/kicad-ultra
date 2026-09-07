using System;

namespace SExpressionSharp
{
    /// <summary>
    /// Thrown when S-expression text cannot be parsed. Derives from <see cref="FormatException"/>
    /// so callers written against the previous parser still catch it.
    /// </summary>
    public sealed class SExpressionFormatException : FormatException
    {
        /// <summary>Creates the exception without position information.</summary>
        public SExpressionFormatException()
        {
        }

        /// <summary>Creates the exception with a message.</summary>
        /// <param name="message">What went wrong.</param>
        public SExpressionFormatException(string message)
            : base(message)
        {
        }

        /// <summary>Creates the exception with a message and an inner exception.</summary>
        /// <param name="message">What went wrong.</param>
        /// <param name="innerException">The cause.</param>
        public SExpressionFormatException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        internal SExpressionFormatException(string message, string source, int position)
            : base(Describe(message, source, position))
        {
            Position = position;
            (Line, Column) = LineColumn(source, position);
        }

        /// <summary>Character offset in the source where parsing failed.</summary>
        public int Position { get; }

        /// <summary>1-based line where parsing failed.</summary>
        public int Line { get; }

        /// <summary>1-based column where parsing failed.</summary>
        public int Column { get; }

        private static string Describe(string message, string source, int position)
        {
            var (line, column) = LineColumn(source, position);
            return $"{message} at line {line}, column {column} (offset {position}).";
        }

        private static (int Line, int Column) LineColumn(string source, int position)
        {
            var line = 1;
            var lineStart = 0;
            var end = Math.Min(position, source.Length);
            for (var i = 0; i < end; i++)
            {
                if (source[i] == '\n')
                {
                    line++;
                    lineStart = i + 1;
                }
            }

            return (line, end - lineStart + 1);
        }
    }
}
