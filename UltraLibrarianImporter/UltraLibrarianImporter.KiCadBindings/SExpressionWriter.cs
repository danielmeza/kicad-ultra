using System;
using System.IO;
using System.Text;

namespace UltraLibrarianImporter.KiCadBindings
{
    /// <summary>
    /// Writer for KiCad S-expression files. Converts a tree of SExpression objects to formatted text.
    /// </summary>
    public class SExpressionWriter
    {
        /// <summary>
        /// Convert an S-expression to a string
        /// </summary>
        /// <param name="expression">The expression to convert</param>
        /// <param name="indentLevel">The starting indentation level (0 by default)</param>
        /// <returns>The formatted S-expression string</returns>
        public string Write(SExpression expression, int indentLevel = 0)
        {
            var sb = new StringBuilder();
            WriteToBuilder(expression, sb, indentLevel);
            return sb.ToString();
        }

        /// <summary>
        /// Write an S-expression to a file
        /// </summary>
        /// <param name="expression">The expression to write</param>
        /// <param name="filePath">The path of the file to write to</param>
        public void WriteToFile(SExpression expression, string filePath)
        {
            string content = Write(expression);
            File.WriteAllText(filePath, content);
        }

        private void WriteToBuilder(SExpression expression, StringBuilder sb, int indentLevel)
        {
            string indent = new string(' ', indentLevel * 2);
            
            // Start the expression
            sb.Append(indent);
            sb.Append('(');
            sb.Append(expression.Token);
            
            // Simple case: just a token with values on a single line
            if (expression.Children.Count == 0 && 
                (expression.Values.Count <= 3 || expression.Token == "version" || expression.Token == "generator"))
            {
                // Write values inline
                foreach (var value in expression.Values)
                {
                    sb.Append(' ');
                    WriteValue(value, sb);
                }
                
                sb.Append(')');
                sb.AppendLine();
                return;
            }
            
            // Complex case: children or many values that need formatting
            
            // Write values first
            foreach (var value in expression.Values)
            {
                sb.Append(' ');
                WriteValue(value, sb);
            }
            
            // If we have children, put them on new lines
            if (expression.Children.Count > 0)
            {
                sb.AppendLine();
                
                // Write children with increased indentation
                foreach (var child in expression.Children)
                {
                    WriteToBuilder(child, sb, indentLevel + 1);
                }
                
                // Close on a new line with the same indentation as the opening
                sb.Append(indent);
                sb.Append(')');
                sb.AppendLine();
            }
            else
            {
                // No children, close on the same line
                sb.Append(')');
                sb.AppendLine();
            }
        }

        private static void WriteValue(string value, StringBuilder sb)
        {
            // Check if the value needs to be quoted
            bool needsQuotes = NeedsQuotes(value);
            
            if (needsQuotes)
            {
                sb.Append('"');
                
                // Escape special characters
                foreach (char c in value)
                {
                    if (c == '"' || c == '\\')
                    {
                        sb.Append('\\');
                    }
                    sb.Append(c);
                }
                
                sb.Append('"');
            }
            else
            {
                sb.Append(value);
            }
        }

        private static bool NeedsQuotes(string value)
        {
            // Check if the value contains any characters that would require quoting
            if (string.IsNullOrEmpty(value))
                return true;
            
            // If it starts with a digit, check if it's just a number or has other characters
            if (char.IsDigit(value[0]) || value[0] == '-' || value[0] == '+')
            {
                // Test if it's a valid number format
                if (double.TryParse(value, out _) || int.TryParse(value, out _))
                {
                    return false;
                }
            }
            
            // Check for whitespace or special characters
            foreach (char c in value)
            {
                if (char.IsWhiteSpace(c) || c == '(' || c == ')' || c == '"' || c == '\\')
                {
                    return true;
                }
            }
            
            return false;
        }
    }
}