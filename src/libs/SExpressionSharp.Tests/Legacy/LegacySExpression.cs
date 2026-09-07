// Verbatim copy of SExpressionSharp at commit 0c019af (the implementation this branch
// replaces), renamed and kept only so the benchmark can measure before and after in one process.
#pragma warning disable CS1591, CA1822, IDE0079
using System;
using System.Collections.Generic;
using System.Linq;

namespace SExpressionSharp.Tests.Legacy
{
    /// <summary>
    /// Represents a node in an S-expression tree structure.
    /// S-expressions are used by KiCad to store symbols, footprints, and other data.
    /// </summary>
    public class LegacySExpression
    {
        /// <summary>
        /// Gets the token or name of this S-expression
        /// </summary>
        public string Token { get; }

        /// <summary>
        /// Gets the list of child expressions
        /// </summary>
        public List<LegacySExpression> Children { get; } = new List<LegacySExpression>();

        /// <summary>
        /// Gets the list of string values directly under this expression
        /// </summary>
        public List<string> Values { get; } = new List<string>();

        /// <summary>
        /// Creates a new S-expression with the specified token name
        /// </summary>
        /// <param name="token">The token or name of this expression</param>
        public LegacySExpression(string token)
        {
            Token = token;
        }

        /// <summary>
        /// Creates a new S-expression with the specified token name and values
        /// </summary>
        /// <param name="token">The token or name of this expression</param>
        /// <param name="values">Initial values for this expression</param>
        public LegacySExpression(string token, params string[] values) : this(token)
        {
            Values.AddRange(values);
        }

        /// <summary>
        /// Gets the first child S-expression with the specified token
        /// </summary>
        /// <param name="token">The token to search for</param>
        /// <returns>The first child matching the token, or null if not found</returns>
        public LegacySExpression? GetChild(string token)
        {
            return Children.FirstOrDefault(c => c.Token == token);
        }

        /// <summary>
        /// Gets all child S-expressions with the specified token
        /// </summary>
        /// <param name="token">The token to search for</param>
        /// <returns>An enumerable of matching child expressions</returns>
        public IEnumerable<LegacySExpression> GetChildren(string token)
        {
            return Children.Where(c => c.Token == token);
        }

        /// <summary>
        /// Gets the value at the specified index
        /// </summary>
        /// <param name="index">The index of the value to get</param>
        /// <returns>The value at the specified index, or null if the index is out of range</returns>
        public string? GetValue(int index)
        {
            return index < Values.Count ? Values[index] : null;
        }

        /// <summary>
        /// Gets the first value as a string
        /// </summary>
        /// <returns>The first value, or null if there are no values</returns>
        public string? GetValueAsString()
        {
            return GetValue(0);
        }

        /// <summary>
        /// Gets the value at the specified index as a double
        /// </summary>
        /// <param name="index">The index of the value to get</param>
        /// <returns>The value as a double, or 0 if the value cannot be parsed</returns>
        public double GetValueAsDouble(int index = 0)
        {
            var value = GetValue(index);
            return double.TryParse(value, out double result) ? result : 0;
        }

        /// <summary>
        /// Gets the value at the specified index as an integer
        /// </summary>
        /// <param name="index">The index of the value to get</param>
        /// <returns>The value as an integer, or 0 if the value cannot be parsed</returns>
        public int GetValueAsInt(int index = 0)
        {
            var value = GetValue(index);
            return int.TryParse(value, out int result) ? result : 0;
        }

        /// <summary>
        /// Gets the value at the specified index as a boolean
        /// </summary>
        /// <param name="index">The index of the value to get</param>
        /// <returns>The value as a boolean, or false if the value cannot be parsed</returns>
        public bool GetValueAsBool(int index = 0)
        {
            var value = GetValue(index);
            if (value == null) return false;
            
            return value.ToLowerInvariant() == "yes" || 
                   value.ToLowerInvariant() == "true" || 
                   value == "1";
        }

        /// <summary>
        /// Adds a child S-expression to this expression
        /// </summary>
        /// <param name="child">The child expression to add</param>
        public void AddChild(LegacySExpression child)
        {
            Children.Add(child);
        }

        /// <summary>
        /// Creates and adds a new child S-expression with the specified token and values
        /// </summary>
        /// <param name="token">The token for the new child expression</param>
        /// <param name="values">Values for the new child expression</param>
        /// <returns>The newly created child expression</returns>
        public LegacySExpression CreateChild(string token, params string[] values)
        {
            var child = new LegacySExpression(token, values);
            Children.Add(child);
            return child;
        }

        /// <summary>
        /// Returns a string representation of this S-expression
        /// </summary>
        public override string ToString()
        {
            return $"({Token} {string.Join(" ", Values)})";
        }
    }
}