using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace UltraLibrarianImporter.KiCadBindings
{
    /// <summary>
    /// Parser for KiCad S-expression files. Parses text in S-expression format into a tree of SExpression objects.
    /// </summary>
    public class SExpressionParser
    {
        private string _text = "";
        private int _position = 0;

        /// <summary>
        /// Parse a string containing S-expressions and return the root expression
        /// </summary>
        /// <param name="text">The text to parse</param>
        /// <returns>The root S-expression</returns>
        public SExpression Parse(string text)
        {
            _text = text;
            _position = 0;
            
            SkipWhitespace();
            
            if (_position >= _text.Length || _text[_position] != '(')
            {
                throw new FormatException("Expected '(' at the start of S-expression");
            }
            
            return ParseExpression();
        }

        /// <summary>
        /// Parse a file containing S-expressions and return the root expression
        /// </summary>
        /// <param name="filePath">Path to the file to parse</param>
        /// <returns>The root S-expression</returns>
        public SExpression ParseFile(string filePath)
        {
            string text = File.ReadAllText(filePath);
            return Parse(text);
        }

        private SExpression ParseExpression()
        {
            // Skip the opening parenthesis
            _position++;
            
            SkipWhitespace();
            
            // Parse the token
            string token = ParseToken();
            var expression = new SExpression(token);
            
            SkipWhitespace();
            
            // Parse values and child expressions until we hit the closing parenthesis
            while (_position < _text.Length && _text[_position] != ')')
            {
                if (_text[_position] == '(')
                {
                    // Parse a child expression
                    expression.AddChild(ParseExpression());
                }
                else
                {
                    // Parse a value
                    expression.Values.Add(ParseValue());
                }
                
                SkipWhitespace();
            }
            
            // Skip the closing parenthesis
            if (_position < _text.Length && _text[_position] == ')')
            {
                _position++;
            }
            else
            {
                throw new FormatException($"Expected ')' at position {_position}");
            }
            
            return expression;
        }

        private string ParseToken()
        {
            int start = _position;
            
            // A token is a sequence of non-whitespace, non-bracket characters
            while (_position < _text.Length 
                   && _text[_position] != ' ' 
                   && _text[_position] != '\t' 
                   && _text[_position] != '\r' 
                   && _text[_position] != '\n'
                   && _text[_position] != '('
                   && _text[_position] != ')')
            {
                _position++;
            }
            
            return _text.Substring(start, _position - start);
        }

        private string ParseValue()
        {
            SkipWhitespace();
            
            if (_position >= _text.Length)
            {
                throw new FormatException("Unexpected end of input while parsing value");
            }
            
            // Check if the value is a quoted string
            if (_text[_position] == '"')
            {
                return ParseQuotedString();
            }
            
            // Otherwise, it's an unquoted token
            return ParseToken();
        }

        private string ParseQuotedString()
        {
            // Skip the opening quote
            _position++;
            
            var sb = new StringBuilder();
            bool escaped = false;
            
            while (_position < _text.Length)
            {
                char c = _text[_position];
                
                if (escaped)
                {
                    // Handle escaped character
                    sb.Append(c);
                    escaped = false;
                }
                else if (c == '\\')
                {
                    // Start of escape sequence
                    escaped = true;
                }
                else if (c == '"')
                {
                    // End of string
                    _position++;
                    return sb.ToString();
                }
                else
                {
                    // Normal character
                    sb.Append(c);
                }
                
                _position++;
            }
            
            throw new FormatException("Unterminated quoted string");
        }

        private void SkipWhitespace()
        {
            while (_position < _text.Length)
            {
                char c = _text[_position];
                
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n')
                {
                    _position++;
                }
                else
                {
                    break;
                }
            }
        }
    }
}