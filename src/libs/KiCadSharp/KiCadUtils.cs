using System;
using System.IO;

using KiCadSharp.Documents;

using SExpressionSharp;

namespace KiCadSharp
{
    /// <summary>
    /// Provides utility functions for working with KiCad files and handling format conversions
    /// </summary>
    public static class KiCadUtils
    {
        /// <summary>
        /// Parse a KiCad symbol library file
        /// </summary>
        /// <param name="filePath">Path to the KiCad symbol library file (.kicad_sym)</param>
        /// <returns>The parsed symbol library</returns>
        public static KiCadSymbolLibrary ParseSymbolLibrary(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"KiCad symbol library file not found: {filePath}");
            }

            // Use our S-expression parser to load the file
            var parser = new SExpressionParser();
            var rootExpression = parser.ParseFile(filePath);
            
            // Create a symbol library from the parsed S-expression
            return new KiCadSymbolLibrary(rootExpression);
        }

        /// <summary>
        /// Export a symbol to a new KiCad symbol library file
        /// </summary>
        /// <param name="symbol">Symbol to export</param>
        /// <param name="filePath">Path to the output file</param>
        public static void ExportSymbolToLibrary(KiCadSymbol symbol, string filePath)
        {
            var library = new KiCadSymbolLibrary();
            library.AddSymbol(symbol);
            library.Save(filePath);
        }

        /// <summary>
        /// Validate a KiCad symbol library file
        /// </summary>
        /// <param name="filePath">Path to the KiCad symbol library file</param>
        /// <returns>True if the file is valid, false otherwise</returns>
        public static bool ValidateSymbolLibrary(string filePath)
        {
            try
            {
                var parser = new SExpressionParser();
                var rootExpression = parser.ParseFile(filePath);
                
                // Verify it's a symbol library
                if (rootExpression.Token != "kicad_symbol_lib")
                {
                    return false;
                }
                
                // Verify it has a version
                if (rootExpression.GetChild("version") == null)
                {
                    return false;
                }
                
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Create a copy of a symbol with a new name
        /// </summary>
        /// <param name="originalSymbol">Original symbol to copy</param>
        /// <param name="newId">New ID for the symbol</param>
        /// <returns>A copy of the symbol with the new ID</returns>
        public static KiCadSymbol CloneSymbol(KiCadSymbol originalSymbol, string newId)
        {
            // First convert to S-expression, then parse back to get a deep copy
            var sexp = originalSymbol.ToSExpression();
            var clonedSymbol = new KiCadSymbol(sexp);
            
            // Update the ID and Value property
            clonedSymbol.Id = newId;
            
            // Find and update Value property
            foreach (var property in clonedSymbol.Properties)
            {
                if (property.Key == "Value")
                {
                    property.Value = newId;
                    break;
                }
            }
            
            return clonedSymbol;
        }

        /// <summary>
        /// Extract library name from a KiCad library file path
        /// </summary>
        /// <param name="libraryPath">Path to a KiCad library file</param>
        /// <returns>The library name without path or extension</returns>
        public static string GetLibraryName(string libraryPath)
        {
            return Path.GetFileNameWithoutExtension(libraryPath);
        }
    }
}